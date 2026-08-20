namespace GameServer.Models;

// BossRushRepository 전용 DB 행 매핑용 POCO(제네릭 매핑 전용, dynamic 금지).
// Dapper.MatchNamesWithUnderscores=true(Program.cs)로 snake_case 컬럼 → PascalCase 프로퍼티 매핑.

/// <summary>boss_rush_season 행 매핑용 POCO.</summary>
class BossRushSeasonRow
{
    public int SeasonId { get; set; }
    public long StartAt { get; set; }
    public long EndAt { get; set; }
    public int Status { get; set; }
}

/// <summary>game_player 진행도 조회용 POCO(보스러시 해금 판정·정보 조회).</summary>
class BossRushPlayerRow
{
    public int MaxStageCleared { get; set; }
}

/// <summary>boss_rush_run 조회용 POCO(진행 중 런).</summary>
class BossRushRunRow
{
    public long RunId { get; set; }
    public int SeasonId { get; set; }
    public long StartedAt { get; set; }
}

/// <summary>boss_rush_record 최고 기록 조회용 POCO.</summary>
class BossRushRecordRow
{
    public int BestClearMs { get; set; }
    public long RecordedAt { get; set; }
}

/// <summary>boss_rush_record 랭킹 조회용 POCO(final_rank 포함).</summary>
class BossRushRankRecordRow
{
    public long UserId { get; set; }
    public int BestClearMs { get; set; }
    public long RecordedAt { get; set; }
    public int FinalRank { get; set; }
}

/// <summary>boss_rush_record 정산·워밍업 스캔용 POCO.</summary>
class BossRushSettleRow
{
    public long UserId { get; set; }
    public int BestClearMs { get; set; }
    public long RecordedAt { get; set; }
}

/// <summary>game_player 닉네임 조회용 POCO(랭킹 표시 이름).</summary>
class BossRushNicknameRow
{
    public long UserId { get; set; }
    public string? Nickname { get; set; }
}
