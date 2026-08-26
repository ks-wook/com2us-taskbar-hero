using GameServer.Logging;
using GameServer.Repositories.GameDb.Interfaces;
using GameServer.Repositories.MemoryDb.Interfaces;
using GameServer.Util;
using ZLogger;

namespace GameServer.Batch;

/// <summary>
/// 시간 단위 히스토리 배치(로그 이벤트 정의 7장). 1시간마다 <b>재화 유통 총량</b>(<c>history.currency_supply</c>)과
/// <b>거래소 호가 스냅샷</b>(<c>history.trade_market</c>)을 방출한다.
/// <para><b>둘을 한 배치로 묶는 이유</b>는 주기가 같기 때문만이 아니다 — 유통 총량과 시세는 함께 읽어야
/// 뜻이 생기는 짝이다(총량이 늘면서 호가가 오르면 인플레이션, 총량이 그대로인데 호가만 오르면 품귀).
/// 배치를 나누면 두 스냅샷의 시각이 어긋나 그 비교가 흐려진다.</para>
/// <para><b>실행 시각: 매시 00분</b> — 값은 <see cref="BatchSettingConstants.HourlyHistory"/>에 있다.</para>
/// </summary>
public sealed class HourlyHistoryBatchScheduler : PeriodicBatchScheduler
{
    private readonly int _intervalSeconds;
    private readonly ILogger<HourlyHistoryBatchScheduler> _logger;
    private readonly IEventLogger _eventLogger;

    /// <summary>설정에서 실행 주기를 읽는다(없거나 0 이하이면 기본값 1시간).</summary>
    public HourlyHistoryBatchScheduler(
        IServiceScopeFactory scopeFactory, IConfiguration configuration,
        ILogger<HourlyHistoryBatchScheduler> logger, IEventLogger eventLogger)
        : base(scopeFactory, logger, eventLogger)
    {
        var interval = configuration.GetValue(
            BatchSettingConstants.HourlyHistory.IntervalSecondsKey, BatchSettingConstants.HourlyHistory.DefaultIntervalSeconds);
        _intervalSeconds = interval > 0 ? interval : BatchSettingConstants.HourlyHistory.DefaultIntervalSeconds;
        _logger = logger;
        _eventLogger = eventLogger;
    }

    protected override TimeSpan Interval => TimeSpan.FromSeconds(_intervalSeconds);

    protected override string BatchName => "시간 단위 히스토리 배치";

    protected override string BatchKey => "history-hourly";

    /// <summary>
    /// 기동 시 지나간 실행을 <b>따라잡지 않는다</b> — 재화 유통·호가는 1회 실행이 곧 시계열의 점 하나라,
    /// 재기동할 때마다 같은 시간대에 점이 하나 더 찍히면 추이가 부풀려진다. 다음 정시부터 시작한다.
    /// </summary>
    protected override bool CatchUpOnStart => false;

    /// <summary>
    /// 1주기 작업: 재화 종류별 유통 스냅샷과 판매중 아이템별 호가 스냅샷을 세어 각각 1행씩 방출한다.
    /// <para>만료 판정 기준 시각을 <b>한 번만 읽어 두 집계에 함께 넘긴다</b> — 우편함 부채와 호가가
    /// 서로 다른 순간을 가리키면 같은 주기의 두 행이 같은 시점의 스냅샷이 아니게 된다.</para>
    /// </summary>
    protected override async Task<BatchCycleResult> RunCycleAsync(IServiceScope scope, CancellationToken stoppingToken)
    {
        var historyRepository = scope.ServiceProvider.GetRequiredService<IHistoryRepository>();
        var now = DateTimeUtil.NowUnixSeconds();

        var supplies = await historyRepository.GetCurrencySupplyAsync(now);
        foreach (var supply in supplies)
        {
            _eventLogger.Action(
                Constants.EventLog.Tags.History.CurrencySupply, null,
                new CurrencySupplyHistoryEvent(
                    supply.CurrencyCode, supply.TotalAmount, supply.HolderCount, supply.MailPendingAmount));
        }

        var markets = await historyRepository.GetTradeMarketAsync(now);
        foreach (var market in markets)
        {
            _eventLogger.Action(
                Constants.EventLog.Tags.History.TradeMarket, null,
                new TradeMarketHistoryEvent(
                    market.ItemCode, market.ListingCount, market.MinPrice, market.AvgPrice));
        }

        _logger.ZLogInformation($"시간 단위 히스토리: 재화 {supplies.Count:@CurrencyRows}종, 거래 호가 {markets.Count:@MarketRows}종 방출");

        // 방출한 히스토리 행 수를 처리량으로 환산한다(집계 쿼리라 건별 스킵·실패가 없다).
        return new BatchCycleResult(supplies.Count + markets.Count, 0, 0);
    }
}
