using GameServer.MasterData;
using GameServer.Repositories;
using TaskbarHero.Common;
using TaskbarHero.Common.Dto;
using ZLogger;

namespace GameServer.Services;

public interface IAttendanceService
{
    Task<SaveResult> StatusAsync(long userId);
    Task<SaveResult> ClaimAsync(long userId);
}

/// <summary>
/// 출석부 보상 처리(attendance 기획서 §5·§6). "오늘"은 요청 수신 시점의 서버 시각을 KST(UTC+9) 자정 경계로
/// 판정하며(서버 권위, 클라이언트 날짜 불신), 보상 일차는 <b>날짜가 아니라 이번달 누적 출석 순번</b>
/// (= 이번달 출석 수 + 1, 1~30)이다 — 월중에 처음 접속해도 1일차 보상부터 순서대로 받는다.
/// 일차별 보상은 attendance_master가 확정한다.
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
    /// 이번달 출석 진행도를 반환한다(5.1). 서버 KST 기준 이번달·오늘을 판정하고, 이번달 출석 수로
    /// 진행 일차(attendedCount·todayDay·canClaim)를 산출한 뒤 attendance_master 전체 일차(1~30)의 보상과
    /// 수령 여부(day ≤ attendedCount)를 구성한다. 상태는 바꾸지 않는다.
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
        var (monthFrom, monthTo) = MonthRange(kstNow);

        // 이번달 출석한 일자(YYYYMMDD) 목록. 행 수 = 수령 완료한 일차 수(일차를 따로 저장하지 않는다).
        var claimedDates = await _attendanceRepository.GetClaimedDatesAsync(userId, monthFrom, monthTo);
        var attendedCount = claimedDates.Count;
        var todayClaimed = claimedDates.Contains(today);

        // 오늘 해당하는 일차: 이미 받았으면 오늘 받은 일차(= 누적 수), 아니면 다음 일차(= 누적 수 + 1).
        // 사다리를 모두 소진했으면 0(수령 불가).
        var maxDay = _masterData.MaxAttendanceDay;
        int todayDay;
        if (todayClaimed)
        {
            todayDay = attendedCount;
        }
        else
        {
            var nextDay = attendedCount + 1;
            todayDay = nextDay <= maxDay ? nextDay : 0;
        }

        // 보상 사다리: attendance_master에 정의된 전 일차(1~30). 앞에서부터 순서대로 수령되므로
        // claimed는 "day ≤ 이번달 누적 출석 수"로 판정한다(날짜와 무관).
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
                claimed = day <= attendedCount,
            });
        }

        var data = new AttendanceStatusResultData
        {
            yearMonth = yearMonth,
            today = today,
            attendedCount = attendedCount,
            todayDay = todayDay,
            todayClaimed = todayClaimed,
            canClaim = !todayClaimed && todayDay >= 1,
            days = days,
        };

        return new SaveResult(ErrorCode.Success, "OK", data);
    }

    /// <summary>
    /// 오늘자 출석 보상을 획득한다(5.2·6.1). "일차 산출(이번달 출석 수 + 1) + 출석 기록 삽입 + 보상 메일 발급"을
    /// 리포지토리 트랜잭션으로 원자 적용하며, 일차별 보상·메일 초안(템플릿 301)은 트랜잭션 안에서 콜백으로 확정한다
    /// (일차가 트랜잭션 내 집계로 정해지므로 미리 만들 수 없다). 하루 1회 — 이미 출석이면 9001,
    /// 이번달 마지막 일차까지 모두 받았으면 9002, 캐릭터 생성 전(계정 세이브 없음)이면 2001.
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
        var (monthFrom, monthTo) = MonthRange(kstNow);

        // 메일 템플릿은 일차와 무관하므로 먼저 확인한다(없으면 마스터 결함 → 10001, §6.2).
        var template = _masterData.GetMailTemplate(AttendanceMailTemplateCode);
        if (template is null)
        {
            _logger.ZLogError($"출석 보상 메일 템플릿 미정의: templateCode {AttendanceMailTemplateCode:@TemplateCode} — mail_master 확인 필요");
            return new SaveResult(ErrorCode.MasterDataNotLoaded, string.Empty, null);
        }

        var nowUnix = now.ToUnixTimeSeconds();
        var outcome = await _attendanceRepository.ApplyClaimAsync(
            userId, today, monthFrom, monthTo, _masterData.MaxAttendanceDay,
            day => ComposeRewardMail(template, day, nowUnix), nowUnix);

        switch (outcome.Status)
        {
            case AttendanceClaimStatus.NoPlayer:
                // 계정 세이브(game_player) 미생성 — 캐릭터 생성 전.
                return new SaveResult(ErrorCode.SaveNotFound, string.Empty, null);
            case AttendanceClaimStatus.AlreadyClaimed:
                return new SaveResult(ErrorCode.AttendanceAlreadyClaimed, string.Empty, null);
            case AttendanceClaimStatus.AllClaimed:
                // 이번달 보상 사다리 소진(31일 있는 달에 하루도 빠짐없이 출석한 경우).
                return new SaveResult(ErrorCode.AttendanceAllClaimed, string.Empty, null);
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

    /// <summary>KST 시각을 출석 일자 YYYYMMDD(int)로 변환한다.</summary>
    private static int ToYyyyMmDd(DateTimeOffset kst)
        => kst.Year * 10000 + kst.Month * 100 + kst.Day;

    /// <summary>KST 기준 이번달 집계 범위(이달 1일, 이달 말일)를 YYYYMMDD로 돌려준다.
    /// 출석 일차는 이 범위의 출석 행 수로 산출하므로, 달이 바뀌면 자동으로 1일차부터 다시 시작한다.</summary>
    private static (int From, int To) MonthRange(DateTimeOffset kst)
    {
        var yearMonth = kst.Year * 10000 + kst.Month * 100;
        return (yearMonth + 1, yearMonth + DateTime.DaysInMonth(kst.Year, kst.Month));
    }
}
