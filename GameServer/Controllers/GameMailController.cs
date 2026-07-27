using GameServer.Services;
using Microsoft.AspNetCore.Mvc;
using TaskbarHero.Common.Dto;

namespace GameServer.Controllers;

/// <summary>메일(우편함) API(mail 기획서 §5). 인증 필요.</summary>
[ApiController]
[Route("api/game/mail")]
public sealed class GameMailController(IMailService mailService) : GameApiControllerBase
{
    /// <summary>우편함 조회(+미열람 읽음 처리). POST /api/game/mail/list — 요청 data 없음.</summary>
    [HttpPost("list")]
    public async Task<IActionResult> List()
    {
        var result = await mailService.ListAsync(AuthenticatedUserId());
        return ApiResult(result.ErrorCode, result.SuccessMessage, result.Data);
    }

    /// <summary>메일 첨부 단건 수령. POST /api/game/mail/claim</summary>
    [HttpPost("claim")]
    public async Task<IActionResult> Claim([FromBody] MailClaimRequest request)
    {
        var data = request?.data ?? new MailClaimData();
        var result = await mailService.ClaimAsync(AuthenticatedUserId(), data.mailId);
        return ApiResult(result.ErrorCode, result.SuccessMessage, result.Data);
    }

    /// <summary>수령 가능한 메일 일괄 수령. POST /api/game/mail/claim-all — 요청 data 없음.</summary>
    [HttpPost("claim-all")]
    public async Task<IActionResult> ClaimAll()
    {
        var result = await mailService.ClaimAllAsync(AuthenticatedUserId());
        return ApiResult(result.ErrorCode, result.SuccessMessage, result.Data);
    }
}
