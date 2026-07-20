using System.Security.Cryptography;
using System.Text;

namespace AccountServer.Auth;

/// <summary>
/// 커스텀 HMAC-SHA256 인증 토큰 생성기(계정/로그인 기획서 4.1).
/// JWT를 쓰지 않는다. SecretKey는 하드코딩/appsettings 금지 → User Secrets/환경변수(Security:SecretKey)에서 읽는다.
/// GameServer는 SecretKey 없이 Redis 대조로만 검증하므로, 서명 재계산은 발급자(AccountServer)만 수행한다.
/// </summary>
public sealed class TokenGenerator
{
    private readonly byte[] _secretKey;

    public TokenGenerator(IConfiguration configuration)
    {
        var key = configuration["Security:SecretKey"]
            ?? throw new InvalidOperationException(
                "Security:SecretKey 가 설정되지 않았습니다. User Secrets 또는 환경변수로 주입하세요.");
        _secretKey = Encoding.UTF8.GetBytes(key);
    }

    /// <summary>
    /// token = Base64("{userId}:{timestamp}:{salt}:{hash}")
    /// hash = HMAC-SHA256(SecretKey, "{userId}:{timestamp}:{salt}")
    /// </summary>
    public string GenerateToken(long userId)
    {
        var salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var tokenData = $"{userId}:{timestamp}:{salt}";

        using var hmac = new HMACSHA256(_secretKey);
        var hash = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(tokenData)));

        var payload = $"{tokenData}:{hash}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
    }
}
