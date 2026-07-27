using CloudStructures;
using CloudStructures.Structures;
using StackExchange.Redis;

namespace GameServer.Batch;

/// <summary>
/// 주기 배치 공통 골격(trade 기획서 7.6.1, mail 기획서 6.5). 파생 클래스는 실행 주기(<see cref="Interval"/>)와
/// 1주기 작업(<see cref="RunCycleAsync"/>)만 구현한다. 골격이 보장하는 것:
/// <para>· <see cref="PeriodicTimer"/> + WaitForNextTickAsync 루프 — 이전 주기가 끝나야 다음 tick을 기다리므로 재진입이 구조적으로 불가.</para>
/// <para>· 기동 직후 즉시 1회 실행 — 서버 중단 동안 쌓인 대상을 바로 소화.</para>
/// <para>· 주기마다 DI 스코프 생성 — 호스티드 서비스(싱글턴)가 scoped 리포지토리를 안전하게 사용.</para>
/// <para>· Redis 리더 락(batch:lock:{키}, SET NX + TTL=주기) — scale-out 시 먼저 락을 잡은 인스턴스만 그 주기를 실행하고
///   나머지는 스킵한다. 락은 해제하지 않고 TTL로 자연 만료시킨다(주기당 1회 실행 상한). Redis 장애 시에는 락 없이
///   진행한다(축소 운전 — 배치 작업은 멱등/CAS 선점으로 중복 실행에도 안전해야 하며, 락은 낭비 제거용이다).</para>
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

    /// <summary>배치 이름(로그 표시용).</summary>
    protected abstract string BatchName { get; }

    /// <summary>리더 락 키 접미사(ASCII, 예: "mail-gc"). 락 키는 batch:lock:{BatchKey}.</summary>
    protected abstract string BatchKey { get; }

    /// <summary>1주기 작업. scope에서 scoped 의존성(리포지토리 등)을 해석해 사용한다.</summary>
    protected abstract Task RunCycleAsync(IServiceScope scope, CancellationToken stoppingToken);

    /// <summary>
    /// 배치 루프 본체: 즉시 1회 실행 후 Interval 주기로 반복한다. 주기마다 Redis 리더 락을 먼저 시도해
    /// 획득한 경우에만 작업을 수행한다(미획득 = 다른 인스턴스가 이번 주기를 실행 중/완료 — 스킵).
    /// 주기 실행 중 예외는 Error 로그 후 루프를 유지하고, 종료 요청(stoppingToken)은 처리 중인 주기를 마무리한 뒤 정상 종료한다.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        do
        {
            try
            {
                if (!await TryAcquireLeaderLockAsync())
                {
                    _logger.LogDebug("{Batch}: 리더 락 미획득 — 이번 주기는 다른 인스턴스가 실행(스킵).", BatchName);
                    continue;
                }

                using var scope = _scopeFactory.CreateScope();
                await RunCycleAsync(scope, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // 종료 요청 — 정상 종료
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Batch} 주기 실행 실패 — 다음 주기에 재시도합니다.", BatchName);
            }
        }
        while (await WaitForNextTickAsync(timer, stoppingToken));
    }

    /// <summary>
    /// 리더 락(batch:lock:{BatchKey})을 SET NX + TTL(=Interval)로 시도한다. 획득 실패(이미 존재)는 false.
    /// 락은 명시 해제하지 않고 TTL로 자연 만료시켜 "주기당 인스턴스 1대 실행"을 보장한다.
    /// Redis 장애(예외)면 Warning 후 true — 락 없이 진행한다(축소 운전, 작업 멱등성이 정합성을 보증).
    /// </summary>
    private async Task<bool> TryAcquireLeaderLockAsync()
    {
        try
        {
            var lockEntry = new RedisString<string>(_redis, $"batch:lock:{BatchKey}", null);
            return await lockEntry.SetAsync(_instanceId, Interval, When.NotExists);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Batch}: Redis 리더 락 사용 불가 — 락 없이 진행합니다(축소 운전).", BatchName);
            return true;
        }
    }

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
