using GameServer.MasterData;
using GameServer.Repositories.GameDb;
using GameServer.Repositories.GameDb.Interfaces;
using GameServer.Services;
using TaskbarHero.Common;
using ZLogger;
using GameServer.Repositories.MemoryDb.Interfaces;
using GameServer.Repositories;
using GameServer.Repositories.MasterDb;
using GameServer.Models;

namespace GameServer.Batch;

/// <summary>
/// 보스러시 시즌 정산 배치(보스러시 기획서 6.4). 종료 시각이 지난 시즌을 조건부 갱신으로 선점해
/// (status 1 → 2) 순위를 확정하고, <b>1~3위에게만 골드 순위 보상을 메일로 발급</b>한 뒤 시즌을 종료하고
/// 다음 시즌을 개시한다.
/// <para><b>멱등</b>하다 — 순위 확정은 <c>final_rank = 0</c> 조건부 갱신이라 재진입 시 이미 처리한 행은
/// 0행이 되어 스킵되고, 다음 시즌 개시는 <c>start_at</c> 유니크가 중복을 막는다. 배치가 중간에 죽어도
/// 다음 주기가 남은 행만 이어서 처리한다.</para>
/// <para>기동 시에는 정산 전에 <b>랭킹 캐시 워밍업</b>도 수행한다 — 현재 시즌 리더보드가 비어 있으면
/// <c>boss_rush_record</c>를 페이지 단위로 읽어 ZADD로 재구축하고, 현재 시즌 메타 캐시를 채운다(6.3).
/// 이 작업이 배치에 붙어 있는 이유는 <b>Redis 리더 락</b>이 이미 여기에 있어 scale-out 시 중복 재구축을
/// 그대로 막아 주기 때문이다.</para>
/// 설정: appsettings "BossRushSeasonBatch" 섹션(IntervalSeconds 기본 600=10분 · BatchSize 기본 500).
/// </summary>
public sealed class BossRushSeasonBatchService : PeriodicBatchService
{
    /// <summary>기본 실행 주기 10분. 시즌 경계(주 1회)에 비해 충분히 촘촘하다.</summary>
    private const int DefaultIntervalSeconds = 10 * 60;

    /// <summary>1주기(정산 페이지) 처리 상한. 페이지 단위 트랜잭션으로 쪼개 긴 잠금을 만들지 않는다.</summary>
    private const int DefaultBatchSize = 500;

    /// <summary>순위 보상 메일 템플릿(mail_master 501, {0} = 시즌 번호 · {1} = 최종 순위).</summary>
    private const int RankRewardMailTemplateCode = 501;

    /// <summary>메일 첨부 reward_type 1:골드. 순위 보상은 골드뿐이라 이 값만 쓴다(기획서 4.1).</summary>
    private const int RewardTypeGold = 1;

    /// <summary>종료된 시즌 리더보드에 거는 TTL(7일). 과거 키가 무한히 쌓이지 않게 한다(기획서 4.3).</summary>
    private static readonly TimeSpan ClosedSeasonTtl = TimeSpan.FromDays(7);

    /// <summary>KST(UTC+9) — 시즌 경계(월요일 00:00)를 잡는 기준.</summary>
    private static readonly TimeSpan KstOffset = TimeSpan.FromHours(9);

    private readonly int _intervalSeconds;
    private readonly int _batchSize;
    private readonly MasterDbProvider _masterData;
    private readonly ILogger<BossRushSeasonBatchService> _logger;

    /// <summary>설정에서 실행 주기·1회 처리 상한을 읽고(없거나 0 이하이면 기본값), 마스터 데이터를 주입받는다.</summary>
    public BossRushSeasonBatchService(
        IServiceScopeFactory scopeFactory, IBatchLock batchLock, IConfiguration configuration,
        MasterDbProvider masterData, ILogger<BossRushSeasonBatchService> logger)
        : base(scopeFactory, batchLock, logger)
    {
        var interval = configuration.GetValue("BossRushSeasonBatch:IntervalSeconds", DefaultIntervalSeconds);
        var batchSize = configuration.GetValue("BossRushSeasonBatch:BatchSize", DefaultBatchSize);
        _intervalSeconds = interval > 0 ? interval : DefaultIntervalSeconds;
        _batchSize = batchSize > 0 ? batchSize : DefaultBatchSize;
        _masterData = masterData;
        _logger = logger;
    }

    protected override TimeSpan Interval => TimeSpan.FromSeconds(_intervalSeconds);

    protected override string BatchName => "보스러시 시즌 정산 배치";

    protected override string BatchKey => "bossrush-season";

    /// <summary>
    /// 1주기 작업: ①현재 시즌 보장(없으면 개시) + 랭킹 캐시 워밍업 → ②종료 시각이 지난 시즌 선점 →
    /// ③순위 확정·보상 메일 발급 → ④시즌 종료·리더보드 TTL → ⑤다음 시즌 개시.
    /// 정산할 시즌이 없으면 워밍업만 하고 조용히 끝낸다(로그 소음 방지).
    /// </summary>
    protected override async Task RunCycleAsync(IServiceScope scope, CancellationToken stoppingToken)
    {
        var rule = _masterData.IsLoaded ? _masterData.BossRushRule : null;
        if (rule is null)
        {
            return; // 마스터 미적재·콘텐츠 미구성 — 다음 주기에 재시도.
        }

        var repository = scope.ServiceProvider.GetRequiredService<IBossRushRepository>();
        var rankCache = scope.ServiceProvider.GetRequiredService<IBossRushRankCache>();
        var now = DateTimeOffset.UtcNow;
        var nowUnix = now.ToUnixTimeSeconds();

        // ① 현재 시즌 보장 + 랭킹 캐시 워밍업.
        await EnsureCurrentSeasonAsync(repository, rankCache, rule, now);

        // ② 종료 시각이 지난 시즌 선점(조건부 갱신 0행이면 정산할 시즌 없음).
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

        // ③ 순위 확정 + 보상 메일 발급.
        var (settled, rewarded) = await SettleAsync(repository, season, template, nowUnix, stoppingToken);

        // ④ 시즌 종료 + 리더보드 TTL.
        await repository.CloseSeasonAsync(season.SeasonId, nowUnix);
        await rankCache.ExpireAsync(season.SeasonId, ClosedSeasonTtl);

        // ⑤ 다음 시즌 개시 + 시즌 메타 캐시 갱신(정산의 마지막 단계, 기획서 6.4).
        var next = await repository.StartNextSeasonAsync(
            season.EndAt, season.EndAt + (long)rule.SeasonPeriodDays * SecondsPerDay);
        await rankCache.SetCurrentSeasonAsync(next);

        _logger.ZLogInformation($"보스러시 시즌 정산 완료: seasonId {season.SeasonId:@SeasonId} 순위 확정 {settled:@Settled}건 · 보상 발급 {rewarded:@Rewarded}건 → 다음 시즌 {next.SeasonId:@NextSeasonId}");
    }

    /// <summary>하루(초).</summary>
    private const long SecondsPerDay = 86_400;

    /// <summary>
    /// 진행 중 시즌이 없으면 개시하고, 현재 시즌 메타 캐시와 리더보드를 채운다(6.3 워밍업).
    /// 리더보드 키가 이미 있으면 재구축하지 않는다 — 워밍업은 "비었을 때만" 하는 복구 작업이다.
    /// </summary>
    private async Task EnsureCurrentSeasonAsync(
        IBossRushRepository repository, IBossRushRankCache rankCache, BossRushRuleDef rule, DateTimeOffset now)
    {
        var season = await repository.GetRunningSeasonAsync();
        if (season is null)
        {
            // 첫 기동(시즌 행이 아예 없음) — 이번 주 월요일 00:00(KST)부터 시작하는 시즌을 만든다.
            var startAt = WeekStartUnix(now);
            season = await repository.StartNextSeasonAsync(
                startAt, startAt + (long)rule.SeasonPeriodDays * SecondsPerDay);
            _logger.ZLogInformation($"보스러시 첫 시즌 개시: seasonId {season.SeasonId:@SeasonId} (시작 {season.StartAt:@StartAt} · 종료 {season.EndAt:@EndAt})");
        }

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
                    season.SeasonId, record.UserId, record.BestClearMs, record.RecordedAt);
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

    /// <summary>이번 주 월요일 00:00(KST)의 유닉스초. 첫 시즌 개시 시각을 잡는 데 쓴다.</summary>
    private static long WeekStartUnix(DateTimeOffset utcNow)
    {
        var kstNow = utcNow.ToOffset(KstOffset);
        var daysFromMonday = ((int)kstNow.DayOfWeek + 6) % 7; // 월=0 … 일=6
        var monday = new DateTimeOffset(kstNow.Year, kstNow.Month, kstNow.Day, 0, 0, 0, KstOffset)
            .AddDays(-daysFromMonday);
        return monday.ToUnixTimeSeconds();
    }
}
