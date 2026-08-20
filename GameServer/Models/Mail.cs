namespace GameServer.Models;

// MailRepository 전용 DB 행 매핑용 POCO(제네릭 매핑 전용, dynamic 금지).
// Dapper.MatchNamesWithUnderscores=true(Program.cs)로 snake_case 컬럼 → PascalCase 프로퍼티 매핑.

class MailRow
{
    public long MailId { get; set; }
    public int Category { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public int IsRead { get; set; }
    public int Claimed { get; set; }
    public long CreatedAt { get; set; }
    public long ExpiresAt { get; set; }
}

class MailRewardRow
{
    public long MailId { get; set; }
    public int Seq { get; set; }
    public int RewardType { get; set; }
    public int RewardCode { get; set; }
    public long Quantity { get; set; }
    public int EnhanceLevel { get; set; }
}
