using GameServer.Data;
using MySqlConnector;
using SqlKata.Execution;

namespace GameServer.Repositories;

public enum AttendanceClaimStatus
{
    Ok,
    NoPlayer,       // game_player 없음(세이브 미생성)
    AlreadyClaimed, // 오늘자 출석을 이미 수령(동시 요청 경합 포함)
    AllClaimed,     // 이번달 마지막 일차(30)까지 모두 수령 — 더 받을 보상 없음
    RewardNotFound, // 산출된 일차의 보상이 attendance_master에 없음(마스터 결함)
}

/// <summary>출석 획득 트랜잭션 결과. 성공 시 Day = 이번에 받은 출석 일차, MailId = 발급된 보상 메일.</summary>
public sealed record AttendanceClaimOutcome(AttendanceClaimStatus Status, int Day, long MailId)
{
    public static AttendanceClaimOutcome Fail(AttendanceClaimStatus status, int day = 0) => new(status, day, 0);
}

public interface IAttendanceRepository
{
    /// <summary>지정 기간(YYYYMMDD, 양끝 포함)에 출석한 일자 목록을 조회한다. 이번달 출석 진행도 구성에 사용한다.</summary>
    Task<IReadOnlyList<int>> GetClaimedDatesAsync(long userId, int fromDate, int toDate);

    /// <summary>
    /// 오늘자 출석 획득을 한 트랜잭션으로 적용한다(attendance 기획서 §6.1): 일차 산출(이번달 출석 수 + 1)
    /// + 출석 기록 삽입(하루 1회) + 보상 메일 발급. <b>일차 산출을 같은 트랜잭션에 넣어</b> 산출과 삽입 사이에
    /// 다른 요청이 끼어들어 같은 일차가 두 번 발급되는 것을 막는다.
    /// (user_id, attend_date) PK가 하루 1회를 보장하며, 이미 행이 있으면(동시 요청 경합 포함) AlreadyClaimed.
    /// 계정 세이브(game_player)가 없으면 NoPlayer, 산출 일차가 maxDay를 넘으면 AllClaimed.
    /// </summary>
    /// <param name="monthFromDate">이번달 집계 시작 일자(YYYYMMDD, 이달 1일).</param>
    /// <param name="monthToDate">이번달 집계 종료 일자(YYYYMMDD, 이달 말일).</param>
    /// <param name="maxDay">보상 사다리의 마지막 일차(attendance_master 최대 day).</param>
    /// <param name="composeRewardMail">산출된 일차 → 발급할 보상 메일 초안. null을 돌려주면 RewardNotFound.</param>
    Task<AttendanceClaimOutcome> ApplyClaimAsync(
        long userId, int attendDate, int monthFromDate, int monthToDate, int maxDay,
        Func<int, MailDraft?> composeRewardMail, long nowUnix);
}

/// <summary>출석 기록 세이브 접근 계층(taskbar_hero_game). SqlKata 쿼리 빌더 + 제네릭 매핑만 사용한다(dynamic 금지).</summary>
public sealed class AttendanceRepository : IAttendanceRepository
{
    private readonly GameDbFactory _dbFactory;

    /// <summary>세이브 DB 커넥션 팩토리를 주입받는다.</summary>
    public AttendanceRepository(GameDbFactory dbFactory) => _dbFactory = dbFactory;

    /// <summary>player_attendance에서 기간 내 attend_date를 오름차순으로 조회한다(행 존재 = 그날 수령함).</summary>
    public async Task<IReadOnlyList<int>> GetClaimedDatesAsync(long userId, int fromDate, int toDate)
    {
        await using var connection = _dbFactory.CreateConnection();
        await connection.OpenAsync();

        var db = _dbFactory.Create(connection);
        var dates = await db.Query("player_attendance")
            .Select("attend_date")
            .Where("user_id", userId)
            .WhereBetween("attend_date", fromDate, toDate)
            .OrderBy("attend_date")
            .GetAsync<int>();
        return dates.ToList();
    }

    /// <summary>
    /// 일차 산출 + 출석 기록 삽입 + 보상 메일 발급을 단일 커넥션의 단일 트랜잭션으로 적용한다.
    /// 검증 실패(NoPlayer·AlreadyClaimed·AllClaimed·RewardNotFound)는 즉시 롤백 후 Fail 상태로 반환하고, 예외는 롤백 후 전파한다.
    /// </summary>
    /// <remarks>
    /// 한 트랜잭션으로 묶는 작업(하나라도 실패하면 전부 롤백 — 출석만 기록되고 메일이 없는 상태를 막는다):
    /// <para>1) game_player SELECT — 계정 세이브 존재 확인(없으면 NoPlayer. 캐릭터 생성 전 호출)</para>
    /// <para>2) player_attendance SELECT — 오늘자 기존 행 확인(있으면 AlreadyClaimed)</para>
    /// <para>3) player_attendance COUNT(이번달) — 일차 산출(day = 이번달 출석 수 + 1). maxDay 초과면 AllClaimed</para>
    /// <para>4) 보상 메일 초안 렌더링 — 산출된 일차의 보상 확정(정의 없으면 RewardNotFound)</para>
    /// <para>5) player_attendance INSERT — (user_id, attend_date) PK가 동시 요청을 직렬화, 중복 키면 경합 패배(AlreadyClaimed)</para>
    /// <para>6) player_mail + player_mail_reward INSERT — 보상 메일 발급(MailRepository.InsertMailAsync, §6.4 규약)</para>
    /// </remarks>
    public async Task<AttendanceClaimOutcome> ApplyClaimAsync(
        long userId, int attendDate, int monthFromDate, int monthToDate, int maxDay,
        Func<int, MailDraft?> composeRewardMail, long nowUnix)
    {
        await using var connection = _dbFactory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var db = _dbFactory.Create(connection);

            // 1) 계정 세이브 확인. 없으면 출석 기록의 FK(fk_attend_player)가 깨지므로 먼저 거부한다.
            var player = await db.Query("game_player")
                .Select("user_id")
                .Where("user_id", userId)
                .FirstOrDefaultAsync<long?>(transaction);
            if (player is null)
            {
                await transaction.RollbackAsync();
                return AttendanceClaimOutcome.Fail(AttendanceClaimStatus.NoPlayer);
            }

            // 2) 오늘자 기존 행 확인(행 존재 = 이미 수령).
            var existing = await db.Query("player_attendance")
                .Select("attend_date")
                .Where("user_id", userId).Where("attend_date", attendDate)
                .FirstOrDefaultAsync<int?>(transaction);
            if (existing is not null)
            {
                await transaction.RollbackAsync();
                return AttendanceClaimOutcome.Fail(AttendanceClaimStatus.AlreadyClaimed);
            }

            // 3) 일차 산출 — 이번달 출석 수 + 1. 날짜(day-of-month)가 아니라 누적 출석 순번이므로
            //    월중에 처음 출석해도 1일차부터 시작한다. 집계를 이 트랜잭션 안에서 해야 삽입과의 사이가 벌어지지 않는다.
            var attendedCount = await db.Query("player_attendance")
                .Where("user_id", userId)
                .WhereBetween("attend_date", monthFromDate, monthToDate)
                .CountAsync<int>(transaction: transaction);
            var day = attendedCount + 1;
            if (day > maxDay)
            {
                await transaction.RollbackAsync();
                return AttendanceClaimOutcome.Fail(AttendanceClaimStatus.AllClaimed, day);
            }

            // 4) 산출된 일차의 보상으로 메일 초안을 렌더링한다(마스터에 없으면 결함 → RewardNotFound).
            var rewardMail = composeRewardMail(day);
            if (rewardMail is null)
            {
                await transaction.RollbackAsync();
                return AttendanceClaimOutcome.Fail(AttendanceClaimStatus.RewardNotFound, day);
            }

            // 5) 출석 기록 삽입. PK (user_id, attend_date)가 하루 1회를 보장 — 중복 키는 동시 요청 경합 패배.
            try
            {
                await db.Query("player_attendance").InsertAsync(new
                {
                    user_id = userId,
                    attend_date = attendDate,
                    claimed_at = nowUnix,
                }, transaction);
            }
            catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
            {
                await transaction.RollbackAsync();
                return AttendanceClaimOutcome.Fail(AttendanceClaimStatus.AlreadyClaimed);
            }

            // 6) 보상 메일 발급(같은 트랜잭션).
            var mailId = await MailRepository.InsertMailAsync(db, transaction, userId, rewardMail, nowUnix);

            await transaction.CommitAsync();
            return new AttendanceClaimOutcome(AttendanceClaimStatus.Ok, day, mailId);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
