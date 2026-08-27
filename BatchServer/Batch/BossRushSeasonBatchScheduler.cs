using GameServer.MasterData;
using GameServer.Repositories.GameDb;
using GameServer.Repositories.GameDb.Interfaces;
using TaskbarHero.Common;
using ZLogger;
using GameServer.Repositories.MemoryDb.Interfaces;
using GameServer.Repositories.MasterDb;
using GameServer.Models;
using GameServer.Util;
using GameServer.Logging;

namespace GameServer.Batch;

/// <summary>
/// 보스러시 시즌 정산 배치(보스러시 기획서 6.4). 종료 시각이 지난 시즌을 조건부 갱신으로 선점해
/// (status 1 → 2) 순위를 확정하고, <b>1~3위에게만 골드 순위 보상을 메일로 발급</b>한 뒤 시즌을 종료하고
/// 다음 시즌을 개시한다.
/// <para><b>멱등</b>하다 — 순위 확정은 <c>final_rank = 0</c> 조건부 갱신이라 재진입 시 이미 처리한 행은
/// 0행이 되어 스킵되고, 다음 시즌 개시는 <c>start_at</c> 유니크가 중복을 막는다. 배치가 중간에 죽어도
/// 다음 주기가 남은 행만 이어서 처리한다.</para>
/// <para><b>시즌을 새로 만들지 않는다</b> — 진행 중 시즌이 없으면 그 발화는 아무 일도 하지 않고 끝낸다.
/// 이 배치가 여는 것은 <b>다음</b> 시즌뿐이고, <b>첫 시즌 1행은 스키마 초기화 SQL</b>(<c>docs/공통/db-schema.sql</c>)이
/// 심는다 — 그 행이 없으면 보스러시는 계속 닫힌 상태로 남는다.</para>
/// <para><b>폴링하지 않는다</b> — 정산이 필요한 순간은 진행 중 시즌의 <c>end_at</c> 하나뿐이고 그 시각은 이미
/// DB에 있으므로, <see cref="NextFireTimeAsync"/>가 <b>그 시각을 그대로 발화 시각으로 삼는다</b>.
/// 며칠 뒤여도 그때까지 통째로 자고 정확히 그 시각에 깨어난다.</para>
/// <para><b>랭킹 캐시 최초 적재(워밍업)는 이 배치가 하지 않는다</b> — 서버 밖의 부트스트랩 스크립트
/// (<c>server_up_with_docker.py</c>)가 기동을 확인한 뒤 관리 API <c>POST /api/admin/boss-rush/rank/warmup</c>을
/// 한 번 호출한다(6.3). 정산이 만드는 캐시 변화(리더보드 TTL·다음 시즌 메타)는 그대로 이 배치가 낸다.</para>
/// <para><b>실행 시각: 진행 중 시즌의 종료 시각(<c>end_at</c>)</b>. 시즌이 없을 때만 매시 00 10 20 30 40 50분에
/// 다시 살핀다 — 값은 <see cref="BatchSettingConstants.BossRushSeason"/>에 있다.</para>
/// </summary>
public sealed class BossRushSeasonBatchScheduler : PeriodicBatchScheduler
{
    private readonly int _intervalSeconds;
    private readonly int _batchSize;
    private readonly MasterDbProvider _masterData;
    private readonly ILogger<BossRushSeasonBatchScheduler> _logger;
    private readonly IEventLogger _eventLogger;

    /// <summary>설정에서 실행 주기·1회 처리 상한을 읽고(없거나 0 이하이면 기본값), 마스터 데이터와 이벤트 로거를 주입받는다.</summary>
    public BossRushSeasonBatchScheduler(
        IServiceScopeFactory scopeFactory, IConfiguration configuration,
        MasterDbProvider masterData, ILogger<BossRushSeasonBatchScheduler> logger, IEventLogger eventLogger)
        : base(scopeFactory, logger, eventLogger)
    {
        _eventLogger = eventLogger;
        var interval = configuration.GetValue(
            BatchSettingConstants.BossRushSeason.IntervalSecondsKey, BatchSettingConstants.BossRushSeason.DefaultIntervalSeconds);
        var batchSize = configuration.GetValue(
            BatchSettingConstants.BossRushSeason.BatchSizeKey, BatchSettingConstants.BossRushSeason.DefaultBatchSize);
        _intervalSeconds = interval > 0 ? interval : BatchSettingConstants.BossRushSeason.DefaultIntervalSeconds;
        _batchSize = batchSize > 0 ? batchSize : BatchSettingConstants.BossRushSeason.DefaultBatchSize;
        _masterData = masterData;
        _logger = logger;
    }

    protected override TimeSpan Interval => TimeSpan.FromSeconds(_intervalSeconds);

    /// <summary>
    /// 발화 시각 = <b>진행 중 시즌의 종료 시각</b>(<c>end_at</c>). 정산이 필요한 순간은 그때 하나뿐이고 그 값은
    /// 이미 DB에 있으므로, 며칠 뒤여도 <b>그때까지 통째로 자고 정확히 그 시각에 깨어난다</b>(폴링 아님).
    /// <para>진행 중 시즌이 없거나 마스터가 아직 안 올라왔으면 <b>기본 간격</b>으로 되돌아가 다시 살핀다 —
    /// 정상 운영에서는 오지 않는 상태이고(정산이 항상 다음 시즌을 연다), 여기서 무기한 자면 첫 시즌이
    /// 등록돼도 아무도 깨우지 못해 배치가 영구히 멈춘다.</para>
    /// </summary>
    protected override async ValueTask<long> NextFireTimeAsync(
        IServiceScope scope, long lastFireUnix, long nowUnix, CancellationToken stoppingToken)
    {
        if (!_masterData.IsLoaded || _masterData.BossRushRule is null)
        {
            return BucketFireTime(lastFireUnix, nowUnix);
        }

        var repository = scope.ServiceProvider.GetRequiredService<IBossRushRepository>();

        // 정산 중(2)으로 남은 시즌이 있으면 직전 정산이 완주하지 못한 것이다 — 다음 시즌의 종료 시각까지
        // 자면 그동안 복구가 미뤄지므로(그 사이 순위·보상이 확정되지 않는다) 기본 간격으로 곧바로 이어서 돈다.
        if (await repository.GetSettlingSeasonAsync() is not null)
        {
            return BucketFireTime(lastFireUnix, nowUnix);
        }

        var current = await repository.GetRunningSeasonAsync();

        return current?.EndAt ?? BucketFireTime(lastFireUnix, nowUnix);
    }

    protected override string BatchName => "보스러시 시즌 정산 배치";

    protected override string BatchKey => "bossrush-season";

    /// <summary>
    /// 1회 작업: ①정산할 시즌 선정(정산 중으로 남은 시즌을 먼저 이어받고, 없으면 종료 시각이 지난 시즌 선점)
    /// → ②순위 확정·보상 메일 발급 → ③다음 시즌 개시 → ④시즌 종료·리더보드 TTL.
    /// <para>②가 완주하지 못하면 ③·④로 가지 않고 시즌을 <b>정산 중</b>으로 남긴다 — 다음 발화가 ①에서
    /// 이어받는다. ③을 ④보다 앞에 두는 것도 같은 이유다(중간에 죽어도 복구 진입점이 남는다).</para>
    /// <para><b>진행 중 시즌 확인은 여기서 하지 않는다</b> — 발화 시각 계산이 이미 그 일을 하고, 정산 대상
    /// 자체는 ①의 조건부 갱신이 원자적으로 고른다. 정산할 시즌이 없으면 조용히 끝낸다(로그 소음 방지).</para>
    /// </summary>
    protected override async Task<BatchCycleResult> RunCycleAsync(IServiceScope scope, CancellationToken stoppingToken)
    {
        var rule = _masterData.IsLoaded ? _masterData.BossRushRule : null;
        if (rule is null)
        {
            return BatchCycleResult.Idle; // 마스터 미적재·콘텐츠 미구성.
        }

        var repository = scope.ServiceProvider.GetRequiredService<IBossRushRepository>();
        var rankCache = scope.ServiceProvider.GetRequiredService<IBossRushRankCache>();
        var now = DateTimeUtil.UtcNow;
        var nowUnix = DateTimeUtil.ToUnixSeconds(now);

        // ① 정산할 시즌 선정.
        //    **정산 중(2)으로 남은 시즌을 먼저 본다** — 그 상태로 남아 있다는 것은 직전 정산이 끝까지 돌지
        //    못했다는 뜻이므로(프로세스 종료·예외), 새 시즌을 집기 전에 그 시즌부터 이어서 끝낸다.
        //    정산은 멱등하므로(순위 확정은 final_rank=0 조건부 갱신) 같은 코드를 다시 태우면 남은 사람만 처리된다.
        var season = await repository.GetSettlingSeasonAsync();
        if (season is not null)
        {
            _logger.ZLogWarning(
                $"보스러시 정산 미완료 시즌 감지 — 이어서 정산합니다: seasonId {season.SeasonId:@SeasonId} (종료 {season.EndAt:@EndAt})");
        }
        else
        {
            season = await repository.ClaimSeasonForSettlementAsync(nowUnix);
            if (season is null)
            {
                return BatchCycleResult.Idle;
            }

            _logger.ZLogInformation($"보스러시 시즌 정산 시작: seasonId {season.SeasonId:@SeasonId} (종료 {season.EndAt:@EndAt})");
        }

        var template = _masterData.GetMailTemplate(Constants.MailTemplate.BossRushRankReward);
        if (template is null)
        {
            // 보상 없이 순위만 확정한다 — 시즌을 정산중(2)에 묶어 두면 새 런을 계속 받지 못한다.
            _logger.ZLogError($"보스러시 순위 보상 메일 템플릿 미정의: templateCode {Constants.MailTemplate.BossRushRankReward:@TemplateCode} — mail_master 확인 필요(순위만 확정합니다)");
        }

        // ② 순위 확정 + 보상 메일 발급.
        var (settled, rewarded, failed, completed) = await SettleAsync(repository, season, template, nowUnix, stoppingToken);

        // 완주하지 못했으면 **시즌을 닫지 않는다** — 정산 중(2) 상태로 남겨 다음 발화가 ①에서 이어받게 한다.
        // 여기서 닫아 버리면 미확정자가 final_rank=0으로 영구히 남고 순위 보상도 받지 못한다.
        if (!completed)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                _logger.ZLogInformation(
                    $"보스러시 시즌 정산 중단(종료 요청): seasonId {season.SeasonId:@SeasonId} 확정 {settled:@Settled}건 — 다음 기동에서 이어서 정산합니다.");
            }
            else
            {
                // 남은 대상이 있는데 한 건도 진전되지 않았다 — 코드·데이터 결함이라 재시도만으로 풀리지 않는다.
                _logger.ZLogError(
                    $"보스러시 시즌 정산 진전 없음: seasonId {season.SeasonId:@SeasonId} 확정 {settled:@Settled}건 · 실패 {failed:@Failed}건 — 원인 확인이 필요합니다(다음 발화에서 재시도).");
            }

            return new BatchCycleResult(settled, 0, failed);
        }

        // ③ 다음 시즌 개시 + 시즌 메타 캐시 갱신(기획서 6.4).
        //    **시즌 종료(④)보다 앞에 둔다** — 이 사이에서 죽어도 지금 시즌이 정산 중(2)으로 남아 다음 발화가
        //    ①에서 이어받는다. 순서를 뒤집으면 "종료됐는데 다음 시즌이 없는" 상태가 만들어지고, 그때는
        //    진행 중 시즌도 정산 중 시즌도 없어 아무도 복구하지 못한다. 개시는 start_at 유니크로 멱등하다.
        var next = await repository.StartNextSeasonAsync(
            season.EndAt, season.EndAt + DateTimeUtil.DaysToSeconds(rule.SeasonPeriodDays));
        await rankCache.SetCurrentSeasonAsync(next);

        // ④ 시즌 종료(정산 중 → 종료) + 리더보드 TTL. 여기까지 와야 이 시즌의 정산이 끝난 것이다.
        await repository.CloseSeasonAsync(season.SeasonId, nowUnix);
        await rankCache.ExpireAsync(season.SeasonId, BatchSettingConstants.BossRushSeason.ClosedSeasonTtl);

        _logger.ZLogInformation($"보스러시 시즌 정산 완료: seasonId {season.SeasonId:@SeasonId} 순위 확정 {settled:@Settled}건 · 보상 발급 {rewarded:@Rewarded}건 → 다음 시즌 {next.SeasonId:@NextSeasonId}");

        // 처리량은 확정한 순위 건수로 센다(보상 발급 수는 상위 3위로 고정이라 처리량 지표가 되지 못한다).
        return new BatchCycleResult(settled, 0, failed);
    }

    /// <summary>
    /// 순위 미확정 기록을 정렬 순서로 읽어 순위를 확정하고, 매칭되는 보상 구간이 있으면(1~3위) 골드
    /// 보상 메일을 같은 트랜잭션에서 발급한다. 이미 확정된 행 수를 시작 순위로 이어 재진입에서도
    /// 순위가 어긋나지 않게 한다.
    /// <para><b>Completed</b>는 대상이 남지 않을 때까지 끝냈는지를 알린다. 종료 요청으로 중단됐거나,
    /// 남은 대상이 있는데 <b>한 건도 진전되지 않으면</b>(모두 예외) false다 — 후자를 그냥 두면 같은 대상을
    /// 쉬지 않고 다시 조회하는 무한 루프가 된다. 호출자는 false일 때 시즌을 닫지 않는다.</para>
    /// </summary>
    private async Task<(int Settled, int Rewarded, int Failed, bool Completed)> SettleAsync(
        IBossRushRepository repository, BossRushSeason season, MailTemplateDef? template,
        long nowUnix, CancellationToken stoppingToken)
    {
        var settled = 0;
        var rewarded = 0;
        var failed = 0;
        var completed = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            var targets = await repository.GetUnsettledRecordsAsync(season.SeasonId, _batchSize);
            if (targets.Count == 0)
            {
                completed = true;
                break;
            }

            // 이미 확정된 건수가 곧 이 페이지의 시작 순위 - 1이다(정렬 순서가 같으므로).
            var rank = await repository.CountSettledAsync(season.SeasonId) + 1;

            // 이 페이지에서 대상 집합이 실제로 줄어든 건수. 0이면 다음 조회가 같은 대상을 그대로 돌려주므로
            // 루프를 끊는다(대기 없이 도는 재조회를 막는다).
            var progressed = 0;

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
                    mail = MailUtil.Compose(
                        template, season.SeasonId.ToString(), rank.ToString(), nowUnix,
                        new[] { new MailAttachment(Constants.RewardType.Gold, 0, reward.RewardGold) });
                }

                try
                {
                    var settleResult = await repository.SettleRecordAsync(
                        season.SeasonId, target.UserId, rank, mail, nowUnix);
                    if (settleResult.Applied)
                    {
                        settled++;
                        if (mail is not null)
                        {
                            rewarded++;

                            // 순위 보상 메일 발급(5.8). 배치가 내는 라인이라 req_id가 없다.
                            _eventLogger.MailIssued(
                                target.UserId, settleResult.RewardMailId, mail, MailSource.BossRushRank);
                        }
                    }

                    // 예외 없이 끝난 건은 순위 미확정 집합에서 빠진다 — 다음 조회가 다시 돌려주지 않는다.
                    progressed++;

                    // **성공했을 때만** 다음 번호로 넘어간다. 실패한 자리에서 번호를 흘려보내면 뒤 사람들의
                    // 순위가 한 칸씩 밀리고, 실패분을 재시도할 때 이미 쓰인 번호와 중복된다.
                    rank++;
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.ZLogError(ex, $"보스러시 순위 확정 실패(seasonId {season.SeasonId:@SeasonId} userId {target.UserId:@UserId} rank {rank:@Rank}) — 다음 주기에 재시도합니다.");
                }
            }

            if (progressed == 0)
            {
                break;
            }
        }

        return (settled, rewarded, failed, completed);
    }

}
