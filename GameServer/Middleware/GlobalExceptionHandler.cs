using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using TaskbarHero.Common;
using TaskbarHero.Common.Dto;
using ZLogger;

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

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) => _logger = logger;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        _logger.ZLogError(exception, $"미처리 예외: {context.Request.Method:@Method} {context.Request.Path.Value:@Path}");

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
}
