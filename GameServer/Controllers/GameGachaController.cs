using GameServer.Services;
using GameServer.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using TaskbarHero.Common.Dto;

namespace GameServer.Controllers;

/// <summary>가챠(배너 조회·뽑기·기록 조회) API(가챠 기획서 §5.1~5.3). 인증 필요.</summary>
[ApiController]
[Route("api/game/gacha")]
public sealed class GameGachaController(IGachaService gachaService) : GameApiControllerBase
{
    /// <summary>지금 돌릴 수 있는 가챠 배너 목록 조회. POST /api/game/gacha/banners</summary>
    [HttpPost("banners")]
    public async Task<IActionResult> Banners()
    {
        var result = await gachaService.GetBannersAsync(AuthenticatedUserId());
        return ApiResult(result.ErrorCode, result.SuccessMessage, result.Data);
    }

    /// <summary>가챠 뽑기(1연·10연 공통, pullType이 상품을 가른다). POST /api/game/gacha/pull</summary>
    [HttpPost("pull")]
    public async Task<IActionResult> Pull([FromBody] GachaPullRequest request)
    {
        var data = request?.data ?? new GachaPullData();
        var result = await gachaService.PullAsync(AuthenticatedUserId(), data.gachaCode, data.pullType);
        return ApiResult(result.ErrorCode, result.SuccessMessage, result.Data);
    }

    /// <summary>뽑기 기록 조회(최신순 커서 페이징). POST /api/game/gacha/history</summary>
    [HttpPost("history")]
    public async Task<IActionResult> History([FromBody] GachaHistoryRequest request)
    {
        var data = request?.data ?? new GachaHistoryData();
        var result = await gachaService.GetHistoryAsync(
            AuthenticatedUserId(), data.gachaCode, data.cursor, data.limit);
        return ApiResult(result.ErrorCode, result.SuccessMessage, result.Data);
    }
}
