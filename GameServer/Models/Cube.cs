namespace GameServer.Models;

// CubeRepository 전용 DB 행 매핑용 POCO(제네릭 매핑 전용, dynamic 금지).
// Dapper.MatchNamesWithUnderscores=true(Program.cs)로 snake_case 컬럼 → PascalCase 프로퍼티 매핑.

class PlayerItemBriefRow
{
    public long PlayerItemId { get; set; }
    public int ItemCode { get; set; }
    public int RowType { get; set; }
    public long Quantity { get; set; }
    public int? Slot { get; set; }
}

class CubeStateRow
{
    public int CubeLevel { get; set; }
    public long CubeExp { get; set; }
}
