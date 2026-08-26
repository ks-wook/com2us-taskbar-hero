namespace GameServer.Models;

// OfflineRepository 전용 DB 행 매핑용 POCO(제네릭 매핑 전용, dynamic 금지).
// Dapper.MatchNamesWithUnderscores=true(Program.cs)로 snake_case 컬럼 → PascalCase 프로퍼티 매핑.

class OfflineContextRow
{
    public long LastActiveAt { get; set; }
    public int Act { get; set; }
    public int Difficulty { get; set; }
    public int Stage { get; set; }
}

class LastActiveRow
{
    public long LastActiveAt { get; set; }
}
