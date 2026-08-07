using System.Data.Common;
using GameServer.Data;
using GameServer.MasterData;
using SqlKata.Execution;
using TaskbarHero.Common.Dto;

namespace GameServer.Repositories;

// ── 뽑기(pull) ──

/// <summary>뽑기 처리 결과 상태(가챠 기획서 §6.5·6.7).</summary>
public enum GachaPullStatus
{
    Ok,
    InsufficientCurrency, // 비용 재화 부족(차감 전 검증이라 상태 변화 없음)
    PoolEmpty,            // 추첨된 등급 슬롯에 후보가 없음(마스터 결함) → 전체 롤백
    InventoryFull,        // 지급 아이템을 적재할 빈 칸 부족 → 전체 롤백(골드도 돌아온다)
}

/// <summary>회차별 뽑기 결과 1건(서버가 확정한 값). Quantity는 gacha_item_pool.quantity.</summary>
public sealed record GachaPullEntry(int Seq, int Grade, int ItemCode, int Quantity, bool PityApplied, bool Guaranteed);

/// <summary>
/// 뽑기 트랜잭션 결과. Entries는 회차별 결과(1연 1건 / 10연 multi_count건),
/// Counters는 갱신 후 천장 진행도(등급 → 누적 미획득 횟수)다.
/// </summary>
public sealed record GachaPullOutcome(GachaPullStatus Status)
{
    /// <summary>이번 뽑기의 기록 식별자(player_gacha_pull.pull_id). 기록 조회 커서와 같은 값.</summary>
    public long PullId { get; init; }

    /// <summary>뽑은 시각(Unix ts).</summary>
    public long PulledAt { get; init; }

    /// <summary>실제 차감한 비용(cost_single 또는 cost_multi).</summary>
    public long CostAmount { get; init; }

    /// <summary>차감 후 비용 재화 잔액.</summary>
    public long Balance { get; init; }

    public IReadOnlyList<GachaPullEntry> Entries { get; init; } = Array.Empty<GachaPullEntry>();

    /// <summary>갱신 후 천장 카운터(등급 → 누적 미획득 횟수). 천장 규칙이 있는 등급만.</summary>
    public IReadOnlyDictionary<int, int> Counters { get; init; } = new Dictionary<int, int>();

    /// <summary>가방 변경분(응답·캐시 갱신용, 인벤토리 기획서 §5.0).</summary>
    public InventoryDeltaDto Delta { get; init; } = new InventoryDeltaDto();

    public static GachaPullOutcome Fail(GachaPullStatus status) => new(status);
}

// ── 기록 조회(history) ──

/// <summary>기록 한 건(뽑기 요청 1건 = 부모 1행 + 자식 n행).</summary>
public sealed record GachaHistoryEntry(
    long PullId, int GachaCode, int PullType, int CostCurrencyCode, long CostAmount, long PulledAt,
    IReadOnlyList<GachaPullEntry> Items);

/// <summary>기록 페이지(커서 페이징). HasMore는 limit+1번째 행의 존재로 판정한다.</summary>
public sealed record GachaHistoryPage(IReadOnlyList<GachaHistoryEntry> Entries, long NextCursor, bool HasMore);

public interface IGachaRepository
{
    /// <summary>
    /// 배너별 천장 진행도(등급 → 누적 미획득 횟수)를 읽는다. 행이 없는 등급은 0으로 채운다(lazy 생성 전 상태).
    /// 배너 조회(§5.1)가 쓰는 읽기 전용 경로다.
    /// </summary>
    Task<IReadOnlyDictionary<int, int>> LoadCountersAsync(long userId, int gachaCode, IReadOnlyList<int> grades);

    /// <summary>
    /// 뽑기를 하나의 트랜잭션으로 처리한다(기획서 §6.5): 비용 검증·차감 → 카운터 조회 → 추첨(델리게이트) →
    /// 지급 → 카운터 UPSERT → 원장 적재. 중간 실패 시 전체 롤백하므로 골드만 빠지는 상태가 생기지 않는다.
    /// </summary>
    /// <param name="rollAll">
    /// 추첨 델리게이트. 입력은 현재 천장 카운터(등급 → 누적 미획득 횟수)이며, 회차별 결과 목록을 반환한다.
    /// null을 반환하면 후보 풀 부재(PoolEmpty)로 전체 롤백한다. 카운터 갱신도 이 델리게이트가 함께 수행한다.
    /// </param>
    /// <param name="itemLookup">item_code → (itemType, stackMax) 마스터 조회(적재 규칙 판정용).</param>
    Task<GachaPullOutcome> ApplyPullAsync(
        long userId, GachaBannerDef banner, int pullType, long cost,
        Func<IDictionary<int, int>, IReadOnlyList<GachaPullEntry>?> rollAll,
        Func<int, (int itemType, int stackMax)> itemLookup,
        long nowUnix);

    /// <summary>
    /// 뽑기 기록을 최신순 커서 페이징으로 조회한다(기획서 §6.6). gachaCode 0이면 전체,
    /// cursor 0이면 최신부터. 부모 페이지를 먼저 뽑고 자식은 pull_id 집합으로 한 번에 가져온다(쿼리 2회 고정).
    /// </summary>
    Task<GachaHistoryPage> GetHistoryAsync(long userId, int gachaCode, long cursor, int limit);
}

file sealed class ItemIdQtyRow
{
    public long PlayerItemId { get; set; }
    public long Quantity { get; set; }
}

file sealed class ItemIdQtySlotRow
{
    public long PlayerItemId { get; set; }
    public long Quantity { get; set; }
    public int? Slot { get; set; }
}

file sealed class GachaCounterRow
{
    public int Grade { get; set; }
    public int PityCount { get; set; }
}

file sealed class GachaPullRow
{
    public long PullId { get; set; }
    public int GachaCode { get; set; }
    public int PullType { get; set; }
    public int CostCurrencyCode { get; set; }
    public long CostAmount { get; set; }
    public long PulledAt { get; set; }
}

file sealed class GachaPullItemRow
{
    public long PullId { get; set; }
    public int Seq { get; set; }
    public int ItemCode { get; set; }
    public int Grade { get; set; }
    public int Quantity { get; set; }
    public int PityApplied { get; set; }
    public int Guaranteed { get; set; }
}

/// <summary>
/// 가챠 세이브 접근(player_gacha_counter·player_gacha_pull·player_gacha_pull_item + 비용 재화·지급 적재).
/// 쿼리는 SqlKata 빌더로만 작성하고 결과는 제네릭 매핑으로 POCO에 받는다(프로젝트 규칙).
/// </summary>
public sealed class GachaRepository : IGachaRepository
{
    private const int RowTypeItem = 1;
    private const int RowTypeCurrency = 2;

    private readonly GameDbFactory _dbFactory;

    /// <summary>세이브 DB 팩토리를 주입받는다.</summary>
    public GachaRepository(GameDbFactory dbFactory) => _dbFactory = dbFactory;

    /// <summary>배너별·등급별 천장 진행도를 읽는다(행이 없는 등급은 0). 읽기 전용이므로 트랜잭션을 쓰지 않는다.</summary>
    public async Task<IReadOnlyDictionary<int, int>> LoadCountersAsync(
        long userId, int gachaCode, IReadOnlyList<int> grades)
    {
        var result = new Dictionary<int, int>();
        foreach (var grade in grades)
        {
            result[grade] = 0;
        }

        if (grades.Count == 0)
        {
            return result;
        }

        using var db = _dbFactory.Create();
        var rows = await db.Query("player_gacha_counter")
            .Select("grade", "pity_count")
            .Where("user_id", userId).Where("gacha_code", gachaCode)
            .GetAsync<GachaCounterRow>();

        foreach (var row in rows)
        {
            if (result.ContainsKey(row.Grade))
            {
                result[row.Grade] = row.PityCount;
            }
        }

        return result;
    }

    /// <summary>
    /// 뽑기 트랜잭션(기획서 §6.5). 비용 차감을 추첨보다 앞에 두어 잔액 부족이 추첨 전에 끝나게 하고,
    /// 원장 적재를 같은 트랜잭션에 넣어 "지급됐는데 기록에 없는 뽑기"가 생기지 않게 한다.
    /// </summary>
    public async Task<GachaPullOutcome> ApplyPullAsync(
        long userId, GachaBannerDef banner, int pullType, long cost,
        Func<IDictionary<int, int>, IReadOnlyList<GachaPullEntry>?> rollAll,
        Func<int, (int itemType, int stackMax)> itemLookup,
        long nowUnix)
    {
        await using var connection = _dbFactory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var db = _dbFactory.Create(connection);

            // 1) 비용 재화 조회·검증(차감 전이라 실패해도 상태 변화가 없다).
            var currencyRow = await db.Query("player_item")
                .Select("player_item_id", "quantity")
                .Where("user_id", userId).Where("row_type", RowTypeCurrency)
                .Where("item_code", banner.CostCurrencyCode)
                .FirstOrDefaultAsync<ItemIdQtyRow>(transaction);

            long held = currencyRow?.Quantity ?? 0;
            if (currencyRow is null || held < cost)
            {
                await transaction.RollbackAsync();
                return GachaPullOutcome.Fail(GachaPullStatus.InsufficientCurrency);
            }

            // 2) 비용 차감.
            long balance = held - cost;
            await db.Query("player_item").Where("player_item_id", currencyRow.PlayerItemId)
                .UpdateAsync(new { quantity = balance }, transaction);

            // 3) 천장 카운터 조회(행이 있는 등급만 값을 채우고 나머지는 0).
            var pityGrades = banner.PityGrades;
            var counters = new Dictionary<int, int>();
            foreach (var grade in pityGrades)
            {
                counters[grade] = 0;
            }

            var existing = new HashSet<int>();
            if (pityGrades.Count > 0)
            {
                var rows = await db.Query("player_gacha_counter")
                    .Select("grade", "pity_count")
                    .Where("user_id", userId).Where("gacha_code", banner.GachaCode)
                    .GetAsync<GachaCounterRow>(transaction);
                foreach (var row in rows)
                {
                    existing.Add(row.Grade);
                    if (counters.ContainsKey(row.Grade))
                    {
                        counters[row.Grade] = row.PityCount;
                    }
                }
            }

            // 4) 추첨(서버 권위 RNG). 델리게이트가 회차별 결과를 만들고 counters를 갱신한다.
            var entries = rollAll(counters);
            if (entries is null || entries.Count == 0)
            {
                await transaction.RollbackAsync();
                return GachaPullOutcome.Fail(GachaPullStatus.PoolEmpty);
            }

            // 5) 지급. 같은 아이템이 여러 회차에 나오면 코드별로 합산해 한 번에 적재한다(스택 병합이 한 번에 이뤄진다).
            int capacity = await InventorySlotAllocator.LoadCapacityAsync(db, transaction, userId);
            var used = await InventorySlotAllocator.LoadUsedSlotsAsync(db, transaction, userId);
            var delta = new InventoryDeltaDto();

            var grouped = entries
                .GroupBy(e => e.ItemCode)
                .Select(g => (ItemCode: g.Key, Quantity: g.Sum(e => (long)e.Quantity)))
                .OrderBy(g => g.ItemCode)
                .ToList();

            foreach (var (itemCode, quantity) in grouped)
            {
                var (_, stackMax) = itemLookup(itemCode);
                bool stored = await StoreItemAsync(
                    db, transaction, userId, itemCode, quantity, stackMax, capacity, used, nowUnix, delta);
                if (!stored)
                {
                    await transaction.RollbackAsync();
                    return GachaPullOutcome.Fail(GachaPullStatus.InventoryFull);
                }
            }

            // 6) 천장 카운터 UPSERT(회차마다가 아니라 최종값 한 번).
            foreach (var (grade, count) in counters)
            {
                if (existing.Contains(grade))
                {
                    await db.Query("player_gacha_counter")
                        .Where("user_id", userId).Where("gacha_code", banner.GachaCode).Where("grade", grade)
                        .UpdateAsync(new { pity_count = count, updated_at = nowUnix }, transaction);
                }
                else
                {
                    await db.Query("player_gacha_counter").InsertAsync(new
                    {
                        user_id = userId,
                        gacha_code = banner.GachaCode,
                        grade,
                        pity_count = count,
                        updated_at = nowUnix,
                    }, transaction);
                }
            }

            // 7) 원장 적재(부모 1행 + 자식 n행). 같은 트랜잭션이라 감사 근거가 지급과 함께 확정된다.
            long pullId = await db.Query("player_gacha_pull").InsertGetIdAsync<long>(new
            {
                user_id = userId,
                gacha_code = banner.GachaCode,
                pull_type = pullType,
                cost_currency_code = banner.CostCurrencyCode,
                cost_amount = cost,
                pulled_at = nowUnix,
            }, transaction);

            foreach (var entry in entries)
            {
                await db.Query("player_gacha_pull_item").InsertAsync(new
                {
                    pull_id = pullId,
                    seq = entry.Seq,
                    item_code = entry.ItemCode,
                    grade = entry.Grade,
                    quantity = entry.Quantity,
                    pity_applied = entry.PityApplied ? 1 : 0,
                    guaranteed = entry.Guaranteed ? 1 : 0,
                }, transaction);
            }

            await transaction.CommitAsync();
            return new GachaPullOutcome(GachaPullStatus.Ok)
            {
                PullId = pullId,
                PulledAt = nowUnix,
                CostAmount = cost,
                Balance = balance,
                Entries = entries,
                Counters = counters,
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
    /// 기록을 최신순 커서 페이징으로 조회한다. `pull_id &lt; cursor` 조건이 (user_id, pull_id) 인덱스를 타므로
    /// 페이지 깊이와 무관하게 비용이 일정하다. limit+1행을 읽어 hasMore를 판정하고 응답에서 잘라낸다.
    /// </summary>
    public async Task<GachaHistoryPage> GetHistoryAsync(long userId, int gachaCode, long cursor, int limit)
    {
        using var db = _dbFactory.Create();

        var query = db.Query("player_gacha_pull")
            .Select("pull_id", "gacha_code", "pull_type", "cost_currency_code", "cost_amount", "pulled_at")
            .Where("user_id", userId);

        if (gachaCode > 0)
        {
            query = query.Where("gacha_code", gachaCode);
        }

        if (cursor > 0)
        {
            query = query.Where("pull_id", "<", cursor);
        }

        var parents = (await query.OrderByDesc("pull_id").Limit(limit + 1).GetAsync<GachaPullRow>()).ToList();

        bool hasMore = parents.Count > limit;
        if (hasMore)
        {
            parents.RemoveRange(limit, parents.Count - limit);
        }

        if (parents.Count == 0)
        {
            return new GachaHistoryPage(Array.Empty<GachaHistoryEntry>(), 0, false);
        }

        // 자식은 조회된 pull_id 집합으로 한 번에 가져와 메모리에서 묶는다(N+1 없음).
        var pullIds = parents.Select(p => p.PullId).ToList();
        var childRows = await db.Query("player_gacha_pull_item")
            .Select("pull_id", "seq", "item_code", "grade", "quantity", "pity_applied", "guaranteed")
            .WhereIn("pull_id", pullIds)
            .OrderBy("pull_id", "seq")
            .GetAsync<GachaPullItemRow>();

        var itemsByPull = new Dictionary<long, List<GachaPullEntry>>();
        foreach (var row in childRows)
        {
            if (!itemsByPull.TryGetValue(row.PullId, out var list))
            {
                list = new List<GachaPullEntry>();
                itemsByPull[row.PullId] = list;
            }

            list.Add(new GachaPullEntry(
                row.Seq, row.Grade, row.ItemCode, row.Quantity, row.PityApplied == 1, row.Guaranteed == 1));
        }

        var entries = parents.Select(p => new GachaHistoryEntry(
            p.PullId, p.GachaCode, p.PullType, p.CostCurrencyCode, p.CostAmount, p.PulledAt,
            itemsByPull.TryGetValue(p.PullId, out var items) ? items : new List<GachaPullEntry>())).ToList();

        long nextCursor = hasMore ? entries[^1].PullId : 0;
        return new GachaHistoryPage(entries, nextCursor, hasMore);
    }

    // ── 헬퍼 ──

    /// <summary>
    /// 지급 아이템을 적재한다. 적재 규칙은 스택 상한(stack_max)만으로 결정되므로 재료·소모품 모두 병합 대상이고,
    /// 장비는 stack_max=1이라 자연히 1개당 1행이 된다. 빈 칸이 부족하면 false(호출측 전체 롤백).
    /// 병합된 스택과 새 행을 <paramref name="delta"/>에 기록해 응답·캐시 갱신에 쓴다(인벤토리 기획서 §5.0).
    /// </summary>
    private static async Task<bool> StoreItemAsync(
        QueryFactory db, DbTransaction tx, long userId, int itemCode, long quantity,
        int stackMax, int capacity, HashSet<int> used, long nowUnix, InventoryDeltaDto delta)
    {
        long remaining = quantity;

        if (stackMax > 1)
        {
            var stacks = await db.Query("player_item").Select("player_item_id", "quantity", "slot")
                .Where("user_id", userId).Where("row_type", RowTypeItem).Where("item_code", itemCode)
                .Where("enhance_level", 0)
                .Where("quantity", "<", stackMax)
                .OrderBy("player_item_id")
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

        int perRow = Math.Max(stackMax, 1);
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
                slot,
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
