using GameServer.Repositories.GameDb;

namespace GameServer.Repositories.GameDb.Interfaces;

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
