using GameServer.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace GameServer.Controllers;

/// <summary>
/// 보스러시 운영·개발용 관리 API(<c>X-Admin-Key</c> 필요, 게임 인증 토큰과 무관).
/// <para>호출 주체는 부트스트랩 스크립트 <c>server_up.py</c>다 — 컨테이너와 서버를 띄운 뒤
/// 랭킹 캐시 최초 적재를 이 엔드포인트로 지시한다(보스러시 기획서 6.3).</para>
/// </summary>
[ApiController]
[Route("api/admin/boss-rush")]
public sealed class AdminBossRushController(IBossRushRankWarmupService warmupService) : AdminApiControllerBase
{
    /// <summary>
    /// 랭킹 캐시 최초 적재(워밍업). POST /api/admin/boss-rush/rank/warmup?force=false
    /// <para>Redis에 적재하지 못하면 503으로 답해 호출한 스크립트가 실패로 판정하게 한다.</para>
    /// </summary>
    [HttpPost("rank/warmup")]
    public async Task<IActionResult> RankWarmup([FromQuery] bool force = false, CancellationToken cancellationToken = default)
    {
        var reject = RejectIfUnauthorized();
        if (reject is not null)
        {
            return reject;
        }

        var result = await warmupService.WarmUpAsync(force, cancellationToken);
        var failed = result.Status == Constants.BossRush.RankWarmupStatus.CacheUnavailable;

        return AdminResponse(
            failed ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status200OK,
            !failed,
            failed ? "Boss rush rank cache warmup failed" : "Boss rush rank cache warmup done",
            result);
    }
}
