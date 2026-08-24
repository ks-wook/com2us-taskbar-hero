using System.Security.Cryptography;
using System.Text;

namespace AccountServer.Auth;

/// <summary>
/// 커스텀 HMAC-SHA256 인증 토큰 생성기(계정/로그인 기획서 4.1).
/// JWT를 쓰지 않는다. SecretKey는 <c>Security:SecretKey</c> 설정에서 읽는다 — <b>appsettings.json에 로컬
/// 개발용 기본값</b>을 두어 클론만 하면 아무 준비 없이 뜨게 하고, 운영·배포에서는 환경변수
/// (<c>Security__SecretKey</c>)나 User Secrets로 덮어쓴다(설정 우선순위상 그쪽이 이긴다).
/// <b>소스에는 절대 넣지 않는다</b> — 코드가 키를 들고 있으면 어느 배포에서도 바꿀 수 없다.
/// GameServer는 SecretKey 없이 Redis 대조로만 검증하므로, 서명 재계산은 발급자(AccountServer)만 수행한다.
/// </summary>
public sealed class TokenGenerator
{
    private readonly byte[] _secretKey;

    public TokenGenerator(IConfiguration configuration)
    {
        var key = configuration["Security:SecretKey"];
        if (string.IsNullOrWhiteSpace(key))
        {
            // appsettings.json에 기본값이 있으므로 여기까지 오는 것은 그 값을 지웠거나, 환경변수를
            // 빈 값으로 덮어쓴 경우다 — 무엇을 되돌려야 하는지 함께 알린다.
            throw new InvalidOperationException(
                "Security:SecretKey 가 비어 있습니다. appsettings.json의 Security:SecretKey를 두거나 "
                + "환경변수 Security__SecretKey / User Secrets로 주입하세요.");
        }

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
