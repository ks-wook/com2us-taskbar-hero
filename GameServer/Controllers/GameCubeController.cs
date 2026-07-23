using GameServer.Services;
using Microsoft.AspNetCore.Mvc;
using TaskbarHero.Common.Dto;

namespace GameServer.Controllers;

/// <summary>큐브(합성·분해·제작) 액션 API(inventory-item-cube 기획서 §5.6·5.7·5.8). 인증 필요.</summary>
[ApiController]
[Route("api/game/cube")]
public sealed class GameCubeController(ICubeService cubeService) : GameApiControllerBase
{
    /// <summary>큐브 합성. POST /api/game/cube/combine</summary>
    [HttpPost("combine")]
    public async Task<IActionResult> Combine([FromBody] CubeCombineRequest request)
    {
        var data = request?.data ?? new CubeCombineData();
        var result = await cubeService.CombineAsync(AuthenticatedUserId(), data.itemIds ?? new List<long>());
        return ApiResult(result.ErrorCode, result.SuccessMessage, result.Data);
    }

    /// <summary>큐브 분해. POST /api/game/cube/dismantle</summary>
    [HttpPost("dismantle")]
    public async Task<IActionResult> Dismantle([FromBody] CubeDismantleRequest request)
    {
        var data = request?.data ?? new CubeDismantleData();
        var result = await cubeService.DismantleAsync(AuthenticatedUserId(), data.items ?? new List<CubeDismantleItemDto>());
        return ApiResult(result.ErrorCode, result.SuccessMessage, result.Data);
    }

    /// <summary>큐브 제작. POST /api/game/cube/craft</summary>
    [HttpPost("craft")]
    public async Task<IActionResult> Craft([FromBody] CubeCraftRequest request)
    {
        var data = request?.data ?? new CubeCraftData();
        var result = await cubeService.CraftAsync(AuthenticatedUserId(), data.recipeCode);
        return ApiResult(result.ErrorCode, result.SuccessMessage, result.Data);
    }
}
