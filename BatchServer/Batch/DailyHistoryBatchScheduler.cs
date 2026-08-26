using GameServer.Logging;
using GameServer.MasterData;
using GameServer.Repositories.GameDb.Interfaces;
using GameServer.Repositories.MemoryDb.Interfaces;
using GameServer.Util;
using ZLogger;

namespace GameServer.Batch;

/// <summary>
/// 일 단위 히스토리 배치(로그 이벤트 정의 7장). 하루 1회 <b>상태 스냅샷 6종</b>을 방출한다 —
/// <c>history.item_supply</c>·<c>stage_progress</c>·<c>equip_item</c>·<c>party_comp</c>·
/// <c>skill_build</c>·<c>skill_invest</c>.
/// <para><b>여섯을 한 배치로 묶는다</b> — 모두 <c>player_item</c>·<c>player_character</c>·<c>player_skill</c>
/// 전체를 GROUP BY 하는 무거운 집계라, 나누면 무거운 스캔이 하루에 여섯 번 서로 다른 시각에 흩어진다.
/// 한 번에 몰아 돌려야 트래픽이 낮은 시간대에 가둘 수 있고, 여섯 스냅샷이 <b>같은 시점의 상태</b>가 되어
/// 서로 나눠 볼 수 있다(예: 착용률 = <c>equip_item</c> ÷ <c>item_supply</c>).</para>
/// <para><b>폴링하지 않는다</b> — 다음 실행 시각까지 자고 정확히 그때 깨어난다.</para>
/// <para><b>실행 시각: 매일 KST 05시 00분</b> — 값은 <see cref="BatchSettingConstants.DailyHistory"/>에 있다.</para>
/// </summary>
public sealed class DailyHistoryBatchScheduler : PeriodicBatchScheduler
{
    private readonly int _intervalSeconds;
    private readonly int _runHourKst;
    private readonly ILogger<DailyHistoryBatchScheduler> _logger;
    private readonly IEventLogger _eventLogger;

    /// <summary>설정에서 실행 주기(재시도 간격)·실행 시각(KST 시)을 읽는다(없거나 범위 밖이면 기본값).</summary>
    public DailyHistoryBatchScheduler(
        IServiceScopeFactory scopeFactory, IConfiguration configuration,
        ILogger<DailyHistoryBatchScheduler> logger, IEventLogger eventLogger)
        : base(scopeFactory, logger, eventLogger)
    {
        var interval = configuration.GetValue(
            BatchSettingConstants.DailyHistory.IntervalSecondsKey, BatchSettingConstants.DailyHistory.DefaultIntervalSeconds);
        var runHour = configuration.GetValue(
            BatchSettingConstants.DailyHistory.RunHourKstKey, BatchSettingConstants.DailyHistory.DefaultRunHourKst);
        _intervalSeconds = interval > 0 ? interval : BatchSettingConstants.DailyHistory.DefaultIntervalSeconds;
        _runHourKst =
            runHour >= BatchSettingConstants.DailyHistory.MinRunHourKst
            && runHour <= BatchSettingConstants.DailyHistory.MaxRunHourKst
                ? runHour
                : BatchSettingConstants.DailyHistory.DefaultRunHourKst;
        _logger = logger;
        _eventLogger = eventLogger;
    }

    protected override TimeSpan Interval => TimeSpan.FromSeconds(_intervalSeconds);

    protected override string BatchName => "일 단위 히스토리 배치";

    protected override string BatchKey => "history-daily";

    /// <summary>
    /// 실행 시각 = <b>KST 05시 00분</b>(<see cref="BatchSettingConstants.DailyHistory.DefaultRunHourKst"/>). 벽시계 시각이라,
    /// 프로세스를 언제 띄웠든 늘 같은 시각에 돈다.
    /// <para>오늘분을 아직 다루지 않았으면 <b>오늘 그 시각</b>을 돌려준다 — 이미 지난 시각이면 골격이 곧바로
    /// 발화하므로, 예정 시각에 프로세스가 내려가 있었어도 <b>그날의 스냅샷이 비지 않는다</b>. 적재 테이블이
    /// <c>(log_date, …)</c> 자연 키라 어떤 이유로 같은 날 두 번 돌아도 덮어쓰기가 되므로 이 따라잡기는 안전하다.
    /// 오늘분을 다뤘으면 내일 같은 시각으로 넘긴다.</para>
    /// <para>고정 24시간 간격으로 두지 않는 이유는, 그러면 <b>실행 시각이 프로세스 기동 시각에 끌려다녀</b>
    /// 재기동을 몇 번 하면 집계가 한낮으로 밀리기 때문이다.</para>
    /// </summary>
    protected override ValueTask<long> NextFireTimeAsync(
        IServiceScope scope, long lastFireUnix, long nowUnix, CancellationToken stoppingToken)
    {
        var todayFire = RunAtUnix(DateTimeUtil.ToKst(nowUnix));

        // KST는 서머타임이 없어 하루가 항상 정확히 86400초다(내일 같은 시각 = +1일).
        return new(todayFire > lastFireUnix ? todayFire : todayFire + DateTimeUtil.SecondsPerDay);
    }

    /// <summary>
    /// 주어진 KST 날짜의 실행 시각(기본 05시 00분 00초)을 유닉스초로 환산한다.
    /// </summary>
    private long RunAtUnix(DateTimeOffset kst)
        => DateTimeUtil.ToUnixSeconds(
            new DateTimeOffset(
                kst.Year, kst.Month, kst.Day, _runHourKst,
                BatchSettingConstants.DailyHistory.RunMinute, BatchSettingConstants.DailyHistory.RunSecond,
                DateTimeUtil.KstOffset));

    /// <summary>
    /// 1주기 작업: 상태 스냅샷 6종을 차례로 집계해 방출한다. <c>log_date</c>는 <b>집계를 돌린 시점의 KST 날짜</b>이며
    /// 여섯 종이 같은 값을 공유한다 — 자정을 걸쳐 실행돼도 한 스냅샷이 두 날짜로 갈라지지 않게 한 번만 읽는다.
    /// <para>예정 시각을 지나 기동하면 <b>그날분을 따라잡아</b> 1회 돈다(발화 시각 계산 참고) — 프로세스가
    /// 내려가 있었더라도 그날의 스냅샷이 비지 않는다. 적재 테이블이 <c>(log_date, …)</c> 자연 키 PK라
    /// 어떤 이유로든 같은 날 다시 돌면 <b>덮어쓰는 것이 정상</b>이다(7장).</para>
    /// </summary>
    protected override async Task<BatchCycleResult> RunCycleAsync(IServiceScope scope, CancellationToken stoppingToken)
    {
        var historyRepository = scope.ServiceProvider.GetRequiredService<IHistoryRepository>();
        var logDate = DateTimeUtil.ToDateString(DateTimeUtil.KstNow);

        var emitted = 0;
        emitted += await EmitItemSupplyAsync(historyRepository, logDate);
        emitted += await EmitStageProgressAsync(historyRepository, logDate);
        emitted += await EmitEquipItemAsync(historyRepository, logDate);
        emitted += await EmitPartyCompAsync(historyRepository, logDate);
        emitted += await EmitSkillBuildAsync(historyRepository, logDate);
        emitted += await EmitSkillInvestAsync(historyRepository, logDate);

        _logger.ZLogInformation($"일 단위 히스토리: logDate {logDate:@LogDate}, 스냅샷 {emitted:@Rows}행 방출");

        // 방출한 히스토리 행 수를 처리량으로 환산한다(집계 쿼리라 건별 스킵·실패가 없다).
        return new BatchCycleResult(emitted, 0, 0);
    }

    /// <summary>아이템별 유통량 스냅샷을 방출하고 방출 행 수를 돌려준다(<c>history.item_supply</c>).</summary>
    private async Task<int> EmitItemSupplyAsync(IHistoryRepository repository, string logDate)
    {
        var rows = await repository.GetItemSupplyAsync();
        foreach (var row in rows)
        {
            _eventLogger.Action(
                Constants.EventLog.Tags.History.ItemSupply, null,
                new ItemSupplyHistoryEvent(logDate, row.ItemCode, row.TotalCount, row.HolderCount));
        }

        return rows.Count;
    }

    /// <summary>
    /// 진행도 분포 스냅샷을 방출한다(<c>history.stage_progress</c>).
    /// <para>게임 DB가 들고 있는 값은 <b>진행 시퀀스</b>(1~100)이므로 좌표 규약으로 <c>stage_id</c>로 환산한다 —
    /// 로그의 축은 다른 스테이지 테이블(<c>stage_clear_logs</c> 등)과 같은 <c>stage_id</c>여야 조인이 된다.
    /// 아직 한 판도 깨지 못한 계정(시퀀스 0)은 <c>stage_id=0</c>으로 그대로 센다.</para>
    /// </summary>
    private async Task<int> EmitStageProgressAsync(IHistoryRepository repository, string logDate)
    {
        var rows = await repository.GetStageProgressAsync();
        foreach (var row in rows)
        {
            var stageId = StageCoords.TryDecodeSequence(row.Sequence, out var act, out var difficulty, out var stage)
                ? StageCoords.StageId(act, difficulty, stage)
                : 0;

            _eventLogger.Action(
                Constants.EventLog.Tags.History.StageProgress, null,
                new StageProgressHistoryEvent(logDate, stageId, row.UserCount));
        }

        return rows.Count;
    }

    /// <summary>착용 장비 분포 스냅샷을 방출하고 방출 행 수를 돌려준다(<c>history.equip_item</c>).</summary>
    private async Task<int> EmitEquipItemAsync(IHistoryRepository repository, string logDate)
    {
        var rows = await repository.GetEquipItemAsync();
        foreach (var row in rows)
        {
            _eventLogger.Action(
                Constants.EventLog.Tags.History.EquipItem, null,
                new EquipItemHistoryEvent(
                    logDate, row.ItemCode, row.EquipSlot, row.EquippedCount, row.HolderCount));
        }

        return rows.Count;
    }

    /// <summary>파티 조합 분포 스냅샷을 방출하고 방출 행 수를 돌려준다(<c>history.party_comp</c>).</summary>
    private async Task<int> EmitPartyCompAsync(IHistoryRepository repository, string logDate)
    {
        var rows = await repository.GetPartyCompAsync();
        foreach (var row in rows)
        {
            _eventLogger.Action(
                Constants.EventLog.Tags.History.PartyComp, null,
                new PartyCompHistoryEvent(
                    logDate, row.Slot1ClassCode, row.Slot2ClassCode, row.Slot3ClassCode, row.UserCount));
        }

        return rows.Count;
    }

    /// <summary>액티브 스킬 조합 스냅샷을 방출하고 방출 행 수를 돌려준다(<c>history.skill_build</c>).</summary>
    private async Task<int> EmitSkillBuildAsync(IHistoryRepository repository, string logDate)
    {
        var rows = await repository.GetSkillBuildAsync();
        foreach (var row in rows)
        {
            _eventLogger.Action(
                Constants.EventLog.Tags.History.SkillBuild, null,
                new SkillBuildHistoryEvent(
                    logDate, row.ClassCode, row.ActiveSkillCode1, row.ActiveSkillCode2, row.CharacterCount));
        }

        return rows.Count;
    }

    /// <summary>스킬 투자 분포 스냅샷을 방출하고 방출 행 수를 돌려준다(<c>history.skill_invest</c>).</summary>
    private async Task<int> EmitSkillInvestAsync(IHistoryRepository repository, string logDate)
    {
        var rows = await repository.GetSkillInvestAsync();
        foreach (var row in rows)
        {
            _eventLogger.Action(
                Constants.EventLog.Tags.History.SkillInvest, null,
                new SkillInvestHistoryEvent(
                    logDate, row.ClassCode, row.SkillCode, row.Level, row.CharacterCount));
        }

        return rows.Count;
    }
}
