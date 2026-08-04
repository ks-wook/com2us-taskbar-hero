using GameServer.MasterData;
using GameServer.Repositories;
using TaskbarHero.Common;
using TaskbarHero.Common.Dto;
using ZLogger;

namespace GameServer.Services;

public interface IMailService
{
    Task<SaveResult> ListAsync(long userId);
    Task<SaveResult> ClaimAsync(long userId, long mailId);
    Task<SaveResult> ClaimAllAsync(long userId);
}

/// <summary>
/// 메일(우편함) 처리(mail 기획서 §5·§6). 첨부 종류·수량은 발급 시점에 확정된 메일 원장(player_mail_reward)이 기준이며
/// (서버 권위, 클라이언트 입력 없음), 지급 + 수령 플래그 갱신은 리포지토리 트랜잭션으로 원자적으로 반영한다.
/// </summary>
public sealed class MailService : IMailService
{
    private const int GoldCurrencyType = 1;

    private readonly IMailRepository _mailRepository;
    private readonly MasterDataProvider _masterData;
    private readonly ILogger<MailService> _logger;

    /// <summary>의존성(메일 리포지토리·마스터 데이터·가방 조회 캐시·로거)을 주입받는다.</summary>
    public MailService(
        IMailRepository mailRepository, MasterDataProvider masterData,
        ILogger<MailService> logger)
    {
        _mailRepository = mailRepository;
        _masterData = masterData;
        _logger = logger;
    }

    /// <summary>
    /// 우편함 목록을 반환한다(5.1). 조회와 함께 미열람 메일을 읽음 처리하되(조회 = 열람, §8 확정),
    /// 응답의 isRead는 조회 시점 값이라 클라이언트가 신규 메일 표시에 쓸 수 있다. 조회는 첨부를 지급하지 않는다.
    /// </summary>
    public async Task<SaveResult> ListAsync(long userId)
    {
        var mails = await _mailRepository.GetMailboxAndMarkReadAsync(userId);

        var data = new MailListResultData
        {
            mails = mails.Select(m => new MailDto
            {
                mailId = m.MailId,
                category = m.Category,
                title = m.Title,
                body = m.Body,
                attachments = m.Attachments.Select(a => new MailAttachmentDto
                {
                    rewardType = a.RewardType,
                    rewardCode = a.RewardCode,
                    quantity = a.Quantity,
                }).ToList(),
                isRead = m.IsRead,
                claimed = m.Claimed,
                createdAt = m.CreatedAt,
                expiresAt = m.ExpiresAt,
            }).ToList(),
        };

        return new SaveResult(ErrorCode.Success, "OK", data);
    }

    /// <summary>
    /// 메일 단건 수령을 처리한다(5.2·6.1). 마스터 로드를 확인하고(첨부 아이템 적재 규칙 조회에 필요),
    /// 리포지토리 트랜잭션 안에서 소유·미수령·미만료 검증 → 조건부 갱신 선점 → 첨부 지급을 수행한다.
    /// 성공 시 지급 내역(gained)과 재화 잔액(balance)을 반환한다.
    /// </summary>
    public async Task<SaveResult> ClaimAsync(long userId, long mailId)
    {
        if (!_masterData.IsLoaded)
        {
            return new SaveResult(ErrorCode.MasterDataNotLoaded, string.Empty, null);
        }

        if (mailId <= 0)
        {
            return new SaveResult(ErrorCode.MailNotFound, string.Empty, null);
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var outcome = await _mailRepository.ApplyClaimAsync(userId, mailId, LookupItemStacking, now);

        if (outcome.Status != MailClaimStatus.Ok)
        {
            return new SaveResult(ToErrorCode(outcome.Status), string.Empty, null);
        }

        var data = new MailClaimResultData
        {
            mailId = mailId,
            gained = BuildGained(outcome.Gold, outcome.Items),
            balance = BuildBalance(outcome.GoldBalance),
            inventoryDelta = outcome.Delta,
        };

        _logger.ZLogInformation($"메일 수령: userId {userId:@UserId}, mailId {mailId:@MailId}, gold {outcome.Gold:@Gold}, items {outcome.Items.Count:@ItemKinds}종");
        return new SaveResult(ErrorCode.Success, "Claimed", data);
    }

    /// <summary>
    /// 일괄 수령을 처리한다(5.3·6.2). 미수령·미만료 메일 전건을 하나의 트랜잭션으로 수령하고 첨부 합계를 반환한다.
    /// 아이템 적재가 인벤토리 용량을 넘으면 전체 롤백 후 InventoryFull(§8 확정 — 부분 수령 없음).
    /// 수령 대상이 없으면 빈 목록으로 성공한다.
    /// </summary>
    public async Task<SaveResult> ClaimAllAsync(long userId)
    {
        if (!_masterData.IsLoaded)
        {
            return new SaveResult(ErrorCode.MasterDataNotLoaded, string.Empty, null);
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var outcome = await _mailRepository.ApplyClaimAllAsync(userId, LookupItemStacking, now);

        if (outcome.Status != MailClaimStatus.Ok)
        {
            return new SaveResult(ToErrorCode(outcome.Status), string.Empty, null);
        }

        var data = new MailClaimAllResultData
        {
            claimedMailIds = outcome.ClaimedMailIds.ToList(),
            gained = BuildGained(outcome.Gold, outcome.Items),
            balance = BuildBalance(outcome.GoldBalance),
            inventoryDelta = outcome.Delta,
        };

        _logger.ZLogInformation($"메일 일괄 수령: userId {userId:@UserId}, mails {outcome.ClaimedMailIds.Count:@MailCount}건, gold {outcome.Gold:@Gold}, items {outcome.Items.Count:@ItemKinds}종");
        return new SaveResult(ErrorCode.Success, "Claimed all", data);
    }

    /// <summary>
    /// 첨부 아이템 적재 규칙 조회(리포지토리 델리게이트): item_code로 마스터에서 (itemType, stackMax)를 찾는다.
    /// 마스터에 없는 코드(발급 데이터 결함)는 비스택 장비(1, 1)로 안전하게 적재한다.
    /// </summary>
    private (int itemType, int stackMax) LookupItemStacking(int itemCode)
    {
        var def = _masterData.GetItem(itemCode);
        return def is null ? (1, 1) : (def.ItemType, def.StackMax);
    }

    /// <summary>리포지토리 수령 상태를 공유 ErrorCode로 변환한다.</summary>
    private static ErrorCode ToErrorCode(MailClaimStatus status) => status switch
    {
        MailClaimStatus.MailNotFound => ErrorCode.MailNotFound,
        MailClaimStatus.MailAlreadyClaimed => ErrorCode.MailAlreadyClaimed,
        MailClaimStatus.MailExpired => ErrorCode.MailExpired,
        MailClaimStatus.InventoryFull => ErrorCode.InventoryFull,
        _ => ErrorCode.ServerError,
    };

    /// <summary>지급 내역 DTO를 만든다. 골드가 0이면 currencies는 빈 목록(첨부 없는 메일 수령 등).</summary>
    private static MailGainedDto BuildGained(long gold, IReadOnlyList<MailAttachment> items)
        => new MailGainedDto
        {
            currencies = gold > 0
                ? new List<CurrencyDto> { new CurrencyDto { currencyType = GoldCurrencyType, amount = gold } }
                : new List<CurrencyDto>(),
            items = items.Select(i => new ItemQuantityDto { itemCode = i.RewardCode, quantity = i.Quantity }).ToList(),
        };

    /// <summary>지급 후 재화 잔액 DTO를 만든다(현재는 골드 1종).</summary>
    private static List<CurrencyDto> BuildBalance(long goldBalance)
        => new List<CurrencyDto> { new CurrencyDto { currencyType = GoldCurrencyType, amount = goldBalance } };
}
