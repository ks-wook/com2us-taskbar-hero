using AccountServer.Repositories.MemoryDb.Interfaces;
using CloudStructures;
using CloudStructures.Structures;

namespace AccountServer.Repositories.MemoryDb;

/// <summary>
/// 인증 토큰 Redis 캐시. 키는 auth:token:{userId}, 값은 발급 토큰.
/// 런타임 인증(GameServer)은 이 값과의 대조로 이뤄지므로, 재로그인/로그아웃 시 값 갱신·삭제가 곧 세션 제어다.
/// <para><b>실패를 흡수하지 않는다</b> — 이 값은 캐시가 아니라 <b>세션의 정본</b>이라 실패 시 기본값으로
/// 축소 운전하지 않고 예외를 그대로 올린다(없는 세션을 통과시키는 것보다 안전하다).
/// GameServer의 <c>AuthTokenReader</c>가 <c>MemoryDbBase</c>를 상속하지 않는 것과 같은 이유로,
/// 여기에도 실패 흡수용 기반 클래스를 두지 않는다.</para>
/// </summary>
public sealed class AuthTokenCache : IAuthTokenCache
{
    private readonly RedisConnection _connection;

    /// <summary>Redis 연결을 주입받는다.</summary>
    public AuthTokenCache(RedisConnection connection) => _connection = connection;

    /// <summary>해당 계정의 토큰 키 구조체를 만든다(TTL은 쓰기 시점에 지정).</summary>
    private RedisString<string> Entry(long userId)
        => new(_connection, $"auth:token:{userId}", null);

    /// <summary>토큰을 TTL과 함께 기록한다(로그인 시 덮어써 이전 세션을 밀어낸다).</summary>
    public Task SetAsync(long userId, string token, TimeSpan ttl)
        => Entry(userId).SetAsync(token, ttl);

    /// <summary>현재 유효한 토큰 값을 읽는다. 키가 없으면 null(만료·로그아웃).</summary>
    public async Task<string?> GetAsync(long userId)
    {
        var result = await Entry(userId).GetAsync();
        return result.HasValue ? result.Value : null;
    }

    /// <summary>토큰 키를 삭제해 세션을 무효화한다(로그아웃).</summary>
    public Task DeleteAsync(long userId)
        => Entry(userId).DeleteAsync();
}
