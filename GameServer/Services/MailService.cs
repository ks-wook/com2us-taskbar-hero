using GameServer.MasterData;
using GameServer.Repositories.GameDb;
using GameServer.Repositories.GameDb.Interfaces;
using TaskbarHero.Common;
using TaskbarHero.Common.Dto;
using ZLogger;
using GameServer.Repositories.MasterDb;
using GameServer.Models;
using GameServer.Services.Interfaces;
using GameServer.Logging;
using GameServer.Util;

namespace GameServer.Services;

/// <summary>
/// 메일(우편함) 처리(mail 기획서 §5·§6). 첨부 종류·수량은 발급 시점에 확정된 메일 원장(player_mail_reward)이 기준이며
/// (서버 권위, 클라이언트 입력 없음), 지급 + 수령 플래그 갱신은 리포지토리 트랜잭션으로 원자적으로 반영한다.
/// </summary>
public sealed class MailService : IMailService
{
    private readonly IMailRepository _mailRepository;
    private readonly MasterDbProvider _masterData;
    private readonly ILogger<MailService> _logger;
    private readonly IEventLogger _eventLogger;

    /// <summary>의존성(메일 리포지토리·마스터 데이터·가방 조회 캐시·로거)을 주입받는다.</summary>
    public MailService(
        IMailRepository mailRepository, MasterDbProvider masterData,
        ILogger<MailService> logger, IEventLogger eventLogger)
    {
        _mailRepository = mailRepository;
        _masterData = masterData;
        _logger = logger;
        _eventLogger = eventLogger;
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

        var now = DateTimeUtil.NowUnixSeconds();
        var outcome = await _mailRepository.ApplyClaimAsync(userId, mailId, now);

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

        // 재화 원장(6.1). 출석·거래 대금·순위 보상·신규 지원금이 전부 이 자리로 들어온다 —
        // 우편함에 부채로 떠 있던 재화가 실제로 경제에 풀리는 순간이다.
        if (outcome.Gold > 0)
        {
            _eventLogger.CurrencyGained(
                userId, outcome.Gold, outcome.GoldBalance, CurrencySource.MailClaim, mailId);
        }

        // 아이템 원장(6.2). 거래 구매·반송·출석 보상·신규 지원금이 실제로 가방에 들어오는 지점이 여기다 —
        // 발급(mail.issue)은 건수만 담으므로 품목별 유통량은 이 행들이 유일한 근거다.
        EmitClaimedItems(
            userId, mailId, outcome.Category, outcome.Items, new GrantedItemIds(outcome.Delta));

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

        var now = DateTimeUtil.NowUnixSeconds();
        var outcome = await _mailRepository.ApplyClaimAllAsync(userId, now);

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

        // 두 원장 모두 지급은 합계 한 번이지만 **메일 1건당** 남긴다(ref_id = mail_id, 6.1·6.2).
        // 재화 잔액은 지급 전 잔액에서 메일 순서대로 누적해 채운다. 한 트랜잭션 안의 값이라 중간 잔액이
        // DB에 실재하지는 않지만, 합이 정확하고 마지막 행이 실제 잔액과 일치한다.
        // 아이템 개체 id는 지급 순서대로 하나의 목록에서 꺼내므로 메일을 넘나들어도 중복되지 않는다.
        var runningBalance = outcome.GoldBalance - outcome.Gold;
        var granted = new GrantedItemIds(outcome.Delta);
        foreach (var claimed in outcome.ClaimedMails)
        {
            var gold = claimed.Attachments
                .Where(a => a.RewardType == Constants.RewardType.Gold)
                .Sum(a => a.Quantity);
            if (gold > 0)
            {
                runningBalance += gold;
                _eventLogger.CurrencyGained(
                    userId, gold, runningBalance, CurrencySource.MailClaim, claimed.MailId);
            }

            EmitClaimedItems(userId, claimed.MailId, claimed.Category, claimed.Attachments, granted);
        }

        return new SaveResult(ErrorCode.Success, "Claimed all", data);
    }

    /// <summary>
    /// 메일 1건의 첨부 아이템을 아이템 원장에 남긴다(<c>item.flow</c>, 6.2). 골드 첨부는 재화 원장의 몫이라 건너뛴다.
    /// <para>유입 사유는 메일 분류가 가른다 — 운영 메일(= 신규 가입 지원금)은 <c>newbie_grant</c>,
    /// 나머지(거래 구매·만료 반송·출석·순위 보상)는 <c>mail_claim</c>이다.</para>
    /// </summary>
    /// <param name="granted">장비 개체 id를 꺼낼 목록. 일괄 수령은 메일 여러 건이 하나를 공유한다.</param>
    private void EmitClaimedItems(
        long userId, long mailId, int category, IReadOnlyList<MailAttachment> attachments, GrantedItemIds granted)
    {
        var reason = category == Constants.MailCategory.Operation
            ? ItemFlowReason.NewbieGrant
            : ItemFlowReason.MailClaim;

        foreach (var attachment in attachments)
        {
            if (attachment.RewardType == Constants.RewardType.Gold)
            {
                continue;
            }

            _eventLogger.ItemGained(
                userId, attachment.RewardCode, _masterData.GetItem(attachment.RewardCode), attachment.Quantity,
                granted, reason, mailId);
        }
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
                ? new List<CurrencyDto> { new CurrencyDto { currencyType = Constants.Currency.GoldType, amount = gold } }
                : new List<CurrencyDto>(),
            items = items.Select(i => new ItemQuantityDto { itemCode = i.RewardCode, quantity = i.Quantity }).ToList(),
        };

    /// <summary>지급 후 재화 잔액 DTO를 만든다(현재는 골드 1종).</summary>
    private static List<CurrencyDto> BuildBalance(long goldBalance)
        => new List<CurrencyDto> { new CurrencyDto { currencyType = Constants.Currency.GoldType, amount = goldBalance } };
}
