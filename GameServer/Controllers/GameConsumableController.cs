using GameServer.Services;
using GameServer.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using TaskbarHero.Common.Dto;

namespace GameServer.Controllers;

/// <summary>
/// 소모품(소모성 아이템) 사용·활성 버프 조회 API(소모품/버프 기획서 §5.1·§5.2). 인증 필요.
/// 최초 버프 UI는 코어 로드(POST /api/game/load)의 activeBuffs가 채우고, 이후 재동기화는 여기의 경량 조회가 담당한다.
/// </summary>
[ApiController]
[Route("api/game/consumable")]
public sealed class GameConsumableController(IConsumableService consumableService) : GameApiControllerBase
{
    /// <summary>소모품 1개 사용 → 계정 획득량 버프 부여·연장. POST /api/game/consumable/use</summary>
    [HttpPost("use")]
    public async Task<IActionResult> Use([FromBody] ConsumableUseRequest request)
    {
        var data = request?.data ?? new ConsumableUseData();
        var result = await consumableService.UseAsync(AuthenticatedUserId(), data.itemId);
        return ApiResult(result.ErrorCode, result.SuccessMessage, result.Data);
    }

    /// <summary>현재 적용 중인 버프 조회(버프 UI 재동기화용 경량 조회). POST /api/game/consumable/buffs — 요청 data 없음.</summary>
    [HttpPost("buffs")]
    public async Task<IActionResult> Buffs()
    {
        var result = await consumableService.GetActiveBuffsAsync(AuthenticatedUserId());
        return ApiResult(result.ErrorCode, result.SuccessMessage, result.Data);
    }
}
