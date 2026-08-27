namespace GameServer.Logging;

/// <summary>
/// 집계용 이벤트 로그 방출기([로그 이벤트 정의](../../docs/공통/로그-이벤트-정의.md) 4장).
/// 사람이 읽는 운영 로그(<see cref="ILogger{T}"/>·콘솔)와 <b>다른 경로</b>다 — 이쪽은 기계가 읽는
/// 평탄 JSON 1줄을 전용 파일에 쓰고, fluentd가 그 파일만 걷어 <c>logdb</c>에 적재한다(3장).
/// </summary>
public interface IEventLogger
{
    /// <summary>
    /// 액션 로그 1행을 방출한다. <paramref name="tag"/>가 곧 적재 테이블을 결정한다(4.3).
    /// <para>호출 지점은 <b>서비스 계층</b>이다(컨트롤러 금지 — 로깅 규칙 §4). 트랜잭션이 있는 이벤트는
    /// <b>커밋 성공 이후</b>에 부른다 — 롤백된 사실을 로그에 남기지 않기 위해서다.</para>
    /// <para><c>req_id</c>는 호출자가 넘기지 않는다 — 현재 요청에서 스스로 찾고, 요청 밖(배치)이면 넣지 않는다.</para>
    /// <para><b>방출 실패는 구현이 흡수한다</b> — 운영 로그에 Error로 남기고 호출자에게는 올리지 않는다.
    /// 커밋 뒤에 부르는 자리가 많아, 로그 유실이 성공한 요청을 실패로 뒤집으면 안 되기 때문이다.</para>
    /// </summary>
    /// <param name="tag">이벤트 태그. <see cref="Constants.EventLog.Tags"/>의 상수만 쓴다.</param>
    /// <param name="uid">계정 식별자. 시스템 이벤트처럼 계정이 없으면 null(필드 자체가 빠진다).</param>
    /// <param name="fields">이벤트 고유 필드(<see cref="IEventFields"/> 파생 record).</param>
    /// <param name="errorCode">
    /// 거부를 남기는 이벤트만 넘긴다(0=성공). null이면 <c>error_code</c> 필드를 내지 않는다 —
    /// 어떤 이벤트가 거부를 남기는지는 카탈로그(5장)가 정한다.
    /// </param>
    void Action(string tag, long? uid, IEventFields fields, int? errorCode = null);
}
