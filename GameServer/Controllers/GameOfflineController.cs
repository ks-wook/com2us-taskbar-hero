using GameServer.Services;
using GameServer.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace GameServer.Controllers;

/// <summary>오프라인(방치) 보상 정산 API(offline-reward 기획서 §5). 인증 필요.</summary>
[ApiController]
[Route("api/game/offline")]
public sealed class GameOfflineController(IOfflineService offlineService) : GameApiControllerBase
{
    /// <summary>오프라인 보상 정산. POST /api/game/offline/claim — 요청 data 없음(경과·보상은 서버가 계산).</summary>
    [HttpPost("claim")]
    public async Task<IActionResult> Claim()
    {
        var result = await offlineService.ClaimAsync(AuthenticatedUserId());
        return ApiResult(result.ErrorCode, result.SuccessMessage, result.Data);
    }
}
