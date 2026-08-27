using System.Data.Common;
using SqlKata.Execution;

namespace GameServer.Repositories.GameDb;

/// <summary>
/// 가방 칸(<c>player_item.slot</c>) 배치 규칙을 한곳에 모은 헬퍼. 장착 해제·전리품 적재·메일 첨부 수령·큐브·가챠·
/// 거래 반송이 모두 <b>같은 규칙</b>(<c>[0, inventory_capacity)</c> 범위의 가장 작은 빈 칸)으로 아이템을 적재하므로,
/// 리포지토리마다 복제하지 않고 여기서만 정의한다.
/// </summary>
/// <remarks>
/// 조회는 모두 <b>호출측 트랜잭션 안에서</b> 수행한다 — 판정과 배치 사이에 다른 요청이 그 칸을 채우면
/// <c>(user_id, slot)</c> 유니크 제약에 걸리기 때문이다(인벤토리/아이템/큐브 기획서 5.1·5.2).
/// </remarks>
internal static class InventorySlotAllocator
{
    /// <summary>인벤토리 용량(<c>game_player.inventory_capacity</c>)을 읽는다. 계정 세이브가 없으면 0.</summary>
    public static async Task<int> LoadCapacityAsync(QueryFactory db, DbTransaction tx, long userId)
        => await db.Query("game_player").Select("inventory_capacity").Where("user_id", userId)
            .FirstOrDefaultAsync<int?>(tx) ?? 0;

    /// <summary>
    /// 현재 점유 중인 가방 칸(slot) 집합을 읽는다. 재화 행과 장착 중인 장비는 <c>slot</c>이 NULL이라 자동으로 빠진다.
    /// </summary>
    /// <remarks>
    /// <b>잠금 조회로 읽는다.</b> 잠금 없는 조회는 트랜잭션이 처음 읽은 시점의 스냅샷을 계속 보므로, 그 사이
    /// 다른 요청이 칸을 채웠어도 빈 칸으로 보인다 — 그 상태로 배정하면 <c>(user_id, slot)</c> 유니크에 걸려
    /// 적재가 실패한다. 잠금 조회는 최신 커밋값을 읽고 그 구간을 잠그므로, 겹친 요청이 순서대로 다음 칸을 받는다.
    /// <para>잠그는 범위는 <b>그 계정의 가방 칸</b>뿐이다. 계정 전체를 잠그는 것과 달리 재화·세이브·파티처럼
    /// 가방과 무관한 경로는 영향을 받지 않는다.</para>
    /// </remarks>
    public static async Task<HashSet<int>> LoadUsedSlotsAsync(QueryFactory db, DbTransaction tx, long userId)
    {
        var slots = await db.SelectAsync<int>(
            """
            SELECT slot FROM player_item WHERE user_id = @userId AND slot IS NOT NULL ORDER BY slot FOR UPDATE
            """,
            new { userId }, tx);
        return slots.ToHashSet();
    }

    /// <summary>
    /// <paramref name="used"/> 기준으로 <c>[0, capacity)</c> 범위의 <b>가장 작은 빈 칸</b>을 찾는다.
    /// 찾으면 true와 그 칸을, 빈 칸이 없으면(용량 초과) false를 반환한다.
    /// 한 트랜잭션에서 여러 행을 연속 적재할 때는 호출측이 배정한 칸을 <paramref name="used"/>에 더해가며 재사용한다.
    /// </summary>
    /// <remarks>
    /// <b>범위를 반드시 <c>capacity</c>로 제한한다.</b> 점유 칸 "개수"만 보고 상한 없이 증가시키면, slot 값이
    /// <c>[0, capacity)</c> 안에 조밀하게 있다는 전제가 깨졌을 때 용량 범위 밖 칸을 배정하게 된다.
    /// </remarks>
    public static bool TryFirstFree(HashSet<int> used, int capacity, out int slot)
    {
        for (var i = 0; i < capacity; i++)
        {
            if (!used.Contains(i))
            {
                slot = i;
                return true;
            }
        }

        slot = -1;
        return false;
    }

    /// <summary>
    /// 용량과 점유 칸을 조회해 가장 작은 빈 칸을 반환한다. 빈 칸이 없거나 계정 세이브가 없으면 null.
    /// 칸 하나만 필요한 경로(장착 해제·장착 스왑 복귀)를 위한 단발 조회다.
    /// </summary>
    public static async Task<int?> FindFreeSlotAsync(QueryFactory db, DbTransaction tx, long userId)
    {
        int capacity = await LoadCapacityAsync(db, tx, userId);
        var used = await LoadUsedSlotsAsync(db, tx, userId);
        return TryFirstFree(used, capacity, out int slot) ? slot : null;
    }
}
