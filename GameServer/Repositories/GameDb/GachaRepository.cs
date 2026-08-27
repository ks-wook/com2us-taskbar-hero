using System.Data.Common;
using GameServer.MasterData;
using GameServer.Models;
using GameServer.Repositories.GameDb.Interfaces;
using SqlKata.Execution;
using TaskbarHero.Common.Dto;

namespace GameServer.Repositories.GameDb;

// ── 뽑기(pull) ──

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

/// <summary>
/// 가챠 세이브 접근(player_gacha_counter·player_gacha_pull·player_gacha_pull_item + 비용 재화·지급 적재).
/// 쿼리는 SqlKata 빌더로만 작성하고 결과는 제네릭 매핑으로 POCO에 받는다(프로젝트 규칙).
/// </summary>
public sealed class GachaRepository : GameDbBase, IGachaRepository
{
    private readonly IItemLookup _itemLookup;

    /// <summary>세이브 DB 커넥션 팩토리를 기반 클래스로 전달한다.</summary>
    public GachaRepository(GameDbFactory dbFactory, IItemLookup itemLookup) : base(dbFactory)
    {
        _itemLookup = itemLookup;
    }

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

        using var db = Db();
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
        long nowUnix)
        => await TransactionAsync<GachaPullOutcome>(async (db, transaction) =>
        {
            // 1~2) 비용 차감. 조회·검증·차감이 한 문장이라 잔액을 읽은 뒤 차감하기 전에 다른 요청이
            //      끼어드는 구간이 없다(부족하면 아무것도 바뀌지 않는다).
            long? debited = await TryDebitCurrencyAsync(
                db, transaction, userId, banner.CostCurrencyCode, cost);
            if (debited is null)
            {
                return TxResult<GachaPullOutcome>.Rollback(GachaPullOutcome.Fail(GachaPullStatus.InsufficientCurrency));
            }

            long balance = debited.Value;

            // 3) 천장 카운터 조회(행이 있는 등급만 값을 채우고 나머지는 0).
            var pityGrades = banner.PityGrades;
            var counters = new Dictionary<int, int>();
            foreach (var grade in pityGrades)
            {
                counters[grade] = 0;
            }

            var existing = new HashSet<int>();

            // 추첨이 counters를 바꾸므로, 갱신 조건에 쓸 '읽은 값'을 따로 남겨 둔다.
            var observedCounts = new Dictionary<int, int>();
            if (pityGrades.Count > 0)
            {
                // **잠금 조회** — 카운터가 계산의 입력이라 최신값이어야 한다.
                var rows = await db.SelectAsync<GachaCounterRow>(
                    """
                    SELECT grade, pity_count FROM player_gacha_counter
                     WHERE user_id = @userId AND gacha_code = @gachaCode ORDER BY grade FOR UPDATE
                    """,
                    new { userId, gachaCode = banner.GachaCode }, transaction);
                foreach (var row in rows)
                {
                    existing.Add(row.Grade);
                    if (counters.ContainsKey(row.Grade))
                    {
                        counters[row.Grade] = row.PityCount;
                        observedCounts[row.Grade] = row.PityCount;
                    }
                }
            }

            // 4) 추첨(서버 권위 RNG). 델리게이트가 회차별 결과를 만들고 counters를 갱신한다.
            var entries = rollAll(counters);
            if (entries is null || entries.Count == 0)
            {
                return TxResult<GachaPullOutcome>.Rollback(GachaPullOutcome.Fail(GachaPullStatus.PoolEmpty));
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
                var stackMax = _itemLookup.Stacking(itemCode).StackMax;
                bool stored = await StoreItemAsync(
                    db, transaction, userId, itemCode, quantity, stackMax, capacity, used, nowUnix, delta);
                if (!stored)
                {
                    return TxResult<GachaPullOutcome>.Rollback(GachaPullOutcome.Fail(GachaPullStatus.InventoryFull));
                }
            }

            // 6) 천장 카운터 UPSERT(회차마다가 아니라 최종값 한 번).
            foreach (var (grade, count) in counters)
            {
                if (existing.Contains(grade))
                {
                    // 읽은 카운터 값 그대로일 때만 쓴다. 조건이 없으면 같은 계정의 뽑기 둘이 겹칠 때 한쪽의
                    // 카운터 진행이 사라져 천장이 늦게 온다.
                    var changed = await db.Query("player_gacha_counter")
                        .Where("user_id", userId).Where("gacha_code", banner.GachaCode).Where("grade", grade)
                        .Where("pity_count", observedCounts.GetValueOrDefault(grade))
                        .UpdateAsync(new { pity_count = count, updated_at = nowUnix }, transaction);
                    if (changed == 0)
                    {
                        throw new ConcurrencyConflictException("천장 카운터");
                    }
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

            return TxResult<GachaPullOutcome>.Commit(new GachaPullOutcome(GachaPullStatus.Ok)
            {
                PullId = pullId,
                PulledAt = nowUnix,
                CostAmount = cost,
                Balance = balance,
                Entries = entries,
                Counters = counters,
                Delta = delta,
            });
        });

    /// <summary>
    /// 기록을 최신순 커서 페이징으로 조회한다. `pull_id &lt; cursor` 조건이 (user_id, pull_id) 인덱스를 타므로
    /// 페이지 깊이와 무관하게 비용이 일정하다. limit+1행을 읽어 hasMore를 판정하고 응답에서 잘라낸다.
    /// </summary>
    public async Task<GachaHistoryPage> GetHistoryAsync(long userId, int gachaCode, long cursor, int limit)
    {
        using var db = Db();

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
            // **잠금 조회** — 합칠 스택의 수량이 계산의 입력이라 최신값이어야 한다. 잠금 없는 조회는
            // 트랜잭션이 처음 읽은 시점의 스냅샷을 계속 보므로, 그 사이 다른 요청이 같은 스택을 채웠어도
            // 옛 수량이 보여 아래 조건부 갱신이 매번 어긋난다.
            var stacks = await db.SelectAsync<ItemIdQtySlotRow>(
                """
                SELECT player_item_id, quantity, slot FROM player_item
                 WHERE user_id = @userId AND row_type = @rowType AND item_code = @itemCode
                   AND enhance_level = 0 AND quantity < @stackMax
                 ORDER BY player_item_id FOR UPDATE
                """,
                new { userId, rowType = Constants.PlayerItemRow.Item, itemCode, stackMax }, tx);

            foreach (var stack in stacks)
            {
                if (remaining <= 0)
                {
                    break;
                }

                long room = stackMax - stack.Quantity;
                long add = Math.Min(room, remaining);
                long merged = stack.Quantity + add;
                // 읽은 수량 그대로일 때만 합친다. 조건이 없으면 같은 스택에 두 요청이 동시에 합칠 때
                // 한쪽 수량이 사라진다.
                var mergedRows = await db.Query("player_item")
                    .Where("player_item_id", stack.PlayerItemId).Where("quantity", stack.Quantity)
                    .UpdateAsync(new { quantity = merged }, tx);
                if (mergedRows == 0)
                {
                    throw new ConcurrencyConflictException("아이템 스택");
                }
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
                row_type = Constants.PlayerItemRow.Item,
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

    /// <summary>
    /// 재화 행(<c>player_item</c>, <c>row_type=2</c>)의 <c>player_item_id</c>를 찾는다(없으면 null).
    /// 잔액이 아니라 <b>행 번호</b>만 읽으므로 이 조회는 낡지 않는다 — 재화 행은 세이브를 만들 때 함께 만들어지고
    /// 이후 지워지지 않아 번호가 고정이다(<c>SaveRepository.CreatePlayerWithFirstCharacterAsync</c>).
    /// </summary>
    private static async Task<long?> FindCurrencyRowIdAsync(
        QueryFactory db, DbTransaction tx, long userId, int currencyCode)
        => await db.Query("player_item").Select("player_item_id")
            .Where("user_id", userId)
            .Where("row_type", Constants.PlayerItemRow.Currency)
            .Where("item_code", currencyCode)
            .FirstOrDefaultAsync<long?>(tx);

    /// <summary>갱신 직후 잔액을 기본키로 읽는다. 자기 트랜잭션이 그 행에 X락을 쥔 상태라 방금 확정한 값이 보인다.</summary>
    private static async Task<long> ReadCurrencyBalanceAsync(QueryFactory db, DbTransaction tx, long rowId)
        => await db.Query("player_item").Select("quantity")
            .Where("player_item_id", rowId)
            .FirstOrDefaultAsync<long?>(tx) ?? 0;

    /// <summary>
    /// 재화를 <paramref name="amount"/>만큼 차감한다. 잔액이 부족하면 <b>아무것도 바꾸지 않고</b> null을 돌려주고,
    /// 성공하면 차감 후 잔액을 돌려준다.
    /// <para><b>읽어서 계산한 값을 쓰지 않는다.</b> 잠금 없는 <c>SELECT</c>는 스냅샷 읽기라 동시에 도는 다른
    /// 트랜잭션의 변경을 보지 못한다 — 같은 계정의 요청 둘이 겹치면 양쪽이 같은 잔액을 읽고 각자 계산한 값을
    /// 덮어써 한쪽 차감이 사라진다. <c>quantity = quantity - @amount</c>로 DB가 직접 계산하게 하면 그 갱신은
    /// 최신 커밋값을 읽고 그 행을 잠그므로 겹칠 틈이 없고, 잔액 검사도 같은 문장의 <c>WHERE</c>가 겸한다.</para>
    /// <para><b>갱신은 기본키로 건다.</b> <c>WHERE user_id = ? AND row_type = 2 AND item_code = ?</c>로 바로 갱신하면
    /// 이 조건에 맞는 인덱스가 없어 그 계정의 아이템 행까지 훑으며 잠그고, 잠그는 순서가 엇갈리면 교착이 난다.</para>
    /// <para>상대값 갱신은 SqlKata 빌더로 표현되지 않아(증감폭을 <c>int</c>로만 받는다) 파라미터를 바인딩한
    /// 문장을 쓴다. 문자열을 조립하지는 않는다.</para>
    /// </summary>
    private static async Task<long?> TryDebitCurrencyAsync(
        QueryFactory db, DbTransaction tx, long userId, int currencyCode, long amount)
    {
        var rowId = await FindCurrencyRowIdAsync(db, tx, userId, currencyCode);

        // 비용 0(무료 경로)은 갱신할 것이 없다. 응답에 담을 현재 잔액만 돌려준다.
        if (amount <= 0)
        {
            return rowId is null ? 0 : await ReadCurrencyBalanceAsync(db, tx, rowId.Value);
        }

        if (rowId is null)
        {
            return null;
        }

        var affected = await db.StatementAsync(
            """
            UPDATE player_item SET quantity = quantity - @amount
             WHERE player_item_id = @rowId AND quantity >= @amount
            """,
            new { rowId, amount }, tx);

        // 0행 = 잔액 부족. 조건이 갱신 문장 안에 있으므로 부족하면 아무것도 바뀌지 않는다.
        return affected == 0 ? null : await ReadCurrencyBalanceAsync(db, tx, rowId.Value);
    }
}
