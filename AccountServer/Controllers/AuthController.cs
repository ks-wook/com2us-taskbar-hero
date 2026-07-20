using AccountServer.Services;
using Microsoft.AspNetCore.Mvc;
using TaskbarHero.Common;
using TaskbarHero.Common.Dto;

namespace AccountServer.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(IAuthService authService) : AccountApiControllerBase
{
    /// <summary>회원가입. POST /api/auth/signup — 무인증.</summary>
    [HttpPost("signup")]
    public async Task<IActionResult> Signup([FromBody] SignupRequest request)
    {
        var result = await authService.SignupAsync(request);

        var response = new SignupResponse
        {
            success = result.ErrorCode == ErrorCode.Success,
            errorCode = (int)result.ErrorCode,
            userId = result.UserId,
            message = MessageFor(result.ErrorCode, "Signup successful"),
        };

        return ApiResult(result.ErrorCode, response);
    }

    /// <summary>로그인. POST /api/auth/login — 무인증. 성공 시 인증 토큰 발급.</summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var result = await authService.LoginAsync(request);

        var response = new LoginResponse
        {
            success = result.ErrorCode == ErrorCode.Success,
            errorCode = (int)result.ErrorCode,
            userId = result.UserId,
            token = result.Token,
            message = MessageFor(result.ErrorCode, "Login successful"),
        };

        return ApiResult(result.ErrorCode, response);
    }

    /// <summary>로그아웃. POST /api/auth/logout — 인증(body의 userId·token). 토큰 무효화.</summary>
    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest request)
    {
        var errorCode = await authService.LogoutAsync(request.userId, request.token ?? string.Empty);

        var response = new LogoutResponse
        {
            success = errorCode == ErrorCode.Success,
            errorCode = (int)errorCode,
            message = MessageFor(errorCode, "Logout successful"),
        };

        return ApiResult(errorCode, response);
    }
}
