using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ZLogger;
using GameServer.Util;

namespace GameServer.Logging;

/// <summary>
/// 이벤트 로그를 <b>평탄 JSON 1줄</b>로 만들어 전용 로거 카테고리로 흘려보낸다
/// ([로그 이벤트 정의](../../docs/공통/로그-이벤트-정의.md) 4.1). 그 카테고리는 <c>Program.cs</c>의 필터가
/// JSON 파일 sink로만 보내므로, 콘솔의 운영 로그와 절대 섞이지 않는다.
/// <para>라인은 직접 조립한다 — 로거 프레임워크의 JSON 포매터가 붙이는 <c>LogLevel</c>·<c>Category</c> 같은
/// 필드가 섞이면 <c>out_sql</c>의 컬럼 매핑이 어긋나기 때문이다. 이 클래스가 내는 키는 문서가 정한 것뿐이다.</para>
/// </summary>
public sealed class EventLogger : IEventLogger
{
    /// <summary>
    /// 필드 record → JSON 변환 규칙. <c>PascalCase</c> 프로퍼티를 <c>snake_case</c> 컬럼 이름으로 바꾸고,
    /// null 필드는 아예 내지 않는다(컬럼이 NULL로 들어가는 것과 키가 없는 것은 <c>out_sql</c>에서 같다).
    /// </summary>
    private static readonly JsonSerializerOptions FieldOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// 시각 형식(UTC·밀리초). fluentd의 <c>time_format</c>과 맞춘 문자열이며,
    /// 적재되는 시각은 수집 시각이 아니라 <b>서버가 찍은 이 값</b>이다(9.1).
    /// </summary>

    private readonly ILogger _logger;
    private readonly IRequestIdAccessor _requestId;

    /// <summary>전용 카테고리 로거를 만들고, req_id 공급자를 주입받는다.</summary>
    public EventLogger(ILoggerFactory loggerFactory, IRequestIdAccessor requestId)
    {
        _logger = loggerFactory.CreateLogger(Constants.EventLog.Category);
        _requestId = requestId;
    }

    /// <summary>
    /// 공통 필드(timestamp·tag·uid·req_id·error_code)와 이벤트 고유 필드를 <b>한 단계로 평탄하게</b> 합쳐
    /// JSON 1줄로 방출한다. 중첩을 만들지 않는 이유는 <c>out_sql</c>의 컬럼 매핑이 그대로 맞아
    /// 수집기에 변환 단계가 없어지기 때문이다(4.1).
    /// </summary>
    public void Action(string tag, long? uid, IEventFields fields, int? errorCode = null)
    {
        var line = new JsonObject
        {
            ["timestamp"] = DateTimeUtil.NowEventTimestamp(),
            ["tag"] = tag,
        };

        // uid·req_id는 없을 수 있다(시스템 이벤트는 uid가, 배치가 낸 라인은 req_id가 없다).
        if (uid is not null)
        {
            line["uid"] = uid.Value;
        }

        var requestId = _requestId.Current;
        if (!string.IsNullOrEmpty(requestId))
        {
            line["req_id"] = requestId;
        }

        if (errorCode is not null)
        {
            line["error_code"] = errorCode.Value;
        }

        // 고유 필드를 최상위로 펼친다. 키 순서는 문서의 예시 라인과 같은 순서가 된다.
        var payload = JsonSerializer.SerializeToNode(fields, fields.GetType(), FieldOptions) as JsonObject;
        if (payload is not null)
        {
            foreach (var field in payload.ToList())
            {
                payload.Remove(field.Key);
                line[field.Key] = field.Value;
            }
        }

        // 메시지 자체가 완성된 JSON 라인이다 — 파일 sink는 접두사 없이 이 문자열만 쓴다.
        _logger.ZLogInformation($"{line.ToJsonString()}");
    }
}
