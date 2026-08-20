using GameServer.Services;
using GameServer.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using TaskbarHero.Common.Dto;

namespace GameServer.Controllers;

/// <summary>성장(스킬·룬) 액션 API(growth 기획서 §5). 인증 필요.</summary>
[ApiController]
[Route("api/game/growth")]
public sealed class GameGrowthController(IGrowthService growthService) : GameApiControllerBase
{
    /// <summary>스킬 레벨업. POST /api/game/growth/skill/levelup</summary>
    [HttpPost("skill/levelup")]
    public async Task<IActionResult> SkillLevelUp([FromBody] SkillLevelUpRequest request)
    {
        var data = request?.data ?? new SkillLevelUpData();
        var result = await growthService.SkillLevelUpAsync(AuthenticatedUserId(), data.characterId, data.skillCode);
        return ApiResult(result.ErrorCode, result.SuccessMessage, result.Data);
    }

    /// <summary>스킬 초기화. POST /api/game/growth/skill/reset</summary>
    [HttpPost("skill/reset")]
    public async Task<IActionResult> SkillReset([FromBody] SkillResetRequest request)
    {
        var data = request?.data ?? new SkillResetData();
        var result = await growthService.SkillResetAsync(AuthenticatedUserId(), data.characterId);
        return ApiResult(result.ErrorCode, result.SuccessMessage, result.Data);
    }

    /// <summary>액티브 스킬 장착. POST /api/game/growth/skill/equip</summary>
    [HttpPost("skill/equip")]
    public async Task<IActionResult> SkillEquip([FromBody] SkillEquipRequest request)
    {
        var data = request?.data ?? new SkillEquipData();
        var result = await growthService.SkillEquipAsync(AuthenticatedUserId(), data.characterId, data.skillCodes ?? new List<int>());
        return ApiResult(result.ErrorCode, result.SuccessMessage, result.Data);
    }

    /// <summary>룬 업그레이드. POST /api/game/growth/rune/upgrade</summary>
    [HttpPost("rune/upgrade")]
    public async Task<IActionResult> RuneUpgrade([FromBody] RuneUpgradeRequest request)
    {
        var data = request?.data ?? new RuneUpgradeData();
        var result = await growthService.RuneUpgradeAsync(AuthenticatedUserId(), data.runeCode);
        return ApiResult(result.ErrorCode, result.SuccessMessage, result.Data);
    }
}
