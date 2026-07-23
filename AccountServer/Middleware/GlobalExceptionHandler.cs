using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using TaskbarHero.Common;

namespace AccountServer.Middleware;

/// <summary>
/// 전역 예외 처리기(로깅 규칙 §6). 파이프라인에서 처리되지 못한 예외를 Error로 1회 로깅하고,
/// 클라이언트에는 내부 정보를 노출하지 않는 일반화된 응답({ success:false, errorCode:ServerError })을
/// HTTP 500으로 반환한다. 응답 본문 형식은 공통 응답 계약({ success, errorCode, message, data })을 따른다.
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) => _logger = logger;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "미처리 예외: {Method} {Path}",
            context.Request.Method, context.Request.Path.Value);

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";

        var payload = new
        {
            success = false,
            errorCode = (int)ErrorCode.ServerError,
            message = "Internal server error",
            data = (object?)null,
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions), cancellationToken);
        return true;
    }
}
