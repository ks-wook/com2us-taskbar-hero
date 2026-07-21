using GameServer.Services;
using Microsoft.AspNetCore.Mvc;
using TaskbarHero.Common.Dto;

namespace GameServer.Controllers;

/// <summary>스테이지 진입·클리어 API(stage-battle 기획서 §5). 인증 필요.</summary>
[ApiController]
[Route("api/game/stage")]
public sealed class GameStageController(IStageService stageService) : GameApiControllerBase
{
    /// <summary>스테이지 진입. POST /api/game/stage/enter</summary>
    [HttpPost("enter")]
    public async Task<IActionResult> Enter([FromBody] StageActionRequest request)
    {
        var data = request?.data ?? new StageActionData();
        var result = await stageService.EnterAsync(AuthenticatedUserId(), data.act, data.difficulty, data.stage);
        return ApiResult(result.ErrorCode, result.SuccessMessage, result.Data);
    }

    /// <summary>스테이지 클리어. POST /api/game/stage/clear</summary>
    [HttpPost("clear")]
    public async Task<IActionResult> Clear([FromBody] StageActionRequest request)
    {
        var data = request?.data ?? new StageActionData();
        var result = await stageService.ClearAsync(AuthenticatedUserId(), data.act, data.difficulty, data.stage);
        return ApiResult(result.ErrorCode, result.SuccessMessage, result.Data);
    }
}
