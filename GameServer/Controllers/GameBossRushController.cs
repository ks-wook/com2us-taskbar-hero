using GameServer.Services;
using GameServer.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using TaskbarHero.Common.Dto;

namespace GameServer.Controllers;

/// <summary>
/// 보스러시 / 랭킹 API(보스러시 기획서 §5.1~5.5). 인증 필요.
/// <para>실패·포기 보고 엔드포인트를 두지 않는다 — 5라운드를 못 깨면 클라이언트는 아무것도 보내지 않고
/// 그 런은 제한 시간이 지나 만료된다(§6.2 lazy 만료).</para>
/// </summary>
[ApiController]
[Route("api/game/boss-rush")]
public sealed class GameBossRushController(IBossRushService bossRushService) : GameApiControllerBase
{
    /// <summary>보스러시 정보 조회(해금·일일 잔여·현 시즌·내 기록·진행 중 런). POST /api/game/boss-rush/info</summary>
    [HttpPost("info")]
    public async Task<IActionResult> Info()
    {
        var result = await bossRushService.GetInfoAsync(AuthenticatedUserId());
        return ApiResult(result.ErrorCode, result.SuccessMessage, result.Data);
    }

    /// <summary>도전 시작(일일 횟수 차감 + 5라운드 스폰 구성 반환). POST /api/game/boss-rush/enter</summary>
    [HttpPost("enter")]
    public async Task<IActionResult> Enter()
    {
        var result = await bossRushService.EnterAsync(AuthenticatedUserId());
        return ApiResult(result.ErrorCode, result.SuccessMessage, result.Data);
    }

    /// <summary>클리어 보고(클라 측정 시간 기록 + 시즌 최고 기록 갱신). POST /api/game/boss-rush/clear</summary>
    [HttpPost("clear")]
    public async Task<IActionResult> Clear([FromBody] BossRushClearRequest request)
    {
        var data = request?.data ?? new BossRushClearData();
        var result = await bossRushService.ClearAsync(AuthenticatedUserId(), data);
        return ApiResult(result.ErrorCode, result.SuccessMessage, result.Data);
    }

    /// <summary>랭킹 목록 조회(전체 등재 유저 오프셋 페이징). POST /api/game/boss-rush/rank</summary>
    [HttpPost("rank")]
    public async Task<IActionResult> Rank([FromBody] BossRushRankRequest request)
    {
        var data = request?.data ?? new BossRushRankData();
        var result = await bossRushService.GetRankAsync(
            AuthenticatedUserId(), data.seasonId, data.offset, data.limit);
        return ApiResult(result.ErrorCode, result.SuccessMessage, result.Data);
    }

    /// <summary>내 순위 조회(뷰어 전용 1건). POST /api/game/boss-rush/my-rank</summary>
    [HttpPost("my-rank")]
    public async Task<IActionResult> MyRank([FromBody] BossRushMyRankRequest request)
    {
        var data = request?.data ?? new BossRushMyRankData();
        var result = await bossRushService.GetMyRankAsync(AuthenticatedUserId(), data.seasonId);
        return ApiResult(result.ErrorCode, result.SuccessMessage, result.Data);
    }
}
