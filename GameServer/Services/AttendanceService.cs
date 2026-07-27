using GameServer.MasterData;
using GameServer.Repositories;
using TaskbarHero.Common;
using TaskbarHero.Common.Dto;

namespace GameServer.Services;

public interface IAttendanceService
{
    Task<SaveResult> StatusAsync(long userId);
    Task<SaveResult> ClaimAsync(long userId);
}

/// <summary>
/// 출석부 보상 처리(attendance 기획서 §5·§6). "오늘"은 요청 수신 시점의 서버 시각을 KST(UTC+9) 자정 경계로
/// 판정하며(서버 권위, 클라이언트 날짜 불신), 일자별 보상은 attendance_master가 확정한다.
/// 보상은 즉시 지급하지 않고 메일(category=3, 템플릿 301)로 발급한다 — 계정 반영은 우편함 수령 시.
/// </summary>
public sealed class AttendanceService : IAttendanceService
{
    /// <summary>출석 보상 메일 템플릿 코드(mail_master 301, {0} = 출석 일차).</summary>
    private const int AttendanceMailTemplateCode = 301;

    private static readonly TimeSpan KstOffset = TimeSpan.FromHours(9);

    private readonly IAttendanceRepository _attendanceRepository;
    private readonly MasterDataProvider _masterData;
    private readonly ILogger<AttendanceService> _logger;

    /// <summary>의존성(출석 리포지토리·마스터 데이터·로거)을 주입받는다.</summary>
    public AttendanceService(
        IAttendanceRepository attendanceRepository, MasterDataProvider masterData, ILogger<AttendanceService> logger)
    {
        _attendanceRepository = attendanceRepository;
        _masterData = masterData;
        _logger = logger;
    }

    /// <summary>
    /// 이번달 출석 현황을 반환한다(5.1). 서버 KST 기준 이번달·오늘을 판정하고, 이달 정의된 각 일자의
    /// 보상(attendance_master)과 수령 여부(player_attendance), 오늘 수령 가능 여부를 구성한다. 상태는 바꾸지 않는다.
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
        var daysInMonth = DateTime.DaysInMonth(kstNow.Year, kstNow.Month);

        // 이번달 출석한 일자(YYYYMMDD) 집합.
        var claimedDates = await _attendanceRepository.GetClaimedDatesAsync(
            userId, yearMonth * 100 + 1, yearMonth * 100 + daysInMonth);
        var claimedSet = claimedDates.ToHashSet();

        // 이달 달력: attendance_master에 정의된 일자 중 이달 범위(1~말일)만 노출.
        var days = new List<AttendanceDayDto>();
        foreach (var day in _masterData.AttendanceDays)
        {
            if (day < 1 || day > daysInMonth)
            {
                continue;
            }

            var reward = _masterData.GetAttendanceReward(day)!;
            days.Add(new AttendanceDayDto
            {
                day = day,
                rewardType = reward.RewardType,
                rewardCode = reward.RewardCode,
                quantity = reward.Quantity,
                claimed = claimedSet.Contains(yearMonth * 100 + day),
            });
        }

        var data = new AttendanceStatusResultData
        {
            yearMonth = yearMonth,
            today = today,
            todayDay = kstNow.Day,
            todayClaimed = claimedSet.Contains(today),
            days = days,
        };

        return new SaveResult(ErrorCode.Success, "OK", data);
    }

    /// <summary>
    /// 오늘자 출석 보상을 획득한다(5.2·6.1). 오늘(KST) 보상을 마스터에서 확정하고, 메일 초안(템플릿 301)을 렌더링한 뒤
    /// "출석 기록 삽입 + 보상 메일 발급"을 리포지토리 트랜잭션으로 원자 적용한다. 하루 1회 — 이미 출석이면 9001,
    /// 캐릭터 생성 전(계정 세이브 없음)이면 2001.
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
        var day = kstNow.Day;

        // 오늘 day의 보상 정의·메일 템플릿. 미정의는 마스터 데이터 결함으로 보고 10001로 거부(§6.2).
        var reward = _masterData.GetAttendanceReward(day);
        var template = _masterData.GetMailTemplate(AttendanceMailTemplateCode);
        if (reward is null || template is null)
        {
            _logger.LogError(
                "출석 마스터 미정의: day {Day}, 보상 {HasReward}, 템플릿 {HasTemplate} — attendance_master/mail_master 확인 필요",
                day, reward is not null, template is not null);
            return new SaveResult(ErrorCode.MasterDataNotLoaded, string.Empty, null);
        }

        var nowUnix = now.ToUnixTimeSeconds();
        var draft = MailComposer.Compose(
            template, day.ToString(), nowUnix,
            new[] { new MailAttachment(reward.RewardType, reward.RewardCode, reward.Quantity) });

        var outcome = await _attendanceRepository.ApplyClaimAsync(userId, today, draft, nowUnix);
        switch (outcome.Status)
        {
            case AttendanceClaimStatus.NoPlayer:
                // 계정 세이브(game_player) 미생성 — 캐릭터 생성 전.
                return new SaveResult(ErrorCode.SaveNotFound, string.Empty, null);
            case AttendanceClaimStatus.AlreadyClaimed:
                return new SaveResult(ErrorCode.AttendanceAlreadyClaimed, string.Empty, null);
        }

        var data = new AttendanceClaimResultData
        {
            attendDate = today,
            day = day,
            reward = new AttendanceRewardDto
            {
                rewardType = reward.RewardType,
                rewardCode = reward.RewardCode,
                quantity = reward.Quantity,
            },
            mailId = outcome.MailId,
        };

        _logger.LogInformation(
            "출석 보상 발급: userId {UserId}, attendDate {AttendDate}, day {Day}, mailId {MailId}",
            userId, today, day, outcome.MailId);
        return new SaveResult(ErrorCode.Success, "Attended", data);
    }

    /// <summary>KST 시각을 출석 일자 YYYYMMDD(int)로 변환한다.</summary>
    private static int ToYyyyMmDd(DateTimeOffset kst)
        => kst.Year * 10000 + kst.Month * 100 + kst.Day;
}
