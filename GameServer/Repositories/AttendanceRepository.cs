using GameServer.Data;
using SqlKata.Execution;

namespace GameServer.Repositories;

public enum AttendanceClaimStatus
{
    Ok,
    NoPlayer,       // player_attendance 행 없음 = 계정 세이브 미생성(캐릭터 생성 시 함께 만든다)
    AlreadyClaimed, // 오늘자 출석을 이미 수령(동시 요청 경합 포함)
    RewardNotFound, // 산출된 일차의 보상이 attendance_master에 없거나 사다리가 비어 있음(마스터 결함)
}

/// <summary>출석 획득 트랜잭션 결과. 성공 시 Day = 이번에 받은 출석 일차, MailId = 발급된 보상 메일.</summary>
public sealed record AttendanceClaimOutcome(AttendanceClaimStatus Status, int Day, long MailId)
{
    public static AttendanceClaimOutcome Fail(AttendanceClaimStatus status, int day = 0) => new(status, day, 0);
}

/// <summary>출석 진행도 스냅샷(계정당 1행). AttendCount = 누적 출석일수, LastAttendDate = 마지막 획득 일자(YYYYMMDD, 0=없음).</summary>
public sealed record AttendanceProgress(int AttendCount, int LastAttendDate);

public interface IAttendanceRepository
{
    /// <summary>계정의 출석 진행도를 조회한다(계정당 1행). 세이브가 없으면 null.</summary>
    Task<AttendanceProgress?> GetProgressAsync(long userId);

    /// <summary>
    /// 오늘자 출석 획득을 한 트랜잭션으로 적용한다(attendance 기획서 §6.1): 진행도 관측 → 일차 산출
    /// (누적 출석일수 % maxDay + 1) → 진행도 조건부 갱신 → 보상 메일 발급.
    /// <b>하루 1회는 last_attend_date 조건부 갱신(CAS)이 보장한다</b> — 관측한 값과 달라졌으면(동시 요청이 먼저 처리)
    /// 0행이 되어 AlreadyClaimed. 관측 시점에 이미 오늘 날짜면 즉시 AlreadyClaimed.
    /// 행이 없으면(계정 세이브 미생성) NoPlayer. 마지막 일차까지 받으면 다시 1일차로 순환하므로 소진 실패는 없다.
    /// </summary>
    /// <param name="attendDate">오늘 일자(YYYYMMDD, 서버 KST 기준).</param>
    /// <param name="maxDay">보상 사다리의 마지막 일차(attendance_master 최대 day) = 순환 주기. 0 이하면 RewardNotFound.</param>
    /// <param name="composeRewardMail">산출된 일차 → 발급할 보상 메일 초안. null을 돌려주면 RewardNotFound.</param>
    Task<AttendanceClaimOutcome> ApplyClaimAsync(
        long userId, int attendDate, int maxDay, Func<int, MailDraft?> composeRewardMail, long nowUnix);
}

/// <summary>출석 진행도 세이브 접근 계층(taskbar_hero_game). SqlKata 쿼리 빌더 + 제네릭 매핑만 사용한다(dynamic 금지).</summary>
public sealed class AttendanceRepository : IAttendanceRepository
{
    private readonly GameDbFactory _dbFactory;

    /// <summary>세이브 DB 커넥션 팩토리를 주입받는다.</summary>
    public AttendanceRepository(GameDbFactory dbFactory) => _dbFactory = dbFactory;

    /// <summary>player_attendance 단일 행을 읽어 진행도로 돌려준다(행 없음 = 계정 세이브 없음 → null).</summary>
    public async Task<AttendanceProgress?> GetProgressAsync(long userId)
    {
        await using var connection = _dbFactory.CreateConnection();
        await connection.OpenAsync();

        var db = _dbFactory.Create(connection);
        var row = await db.Query("player_attendance")
            .Select("attend_count", "last_attend_date")
            .Where("user_id", userId)
            .FirstOrDefaultAsync<AttendanceProgressRow>();

        return row is null ? null : new AttendanceProgress(row.AttendCount, row.LastAttendDate);
    }

    /// <summary>
    /// 일차 산출 + 진행도 갱신 + 보상 메일 발급을 단일 커넥션의 단일 트랜잭션으로 적용한다.
    /// 검증 실패(NoPlayer·AlreadyClaimed·RewardNotFound)는 즉시 롤백 후 Fail 상태로 반환하고, 예외는 롤백 후 전파한다.
    /// </summary>
    /// <remarks>
    /// 한 트랜잭션으로 묶는 작업(하나라도 실패하면 전부 롤백 — 진행도만 오르고 메일이 없는 상태를 막는다):
    /// <para>1) player_attendance SELECT — 진행도 관측(행 없으면 NoPlayer). 오늘 날짜면 AlreadyClaimed</para>
    /// <para>2) 일차 산출 — day = 갱신 후 누적 % maxDay + 1(30일차 이후 1일차로 순환)</para>
    /// <para>3) 보상 메일 초안 렌더링 — 산출된 일차의 보상 확정(정의 없으면 RewardNotFound)</para>
    /// <para>4) player_attendance 조건부 갱신 — last_attend_date가 관측값일 때만 전이(0행이면 경합 패배 → AlreadyClaimed)</para>
    /// <para>5) player_mail + player_mail_reward INSERT — 보상 메일 발급(MailRepository.InsertMailAsync, §6.4 규약)</para>
    /// </remarks>
    public async Task<AttendanceClaimOutcome> ApplyClaimAsync(
        long userId, int attendDate, int maxDay, Func<int, MailDraft?> composeRewardMail, long nowUnix)
    {
        await using var connection = _dbFactory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var db = _dbFactory.Create(connection);

            if (maxDay <= 0)
            {
                await transaction.RollbackAsync();
                return AttendanceClaimOutcome.Fail(AttendanceClaimStatus.RewardNotFound);
            }

            // 1) 진행도 관측. 행은 캐릭터 생성 시 함께 만들어지므로, 없으면 계정 세이브가 없는 것이다.
            var progress = await db.Query("player_attendance")
                .Select("attend_count", "last_attend_date")
                .Where("user_id", userId)
                .FirstOrDefaultAsync<AttendanceProgressRow>(transaction);
            if (progress is null)
            {
                await transaction.RollbackAsync();
                return AttendanceClaimOutcome.Fail(AttendanceClaimStatus.NoPlayer);
            }

            if (progress.LastAttendDate == attendDate)
            {
                await transaction.RollbackAsync();
                return AttendanceClaimOutcome.Fail(AttendanceClaimStatus.AlreadyClaimed);
            }

            // 2) 일차 산출 — 갱신 후 누적을 기준으로 하며 maxDay 주기로 순환한다(§2 사다리 순환).
            //    날짜(day-of-month)와 무관하므로 월중에 처음 출석해도 1일차부터 시작한다.
            var newCount = progress.AttendCount + 1;
            var day = ((newCount - 1) % maxDay) + 1;

            // 3) 산출된 일차의 보상으로 메일 초안을 렌더링한다(마스터에 없으면 결함 → RewardNotFound).
            var rewardMail = composeRewardMail(day);
            if (rewardMail is null)
            {
                await transaction.RollbackAsync();
                return AttendanceClaimOutcome.Fail(AttendanceClaimStatus.RewardNotFound, day);
            }

            // 4) 진행도 선점(조건부 갱신): 관측한 last_attend_date일 때만 전이한다.
            //    동시 요청이 먼저 처리했으면 관측값과 달라 0행 → 중복 발급을 막는 핵심 게이트.
            var updated = await db.Query("player_attendance")
                .Where("user_id", userId)
                .Where("last_attend_date", progress.LastAttendDate)
                .UpdateAsync(new { attend_count = newCount, last_attend_date = attendDate }, transaction);
            if (updated == 0)
            {
                await transaction.RollbackAsync();
                return AttendanceClaimOutcome.Fail(AttendanceClaimStatus.AlreadyClaimed);
            }

            // 5) 보상 메일 발급(같은 트랜잭션).
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

/// <summary>player_attendance 행 매핑용 POCO(snake_case → PascalCase 자동 매핑).</summary>
file sealed class AttendanceProgressRow
{
    public int AttendCount { get; set; }
    public int LastAttendDate { get; set; }
}
