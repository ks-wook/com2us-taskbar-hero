using GameServer.MasterData;
using GameServer.Repositories.GameDb;
using GameServer.Repositories.GameDb.Interfaces;
using GameServer.Services;
using TaskbarHero.Common;
using ZLogger;
using GameServer.Repositories.MemoryDb.Interfaces;
using GameServer.Repositories.MasterDb;
using GameServer.Models;
using GameServer.Util;

namespace GameServer.Batch;

/// <summary>
/// 보스러시 시즌 정산 배치(보스러시 기획서 6.4). 종료 시각이 지난 시즌을 조건부 갱신으로 선점해
/// (status 1 → 2) 순위를 확정하고, <b>1~3위에게만 골드 순위 보상을 메일로 발급</b>한 뒤 시즌을 종료하고
/// 다음 시즌을 개시한다.
/// <para><b>멱등</b>하다 — 순위 확정은 <c>final_rank = 0</c> 조건부 갱신이라 재진입 시 이미 처리한 행은
/// 0행이 되어 스킵되고, 다음 시즌 개시는 <c>start_at</c> 유니크가 중복을 막는다. 배치가 중간에 죽어도
/// 다음 주기가 남은 행만 이어서 처리한다.</para>
/// <para><b>시즌을 새로 만들지 않는다</b> — 진행 중 시즌이 없으면 그 주기는 아무 일도 하지 않고 끝낸다.</para>
/// <para><b>폴링하지 않는다</b> — 정산이 필요한 순간은 진행 중 시즌의 <c>end_at</c> 하나뿐이고 그 시각은 이미
/// DB에 있으므로, <see cref="NextDelay"/>를 재정의해 <b>그 시각까지 자고 정확히 그때 깨어난다</b>.
/// 진행 중 시즌이 없으면 <b>무기한 잔다</b> — 시즌이 없는 동안은 몇 번을 깨어나도 할 일이 없기 때문이다.
/// 새 시즌이 등록되면 그 사실을 아는 쪽이 <see cref="PeriodicBatchScheduler.Wake"/>로 깨운다(서버 기동 시
/// 도는 첫 주기도 같은 역할을 한다).</para>
/// <para>기동 시에는 정산 전에 <b>랭킹 캐시 워밍업</b>도 수행한다 — 현재 시즌 리더보드가 비어 있으면
/// <c>boss_rush_record</c>를 페이지 단위로 읽어 ZADD로 재구축하고, 현재 시즌 메타 캐시를 채운다(6.3).
/// 이 작업이 배치에 붙어 있는 이유는 <b>Redis 리더 락</b>이 이미 여기에 있어 scale-out 시 중복 재구축을
/// 그대로 막아 주기 때문이다.</para>
/// 설정: appsettings "BossRushSeasonBatch" 섹션(IntervalSeconds 기본 600=10분 · BatchSize 기본 500).
/// </summary>
public sealed class BossRushSeasonBatchScheduler : PeriodicBatchScheduler
{
    /// <summary>
    /// 기본 주기 10분. 이 배치는 <b>정상 경로에서 이 주기로 돌지 않는다</b> — 진행 중 시즌이 있으면 그
    /// 종료 시각까지, 없으면 무기한 자기 때문이다. 남은 쓰임은 ①리더 락 TTL 산정과 ②주기가 실제로 돌지
    /// 못했을 때(리더 락 스킵·예외)의 재시도 간격이다.
    /// </summary>
    private const int DefaultIntervalSeconds = 10 * 60;

    /// <summary>1주기(정산 페이지) 처리 상한. 페이지 단위 트랜잭션으로 쪼개 긴 잠금을 만들지 않는다.</summary>
    private const int DefaultBatchSize = 500;

    /// <summary>순위 보상 메일 템플릿(mail_master 501, {0} = 시즌 번호 · {1} = 최종 순위).</summary>
    private const int RankRewardMailTemplateCode = 501;

    /// <summary>메일 첨부 reward_type 1:골드. 순위 보상은 골드뿐이라 이 값만 쓴다(기획서 4.1).</summary>
    private const int RewardTypeGold = 1;

    /// <summary>
    /// 종료 시각이 이미 지났는데도 정산되지 않은 시즌이 남아 있을 때(다른 인스턴스가 정산 중이라 리더 락을
    /// 놓쳤거나 직전 주기가 실패한 경우)의 재시도 간격. 그 상황에서만 쓰이므로 짧게 잡아도 안전하다.
    /// </summary>
    private static readonly TimeSpan PastDueRetryDelay = TimeSpan.FromMinutes(1);

    /// <summary>종료된 시즌 리더보드에 거는 TTL(7일). 과거 키가 무한히 쌓이지 않게 한다(기획서 4.3).</summary>
    private static readonly TimeSpan ClosedSeasonTtl = TimeSpan.FromDays(7);

    private readonly int _intervalSeconds;
    private readonly int _batchSize;
    private readonly MasterDbProvider _masterData;
    private readonly ILogger<BossRushSeasonBatchScheduler> _logger;

    /// <summary>설정에서 실행 주기·1회 처리 상한을 읽고(없거나 0 이하이면 기본값), 마스터 데이터를 주입받는다.</summary>
    public BossRushSeasonBatchScheduler(
        IServiceScopeFactory scopeFactory, IBatchLock batchLock, IConfiguration configuration,
        MasterDbProvider masterData, ILogger<BossRushSeasonBatchScheduler> logger)
        : base(scopeFactory, batchLock, logger)
    {
        var interval = configuration.GetValue("BossRushSeasonBatch:IntervalSeconds", DefaultIntervalSeconds);
        var batchSize = configuration.GetValue("BossRushSeasonBatch:BatchSize", DefaultBatchSize);
        _intervalSeconds = interval > 0 ? interval : DefaultIntervalSeconds;
        _batchSize = batchSize > 0 ? batchSize : DefaultBatchSize;
        _masterData = masterData;
        _logger = logger;
    }

    /// <summary>
    /// 다음 기상 시각(유닉스초) = 진행 중 시즌의 종료 시각. <b>정산이 필요한 순간은 그때 하나뿐</b>이라
    /// 그 시각까지 자면 되고, 그 사이를 주기적으로 확인할 이유가 없다.
    /// 0이면 미확정(진행 중 시즌 없음·마스터 미적재) — 기본 주기로 되돌아간다.
    /// </summary>
    private long _nextWakeUnix;

    protected override TimeSpan Interval => TimeSpan.FromSeconds(_intervalSeconds);

    /// <summary>
    /// 진행 중 시즌의 종료 시각을 알고 있으면 <b>그 시각까지 정확히 잔다</b>(폴링 아님).
    /// 진행 중 시즌이 없으면 <b>무기한 대기</b>한다 — 시즌이 없는 동안은 몇 번을 깨어나도 할 일이 없다.
    /// 반환값의 상·하한은 골격이 잡는다.
    /// </summary>
    /// <summary>
    /// 대기 상한을 기본 주기 대신 7일로 푼다 — 시즌 종료가 며칠 뒤여도 <b>그때까지 통째로 자기 위해서</b>다.
    /// 상한을 주기(10분)로 두면 아는 시각까지 자려던 대기가 매번 잘려 결국 폴링이 된다.
    /// 7일은 시즌 길이보다 길어 실질적인 제약이 아니면서 <c>Task.Delay</c>의 한계 안에 안전하게 든다.
    /// </summary>
    protected override TimeSpan MaxDelay => TimeSpan.FromDays(7);

    protected override TimeSpan NextDelay()
    {
        if (_nextWakeUnix <= 0)
        {
            // 진행 중 시즌이 없다 — 깨어나도 정산할 대상이 없으므로 아예 돌지 않는다.
            // 새 시즌이 등록되면 그 사실을 아는 쪽이 Wake()로 깨운다(기동 시 첫 주기도 그 역할을 한다).
            return Timeout.InfiniteTimeSpan;
        }

        var remainingSeconds = _nextWakeUnix - DateTimeUtil.NowUnixSeconds();
        return remainingSeconds > 0 ? TimeSpan.FromSeconds(remainingSeconds) : PastDueRetryDelay;
    }

    protected override string BatchName => "보스러시 시즌 정산 배치";

    protected override string BatchKey => "bossrush-season";

    /// <summary>
    /// 1주기 작업: ①진행 중 시즌 확인 → ②랭킹 캐시 워밍업 → ③종료 시각이 지난 시즌 선점 →
    /// ④순위 확정·보상 메일 발급 → ⑤시즌 종료·리더보드 TTL → ⑥다음 시즌 개시.
    /// <para><b>진행 중 시즌이 없으면 ①에서 즉시 끝낸다</b> — 시즌을 새로 만들지 않으므로 워밍업·정산 모두
    /// 대상이 없다. 정산할 시즌이 없을 때도 조용히 끝낸다(로그 소음 방지).</para>
    /// </summary>
    protected override async Task RunCycleAsync(IServiceScope scope, CancellationToken stoppingToken)
    {
        var rule = _masterData.IsLoaded ? _masterData.BossRushRule : null;
        if (rule is null)
        {
            _nextWakeUnix = 0; // 기상 시각을 모른다 — 기본 주기로 재시도.
            return; // 마스터 미적재·콘텐츠 미구성.
        }

        var repository = scope.ServiceProvider.GetRequiredService<IBossRushRepository>();
        var rankCache = scope.ServiceProvider.GetRequiredService<IBossRushRankCache>();
        var now = DateTimeUtil.UtcNow;
        var nowUnix = DateTimeUtil.ToUnixSeconds(now);

        // ① 진행 중 시즌 확인. 없으면 이번 주기는 할 일이 없다 — 시즌 개시는 이 배치가 결정하지 않는다.
        var current = await repository.GetRunningSeasonAsync();
        if (current is null)
        {
            _nextWakeUnix = 0; // 정산할 시즌이 없다 — 기본 주기로 새 시즌 등록을 살핀다.
            return;
        }

        // 이 시즌이 끝나는 순간이 다음에 깨어날 유일한 이유다.
        _nextWakeUnix = current.EndAt;

        // ② 랭킹 캐시 워밍업(리더보드가 비어 있을 때만).
        await WarmUpRankCacheAsync(repository, rankCache, current);

        // ③ 종료 시각이 지난 시즌 선점(조건부 갱신 0행이면 정산할 시즌 없음).
        var season = await repository.ClaimSeasonForSettlementAsync(nowUnix);
        if (season is null)
        {
            return;
        }

        _logger.ZLogInformation($"보스러시 시즌 정산 시작: seasonId {season.SeasonId:@SeasonId} (종료 {season.EndAt:@EndAt})");

        var template = _masterData.GetMailTemplate(RankRewardMailTemplateCode);
        if (template is null)
        {
            // 보상 없이 순위만 확정한다 — 시즌을 정산중(2)에 묶어 두면 새 런을 계속 받지 못한다.
            _logger.ZLogError($"보스러시 순위 보상 메일 템플릿 미정의: templateCode {RankRewardMailTemplateCode:@TemplateCode} — mail_master 확인 필요(순위만 확정합니다)");
        }

        // ④ 순위 확정 + 보상 메일 발급.
        var (settled, rewarded) = await SettleAsync(repository, season, template, nowUnix, stoppingToken);

        // ⑤ 시즌 종료 + 리더보드 TTL.
        await repository.CloseSeasonAsync(season.SeasonId, nowUnix);
        await rankCache.ExpireAsync(season.SeasonId, ClosedSeasonTtl);

        // ⑥ 다음 시즌 개시 + 시즌 메타 캐시 갱신(정산의 마지막 단계, 기획서 6.4).
        var next = await repository.StartNextSeasonAsync(
            season.EndAt, season.EndAt + DateTimeUtil.DaysToSeconds(rule.SeasonPeriodDays));
        await rankCache.SetCurrentSeasonAsync(next);
        _nextWakeUnix = next.EndAt;

        _logger.ZLogInformation($"보스러시 시즌 정산 완료: seasonId {season.SeasonId:@SeasonId} 순위 확정 {settled:@Settled}건 · 보상 발급 {rewarded:@Rewarded}건 → 다음 시즌 {next.SeasonId:@NextSeasonId}");
    }

    /// <summary>
    /// 진행 중 시즌의 메타 캐시와 리더보드를 채운다(6.3 워밍업).
    /// 리더보드 키가 이미 있으면 재구축하지 않는다 — 워밍업은 "비었을 때만" 하는 복구 작업이다.
    /// </summary>
    private async Task WarmUpRankCacheAsync(
        IBossRushRepository repository, IBossRushRankCache rankCache, BossRushSeason season)
    {
        await rankCache.SetCurrentSeasonAsync(season);

        var exists = await rankCache.ExistsAsync(season.SeasonId);
        if (exists is null || exists.Value)
        {
            return; // 캐시를 쓸 수 없거나(다음 주기 재시도) 이미 채워져 있다.
        }

        var restored = 0;
        var offset = 0;
        while (true)
        {
            var page = await repository.ScanRecordsAsync(season.SeasonId, offset, _batchSize);
            if (page.Count == 0)
            {
                break;
            }

            foreach (var record in page)
            {
                await rankCache.UpsertAsync(
                    season.SeasonId, season.StartAt, record.UserId, record.BestClearMs, record.RecordedAt);
                restored++;
            }

            offset += page.Count;
        }

        if (restored > 0)
        {
            _logger.ZLogInformation($"보스러시 랭킹 캐시 워밍업: seasonId {season.SeasonId:@SeasonId} {restored:@Restored}건 재구축");
        }
    }

    /// <summary>
    /// 순위 미확정 기록을 정렬 순서로 읽어 순위를 확정하고, 매칭되는 보상 구간이 있으면(1~3위) 골드
    /// 보상 메일을 같은 트랜잭션에서 발급한다. 이미 확정된 행 수를 시작 순위로 이어 재진입에서도
    /// 순위가 어긋나지 않게 한다.
    /// </summary>
    private async Task<(int Settled, int Rewarded)> SettleAsync(
        IBossRushRepository repository, BossRushSeason season, MailTemplateDef? template,
        long nowUnix, CancellationToken stoppingToken)
    {
        var settled = 0;
        var rewarded = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            var targets = await repository.GetUnsettledRecordsAsync(season.SeasonId, _batchSize);
            if (targets.Count == 0)
            {
                break;
            }

            // 이미 확정된 건수가 곧 이 페이지의 시작 순위 - 1이다(정렬 순서가 같으므로).
            var rank = await repository.CountSettledAsync(season.SeasonId) + 1;

            foreach (var target in targets)
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                var reward = _masterData.BossRushRankRewardFor(rank);
                MailDraft? mail = null;
                if (reward is not null && reward.RewardGold > 0 && template is not null)
                {
                    mail = MailComposer.Compose(
                        template, season.SeasonId.ToString(), rank.ToString(), nowUnix,
                        new[] { new MailAttachment(RewardTypeGold, 0, reward.RewardGold) });
                }

                try
                {
                    var applied = await repository.SettleRecordAsync(
                        season.SeasonId, target.UserId, rank, mail, nowUnix);
                    if (applied)
                    {
                        settled++;
                        if (mail is not null)
                        {
                            rewarded++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.ZLogError(ex, $"보스러시 순위 확정 실패(seasonId {season.SeasonId:@SeasonId} userId {target.UserId:@UserId} rank {rank:@Rank}) — 다음 주기에 재시도합니다.");
                }

                rank++;
            }
        }

        return (settled, rewarded);
    }

}
