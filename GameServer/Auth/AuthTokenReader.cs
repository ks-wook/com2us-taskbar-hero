using CloudStructures;
using CloudStructures.Structures;

namespace GameServer.Auth;

public interface IAuthTokenReader
{
    /// <summary>Redis auth:token:{userId} 값 조회. 없으면 null(만료/폐기).</summary>
    Task<string?> GetAsync(long userId);
}

/// <summary>
/// 인증 토큰 Redis 조회기. GameServer는 SecretKey 없이 Redis에 저장된 토큰값과의 대조만으로 인증한다
/// (계정/로그인 기획서 4.2·5.5). 발급/삭제는 AccountServer가 담당하고, 여기서는 읽기만 한다.
/// </summary>
public sealed class RedisAuthTokenReader : IAuthTokenReader
{
    private readonly RedisConnection _connection;

    public RedisAuthTokenReader(RedisConnection connection) => _connection = connection;

    public async Task<string?> GetAsync(long userId)
    {
        var entry = new RedisString<string>(_connection, $"auth:token:{userId}", null);
        var result = await entry.GetAsync();
        return result.HasValue ? result.Value : null;
    }
}
