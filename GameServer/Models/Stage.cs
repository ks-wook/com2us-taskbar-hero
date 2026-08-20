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
