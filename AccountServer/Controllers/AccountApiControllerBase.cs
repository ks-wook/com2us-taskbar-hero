using Microsoft.AspNetCore.Mvc;
using TaskbarHero.Common;

namespace AccountServer.Controllers;

/// <summary>
/// 계정 컨트롤러 공통 베이스. HTTP 액션 메서드 외의 보조 로직(ErrorCode → HTTP 상태/메시지 매핑, 응답 래핑)을
/// 여기에 두고, 각 컨트롤러가 이를 상속해 사용한다(프로젝트 규칙: 컨트롤러는 HTTP 액션만 포함).
/// </summary>
public abstract class AccountApiControllerBase : ControllerBase
{
    /// <summary>응답 본문을 ErrorCode에 대응하는 HTTP 상태로 감싸 반환한다.</summary>
    protected IActionResult ApiResult(ErrorCode errorCode, object response)
        => StatusCode(HttpStatus(errorCode), response);

    /// <summary>성공이면 엔드포인트별 성공 문구를, 실패면 코드별 에러 문구를 반환한다.</summary>
    protected static string MessageFor(ErrorCode errorCode, string successMessage)
        => errorCode == ErrorCode.Success ? successMessage : ErrorMessage(errorCode);

    private static int HttpStatus(ErrorCode code) => code switch
    {
        ErrorCode.Success => StatusCodes.Status200OK,
        ErrorCode.DuplicateEmail => StatusCodes.Status409Conflict,
        ErrorCode.InvalidRequest => StatusCodes.Status400BadRequest,
        ErrorCode.NicknameTooLong => StatusCodes.Status400BadRequest,
        // 인증 실패 계열은 401.
        ErrorCode.UserNotFound => StatusCodes.Status401Unauthorized,
        ErrorCode.InvalidPassword => StatusCodes.Status401Unauthorized,
        ErrorCode.InvalidToken => StatusCodes.Status401Unauthorized,
        ErrorCode.ExpiredToken => StatusCodes.Status401Unauthorized,
        // 서비스가 예외를 잡아 일반화한 코드. 전역 예외 처리기의 응답과 같은 500으로 맞춘다.
        ErrorCode.ServerError => StatusCodes.Status500InternalServerError,
        _ => StatusCodes.Status400BadRequest,
    };

    private static string ErrorMessage(ErrorCode code) => code switch
    {
        ErrorCode.DuplicateEmail => "Duplicate email",
        ErrorCode.InvalidRequest => "Invalid request",
        ErrorCode.NicknameTooLong => "Nickname too long",
        ErrorCode.UserNotFound => "User not found",
        ErrorCode.InvalidPassword => "Invalid password",
        ErrorCode.InvalidToken => "Invalid token",
        ErrorCode.ExpiredToken => "Expired token",
        ErrorCode.ServerError => "Internal server error",
        _ => code.ToString(),
    };
}
