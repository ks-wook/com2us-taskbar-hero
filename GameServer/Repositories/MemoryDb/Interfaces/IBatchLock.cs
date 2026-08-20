namespace GameServer.Repositories.MemoryDb.Interfaces;

/// <summary>배치 리더 락 시도 결과.</summary>
public enum BatchLockState
{
    /// <summary>획득 — 이 인스턴스가 리더이며 <see cref="IBatchLock.ReleaseAsync"/> 책임이 있다.</summary>
    Acquired,

    /// <summary>다른 인스턴스가 같은 주기를 실행 중 — 이번 주기는 건너뛴다.</summary>
    Skip,

    /// <summary>Redis 장애로 락을 쓸 수 없다 — 락 없이 진행한다(축소 운전).</summary>
    Unavailable,
}

/// <summary>
/// 주기 배치의 Redis 리더 락(<c>batch:lock:{배치키}</c>). scale-out 시 여러 인스턴스가 같은 주기를 동시에
/// 돌지 않게 하는 <b>상호 배제</b>만 담당한다("주기당 1회"는 각 인스턴스의 타이머가 페이싱한다).
/// </summary>
public interface IBatchLock
{
    /// <summary>SET NX + TTL로 락을 시도한다. TTL은 "락을 잡은 채 프로세스가 죽었을 때 자동으로 풀리게 하는 안전망"이다.</summary>
    Task<BatchLockState> TryAcquireAsync(string batchKey, TimeSpan ttl);

    /// <summary>락을 해제한다. <b>소유자가 자신일 때만</b> 지운다(Lua CAS).</summary>
    Task ReleaseAsync(string batchKey);
}
