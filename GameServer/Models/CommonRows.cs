namespace GameServer.Models;

// 여러 리포지토리가 공유하는 DB 행 매핑용 POCO(제네릭 매핑 전용, dynamic 금지).
// Dapper.MatchNamesWithUnderscores=true(Program.cs)로 snake_case 컬럼 → PascalCase 프로퍼티 매핑.

class ItemIdQtyRow
{
    public long PlayerItemId { get; set; }
    public long Quantity { get; set; }
}

/// <summary>가방 변경분(5.0) 조립에 배치 칸이 필요한 조회용(재료 차감·스택 병합).</summary>
class ItemIdQtySlotRow
{
    public long PlayerItemId { get; set; }
    public long Quantity { get; set; }
    public int? Slot { get; set; }
}

class CharClassLevelRow
{
    public int ClassCode { get; set; }
    public int Level { get; set; }
}

class CharProgressRow
{
    public int CharacterId { get; set; }
    public int Level { get; set; }
    public long Exp { get; set; }
}
