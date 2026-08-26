using GameServer.Logging;
using GameServer.Repositories.GameDb.Interfaces;
using GameServer.Repositories.MemoryDb.Interfaces;
using GameServer.Util;

namespace GameServer.Batch;

/// <summary>
/// 동시 접속 히스토리 배치(로그 이벤트 정의 7장 <c>history.online_user</c>). 5분마다 최근 활동 계정 수를 세어
/// 1행을 방출한다.
/// <para><b>하트비트를 액션 로그로 남기지 않기 위한 배치</b>다 — <c>update-last-active</c>는 5분 주기 ×
/// 전체 접속자라 그것 하나가 이 체계의 전체 볼륨을 넘지만, 같은 주기에 <b>접속자 수만 세면 1행</b>이고
/// 필요한 답(동접 추이)은 그대로 나온다.</para>
/// <para>설정: appsettings "OnlineUserHistoryBatch" 섹션(IntervalSeconds 기본 300 · ActiveWindowSeconds 기본 600).</para>
/// </summary>
public sealed class OnlineUserHistoryBatchScheduler : PeriodicBatchScheduler
{
    private readonly int _intervalSeconds;
    private readonly int _activeWindowSeconds;
    private readonly IEventLogger _eventLogger;

    /// <summary>설정에서 실행 주기·활동 창을 읽는다(없거나 0 이하이면 기본값).</summary>
    public OnlineUserHistoryBatchScheduler(
        IServiceScopeFactory scopeFactory, IConfiguration configuration,
        ILogger<OnlineUserHistoryBatchScheduler> logger, IEventLogger eventLogger)
        : base(scopeFactory, logger, eventLogger)
    {
        var interval = configuration.GetValue(
            "OnlineUserHistoryBatch:IntervalSeconds", Constants.Batch.OnlineUserHistory.DefaultIntervalSeconds);
        var window = configuration.GetValue(
            "OnlineUserHistoryBatch:ActiveWindowSeconds", Constants.Batch.OnlineUserHistory.DefaultActiveWindowSeconds);
        _intervalSeconds = interval > 0 ? interval : Constants.Batch.OnlineUserHistory.DefaultIntervalSeconds;
        _activeWindowSeconds = window > 0 ? window : Constants.Batch.OnlineUserHistory.DefaultActiveWindowSeconds;
        _eventLogger = eventLogger;
    }

    protected override TimeSpan Interval => TimeSpan.FromSeconds(_intervalSeconds);

    protected override string BatchName => "동시 접속 히스토리 배치";

    protected override string BatchKey => "history-online-user";

    /// <summary>
    /// 기동 시 현재 버킷을 <b>따라잡지 않는다</b> — 이 배치는 발화 1회가 곧 동접 그래프의 점 하나라,
    /// 재기동할 때마다 같은 5분 구간에 점이 하나 더 찍히면 추이가 부풀려진다. 다음 경계(:00·:05·:10…)부터 시작한다.
    /// </summary>
    protected override bool CatchUpOnStart => false;

    /// <summary>
    /// 1주기 작업: 활동 창(기본 10분) 안에 하트비트를 보낸 계정 수를 세어 <c>history.online_user</c> 1행을 낸다.
    /// <para><b>0명이어도 방출한다</b> — 접속이 0인 시간대가 비어 있으면 그 구간이 "집계가 안 돈 것"인지
    /// "아무도 없었던 것"인지 구분되지 않는다. 추이 그래프에 구멍을 남기지 않는 것이 이 배치의 일이다.</para>
    /// </summary>
    protected override async Task<BatchCycleResult> RunCycleAsync(IServiceScope scope, CancellationToken stoppingToken)
    {
        var historyRepository = scope.ServiceProvider.GetRequiredService<IHistoryRepository>();

        var activeSince = DateTimeUtil.NowUnixSeconds() - _activeWindowSeconds;
        var onlineCount = await historyRepository.CountOnlineUsersAsync(activeSince);

        _eventLogger.Action(
            Constants.EventLog.Tags.History.OnlineUser, null, new OnlineUserHistoryEvent(onlineCount));

        // 방출한 히스토리 행 수를 처리량으로 환산한다(이 배치는 언제나 1행).
        return new BatchCycleResult(1, 0, 0);
    }
}
