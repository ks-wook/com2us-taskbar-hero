namespace GameServer.Models;

// AttendanceRepository 전용 DB 행 매핑용 POCO(제네릭 매핑 전용, dynamic 금지).
// Dapper.MatchNamesWithUnderscores=true(Program.cs)로 snake_case 컬럼 → PascalCase 프로퍼티 매핑.

/// <summary>player_attendance 행 매핑용 POCO(snake_case → PascalCase 자동 매핑).</summary>
class AttendanceProgressRow
{
    public int AttendCount { get; set; }
    public int LastAttendDate { get; set; }
}

// ── 마스터 데이터 정의(불변). Repositories/MasterDb 가 적재하고 MasterDbProvider 가 조회한다. ──
/// <summary>출석부 일차별 보상 정의(attendance_master). Day는 날짜가 아니라 이번달 누적 출석 순번(1~30).
/// RewardType 1:골드 2:아이템 3:재료(메일 첨부와 동일 enum), 골드는 RewardCode 0.</summary>
public sealed record AttendanceRewardDef(int Day, int RewardType, int RewardCode, int Quantity);

// ── 마스터 적재용 DB 행 매핑 POCO(제네릭 매핑 전용, dynamic 금지). snake_case→PascalCase는 Dapper 규칙으로 매핑.
//    DECIMAL 컬럼은 decimal로 받아 float/double로 캐스팅한다. 적재는 Repositories/MasterDb/MasterDbLoader. ──
class AttendanceMasterRow
{
    public int Day { get; set; }
    public int RewardType { get; set; }
    public int RewardCode { get; set; }
    public int Quantity { get; set; }
}
