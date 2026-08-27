using GameServer.Models;

namespace GameServer.Services.Interfaces;

/// <summary>
/// 보스러시 랭킹 캐시 적재(워밍업) 서비스. 진행 중 시즌의 리더보드가 적재되지 않았으면
/// MySQL <c>boss_rush_record</c>를 읽어 Redis Sorted Set을 재구축한다(보스러시 기획서 6.3).
/// <para>들어오는 경로가 둘이다 — <b>기동 절차</b>(부트스트랩 스크립트 <c>server_up_with_docker.py</c>가
/// 관리 API <c>POST /api/admin/boss-rush/rank/warmup</c> 호출)와, <b>조회가 적재되지 않은 리더보드를
/// 발견했을 때의 자동 재적재</b>(<see cref="RequestRebuild"/>)다. 후자가 있어 Redis를 껐다 켜도
/// 사람이 관리 API를 다시 부를 때까지 기다리지 않는다.</para>
/// </summary>
public interface IBossRushRankWarmupService
{
    /// <summary>
    /// 진행 중 시즌의 시즌 메타 캐시를 갱신하고, 리더보드가 적재되지 않았으면 MySQL에서 재구축한다.
    /// <paramref name="force"/>가 true면 적재 완료 마커가 있어도 다시 적재한다(멤버 단위 덮어쓰기).
    /// </summary>
    Task<BossRushRankWarmupResult> WarmUpAsync(bool force, CancellationToken cancellationToken = default);

    /// <summary>
    /// 리더보드 재적재를 백그라운드로 건다(조회가 적재되지 않은 리더보드를 만났을 때). <b>기다리지 않는다</b> —
    /// 부른 요청은 정본(MySQL) 폴백으로 이미 정답을 내려주고, 캐시 복구는 뒤따라 일어난다.
    /// <para>재적재 락을 잡은 인스턴스에서만 실제로 돈다(게임 API는 N대로 뜬다). 락을 잡지 못했거나
    /// Redis를 쓸 수 없으면 조용히 넘어간다 — 다음 조회가 다시 건다.</para>
    /// </summary>
    void RequestRebuild(int seasonId);
}
