using GameServer.Util;

namespace GameServer.Batch;

/// <summary>
/// BatchServer 배치 설정값. 실행 시각·주기·1회 처리 상한과 그 값을 읽는 appsettings 키를 모아 둔다.
/// 게임 규칙 상수는 <c>GameServer/Constants.cs</c>에 있고, 여기에는 이 프로세스의 운영값만 둔다.
/// <code>
/// 배치                 실행 시각(KST)               하는 일
/// ─────────────────────────────────────────────────────────────────────────────────
/// 거래소 만료          매시 00분                    기간 지난 매물 정리 + 아이템 메일 반송
/// 메일 GC              매시 00분                    보관 기간 지난 메일 삭제
/// 보스러시 시즌 정산   시즌 종료 시각(DB end_at)    순위 확정 + 1~3위 보상 메일 + 다음 시즌 개시
/// 동접 히스토리        매시 00 05 10 … 55분         접속자 수 집계
/// 시간 단위 히스토리   매시 00분                    재화 유통량 + 거래소 호가 집계
/// 일 단위 히스토리     매일 05시 00분               상태 스냅샷 6종 집계
/// </code>
/// 위 시각은 프로세스를 언제 띄웠는지와 무관하게 고정이다. appsettings가 값을 덮어쓰면 그 값이 시각을 정한다.
/// </summary>
public static class BatchSettingConstants
{
    /// <summary>한 번에 잘 수 있는 최대 시간(7일). 더 길면 잘라서 자고 실행 시각을 다시 계산한다.</summary>
    public static readonly TimeSpan MaxWait = TimeSpan.FromDays(7);

    /// <summary>실행이 예외로 끝났을 때 다시 시도하기까지의 대기(1분).</summary>
    public static readonly TimeSpan ErrorRetryDelay = TimeSpan.FromMinutes(1);

    /// <summary>보스러시 시즌 정산 — 실행 시각은 진행 중 시즌의 종료 시각(DB <c>end_at</c>).</summary>
    public static class BossRushSeason
    {
        // appsettings 키
        public const string Section = "BossRushSeasonBatch";
        public const string IntervalSecondsKey = Section + ":IntervalSeconds";
        public const string BatchSizeKey = Section + ":BatchSize";

        /// <summary>진행 중 시즌이 없을 때만 쓰는 재확인 주기 10분 → 매시 00 10 20 30 40 50분.</summary>
        public const int DefaultIntervalSeconds = 10 * (int)DateTimeUtil.SecondsPerMinute;

        /// <summary>1회 처리 상한(정산 페이지 크기). 남은 행은 다음 실행이 이어서 처리한다.</summary>
        public const int DefaultBatchSize = 500;

        /// <summary>종료된 시즌 랭킹 캐시 보관 기간(7일).</summary>
        public static readonly TimeSpan ClosedSeasonTtl = TimeSpan.FromDays(7);
    }

    /// <summary>메일 GC — 매시 00분. 보관 기간(<c>Constants.Mail.RetentionSeconds</c>) 지난 메일을 삭제한다.</summary>
    public static class MailGc
    {
        // appsettings 키
        public const string Section = "MailGcBatch";
        public const string IntervalSecondsKey = Section + ":IntervalSeconds";
        public const string BatchSizeKey = Section + ":BatchSize";

        /// <summary>실행 주기 1시간 → 매시 00분.</summary>
        public const int DefaultIntervalSeconds = (int)DateTimeUtil.SecondsPerHour;

        /// <summary>1회 삭제 상한. 초과분은 다음 실행으로 넘긴다.</summary>
        public const int DefaultBatchSize = 500;
    }

    /// <summary>거래소 만료 — 매시 00분. 판매 기간 지난 매물을 정리하고 아이템을 메일로 반송한다.</summary>
    public static class TradeExpire
    {
        // appsettings 키
        public const string Section = "TradeExpireBatch";
        public const string IntervalSecondsKey = Section + ":IntervalSeconds";
        public const string BatchSizeKey = Section + ":BatchSize";

        /// <summary>실행 주기 1시간 → 매시 00분. 아이템이 판매자에게 반송되기까지의 지연 상한이다.</summary>
        public const int DefaultIntervalSeconds = (int)DateTimeUtil.SecondsPerHour;

        /// <summary>1회 처리 상한. 초과분은 다음 실행으로 넘긴다.</summary>
        public const int DefaultBatchSize = 1000;
    }

    /// <summary>동접 히스토리 — 매시 00 05 10 … 55분. 접속자 수를 세어 1행 남긴다.</summary>
    public static class OnlineUserHistory
    {
        // appsettings 키
        public const string Section = "OnlineUserHistoryBatch";
        public const string IntervalSecondsKey = Section + ":IntervalSeconds";
        public const string ActiveWindowSecondsKey = Section + ":ActiveWindowSeconds";

        /// <summary>실행 주기 5분 → 매시 00 05 10 … 55분.</summary>
        public const int DefaultIntervalSeconds = 5 * (int)DateTimeUtil.SecondsPerMinute;

        /// <summary>접속 중으로 볼 마지막 활동 시간(10분). 클라이언트 하트비트 주기 5분의 2배.</summary>
        public const int DefaultActiveWindowSeconds = 10 * (int)DateTimeUtil.SecondsPerMinute;
    }

    /// <summary>시간 단위 히스토리 — 매시 00분. 재화 유통량과 거래소 호가를 함께 집계한다.</summary>
    public static class HourlyHistory
    {
        // appsettings 키
        public const string Section = "HourlyHistoryBatch";
        public const string IntervalSecondsKey = Section + ":IntervalSeconds";

        /// <summary>실행 주기 1시간 → 매시 00분.</summary>
        public const int DefaultIntervalSeconds = (int)DateTimeUtil.SecondsPerHour;
    }

    /// <summary>일 단위 히스토리 — 매일 05시 00분. 상태 스냅샷 6종을 집계한다.</summary>
    public static class DailyHistory
    {
        // appsettings 키
        public const string Section = "DailyHistoryBatch";
        public const string IntervalSecondsKey = Section + ":IntervalSeconds";
        public const string RunHourKstKey = Section + ":RunHourKst";

        /// <summary>실행 주기 1일. 실행 시각은 <see cref="DefaultRunHourKst"/>가 정하므로 쓰이지 않는다.</summary>
        public const int DefaultIntervalSeconds = (int)DateTimeUtil.SecondsPerDay;

        /// <summary>실행 시각 05시(KST). 집계가 무거워 트래픽이 적은 새벽에 돌린다.</summary>
        public const int DefaultRunHourKst = 5;

        /// <summary>실행 시각으로 받는 최솟값 0시.</summary>
    public const int MinRunHourKst = 0;

        /// <summary>실행 시각으로 받는 최댓값 23시. 이 범위를 벗어나면 기본값을 쓴다.</summary>
        public const int MaxRunHourKst = 23;

        /// <summary>실행 시각의 분(00분 고정).</summary>
        public const int RunMinute = 0;

        /// <summary>실행 시각의 초(00초 고정).</summary>
        public const int RunSecond = 0;
    }
}
