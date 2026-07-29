using GameServer.Data;
using SqlKata.Execution;
using TaskbarHero.Common.Dto;

namespace GameServer.Repositories;

/// <summary>장착 트랜잭션 결과 상태.</summary>
public enum EquipStatus
{
    Ok,
    InvalidCharacter, // player_character 슬롯 없음
    ItemNotFound,     // 인벤토리에 해당 아이템 없음
    ItemEquipped,     // 이미 어딘가에 장착 중
    NotEquippable,    // 장비 아님·슬롯/클래스/레벨 부적합
}

/// <summary>장착 트랜잭션 결과. Slot=장착 슬롯, UnequippedItemId=스왑으로 밀려난 기존 장비(없으면 null).</summary>
public sealed record EquipOutcome(EquipStatus Status, int Slot, long? UnequippedItemId)
{
    public static EquipOutcome Fail(EquipStatus status) => new(status, 0, null);
}

/// <summary>장착 해제 트랜잭션 결과 상태.</summary>
public enum UnequipStatus
{
    Ok,
    InvalidCharacter, // player_character 슬롯 없음
    NotEquipped,      // 해당 캐릭터-슬롯에 장착된 장비 없음
}

/// <summary>장착 해제 트랜잭션 결과.</summary>
public sealed record UnequipOutcome(UnequipStatus Status, long ItemId)
{
    public static UnequipOutcome Fail(UnequipStatus status) => new(status, 0);
}

/// <summary>배치 이동 트랜잭션 결과 상태.</summary>
public enum MoveStatus
{
    Ok,
    ItemNotFound, // 대상 아이템이 인벤토리에 없음(또는 재화 행)
    InvalidSlot,  // toSlot이 용량 범위 밖
}

/// <summary>배치 이동 트랜잭션 결과. Swapped*는 목표 칸에 있던 아이템(비어 있었으면 null).</summary>
public sealed record MoveOutcome(MoveStatus Status, long MovedItemId, int MovedSlot, long? SwappedItemId, int? SwappedSlot)
{
    public static MoveOutcome Fail(MoveStatus status) => new(status, 0, 0, null, null);
}

/// <summary>인벤토리 용량 확장 트랜잭션 결과 상태.</summary>
public enum ExpandStatus
{
    Ok,
    NoPlayer,             // game_player 없음(세이브 미생성)
    CapacityMax,          // 이미 상한이라 더 확장 불가
    InsufficientCurrency, // 골드 부족
}

/// <summary>인벤토리 확장 트랜잭션 결과. Cost=차감 골드, GoldBalance=차감 후 잔액.</summary>
public sealed record ExpandOutcome(ExpandStatus Status, int InventoryCapacity, long Cost, long GoldBalance)
{
    public static ExpandOutcome Fail(ExpandStatus status) => new(status, 0, 0, 0);
}

/// <summary>인벤토리 페이지 조회 결과 상태.</summary>
public enum InventoryPageStatus
{
    Ok,
    NoPlayer,        // game_player 없음(세이브 미생성)
    RevisionChanged, // 페이징 도중 인벤토리가 변경됨
}

/// <summary>
/// 인벤토리 페이지 조회 결과. Items는 slot 오름차순 가방 아이템, Revision은 읽은 시점의 변경 카운터.
/// RevisionChanged일 때도 클라이언트가 재조회 기준을 잡도록 현재 Revision을 함께 담는다.
/// </summary>
public sealed record InventoryPageOutcome(
    InventoryPageStatus Status, IReadOnlyList<InventoryItemDto> Items, bool HasMore, int Total, long Revision)
{
    public static InventoryPageOutcome Fail(InventoryPageStatus status, long revision) =>
        new(status, Array.Empty<InventoryItemDto>(), false, 0, revision);
}

public interface IInventoryRepository
{
    /// <summary>
    /// 가방 아이템 한 페이지를 slot 커서 keyset 페이징으로 조회한다. expectedRevision이 0보다 크고 현재
    /// inventory_revision과 다르면 RevisionChanged로 거부한다.
    /// </summary>
    Task<InventoryPageOutcome> GetPageAsync(long userId, int cursor, int limit, long expectedRevision);

    /// <summary>
    /// 장착을 한 트랜잭션으로 적용한다: 캐릭터·아이템 존재/미장착 확인 → validate(마스터 검증)로 장착 가능 여부·대상 슬롯 판정
    /// → 같은 슬롯 기존 장비 해제(스왑) → 장착 행 INSERT. validate는 (itemCode, classCode, level)→(ok, slot).
    /// </summary>
    Task<EquipOutcome> ApplyEquipAsync(
        long userId, int characterId, long itemId,
        Func<int, int, int, (bool ok, int slot)> validate);

    /// <summary>지정 캐릭터-장착 슬롯의 장비를 해제(장착 행 DELETE)한다.</summary>
    Task<UnequipOutcome> ApplyUnequipAsync(long userId, int characterId, int slot);

    /// <summary>아이템을 목표 칸으로 이동한다. 목표 칸이 차 있으면 두 칸을 교환(swap)하며, 한 트랜잭션으로 처리한다.</summary>
    Task<MoveOutcome> ApplyMoveAsync(long userId, long itemId, int toSlot);

    /// <summary>
    /// 인벤토리 용량을 1칸 확장한다: 현재 용량으로 planOne(비용·가능 여부)을 산출 → 골드 확인·차감 → inventory_capacity += 1.
    /// planOne은 (currentCapacity)→(ok, cost). 한 트랜잭션으로 처리하며 실패 시 전체 롤백한다.
    /// </summary>
    Task<ExpandOutcome> ApplyExpandAsync(long userId, Func<int, (bool ok, long cost)> planOne, long nowUnix);
}

// ── DB 행 매핑용 POCO(제네릭 매핑 전용, dynamic 금지). snake_case→PascalCase는 Dapper 규칙으로 매핑. ──
file sealed class CharClassLevelRow
{
    public int ClassCode { get; set; }
    public int Level { get; set; }
}

file sealed class ItemCodeEnhanceRow
{
    public int ItemCode { get; set; }
    public int EnhanceLevel { get; set; }
}

file sealed class ItemRowTypeSlotRow
{
    public int RowType { get; set; }
    public int? Slot { get; set; }
}

file sealed class ItemIdQtyRow
{
    public long PlayerItemId { get; set; }
    public long Quantity { get; set; }
}

file sealed class BagItemRow
{
    public long PlayerItemId { get; set; }
    public int Slot { get; set; }
    public int ItemCode { get; set; }
    public long Quantity { get; set; }
    public int EnhanceLevel { get; set; }
}

/// <summary>인벤토리/아이템 액션 세이브 접근 계층(taskbar_hero_game). SqlKata 쿼리 빌더 + 제네릭 매핑만 사용한다(dynamic 금지).</summary>
public sealed class InventoryRepository : IInventoryRepository
{
    private const int RowTypeItem = 1;
    private const int RowTypeCurrency = 2;
    private const int GoldItemCode = 1;

    private readonly GameDbFactory _dbFactory;

    /// <summary>세이브 DB 커넥션 팩토리를 주입받는다.</summary>
    public InventoryRepository(GameDbFactory dbFactory) => _dbFactory = dbFactory;

    /// <summary>
    /// 가방 아이템 한 페이지를 조회한다. 정합성 기준값(expectedRevision)을 먼저 대조하고, 통과하면
    /// slot 커서 기준으로 limit+1건을 읽어 다음 페이지 존재 여부(HasMore)를 판정한 뒤 limit건만 돌려준다.
    /// 읽기 전용이지만 revision·목록·총계를 한 시점으로 묶어야 하므로 단일 커넥션의 스냅샷 트랜잭션에서 읽는다.
    /// </summary>
    /// <remarks>
    /// 한 트랜잭션(REPEATABLE READ 스냅샷)으로 묶는 읽기 — 세 쿼리가 서로 다른 시점을 보면 revision은
    /// 그대로인데 목록·총계가 어긋난 응답이 나갈 수 있다:
    /// <para>1) game_player SELECT — 현재 inventory_revision 확인(행 없으면 NoPlayer, 기대값과 다르면 RevisionChanged)</para>
    /// <para>2) player_item SELECT — row_type=1이고 slot &gt; cursor인 행을 slot 오름차순 limit+1건.
    ///     (user_id, slot) 유니크 인덱스가 이 범위 스캔을 커버한다(OFFSET 미사용)</para>
    /// <para>3) player_item COUNT — 가방 아이템 총 행 수(진행률 표시용)</para>
    /// </remarks>
    public async Task<InventoryPageOutcome> GetPageAsync(long userId, int cursor, int limit, long expectedRevision)
    {
        await using var connection = _dbFactory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var db = _dbFactory.Create(connection);

            // 1) 정합성 기준값 확인. 계정 세이브가 없으면 인벤토리도 없다.
            var revisionVal = await db.Query("game_player")
                .Select("inventory_revision")
                .Where("user_id", userId)
                .FirstOrDefaultAsync<long?>(transaction);
            if (revisionVal is null)
            {
                await transaction.RollbackAsync();
                return InventoryPageOutcome.Fail(InventoryPageStatus.NoPlayer, 0);
            }

            long revision = revisionVal.Value;
            // expectedRevision=0은 "기준값 없음"(첫 페이지)이라 검증을 건너뛴다. 계정의 실제 카운터는
            // 생성 시 1부터 시작하므로 0과 겹치지 않는다.
            if (expectedRevision > 0 && expectedRevision != revision)
            {
                await transaction.RollbackAsync();
                return InventoryPageOutcome.Fail(InventoryPageStatus.RevisionChanged, revision);
            }

            // 2) keyset 페이징. 다음 페이지 존재 여부를 알려고 limit+1건을 읽는다.
            //    재화 행은 slot이 NULL이라 slot > cursor 비교에서 자동으로 빠진다.
            var rows = (await db.Query("player_item")
                .Select("player_item_id", "slot", "item_code", "quantity", "enhance_level")
                .Where("user_id", userId).Where("row_type", RowTypeItem).Where("slot", ">", cursor)
                .OrderBy("slot")
                .Limit(limit + 1)
                .GetAsync<BagItemRow>(transaction)).ToList();

            bool hasMore = rows.Count > limit;
            if (hasMore)
            {
                rows.RemoveAt(rows.Count - 1);
            }

            // 3) 총 개수(진행률 표시용). 같은 스냅샷이라 2)의 결과와 시점이 일치한다.
            int total = await db.Query("player_item")
                .Where("user_id", userId).Where("row_type", RowTypeItem).WhereNotNull("slot")
                .CountAsync<int>(transaction: transaction);

            await transaction.CommitAsync();

            var items = rows.Select(r => new InventoryItemDto
            {
                itemId = r.PlayerItemId,
                slot = r.Slot,
                itemCode = r.ItemCode,
                quantity = r.Quantity,
                enhanceLevel = r.EnhanceLevel,
            }).ToList();

            return new InventoryPageOutcome(InventoryPageStatus.Ok, items, hasMore, total, revision);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// 장비 장착(같은 슬롯에 기존 장비가 있으면 스왑)을 단일 커넥션의 단일 트랜잭션으로 적용한다.
    /// 검증 실패(InvalidCharacter·ItemNotFound·ItemEquipped·NotEquippable)는 즉시 롤백 후 Fail 상태로 반환하고,
    /// 예외는 롤백 후 전파한다.
    /// </summary>
    /// <remarks>
    /// 한 트랜잭션으로 묶는 작업(해제와 장착을 함께 커밋해 슬롯이 비거나 유니크 제약(user_id, 캐릭터, 슬롯)이 깨지지 않게 한다):
    /// <para>1) player_character SELECT — 대상 캐릭터 존재 확인 및 직업·레벨 확보(장착 검증 입력)</para>
    /// <para>2) player_item SELECT — 대상 아이템의 계정 소유 확인 및 item_code·enhance_level 확보</para>
    /// <para>3) player_item_equipped SELECT — 이미 어딘가에 장착 중이면 거부(ItemEquipped)</para>
    /// <para>4) validate 델리게이트 — 마스터 검증(장비 여부·슬롯·클래스·레벨 제한)과 대상 장착 슬롯 산출(DB 접근 없음)</para>
    /// <para>5) player_item_equipped SELECT + DELETE — 같은 캐릭터·슬롯의 기존 장비가 있으면 장착 해제(스왑)</para>
    /// <para>6) player_item_equipped INSERT — 새 장착 행 생성</para>
    /// </remarks>
    public async Task<EquipOutcome> ApplyEquipAsync(
        long userId, int characterId, long itemId,
        Func<int, int, int, (bool ok, int slot)> validate)
    {
        await using var connection = _dbFactory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var db = _dbFactory.Create(connection);

            // 1) 대상 캐릭터 존재 확인(클래스·레벨은 장착 검증에 사용).
            var charRow = await db.Query("player_character")
                .Select("class_code", "level")
                .Where("user_id", userId).Where("character_id", characterId)
                .FirstOrDefaultAsync<CharClassLevelRow>(transaction);
            if (charRow is null)
            {
                await transaction.RollbackAsync();
                return EquipOutcome.Fail(EquipStatus.InvalidCharacter);
            }

            // 2) 대상 아이템 존재 확인(계정 소유).
            var itemRow = await db.Query("player_item")
                .Select("item_code", "enhance_level")
                .Where("player_item_id", itemId).Where("user_id", userId)
                .FirstOrDefaultAsync<ItemCodeEnhanceRow>(transaction);
            if (itemRow is null)
            {
                await transaction.RollbackAsync();
                return EquipOutcome.Fail(EquipStatus.ItemNotFound);
            }

            // 3) 이미 어딘가에 장착 중이면 거부(PK가 player_item_id).
            var alreadyEquippedId = await db.Query("player_item_equipped")
                .Select("player_item_id")
                .Where("player_item_id", itemId)
                .FirstOrDefaultAsync<long?>(transaction);
            if (alreadyEquippedId is not null)
            {
                await transaction.RollbackAsync();
                return EquipOutcome.Fail(EquipStatus.ItemEquipped);
            }

            int itemCode = itemRow.ItemCode;
            int enhanceLevel = itemRow.EnhanceLevel;
            int classCode = charRow.ClassCode;
            int level = charRow.Level;

            // 4) 마스터 검증: 장비/슬롯/클래스/레벨 정합. 대상 장착 슬롯 산출.
            var (ok, slot) = validate(itemCode, classCode, level);
            if (!ok)
            {
                await transaction.RollbackAsync();
                return EquipOutcome.Fail(EquipStatus.NotEquippable);
            }

            // 5) 같은 캐릭터-슬롯에 기존 장비가 있으면 해제(스왑).
            var prevItemId = await db.Query("player_item_equipped")
                .Select("player_item_id")
                .Where("user_id", userId).Where("equipped_character_id", characterId).Where("equipped_slot", slot)
                .FirstOrDefaultAsync<long?>(transaction);

            if (prevItemId is not null)
            {
                await db.Query("player_item_equipped").Where("player_item_id", prevItemId.Value).DeleteAsync(transaction);
            }

            // 6) 장착 행 INSERT.
            await db.Query("player_item_equipped").InsertAsync(new
            {
                player_item_id = itemId,
                user_id = userId,
                item_code = itemCode,
                enhance_level = enhanceLevel,
                equipped_character_id = characterId,
                equipped_slot = slot,
            }, transaction);

            // 7) 장착 상태가 바뀌었으므로 페이지 조회 정합성 카운터를 올린다.
            await InventoryRevision.BumpAsync(db, transaction, userId);

            await transaction.CommitAsync();
            return new EquipOutcome(EquipStatus.Ok, slot, prevItemId);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// 지정 캐릭터·장착 슬롯의 장비 해제를 단일 커넥션의 단일 트랜잭션으로 적용한다.
    /// 검증 실패(InvalidCharacter·NotEquipped)는 즉시 롤백 후 Fail 상태로 반환하고, 예외는 롤백 후 전파한다.
    /// </summary>
    /// <remarks>
    /// 한 트랜잭션으로 묶는 작업(검증과 삭제 사이에 대상이 바뀌지 않도록 같은 트랜잭션에서 읽고 지운다):
    /// <para>1) player_character SELECT — 대상 캐릭터 존재 확인</para>
    /// <para>2) player_item_equipped SELECT — 해당 캐릭터·슬롯의 장착 행 조회(없으면 NotEquipped)</para>
    /// <para>3) player_item_equipped DELETE — 장착 행 삭제(아이템 자체는 player_item에 남아 인벤토리로 복귀)</para>
    /// </remarks>
    public async Task<UnequipOutcome> ApplyUnequipAsync(long userId, int characterId, int slot)
    {
        await using var connection = _dbFactory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var db = _dbFactory.Create(connection);

            // 대상 캐릭터 존재 확인.
            var charId = await db.Query("player_character")
                .Select("character_id")
                .Where("user_id", userId).Where("character_id", characterId)
                .FirstOrDefaultAsync<int?>(transaction);
            if (charId is null)
            {
                await transaction.RollbackAsync();
                return UnequipOutcome.Fail(UnequipStatus.InvalidCharacter);
            }

            // 해당 캐릭터-슬롯의 장착 행 조회.
            var itemId = await db.Query("player_item_equipped")
                .Select("player_item_id")
                .Where("user_id", userId).Where("equipped_character_id", characterId).Where("equipped_slot", slot)
                .FirstOrDefaultAsync<long?>(transaction);
            if (itemId is null)
            {
                await transaction.RollbackAsync();
                return UnequipOutcome.Fail(UnequipStatus.NotEquipped);
            }

            await db.Query("player_item_equipped").Where("player_item_id", itemId.Value).DeleteAsync(transaction);

            // 장착 상태가 바뀌었으므로 페이지 조회 정합성 카운터를 올린다.
            await InventoryRevision.BumpAsync(db, transaction, userId);

            await transaction.CommitAsync();
            return new UnequipOutcome(UnequipStatus.Ok, itemId.Value);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// 인벤토리 칸 이동(목표 칸이 차 있으면 교환)을 단일 커넥션의 단일 트랜잭션으로 적용한다.
    /// 검증 실패(ItemNotFound·InvalidSlot)는 즉시 롤백 후 Fail 상태로 반환하고, 예외는 롤백 후 전파한다.
    /// 같은 칸으로의 이동은 변경 없이 커밋한다.
    /// </summary>
    /// <remarks>
    /// 한 트랜잭션으로 묶는 작업(교환이 3개의 UPDATE로 이루어져 트랜잭션 없이는 유니크 제약(user_id, slot) 위반 상태가 남을 수 있다):
    /// <para>1) game_player SELECT — inventory_capacity 확보 후 toSlot 범위 검증(범위 밖 → InvalidSlot)</para>
    /// <para>2) player_item SELECT — 이동 대상의 계정 소유·아이템 행(재화 아님)·배치 여부(slot NOT NULL) 확인</para>
    /// <para>3) player_item SELECT — 목표 칸 점유자 조회</para>
    /// <para>4-a) 점유자 있음(교환): 대상 slot을 NULL로 비움 → 점유자를 대상의 원래 칸으로 이동 → 대상을 목표 칸으로 이동(UPDATE 3회)</para>
    /// <para>4-b) 점유자 없음: 대상 slot을 목표 칸으로 UPDATE(1회)</para>
    /// </remarks>
    public async Task<MoveOutcome> ApplyMoveAsync(long userId, long itemId, int toSlot)
    {
        await using var connection = _dbFactory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var db = _dbFactory.Create(connection);

            // 1) 용량 확인(toSlot 범위 검증용). game_player 없으면 인벤토리 자체가 없음.
            var capacityVal = await db.Query("game_player")
                .Select("inventory_capacity")
                .Where("user_id", userId)
                .FirstOrDefaultAsync<int?>(transaction);
            if (capacityVal is null)
            {
                await transaction.RollbackAsync();
                return MoveOutcome.Fail(MoveStatus.ItemNotFound);
            }

            int capacity = capacityVal.Value;
            if (toSlot < 0 || toSlot >= capacity)
            {
                await transaction.RollbackAsync();
                return MoveOutcome.Fail(MoveStatus.InvalidSlot);
            }

            // 2) 이동 대상 아이템(계정 소유) 확인.
            var itemRow = await db.Query("player_item")
                .Select("row_type", "slot")
                .Where("player_item_id", itemId).Where("user_id", userId)
                .FirstOrDefaultAsync<ItemRowTypeSlotRow>(transaction);
            if (itemRow is null)
            {
                await transaction.RollbackAsync();
                return MoveOutcome.Fail(MoveStatus.ItemNotFound);
            }

            // 재화 행(row_type≠1)이나 미배치(slot NULL) 행은 이동 대상이 아니다.
            if (itemRow.RowType != RowTypeItem || itemRow.Slot is null)
            {
                await transaction.RollbackAsync();
                return MoveOutcome.Fail(MoveStatus.ItemNotFound);
            }

            int fromSlot = itemRow.Slot.Value;
            if (fromSlot == toSlot)
            {
                // 같은 칸으로의 이동은 변경 없음.
                await transaction.CommitAsync();
                return new MoveOutcome(MoveStatus.Ok, itemId, toSlot, null, null);
            }

            // 3) 목표 칸 점유자 조회.
            var occupantId = await db.Query("player_item")
                .Select("player_item_id")
                .Where("user_id", userId).Where("slot", toSlot)
                .FirstOrDefaultAsync<long?>(transaction);

            if (occupantId is not null)
            {
                // 교환: (user_id, slot) 유니크 위반을 피하려 이동 대상 slot을 잠시 비운 뒤 재배치한다.
                await db.Query("player_item").Where("player_item_id", itemId).UpdateAsync(new { slot = (int?)null }, transaction);
                await db.Query("player_item").Where("player_item_id", occupantId).UpdateAsync(new { slot = fromSlot }, transaction);
                await db.Query("player_item").Where("player_item_id", itemId).UpdateAsync(new { slot = toSlot }, transaction);

                // 두 아이템의 slot(=페이징 커서)이 바뀌었으므로 정합성 카운터를 올린다.
                await InventoryRevision.BumpAsync(db, transaction, userId);

                await transaction.CommitAsync();
                return new MoveOutcome(MoveStatus.Ok, itemId, toSlot, occupantId, fromSlot);
            }

            // 빈 칸으로 이동.
            await db.Query("player_item").Where("player_item_id", itemId).UpdateAsync(new { slot = toSlot }, transaction);

            // slot(=페이징 커서)이 바뀌었으므로 정합성 카운터를 올린다.
            await InventoryRevision.BumpAsync(db, transaction, userId);

            await transaction.CommitAsync();
            return new MoveOutcome(MoveStatus.Ok, itemId, toSlot, null, null);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// 인벤토리 용량 1칸 확장(골드 소모)을 단일 커넥션의 단일 트랜잭션으로 적용한다.
    /// 검증 실패(NoPlayer·CapacityMax·InsufficientCurrency)는 즉시 롤백 후 Fail 상태로 반환하고, 예외는 롤백 후 전파한다.
    /// </summary>
    /// <remarks>
    /// 한 트랜잭션으로 묶는 작업(하나라도 실패하면 전부 롤백 — 골드만 차감되고 용량이 안 늘어나는 상태를 막는다):
    /// <para>1) game_player SELECT — 현재 inventory_capacity 확인(계정 세이브 없으면 NoPlayer)</para>
    /// <para>2) planOne 델리게이트 — 현재 용량 기준 확장 가능 여부·비용 산출(마스터, DB 접근 없음. 상한 도달 → CapacityMax)</para>
    /// <para>3) player_item(재화 행) SELECT — 골드 잔액 확인(부족 → InsufficientCurrency)</para>
    /// <para>4) player_item UPDATE — 확장 비용 골드 차감</para>
    /// <para>5) game_player UPDATE — inventory_capacity +1 및 updated_at 갱신</para>
    /// </remarks>
    public async Task<ExpandOutcome> ApplyExpandAsync(long userId, Func<int, (bool ok, long cost)> planOne, long nowUnix)
    {
        await using var connection = _dbFactory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var db = _dbFactory.Create(connection);

            // 1) 현재 용량 확인.
            var capacityVal = await db.Query("game_player")
                .Select("inventory_capacity")
                .Where("user_id", userId)
                .FirstOrDefaultAsync<int?>(transaction);
            if (capacityVal is null)
            {
                await transaction.RollbackAsync();
                return ExpandOutcome.Fail(ExpandStatus.NoPlayer);
            }

            int capacity = capacityVal.Value;

            // 2) 확장 가능 여부·비용 산출(마스터). 상한 도달이면 거부.
            var (ok, cost) = planOne(capacity);
            if (!ok)
            {
                await transaction.RollbackAsync();
                return ExpandOutcome.Fail(ExpandStatus.CapacityMax);
            }

            // 3) 골드 잔액 확인.
            var goldRow = await db.Query("player_item")
                .Select("player_item_id", "quantity")
                .Where("user_id", userId).Where("row_type", RowTypeCurrency).Where("item_code", GoldItemCode)
                .FirstOrDefaultAsync<ItemIdQtyRow>(transaction);

            long gold = goldRow is null ? 0 : goldRow.Quantity;
            if (gold < cost)
            {
                await transaction.RollbackAsync();
                return ExpandOutcome.Fail(ExpandStatus.InsufficientCurrency);
            }

            // 4) 골드 차감(재화 행 UPDATE) + 용량 +1.
            long newGold = gold - cost;
            if (goldRow is not null)
            {
                await db.Query("player_item").Where("player_item_id", goldRow.PlayerItemId)
                    .UpdateAsync(new { quantity = newGold }, transaction);
            }

            int newCapacity = capacity + 1;
            await db.Query("game_player").Where("user_id", userId)
                .UpdateAsync(new { inventory_capacity = newCapacity, updated_at = nowUnix }, transaction);

            // 용량이 늘어 인벤토리 뷰 자체가 달라지므로 정합성 카운터를 올린다(진행 중인 페이징은 재조회).
            await InventoryRevision.BumpAsync(db, transaction, userId);

            await transaction.CommitAsync();
            return new ExpandOutcome(ExpandStatus.Ok, newCapacity, cost, newGold);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
