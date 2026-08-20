namespace GameServer.Models;

// AttendanceRepository 전용 DB 행 매핑용 POCO(제네릭 매핑 전용, dynamic 금지).
// Dapper.MatchNamesWithUnderscores=true(Program.cs)로 snake_case 컬럼 → PascalCase 프로퍼티 매핑.

/// <summary>player_attendance 행 매핑용 POCO(snake_case → PascalCase 자동 매핑).</summary>
class AttendanceProgressRow
{
    public int AttendCount { get; set; }
    public int LastAttendDate { get; set; }
}
