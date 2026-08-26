using System.Diagnostics;
using GameServer.Logging;
using GameServer.Util;
using ZLogger;

namespace GameServer.Batch;

/// <summary>
/// 1회 발화가 처리한 건수. 골격이 이 값을 그대로 <c>batch.run</c> 이벤트 로그로 방출하므로
/// (로그 이벤트 정의 5.11), 파생 배치는 자기 도메인의 처리량을 이 셋으로 환산해 돌려준다.
/// </summary>
/// <param name="Processed">실제로 처리한 건수.</param>
/// <param name="Skipped">대상이었지만 그 사이 다른 경로가 처리해 건너뛴 건수.</param>
/// <param name="Failed">건별 예외로 실패해 다음 발화로 미룬 건수.</param>
public readonly record struct BatchCycleResult(int Processed, int Skipped, int Failed)
{
    /// <summary>할 일이 없었던 발화(대상 0건·선행 조건 미충족).</summary>
    public static readonly BatchCycleResult Idle = new(0, 0, 0);
}

/// <summary>
/// 주기 배치 공통 골격(trade 기획서 7.6.1, mail 기획서 6.5). 파생 클래스는 <b>발화 시각</b>과 <b>1회 작업</b>만
/// 정하고, "언제 도는가"는 이 골격이 보장한다.
///
/// <para><b>① 발화 시각은 절대 시각이다.</b> 대기 간격이 아니라 <see cref="NextFireTimeAsync"/>가 돌려주는
/// <b>유닉스초</b>가 실행 시점을 정한다. 기본 구현은 <see cref="Interval"/>로 나눈 <b>버킷 경계</b>
/// (5분 배치면 매시 :00·:05·:10…)이므로, <b>프로세스를 언제 띄웠는지와 무관하게 늘 같은 시각에 돈다.</b>
/// "직전 실행 + N초"라는 상대 간격으로 두면 기준점이 프로세스 기동 시각이 되어, 재기동할 때마다 집계
/// 시각이 조금씩 밀리고 히스토리 시계열의 눈금이 흔들린다.</para>
///
/// <para><b>② 분산 락을 쓰지 않는다.</b> 배치는 <b>BatchServer 1대</b>에서만 도는 것이 배포로 보장되므로
/// (compose는 <c>container_name</c> 고정, 오케스트레이터라면 <c>replicas: 1</c>), "N대 중 하나만"을 매 발화마다
/// 맞출 이유가 없다. 게임 API를 scale-out 해도 이 프로세스는 늘어나지 않는다 — 배치를 GameServer에서
/// 떼어낸 이유가 그것이다.</para>
///
/// <para><b>③ 밀린 발화는 현재 버킷 하나로 접는다.</b> 작업이 길어져 여러 버킷을 넘겼어도 하나만 따라잡는다
/// (지난 스냅샷은 되살릴 수 없고, 드레인 배치는 밀린 물량을 이번 발화에 함께 처리한다).
/// 기동 직후 현재 버킷을 따라잡을지는 <see cref="CatchUpOnStart"/>가 배치마다 정한다.</para>
///
/// <para><b>④ 재진입은 구조적으로 불가능하다.</b> 순차 루프라 이전 발화의 작업이 끝나야 다음 발화 시각을
/// 계산한다. 작업이 길어져 다음 버킷을 넘겼으면 그 다음 발화가 곧바로 이어진다.</para>
///
/// <para><b>⑤ 발화마다 DI 스코프를 만든다</b> — 호스티드 서비스(싱글턴)가 scoped 리포지토리를 안전하게 쓴다.
/// 스코프는 <see cref="NextFireTimeAsync"/>에도 넘어간다(발화 시각이 DB에 있는 배치가 있다).</para>
///
/// <para><b>⑥ 1회 실패는 Error 로그만 남기고 루프를 유지한다</b>(배치 사망으로 대상이 영구 방치되는 것 방지).</para>
/// </summary>
public abstract class PeriodicBatchScheduler : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger _logger;
    private readonly IEventLogger _eventLogger;

    /// <summary>스코프 팩토리(발화마다 scoped 의존성 해석용)·파생 클래스의 로거·이벤트 로거를 주입받는다.</summary>
    protected PeriodicBatchScheduler(
        IServiceScopeFactory scopeFactory, ILogger logger, IEventLogger eventLogger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _eventLogger = eventLogger;
    }

    /// <summary>
    /// 발화 간격. 파생 클래스가 설정(appsettings)에서 읽어 제공하며, 기본 발화 시각 계산의 <b>버킷 크기</b>다.
    /// </summary>
    protected abstract TimeSpan Interval { get; }

    /// <summary>배치 이름(로그 표시용).</summary>
    protected abstract string BatchName { get; }

    /// <summary>배치 키(<c>batch.run</c> 이벤트 로그의 식별자, ASCII. 예: "mail-gc").</summary>
    protected abstract string BatchKey { get; }

    /// <summary>
    /// 1회 작업. scope에서 scoped 의존성(리포지토리 등)을 해석해 사용한다.
    /// 반환한 처리 건수는 골격이 <c>batch.run</c> 이벤트 로그로 방출한다 — 할 일이 없었으면
    /// <see cref="BatchCycleResult.Idle"/>을 돌려준다.
    /// </summary>
    protected abstract Task<BatchCycleResult> RunCycleAsync(IServiceScope scope, CancellationToken stoppingToken);

    /// <summary>
    /// 기동 직후 <b>현재 버킷을 따라잡을지</b>(기본 true). 프로세스가 내려가 있는 동안 지나간 발화를
    /// 지금 소화할 것인가의 문제다.
    /// <para><b>true</b> — 드레인 배치(메일 GC·거래 만료)나 <b>덮어쓰기가 되는 스냅샷</b>(일 단위 히스토리는
    /// <c>log_date</c> 자연 키라 같은 날 다시 써도 덮어쓴다). 밀린 물량을 다음 발화까지 미룰 이유가 없다.</para>
    /// <para><b>false</b> — 발화 1회가 곧 시계열의 점 하나가 되는 스냅샷(동시 접속·시간 단위 히스토리).
    /// 여기서 따라잡으면 재기동할 때마다 같은 구간에 점이 하나 더 찍혀 그래프가 부풀려진다.
    /// 그 한 점을 얻는 것보다 눈금이 정확한 편이 낫다.</para>
    /// </summary>
    protected virtual bool CatchUpOnStart => true;

    /// <summary>
    /// 다음 발화 시각(유닉스초). 기본은 <see cref="Interval"/> 버킷 경계다(<see cref="BucketFireTime"/>).
    /// <b>과거 값이면 즉시 발화하고, 미래 값이면 그때까지 잔다.</b>
    /// <para>발화 시각이 DB에 있는 배치(예: 시즌 종료 시각)를 위해 <paramref name="scope"/>를 넘긴다.
    /// <paramref name="lastFireUnix"/>는 이 프로세스가 직전에 다룬 발화 시각이다(기동 직후 값은
    /// <see cref="CatchUpOnStart"/>에 따라 0 또는 현재 버킷 시작).
    /// 그보다 크지 않은 값을 돌려줘도 된다 — 골격이 기본 버킷으로 밀어 재시도한다.</para>
    /// </summary>
    protected virtual ValueTask<long> NextFireTimeAsync(
        IServiceScope scope, long lastFireUnix, long nowUnix, CancellationToken stoppingToken)
        => new(BucketFireTime(lastFireUnix, nowUnix));

    /// <summary>
    /// <see cref="Interval"/> 버킷 경계 기준의 발화 시각 = max(직전 발화 + 간격, 지금이 속한 버킷의 시작).
    /// <para>앞의 항이 정상 진행이고, 뒤의 항이 <b>작업이 길어져 밀린 발화를 현재 버킷 하나로 접는</b> 역할이다.</para>
    /// <para>기동 직후의 시작점은 <see cref="ExecuteAsync"/>가 <see cref="CatchUpOnStart"/>에 따라 정해 넘긴다 —
    /// 여기서 "직전 발화가 없다"를 따로 다루지 않는 이유는, 대기 중 루프를 돌 때마다 그 분기가 다시 타면서
    /// 발화 시각이 한 칸씩 밀려 <b>영영 돌지 않게</b> 되기 때문이다.</para>
    /// </summary>
    protected long BucketFireTime(long lastFireUnix, long nowUnix)
    {
        var interval = IntervalSeconds;
        var next = lastFireUnix + interval;
        var currentBucket = nowUnix / interval * interval;
        return next > currentBucket ? next : currentBucket;
    }

    /// <summary>발화 간격(초). 0 이하가 들어와 나눗셈이 배치를 영구히 죽이는 것만 막는다.</summary>
    private long IntervalSeconds => Math.Max(1L, (long)Interval.TotalSeconds);

    /// <summary>
    /// 배치 루프 본체: <b>발화 시각 계산 → 그 시각까지 대기 → 실행</b>의 반복이다.
    /// 작업이 실패해도 그 발화는 지나간 것으로 보고 다음 발화로 넘어간다.
    /// 종료 요청(stoppingToken)은 처리 중인 작업을 마무리한 뒤 정상 종료한다.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 시작점. 따라잡기를 하는 배치는 0에서 시작해 **현재 버킷**을 노리고(과거이므로 즉시 발화),
        // 하지 않는 배치는 현재 버킷을 **이미 다룬 것으로 보고** 시작해 다음 경계부터 돈다.
        var now0 = DateTimeUtil.NowUnixSeconds();
        var lastFire = CatchUpOnStart ? 0L : now0 / IntervalSeconds * IntervalSeconds;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();

                var now = DateTimeUtil.NowUnixSeconds();
                var fireAt = await NextFireTimeAsync(scope, lastFire, now, stoppingToken);

                // 파생이 이미 다룬 발화를 다시 돌려줬다(예: 정산이 실패해 시즌이 그대로 남았다).
                // 그대로 두면 같은 시각을 물고 쉬지 않고 도는 루프가 되므로 기본 버킷으로 밀어 재시도한다.
                if (fireAt <= lastFire)
                {
                    fireAt = BucketFireTime(lastFire, now);
                }

                if (fireAt > now)
                {
                    await WaitAsync(TimeSpan.FromSeconds(fireAt - now), stoppingToken);
                    continue;
                }

                // 작업이 실패하더라도 이 발화는 지나간 것으로 본다 — 재시도는 다음 발화가 한다.
                lastFire = fireAt;

                // 작업 소요를 잰다 — 이 값이 늘어나면 대상이 쌓이고 있다는 신호다(5.11).
                var stopwatch = Stopwatch.StartNew();
                var result = await RunCycleAsync(scope, stoppingToken);
                stopwatch.Stop();

                // 계정이 없는 시스템 이벤트라 uid가 없고, 요청에서 나온 값이 아니라 req_id도 없다.
                _eventLogger.Action(
                    Constants.EventLog.Tags.BatchRun, null,
                    new BatchRunEvent(
                        BatchKey, result.Processed, result.Skipped, result.Failed, stopwatch.ElapsedMilliseconds));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // 종료 요청 — 정상 종료
            }
            catch (Exception ex)
            {
                _logger.ZLogError(ex, $"{BatchName:@Batch} 발화 실행 실패 — 다음 발화에 재시도합니다.");

                // 발화 시각 계산 자체가 실패했다면(DB 장애 등) lastFire가 그대로라 곧바로 같은 계산을 다시 한다.
                // 그 사이를 쉬지 않고 도는 것만 막는다.
                await WaitAsync(BatchSettingConstants.ErrorRetryDelay, stoppingToken);
            }
        }
    }

    /// <summary>
    /// 지정 시간만큼 잔다. 상한(<c>BatchSettingConstants.MaxWait</c>)을 넘으면 잘라서 자고 루프가 발화 시각을 다시
    /// 계산한다 — 대기 API의 한계를 넘기지 않으면서, 며칠 뒤 발화도 폴링 없이 기다리기 위해서다.
    /// 종료 요청으로 취소되면 조용히 돌아간다(루프 조건이 종료를 판정한다).
    /// </summary>
    private static async Task WaitAsync(TimeSpan duration, CancellationToken stoppingToken)
    {
        if (duration > BatchSettingConstants.MaxWait)
        {
            duration = BatchSettingConstants.MaxWait;
        }

        try
        {
            await Task.Delay(duration, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // 종료 요청 — while 조건이 루프를 끝낸다.
        }
    }
}
