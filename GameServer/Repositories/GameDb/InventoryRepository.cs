using System.Data.Common;
using GameServer.Data;
using GameServer.Models;
using GameServer.Repositories.GameDb.Interfaces;
using SqlKata.Execution;
using TaskbarHero.Common.Dto;

namespace GameServer.Repositories.GameDb;

/// <summary>
/// 장착 트랜잭션 결과. Slot=장착 슬롯, UnequippedItemId=스왑으로 밀려난 기존 장비(없으면 null),
/// UnequippedBagSlot=그 장비가 되돌아간 가방 칸(없으면 null).
/// </summary>
public sealed record EquipOutcome(EquipStatus Status, int Slot, long? UnequippedItemId, int? UnequippedBagSlot)
{
    /// <summary>가방 변경분(5.0). 장착 아이템은 칸을 반납하므로 removed, 스왑된 장비는 그 칸으로 들어오므로 upserted.</summary>
    public InventoryDeltaDto Delta { get; init; } = new InventoryDeltaDto();

    public static EquipOutcome Fail(EquipStatus status) => new(status, 0, null, null);
}

/// <summary>장착 해제 트랜잭션 결과. BagSlot=장비가 되돌아간 가방 칸.</summary>
public sealed record UnequipOutcome(UnequipStatus Status, long ItemId, int BagSlot)
{
    /// <summary>가방 변경분(5.0). 해제한 장비가 BagSlot 칸으로 돌아오므로 upserted 1건.</summary>
    public InventoryDeltaDto Delta { get; init; } = new InventoryDeltaDto();

    public static UnequipOutcome Fail(UnequipStatus status) => new(status, 0, 0);
}

/// <summary>배치 이동 트랜잭션 결과. Swapped*는 목표 칸에 있던 아이템(비어 있었으면 null).</summary>
public sealed record MoveOutcome(MoveStatus Status, long MovedItemId, int MovedSlot, long? SwappedItemId, int? SwappedSlot)
{
    /// <summary>가방 변경분(5.0). 이동한 아이템과 교환으로 밀려난 아이템의 최종 배치가 upserted에 담긴다.</summary>
    public InventoryDeltaDto Delta { get; init; } = new InventoryDeltaDto();

    public static MoveOutcome Fail(MoveStatus status) => new(status, 0, 0, null, null);
}

/// <summary>
/// 장비 강화 트랜잭션 결과. EnhanceLevel=상승 후 단계, Cost=차감한 재화량,
/// CurrencyCode=소모 재화 item_code, CurrencyBalance=차감 후 잔액, Equipped=장착 중인 장비였는지.
/// </summary>
public sealed record EnhanceOutcome(
    EnhanceStatus Status, int EnhanceLevel, long Cost, int CurrencyCode, long CurrencyBalance, bool Equipped)
{
    public static EnhanceOutcome Fail(EnhanceStatus status) => new(status, 0, 0, 0, 0, false);
}

/// <summary>인벤토리 확장 트랜잭션 결과. Cost=차감 골드, GoldBalance=차감 후 잔액.</summary>
public sealed record ExpandOutcome(ExpandStatus Status, int InventoryCapacity, long Cost, long GoldBalance)
{
    public static ExpandOutcome Fail(ExpandStatus status) => new(status, 0, 0, 0);
}

/// <summary>
/// 가방 페이지 조회 결과. Items는 요청 커서 다음부터 slot 오름차순으로 <b>limit개까지</b>이고,
/// Total은 그 계정의 가방 점유 칸 수 전체다(페이지 크기와 무관).
/// </summary>
public sealed record InventoryBagOutcome(InventoryPageStatus Status, IReadOnlyList<InventoryItemDto> Items, int Total)
{
    public static InventoryBagOutcome Fail(InventoryPageStatus status) =>
        new(status, Array.Empty<InventoryItemDto>(), 0);
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
    /// 그 계정의 가방 아이템을 slot 커서 keyset 페이징으로 조회한다. 계정 세이브가 없으면 NoPlayer.
    /// </summary>
    /// <remarks>
    /// 읽기 2개(+조건부 1개). 읽기 전용이라 트랜잭션으로 묶지 않는다 — 페이지 간 정합성은 애초에 보장 대상이
    /// 아니고, 클라이언트가 itemId 기준 병합으로 흡수한다:
    /// <para>1) player_item SELECT — <c>row_type=1 AND slot &gt; cursor</c>를 slot 오름차순으로 limit개.
    ///     <c>(user_id, slot)</c> 유니크 인덱스가 범위 스캔과 정렬을 모두 담당해 filesort가 없다.
    ///     재화 행과 장착 중인 장비는 slot이 NULL이라 자동으로 빠진다</para>
    /// <para>2) player_item COUNT — 총 점유 칸 수(응답 <c>total</c>). 같은 인덱스를 타며 페이지와 무관하다</para>
    /// <para>3) game_player SELECT — <b>총 칸 수가 0일 때만</b> 계정 세이브 존재를 확인한다(없으면 NoPlayer)</para>
    /// </remarks>
    /// <remarks>
    /// <b>계정 세이브 확인을 조건부로 미루는 근거</b>: <c>player_item</c>은 <c>game_player</c>에 FK
    /// (<c>fk_item_player … ON DELETE CASCADE</c>)로 매달려 있어 <b>세이브 없이 아이템 행이 존재할 수 없다.</b>
    /// 따라서 2)의 총 칸 수가 1 이상이면 세이브 존재가 이미 증명되어 확인 쿼리가 불필요하다. 가방이 완전히
    /// 빈 계정에서만 3)을 쳐서 "세이브 없음(NoPlayer)"과 "세이브는 있고 가방만 빔"을 가른다 — 응답은
    /// 확인을 먼저 하던 때와 완전히 동일하고, 정상 경로의 DB 왕복만 3회에서 2회로 준다
    /// (측정 근거: <c>docs/공통/쿼리-분석.md</c> 8.3).
    /// </remarks>
    /// <remarks>
    /// <b>전량을 읽어 메모리에서 자르지 않는다.</b> 필요한 구간만 DB에서 잘라 오므로 페이지 크기에 비례한 비용만
    /// 든다(가방 캐시를 두지 않는 이유이자 전제다 — 기획서 6.5).
    /// </remarks>
    public async Task<InventoryBagOutcome> GetPageAsync(long userId, int cursor, int limit)
    {
        await using var connection = _dbFactory.CreateConnection();
        await connection.OpenAsync();
        var db = _dbFactory.Create(connection);

        // 1) 요청 구간만(커서 다음부터 limit개). 한 건 더 읽어 호출측이 hasMore를 판정한다.
        var rows = await db.Query("player_item")
            .Select("player_item_id", "slot", "item_code", "quantity", "enhance_level")
            .Where("user_id", userId).Where("row_type", RowTypeItem).Where("slot", ">", cursor)
            .OrderBy("slot")
            .Limit(limit)
            .GetAsync<BagItemRow>();

        // 2) 총 점유 칸 수(페이지와 무관한 전체 집계).
        var total = await db.Query("player_item")
            .Where("user_id", userId).Where("row_type", RowTypeItem).WhereNotNull("slot")
            .CountAsync<int>();

        // 3) 가방이 완전히 비었을 때만 계정 세이브를 확인한다. FK(ON DELETE CASCADE) 때문에 세이브 없이
        //    아이템 행이 있을 수 없으므로, total > 0이면 세이브 존재가 이미 증명된 것이라 왕복을 아낀다.
        if (total == 0)
        {
            var playerId = await db.Query("game_player")
                .Select("user_id")
                .Where("user_id", userId)
                .FirstOrDefaultAsync<long?>();
            if (playerId is null)
            {
                return InventoryBagOutcome.Fail(InventoryPageStatus.NoPlayer);
            }
        }

        var items = rows.Select(r => new InventoryItemDto
        {
            itemId = r.PlayerItemId,
            slot = r.Slot,
            itemCode = r.ItemCode,
            quantity = r.Quantity,
            enhanceLevel = r.EnhanceLevel,
        }).ToList();

        return new InventoryBagOutcome(InventoryPageStatus.Ok, items, total);
    }

    /// <summary>
    /// 장비 장착(같은 슬롯에 기존 장비가 있으면 스왑)을 단일 커넥션의 단일 트랜잭션으로 적용한다.
    /// 장착하는 아이템은 **가방 칸을 반납**하고(<c>player_item.slot = NULL</c>), 스왑으로 밀려난 기존 장비는
    /// 그 반납된 칸으로 들어간다(칸 수가 상쇄되므로 스왑은 용량 부족으로 실패하지 않는다).
    /// 검증 실패(InvalidCharacter·ItemNotFound·ItemEquipped·NotEquippable·InventoryFull)는 즉시 롤백 후
    /// Fail 상태로 반환하고, 예외는 롤백 후 전파한다.
    /// </summary>
    /// <remarks>
    /// 한 트랜잭션으로 묶는 작업(해제와 장착을 함께 커밋해 슬롯이 비거나 유니크 제약(user_id, 캐릭터, 슬롯)이 깨지지 않게 한다):
    /// <para>1) player_character SELECT — 대상 캐릭터 존재 확인 및 직업·레벨 확보(장착 검증 입력)</para>
    /// <para>2) player_item SELECT — 대상 아이템의 계정 소유 확인 및 item_code·enhance_level·현재 가방 칸 확보</para>
    /// <para>3) player_item_equipped SELECT — 이미 어딘가에 장착 중이면 거부(ItemEquipped)</para>
    /// <para>4) validate 델리게이트 — 마스터 검증(장비 여부·슬롯·클래스·레벨 제한)과 대상 장착 슬롯 산출(DB 접근 없음)</para>
    /// <para>5) player_item_equipped SELECT + DELETE — 같은 캐릭터·슬롯의 기존 장비가 있으면 장착 해제(스왑)</para>
    /// <para>6) player_item UPDATE — 장착 아이템의 slot을 NULL로(가방 칸 반납). 유니크 (user_id, slot) 위반을
    ///     피하려고 기존 장비를 그 칸에 넣기 전에 먼저 비운다</para>
    /// <para>7) player_item UPDATE — 스왑으로 밀려난 기존 장비를 6)에서 비운 칸에 배치(그 칸이 없으면 빈 칸 탐색,
    ///     그마저 없으면 InventoryFull)</para>
    /// <para>8) player_item_equipped INSERT — 새 장착 행 생성</para>
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

            // 2) 대상 아이템 존재 확인(계정 소유). slot은 장착 시 반납할 가방 칸이다.
            var itemRow = await db.Query("player_item")
                .Select("item_code", "enhance_level", "slot")
                .Where("player_item_id", itemId).Where("user_id", userId)
                .FirstOrDefaultAsync<ItemCodeEnhanceSlotRow>(transaction);
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

            // 6) 장착 아이템의 가방 칸 반납. 7)에서 기존 장비를 이 칸에 넣으므로 먼저 비워야
            //    유니크 (user_id, slot)에 걸리지 않는다.
            int? freedSlot = itemRow.Slot;
            if (freedSlot is not null)
            {
                await db.Query("player_item").Where("player_item_id", itemId)
                    .UpdateAsync(new { slot = (int?)null }, transaction);
            }

            // 7) 스왑으로 밀려난 기존 장비를 가방으로 되돌린다. 방금 반납한 칸을 그대로 물려주므로
            //    점유 칸 수가 상쇄되어 통상 실패하지 않는다(반납할 칸이 없던 예외 상황만 빈 칸을 찾는다).
            int? prevBagSlot = null;
            if (prevItemId is not null)
            {
                prevBagSlot = freedSlot ?? await InventorySlotAllocator.FindFreeSlotAsync(db, transaction, userId);
                if (prevBagSlot is null)
                {
                    await transaction.RollbackAsync();
                    return EquipOutcome.Fail(EquipStatus.InventoryFull);
                }

                await db.Query("player_item").Where("player_item_id", prevItemId.Value)
                    .UpdateAsync(new { slot = prevBagSlot.Value }, transaction);
            }

            // 8) 장착 행 INSERT.
            await db.Query("player_item_equipped").InsertAsync(new
            {
                player_item_id = itemId,
                user_id = userId,
                item_code = itemCode,
                enhance_level = enhanceLevel,
                equipped_character_id = characterId,
                equipped_slot = slot,
            }, transaction);

            // 9) 가방 변경분(5.0): 장착 아이템은 칸을 반납해 removed, 스왑된 장비는 그 칸으로 들어와 upserted.
            var delta = new InventoryDeltaDto();
            delta.removed.Add(itemId);
            if (prevItemId is not null && prevBagSlot is not null)
            {
                delta.upserted.Add(await LoadBagItemAsync(db, transaction, prevItemId.Value, prevBagSlot.Value));
            }

            await transaction.CommitAsync();
            return new EquipOutcome(EquipStatus.Ok, slot, prevItemId, prevBagSlot) { Delta = delta };
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// 지정 캐릭터·장착 슬롯의 장비 해제를 단일 커넥션의 단일 트랜잭션으로 적용한다.
    /// 장착 중에는 가방 칸을 쓰지 않으므로(<c>slot = NULL</c>), 해제하려면 **되돌릴 빈 칸이 필요**하다.
    /// 검증 실패(InvalidCharacter·NotEquipped·InventoryFull)는 즉시 롤백 후 Fail 상태로 반환하고,
    /// 예외는 롤백 후 전파한다.
    /// </summary>
    /// <remarks>
    /// 한 트랜잭션으로 묶는 작업(장착 해제와 가방 배치를 함께 커밋해 어느 쪽에도 없는 아이템이 생기지 않게 한다):
    /// <para>1) player_character SELECT — 대상 캐릭터 존재 확인</para>
    /// <para>2) player_item_equipped SELECT — 해당 캐릭터·슬롯의 장착 행 조회(없으면 NotEquipped)</para>
    /// <para>3) game_player + player_item SELECT — 용량과 점유 칸을 읽어 가장 작은 빈 칸 산출(없으면 InventoryFull)</para>
    /// <para>4) player_item UPDATE — 그 빈 칸에 아이템 배치(가방 복귀)</para>
    /// <para>5) player_item_equipped DELETE — 장착 행 삭제</para>
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

            // 가방에 되돌릴 빈 칸 확보. 장착 중에는 칸을 쓰지 않으므로 해제하려면 자리가 있어야 한다.
            var bagSlot = await InventorySlotAllocator.FindFreeSlotAsync(db, transaction, userId);
            if (bagSlot is null)
            {
                await transaction.RollbackAsync();
                return UnequipOutcome.Fail(UnequipStatus.InventoryFull);
            }

            await db.Query("player_item").Where("player_item_id", itemId.Value)
                .UpdateAsync(new { slot = bagSlot.Value }, transaction);

            await db.Query("player_item_equipped").Where("player_item_id", itemId.Value).DeleteAsync(transaction);

            // 가방 변경분(5.0): 해제한 장비가 bagSlot 칸으로 돌아온다.
            var delta = new InventoryDeltaDto();
            delta.upserted.Add(await LoadBagItemAsync(db, transaction, itemId.Value, bagSlot.Value));

            await transaction.CommitAsync();
            return new UnequipOutcome(UnequipStatus.Ok, itemId.Value, bagSlot.Value) { Delta = delta };
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

            // 2) 이동 대상 아이템(계정 소유) 확인. item_code·quantity·enhance_level은 가방 변경분(5.0) 조립용.
            var itemRow = await db.Query("player_item")
                .Select("row_type", "slot", "item_code", "quantity", "enhance_level")
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
                // 같은 칸으로의 이동은 변경 없음(가방 변경분도 비어 있다).
                await transaction.CommitAsync();
                return new MoveOutcome(MoveStatus.Ok, itemId, toSlot, null, null);
            }

            // 3) 목표 칸 점유자 조회(변경분 조립을 위해 표시 정보까지 함께 읽는다).
            var occupant = await db.Query("player_item")
                .Select("player_item_id", "slot", "item_code", "quantity", "enhance_level")
                .Where("user_id", userId).Where("slot", toSlot)
                .FirstOrDefaultAsync<BagItemRow>(transaction);

            // 가방 변경분(5.0): 이동한 아이템의 최종 배치는 언제나 toSlot.
            var delta = new InventoryDeltaDto();
            delta.upserted.Add(new InventoryItemDto
            {
                itemId = itemId,
                slot = toSlot,
                itemCode = itemRow.ItemCode,
                quantity = itemRow.Quantity,
                enhanceLevel = itemRow.EnhanceLevel,
            });

            if (occupant is not null)
            {
                // 교환: (user_id, slot) 유니크 위반을 피하려 이동 대상 slot을 잠시 비운 뒤 재배치한다.
                await db.Query("player_item").Where("player_item_id", itemId).UpdateAsync(new { slot = (int?)null }, transaction);
                await db.Query("player_item").Where("player_item_id", occupant.PlayerItemId).UpdateAsync(new { slot = fromSlot }, transaction);
                await db.Query("player_item").Where("player_item_id", itemId).UpdateAsync(new { slot = toSlot }, transaction);

                delta.upserted.Add(new InventoryItemDto
                {
                    itemId = occupant.PlayerItemId,
                    slot = fromSlot,
                    itemCode = occupant.ItemCode,
                    quantity = occupant.Quantity,
                    enhanceLevel = occupant.EnhanceLevel,
                });

                await transaction.CommitAsync();
                return new MoveOutcome(MoveStatus.Ok, itemId, toSlot, occupant.PlayerItemId, fromSlot) { Delta = delta };
            }

            // 빈 칸으로 이동.
            await db.Query("player_item").Where("player_item_id", itemId).UpdateAsync(new { slot = toSlot }, transaction);

            await transaction.CommitAsync();
            return new MoveOutcome(MoveStatus.Ok, itemId, toSlot, null, null) { Delta = delta };
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// 장비 강화(단계 +1)를 단일 커넥션의 단일 트랜잭션으로 적용한다.
    /// 검증 실패(ItemNotFound·NotEquippable·MaxEnhanceReached·InsufficientCurrency)는 즉시 롤백 후 Fail 상태로
    /// 반환하고, 예외는 롤백 후 전파한다. <b>장착 중인 장비도 강화할 수 있다</b> — 가방이 가득 차면 해제 자체가
    /// 실패해(InventoryFull) 강화가 막히므로, 해제를 요구하지 않고 장착 행의 강화 단계까지 같은 트랜잭션에서 올린다.
    /// </summary>
    /// <remarks>
    /// 한 트랜잭션으로 묶는 작업(하나라도 실패하면 전부 롤백 — 재화만 빠지고 단계가 안 오르는 상태를 막는다):
    /// <para>1) player_item SELECT — 대상의 계정 소유 확인 및 row_type·item_code·현재 enhance_level 확보</para>
    /// <para>2) plan 델리게이트 — 마스터 검증(장비 여부·다음 단계 존재)과 비용·소모 재화 산출(DB 접근 없음)</para>
    /// <para>3) player_item(재화 행) SELECT — 비용 재화 잔액 확인(부족 → InsufficientCurrency)</para>
    /// <para>4) player_item UPDATE — 비용 재화 차감</para>
    /// <para>5) player_item UPDATE — 대상의 enhance_level += 1</para>
    /// <para>6) player_item_equipped UPDATE — 장착 중이면 장착 행의 enhance_level도 같은 값으로 갱신
    ///     (코어 로드의 equipped가 이 컬럼을 그대로 내려주므로 갱신하지 않으면 장착 스탯이 옛 단계로 남는다)</para>
    /// </remarks>
    public async Task<EnhanceOutcome> ApplyEnhanceAsync(
        long userId, long itemId,
        Func<int, int, (EnhancePlanStatus status, long cost, int currencyCode)> plan)
    {
        await using var connection = _dbFactory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var db = _dbFactory.Create(connection);

            // 1) 대상 아이템 존재 확인(계정 소유).
            var itemRow = await db.Query("player_item")
                .Select("row_type", "slot", "item_code", "quantity", "enhance_level")
                .Where("player_item_id", itemId).Where("user_id", userId)
                .FirstOrDefaultAsync<ItemRowTypeSlotRow>(transaction);
            if (itemRow is null)
            {
                await transaction.RollbackAsync();
                return EnhanceOutcome.Fail(EnhanceStatus.ItemNotFound);
            }

            // 재화 행(row_type≠1)은 강화 대상이 아니다(마스터 판정 전에 걸러낸다).
            if (itemRow.RowType != RowTypeItem)
            {
                await transaction.RollbackAsync();
                return EnhanceOutcome.Fail(EnhanceStatus.NotEquippable);
            }

            // 2) 마스터 검증: 장비 여부 · 다음 단계 존재 · 비용/소모 재화.
            var (planStatus, cost, currencyCode) = plan(itemRow.ItemCode, itemRow.EnhanceLevel);
            if (planStatus != EnhancePlanStatus.Ok)
            {
                await transaction.RollbackAsync();
                return EnhanceOutcome.Fail(planStatus == EnhancePlanStatus.MaxReached
                    ? EnhanceStatus.MaxEnhanceReached
                    : EnhanceStatus.NotEquippable);
            }

            // 3) 비용 재화 잔액 확인.
            var currencyRow = await db.Query("player_item")
                .Select("player_item_id", "quantity")
                .Where("user_id", userId).Where("row_type", RowTypeCurrency).Where("item_code", currencyCode)
                .FirstOrDefaultAsync<ItemIdQtyRow>(transaction);

            long balance = currencyRow is null ? 0 : currencyRow.Quantity;
            if (balance < cost)
            {
                await transaction.RollbackAsync();
                return EnhanceOutcome.Fail(EnhanceStatus.InsufficientCurrency);
            }

            // 4) 비용 차감(재화 행 UPDATE).
            long newBalance = balance - cost;
            if (currencyRow is not null && cost > 0)
            {
                await db.Query("player_item").Where("player_item_id", currencyRow.PlayerItemId)
                    .UpdateAsync(new { quantity = newBalance }, transaction);
            }

            // 5) 강화 단계 +1.
            int newLevel = itemRow.EnhanceLevel + 1;
            await db.Query("player_item").Where("player_item_id", itemId)
                .UpdateAsync(new { enhance_level = newLevel }, transaction);

            // 6) 장착 중이면 장착 행의 강화 단계 스냅샷도 함께 갱신(코어 로드 equipped가 이 값을 내려준다).
            int equippedUpdated = await db.Query("player_item_equipped").Where("player_item_id", itemId)
                .UpdateAsync(new { enhance_level = newLevel }, transaction);

            await transaction.CommitAsync();
            return new EnhanceOutcome(
                EnhanceStatus.Ok, newLevel, cost, currencyCode, newBalance, equippedUpdated > 0);
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

            await transaction.CommitAsync();
            return new ExpandOutcome(ExpandStatus.Ok, newCapacity, cost, newGold);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// 가방 변경분(5.0)에 담을 행 1건을 조립한다. 아이템의 표시 정보(item_code·quantity·enhance_level)를
    /// 호출측 트랜잭션 안에서 읽고, 배치 칸은 그 트랜잭션이 확정한 값(<paramref name="slot"/>)을 쓴다
    /// (아직 커밋 전이라 DB의 slot 컬럼과 다를 수 있으므로 조회하지 않고 인자로 받는다).
    /// </summary>
    private static async Task<InventoryItemDto> LoadBagItemAsync(
        QueryFactory db, DbTransaction transaction, long itemId, int slot)
    {
        var row = await db.Query("player_item")
            .Select("player_item_id", "slot", "item_code", "quantity", "enhance_level")
            .Where("player_item_id", itemId)
            .FirstOrDefaultAsync<BagItemRow>(transaction);

        return new InventoryItemDto
        {
            itemId = itemId,
            slot = slot,
            itemCode = row?.ItemCode ?? 0,
            quantity = row?.Quantity ?? 1,
            enhanceLevel = row?.EnhanceLevel ?? 0,
        };
    }
}
