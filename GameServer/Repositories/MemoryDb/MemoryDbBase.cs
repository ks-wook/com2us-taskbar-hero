using CloudStructures;
using ZLogger;

namespace GameServer.Repositories.MemoryDb;

/// <summary>
/// Redis(MemoryDb) 계층의 공통 기반 — 연결과 <b>실패 정책</b>을 한곳에 모은다.
/// <para><b>실패 정책은 두 갈래다.</b> Redis에 있는 값이 <b>MySQL 정본의 파생</b>(랭킹 리더보드·닉네임·시즌 캐시)이면
/// 실패를 흡수해야 한다 — 호출측이 MySQL로 폴백하면 응답은 정상이므로, <see cref="SafeAsync{T}"/>가 Warning만 남기고
/// 기본값을 돌려준다(축소 운전). 반대로 Redis가 <b>정본</b>인 값(인증 토큰)은 흡수하면 없는 세션을 통과시키게 되므로
/// Safe 계열을 쓰지 않고 예외를 그대로 올린다.</para>
/// <para>그래서 파생 클래스의 메서드는 <b>"SafeAsync로 감쌌는가"만 보면 실패 시 거동을 알 수 있다.</b>
/// Safe 계열이 필요 없는 계층(<see cref="AuthTokenReader"/>)은 이 클래스를 상속하지 않는다.</para>
/// </summary>
public abstract class MemoryDbBase
{
    private readonly ILogger _logger;

    /// <summary>파생 클래스가 CloudStructures 구조체를 만들 때 쓰는 Redis 연결.</summary>
    protected RedisConnection Connection { get; }

    /// <summary>실패 로그에 붙는 계층 이름(예: "보스러시 랭킹 캐시") — 어느 캐시가 축소 운전 중인지 알아보게 한다.</summary>
    protected string Area { get; }

    /// <summary>Redis 연결·로거와 로그용 계층 이름을 받는다.</summary>
    protected MemoryDbBase(RedisConnection connection, ILogger logger, string area)
    {
        Connection = connection;
        _logger = logger;
        Area = area;
    }

    /// <summary>값을 돌려주는 캐시 접근을 감싼다. 실패하면 Warning을 남기고 <paramref name="fallback"/>을 돌려준다.</summary>
    protected async Task<T> SafeAsync<T>(Func<Task<T>> body, T fallback, string operation)
    {
        try
        {
            return await body();
        }
        catch (Exception ex)
        {
            Warn(ex, operation);
            return fallback;
        }
    }

    /// <summary>부수효과만 있는 캐시 쓰기를 감싼다. 실패하면 Warning만 남긴다(응답에 영향 없음).</summary>
    protected async Task SafeAsync(Func<Task> body, string operation)
    {
        try
        {
            await body();
        }
        catch (Exception ex)
        {
            Warn(ex, operation);
        }
    }

    /// <summary>캐시 실패는 사용자 실수가 아니라 인프라 경합이므로 Warning이다(로깅 규칙).</summary>
    protected void Warn(Exception ex, string operation)
        => _logger.ZLogWarning(ex, $"{Area:@Area} {operation:@Operation} 실패 — 정본 폴백으로 진행합니다.");
}
