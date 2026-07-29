using GameServer.Services;
using Microsoft.AspNetCore.Mvc;
using TaskbarHero.Common.Dto;

namespace GameServer.Controllers;

[ApiController]
[Route("api/game")]
public sealed class GameSaveController(ISaveService saveService) : GameApiControllerBase
{
    /// <summary>세이브 로드. POST /api/game/load — 인증 필요.</summary>
    [HttpPost("load")]
    public async Task<IActionResult> Load()
    {
        var result = await saveService.LoadAsync(AuthenticatedUserId());
        return ApiResult(result.ErrorCode, result.SuccessMessage, result.Data);
    }

    /// <summary>캐릭터 생성. POST /api/game/create-character — 인증 필요.</summary>
    [HttpPost("create-character")]
    public async Task<IActionResult> CreateCharacter([FromBody] CreateCharacterRequest request)
    {
        var data = request.data ?? new CreateCharacterData();
        var result = await saveService.CreateCharacterAsync(AuthenticatedUserId(), data.nickname, data.classCode, data.gender);
        return ApiResult(result.ErrorCode, result.SuccessMessage, result.Data);
    }

    /// <summary>접속 시각 갱신(heartbeat). POST /api/game/update-last-active — 인증 필요.</summary>
    [HttpPost("update-last-active")]
    public async Task<IActionResult> UpdateLastActive()
    {
        var result = await saveService.UpdateLastActiveAsync(AuthenticatedUserId());
        return ApiResult(result.ErrorCode, result.SuccessMessage, result.Data);
    }
}
