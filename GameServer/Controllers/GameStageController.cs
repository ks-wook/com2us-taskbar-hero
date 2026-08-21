using GameServer.Services;
using GameServer.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using TaskbarHero.Common.Dto;

namespace GameServer.Controllers;

/// <summary>스테이지 진입·클리어·실패 보고 API(stage-battle 기획서 §5). 인증 필요.</summary>
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

    /// <summary>스테이지 실패(파티 전멸) 보고. POST /api/game/stage/fail</summary>
    [HttpPost("fail")]
    public async Task<IActionResult> Fail([FromBody] StageFailRequest request)
    {
        var data = request?.data ?? new StageFailData();
        var result = await stageService.FailAsync(
            AuthenticatedUserId(), data.act, data.difficulty, data.stage,
            data.elapsedMs, data.remainingMonsterCount, data.reachedBoss);
        return ApiResult(result.ErrorCode, result.SuccessMessage, result.Data);
    }
}
