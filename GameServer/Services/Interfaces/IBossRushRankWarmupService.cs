using GameServer.Models;

namespace GameServer.Services.Interfaces;

/// <summary>
/// 보스러시 랭킹 캐시 최초 적재(워밍업) 서비스. 진행 중 시즌의 리더보드가 비어 있으면
/// MySQL <c>boss_rush_record</c>를 읽어 Redis Sorted Set을 재구축한다(보스러시 기획서 6.3).
/// <para>호출 주체는 <b>서버 밖의 부트스트랩 스크립트</b>(<c>server_up_with_docker.py</c>)이며 관리 API
/// <c>POST /api/admin/boss-rush/rank/warmup</c>을 통해 들어온다 — 서버는 이 적재를 스스로 하지 않는다.</para>
/// </summary>
public interface IBossRushRankWarmupService
{
    /// <summary>
    /// 진행 중 시즌의 시즌 메타 캐시를 갱신하고, 리더보드가 비어 있으면 MySQL에서 재구축한다.
    /// <paramref name="force"/>가 true면 리더보드가 이미 있어도 다시 적재한다(멤버 단위 덮어쓰기).
    /// </summary>
    Task<BossRushRankWarmupResult> WarmUpAsync(bool force, CancellationToken cancellationToken = default);
}
