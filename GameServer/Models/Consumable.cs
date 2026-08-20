namespace GameServer.Models;

// ConsumableRepository 전용 DB 행 매핑용 POCO(제네릭 매핑 전용, dynamic 금지).
// Dapper.MatchNamesWithUnderscores=true(Program.cs)로 snake_case 컬럼 → PascalCase 프로퍼티 매핑.

class ConsumablePlayerItemRow
{
    public long PlayerItemId { get; set; }
    public int ItemCode { get; set; }
    public int RowType { get; set; }
    public long Quantity { get; set; }
    public int? Slot { get; set; } // 가방 변경분(5.0) 조립용 배치 칸
}

class BuffRow
{
    public int BuffType { get; set; }
    public decimal BuffValue { get; set; } // DECIMAL(5,3) → decimal로 받아 float로 캐스팅
    public long StartedAt { get; set; }
    public long ExpiresAt { get; set; }
}
