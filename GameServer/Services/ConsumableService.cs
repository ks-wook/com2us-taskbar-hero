using GameServer.MasterData;
using GameServer.Repositories.GameDb;
using GameServer.Repositories.GameDb.Interfaces;
using TaskbarHero.Common;
using TaskbarHero.Common.Dto;
using ZLogger;
using GameServer.Repositories.MasterDb;
using GameServer.Models;
using GameServer.Services.Interfaces;
using GameServer.Util;

namespace GameServer.Services;

/// <summary>
/// 소모품(소모성 아이템) 사용·활성 버프 조회 처리(소모품/버프 기획서 §5.1·§5.2·§6.1).
/// 아이템 1개 차감과 계정 획득량 버프 부여·연장을 하나의 트랜잭션으로 반영한다.
/// 배율·지속시간은 전적으로 마스터 데이터(consumable_master)에서 읽으며 클라이언트 입력을 신뢰하지 않는다.
/// </summary>
public sealed class ConsumableService : IConsumableService
{
    private readonly IConsumableRepository _consumableRepository;
    private readonly MasterDbProvider _masterData;
    private readonly ILogger<ConsumableService> _logger;

    /// <summary>리포지토리·마스터 데이터·가방 조회 캐시·로거를 주입받는다.</summary>
    public ConsumableService(
        IConsumableRepository consumableRepository,
        MasterDbProvider masterData,
        ILogger<ConsumableService> logger)
    {
        _consumableRepository = consumableRepository;
        _masterData = masterData;
        _logger = logger;
    }

    /// <summary>
    /// 소모품 1개를 사용해 계정 획득량 버프를 부여(또는 연장)한다.
    /// 마스터 미로드면 검증이 불가하므로 <see cref="ErrorCode.MasterDataNotLoaded"/>로 거부하고,
    /// 그 외 판정(소유·수량·소모품 여부·효과 정의·누적 상한)은 리포지토리 트랜잭션 안에서 수행한다.
    /// 성공 시 차감 후 남은 수량과 갱신된 버프·계정 활성 버프 전체를 응답 데이터로 돌려준다.
    /// </summary>
    public async Task<SaveResult> UseAsync(long userId, long itemId)
    {
        if (!_masterData.IsLoaded)
        {
            return new SaveResult(ErrorCode.MasterDataNotLoaded, string.Empty, null);
        }

        var now = DateTimeUtil.NowUnixSeconds();

        var outcome = await _consumableRepository.ApplyUseAsync(
            userId, itemId,
            itemCode => Decide(itemCode),
            (prev, durationSec) => PlanBuffWindow(prev, durationSec, now),
            now);

        if (outcome.Status != ConsumableUseStatus.Ok)
        {
            return new SaveResult(ToErrorCode(outcome.Status), string.Empty, null);
        }

        // 버프 부여는 재화 지급과 같은 이득 반영이므로 성공 사실을 남긴다(사용자 실수는 로깅하지 않는다 — 로깅 규칙).
        _logger.ZLogInformation(
            $"소모품 사용: user {userId:@UserId} item {outcome.ItemCode:@ItemCode} buffType {outcome.Buff!.BuffType:@BuffType} expiresAt {outcome.Buff.ExpiresAt:@ExpiresAt}");

        var data = new ConsumableUseResultData
        {
            itemId = itemId,
            itemCode = outcome.ItemCode,
            remainingQuantity = outcome.RemainingQuantity,
            buff = ToDto(outcome.Buff!),
            activeBuffs = outcome.ActiveBuffs.Select(ToDto).ToList(),
            inventoryDelta = outcome.Delta,
        };

        return new SaveResult(ErrorCode.Success, "Consumable used", data);
    }

    /// <summary>
    /// 계정에 현재 적용 중인 버프(<c>expires_at &gt; now</c>)를 조회한다(기획서 §5.2).
    /// 버프 UI 재동기화 전용 경량 조회로, 코어 로드의 activeBuffs와 같은 형식을 반환한다.
    /// 마스터 데이터를 참조하지 않으므로(배율·지속시간은 부여 시점에 이미 확정돼 DB에 있다) 마스터 로드 여부를 검증하지 않고,
    /// 만료 판정은 항상 서버 시각으로 하며 그 시각을 serverTime으로 함께 내려 클라이언트가 로컬 시계 없이 잔여 시간을 계산하게 한다.
    /// 활성 버프가 없으면 빈 목록으로 성공 응답한다(오류가 아니다).
    /// </summary>
    public async Task<SaveResult> GetActiveBuffsAsync(long userId)
    {
        var now = DateTimeUtil.NowUnixSeconds();
        var buffs = await _consumableRepository.GetActiveBuffsAsync(userId, now);

        var data = new ActiveBuffListResultData
        {
            serverTime = now,
            activeBuffs = buffs.Select(ToDto).ToList(),
        };

        return new SaveResult(ErrorCode.Success, "Active buffs loaded", data);
    }

    /// <summary>
    /// 사용 대상 아이템이 소모품인지, 그 효과가 마스터에 정의되어 있는지 판정한다(DB 접근 없음, 리포지토리 델리게이트).
    /// 소모품이 아니면 NotConsumable, consumable_master에 정의가 없으면 MasterNotDefined다.
    /// </summary>
    private ConsumableDecision Decide(int itemCode)
    {
        var item = _masterData.GetItem(itemCode);
        if (item is null || item.ItemType != Constants.ItemType.Consumable)
        {
            return ConsumableDecision.Reject(ConsumableUseStatus.NotConsumable);
        }

        var consumable = _masterData.GetConsumable(itemCode);
        if (consumable is null)
        {
            return ConsumableDecision.Reject(ConsumableUseStatus.MasterNotDefined);
        }

        return ConsumableDecision.Accept(consumable.BuffType, consumable.BuffValue, consumable.DurationSec);
    }

    /// <summary>
    /// 버프의 새 유효 구간을 산출한다(기획서 §4.2 중첩 규칙, DB 접근 없음).
    /// 기존 버프가 <b>활성</b>이면 남은 시간에 <b>누적 연장</b>하고 started_at은 유지한다 —
    /// 오프라인 정산이 소급 참조하는 구간 하한이 흔들리면 안 되기 때문이다.
    /// 기존 버프가 없거나 이미 만료됐으면 now부터 새로 시작한다.
    /// 산출된 잔여 시간이 누적 상한(24시간)을 넘으면 overLimit=true로 알려 호출측이 아이템 차감 전에 거부하게 한다.
    /// </summary>
    private static (long startedAt, long expiresAt, bool overLimit) PlanBuffWindow(
        PlayerBuffRow? prev, int durationSec, long now)
    {
        var isActive = prev is not null && prev.ExpiresAt > now;
        var baseAt = isActive ? prev!.ExpiresAt : now;

        var expiresAt = baseAt + durationSec;
        var startedAt = isActive ? prev!.StartedAt : now;

        return (startedAt, expiresAt, expiresAt - now > Constants.Consumable.BuffDurationCapSec);
    }

    /// <summary>리포지토리 사용 상태를 공유 ErrorCode로 변환한다.</summary>
    private static ErrorCode ToErrorCode(ConsumableUseStatus status) => status switch
    {
        ConsumableUseStatus.ItemNotFound => ErrorCode.ItemNotFound,
        ConsumableUseStatus.NotConsumable => ErrorCode.ItemNotConsumable,
        ConsumableUseStatus.InsufficientQuantity => ErrorCode.InsufficientQuantity,
        ConsumableUseStatus.MasterNotDefined => ErrorCode.MasterDataNotLoaded,
        ConsumableUseStatus.DurationLimitExceeded => ErrorCode.BuffDurationLimitExceeded,
        _ => ErrorCode.ServerError,
    };

    /// <summary>버프 레코드를 공유 DTO로 변환한다(코어 로드·사용 응답 공용 형식).</summary>
    private static ActiveBuffDto ToDto(PlayerBuffRow buff) => new()
    {
        buffType = buff.BuffType,
        buffValue = buff.BuffValue,
        startedAt = buff.StartedAt,
        expiresAt = buff.ExpiresAt,
    };
}
