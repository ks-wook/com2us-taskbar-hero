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
using GameServer.Logging;

namespace GameServer.Services;

/// <summary>
/// 인벤토리/아이템 액션 처리(inventory-item-cube 기획서 §5.1·5.2·5.3·5.4·5.5). 큐브·상자는 범위 밖.
/// 장착: 마스터로 장비/슬롯/클래스/레벨을 검증하고 같은 슬롯 기존 장비를 스왑한다(서버 권위).
/// 해제: 지정 캐릭터-슬롯 장비를 미장착으로 되돌린다. 이동: 인벤토리 칸 배치를 이동/교환한다.
/// 강화: 마스터(enhance_master)로 다음 단계 비용을 확정해 재화를 차감하고 단계를 1 올린다(확정 상승).
/// 상태 변경은 모두 리포지토리 트랜잭션으로 원자적으로 반영한다.
/// </summary>
public sealed class InventoryService : IInventoryService
{
    private readonly IInventoryRepository _inventoryRepository;
    private readonly MasterDbProvider _masterData;
    private readonly ILogger<InventoryService> _logger;
    private readonly IEventLogger _eventLogger;

    /// <summary>의존성(인벤토리 리포지토리·마스터 데이터·운영 로거·이벤트 로거)을 주입받는다.</summary>
    public InventoryService(
        IInventoryRepository inventoryRepository, MasterDbProvider masterData,
        ILogger<InventoryService> logger, IEventLogger eventLogger)
    {
        _inventoryRepository = inventoryRepository;
        _masterData = masterData;
        _logger = logger;
        _eventLogger = eventLogger;
    }

    /// <summary>
    /// 가방 아이템 한 페이지를 조회한다(코어 로드에서 빠진 가변 크기 데이터의 지연 로딩).
    /// slot 커서와 페이지 크기를 <b>그대로 DB 질의로 넘겨</b> 필요한 구간만 읽는다 — 페이지 크기는
    /// 1~<see cref="MaxPageLimit"/>로 클램프한다(0 이하이면 기본값). 페이지 사이의 인벤토리 변경은
    /// 감지하지 않으며, 클라이언트가 itemId 기준으로 병합해 흡수한다(세이브 데이터 기획서 5.2).
    /// </summary>
    public async Task<SaveResult> GetPageAsync(long userId, int cursor, int limit)
    {
        try
        {
            // 클라 버전 차이로 로드가 아예 실패하지 않도록 거부하지 않고 클램프한다(기획서 5.2).
            int effectiveLimit = limit <= 0
                ? Constants.Inventory.DefaultPageLimit
                : Math.Min(limit, Constants.Inventory.MaxPageLimit);

            // 다음 페이지 존재 판정을 위해 한 건 더 읽는다.
            var outcome = await _inventoryRepository.GetPageAsync(userId, cursor, effectiveLimit + 1);
            if (outcome.Status == InventoryPageStatus.NoPlayer)
            {
                return new SaveResult(ErrorCode.SaveNotFound, string.Empty, null);
            }

            var page = outcome.Items.ToList();
            bool hasMore = page.Count > effectiveLimit;
            if (hasMore)
            {
                page.RemoveAt(page.Count - 1);
            }

            var data = new InventoryPageDto
            {
                items = page,
                // 다음 페이지 커서는 이 페이지 마지막 항목의 slot. 빈 페이지면 요청 커서를 그대로 돌려준다.
                nextCursor = page.Count > 0 ? page[^1].slot : cursor,
                hasMore = hasMore,
                total = outcome.Total,
            };

            return new SaveResult(ErrorCode.Success, "Inventory page", data);
        }
        catch (Exception ex)
        {
            _logger.ZLogError(
                ex, $"GetPageAsync 처리 중 예외: errorCode {(int)ErrorCode.ServerError:@ErrorCode}({ErrorCode.ServerError:@ErrorName}), userId {userId:@UserId}");
            return new SaveResult(ErrorCode.ServerError, string.Empty, null);
        }
    }

    /// <summary>
    /// 장착을 처리한다. 마스터 로드를 확인하고, 리포지토리 트랜잭션으로 캐릭터·아이템 검증과 스왑 장착을 수행한다.
    /// 장비/슬롯/클래스/레벨 정합은 ValidateEquip(마스터 조회)으로 판정하며, 결과 상태를 에러 코드로 매핑한다.
    /// 장착한 아이템은 가방 칸을 반납하고, 스왑으로 밀려난 기존 장비는 그 칸으로 되돌아간다.
    /// </summary>
    public async Task<SaveResult> EquipAsync(long userId, int characterId, long itemId)
    {
        try
        {
            if (!_masterData.IsLoaded)
            {
                return new SaveResult(ErrorCode.MasterDataNotLoaded, string.Empty, null);
            }

            var outcome = await _inventoryRepository.ApplyEquipAsync(userId, characterId, itemId, ValidateEquip);

            switch (outcome.Status)
            {
                case EquipStatus.InvalidCharacter:
                    return new SaveResult(ErrorCode.InvalidCharacterId, string.Empty, null);
                case EquipStatus.ItemNotFound:
                    return new SaveResult(ErrorCode.ItemNotFound, string.Empty, null);
                case EquipStatus.ItemEquipped:
                    return new SaveResult(ErrorCode.ItemEquipped, string.Empty, null);
                case EquipStatus.NotEquippable:
                    return new SaveResult(ErrorCode.ItemNotEquippable, string.Empty, null);
                case EquipStatus.InventoryFull:
                    // 스왑된 장비를 되돌릴 칸이 없는 예외 상황(반납할 칸 자체가 없던 경우).
                    return new SaveResult(ErrorCode.InventoryFull, string.Empty, null);
            }

            var data = new EquipResultData
            {
                characterId = characterId,
                equipped = new SlotItemDto { slot = outcome.Slot, itemId = itemId },
                unequipped = outcome.UnequippedItemId is null
                    ? null
                    : new SlotItemDto { slot = outcome.Slot, itemId = outcome.UnequippedItemId.Value },
                unequippedBagSlot = outcome.UnequippedBagSlot ?? -1,
            };

            _logger.ZLogDebug($"장착 성공: userId {userId:@UserId}, characterId {characterId:@CharacterId}, itemId {itemId:@ItemId}, slot {outcome.Slot:@Slot}");
            return new SaveResult(ErrorCode.Success, "Equipped", data);
        }
        catch (Exception ex)
        {
            _logger.ZLogError(
                ex, $"EquipAsync 처리 중 예외: errorCode {(int)ErrorCode.ServerError:@ErrorCode}({ErrorCode.ServerError:@ErrorName}), userId {userId:@UserId}");
            return new SaveResult(ErrorCode.ServerError, string.Empty, null);
        }
    }

    /// <summary>
    /// 장착 해제를 처리한다. 리포지토리 트랜잭션으로 지정 캐릭터-슬롯 장비를 가방 빈 칸에 되돌리고 장착 행을
    /// DELETE한 뒤, 결과를 에러 코드로 매핑한다. 장착 중에는 가방 칸을 쓰지 않으므로 가방이 가득 차 있으면
    /// InventoryFull로 거부한다.
    /// </summary>
    public async Task<SaveResult> UnequipAsync(long userId, int characterId, int slot)
    {
        try
        {
            var outcome = await _inventoryRepository.ApplyUnequipAsync(userId, characterId, slot);

            switch (outcome.Status)
            {
                case UnequipStatus.InvalidCharacter:
                    return new SaveResult(ErrorCode.InvalidCharacterId, string.Empty, null);
                case UnequipStatus.NotEquipped:
                    return new SaveResult(ErrorCode.ItemNotFound, string.Empty, null);
                case UnequipStatus.InventoryFull:
                    return new SaveResult(ErrorCode.InventoryFull, string.Empty, null);
            }

            var data = new UnequipResultData
            {
                characterId = characterId,
                slot = slot,
                itemId = outcome.ItemId,
                bagSlot = outcome.BagSlot,
            };
            _logger.ZLogDebug($"장착 해제 성공: userId {userId:@UserId}, characterId {characterId:@CharacterId}, slot {slot:@Slot}, itemId {outcome.ItemId:@ItemId}, bagSlot {outcome.BagSlot:@BagSlot}");
            return new SaveResult(ErrorCode.Success, "Unequipped", data);
        }
        catch (Exception ex)
        {
            _logger.ZLogError(
                ex, $"UnequipAsync 처리 중 예외: errorCode {(int)ErrorCode.ServerError:@ErrorCode}({ErrorCode.ServerError:@ErrorName}), userId {userId:@UserId}");
            return new SaveResult(ErrorCode.ServerError, string.Empty, null);
        }
    }

    /// <summary>배치 이동/교환을 처리한다. 리포지토리 트랜잭션으로 칸 소유·용량 범위를 검증하고 이동/스왑한 뒤, 결과를 에러 코드로 매핑한다.</summary>
    public async Task<SaveResult> MoveAsync(long userId, long itemId, int toSlot)
    {
        try
        {
            var outcome = await _inventoryRepository.ApplyMoveAsync(userId, itemId, toSlot);

            switch (outcome.Status)
            {
                case MoveStatus.ItemNotFound:
                    return new SaveResult(ErrorCode.ItemNotFound, string.Empty, null);
                case MoveStatus.InvalidSlot:
                    return new SaveResult(ErrorCode.InvalidInventorySlot, string.Empty, null);
            }

            var data = new MoveResultData
            {
                moved = new SlotItemDto { slot = outcome.MovedSlot, itemId = outcome.MovedItemId },
                swapped = outcome.SwappedItemId is null
                    ? null
                    : new SlotItemDto { slot = outcome.SwappedSlot!.Value, itemId = outcome.SwappedItemId.Value },
            };

            _logger.ZLogDebug($"배치 이동 성공: userId {userId:@UserId}, itemId {itemId:@ItemId}, toSlot {toSlot:@ToSlot}");
            return new SaveResult(ErrorCode.Success, "Moved", data);
        }
        catch (Exception ex)
        {
            _logger.ZLogError(
                ex, $"MoveAsync 처리 중 예외: errorCode {(int)ErrorCode.ServerError:@ErrorCode}({ErrorCode.ServerError:@ErrorName}), userId {userId:@UserId}");
            return new SaveResult(ErrorCode.ServerError, string.Empty, null);
        }
    }

    /// <summary>
    /// 장비 강화(단계 +1)를 처리한다. 마스터 로드를 확인하고, 리포지토리 트랜잭션으로 비용 재화를 차감한 뒤
    /// 강화 단계를 1 올린다(장착 중인 장비면 장착 정보의 강화 단계도 함께 갱신된다). 강화 가능 여부와 비용은
    /// PlanEnhance(마스터 조회)가 판정하며, 결과 상태를 에러 코드로 매핑한다. 실패·하락 확률은 없다(확정 상승).
    /// </summary>
    public async Task<SaveResult> EnhanceAsync(long userId, long itemId)
    {
        try
        {
            if (!_masterData.IsLoaded)
            {
                return new SaveResult(ErrorCode.MasterDataNotLoaded, string.Empty, null);
            }

            var outcome = await _inventoryRepository.ApplyEnhanceAsync(userId, itemId, PlanEnhance);

            switch (outcome.Status)
            {
                case EnhanceStatus.ItemNotFound:
                    return new SaveResult(ErrorCode.ItemNotFound, string.Empty, null);
                case EnhanceStatus.NotEquippable:
                    // 장비가 아니면 강화 대상이 아니다(재료·소모품·재화).
                    return new SaveResult(ErrorCode.ItemNotEquippable, string.Empty, null);
                case EnhanceStatus.MaxEnhanceReached:
                    return new SaveResult(ErrorCode.MaxEnhanceReached, string.Empty, null);
                case EnhanceStatus.InsufficientCurrency:
                    // 이 거부만 남긴다 — 어느 단계에서 골드가 막히는지는 강화 곡선을 조정할 자리를 가리킨다.
                    // 나머지(아이템 없음·장비 아님·최대 단계)는 정상 클라이언트라면 시도조차 하지 않는 요청이라
                    // 반복돼도 기획이 아니라 클라이언트를 고쳐야 한다(4.1의 선별 기준).
                    EmitEnhance(userId, itemId, outcome, ErrorCode.InsufficientCurrency);
                    return new SaveResult(ErrorCode.InsufficientCurrency, string.Empty, null);
            }

            var data = new EnhanceResultData
            {
                itemId = itemId,
                enhanceLevel = outcome.EnhanceLevel,
                equipped = outcome.Equipped,
                cost = new CurrencyDto { currencyType = outcome.CurrencyCode, amount = outcome.Cost },
                balance = new List<CurrencyDto>
                {
                    new CurrencyDto { currencyType = outcome.CurrencyCode, amount = outcome.CurrencyBalance },
                },
            };

            _logger.ZLogInformation($"장비 강화 성공: userId {userId:@UserId}, itemId {itemId:@ItemId}, enhanceLevel {outcome.EnhanceLevel:@EnhanceLevel}, cost {outcome.Cost:@Cost}, equipped {outcome.Equipped:@Equipped}");

            // 차감·상승 트랜잭션이 커밋된 뒤에 방출한다(4.2).
            EmitEnhance(userId, itemId, outcome, ErrorCode.Success);

            // 재화 원장(6.1). 강화는 이 경제의 주요 골드 유출원이다.
            if (outcome.Cost > 0)
            {
                _eventLogger.CurrencySpent(
                    userId, outcome.Cost, outcome.CurrencyBalance, CurrencySource.ItemEnhance, itemId);
            }
            return new SaveResult(ErrorCode.Success, "Enhanced", data);
        }
        catch (CurrencyRowMissingException ex)
        {
            // 재화 행이 없다 = 세이브 생성 때 만들어진 행이 코드 밖에서 사라졌다는 뜻이다. 잔액 부족으로 돌려주면
            // 서버 결함이 사용자 실수로 응답되고 기록도 남지 않으므로, 구분된 코드로 올린다(7.2).
            _logger.ZLogError(
                ex, $"재화 행 없음: errorCode {(int)ErrorCode.CurrencyRowMissing:@ErrorCode}({ErrorCode.CurrencyRowMissing:@ErrorName}), userId {userId:@UserId}");
            return new SaveResult(ErrorCode.CurrencyRowMissing, string.Empty, null);
        }
        catch (Exception ex)
        {
            _logger.ZLogError(
                ex, $"EnhanceAsync 처리 중 예외: errorCode {(int)ErrorCode.ServerError:@ErrorCode}({ErrorCode.ServerError:@ErrorName}), userId {userId:@UserId}");
            return new SaveResult(ErrorCode.ServerError, string.Empty, null);
        }
    }

    /// <summary>
    /// 강화 이벤트 1행을 방출한다(5.5). 성공과 거부가 <b>같은 필드에 error_code만 다른</b> 형태라 한 자리에서 낸다 —
    /// 거부 라인의 <c>to_level</c>·<c>cost</c>는 시도한 값이다. 등급은 마스터에서 아이템 코드로 찾는다.
    /// </summary>
    private void EmitEnhance(long userId, long itemId, EnhanceOutcome outcome, ErrorCode errorCode)
    {
        var grade = _masterData.GetItem(outcome.ItemCode)?.Grade ?? 0;
        _eventLogger.Action(
            Constants.EventLog.Tags.ItemEnhance, userId,
            new ItemEnhanceEvent(
                itemId, outcome.ItemCode, grade,
                outcome.FromLevel, outcome.FromLevel + 1, outcome.Cost),
            (int)errorCode);
    }

    /// <summary>
    /// 인벤토리 용량을 1칸 확장한다. 마스터 로드를 확인하고, 리포지토리 트랜잭션으로 확장 비용(마스터 산출)을
    /// 골드에서 차감한 뒤 용량을 1 올린다. 상한 도달·골드 부족 등 결과 상태를 에러 코드로 매핑한다.
    /// </summary>
    public async Task<SaveResult> ExpandAsync(long userId)
    {
        try
        {
            if (!_masterData.IsLoaded)
            {
                return new SaveResult(ErrorCode.MasterDataNotLoaded, string.Empty, null);
            }

            var now = DateTimeUtil.NowUnixSeconds();
            var outcome = await _inventoryRepository.ApplyExpandAsync(userId, _masterData.PlanExpandOne, now);

            switch (outcome.Status)
            {
                case ExpandStatus.NoPlayer:
                    return new SaveResult(ErrorCode.SaveNotFound, string.Empty, null);
                case ExpandStatus.CapacityMax:
                    return new SaveResult(ErrorCode.InventoryCapacityMax, string.Empty, null);
                case ExpandStatus.InsufficientCurrency:
                    return new SaveResult(ErrorCode.InsufficientCurrency, string.Empty, null);
            }

            var data = new ExpandResultData
            {
                inventoryCapacity = outcome.InventoryCapacity,
                cost = new CurrencyDto { currencyType = Constants.Currency.GoldType, amount = outcome.Cost },
                balance = new List<CurrencyDto>
                {
                    new CurrencyDto { currencyType = Constants.Currency.GoldType, amount = outcome.GoldBalance },
                },
            };

            _logger.ZLogInformation($"인벤토리 확장 성공: userId {userId:@UserId}, capacity {outcome.InventoryCapacity:@Capacity}, cost {outcome.Cost:@Cost}");

            // 재화 원장(6.1). 1회 1칸이라 행 수가 곧 누적 확장량이고, ref_id에는 도달한 용량을 담는다.
            if (outcome.Cost > 0)
            {
                _eventLogger.CurrencySpent(
                    userId, outcome.Cost, outcome.GoldBalance,
                    CurrencySource.InventoryExpand, outcome.InventoryCapacity);
            }

            return new SaveResult(ErrorCode.Success, "Expanded", data);
        }
        catch (CurrencyRowMissingException ex)
        {
            // 재화 행이 없다 = 세이브 생성 때 만들어진 행이 코드 밖에서 사라졌다는 뜻이다. 잔액 부족으로 돌려주면
            // 서버 결함이 사용자 실수로 응답되고 기록도 남지 않으므로, 구분된 코드로 올린다(7.2).
            _logger.ZLogError(
                ex, $"재화 행 없음: errorCode {(int)ErrorCode.CurrencyRowMissing:@ErrorCode}({ErrorCode.CurrencyRowMissing:@ErrorName}), userId {userId:@UserId}");
            return new SaveResult(ErrorCode.CurrencyRowMissing, string.Empty, null);
        }
        catch (Exception ex)
        {
            _logger.ZLogError(
                ex, $"ExpandAsync 처리 중 예외: errorCode {(int)ErrorCode.ServerError:@ErrorCode}({ErrorCode.ServerError:@ErrorName}), userId {userId:@UserId}");
            return new SaveResult(ErrorCode.ServerError, string.Empty, null);
        }
    }

    /// <summary>
    /// 강화 가능 여부와 이번 단계의 비용·소모 재화를 마스터로 판정한다(리포지토리 트랜잭션에 델리게이트로 전달).
    /// 장비(item_type=1)여야 하고, 다음 단계(현재 단계 + 1)가 enhance_master에 정의되어 있어야 한다
    /// (없으면 최대 단계 도달). 배율은 클라이언트가 번들에서 읽으므로 여기서는 비용만 산출한다.
    /// </summary>
    private (EnhancePlanStatus status, long cost, int currencyCode) PlanEnhance(int itemCode, int currentLevel)
    {
        var def = _masterData.GetItem(itemCode);
        if (def is null || def.ItemType != Constants.ItemType.Equip)
        {
            return (EnhancePlanStatus.NotEquippable, 0, 0);
        }

        var rule = _masterData.GetEnhance(currentLevel + 1);
        if (rule is null)
        {
            return (EnhancePlanStatus.MaxReached, 0, 0);
        }

        return (EnhancePlanStatus.Ok, rule.Cost, rule.CurrencyCode);
    }

    /// <summary>
    /// 장착 가능 여부와 대상 장착 슬롯을 마스터로 판정한다(리포지토리 트랜잭션에 델리게이트로 전달).
    /// 장비(item_type=1)이며 슬롯이 있어야 하고, 클래스 제한(class_req≠0)·요구 레벨(level_req)을 모두 충족해야 한다.
    /// </summary>
    private (bool ok, int slot) ValidateEquip(int itemCode, int classCode, int level)
    {
        var def = _masterData.GetItem(itemCode);
        if (def is null || def.ItemType != Constants.ItemType.Equip || def.EquipSlot == 0)
        {
            return (false, 0);
        }

        if (def.ClassReq != 0 && def.ClassReq != classCode)
        {
            return (false, 0);
        }

        if (level < def.LevelReq)
        {
            return (false, 0);
        }

        return (true, def.EquipSlot);
    }
}
