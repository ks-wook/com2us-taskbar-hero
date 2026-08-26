using GameServer.Repositories.GameDb;
using GameServer.Repositories.GameDb.Interfaces;
using ZLogger;
using GameServer.Repositories.MemoryDb.Interfaces;
using GameServer.Util;
using GameServer.Logging;

namespace GameServer.Batch;

/// <summary>
/// 메일 보관 GC 배치(mail 기획서 6.5). 발급(수신) 시각 기준 7일이 지난 메일을 주기적으로 삭제한다
/// (player_mail DELETE — 첨부 player_mail_reward는 FK CASCADE로 함께 삭제).
/// 단 <b>미수령 무기한 메일(expires_at=0)은 보관</b>한다 — 거래소 구매 아이템처럼 만료를 두지 않기로 한
/// 메일까지 지우면 무기한 발급이 무의미해지기 때문이다(수령 후에는 보관 기한이 지나면 정리된다).
/// 전용 삭제 API는 두지 않으며, 1회 처리 건수를 제한해 밀린 분량은 다음 주기로 이월한다.
/// <para><b>실행 시각: 매시 00분</b> — 값은 <see cref="BatchSettingConstants.MailGc"/>에 있다
/// (보관 기간은 <see cref="Constants.Mail.RetentionSeconds"/>).</para>
/// </summary>
public sealed class MailGcBatchScheduler : PeriodicBatchScheduler
{
    private readonly int _intervalSeconds;
    private readonly int _batchSize;
    private readonly ILogger<MailGcBatchScheduler> _logger;

    /// <summary>설정에서 실행 주기·1회 처리 상한을 읽는다(없거나 0 이하이면 기본값).</summary>
    public MailGcBatchScheduler(
        IServiceScopeFactory scopeFactory, IConfiguration configuration,
        ILogger<MailGcBatchScheduler> logger, IEventLogger eventLogger)
        : base(scopeFactory, logger, eventLogger)
    {
        var interval = configuration.GetValue(
            BatchSettingConstants.MailGc.IntervalSecondsKey, BatchSettingConstants.MailGc.DefaultIntervalSeconds);
        var batchSize = configuration.GetValue(
            BatchSettingConstants.MailGc.BatchSizeKey, BatchSettingConstants.MailGc.DefaultBatchSize);
        _intervalSeconds = interval > 0 ? interval : BatchSettingConstants.MailGc.DefaultIntervalSeconds;
        _batchSize = batchSize > 0 ? batchSize : BatchSettingConstants.MailGc.DefaultBatchSize;
        _logger = logger;
    }

    protected override TimeSpan Interval => TimeSpan.FromSeconds(_intervalSeconds);

    protected override string BatchName => "메일 GC 배치";

    protected override string BatchKey => "mail-gc";

    /// <summary>
    /// 1주기 작업: 보관 기한(now − 7일) 이전에 발급된 메일 중 <b>미수령 무기한 메일을 제외</b>하고
    /// 상한(BatchSize)까지 삭제한 뒤 요약을 남긴다. 대상 0건이면 로그를 남기지 않는다(소음 방지).
    /// </summary>
    protected override async Task<BatchCycleResult> RunCycleAsync(IServiceScope scope, CancellationToken stoppingToken)
    {
        var mailRepository = scope.ServiceProvider.GetRequiredService<IMailRepository>();

        var now = DateTimeUtil.NowUnixSeconds();
        var deleted = await mailRepository.DeleteRetentionExpiredAsync(now - Constants.Mail.RetentionSeconds, _batchSize);

        if (deleted > 0)
        {
            _logger.ZLogInformation($"메일 GC 배치: 삭제 {deleted:@Deleted}건 (보관 7일 경과 대상, 1회 상한 {_batchSize:@BatchSize}건)");
        }

        // 삭제는 조건 한 방(DELETE ... LIMIT)이라 건별 스킵·실패가 없다.
        return new BatchCycleResult(deleted, 0, 0);
    }
}
