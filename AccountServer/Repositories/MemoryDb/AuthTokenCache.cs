using CloudStructures;
using CloudStructures.Structures;

namespace AccountServer.Repositories.MemoryDb;

public interface IAuthTokenCache
{
    Task SetAsync(long userId, string token, TimeSpan ttl);
    Task<string?> GetAsync(long userId);
    Task DeleteAsync(long userId);
}

/// <summary>
/// 인증 토큰 Redis 캐시. 키는 auth:token:{userId}, 값은 발급 토큰.
/// 런타임 인증(GameServer)은 이 값과의 대조로 이뤄지므로, 재로그인/로그아웃 시 값 갱신·삭제가 곧 세션 제어다.
/// </summary>
public sealed class AuthTokenCache : IAuthTokenCache
{
    private readonly RedisConnection _connection;

    public AuthTokenCache(RedisConnection connection) => _connection = connection;

    private RedisString<string> Entry(long userId)
        => new(_connection, $"auth:token:{userId}", null);

    public Task SetAsync(long userId, string token, TimeSpan ttl)
        => Entry(userId).SetAsync(token, ttl);

    public async Task<string?> GetAsync(long userId)
    {
        var result = await Entry(userId).GetAsync();
        return result.HasValue ? result.Value : null;
    }

    public Task DeleteAsync(long userId)
        => Entry(userId).DeleteAsync();
}
