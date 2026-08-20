using TaskbarHero.Common.Dto;

namespace GameServer.Models;

// StageRepository 전용 DB 행 매핑용 POCO(제네릭 매핑 전용, dynamic 금지).
// Dapper.MatchNamesWithUnderscores=true(Program.cs)로 snake_case 컬럼 → PascalCase 프로퍼티 매핑.

class PlayerProgressRow
{
    public int Act { get; set; }
    public int Difficulty { get; set; }
    public int Stage { get; set; }
    public int MaxStageCleared { get; set; }
    public int InventoryCapacity { get; set; }
}

class BuffMultiplierRow
{
    public int BuffType { get; set; }
    public decimal BuffValue { get; set; } // DECIMAL(5,3) → decimal로 받아 그대로 곱한다(부동소수 오차 없이 내림).
}

// ── 마스터 데이터 정의(불변). Repositories/MasterDb 가 적재하고 MasterDbProvider 가 조회한다. ──
/// <summary>스테이지 정의(구성). 스폰·보스·배경. 보스는 stage_spawn 의 is_boss=1 행에서 투영한다
/// (보스가 없으면 BossMonsterCode·BossMonsterLevel 모두 0).</summary>
public sealed record StageDef(
    int StageId, int Act, int Difficulty, int Stage,
    int BossMonsterCode, int BossMonsterLevel, int BackgroundType,
    IReadOnlyList<StageSpawnDto> Spawns);
/// <summary>스테이지 클리어 보상 정의. GradeProbs[i] = 등급 (i+1) 드롭 확률(길이 = 최대 등급, stage_reward_drop 기준).</summary>
public sealed record StageRewardDef(long Gold, long Exp, double[] GradeProbs);
/// <summary>드롭 추첨 결과(전리품 1개).</summary>
public sealed record DroppedItem(int ItemCode, long Quantity, int ItemType, int StackMax);

// ── 마스터 적재용 DB 행 매핑 POCO(제네릭 매핑 전용, dynamic 금지). snake_case→PascalCase는 Dapper 규칙으로 매핑.
//    DECIMAL 컬럼은 decimal로 받아 float/double로 캐스팅한다. 적재는 Repositories/MasterDb/MasterDbLoader. ──
class StageMasterRow
{
    public int StageId { get; set; }
    public int Act { get; set; }
    public int Difficulty { get; set; }
    public int Stage { get; set; }
    public int BackgroundType { get; set; }
}
class StageSpawnRow
{
    public int StageId { get; set; }
    public int MonsterCode { get; set; }
    public int MonsterLevel { get; set; }
    public int SpawnCount { get; set; }
    public int IsBoss { get; set; }
}
class StageRewardScalarRow
{
    public int StageId { get; set; }
    public long RewardGold { get; set; }
    public long RewardExp { get; set; }
}
class StageRewardDropRow
{
    public int StageId { get; set; }
    public int Grade { get; set; }
    public decimal DropProb { get; set; }
}
