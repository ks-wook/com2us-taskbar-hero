using GameServer.Repositories;

namespace GameServer.Repositories.Interfaces;

public interface IOfflineRepository
{
    /// <summary>정산 기준 시각·파밍 스테이지 좌표를 읽는다(계정 없으면 null).</summary>
    Task<OfflinePlayerContext?> GetContextAsync(long userId);

    /// <summary>
    /// 오프라인 보상을 한 트랜잭션으로 적용한다: 잠금 상태의 last_active_at으로 경과를 재계산하고
    /// (최소 기준 미만이면 AlreadyClaimed) last_active_at을 CAS(관측값 조건부)로 now로 리셋해 정산권을 선점한 뒤,
    /// computeReward로 보상을 산출해 골드 적립·전 캐릭터 경험치 지급·레벨 재계산을 수행한다.
    /// CAS가 0행이면(동시 요청이 먼저 정산) 롤백하고 AlreadyClaimed를 반환한다.
    /// 경험치→레벨 계산은 주입된 applyExp 델리게이트(현재 level·exp + 지급 exp → 반영 후 상태)로 처리한다.
    /// </summary>
    Task<OfflineClaimOutcome> ClaimAsync(
        long userId,
        long nowUnix,
        long minRewardSec,
        Func<long, (long effectiveSec, bool capped, long gold, long exp)> computeReward);
}
