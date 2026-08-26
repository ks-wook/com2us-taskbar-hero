namespace GameServer.Logging;

/// <summary>
/// 이벤트 로그의 <c>req_id</c>(요청 상관 ID) 공급자. <b>이벤트 로거를 웹 스택에서 떼어 내기 위한 추상</b>이다 —
/// GameServer는 <c>HttpContext.TraceIdentifier</c>를 주지만, 요청이라는 개념이 없는 BatchServer는 늘 null을 준다.
/// <para>배치가 내는 라인은 요청에서 나온 값이 아니라 원래부터 <c>req_id</c>가 없으므로(로그 이벤트 정의 4.1),
/// null을 주는 것이 축소가 아니라 정확한 표현이다.</para>
/// </summary>
public interface IRequestIdAccessor
{
    /// <summary>지금 처리 중인 요청의 상관 ID. 요청 맥락이 없으면 null.</summary>
    string? Current { get; }
}

/// <summary>
/// 요청 맥락이 없는 호스트(BatchServer 등)용 구현 — 언제나 null을 돌려준다.
/// </summary>
public sealed class NullRequestIdAccessor : IRequestIdAccessor
{
    /// <summary>요청이라는 개념이 없으므로 언제나 null이다.</summary>
    public string? Current => null;
}
