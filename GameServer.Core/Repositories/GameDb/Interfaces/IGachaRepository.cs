using GameServer.MasterData;
using GameServer.Repositories.GameDb;
using GameServer.Models;

namespace GameServer.Repositories.GameDb.Interfaces;

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
    Task<GachaPullOutcome> ApplyPullAsync(
        long userId, GachaBannerDef banner, int pullType, long cost,
        Func<IDictionary<int, int>, IReadOnlyList<GachaPullEntry>?> rollAll,
        long nowUnix);

    /// <summary>
    /// 뽑기 기록을 최신순 커서 페이징으로 조회한다(기획서 §6.6). gachaCode 0이면 전체,
    /// cursor 0이면 최신부터. 부모 페이지를 먼저 뽑고 자식은 pull_id 집합으로 한 번에 가져온다(쿼리 2회 고정).
    /// </summary>
    Task<GachaHistoryPage> GetHistoryAsync(long userId, int gachaCode, long cursor, int limit);
}
