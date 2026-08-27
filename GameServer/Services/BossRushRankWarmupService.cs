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
/// <para><b>들어오는 경로가 둘이다.</b> 하나는 기동 절차 — <b>서버 밖의 부트스트랩 스크립트</b>
/// (<c>server_up_with_docker.py</c>)가 서버 기동을 확인한 뒤 관리 API를 한 번 호출한다. 다른 하나는
/// <b>조회가 적재되지 않은 리더보드를 만났을 때의 자동 재적재</b>(<see cref="RequestRebuild"/>)다.</para>
/// <para>후자를 둔 이유는 <b>캐시가 저절로 채워지지 않기 때문</b>이다 — Redis를 껐다 켜면 리더보드 키가
/// 사라지는데, 적재 경로가 기동 절차뿐이면 사람이 관리 API를 다시 부를 때까지 랭킹이 비어 있고 아무도
/// 그것을 알아채지 못한다(에러도 로그도 없이 200으로 나간다). 자동 재적재는 첫 조회가 그 복구를 걸게 한다.</para>
/// <para>자동 경로가 생겨 <b>호출자가 하나</b>라는 전제가 깨졌으므로 중복 재구축을 막을 락이 필요하다 —
/// 게임 API는 scale-out으로 N대가 뜬다(락을 쓰지 않는 BatchServer와 다른 점이다).</para>
/// <para>정본은 MySQL이므로 이 작업은 <b>언제 몇 번을 돌려도 안전</b>하다 — ZADD는 member(userId) 단위
/// 덮어쓰기이고, 점수는 기록에서 결정론적으로 계산된다.</para>
/// </summary>
public sealed class BossRushRankWarmupService : IBossRushRankWarmupService
{
    private readonly IBossRushRepository _repository;
    private readonly IBossRushRankCache _rankCache;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<BossRushRankWarmupService> _logger;

    /// <summary>
    /// 게임 DB 리포지토리·랭킹 캐시·로거를 주입받는다. 자동 재적재는 요청 수명 밖에서 돌아야 하므로
    /// 스코프 팩토리와, 서버 종료 시 적재를 끊을 수명 토큰도 함께 받는다.
    /// </summary>
    public BossRushRankWarmupService(
        IBossRushRepository repository, IBossRushRankCache rankCache,
        IServiceScopeFactory scopeFactory, IHostApplicationLifetime lifetime,
        ILogger<BossRushRankWarmupService> logger)
    {
        _repository = repository;
        _rankCache = rankCache;
        _scopeFactory = scopeFactory;
        _lifetime = lifetime;
        _logger = logger;
    }

    /// <summary>
    /// 리더보드 재적재를 백그라운드로 건다(조회가 적재되지 않은 리더보드를 만났을 때). 호출은 즉시 돌아온다 —
    /// 부른 요청은 정본 폴백으로 이미 정답을 냈고, 이 일은 <b>다음 요청부터 캐시를 쓰게 하려는</b> 복구다.
    /// <para>요청 스코프가 응답과 함께 사라지므로 <b>새 스코프</b>에서 돌리고, 게임 API가 N대로 뜨는 것을 감안해
    /// <b>재적재 락을 잡은 인스턴스만</b> 실제로 적재한다. 락을 못 잡았으면(다른 쪽이 이미 하고 있거나 Redis를
    /// 쓸 수 없으면) 조용히 넘어간다 — 다음 조회가 다시 건다.</para>
    /// </summary>
    public void RequestRebuild(int seasonId)
        => _ = Task.Run(() => RebuildInBackgroundAsync(seasonId));

    /// <summary>
    /// <see cref="RequestRebuild"/>의 본체. 락을 잡은 뒤 새 스코프에서 강제 워밍업을 돌리고 락을 푼다.
    /// <b>예외를 밖으로 내보내지 않는다</b> — 아무도 기다리지 않는 작업이라 새어 나가면 관측되지 않은
    /// 예외가 된다. 실패해도 마커가 서지 않으므로 다음 조회가 다시 시도한다.
    /// </summary>
    private async Task RebuildInBackgroundAsync(int seasonId)
    {
        using var scope = _scopeFactory.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IBossRushRankCache>();

        if (!await cache.TryAcquireRebuildLockAsync(seasonId, Constants.BossRush.RankRebuildLockTtl))
        {
            return;
        }

        try
        {
            _logger.ZLogWarning($"보스러시 랭킹 캐시 미적재 감지 — 자동 재적재를 시작합니다: seasonId {seasonId:@SeasonId}");

            var service = scope.ServiceProvider.GetRequiredService<IBossRushRankWarmupService>();
            await service.WarmUpAsync(true, _lifetime.ApplicationStopping);
        }
        catch (Exception ex)
        {
            _logger.ZLogError(ex, $"보스러시 랭킹 캐시 자동 재적재 실패: seasonId {seasonId:@SeasonId} — 다음 조회가 다시 시도합니다.");
        }
        finally
        {
            await cache.ReleaseRebuildLockAsync(seasonId);
        }
    }

    /// <summary>
    /// 진행 중 시즌을 찾아 ①시즌 메타 캐시를 갱신하고 ②리더보드가 비어 있으면(또는 <paramref name="force"/>가
    /// true면) MySQL 기록으로 재구축한다. 진행 중 시즌이 없으면 적재 대상이 없다는 결과를 그대로 돌려준다
    /// (오류가 아니다 — 시즌 개시는 이 서비스가 결정하지 않는다).
    /// </summary>
    public async Task<BossRushRankWarmupResult> WarmUpAsync(bool force, CancellationToken cancellationToken = default)
    {
        try
        {
            var season = await _repository.GetRunningSeasonAsync();
            if (season is null)
            {
                _logger.ZLogInformation($"보스러시 랭킹 캐시 워밍업: 진행 중 시즌이 없어 적재를 건너뜁니다");
                return new BossRushRankWarmupResult(Constants.BossRush.RankWarmupStatus.NoSeason, 0, 0, 0);
            }

            // 시즌 메타 캐시는 리더보드 상태와 무관하게 항상 최신으로 맞춘다(조회 경로가 이 값을 먼저 본다).
            await _rankCache.SetCurrentSeasonAsync(season);

            // **리더보드 키의 존재가 아니라 적재 완료 마커로 판단한다** — 키는 클리어 보고의 ZADD도 만들 수
            // 있어, 한 명만 든 리더보드를 "이미 채워져 있음"으로 오인하면 그 상태가 그대로 굳는다.
            var ready = await _rankCache.IsReadyAsync(season.SeasonId);
            if (ready is null)
            {
                // 캐시를 아예 쓸 수 없다 — 랭킹 조회는 MySQL 폴백으로 돌아가지만 적재는 실패다.
                _logger.ZLogError($"보스러시 랭킹 캐시 워밍업 실패(Redis 접근 불가): seasonId {season.SeasonId:@SeasonId}");
                return new BossRushRankWarmupResult(
                    Constants.BossRush.RankWarmupStatus.CacheUnavailable, season.SeasonId, 0, 0);
            }

            if (ready.Value && !force)
            {
                var current = await _rankCache.CountAsync(season.SeasonId) ?? 0;
                _logger.ZLogInformation($"보스러시 랭킹 캐시 워밍업: seasonId {season.SeasonId:@SeasonId} 리더보드가 이미 적재돼 있어 건너뜁니다({current:@Members}명)");
                return new BossRushRankWarmupResult(
                    Constants.BossRush.RankWarmupStatus.AlreadyWarm, season.SeasonId, 0, current);
            }

            // 적재하는 동안은 리더보드가 반쯤 찬 상태라 순위가 틀린다 — 마커부터 내려 조회가 정본을 보게 한다.
            await _rankCache.ClearReadyAsync(season.SeasonId);

            // **비우고 채운다.** ZADD는 멤버를 갱신할 뿐 정본에서 사라진 멤버를 지우지 않으므로, 덮어쓰기만
            // 하면 유령 멤버가 남아 순위를 밀어낸다(기록이 지워진 계정·다른 시즌 잔여물). 비운 뒤 적재가
            // 중간에 끊겨도 마커가 없으니 조회는 정본으로 폴백한다.
            await _rankCache.ClearBoardAsync(season.SeasonId);

            var (restored, failed) = await RestoreLeaderboardAsync(season, cancellationToken);
            var members = await _rankCache.CountAsync(season.SeasonId) ?? 0;

            if (failed > 0)
            {
                // 일부만 들어간 리더보드는 순위가 틀리므로 성공으로 보고하지 않는다(호출자가 다시 돌려야 한다).
                // 마커도 세우지 않는다 — 그래야 조회가 계속 정본으로 폴백하고 다음 재적재가 다시 걸린다.
                _logger.ZLogError($"보스러시 랭킹 캐시 워밍업 부분 실패: seasonId {season.SeasonId:@SeasonId} 적재 {restored:@Restored}건 · 실패 {failed:@Failed}건");
                return new BossRushRankWarmupResult(
                    Constants.BossRush.RankWarmupStatus.CacheUnavailable, season.SeasonId, restored, members);
            }

            // 전량 적재에 성공했을 때만 마커를 세운다 — 이 시점부터 조회가 캐시를 쓴다.
            await _rankCache.MarkReadyAsync(season.SeasonId);

            _logger.ZLogInformation($"보스러시 랭킹 캐시 워밍업 완료: seasonId {season.SeasonId:@SeasonId} {restored:@Restored}건 적재(등재 {members:@Members}명)");
            return new BossRushRankWarmupResult(
                Constants.BossRush.RankWarmupStatus.Restored, season.SeasonId, restored, members);
        }
        catch (Exception ex)
        {
            _logger.ZLogError(
                ex, $"WarmUpAsync 처리 중 예외: status {Constants.BossRush.RankWarmupStatus.Failed:@Status}");
            return new BossRushRankWarmupResult(
                Constants.BossRush.RankWarmupStatus.Failed, 0, 0, 0);
        }
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
