using GameServer.Models;
using GameServer.Repositories.GameDb.Interfaces;
using GameServer.Repositories.MemoryDb.Interfaces;
using GameServer.Services.Interfaces;
using ZLogger;

namespace GameServer.Services;

/// <summary>
/// 보스러시 랭킹 캐시 최초 적재(워밍업) 서비스(기획서 6.3). 진행 중 시즌의 메타 캐시를 채우고,
/// 리더보드(<c>rank:bossrush:{seasonId}</c>)가 비어 있으면 MySQL <c>boss_rush_record</c>를 페이지 단위로
/// 읽어 ZADD로 재구축한다.
/// <para><b>서버가 스스로 하지 않는다.</b> 예전에는 시즌 정산 배치가 리더 락을 쥔 채 매 주기 앞단에서
/// 이 일을 했지만, 지금은 <b>서버 밖의 부트스트랩 스크립트</b>(<c>server_up.py</c>)가 서버 기동을 확인한
/// 뒤 관리 API를 한 번 호출한다 — 적재 시점이 기동 절차의 명시적인 한 단계가 되고, 호출자가 하나뿐이라
/// 중복 재구축을 막을 분산 락이 필요하지 않다.</para>
/// <para>정본은 MySQL이므로 이 작업은 <b>언제 몇 번을 돌려도 안전</b>하다 — ZADD는 member(userId) 단위
/// 덮어쓰기이고, 점수는 기록에서 결정론적으로 계산된다.</para>
/// </summary>
public sealed class BossRushRankWarmupService : IBossRushRankWarmupService
{
    private readonly IBossRushRepository _repository;
    private readonly IBossRushRankCache _rankCache;
    private readonly ILogger<BossRushRankWarmupService> _logger;

    /// <summary>게임 DB 리포지토리·랭킹 캐시·로거를 주입받는다.</summary>
    public BossRushRankWarmupService(
        IBossRushRepository repository, IBossRushRankCache rankCache,
        ILogger<BossRushRankWarmupService> logger)
    {
        _repository = repository;
        _rankCache = rankCache;
        _logger = logger;
    }

    /// <summary>
    /// 진행 중 시즌을 찾아 ①시즌 메타 캐시를 갱신하고 ②리더보드가 비어 있으면(또는 <paramref name="force"/>가
    /// true면) MySQL 기록으로 재구축한다. 진행 중 시즌이 없으면 적재 대상이 없다는 결과를 그대로 돌려준다
    /// (오류가 아니다 — 시즌 개시는 이 서비스가 결정하지 않는다).
    /// </summary>
    public async Task<BossRushRankWarmupResult> WarmUpAsync(bool force, CancellationToken cancellationToken = default)
    {
        var season = await _repository.GetRunningSeasonAsync();
        if (season is null)
        {
            _logger.ZLogInformation($"보스러시 랭킹 캐시 워밍업: 진행 중 시즌이 없어 적재를 건너뜁니다");
            return new BossRushRankWarmupResult(Constants.BossRush.RankWarmupStatus.NoSeason, 0, 0, 0);
        }

        // 시즌 메타 캐시는 리더보드 상태와 무관하게 항상 최신으로 맞춘다(조회 경로가 이 값을 먼저 본다).
        await _rankCache.SetCurrentSeasonAsync(season);

        var exists = await _rankCache.ExistsAsync(season.SeasonId);
        if (exists is null)
        {
            // 캐시를 아예 쓸 수 없다 — 랭킹 조회는 MySQL 폴백으로 돌아가지만 적재는 실패다.
            _logger.ZLogError($"보스러시 랭킹 캐시 워밍업 실패(Redis 접근 불가): seasonId {season.SeasonId:@SeasonId}");
            return new BossRushRankWarmupResult(
                Constants.BossRush.RankWarmupStatus.CacheUnavailable, season.SeasonId, 0, 0);
        }

        if (exists.Value && !force)
        {
            var current = await _rankCache.CountAsync(season.SeasonId) ?? 0;
            _logger.ZLogInformation($"보스러시 랭킹 캐시 워밍업: seasonId {season.SeasonId:@SeasonId} 리더보드가 이미 채워져 있어 건너뜁니다({current:@Members}명)");
            return new BossRushRankWarmupResult(
                Constants.BossRush.RankWarmupStatus.AlreadyWarm, season.SeasonId, 0, current);
        }

        var (restored, failed) = await RestoreLeaderboardAsync(season, cancellationToken);
        var members = await _rankCache.CountAsync(season.SeasonId) ?? 0;

        if (failed > 0)
        {
            // 일부만 들어간 리더보드는 순위가 틀리므로 성공으로 보고하지 않는다(호출자가 다시 돌려야 한다).
            _logger.ZLogError($"보스러시 랭킹 캐시 워밍업 부분 실패: seasonId {season.SeasonId:@SeasonId} 적재 {restored:@Restored}건 · 실패 {failed:@Failed}건");
            return new BossRushRankWarmupResult(
                Constants.BossRush.RankWarmupStatus.CacheUnavailable, season.SeasonId, restored, members);
        }

        _logger.ZLogInformation($"보스러시 랭킹 캐시 워밍업 완료: seasonId {season.SeasonId:@SeasonId} {restored:@Restored}건 적재(등재 {members:@Members}명)");
        return new BossRushRankWarmupResult(
            Constants.BossRush.RankWarmupStatus.Restored, season.SeasonId, restored, members);
    }

    /// <summary>
    /// 시즌 기록을 페이지 단위로 읽어 리더보드에 ZADD한다. 페이지로 쪼개는 이유는 등재 인원이 늘어도
    /// 메모리 사용량을 <see cref="Constants.BossRush.RankWarmupPageSize"/>에 묶어 두기 위해서다.
    /// 실패한 건수를 함께 돌려주므로 호출측이 부분 적재를 성공으로 오인하지 않는다.
    /// </summary>
    private async Task<(int Restored, int Failed)> RestoreLeaderboardAsync(
        BossRushSeason season, CancellationToken cancellationToken)
    {
        var restored = 0;
        var failed = 0;
        var offset = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var page = await _repository.ScanRecordsAsync(
                season.SeasonId, offset, Constants.BossRush.RankWarmupPageSize);
            if (page.Count == 0)
            {
                break;
            }

            foreach (var record in page)
            {
                var added = await _rankCache.UpsertAsync(
                    season.SeasonId, season.StartAt, record.UserId, record.BestClearMs, record.RecordedAt);
                if (added)
                {
                    restored++;
                }
                else
                {
                    failed++;
                }
            }

            offset += page.Count;
        }

        return (restored, failed);
    }
}
