namespace GameServer.Util;

/// <summary>
/// 서버 시각·날짜 변환을 한곳에 모은 유틸리티. 서버는 <b>UTC Unix 타임스탬프(초)</b>를 시각의 정본으로 쓰고,
/// 일자 경계가 필요한 콘텐츠(출석 등)만 KST(UTC+9)로 환산해 판정한다 — 각 서비스가
/// <c>DateTimeOffset.UtcNow.ToUnixTimeSeconds()</c>·오프셋·초 단위 상수를 따로 들고 있으면
/// 기준이 갈라지므로 모든 변환은 이 클래스를 거친다.
/// <para>DB의 시각 컬럼(<c>*_at</c>)과 응답 DTO의 시각 필드는 모두 이 클래스가 만든 Unix 초 값이다.</para>
/// </summary>
public static class DateTimeUtil
{
    /// <summary>1분(초).</summary>
    public const long SecondsPerMinute = 60;

    /// <summary>1시간(초).</summary>
    public const long SecondsPerHour = 60 * SecondsPerMinute;

    /// <summary>1일(초). 메일 보관·만료·시즌 기간 등 "N일" 계산의 단일 기준.</summary>
    public const long SecondsPerDay = 24 * SecondsPerHour;

    /// <summary>이벤트 로그 timestamp 표기 형식(UTC, 밀리초까지 — 로그 이벤트 정의 4.1).</summary>
    public const string EventTimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";

    /// <summary>일자 경계 판정 기준 시간대(KST = UTC+9). 서버 권위이며 클라이언트 날짜는 신뢰하지 않는다.</summary>
    public static readonly TimeSpan KstOffset = TimeSpan.FromHours(9);

    /// <summary>현재 UTC 시각. 같은 요청에서 Unix 초와 KST 일자를 함께 써야 할 때 이것을 한 번만 받아 파생시킨다.</summary>
    public static DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    /// <summary>현재 시각을 KST(UTC+9) 오프셋으로 환산해 반환한다.</summary>
    public static DateTimeOffset KstNow => DateTimeOffset.UtcNow.ToOffset(KstOffset);

    /// <summary>현재 시각의 Unix 타임스탬프(초). 서버 전역의 시각 기준값이다.</summary>
    public static long NowUnixSeconds() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary>현재 시각의 Unix 타임스탬프(밀리초). 런 소요 시간처럼 초 단위로 부족한 경우에만 쓴다.</summary>
    public static long NowUnixMilliseconds() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>주어진 시각을 Unix 타임스탬프(초)로 변환한다.</summary>
    public static long ToUnixSeconds(DateTimeOffset time) => time.ToUnixTimeSeconds();

    /// <summary>주어진 시각을 Unix 타임스탬프(밀리초)로 변환한다.</summary>
    public static long ToUnixMilliseconds(DateTimeOffset time) => time.ToUnixTimeMilliseconds();

    /// <summary>Unix 타임스탬프(초)를 UTC 시각으로 되돌린다(로그·표기용).</summary>
    public static DateTimeOffset FromUnixSeconds(long unixSeconds)
        => DateTimeOffset.FromUnixTimeSeconds(unixSeconds);

    /// <summary>Unix 타임스탬프(밀리초)를 UTC 시각으로 되돌린다(로그·표기용).</summary>
    public static DateTimeOffset FromUnixMilliseconds(long unixMilliseconds)
        => DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds);

    /// <summary>주어진 시각을 KST(UTC+9) 오프셋으로 환산한다. 일자·월 판정 전에 반드시 거친다.</summary>
    public static DateTimeOffset ToKst(DateTimeOffset time) => time.ToOffset(KstOffset);

    /// <summary>Unix 타임스탬프(초)를 KST 시각으로 환산한다.</summary>
    public static DateTimeOffset ToKst(long unixSeconds) => FromUnixSeconds(unixSeconds).ToOffset(KstOffset);

    /// <summary>시각을 일자 키 YYYYMMDD(int)로 변환한다(예: 2026-08-21 → 20260821).</summary>
    public static int ToDateKey(DateTimeOffset time) => time.Year * 10000 + time.Month * 100 + time.Day;

    /// <summary>시각을 연월 키 YYYYMM(int)로 변환한다(예: 2026-08 → 202608).</summary>
    public static int ToYearMonthKey(DateTimeOffset time) => time.Year * 100 + time.Month;

    /// <summary>KST 기준 <b>오늘</b>의 일자 키 YYYYMMDD(int). 하루 1회 콘텐츠의 중복 판정 기준이다.</summary>
    public static int TodayKstDateKey() => ToDateKey(KstNow);

    /// <summary>
    /// 두 Unix 초 사이의 경과 시간(초)을 구한다. 기준 시각이 미래여도(시계 역행·선반영) 음수를 내지 않고 0으로 깎는다 —
    /// 경과를 그대로 보상·정산 입력으로 쓰기 때문이다.
    /// </summary>
    public static long ElapsedSeconds(long fromUnixSeconds, long nowUnixSeconds)
        => Math.Max(0, nowUnixSeconds - fromUnixSeconds);

    /// <summary>기준 시각(Unix 초)부터 <b>현재</b>까지의 경과 시간(초). 음수는 0으로 깎는다.</summary>
    public static long ElapsedSecondsSince(long fromUnixSeconds)
        => ElapsedSeconds(fromUnixSeconds, NowUnixSeconds());

    /// <summary>일(day)을 초로 환산한다(메일 만료 valid_days·시즌 기간 등).</summary>
    public static long DaysToSeconds(long days) => days * SecondsPerDay;

    /// <summary>시간(hour)을 초로 환산한다.</summary>
    public static long HoursToSeconds(long hours) => hours * SecondsPerHour;

    /// <summary>분(minute)을 초로 환산한다.</summary>
    public static long MinutesToSeconds(long minutes) => minutes * SecondsPerMinute;

    /// <summary>이벤트 로그 한 줄에 실을 현재 시각 문자열(UTC, 밀리초까지)을 만든다.</summary>
    public static string NowEventTimestamp() => DateTimeOffset.UtcNow.ToString(EventTimestampFormat);
}
