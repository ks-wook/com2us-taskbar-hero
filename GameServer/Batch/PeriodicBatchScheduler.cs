using GameServer.Repositories.MemoryDb.Interfaces;
using ZLogger;

namespace GameServer.Batch;

/// <summary>
/// 주기 배치 공통 골격(trade 기획서 7.6.1, mail 기획서 6.5). 파생 클래스는 실행 주기(<see cref="Interval"/>)와
/// 1주기 작업(<see cref="RunCycleAsync"/>)만 구현한다. 골격이 보장하는 것:
/// <para>· 순차 루프(주기 본체 → 대기 → 반복) — 이전 주기가 끝나야 다음 대기가 시작되므로 재진입이 구조적으로 불가.</para>
/// <para>· 기동 직후 즉시 1회 실행 — 서버 중단 동안 쌓인 대상을 바로 소화.</para>
/// <para>· 대기 간격은 <see cref="NextDelay"/>가 정한다 — 기본은 고정 <see cref="Interval"/>이고,
///   <b>발화 시각을 미리 아는 배치</b>(예: 시즌 종료 시각이 DB에 있는 정산 배치)는 이를 재정의해 그 시각까지 잔다.
///   그 경우 대기 상한은 <see cref="MaxDelay"/>가 정한다. <b>깨어나도 할 일이 없는 것이 확실하면</b>
///   <see cref="Timeout.InfiniteTimeSpan"/>을 반환해 무기한 대기하고, 외부에서 <see cref="Wake"/>로 깨운다.</para>
/// <para>· 주기마다 DI 스코프 생성 — 호스티드 서비스(싱글턴)가 scoped 리포지토리를 안전하게 사용.</para>
/// <para>· Redis 리더 락(<see cref="IBatchLock"/>, TTL=<see cref="LeaseDuration"/>) — scale-out 시 동시에 같은 주기를
///   돌지 않게 한다. <b>락은 주기가 끝나면 즉시 해제하고</b>, TTL은 "락을 잡은 채 프로세스가 죽었을 때 자동으로
///   풀리게 하는 안전망"일 뿐이다. 따라서 TTL은 실행 주기가 아니라 <b>1주기 실행 시간의 상한</b>에 맞춘다
///   — 주기와 같게 두면(옛 방식) 크래시·재기동 시 다음 주기까지 배치가 통째로 멈춘다(주기가 24시간이면 하루).</para>
/// <para>· 락은 <b>상호 배제</b>만 담당하고 "주기당 1회"는 각 인스턴스의 타이머가 페이싱한다. 인스턴스가 여러 대면
///   한 주기에 여러 번 돌 수 있으나, 배치 작업은 멱등(조건부 갱신 선점·보관 기한 기준 삭제)이라 중복 실행이
///   자산을 손상시키지 않는다. Redis 장애 시에도 락 없이 진행한다(축소 운전 — 락은 낭비 제거용이다).</para>
/// <para>· 락의 획득·해제 메커니즘(SET NX · 소유자 확인 후 Lua CAS 삭제)은 <see cref="IBatchLock"/>이 담당한다 —
///   Redis 접근은 전부 Repositories/MemoryDb 계층에 있고, 이 골격은 "언제 잡고 언제 놓는가"만 정한다.</para>
/// <para>· 루프 예외 가드 — 1주기 실패를 Error로 남기고 루프를 유지한다(배치 사망으로 대상이 영구 방치되는 것 방지).</para>
/// </summary>
public abstract class PeriodicBatchScheduler : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IBatchLock _batchLock;
    private readonly ILogger _logger;

    /// <summary>
    /// 외부 기상 신호. 대기 중 <see cref="Wake"/>가 호출되면 남은 대기를 건너뛰고 즉시 다음 주기를 돈다.
    /// 용량 1이라 대기 중이 아닐 때 온 신호는 <b>1개만 보관</b>되어 다음 대기를 즉시 통과시킨다
    /// (신호가 유실되지도, 쌓여서 연속 실행을 만들지도 않는다).
    /// </summary>
    private readonly SemaphoreSlim _wakeSignal = new(0, 1);

    /// <summary>스코프 팩토리(주기마다 scoped 의존성 해석용)·리더 락·파생 클래스의 로거를 주입받는다.</summary>
    protected PeriodicBatchScheduler(IServiceScopeFactory scopeFactory, IBatchLock batchLock, ILogger logger)
    {
        _scopeFactory = scopeFactory;
        _batchLock = batchLock;
        _logger = logger;
    }

    /// <summary>
    /// 대기 중인 배치를 즉시 깨워 1주기를 돌린다. <b>무기한 대기</b>(<see cref="NextDelay"/>가
    /// <see cref="Timeout.InfiniteTimeSpan"/>을 반환한 상태)에 들어간 배치를 다시 돌리는 수단이다 —
    /// 할 일이 생겼다는 사실을 아는 쪽(운영 조작 등)이 알려 주는 편이, 배치가 주기적으로 확인하는 것보다
    /// 확실하고 낭비가 없다.
    /// </summary>
    public void Wake()
    {
        if (_wakeSignal.CurrentCount > 0)
        {
            return; // 이미 보관된 신호가 있다 — 다음 대기가 그것으로 통과한다.
        }

        try
        {
            _wakeSignal.Release();
        }
        catch (SemaphoreFullException)
        {
            // 경합으로 그 사이 다른 호출이 넣었다 — 신호는 이미 있으므로 할 일이 없다.
        }
    }

    /// <summary>기상 신호를 정리한다.</summary>
    public override void Dispose()
    {
        _wakeSignal.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>실행 주기. 파생 클래스가 설정(appsettings)에서 읽어 제공한다.</summary>
    protected abstract TimeSpan Interval { get; }

    /// <summary>
    /// 다음 주기까지의 대기 시간(기본 = 고정 <see cref="Interval"/>). <b>발화 시각을 미리 아는 배치</b>는
    /// 이를 재정의해 그 시각까지만 자면 된다 — 알고 있는 시각을 주기적으로 찔러 보는 폴링이 사라진다.
    /// <para>반환값은 [<see cref="MinDelay"/>, <see cref="MaxDelay"/>]로 클램프된다. 예외로
    /// <see cref="Timeout.InfiniteTimeSpan"/>을 반환하면 <b>클램프 없이 무기한 대기</b>하며,
    /// <see cref="Wake"/> 신호나 종료 요청으로만 깨어난다 — <b>깨어나도 할 일이 없는 것이 확실한 배치</b>가
    /// 헛도는 주기를 없애는 데 쓴다.</para>
    /// </summary>
    protected virtual TimeSpan NextDelay() => Interval;

    /// <summary>
    /// 다음 주기 대기의 상한(기본 <see cref="Interval"/>). 기본값은 "적어도 주기마다 한 번은 돈다"는
    /// <b>안전망</b>이다 — <see cref="NextDelay"/>를 재정의하지 않은 배치는 이 값이 곧 실행 주기가 된다.
    /// <para>발화 시각을 정확히 아는 배치는 이를 <b>주기보다 크게</b> 재정의해 상한을 사실상 풀 수 있다.
    /// 그래야 "아는 시각까지 자기"가 주기에 잘려 폴링으로 되돌아가지 않는다.</para>
    /// </summary>
    protected virtual TimeSpan MaxDelay => Interval;

    /// <summary>
    /// 다음 주기 대기의 하한(기본 1초). <b>0·음수 대기로 루프가 쉬지 않고 도는 것만</b> 막는 안전장치다.
    /// 재시도를 얼마나 늦출지는 <see cref="NextDelay"/>를 재정의한 쪽이 정한다 — 여기서 크게 잡으면
    /// 발화 시각이 코앞인 정상 대기까지 뒤로 밀려 정확도가 깨진다.
    /// </summary>
    protected virtual TimeSpan MinDelay => Constants.Batch.MinDelay;

    /// <summary>
    /// 리더 락 TTL의 상한(기본 5분). 1주기 실행 시간의 상한으로 잡는다 — 두 배치 모두 1주기가
    /// <c>BatchSize</c>로 제한된 짧은 트랜잭션의 반복이라 수 초 규모이므로 5분은 충분한 여유다.
    /// 실제 TTL은 <see cref="LeaseDuration"/>이며, 주기가 이보다 짧으면 주기를 쓴다.
    /// </summary>
    protected virtual TimeSpan MaxLockTtl => Constants.Batch.MaxLockTtl;

    /// <summary>
    /// 락 TTL = min(<see cref="Interval"/>, <see cref="MaxLockTtl"/>). 주기가 짧은 배치에서는 TTL이 주기를 넘지
    /// 않게 하고(죽은 락이 여러 주기를 막지 않도록), 주기가 긴 배치에서는 상한(5분)으로 제한한다.
    /// </summary>
    private TimeSpan LeaseDuration => Interval < MaxLockTtl ? Interval : MaxLockTtl;

    /// <summary>배치 이름(로그 표시용).</summary>
    protected abstract string BatchName { get; }

    /// <summary>리더 락 키 접미사(ASCII, 예: "mail-gc"). 락 키는 batch:lock:{BatchKey}.</summary>
    protected abstract string BatchKey { get; }

    /// <summary>1주기 작업. scope에서 scoped 의존성(리포지토리 등)을 해석해 사용한다.</summary>
    protected abstract Task RunCycleAsync(IServiceScope scope, CancellationToken stoppingToken);

    /// <summary>
    /// 배치 루프 본체: 즉시 1회 실행 후 <see cref="NextDelay"/>가 정한 간격으로 반복한다(기본은 Interval,
    /// 발화 시각을 아는 배치는 그 시각까지). 주기마다 Redis 리더 락을 먼저 시도해
    /// 획득한 경우에만 작업을 수행하고(미획득 = 다른 인스턴스가 지금 같은 주기를 실행 중 — 스킵),
    /// <b>작업이 끝나면(예외로 끝나도) 락을 즉시 해제</b>한다. 주기 실행 중 예외는 Error 로그 후 루프를 유지하고,
    /// 종료 요청(stoppingToken)은 처리 중인 주기를 마무리한 뒤 정상 종료한다.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var cycleRan = false;

        do
        {
            BatchLockState? lockState = null;
            cycleRan = false;
            try
            {
                lockState = await _batchLock.TryAcquireAsync(BatchKey, LeaseDuration);
                if (lockState == BatchLockState.Skip)
                {
                    _logger.ZLogDebug($"{BatchName:@Batch}: 리더 락 미획득 — 다른 인스턴스가 실행 중이므로 이번 주기는 스킵.");
                    continue;
                }

                using var scope = _scopeFactory.CreateScope();
                await RunCycleAsync(scope, stoppingToken);
                cycleRan = true;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // 종료 요청 — 정상 종료(락 해제는 finally가 담당)
            }
            catch (Exception ex)
            {
                _logger.ZLogError(ex, $"{BatchName:@Batch} 주기 실행 실패 — 다음 주기에 재시도합니다.");
            }
            finally
            {
                // 실패로 끝났어도 해제한다 — 락은 "지금 돌고 있다"는 표시일 뿐이고, 재시도는 다음 주기가 한다.
                // 락 없이 진행한 경우(Unavailable)는 지울 대상이 없다.
                if (lockState == BatchLockState.Acquired)
                {
                    await _batchLock.ReleaseAsync(BatchKey);
                }
            }
        }
        while (await WaitForNextCycleAsync(cycleRan, stoppingToken));
    }

    /// <summary>
    /// 다음 주기까지 대기한다. 대기 시간은 <see cref="NextDelay"/>가 정하고 [<see cref="MinDelay"/>,
    /// <see cref="MaxDelay"/>] 범위로 클램프하며, <see cref="Timeout.InfiniteTimeSpan"/>이면 무기한 기다린다.
    /// <see cref="Wake"/> 신호가 오면 남은 대기를 건너뛴다. 종료 요청으로 취소되면 false(루프 종료).
    /// <para>대기가 <b>주기 본체가 끝난 뒤</b>에 시작하므로 이전 주기가 끝나기 전에는 다음 주기가 시작될 수
    /// 없다 — 재진입이 구조적으로 불가능하다는 성질은 그대로다.</para>
    /// </summary>
    private async Task<bool> WaitForNextCycleAsync(bool cycleRan, CancellationToken stoppingToken)
    {
        // 주기가 실제로 돌지 않았으면(리더 락 스킵·예외) 파생이 계산한 대기 시간을 믿을 수 없다 —
        // 그 값은 직전 주기가 알아낸 사실에서 나오기 때문이다. 이때는 기본 주기로 재시도한다.
        var delay = cycleRan ? NextDelay() : Interval;

        if (delay != Timeout.InfiniteTimeSpan)
        {
            if (delay > MaxDelay)
            {
                delay = MaxDelay;
            }

            if (delay < MinDelay)
            {
                delay = MinDelay;
            }
        }

        try
        {
            // 대기 중 Wake() 신호가 오면 남은 시간을 건너뛰고 즉시 다음 주기로 넘어간다.
            await _wakeSignal.WaitAsync(delay, stoppingToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
