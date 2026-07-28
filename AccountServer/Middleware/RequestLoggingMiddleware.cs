using System.Diagnostics;
using ZLogger;

namespace AccountServer.Middleware;

/// <summary>
/// 요청 1줄 접근 로그 미들웨어(로깅 규칙 §4). 각 HTTP 요청의 메서드·경로·상태코드·소요시간을
/// Information으로 남긴다. 파이프라인 최외곽에 두어, 전역 예외 처리기가 500으로 확정한 상태코드까지 기록한다.
/// </summary>
public sealed class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();
            _logger.ZLogInformation($"요청 처리: {context.Request.Method:@Method} {context.Request.Path.Value:@Path} → {context.Response.StatusCode:@StatusCode} ({stopwatch.ElapsedMilliseconds:@ElapsedMs}ms)");
        }
    }
}
