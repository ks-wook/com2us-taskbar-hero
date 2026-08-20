using GameServer.Repositories;

namespace GameServer.Repositories.Interfaces;

public interface IConsumableRepository
{
    /// <summary>
    /// 소모품 1개 사용을 한 트랜잭션으로 적용한다: 대상 행 소유·수량 확인 → decide(마스터 검증: 소모품 여부·배율·
    /// 지속시간·누적 상한) → 아이템 1개 차감(0이면 행 삭제) + player_buff upsert. plan은
    /// (기존 버프, 지속시간) → (새 started_at, 새 expires_at, 상한 초과 여부)를 산출한다.
    /// </summary>
    Task<ConsumableUseOutcome> ApplyUseAsync(
        long userId, long itemId,
        Func<int, ConsumableDecision> decide,
        Func<PlayerBuffRow?, int, (long startedAt, long expiresAt, bool overLimit)> plan,
        long nowUnix);

    /// <summary>계정의 활성 버프(expires_at > now)를 조회한다. 코어 세이브 로드의 activeBuffs 항목이 사용한다.</summary>
    Task<List<PlayerBuffRow>> GetActiveBuffsAsync(long userId, long nowUnix);
}
