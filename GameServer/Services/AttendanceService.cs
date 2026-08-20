using GameServer.MasterData;
using GameServer.Repositories.GameDb;
using GameServer.Repositories.GameDb.Interfaces;
using TaskbarHero.Common;
using TaskbarHero.Common.Dto;
using ZLogger;
using GameServer.Repositories.MasterDb;
using GameServer.Models;

namespace GameServer.Services;

public interface IAttendanceService
{
    Task<SaveResult> StatusAsync(long userId);
    Task<SaveResult> ClaimAsync(long userId);
}

/// <summary>
/// 출석부 보상 처리(attendance 기획서 §5·§6). "오늘"은 요청 수신 시점의 서버 시각을 KST(UTC+9) 자정 경계로
/// 판정하며(서버 권위, 클라이언트 날짜 불신), 보상 일차는 <b>날짜가 아니라 누적 출석 순번</b>
/// (= 누적 출석일수 % 30 + 1, 1~30)이다 — 언제 처음 접속해도 1일차 보상부터 순서대로 받고,
/// 30일차까지 받으면 <b>다시 1일차부터 순환</b>한다(사다리 소진·월 리셋으로 진행이 끊기지 않는다).
/// 일차별 보상은 attendance_master가 확정한다.
/// 보상은 즉시 지급하지 않고 메일(category=3, 템플릿 301)로 발급한다 — 계정 반영은 우편함 수령 시.
/// </summary>
public sealed class AttendanceService : IAttendanceService
{
    /// <summary>출석 보상 메일 템플릿 코드(mail_master 301, {0} = 출석 일차).</summary>
    private const int AttendanceMailTemplateCode = 301;

    private static readonly TimeSpan KstOffset = TimeSpan.FromHours(9);

    private readonly IAttendanceRepository _attendanceRepository;
    private readonly MasterDbProvider _masterData;
    private readonly ILogger<AttendanceService> _logger;

    /// <summary>의존성(출석 리포지토리·마스터 데이터·로거)을 주입받는다.</summary>
    public AttendanceService(
        IAttendanceRepository attendanceRepository, MasterDbProvider masterData, ILogger<AttendanceService> logger)
    {
        _attendanceRepository = attendanceRepository;
        _masterData = masterData;
        _logger = logger;
    }

    /// <summary>
    /// 출석 진행도를 반환한다(5.1). 서버 KST 기준 오늘을 판정하고, 계정 진행도 1행(누적 출석일수 +
    /// 마지막 획득 일자)으로 attendedCount·todayDay·canClaim을 산출한 뒤 attendance_master 전체 일차(1~30)의
    /// 보상과 <b>현재 회차에서의</b> 수령 여부(day ≤ 회차 진행도)를 구성한다. 상태는 바꾸지 않는다.
    /// </summary>
    public async Task<SaveResult> StatusAsync(long userId)
    {
        if (!_masterData.IsLoaded)
        {
            return new SaveResult(ErrorCode.MasterDataNotLoaded, string.Empty, null);
        }

        var kstNow = DateTimeOffset.UtcNow.ToOffset(KstOffset);
        var today = ToYyyyMmDd(kstNow);
        var yearMonth = kstNow.Year * 100 + kstNow.Month;

        // 진행도는 계정당 1행(누적 출석일수 + 마지막 획득 일자). 세이브가 없으면 빈 진행도로 응답한다.
        var progress = await _attendanceRepository.GetProgressAsync(userId);
        var attendedCount = progress?.AttendCount ?? 0;
        var todayClaimed = progress is not null && progress.LastAttendDate == today;

        // 사다리는 maxDay 주기로 순환한다(§2). 오늘 해당 일차는 이미 받았으면 오늘 받은 일차(= 현재 회차 진행도),
        // 아니면 다음 일차(= 누적 수 % maxDay + 1)다. 순환하므로 "받을 보상이 없는" 상태는 없다.
        var maxDay = _masterData.MaxAttendanceDay;
        var progressInCycle = ProgressInCycle(attendedCount, maxDay);
        var todayDay = todayClaimed ? progressInCycle : NextDay(attendedCount, maxDay);

        // 보상 사다리: attendance_master에 정의된 전 일차(1~30). 현재 회차에서 앞에서부터 순서대로 수령되므로
        // claimed는 "day ≤ 현재 회차 진행도"로 판정한다(날짜와 무관, 새 회차가 시작되면 다시 비워진다).
        var days = new List<AttendanceDayDto>();
        foreach (var day in _masterData.AttendanceDays)
        {
            var reward = _masterData.GetAttendanceReward(day)!;
            days.Add(new AttendanceDayDto
            {
                day = day,
                rewardType = reward.RewardType,
                rewardCode = reward.RewardCode,
                quantity = reward.Quantity,
                claimed = day <= progressInCycle,
            });
        }

        var data = new AttendanceStatusResultData
        {
            yearMonth = yearMonth,
            today = today,
            attendedCount = attendedCount,
            todayDay = todayDay,
            todayClaimed = todayClaimed,
            canClaim = !todayClaimed,
            days = days,
        };

        return new SaveResult(ErrorCode.Success, "OK", data);
    }

    /// <summary>
    /// 오늘자 출석 보상을 획득한다(5.2·6.1). "일차 산출(누적 출석일수 % 30 + 1) + 진행도 조건부 갱신 + 보상 메일 발급"을
    /// 리포지토리 트랜잭션으로 원자 적용하며, 일차별 보상·메일 초안(템플릿 301)은 트랜잭션 안에서 콜백으로 확정한다
    /// (일차가 트랜잭션 내에서 정해지므로 미리 만들 수 없다). 하루 1회 — 이미 출석이면 9001,
    /// 캐릭터 생성 전(계정 세이브 없음)이면 2001. 마지막 일차 다음은 1일차로 순환하므로 소진 실패는 없다.
    /// 실제 재화·아이템 반영은 우편함 수령 시 이루어진다.
    /// </summary>
    public async Task<SaveResult> ClaimAsync(long userId)
    {
        if (!_masterData.IsLoaded)
        {
            return new SaveResult(ErrorCode.MasterDataNotLoaded, string.Empty, null);
        }

        var now = DateTimeOffset.UtcNow;
        var kstNow = now.ToOffset(KstOffset);
        var today = ToYyyyMmDd(kstNow);

        // 메일 템플릿은 일차와 무관하므로 먼저 확인한다(없으면 마스터 결함 → 10001, §6.2).
        var template = _masterData.GetMailTemplate(AttendanceMailTemplateCode);
        if (template is null)
        {
            _logger.ZLogError($"출석 보상 메일 템플릿 미정의: templateCode {AttendanceMailTemplateCode:@TemplateCode} — mail_master 확인 필요");
            return new SaveResult(ErrorCode.MasterDataNotLoaded, string.Empty, null);
        }

        var nowUnix = now.ToUnixTimeSeconds();
        var outcome = await _attendanceRepository.ApplyClaimAsync(
            userId, today, _masterData.MaxAttendanceDay,
            day => ComposeRewardMail(template, day, nowUnix), nowUnix);

        switch (outcome.Status)
        {
            case AttendanceClaimStatus.NoPlayer:
                // 계정 세이브(game_player) 미생성 — 캐릭터 생성 전.
                return new SaveResult(ErrorCode.SaveNotFound, string.Empty, null);
            case AttendanceClaimStatus.AlreadyClaimed:
                return new SaveResult(ErrorCode.AttendanceAlreadyClaimed, string.Empty, null);
            case AttendanceClaimStatus.RewardNotFound:
                _logger.ZLogError($"출석 일차 보상 미정의: day {outcome.Day:@Day} — attendance_master 확인 필요");
                return new SaveResult(ErrorCode.MasterDataNotLoaded, string.Empty, null);
        }

        // 트랜잭션에서 확정된 일차의 보상을 응답에 그대로 싣는다(위에서 발급된 메일 첨부와 동일).
        var reward = _masterData.GetAttendanceReward(outcome.Day)!;
        var data = new AttendanceClaimResultData
        {
            attendDate = today,
            day = outcome.Day,
            reward = new AttendanceRewardDto
            {
                rewardType = reward.RewardType,
                rewardCode = reward.RewardCode,
                quantity = reward.Quantity,
            },
            mailId = outcome.MailId,
        };

        _logger.ZLogInformation($"출석 보상 발급: userId {userId:@UserId}, attendDate {today:@AttendDate}, day {outcome.Day:@Day}, mailId {outcome.MailId:@MailId}");
        return new SaveResult(ErrorCode.Success, "Attended", data);
    }

    /// <summary>산출된 출석 일차의 보상(attendance_master)으로 발급할 메일 초안을 렌더링한다.
    /// 그 일차 보상이 마스터에 없으면 null을 돌려 호출측(트랜잭션)이 RewardNotFound로 중단하게 한다.</summary>
    private MailDraft? ComposeRewardMail(MailTemplateDef template, int day, long nowUnix)
    {
        var reward = _masterData.GetAttendanceReward(day);
        if (reward is null)
        {
            return null;
        }

        return MailComposer.Compose(
            template, day.ToString(), nowUnix,
            new[] { new MailAttachment(reward.RewardType, reward.RewardCode, reward.Quantity) });
    }

    /// <summary>
    /// 누적 출석 수에서 <b>현재 회차의 진행도</b>(= 이 회차에서 수령 완료한 일차 수, 0~maxDay)를 구한다.
    /// 사다리가 maxDay 주기로 순환하므로 마지막 일차를 채운 시점은 maxDay(전부 수령), 그 다음 출석은 1이 된다.
    /// 예) maxDay=30 → count 30이면 30, count 31이면 1.
    /// </summary>
    private static int ProgressInCycle(int attendedCount, int maxDay)
        => attendedCount <= 0 || maxDay <= 0 ? 0 : ((attendedCount - 1) % maxDay) + 1;

    /// <summary>누적 출석 수에서 <b>다음에 받을 일차</b>(1~maxDay)를 구한다. 마지막 일차를 채웠으면 다시 1일차로 순환한다.</summary>
    private static int NextDay(int attendedCount, int maxDay)
        => maxDay <= 0 ? 0 : (attendedCount % maxDay) + 1;

    /// <summary>KST 시각을 출석 일자 YYYYMMDD(int)로 변환한다.</summary>
    private static int ToYyyyMmDd(DateTimeOffset kst)
        => kst.Year * 10000 + kst.Month * 100 + kst.Day;

}
