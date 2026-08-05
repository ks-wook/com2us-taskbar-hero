using CloudStructures;
using CloudStructures.Structures;
using StackExchange.Redis;
using ZLogger;

namespace GameServer.Batch;

/// <summary>
/// 주기 배치 공통 골격(trade 기획서 7.6.1, mail 기획서 6.5). 파생 클래스는 실행 주기(<see cref="Interval"/>)와
/// 1주기 작업(<see cref="RunCycleAsync"/>)만 구현한다. 골격이 보장하는 것:
/// <para>· <see cref="PeriodicTimer"/> + WaitForNextTickAsync 루프 — 이전 주기가 끝나야 다음 tick을 기다리므로 재진입이 구조적으로 불가.</para>
/// <para>· 기동 직후 즉시 1회 실행 — 서버 중단 동안 쌓인 대상을 바로 소화.</para>
/// <para>· 주기마다 DI 스코프 생성 — 호스티드 서비스(싱글턴)가 scoped 리포지토리를 안전하게 사용.</para>
/// <para>· Redis 리더 락(batch:lock:{키}, SET NX + TTL=<see cref="LeaseDuration"/>) — scale-out 시 동시에 같은 주기를
///   돌지 않게 한다. <b>락은 주기가 끝나면 즉시 해제하고</b>, TTL은 "락을 잡은 채 프로세스가 죽었을 때 자동으로
///   풀리게 하는 안전망"일 뿐이다. 따라서 TTL은 실행 주기가 아니라 <b>1주기 실행 시간의 상한</b>에 맞춘다
///   — 주기와 같게 두면(옛 방식) 크래시·재기동 시 다음 주기까지 배치가 통째로 멈춘다(주기가 24시간이면 하루).</para>
/// <para>· 락은 <b>상호 배제</b>만 담당하고 "주기당 1회"는 각 인스턴스의 타이머가 페이싱한다. 인스턴스가 여러 대면
///   한 주기에 여러 번 돌 수 있으나, 배치 작업은 멱등(조건부 갱신 선점·보관 기한 기준 삭제)이라 중복 실행이
///   자산을 손상시키지 않는다. Redis 장애 시에도 락 없이 진행한다(축소 운전 — 락은 낭비 제거용이다).</para>
/// <para>· 해제는 <b>소유자 확인 후</b> 한다(Lua CAS). TTL이 먼저 만료돼 다른 인스턴스가 새로 잡은 락을
///   뒤늦게 지우는 사고를 막는다.</para>
/// <para>· 루프 예외 가드 — 1주기 실패를 Error로 남기고 루프를 유지한다(배치 사망으로 대상이 영구 방치되는 것 방지).</para>
/// </summary>
public abstract class PeriodicBatchService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RedisConnection _redis;
    private readonly ILogger _logger;

    /// <summary>이 인스턴스의 락 소유자 식별값(관측용 — 어느 인스턴스가 리더였는지 Redis에서 확인 가능).</summary>
    private readonly string _instanceId = $"{Environment.MachineName}:{Environment.ProcessId}";

    /// <summary>스코프 팩토리(주기마다 scoped 의존성 해석용)·Redis 연결(리더 락)·파생 클래스의 로거를 주입받는다.</summary>
    protected PeriodicBatchService(IServiceScopeFactory scopeFactory, RedisConnection redis, ILogger logger)
    {
        _scopeFactory = scopeFactory;
        _redis = redis;
        _logger = logger;
    }

    /// <summary>실행 주기. 파생 클래스가 설정(appsettings)에서 읽어 제공한다.</summary>
    protected abstract TimeSpan Interval { get; }

    /// <summary>
    /// 리더 락 TTL의 상한(기본 5분). 1주기 실행 시간의 상한으로 잡는다 — 두 배치 모두 1주기가
    /// <c>BatchSize</c>로 제한된 짧은 트랜잭션의 반복이라 수 초 규모이므로 5분은 충분한 여유다.
    /// 실제 TTL은 <see cref="LeaseDuration"/>이며, 주기가 이보다 짧으면 주기를 쓴다.
    /// </summary>
    protected virtual TimeSpan MaxLockTtl => TimeSpan.FromMinutes(5);

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
    /// 배치 루프 본체: 즉시 1회 실행 후 Interval 주기로 반복한다. 주기마다 Redis 리더 락을 먼저 시도해
    /// 획득한 경우에만 작업을 수행하고(미획득 = 다른 인스턴스가 지금 같은 주기를 실행 중 — 스킵),
    /// <b>작업이 끝나면(예외로 끝나도) 락을 즉시 해제</b>한다. 주기 실행 중 예외는 Error 로그 후 루프를 유지하고,
    /// 종료 요청(stoppingToken)은 처리 중인 주기를 마무리한 뒤 정상 종료한다.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        do
        {
            var lockState = LockState.None;
            try
            {
                lockState = await TryAcquireLeaderLockAsync();
                if (lockState == LockState.Skip)
                {
                    _logger.ZLogDebug($"{BatchName:@Batch}: 리더 락 미획득 — 다른 인스턴스가 실행 중이므로 이번 주기는 스킵.");
                    continue;
                }

                using var scope = _scopeFactory.CreateScope();
                await RunCycleAsync(scope, stoppingToken);
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
                // 락 없이 진행한 경우(NoRedis)는 지울 대상이 없다.
                if (lockState == LockState.Acquired)
                {
                    await ReleaseLeaderLockAsync();
                }
            }
        }
        while (await WaitForNextTickAsync(timer, stoppingToken));
    }

    /// <summary>리더 락 시도 결과. Acquired=획득(해제 책임 있음) · Skip=다른 인스턴스가 실행 중 · NoRedis=Redis 장애로 락 없이 진행.</summary>
    private enum LockState
    {
        None,
        Acquired,
        Skip,
        NoRedis,
    }

    /// <summary>
    /// 리더 락(batch:lock:{BatchKey})을 SET NX + TTL(<see cref="LeaseDuration"/>)로 시도한다.
    /// 이미 존재하면 Skip(다른 인스턴스가 같은 주기를 실행 중). TTL은 <b>이 주기가 끝나기 전에 프로세스가 죽었을 때
    /// 락이 자동으로 풀리게 하는 안전망</b>이며, 정상 종료 시에는 <see cref="ReleaseLeaderLockAsync"/>가 곧바로 지운다.
    /// Redis 장애(예외)면 Warning 후 NoRedis — 락 없이 진행한다(축소 운전, 작업 멱등성이 정합성을 보증).
    /// </summary>
    private async Task<LockState> TryAcquireLeaderLockAsync()
    {
        try
        {
            var lockEntry = new RedisString<string>(_redis, LockKey, null);
            return await lockEntry.SetAsync(_instanceId, LeaseDuration, When.NotExists)
                ? LockState.Acquired
                : LockState.Skip;
        }
        catch (Exception ex)
        {
            _logger.ZLogWarning(ex, $"{BatchName:@Batch}: Redis 리더 락 사용 불가 — 락 없이 진행합니다(축소 운전).");
            return LockState.NoRedis;
        }
    }

    /// <summary>
    /// 리더 락을 해제한다. <b>값이 자기 인스턴스 식별자일 때만</b> 삭제하는 Lua CAS를 쓴다 — TTL이 먼저 만료돼
    /// 다른 인스턴스가 새로 잡은 락을 뒤늦게 지우는 사고를 막는다(락 해제의 표준 주의점).
    /// 해제 실패(Redis 장애 등)는 Warning만 남긴다 — TTL이 안전망으로 남아 있어 락이 영구히 묶이지 않는다.
    /// </summary>
    private async Task ReleaseLeaderLockAsync()
    {
        try
        {
            // CloudStructures는 CAS 삭제용 구조체를 제공하지 않으므로 하부 StackExchange.Redis 연결로 Lua를 실행한다.
            await _redis.GetConnection().GetDatabase().ScriptEvaluateAsync(
                "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end",
                new RedisKey[] { LockKey },
                new RedisValue[] { _instanceId });
        }
        catch (Exception ex)
        {
            _logger.ZLogWarning(ex, $"{BatchName:@Batch}: 리더 락 해제 실패 — TTL 만료까지 다른 인스턴스가 스킵할 수 있습니다.");
        }
    }

    /// <summary>리더 락 Redis 키(batch:lock:{BatchKey}).</summary>
    private string LockKey => $"batch:lock:{BatchKey}";

    /// <summary>다음 tick까지 대기한다. 종료 요청으로 취소되면 false(루프 종료).</summary>
    private static async Task<bool> WaitForNextTickAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
