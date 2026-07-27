using GameServer.Data;
using MySqlConnector;
using SqlKata.Execution;

namespace GameServer.Repositories;

public enum AttendanceClaimStatus
{
    Ok,
    NoPlayer,       // game_player 없음(세이브 미생성)
    AlreadyClaimed, // 오늘자 출석을 이미 수령(동시 요청 경합 포함)
}

/// <summary>출석 획득 트랜잭션 결과. 성공 시 MailId = 발급된 보상 메일.</summary>
public sealed record AttendanceClaimOutcome(AttendanceClaimStatus Status, long MailId)
{
    public static AttendanceClaimOutcome Fail(AttendanceClaimStatus status) => new(status, 0);
}

public interface IAttendanceRepository
{
    /// <summary>지정 기간(YYYYMMDD, 양끝 포함)에 출석한 일자 목록을 조회한다. 이번달 출석 달력 구성에 사용한다.</summary>
    Task<IReadOnlyList<int>> GetClaimedDatesAsync(long userId, int fromDate, int toDate);

    /// <summary>
    /// 오늘자 출석 획득을 한 트랜잭션으로 적용한다: 출석 기록 삽입(하루 1회) + 보상 메일 발급(attendance 기획서 §6.1).
    /// (user_id, attend_date) PK가 하루 1회를 보장하며, 이미 행이 있으면(동시 요청 경합 포함) AlreadyClaimed.
    /// 계정 세이브(game_player)가 없으면 NoPlayer.
    /// </summary>
    Task<AttendanceClaimOutcome> ApplyClaimAsync(long userId, int attendDate, MailDraft rewardMail, long nowUnix);
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
    /// 출석 기록 삽입 + 보상 메일 발급을 단일 커넥션의 단일 트랜잭션으로 적용한다.
    /// 검증 실패(NoPlayer·AlreadyClaimed)는 즉시 롤백 후 Fail 상태로 반환하고, 예외는 롤백 후 전파한다.
    /// </summary>
    /// <remarks>
    /// 한 트랜잭션으로 묶는 작업(하나라도 실패하면 전부 롤백 — 출석만 기록되고 메일이 없는 상태를 막는다):
    /// <para>1) game_player SELECT — 계정 세이브 존재 확인(없으면 NoPlayer. 캐릭터 생성 전 호출)</para>
    /// <para>2) player_attendance SELECT — 오늘자 기존 행 확인(있으면 AlreadyClaimed)</para>
    /// <para>3) player_attendance INSERT — (user_id, attend_date) PK가 동시 요청을 직렬화, 중복 키면 경합 패배(AlreadyClaimed)</para>
    /// <para>4) player_mail + player_mail_reward INSERT — 보상 메일 발급(MailRepository.InsertMailAsync, §6.4 규약)</para>
    /// </remarks>
    public async Task<AttendanceClaimOutcome> ApplyClaimAsync(
        long userId, int attendDate, MailDraft rewardMail, long nowUnix)
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

            // 3) 출석 기록 삽입. PK (user_id, attend_date)가 하루 1회를 보장 — 중복 키는 동시 요청 경합 패배.
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

            // 4) 보상 메일 발급(같은 트랜잭션).
            var mailId = await MailRepository.InsertMailAsync(db, transaction, userId, rewardMail, nowUnix);

            await transaction.CommitAsync();
            return new AttendanceClaimOutcome(AttendanceClaimStatus.Ok, mailId);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
