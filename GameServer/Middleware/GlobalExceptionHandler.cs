using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using TaskbarHero.Common;
using TaskbarHero.Common.Dto;
using ZLogger;
using GameServer.Logging;

namespace GameServer.Middleware;

/// <summary>
/// 전역 예외 처리기(로깅 규칙 §6). 파이프라인에서 처리되지 못한 예외를 Error로 1회 로깅하고,
/// 클라이언트에는 내부 정보를 노출하지 않는 일반화된 응답(ApiResponse, errorCode=ServerError)을
/// HTTP 500으로 반환한다.
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { IncludeFields = true };

    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly IEventLogger _eventLogger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IEventLogger eventLogger)
    {
        _logger = logger;
        _eventLogger = eventLogger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        _logger.ZLogError(exception, $"미처리 예외: {context.Request.Method:@Method} {context.Request.Path.Value:@Path}");

        // 집계용 이벤트(5.11). 사람이 읽는 위 Error 로그와 달리 여기엔 **예외 타입 이름만** 담는다 —
        // 메시지 전문·스택은 운영 로그의 몫이고, 집계 축이 되는 것은 타입뿐이다.
        // 시스템 이벤트 넷 중 유일하게 요청에서 나온 값(uid·req_id·path)을 갖는다.
        _eventLogger.Action(
            Constants.EventLog.Tags.ApiError, AuthenticatedUserIdOrNull(context),
            new ApiErrorEvent(
                context.Request.Path.Value ?? string.Empty,
                exception.GetType().Name,
                (int)ErrorCode.ServerError));

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";

        var payload = new ApiResponse
        {
            success = false,
            errorCode = (int)ErrorCode.ServerError,
            message = "Internal server error",
            data = null,
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions), cancellationToken);
        return true;
    }

    /// <summary>
    /// 인증 미들웨어가 주입한 userId(있으면). 인증 전에 터진 예외에는 없으므로 null이 되고,
    /// 그 경우 이벤트 라인에서 <c>uid</c> 필드 자체가 빠진다.
    /// </summary>
    private static long? AuthenticatedUserIdOrNull(HttpContext context)
        => context.Items.TryGetValue(Constants.Auth.UserIdItemKey, out var value) && value is long userId
            ? userId
            : null;
}
