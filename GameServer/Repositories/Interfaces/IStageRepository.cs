using GameServer.MasterData;
using GameServer.Repositories;

namespace GameServer.Repositories.Interfaces;

public interface IStageRepository
{
    Task<StageProgressRow?> GetProgressAsync(long userId);

    /// <summary>현재 진입 스테이지를 설정한다(game_player.act/difficulty/stage). 갱신 행 수 반환.</summary>
    Task<int> SetCurrentStageAsync(long userId, int act, int difficulty, int stage, long nowUnix);

    /// <summary>
    /// 클리어를 한 트랜잭션으로 적용한다: 진입 스테이지 재검증 → 활성 획득량 버프 배율 판정 →
    /// 골드/경험치 지급·전리품 적재 → 진행도 갱신. baseGold·baseExp는 마스터의 기본 보상이며 배율은 이 안에서 곱한다.
    /// 경험치→레벨 계산은 주입된 <see cref="ILevelUpCalculator"/>가 처리한다(스테이지·오프라인 공용 정본).
    /// 인벤토리 용량이 부족하면 전리품만 폐기하고(<see cref="ClearOutcome.LootStored"/>=false) 클리어는 성공시킨다.
    /// </summary>
    Task<ClearOutcome> ApplyClearAsync(
        long userId,
        int expectedAct, int expectedDifficulty, int expectedStage,
        long baseGold, long baseExp, DroppedItem? dropped,
        long nowUnix);
}
