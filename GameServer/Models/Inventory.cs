namespace GameServer.Models;

// InventoryRepository 전용 DB 행 매핑용 POCO(제네릭 매핑 전용, dynamic 금지).
// Dapper.MatchNamesWithUnderscores=true(Program.cs)로 snake_case 컬럼 → PascalCase 프로퍼티 매핑.

class ItemCodeEnhanceSlotRow
{
    public int ItemCode { get; set; }
    public int EnhanceLevel { get; set; }
    public int? Slot { get; set; } // 장착 중이면 NULL(가방 칸 미점유)
}

class ItemRowTypeSlotRow
{
    public int RowType { get; set; }
    public int? Slot { get; set; }
    public int ItemCode { get; set; }
    public long Quantity { get; set; }
    public int EnhanceLevel { get; set; }
}

class BagItemRow
{
    public long PlayerItemId { get; set; }
    public int Slot { get; set; }
    public int ItemCode { get; set; }
    public long Quantity { get; set; }
    public int EnhanceLevel { get; set; }
}
