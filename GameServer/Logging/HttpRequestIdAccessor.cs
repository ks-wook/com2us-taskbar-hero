namespace GameServer.Logging;

/// <summary>
/// 웹 요청의 상관 ID를 <c>HttpContext.TraceIdentifier</c>에서 꺼내 주는 <see cref="IRequestIdAccessor"/> 구현.
/// <para>이벤트 로거는 GameServer.Core에 있어 웹 스택을 모른다 — 요청 맥락을 아는 것은 이 계층뿐이라
/// req_id 조회를 여기서 구현해 주입한다. 배치 호스트는 <see cref="NullRequestIdAccessor"/>를 쓴다.</para>
/// </summary>
public sealed class HttpRequestIdAccessor : IRequestIdAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>요청 맥락 접근자를 주입받는다.</summary>
    public HttpRequestIdAccessor(IHttpContextAccessor httpContextAccessor)
        => _httpContextAccessor = httpContextAccessor;

    /// <summary>처리 중인 요청의 TraceIdentifier. 요청 밖(기동·종료·백그라운드)에서는 null.</summary>
    public string? Current => _httpContextAccessor.HttpContext?.TraceIdentifier;
}
