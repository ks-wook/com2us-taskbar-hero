namespace GameServer.Models;

// GrowthRepository 전용 DB 행 매핑용 POCO(제네릭 매핑 전용, dynamic 금지).
// Dapper.MatchNamesWithUnderscores=true(Program.cs)로 snake_case 컬럼 → PascalCase 프로퍼티 매핑.

class SkillCodeLevelRow
{
    public int SkillCode { get; set; }
    public int Level { get; set; }
}

// ── 마스터 데이터 정의(불변). Repositories/MasterDb 가 적재하고 MasterDbProvider 가 조회한다. ──
/// <summary>
/// 장비 강화 단계별 규칙(enhance_master). 한 행 = "그 단계로 올릴 때의 비용"(Cost·CurrencyCode)과
/// "그 단계에 도달했을 때의 스탯 배율"(StatMultiplier)이다. 0단계(미강화)는 배율 1.0이라 행이 없고,
/// 정의된 최대 단계 다음이 없으면 강화 불가(MaxEnhanceReached)다.
/// </summary>
public sealed record EnhanceRule(int EnhanceLevel, long Cost, int CurrencyCode, float StatMultiplier);
/// <summary>스킬 정의(skill_master). 성장 검증(직업 소속·액티브/패시브·최대 레벨)에 사용한다. SkillType 1:액티브 2:패시브.</summary>
public sealed record SkillDef(int SkillCode, int ClassCode, int SkillType, int MaxLevel);
/// <summary>룬 정의(rune_master). 성장 검증(선행 룬·최대 레벨)에 사용한다. 레벨별 골드 비용은 rune_cost(자식)에서 조회한다.</summary>
public sealed record RuneDef(int RuneCode, int PrereqCode, int MaxLevel);

// ── 마스터 적재용 DB 행 매핑 POCO(제네릭 매핑 전용, dynamic 금지). snake_case→PascalCase는 Dapper 규칙으로 매핑.
//    DECIMAL 컬럼은 decimal로 받아 float/double로 캐스팅한다. 적재는 Repositories/MasterDb/MasterDbLoader. ──
class EnhanceMasterRow
{
    public int EnhanceLevel { get; set; }
    public long Cost { get; set; }
    public int CurrencyType { get; set; }
    public decimal StatMultiplier { get; set; } // DECIMAL(5,3) → decimal로 받아 float로 캐스팅
}
class SkillMasterRow
{
    public int SkillCode { get; set; }
    public int ClassCode { get; set; }
    public int SkillType { get; set; }
    public int MaxLevel { get; set; }
}
class RuneMasterRow
{
    public int RuneCode { get; set; }
    public int PrereqCode { get; set; }
    public int MaxLevel { get; set; }
}
class RuneCostRow
{
    public int RuneCode { get; set; }
    public int Level { get; set; }
    public long Cost { get; set; }
}
