namespace GameServer.Models;

// GrowthRepository 전용 DB 행 매핑용 POCO(제네릭 매핑 전용, dynamic 금지).
// Dapper.MatchNamesWithUnderscores=true(Program.cs)로 snake_case 컬럼 → PascalCase 프로퍼티 매핑.

class SkillCodeLevelRow
{
    public int SkillCode { get; set; }
    public int Level { get; set; }
}
