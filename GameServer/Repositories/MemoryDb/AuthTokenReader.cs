using CloudStructures;
using CloudStructures.Structures;
using GameServer.Repositories.MemoryDb.Interfaces;

namespace GameServer.Repositories.MemoryDb;

/// <summary>
/// 인증 토큰 Redis 조회기(<c>auth:token:{userId}</c> 읽기 전용). GameServer는 SecretKey 없이 Redis에 저장된
/// 토큰값과의 대조만으로 인증한다(계정/로그인 기획서 4.2·5.5). 발급·삭제는 AccountServer가 담당한다.
/// <para><b>실패를 흡수하지 않는다</b> — 이 값은 캐시가 아니라 <b>세션의 정본</b>이라
/// <see cref="MemoryDbBase.SafeAsync{T}"/>를 쓰지 않는다. Redis 장애면 예외가 미들웨어로 올라가 요청이 거부되는 것이
/// 맞다(없는 세션을 통과시키는 것보다 안전하다). 그래서 이 클래스는 <see cref="MemoryDbBase"/>를 상속하지 않는다.</para>
/// </summary>
public sealed class AuthTokenReader : IAuthTokenReader
{
    private readonly RedisConnection _connection;

    /// <summary>Redis 연결을 주입받는다.</summary>
    public AuthTokenReader(RedisConnection connection) => _connection = connection;

    /// <summary>토큰 값을 읽는다. 키가 없으면 null(만료·로그아웃).</summary>
    public async Task<string?> GetAsync(long userId)
    {
        var entry = new RedisString<string>(_connection, $"auth:token:{userId}", null);
        var result = await entry.GetAsync();
        return result.HasValue ? result.Value : null;
    }
}
