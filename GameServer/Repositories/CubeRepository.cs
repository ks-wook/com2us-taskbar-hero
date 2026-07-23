using System.Data.Common;
using GameServer.Data;
using GameServer.MasterData;
using SqlKata.Execution;

namespace GameServer.Repositories;

// ── 합성(combine) ──
public enum CombineStatus
{
    Ok,
    ItemNotFound,   // 입력 아이템 일부가 계정에 없음(또는 재화 행)
    ItemEquipped,   // 입력 중 장착 중인 아이템
    RecipeNotMet,   // 등급/슬롯/클래스 불일치·개수·등급 상한 등 조건 미충족
}

/// <summary>합성 입력(리포지토리가 DB에서 채운 아이템 코드). 마스터 검증은 서비스 델리게이트가 수행.</summary>
public sealed record CombineInput(long ItemId, int ItemCode);

/// <summary>합성 판정 결과(서비스 델리게이트 반환). Ok면 결과 아이템 코드·등급·큐브 경험치 획득.</summary>
public sealed record CombineDecision(CombineStatus Status, int ResultItemCode, int ResultGrade, long CubeExpGain)
{
    public static CombineDecision Reject() => new(CombineStatus.RecipeNotMet, 0, 0, 0);
    public static CombineDecision Accept(int resultItemCode, int resultGrade, long cubeExpGain)
        => new(CombineStatus.Ok, resultItemCode, resultGrade, cubeExpGain);
}

/// <summary>합성 트랜잭션 결과.</summary>
public sealed record CombineOutcome(
    CombineStatus Status, long ResultItemId, int ResultItemCode, int ResultGrade, int CubeLevel, long CubeExp)
{
    public static CombineOutcome Fail(CombineStatus status) => new(status, 0, 0, 0, 0, 0);
}

// ── 분해(dismantle) ──
public enum DismantleStatus
{
    Ok,
    ItemNotFound,         // 대상 아이템 없음(또는 재화 행)
    ItemEquipped,         // 장착 중이라 분해 불가
    InsufficientQuantity, // 요청 수량이 보유 수량 초과
}

/// <summary>분해 입력(아이템 코드 + 분해 수량). 골드·경험치 산출은 서비스 델리게이트가 수행.</summary>
public sealed record DismantleInput(long ItemId, int ItemCode, int Count);

/// <summary>분해 보상(서비스 델리게이트 반환): 획득 골드·큐브 경험치 합계.</summary>
public sealed record DismantleReward(long TotalGold, long TotalCubeExp);

/// <summary>분해 트랜잭션 결과. Gold·CubeExp는 이번 분해로 획득한 증가분.</summary>
public sealed record DismantleOutcome(DismantleStatus Status, long Gold, long CubeExp)
{
    public static DismantleOutcome Fail(DismantleStatus status) => new(status, 0, 0);
}

// ── 제작(craft) ──
public enum CraftStatus
{
    Ok,
    CubeLevelInsufficient, // 큐브 레벨이 요구치 미만
    InsufficientCurrency,  // 비용 골드 부족
    RecipeNotMet,          // 소모 재료 부족
    InventoryFull,         // 결과 아이템 적재 용량 부족
}

/// <summary>제작 트랜잭션 결과. CubeLevel·CubeExp는 갱신 후 큐브 상태.</summary>
public sealed record CraftOutcome(CraftStatus Status, int CubeLevel, long CubeExp)
{
    public static CraftOutcome Fail(CraftStatus status) => new(status, 0, 0);
}

public interface ICubeRepository
{
    /// <summary>
    /// 합성을 한 트랜잭션으로 적용한다: 입력 아이템 소유·미장착 확인 → decide(마스터 검증: 등급/슬롯/클래스/개수, 결과 산출)
    /// → 입력 삭제 + 상위 등급 결과 아이템 생성 + 큐브 경험치 반영. advanceCube는 (level,exp,gain)→(newLevel,newExp).
    /// </summary>
    Task<CombineOutcome> ApplyCombineAsync(
        long userId, IReadOnlyList<long> itemIds,
        Func<int, IReadOnlyList<CombineInput>, CombineDecision> decide,
        Func<int, long, long, (int newLevel, long newExp)> advanceCube,
        long nowUnix);

    /// <summary>
    /// 분해를 한 트랜잭션으로 적용한다: 각 아이템 소유·미장착·수량 확인 → computeReward(마스터 등급으로 골드·경험치 산출)
    /// → 아이템 차감/삭제 + 골드 적립 + 큐브 경험치 반영.
    /// </summary>
    Task<DismantleOutcome> ApplyDismantleAsync(
        long userId, IReadOnlyList<(long itemId, int count)> items,
        Func<int, IReadOnlyList<DismantleInput>, DismantleReward> computeReward,
        Func<int, long, long, (int newLevel, long newExp)> advanceCube,
        long nowUnix);

    /// <summary>
    /// 제작을 한 트랜잭션으로 적용한다: 큐브 레벨·골드·재료 확인 → 골드·재료 차감 + 결과 아이템 지급 + 큐브 경험치 반영.
    /// recipe 존재 여부는 호출 전(서비스, 마스터)에서 검증한다. resultItemType/resultStackMax는 결과 아이템 마스터 값.
    /// </summary>
    Task<CraftOutcome> ApplyCraftAsync(
        long userId, RecipeDef recipe, int resultItemType, int resultStackMax, long cubeExpGain,
        Func<int, long, long, (int newLevel, long newExp)> advanceCube,
        long nowUnix);
}

// ── DB 행 매핑용 POCO(제네릭 매핑 전용, dynamic 금지). snake_case→PascalCase는 Dapper 규칙으로 매핑. ──
file sealed class PlayerItemBriefRow
{
    public long PlayerItemId { get; set; }
    public int ItemCode { get; set; }
    public int RowType { get; set; }
    public long Quantity { get; set; }
    public int? Slot { get; set; }
}

file sealed class CubeStateRow
{
    public int CubeLevel { get; set; }
    public long CubeExp { get; set; }
}

file sealed class ItemIdQtyRow
{
    public long PlayerItemId { get; set; }
    public long Quantity { get; set; }
}

/// <summary>큐브(합성·분해·제작) 세이브 접근 계층(taskbar_hero_game). SqlKata 쿼리 빌더 + 제네릭 매핑만 사용한다(dynamic 금지).</summary>
public sealed class CubeRepository : ICubeRepository
{
    private const int RowTypeItem = 1;
    private const int RowTypeCurrency = 2;
    private const int GoldItemCode = 1;
    private const int ItemTypeMaterial = 2;

    private readonly GameDbFactory _dbFactory;

    public CubeRepository(GameDbFactory dbFactory) => _dbFactory = dbFactory;

    public async Task<CombineOutcome> ApplyCombineAsync(
        long userId, IReadOnlyList<long> itemIds,
        Func<int, IReadOnlyList<CombineInput>, CombineDecision> decide,
        Func<int, long, long, (int newLevel, long newExp)> advanceCube,
        long nowUnix)
    {
        await using var connection = _dbFactory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var db = _dbFactory.Create(connection);

            var (cubeLevel, cubeExp, hasCube) = await LoadCubeAsync(db, transaction, userId);

            // 1) 입력 아이템 조회(계정 소유). 개수 불일치 = 일부 미보유.
            var rows = await db.Query("player_item")
                .Select("player_item_id", "item_code", "row_type", "quantity", "slot")
                .Where("user_id", userId).WhereIn("player_item_id", itemIds)
                .GetAsync<PlayerItemBriefRow>(transaction);
            var rowList = rows.ToList();

            if (rowList.Count != itemIds.Count || rowList.Any(r => r.RowType != RowTypeItem))
            {
                await transaction.RollbackAsync();
                return CombineOutcome.Fail(CombineStatus.ItemNotFound);
            }

            // 2) 장착 중 아이템은 소모 불가.
            var equippedIds = (await db.Query("player_item_equipped")
                .Select("player_item_id")
                .Where("user_id", userId).WhereIn("player_item_id", itemIds)
                .GetAsync<long>(transaction)).ToHashSet();
            if (rowList.Any(r => equippedIds.Contains(r.PlayerItemId)))
            {
                await transaction.RollbackAsync();
                return CombineOutcome.Fail(CombineStatus.ItemEquipped);
            }

            // 3) 마스터 검증 + 결과 산출(서비스).
            var inputs = rowList.Select(r => new CombineInput(r.PlayerItemId, r.ItemCode)).ToList();
            var decision = decide(cubeLevel, inputs);
            if (decision.Status != CombineStatus.Ok)
            {
                await transaction.RollbackAsync();
                return CombineOutcome.Fail(decision.Status);
            }

            // 4) 입력 삭제.
            await db.Query("player_item").Where("user_id", userId).WhereIn("player_item_id", itemIds)
                .DeleteAsync(transaction);

            // 5) 결과 아이템 생성(입력 삭제로 빈 칸이 생기므로 항상 적재 가능).
            int capacity = await LoadCapacityAsync(db, transaction, userId);
            var used = await LoadUsedSlotsAsync(db, transaction, userId);
            int slot = FirstFreeSlot(used, capacity);
            if (slot < 0)
            {
                slot = capacity; // 입력 삭제로 자리가 보장되나, 방어적으로 말미에 적재.
            }

            long resultItemId = await db.Query("player_item").InsertGetIdAsync<long>(new
            {
                user_id = userId,
                row_type = RowTypeItem,
                item_code = decision.ResultItemCode,
                quantity = 1,
                slot = slot,
                enhance_level = 0,
                acquired_at = nowUnix,
            }, transaction);

            // 6) 큐브 경험치 반영.
            var (newLevel, newExp) = advanceCube(cubeLevel, cubeExp, decision.CubeExpGain);
            await UpsertCubeAsync(db, transaction, userId, hasCube, newLevel, newExp);

            await transaction.CommitAsync();
            return new CombineOutcome(CombineStatus.Ok, resultItemId, decision.ResultItemCode, decision.ResultGrade, newLevel, newExp);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<DismantleOutcome> ApplyDismantleAsync(
        long userId, IReadOnlyList<(long itemId, int count)> items,
        Func<int, IReadOnlyList<DismantleInput>, DismantleReward> computeReward,
        Func<int, long, long, (int newLevel, long newExp)> advanceCube,
        long nowUnix)
    {
        await using var connection = _dbFactory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var db = _dbFactory.Create(connection);

            var (cubeLevel, cubeExp, hasCube) = await LoadCubeAsync(db, transaction, userId);

            var ids = items.Select(i => i.itemId).ToList();
            var rows = await db.Query("player_item")
                .Select("player_item_id", "item_code", "row_type", "quantity", "slot")
                .Where("user_id", userId).WhereIn("player_item_id", ids)
                .GetAsync<PlayerItemBriefRow>(transaction);
            var byId = rows.ToDictionary(r => r.PlayerItemId);

            var equippedIds = (await db.Query("player_item_equipped")
                .Select("player_item_id")
                .Where("user_id", userId).WhereIn("player_item_id", ids)
                .GetAsync<long>(transaction)).ToHashSet();

            // 1) 검증: 소유(아이템 행)·미장착·수량.
            var inputs = new List<DismantleInput>();
            foreach (var (itemId, count) in items)
            {
                if (!byId.TryGetValue(itemId, out var row) || row.RowType != RowTypeItem)
                {
                    await transaction.RollbackAsync();
                    return DismantleOutcome.Fail(DismantleStatus.ItemNotFound);
                }

                if (equippedIds.Contains(itemId))
                {
                    await transaction.RollbackAsync();
                    return DismantleOutcome.Fail(DismantleStatus.ItemEquipped);
                }

                if (count < 1 || count > row.Quantity)
                {
                    await transaction.RollbackAsync();
                    return DismantleOutcome.Fail(DismantleStatus.InsufficientQuantity);
                }

                inputs.Add(new DismantleInput(itemId, row.ItemCode, count));
            }

            // 2) 보상 산출(마스터 등급 기반, 서비스).
            var reward = computeReward(cubeLevel, inputs);

            // 3) 아이템 차감/삭제.
            foreach (var (itemId, count) in items)
            {
                var row = byId[itemId];
                if (count >= row.Quantity)
                {
                    await db.Query("player_item").Where("player_item_id", itemId).DeleteAsync(transaction);
                }
                else
                {
                    await db.Query("player_item").Where("player_item_id", itemId)
                        .UpdateAsync(new { quantity = row.Quantity - count }, transaction);
                }
            }

            // 4) 골드 적립.
            await CreditGoldAsync(db, transaction, userId, reward.TotalGold, nowUnix);

            // 5) 큐브 경험치 반영.
            var (newLevel, newExp) = advanceCube(cubeLevel, cubeExp, reward.TotalCubeExp);
            await UpsertCubeAsync(db, transaction, userId, hasCube, newLevel, newExp);

            await transaction.CommitAsync();
            return new DismantleOutcome(DismantleStatus.Ok, reward.TotalGold, reward.TotalCubeExp);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<CraftOutcome> ApplyCraftAsync(
        long userId, RecipeDef recipe, int resultItemType, int resultStackMax, long cubeExpGain,
        Func<int, long, long, (int newLevel, long newExp)> advanceCube,
        long nowUnix)
    {
        await using var connection = _dbFactory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var db = _dbFactory.Create(connection);

            var (cubeLevel, cubeExp, hasCube) = await LoadCubeAsync(db, transaction, userId);

            // 1) 큐브 레벨 요구치.
            if (cubeLevel < recipe.ReqCubeLevel)
            {
                await transaction.RollbackAsync();
                return CraftOutcome.Fail(CraftStatus.CubeLevelInsufficient);
            }

            // 2) 비용 골드.
            var goldRow = await db.Query("player_item")
                .Select("player_item_id", "quantity")
                .Where("user_id", userId).Where("row_type", RowTypeCurrency).Where("item_code", GoldItemCode)
                .FirstOrDefaultAsync<ItemIdQtyRow>(transaction);
            long gold = goldRow?.Quantity ?? 0;
            if (gold < recipe.CostGold)
            {
                await transaction.RollbackAsync();
                return CraftOutcome.Fail(CraftStatus.InsufficientCurrency);
            }

            // 3) 재료 보유 확인(재료 코드별 총 보유 수량 ≥ 요구).
            foreach (var ing in recipe.Ingredients)
            {
                var matRows = await db.Query("player_item")
                    .Select("player_item_id", "quantity")
                    .Where("user_id", userId).Where("row_type", RowTypeItem).Where("item_code", ing.MaterialCode)
                    .GetAsync<ItemIdQtyRow>(transaction);
                long owned = matRows.Sum(m => m.Quantity);
                if (owned < ing.Quantity)
                {
                    await transaction.RollbackAsync();
                    return CraftOutcome.Fail(CraftStatus.RecipeNotMet);
                }
            }

            // 4) 골드 차감.
            if (recipe.CostGold > 0 && goldRow is not null)
            {
                await db.Query("player_item").Where("player_item_id", goldRow.PlayerItemId)
                    .UpdateAsync(new { quantity = gold - recipe.CostGold }, transaction);
            }

            // 5) 재료 차감.
            foreach (var ing in recipe.Ingredients)
            {
                await ConsumeMaterialAsync(db, transaction, userId, ing.MaterialCode, ing.Quantity);
            }

            // 6) 결과 아이템 지급.
            int capacity = await LoadCapacityAsync(db, transaction, userId);
            var used = await LoadUsedSlotsAsync(db, transaction, userId);
            bool stored = await StoreResultAsync(
                db, transaction, userId, recipe.ResultItemCode, recipe.ResultQuantity,
                resultItemType, resultStackMax, capacity, used, nowUnix);
            if (!stored)
            {
                await transaction.RollbackAsync();
                return CraftOutcome.Fail(CraftStatus.InventoryFull);
            }

            // 7) 큐브 경험치 반영.
            var (newLevel, newExp) = advanceCube(cubeLevel, cubeExp, cubeExpGain);
            await UpsertCubeAsync(db, transaction, userId, hasCube, newLevel, newExp);

            await transaction.CommitAsync();
            return new CraftOutcome(CraftStatus.Ok, newLevel, newExp);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    // ── 헬퍼 ──

    /// <summary>player_cube 상태(레벨·경험치)와 존재 여부를 읽는다. 행이 없으면 (1, 0, false).</summary>
    private static async Task<(int level, long exp, bool has)> LoadCubeAsync(QueryFactory db, DbTransaction tx, long userId)
    {
        var row = await db.Query("player_cube").Select("cube_level", "cube_exp")
            .Where("user_id", userId).FirstOrDefaultAsync<CubeStateRow>(tx);
        return row is null ? (1, 0L, false) : (row.CubeLevel, row.CubeExp, true);
    }

    /// <summary>큐브 상태를 갱신한다(행이 없으면 INSERT).</summary>
    private static async Task UpsertCubeAsync(QueryFactory db, DbTransaction tx, long userId, bool has, int level, long exp)
    {
        if (has)
        {
            await db.Query("player_cube").Where("user_id", userId)
                .UpdateAsync(new { cube_level = level, cube_exp = exp }, tx);
        }
        else
        {
            await db.Query("player_cube").InsertAsync(new { user_id = userId, cube_level = level, cube_exp = exp }, tx);
        }
    }

    /// <summary>인벤토리 용량(game_player.inventory_capacity). 계정 세이브가 없으면 0.</summary>
    private static async Task<int> LoadCapacityAsync(QueryFactory db, DbTransaction tx, long userId)
        => await db.Query("game_player").Select("inventory_capacity").Where("user_id", userId)
            .FirstOrDefaultAsync<int?>(tx) ?? 0;

    /// <summary>현재 점유 중인 인벤토리 칸(slot) 집합(slot이 NULL이 아닌 행).</summary>
    private static async Task<HashSet<int>> LoadUsedSlotsAsync(QueryFactory db, DbTransaction tx, long userId)
    {
        var slots = await db.Query("player_item").Select("slot")
            .Where("user_id", userId).WhereNotNull("slot").GetAsync<int>(tx);
        return slots.ToHashSet();
    }

    /// <summary>used 집합에서 [0, capacity) 범위의 가장 작은 빈 칸. 빈 칸이 없으면 -1.</summary>
    private static int FirstFreeSlot(HashSet<int> used, int capacity)
    {
        for (var i = 0; i < capacity; i++)
        {
            if (!used.Contains(i))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>골드(재화 행)를 upsert로 적립한다.</summary>
    private static async Task CreditGoldAsync(QueryFactory db, DbTransaction tx, long userId, long amount, long nowUnix)
    {
        if (amount <= 0)
        {
            return;
        }

        var goldRow = await db.Query("player_item").Select("player_item_id", "quantity")
            .Where("user_id", userId).Where("row_type", RowTypeCurrency).Where("item_code", GoldItemCode)
            .FirstOrDefaultAsync<ItemIdQtyRow>(tx);

        if (goldRow is null)
        {
            await db.Query("player_item").InsertAsync(new
            {
                user_id = userId,
                row_type = RowTypeCurrency,
                item_code = GoldItemCode,
                quantity = amount,
                slot = (int?)null,
                enhance_level = 0,
                acquired_at = nowUnix,
            }, tx);
        }
        else
        {
            await db.Query("player_item").Where("player_item_id", goldRow.PlayerItemId)
                .UpdateAsync(new { quantity = goldRow.Quantity + amount }, tx);
        }
    }

    /// <summary>재료(material_code)를 필요 수량만큼 여러 행에 걸쳐 차감한다(부족분은 호출 전에 검증됨).</summary>
    private static async Task ConsumeMaterialAsync(QueryFactory db, DbTransaction tx, long userId, int materialCode, long need)
    {
        var rows = await db.Query("player_item").Select("player_item_id", "quantity")
            .Where("user_id", userId).Where("row_type", RowTypeItem).Where("item_code", materialCode)
            .OrderBy("player_item_id")
            .GetAsync<ItemIdQtyRow>(tx);

        foreach (var row in rows)
        {
            if (need <= 0)
            {
                break;
            }

            long take = Math.Min(need, row.Quantity);
            if (take >= row.Quantity)
            {
                await db.Query("player_item").Where("player_item_id", row.PlayerItemId).DeleteAsync(tx);
            }
            else
            {
                await db.Query("player_item").Where("player_item_id", row.PlayerItemId)
                    .UpdateAsync(new { quantity = row.Quantity - take }, tx);
            }

            need -= take;
        }
    }

    /// <summary>
    /// 제작 결과 아이템을 적재한다. 재료(스택)면 기존 스택에 채운 뒤 남으면 새 행, 장비면 개당 1행씩 새 칸에 넣는다.
    /// 새 칸이 용량을 넘어 부족하면 false(호출측 롤백).
    /// </summary>
    private static async Task<bool> StoreResultAsync(
        QueryFactory db, DbTransaction tx, long userId, int itemCode, int quantity,
        int itemType, int stackMax, int capacity, HashSet<int> used, long nowUnix)
    {
        long remaining = quantity;

        // 재료(스택 가능): 기존 스택의 여유부터 채운다(새 칸 불필요).
        if (itemType == ItemTypeMaterial && stackMax > 1)
        {
            var stacks = await db.Query("player_item").Select("player_item_id", "quantity")
                .Where("user_id", userId).Where("row_type", RowTypeItem).Where("item_code", itemCode)
                .Where("quantity", "<", stackMax)
                .GetAsync<ItemIdQtyRow>(tx);

            foreach (var stack in stacks)
            {
                if (remaining <= 0)
                {
                    break;
                }

                long room = stackMax - stack.Quantity;
                long add = Math.Min(room, remaining);
                await db.Query("player_item").Where("player_item_id", stack.PlayerItemId)
                    .UpdateAsync(new { quantity = stack.Quantity + add }, tx);
                remaining -= add;
            }
        }

        // 남은 수량은 새 행으로. 장비는 1개당 1행, 재료는 stackMax씩 묶는다.
        int perRow = itemType == ItemTypeMaterial ? Math.Max(stackMax, 1) : 1;
        while (remaining > 0)
        {
            int slot = FirstFreeSlot(used, capacity);
            if (slot < 0)
            {
                return false; // 빈 칸 없음(용량 초과)
            }

            long put = Math.Min(perRow, remaining);
            await db.Query("player_item").InsertAsync(new
            {
                user_id = userId,
                row_type = RowTypeItem,
                item_code = itemCode,
                quantity = put,
                slot = slot,
                enhance_level = 0,
                acquired_at = nowUnix,
            }, tx);
            used.Add(slot);
            remaining -= put;
        }

        return true;
    }
}
