using GameServer.MasterData;
using GameServer.Repositories;
using TaskbarHero.Common;
using TaskbarHero.Common.Dto;

namespace GameServer.Services;

public interface IInventoryService
{
    Task<SaveResult> EquipAsync(long userId, int characterId, long itemId);
    Task<SaveResult> UnequipAsync(long userId, int characterId, int slot);
    Task<SaveResult> MoveAsync(long userId, long itemId, int toSlot);
    Task<SaveResult> ExpandAsync(long userId);
}

/// <summary>
/// 인벤토리/아이템 액션 처리(inventory-item-cube 기획서 §5.1·5.2·5.5). 큐브·강화·확장·상자는 범위 밖.
/// 장착: 마스터로 장비/슬롯/클래스/레벨을 검증하고 같은 슬롯 기존 장비를 스왑한다(서버 권위).
/// 해제: 지정 캐릭터-슬롯 장비를 미장착으로 되돌린다. 이동: 인벤토리 칸 배치를 이동/교환한다.
/// 상태 변경은 모두 리포지토리 트랜잭션으로 원자적으로 반영한다.
/// </summary>
public sealed class InventoryService : IInventoryService
{
    private const int ItemTypeEquip = 1;
    private const int GoldCurrencyType = 1;

    private readonly IInventoryRepository _inventoryRepository;
    private readonly MasterDataProvider _masterData;
    private readonly ILogger<InventoryService> _logger;

    /// <summary>의존성(인벤토리 리포지토리·마스터 데이터·로거)을 주입받는다.</summary>
    public InventoryService(IInventoryRepository inventoryRepository, MasterDataProvider masterData, ILogger<InventoryService> logger)
    {
        _inventoryRepository = inventoryRepository;
        _masterData = masterData;
        _logger = logger;
    }

    /// <summary>
    /// 장착을 처리한다. 마스터 로드를 확인하고, 리포지토리 트랜잭션으로 캐릭터·아이템 검증과 스왑 장착을 수행한다.
    /// 장비/슬롯/클래스/레벨 정합은 ValidateEquip(마스터 조회)으로 판정하며, 결과 상태를 에러 코드로 매핑한다.
    /// </summary>
    public async Task<SaveResult> EquipAsync(long userId, int characterId, long itemId)
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
        }

        var data = new EquipResultData
        {
            characterId = characterId,
            equipped = new SlotItemDto { slot = outcome.Slot, itemId = itemId },
            unequipped = outcome.UnequippedItemId is null
                ? null
                : new SlotItemDto { slot = outcome.Slot, itemId = outcome.UnequippedItemId.Value },
        };

        _logger.LogDebug(
            "장착 성공: userId {UserId}, characterId {CharacterId}, itemId {ItemId}, slot {Slot}",
            userId, characterId, itemId, outcome.Slot);
        return new SaveResult(ErrorCode.Success, "Equipped", data);
    }

    /// <summary>장착 해제를 처리한다. 리포지토리 트랜잭션으로 지정 캐릭터-슬롯 장비를 DELETE하고, 결과를 에러 코드로 매핑한다.</summary>
    public async Task<SaveResult> UnequipAsync(long userId, int characterId, int slot)
    {
        var outcome = await _inventoryRepository.ApplyUnequipAsync(userId, characterId, slot);

        switch (outcome.Status)
        {
            case UnequipStatus.InvalidCharacter:
                return new SaveResult(ErrorCode.InvalidCharacterId, string.Empty, null);
            case UnequipStatus.NotEquipped:
                return new SaveResult(ErrorCode.ItemNotFound, string.Empty, null);
        }

        var data = new UnequipResultData { characterId = characterId, slot = slot, itemId = outcome.ItemId };
        _logger.LogDebug(
            "장착 해제 성공: userId {UserId}, characterId {CharacterId}, slot {Slot}, itemId {ItemId}",
            userId, characterId, slot, outcome.ItemId);
        return new SaveResult(ErrorCode.Success, "Unequipped", data);
    }

    /// <summary>배치 이동/교환을 처리한다. 리포지토리 트랜잭션으로 칸 소유·용량 범위를 검증하고 이동/스왑한 뒤, 결과를 에러 코드로 매핑한다.</summary>
    public async Task<SaveResult> MoveAsync(long userId, long itemId, int toSlot)
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

        _logger.LogDebug(
            "배치 이동 성공: userId {UserId}, itemId {ItemId}, toSlot {ToSlot}",
            userId, itemId, toSlot);
        return new SaveResult(ErrorCode.Success, "Moved", data);
    }

    /// <summary>
    /// 인벤토리 용량을 1칸 확장한다. 마스터 로드를 확인하고, 리포지토리 트랜잭션으로 확장 비용(마스터 산출)을
    /// 골드에서 차감한 뒤 용량을 1 올린다. 상한 도달·골드 부족 등 결과 상태를 에러 코드로 매핑한다.
    /// </summary>
    public async Task<SaveResult> ExpandAsync(long userId)
    {
        if (!_masterData.IsLoaded)
        {
            return new SaveResult(ErrorCode.MasterDataNotLoaded, string.Empty, null);
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
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
            cost = new CurrencyDto { currencyType = GoldCurrencyType, amount = outcome.Cost },
            balance = new List<CurrencyDto>
            {
                new CurrencyDto { currencyType = GoldCurrencyType, amount = outcome.GoldBalance },
            },
        };

        _logger.LogInformation(
            "인벤토리 확장 성공: userId {UserId}, capacity {Capacity}, cost {Cost}",
            userId, outcome.InventoryCapacity, outcome.Cost);
        return new SaveResult(ErrorCode.Success, "Expanded", data);
    }

    /// <summary>
    /// 장착 가능 여부와 대상 장착 슬롯을 마스터로 판정한다(리포지토리 트랜잭션에 델리게이트로 전달).
    /// 장비(item_type=1)이며 슬롯이 있어야 하고, 클래스 제한(class_req≠0)·요구 레벨(level_req)을 모두 충족해야 한다.
    /// </summary>
    private (bool ok, int slot) ValidateEquip(int itemCode, int classCode, int level)
    {
        var def = _masterData.GetItem(itemCode);
        if (def is null || def.ItemType != ItemTypeEquip || def.EquipSlot == 0)
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
