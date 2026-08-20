namespace GameServer.Models;

// GachaRepository 전용 DB 행 매핑용 POCO(제네릭 매핑 전용, dynamic 금지).
// Dapper.MatchNamesWithUnderscores=true(Program.cs)로 snake_case 컬럼 → PascalCase 프로퍼티 매핑.

class GachaCounterRow
{
    public int Grade { get; set; }
    public int PityCount { get; set; }
}

class GachaPullRow
{
    public long PullId { get; set; }
    public int GachaCode { get; set; }
    public int PullType { get; set; }
    public int CostCurrencyCode { get; set; }
    public long CostAmount { get; set; }
    public long PulledAt { get; set; }
}

class GachaPullItemRow
{
    public long PullId { get; set; }
    public int Seq { get; set; }
    public int ItemCode { get; set; }
    public int Grade { get; set; }
    public int Quantity { get; set; }
    public int PityApplied { get; set; }
    public int Guaranteed { get; set; }
}
