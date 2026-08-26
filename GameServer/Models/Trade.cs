namespace GameServer.Models;

// TradeRepository 전용 DB 행 매핑용 POCO(제네릭 매핑 전용, dynamic 금지).
// Dapper.MatchNamesWithUnderscores=true(Program.cs)로 snake_case 컬럼 → PascalCase 프로퍼티 매핑.

class TradeListingRow
{
    public long ListingId { get; set; }
    public long SellerUserId { get; set; }
    public int ItemCode { get; set; }
    public int EnhanceLevel { get; set; }
    public int Quantity { get; set; }
    public long Price { get; set; }
    public long CreatedAt { get; set; }
    public long ExpiresAt { get; set; }
}

class TradeListingStatusRow
{
    public long ListingId { get; set; }
    public long SellerUserId { get; set; }
    public int ItemCode { get; set; }
    public int EnhanceLevel { get; set; }
    public int Quantity { get; set; }
    public long Price { get; set; }
    public long CreatedAt { get; set; }
    public long ExpiresAt { get; set; }
    public int Status { get; set; }
}

class TradePlayerItemRow
{
    public long PlayerItemId { get; set; }
    public int ItemCode { get; set; }
    public long Quantity { get; set; }
    public int EnhanceLevel { get; set; }
}
