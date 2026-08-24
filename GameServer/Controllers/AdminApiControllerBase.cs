using Microsoft.AspNetCore.Mvc;

namespace GameServer.Controllers;

/// <summary>
/// 운영·개발용 관리 API(<c>/api/admin/**</c>) 공통 베이스. 게임 인증 미들웨어는 <c>/api/game</c>만
/// 검사하므로, 이 경로는 <b>관리 키 헤더로 스스로 보호한다</b>(<c>X-Admin-Key</c> ≡
/// 설정 <c>Admin:ApiKey</c>).
/// <para>키가 설정돼 있지 않으면 엔드포인트를 <b>404로 닫는다</b> — 설정을 빼먹은 서버에 무인증 관리 API가
/// 열려 있는 상태를 만들지 않고, "그런 경로가 없다"로 존재 자체를 감춘다.</para>
/// <para>HTTP 액션 외의 보조 로직(키 검사·공통 응답 변환)은 프로젝트 규칙대로 전부 이 베이스에 둔다.</para>
/// </summary>
public abstract class AdminApiControllerBase : ControllerBase
{
    /// <summary>
    /// 관리 키를 검사한다. 통과하면 <c>null</c>, 막아야 하면 그대로 반환할 응답을 돌려준다
    /// (액션은 <c>if (reject is not null) return reject;</c> 한 줄로 쓴다).
    /// </summary>
    protected IActionResult? RejectIfUnauthorized()
    {
        var configuration = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var configured = configuration[Constants.Admin.ApiKeyConfigPath];
        if (string.IsNullOrWhiteSpace(configured))
        {
            return AdminResponse(StatusCodes.Status404NotFound, false, "Admin API is not configured");
        }

        if (!Request.Headers.TryGetValue(Constants.Admin.ApiKeyHeader, out var provided)
            || !string.Equals(provided.ToString(), configured, StringComparison.Ordinal))
        {
            return AdminResponse(StatusCodes.Status401Unauthorized, false, "Invalid admin key");
        }

        return null;
    }

    /// <summary>관리 API 공통 응답 형식 <c>{ success, message, data }</c> + HTTP 상태로 변환한다.</summary>
    protected IActionResult AdminResponse(int statusCode, bool success, string message, object? data = null)
        => StatusCode(statusCode, new { success, message, data });
}
