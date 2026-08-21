using System.Text;
using System.Text.Json;
using TaskbarHero.Common;
using ZLogger;
using GameServer.Repositories.MemoryDb.Interfaces;

namespace GameServer.Auth;

/// <summary>
/// GameServer 인증 미들웨어. /api/game/* 요청 body(JSON)의 userId·token을 읽어
/// Redis(auth:token:{userId})와 대조한다(헤더 미사용, 계정/로그인 기획서 5.5).
/// 통과 시 HttpContext.Items["userId"]에 인증된 userId를 주입하고, 실패 시 401로 응답한다.
/// </summary>
public sealed class GameAuthMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly RequestDelegate _next;
    private readonly ILogger<GameAuthMiddleware> _logger;

    public GameAuthMiddleware(RequestDelegate next, ILogger<GameAuthMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IAuthTokenReader tokenReader)
    {
        // 게임 API만 검증 대상. 그 외 경로(OpenAPI 등)는 통과.
        if (!context.Request.Path.StartsWithSegments("/api/game"))
        {
            await _next(context);
            return;
        }

        var (parsed, userId, token) = await TryReadAuthAsync(context.Request);
        if (!parsed || string.IsNullOrEmpty(token))
        {
            // 인증 정보 파싱 실패(userId 특정 불가) — 경로만 남긴다.
            _logger.ZLogWarning($"인증 실패(토큰 파싱 불가): {context.Request.Path.Value:@Path}");
            await WriteUnauthorizedAsync(context, ErrorCode.InvalidToken);
            return;
        }

        var cachedToken = await tokenReader.GetAsync(userId);
        if (cachedToken is null)
        {
            _logger.ZLogWarning($"인증 실패(만료/미보유 토큰): userId {userId:@UserId}");
            await WriteUnauthorizedAsync(context, ErrorCode.ExpiredToken);
            return;
        }

        if (!string.Equals(cachedToken, token, StringComparison.Ordinal))
        {
            _logger.ZLogWarning($"인증 실패(토큰 불일치): userId {userId:@UserId}");
            await WriteUnauthorizedAsync(context, ErrorCode.InvalidToken);
            return;
        }

        context.Items[Constants.Auth.UserIdItemKey] = userId;
        await _next(context);
    }

    /// <summary>
    /// Request.Body(JSON)에서 userId·token을 추출한다. EnableBuffering으로 되감아
    /// 이후 MVC 모델 바인딩이 body를 다시 읽을 수 있게 한다.
    /// </summary>
    private static async Task<(bool parsed, long userId, string? token)> TryReadAuthAsync(HttpRequest request)
    {
        try
        {
            request.EnableBuffering();
            request.Body.Position = 0;

            using var reader = new StreamReader(request.Body, Encoding.UTF8, false, 1024, leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            request.Body.Position = 0; // 컨트롤러가 다시 읽도록 되감기

            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("userId", out var userIdEl) || userIdEl.ValueKind != JsonValueKind.Number
                || !userIdEl.TryGetInt64(out var userId)
                || !root.TryGetProperty("token", out var tokenEl) || tokenEl.ValueKind != JsonValueKind.String)
            {
                return (false, 0, null);
            }

            return (true, userId, tokenEl.GetString());
        }
        catch (JsonException)
        {
            return (false, 0, null);
        }
    }

    private static async Task WriteUnauthorizedAsync(HttpContext context, ErrorCode errorCode)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";

        var payload = new
        {
            success = false,
            errorCode = (int)errorCode,
            message = errorCode == ErrorCode.ExpiredToken ? "Expired token" : "Invalid token",
            data = (object?)null,
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions));
    }
}
