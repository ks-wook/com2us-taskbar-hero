using GameServer.Services;
using GameServer.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace GameServer.Controllers;

/// <summary>출석부 보상 API(attendance 기획서 §5). 인증 필요.</summary>
[ApiController]
[Route("api/game/attendance")]
public sealed class GameAttendanceController(IAttendanceService attendanceService) : GameApiControllerBase
{
    /// <summary>이번달 출석 현황 조회. POST /api/game/attendance/status — 요청 data 없음.</summary>
    [HttpPost("status")]
    public async Task<IActionResult> Status()
    {
        var result = await attendanceService.StatusAsync(AuthenticatedUserId());
        return ApiResult(result.ErrorCode, result.SuccessMessage, result.Data);
    }

    /// <summary>오늘자 출석 보상 획득(보상 메일 발급). POST /api/game/attendance/claim — 요청 data 없음.</summary>
    [HttpPost("claim")]
    public async Task<IActionResult> Claim()
    {
        var result = await attendanceService.ClaimAsync(AuthenticatedUserId());
        return ApiResult(result.ErrorCode, result.SuccessMessage, result.Data);
    }
}
