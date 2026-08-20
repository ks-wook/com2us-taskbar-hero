using GameServer.MasterData;
using GameServer.Repositories;

namespace GameServer.Repositories.Interfaces;

public interface ICubeRepository
{
    /// <summary>
    /// 합성을 한 트랜잭션으로 적용한다: 입력 아이템 소유·미장착 확인 → decide(마스터 검증: 등급/슬롯/클래스/개수, 결과 산출)
    /// → 입력 삭제 + 상위 등급 결과 아이템 생성 + 큐브 경험치 반영. 큐브 성장 계산은 주입된 ICubeLevelCalculator가 처리한다(합성·분해·제작 공용 정본).
    /// </summary>
    Task<CombineOutcome> ApplyCombineAsync(
        long userId, IReadOnlyList<long> itemIds,
        Func<int, IReadOnlyList<CombineInput>, CombineDecision> decide,
        long nowUnix);

    /// <summary>
    /// 분해를 한 트랜잭션으로 적용한다: 각 아이템 소유·미장착·수량 확인 → computeReward(마스터 등급으로 골드·경험치 산출)
    /// → 아이템 차감/삭제 + 골드 적립 + 큐브 경험치 반영.
    /// </summary>
    Task<DismantleOutcome> ApplyDismantleAsync(
        long userId, IReadOnlyList<(long itemId, int count)> items,
        Func<int, IReadOnlyList<DismantleInput>, DismantleReward> computeReward,
        long nowUnix);

    /// <summary>
    /// 제작을 한 트랜잭션으로 적용한다: 큐브 레벨·골드·재료 확인 → 골드·재료 차감 + 결과 아이템 지급 + 큐브 경험치 반영.
    /// recipe 존재 여부는 호출 전(서비스, 마스터)에서 검증한다. resultItemType/resultStackMax는 결과 아이템 마스터 값.
    /// </summary>
    Task<CraftOutcome> ApplyCraftAsync(
        long userId, RecipeDef recipe, int resultItemType, int resultStackMax, long cubeExpGain,
        long nowUnix);
}
