using GameServer.MasterData;
using GameServer.Repositories;
using TaskbarHero.Common;
using TaskbarHero.Common.Dto;
using ZLogger;

namespace GameServer.Services;

public interface IInventoryService
{
    Task<SaveResult> GetPageAsync(long userId, int cursor, int limit);
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

    /// <summary>가방 페이지 크기 기본값(요청이 0 이하일 때).</summary>
    private const int DefaultPageLimit = 200;

    /// <summary>가방 페이지 크기 상한. 한 요청이 인벤토리 전체를 끌어오지 못하게 막는다.</summary>
    private const int MaxPageLimit = 500;

    private readonly IInventoryRepository _inventoryRepository;
    private readonly MasterDataProvider _masterData;
    private readonly InventoryBagCache _bagCache;
    private readonly ILogger<InventoryService> _logger;

    /// <summary>의존성(인벤토리 리포지토리·마스터 데이터·가방 조회 캐시·로거)을 주입받는다.</summary>
    public InventoryService(
        IInventoryRepository inventoryRepository, MasterDataProvider masterData,
        InventoryBagCache bagCache, ILogger<InventoryService> logger)
    {
        _inventoryRepository = inventoryRepository;
        _masterData = masterData;
        _bagCache = bagCache;
        _logger = logger;
    }

    /// <summary>
    /// 가방 아이템 한 페이지를 조회한다(코어 로드에서 빠진 가변 크기 데이터의 지연 로딩).
    /// 가방 캐시(기획서 6.5)에서 전량 스냅샷을 읽고, 미스면 MySQL에서 전량을 읽어 캐시를 채운다.
    /// 그 완전 집합에 slot 커서와 페이지 크기를 적용해 응답을 만든다 — 페이지 크기는 1~<see cref="MaxPageLimit"/>로
    /// 클램프한다(0 이하이면 기본값). 페이지 사이의 인벤토리 변경은 감지하지 않으며, 클라이언트가
    /// itemId 기준으로 병합해 흡수한다(세이브 데이터 기획서 5.2).
    /// </summary>
    public async Task<SaveResult> GetPageAsync(long userId, int cursor, int limit)
    {
        // 클라 버전 차이로 로드가 아예 실패하지 않도록 거부하지 않고 클램프한다(기획서 5.2).
        int effectiveLimit = limit <= 0 ? DefaultPageLimit : Math.Min(limit, MaxPageLimit);

        var bag = await _bagCache.TryGetAsync(userId);
        if (bag is null)
        {
            // 캐시 미스·Redis 장애 → MySQL 폴백. 완전 집합을 읽었으므로 그대로 캐시에 적재한다(6.5).
            var outcome = await _inventoryRepository.GetAllAsync(userId);
            if (outcome.Status == InventoryPageStatus.NoPlayer)
            {
                return new SaveResult(ErrorCode.SaveNotFound, string.Empty, null);
            }

            bag = outcome.Items;
            await _bagCache.FillAsync(userId, bag);
        }

        // 완전 집합에서 요청 구간만 잘라낸다. 한 건 더 떠서 다음 페이지 존재 여부를 판정한다.
        var page = bag.Where(i => i.slot > cursor).Take(effectiveLimit + 1).ToList();
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
            total = bag.Count,
        };

        return new SaveResult(ErrorCode.Success, "Inventory page", data);
    }

    /// <summary>
    /// 장착을 처리한다. 마스터 로드를 확인하고, 리포지토리 트랜잭션으로 캐릭터·아이템 검증과 스왑 장착을 수행한다.
    /// 장비/슬롯/클래스/레벨 정합은 ValidateEquip(마스터 조회)으로 판정하며, 결과 상태를 에러 코드로 매핑한다.
    /// 장착한 아이템은 가방 칸을 반납하고, 스왑으로 밀려난 기존 장비는 그 칸으로 되돌아간다.
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

        await _bagCache.ApplyAsync(userId, outcome.Delta); // 커밋 후 가방 캐시 반영(write-through, 6.5)
        _logger.ZLogDebug($"장착 성공: userId {userId:@UserId}, characterId {characterId:@CharacterId}, itemId {itemId:@ItemId}, slot {outcome.Slot:@Slot}");
        return new SaveResult(ErrorCode.Success, "Equipped", data);
    }

    /// <summary>
    /// 장착 해제를 처리한다. 리포지토리 트랜잭션으로 지정 캐릭터-슬롯 장비를 가방 빈 칸에 되돌리고 장착 행을
    /// DELETE한 뒤, 결과를 에러 코드로 매핑한다. 장착 중에는 가방 칸을 쓰지 않으므로 가방이 가득 차 있으면
    /// InventoryFull로 거부한다.
    /// </summary>
    public async Task<SaveResult> UnequipAsync(long userId, int characterId, int slot)
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
        await _bagCache.ApplyAsync(userId, outcome.Delta); // 커밋 후 가방 캐시 반영(write-through, 6.5)
        _logger.ZLogDebug($"장착 해제 성공: userId {userId:@UserId}, characterId {characterId:@CharacterId}, slot {slot:@Slot}, itemId {outcome.ItemId:@ItemId}, bagSlot {outcome.BagSlot:@BagSlot}");
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

        await _bagCache.ApplyAsync(userId, outcome.Delta); // 커밋 후 가방 캐시 반영(write-through, 6.5)
        _logger.ZLogDebug($"배치 이동 성공: userId {userId:@UserId}, itemId {itemId:@ItemId}, toSlot {toSlot:@ToSlot}");
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

        _logger.ZLogInformation($"인벤토리 확장 성공: userId {userId:@UserId}, capacity {outcome.InventoryCapacity:@Capacity}, cost {outcome.Cost:@Cost}");
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
