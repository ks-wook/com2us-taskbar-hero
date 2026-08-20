using System.Data.Common;
using GameServer.Data;
using GameServer.MasterData;
using GameServer.Models;
using GameServer.Repositories.GameDb.Interfaces;
using SqlKata.Execution;
using TaskbarHero.Common.Dto;

namespace GameServer.Repositories.GameDb;

// ── 합성(combine) ──

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
    /// <summary>가방 변경분(5.0). 커밋 전에 확정된 값이라 응답 조립·캐시 갱신에 추가 조회가 필요 없다.</summary>
    public InventoryDeltaDto Delta { get; init; } = new InventoryDeltaDto();

    public static CombineOutcome Fail(CombineStatus status) => new(status, 0, 0, 0, 0, 0);
}

// ── 분해(dismantle) ──

/// <summary>분해 입력(아이템 코드 + 분해 수량). 골드·경험치 산출은 서비스 델리게이트가 수행.</summary>
public sealed record DismantleInput(long ItemId, int ItemCode, int Count);

/// <summary>분해 보상(서비스 델리게이트 반환): 획득 골드·큐브 경험치 합계.</summary>
public sealed record DismantleReward(long TotalGold, long TotalCubeExp);

/// <summary>분해 트랜잭션 결과. Gold·CubeExp는 이번 분해로 획득한 증가분.</summary>
public sealed record DismantleOutcome(DismantleStatus Status, long Gold, long CubeExp)
{
    /// <summary>가방 변경분(5.0). 커밋 전에 확정된 값이라 응답 조립·캐시 갱신에 추가 조회가 필요 없다.</summary>
    public InventoryDeltaDto Delta { get; init; } = new InventoryDeltaDto();

    /// <summary>갱신 후 큐브 레벨(응답 cube).</summary>
    public int NewCubeLevel { get; init; }

    /// <summary>갱신 후 큐브 누적 경험치(응답 cube). 위 <see cref="CubeExp"/>는 이번 증가분이다.</summary>
    public long NewCubeExp { get; init; }

    /// <summary>적립 후 골드 잔액(응답 balance).</summary>
    public long GoldBalance { get; init; }

    public static DismantleOutcome Fail(DismantleStatus status) => new(status, 0, 0);
}

// ── 제작(craft) ──

/// <summary>제작 트랜잭션 결과. CubeLevel·CubeExp는 갱신 후 큐브 상태.</summary>
public sealed record CraftOutcome(CraftStatus Status, int CubeLevel, long CubeExp)
{
    /// <summary>가방 변경분(5.0). 커밋 전에 확정된 값이라 응답 조립·캐시 갱신에 추가 조회가 필요 없다.</summary>
    public InventoryDeltaDto Delta { get; init; } = new InventoryDeltaDto();

    /// <summary>비용 차감 후 골드 잔액(응답 balance).</summary>
    public long GoldBalance { get; init; }

    public static CraftOutcome Fail(CraftStatus status) => new(status, 0, 0);
}

/// <summary>큐브(합성·분해·제작) 세이브 접근 계층(taskbar_hero_game). SqlKata 쿼리 빌더 + 제네릭 매핑만 사용한다(dynamic 금지).</summary>
public sealed class CubeRepository : ICubeRepository
{
    private const int RowTypeItem = 1;
    private const int RowTypeCurrency = 2;
    private const int GoldItemCode = 1;
    private const int ItemTypeMaterial = 2;

    private readonly GameDbFactory _dbFactory;
    private readonly ICubeLevelCalculator _cubeLevel;

    /// <summary>세이브 DB 커넥션 팩토리를 주입받는다.</summary>
    public CubeRepository(GameDbFactory dbFactory, ICubeLevelCalculator cubeLevel)
    {
        _dbFactory = dbFactory;
        _cubeLevel = cubeLevel;
    }

    /// <summary>
    /// 아이템 합성(입력 여러 개 → 상위 등급 결과 1개)을 단일 커넥션의 단일 트랜잭션으로 적용한다.
    /// 검증·판정 실패(ItemNotFound·ItemEquipped·RecipeNotMet)는 즉시 롤백 후 Fail 상태로 반환하고, 예외는 롤백 후 전파한다.
    /// </summary>
    /// <remarks>
    /// 한 트랜잭션으로 묶는 작업(하나라도 실패하면 전부 롤백 — 입력만 사라지고 결과가 안 나오는 상태를 막는다):
    /// <para>1) player_cube SELECT — 큐브 레벨·경험치 로드(행이 없으면 레벨 1·경험치 0으로 취급)</para>
    /// <para>2) player_item SELECT — 입력 아이템 소유 확인(조회 개수 불일치·재화 행 포함 → ItemNotFound)</para>
    /// <para>3) player_item_equipped SELECT — 입력 중 장착 중인 아이템이 있으면 소모 거부(ItemEquipped)</para>
    /// <para>4) decide 델리게이트 — 마스터 검증(등급·슬롯·클래스·개수)과 결과 아이템·큐브 경험치 산출(DB 접근 없음)</para>
    /// <para>5) player_item DELETE — 입력 아이템 전량 삭제</para>
    /// <para>6) player_item INSERT — 결과 아이템 생성(4)에서 빈 칸이 생기므로 용량은 항상 충족)</para>
    /// <para>7) player_cube upsert — 합성으로 얻은 큐브 경험치 반영 및 레벨 재계산</para>
    /// </remarks>
    public async Task<CombineOutcome> ApplyCombineAsync(
        long userId, IReadOnlyList<long> itemIds,
        Func<int, IReadOnlyList<CombineInput>, CombineDecision> decide,
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
            int capacity = await InventorySlotAllocator.LoadCapacityAsync(db, transaction, userId);
            var used = await InventorySlotAllocator.LoadUsedSlotsAsync(db, transaction, userId);
            if (!InventorySlotAllocator.TryFirstFree(used, capacity, out int slot))
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
            var (newLevel, newExp) = _cubeLevel.Calculate(cubeLevel, cubeExp, decision.CubeExpGain);
            await UpsertCubeAsync(db, transaction, userId, hasCube, newLevel, newExp);

            // 7) 가방 변경분(5.0): 입력 전량이 사라지고 결과 아이템 1개가 slot 칸에 생긴다.
            var delta = new InventoryDeltaDto();
            delta.removed.AddRange(itemIds);
            delta.upserted.Add(new InventoryItemDto
            {
                itemId = resultItemId,
                slot = slot,
                itemCode = decision.ResultItemCode,
                quantity = 1,
                enhanceLevel = 0,
            });

            await transaction.CommitAsync();
            return new CombineOutcome(CombineStatus.Ok, resultItemId, decision.ResultItemCode, decision.ResultGrade, newLevel, newExp)
            {
                Delta = delta,
            };
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// 아이템 분해(아이템 소모 → 골드·큐브 경험치 획득)를 단일 커넥션의 단일 트랜잭션으로 적용한다.
    /// 검증 실패(ItemNotFound·ItemEquipped·InsufficientQuantity)는 즉시 롤백 후 Fail 상태로 반환하고, 예외는 롤백 후 전파한다.
    /// </summary>
    /// <remarks>
    /// 한 트랜잭션으로 묶는 작업(하나라도 실패하면 전부 롤백 — 아이템만 차감되고 보상이 안 들어가는 상태를 막는다):
    /// <para>1) player_cube SELECT — 큐브 레벨·경험치 로드(보상 산출 입력)</para>
    /// <para>2) player_item SELECT — 대상 아이템 일괄 조회(id→행 사전화)</para>
    /// <para>3) player_item_equipped SELECT — 장착 중 아이템 판별용 id 집합 확보</para>
    /// <para>4) 요청 항목별 검증 — 소유(아이템 행)·미장착·요청 수량 ≤ 보유 수량</para>
    /// <para>5) computeReward 델리게이트 — 마스터 등급 기준 골드·큐브 경험치 합계 산출(DB 접근 없음)</para>
    /// <para>6) player_item UPDATE/DELETE — 수량 차감, 전량 분해면 행 삭제</para>
    /// <para>7) player_item(재화 행) upsert — 분해 보상 골드 적립</para>
    /// <para>8) player_cube upsert — 분해로 얻은 큐브 경험치 반영 및 레벨 재계산</para>
    /// </remarks>
    public async Task<DismantleOutcome> ApplyDismantleAsync(
        long userId, IReadOnlyList<(long itemId, int count)> items,
        Func<int, IReadOnlyList<DismantleInput>, DismantleReward> computeReward,
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

            // 3) 아이템 차감/삭제. 같은 루프에서 가방 변경분(5.0)을 모은다.
            var delta = new InventoryDeltaDto();
            foreach (var (itemId, count) in items)
            {
                var row = byId[itemId];
                if (count >= row.Quantity)
                {
                    await db.Query("player_item").Where("player_item_id", itemId).DeleteAsync(transaction);
                    delta.removed.Add(itemId);
                }
                else
                {
                    long remaining = row.Quantity - count;
                    await db.Query("player_item").Where("player_item_id", itemId)
                        .UpdateAsync(new { quantity = remaining }, transaction);
                    delta.upserted.Add(new InventoryItemDto
                    {
                        itemId = itemId,
                        slot = row.Slot ?? 0,
                        itemCode = row.ItemCode,
                        quantity = remaining,
                        enhanceLevel = 0,
                    });
                }
            }

            // 4) 골드 적립(적립 후 잔액을 응답에 담는다).
            long goldBalance = await CreditGoldAsync(db, transaction, userId, reward.TotalGold, nowUnix);

            // 5) 큐브 경험치 반영.
            var (newLevel, newExp) = _cubeLevel.Calculate(cubeLevel, cubeExp, reward.TotalCubeExp);
            await UpsertCubeAsync(db, transaction, userId, hasCube, newLevel, newExp);

            await transaction.CommitAsync();
            return new DismantleOutcome(DismantleStatus.Ok, reward.TotalGold, reward.TotalCubeExp)
            {
                Delta = delta,
                NewCubeLevel = newLevel,
                NewCubeExp = newExp,
                GoldBalance = goldBalance,
            };
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// 아이템 제작(골드·재료 소모 → 결과 아이템 획득)을 단일 커넥션의 단일 트랜잭션으로 적용한다.
    /// 검증 실패(CubeLevelInsufficient·InsufficientCurrency·RecipeNotMet·InventoryFull)는 즉시 롤백 후
    /// Fail 상태로 반환하고, 예외는 롤백 후 전파한다. 레시피 존재 여부는 호출 전(서비스·마스터)에서 검증한다.
    /// </summary>
    /// <remarks>
    /// 한 트랜잭션으로 묶는 작업(하나라도 실패하면 전부 롤백 — 골드·재료만 사라지는 상태를 막는다):
    /// <para>1) player_cube SELECT — 큐브 레벨·경험치 로드</para>
    /// <para>2) 큐브 레벨 요구치 확인(부족 → CubeLevelInsufficient)</para>
    /// <para>3) player_item(재화 행) SELECT — 비용 골드 잔액 확인(부족 → InsufficientCurrency)</para>
    /// <para>4) player_item SELECT — 레시피 재료별 총 보유 수량 확인(부족 → RecipeNotMet)</para>
    /// <para>5) player_item UPDATE — 비용 골드 차감</para>
    /// <para>6) player_item UPDATE/DELETE — 재료를 여러 행에 걸쳐 필요 수량만큼 차감</para>
    /// <para>7) player_item UPDATE/INSERT — 결과 아이템 적재(재료면 스택 병합 후 잔량 새 행, 장비면 개당 1행. 칸 부족 → InventoryFull)</para>
    /// <para>8) player_cube upsert — 제작으로 얻은 큐브 경험치 반영 및 레벨 재계산</para>
    /// </remarks>
    public async Task<CraftOutcome> ApplyCraftAsync(
        long userId, RecipeDef recipe, int resultItemType, int resultStackMax, long cubeExpGain,
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
            long goldBalance = gold;
            if (recipe.CostGold > 0 && goldRow is not null)
            {
                goldBalance = gold - recipe.CostGold;
                await db.Query("player_item").Where("player_item_id", goldRow.PlayerItemId)
                    .UpdateAsync(new { quantity = goldBalance }, transaction);
            }

            // 5) 재료 차감. 차감 결과는 가방 변경분(5.0)에 누적된다.
            var delta = new InventoryDeltaDto();
            foreach (var ing in recipe.Ingredients)
            {
                await ConsumeMaterialAsync(db, transaction, userId, ing.MaterialCode, ing.Quantity, delta);
            }

            // 6) 결과 아이템 지급(생성·병합 결과도 같은 변경분에 누적).
            int capacity = await InventorySlotAllocator.LoadCapacityAsync(db, transaction, userId);
            var used = await InventorySlotAllocator.LoadUsedSlotsAsync(db, transaction, userId);
            bool stored = await StoreResultAsync(
                db, transaction, userId, recipe.ResultItemCode, recipe.ResultQuantity,
                resultItemType, resultStackMax, capacity, used, nowUnix, delta);
            if (!stored)
            {
                await transaction.RollbackAsync();
                return CraftOutcome.Fail(CraftStatus.InventoryFull);
            }

            // 7) 큐브 경험치 반영.
            var (newLevel, newExp) = _cubeLevel.Calculate(cubeLevel, cubeExp, cubeExpGain);
            await UpsertCubeAsync(db, transaction, userId, hasCube, newLevel, newExp);

            await transaction.CommitAsync();
            return new CraftOutcome(CraftStatus.Ok, newLevel, newExp) { Delta = delta, GoldBalance = goldBalance };
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

    /// <summary>골드(재화 행)를 upsert로 적립한다.</summary>
    private static async Task<long> CreditGoldAsync(QueryFactory db, DbTransaction tx, long userId, long amount, long nowUnix)
    {
        var goldRow = await db.Query("player_item").Select("player_item_id", "quantity")
            .Where("user_id", userId).Where("row_type", RowTypeCurrency).Where("item_code", GoldItemCode)
            .FirstOrDefaultAsync<ItemIdQtyRow>(tx);

        if (amount <= 0)
        {
            return goldRow?.Quantity ?? 0; // 적립할 것이 없어도 응답에 담을 현재 잔액은 돌려준다.
        }

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
            return amount;
        }

        long newBalance = goldRow.Quantity + amount;
        await db.Query("player_item").Where("player_item_id", goldRow.PlayerItemId)
            .UpdateAsync(new { quantity = newBalance }, tx);
        return newBalance;
    }

    /// <summary>
    /// 재료(material_code)를 필요 수량만큼 여러 행에 걸쳐 차감한다(부족분은 호출 전에 검증됨).
    /// 사라진 행과 수량이 줄어든 행을 <paramref name="delta"/>에 기록해 응답·캐시 갱신에 쓴다(5.0).
    /// </summary>
    private static async Task ConsumeMaterialAsync(
        QueryFactory db, DbTransaction tx, long userId, int materialCode, long need, InventoryDeltaDto delta)
    {
        var rows = await db.Query("player_item").Select("player_item_id", "quantity", "slot")
            .Where("user_id", userId).Where("row_type", RowTypeItem).Where("item_code", materialCode)
            .OrderBy("player_item_id")
            .GetAsync<ItemIdQtySlotRow>(tx);

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
                delta.removed.Add(row.PlayerItemId);
            }
            else
            {
                long remaining = row.Quantity - take;
                await db.Query("player_item").Where("player_item_id", row.PlayerItemId)
                    .UpdateAsync(new { quantity = remaining }, tx);
                delta.upserted.Add(new InventoryItemDto
                {
                    itemId = row.PlayerItemId,
                    slot = row.Slot ?? 0,
                    itemCode = materialCode,
                    quantity = remaining,
                    enhanceLevel = 0,
                });
            }

            need -= take;
        }
    }

    /// <summary>
    /// 제작 결과 아이템을 적재한다. 재료(스택)면 기존 스택에 채운 뒤 남으면 새 행, 장비면 개당 1행씩 새 칸에 넣는다.
    /// 새 칸이 용량을 넘어 부족하면 false(호출측 롤백).
    /// 병합된 스택과 새로 만든 행을 <paramref name="delta"/>에 기록해 응답·캐시 갱신에 쓴다(5.0).
    /// </summary>
    private static async Task<bool> StoreResultAsync(
        QueryFactory db, DbTransaction tx, long userId, int itemCode, int quantity,
        int itemType, int stackMax, int capacity, HashSet<int> used, long nowUnix, InventoryDeltaDto delta)
    {
        long remaining = quantity;

        // 재료(스택 가능): 기존 스택의 여유부터 채운다(새 칸 불필요).
        if (itemType == ItemTypeMaterial && stackMax > 1)
        {
            var stacks = await db.Query("player_item").Select("player_item_id", "quantity", "slot")
                .Where("user_id", userId).Where("row_type", RowTypeItem).Where("item_code", itemCode)
                .Where("quantity", "<", stackMax)
                .GetAsync<ItemIdQtySlotRow>(tx);

            foreach (var stack in stacks)
            {
                if (remaining <= 0)
                {
                    break;
                }

                long room = stackMax - stack.Quantity;
                long add = Math.Min(room, remaining);
                long merged = stack.Quantity + add;
                await db.Query("player_item").Where("player_item_id", stack.PlayerItemId)
                    .UpdateAsync(new { quantity = merged }, tx);
                delta.upserted.Add(new InventoryItemDto
                {
                    itemId = stack.PlayerItemId,
                    slot = stack.Slot ?? 0,
                    itemCode = itemCode,
                    quantity = merged,
                    enhanceLevel = 0,
                });
                remaining -= add;
            }
        }

        // 남은 수량은 새 행으로. 장비는 1개당 1행, 재료는 stackMax씩 묶는다.
        int perRow = itemType == ItemTypeMaterial ? Math.Max(stackMax, 1) : 1;
        while (remaining > 0)
        {
            if (!InventorySlotAllocator.TryFirstFree(used, capacity, out int slot))
            {
                return false; // 빈 칸 없음(용량 초과)
            }

            long put = Math.Min(perRow, remaining);
            long newItemId = await db.Query("player_item").InsertGetIdAsync<long>(new
            {
                user_id = userId,
                row_type = RowTypeItem,
                item_code = itemCode,
                quantity = put,
                slot = slot,
                enhance_level = 0,
                acquired_at = nowUnix,
            }, tx);
            delta.upserted.Add(new InventoryItemDto
            {
                itemId = newItemId,
                slot = slot,
                itemCode = itemCode,
                quantity = put,
                enhanceLevel = 0,
            });
            used.Add(slot);
            remaining -= put;
        }

        return true;
    }
}
