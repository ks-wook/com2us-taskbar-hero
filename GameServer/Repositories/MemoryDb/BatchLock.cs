using CloudStructures;
using CloudStructures.Structures;
using GameServer.Repositories.MemoryDb.Interfaces;
using StackExchange.Redis;
using ZLogger;

namespace GameServer.Repositories.MemoryDb;

/// <summary>
/// 배치 리더 락 구현(<c>batch:lock:{배치키}</c>). 획득은 SET NX + TTL, 해제는 <b>소유자 확인 후 삭제</b>(Lua CAS)다 —
/// TTL이 먼저 만료돼 다른 인스턴스가 새로 잡은 락을 뒤늦게 지우는 사고를 막는다(락 해제의 표준 주의점).
/// <para>Redis 장애는 <see cref="BatchLockState.Unavailable"/>로 흡수한다 — 락은 <b>중복 실행 낭비를 줄이는 최적화</b>이고
/// 정합성은 배치 작업의 멱등성(조건부 갱신 선점·보관 기한 기준 삭제)이 보증하므로, 락이 없다고 배치를 멈추면 안 된다.</para>
/// </summary>
public sealed class BatchLock : MemoryDbBase, IBatchLock
{
    private readonly ILogger<BatchLock> _logger;

    /// <summary>이 프로세스의 락 소유자 식별값 — 어느 인스턴스가 리더였는지 Redis에서 확인할 수 있다(관측용).</summary>
    private readonly string _ownerId = $"{Environment.MachineName}:{Environment.ProcessId}";

    /// <summary>Redis 연결과 로거를 주입받는다.</summary>
    public BatchLock(RedisConnection connection, ILogger<BatchLock> logger)
        : base(connection, logger, "배치 리더 락")
        => _logger = logger;

    /// <summary>
    /// SET NX + TTL로 락을 시도한다. 이미 존재하면 <see cref="BatchLockState.Skip"/>(다른 인스턴스가 같은 주기를 실행 중).
    /// Redis 장애면 Warning 후 <see cref="BatchLockState.Unavailable"/> — 락 없이 진행한다(축소 운전).
    /// </summary>
    public async Task<BatchLockState> TryAcquireAsync(string batchKey, TimeSpan ttl)
    {
        try
        {
            var entry = new RedisString<string>(Connection, Key(batchKey), null);
            return await entry.SetAsync(_ownerId, ttl, When.NotExists)
                ? BatchLockState.Acquired
                : BatchLockState.Skip;
        }
        catch (Exception ex)
        {
            _logger.ZLogWarning(ex, $"{batchKey:@BatchKey}: Redis 리더 락 사용 불가 — 락 없이 진행합니다(축소 운전).");
            return BatchLockState.Unavailable;
        }
    }

    /// <summary>
    /// 락을 해제한다. 값이 자기 소유자 식별자일 때만 삭제하는 Lua CAS를 쓴다. 해제 실패는 Warning만 남긴다 —
    /// TTL이 안전망으로 남아 있어 락이 영구히 묶이지 않는다.
    /// </summary>
    public async Task ReleaseAsync(string batchKey)
    {
        try
        {
            // CloudStructures는 CAS 삭제용 구조체를 제공하지 않으므로 하부 StackExchange.Redis 연결로 Lua를 실행한다.
            await Connection.GetConnection().GetDatabase().ScriptEvaluateAsync(
                "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end",
                new RedisKey[] { Key(batchKey) },
                new RedisValue[] { _ownerId });
        }
        catch (Exception ex)
        {
            _logger.ZLogWarning(ex, $"{batchKey:@BatchKey}: 리더 락 해제 실패 — TTL 만료까지 다른 인스턴스가 스킵할 수 있습니다.");
        }
    }

    /// <summary>리더 락 Redis 키.</summary>
    private static string Key(string batchKey) => $"batch:lock:{batchKey}";
}
