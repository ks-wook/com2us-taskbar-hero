using GameServer.Repositories.GameDb;
using GameServer.Repositories.GameDb.Interfaces;
using ZLogger;
using GameServer.Repositories.MemoryDb.Interfaces;

namespace GameServer.Batch;

/// <summary>
/// 메일 보관 GC 배치(mail 기획서 6.5). 발급(수신) 시각 기준 7일이 지난 메일을 주기적으로 삭제한다
/// (player_mail DELETE — 첨부 player_mail_reward는 FK CASCADE로 함께 삭제).
/// 단 <b>미수령 무기한 메일(expires_at=0)은 보관</b>한다 — 거래소 구매 아이템처럼 만료를 두지 않기로 한
/// 메일까지 지우면 무기한 발급이 무의미해지기 때문이다(수령 후에는 보관 기한이 지나면 정리된다).
/// 전용 삭제 API는 두지 않으며, 1회 처리 건수를 제한해 밀린 분량은 다음 주기로 이월한다.
/// 설정: appsettings "MailGcBatch" 섹션(IntervalSeconds 기본 3600 · BatchSize 기본 500, 잠정).
/// </summary>
public sealed class MailGcBatchService : PeriodicBatchService
{
    /// <summary>보관 기간(발급 후 7일, mail 기획서 6.5 확정) — 이 시간이 지난 메일이 삭제 대상이다.</summary>
    private const long RetentionSeconds = 7L * 24 * 60 * 60;

    private const int DefaultIntervalSeconds = 3600;
    private const int DefaultBatchSize = 500;

    private readonly int _intervalSeconds;
    private readonly int _batchSize;
    private readonly ILogger<MailGcBatchService> _logger;

    /// <summary>설정에서 실행 주기·1회 처리 상한을 읽는다(없거나 0 이하이면 기본값).</summary>
    public MailGcBatchService(
        IServiceScopeFactory scopeFactory, IBatchLock batchLock, IConfiguration configuration,
        ILogger<MailGcBatchService> logger)
        : base(scopeFactory, batchLock, logger)
    {
        var interval = configuration.GetValue("MailGcBatch:IntervalSeconds", DefaultIntervalSeconds);
        var batchSize = configuration.GetValue("MailGcBatch:BatchSize", DefaultBatchSize);
        _intervalSeconds = interval > 0 ? interval : DefaultIntervalSeconds;
        _batchSize = batchSize > 0 ? batchSize : DefaultBatchSize;
        _logger = logger;
    }

    protected override TimeSpan Interval => TimeSpan.FromSeconds(_intervalSeconds);

    protected override string BatchName => "메일 GC 배치";

    protected override string BatchKey => "mail-gc";

    /// <summary>
    /// 1주기 작업: 보관 기한(now − 7일) 이전에 발급된 메일 중 <b>미수령 무기한 메일을 제외</b>하고
    /// 상한(BatchSize)까지 삭제한 뒤 요약을 남긴다. 대상 0건이면 로그를 남기지 않는다(소음 방지).
    /// </summary>
    protected override async Task RunCycleAsync(IServiceScope scope, CancellationToken stoppingToken)
    {
        var mailRepository = scope.ServiceProvider.GetRequiredService<IMailRepository>();

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var deleted = await mailRepository.DeleteRetentionExpiredAsync(now - RetentionSeconds, _batchSize);

        if (deleted > 0)
        {
            _logger.ZLogInformation($"메일 GC 배치: 삭제 {deleted:@Deleted}건 (보관 7일 경과 대상, 1회 상한 {_batchSize:@BatchSize}건)");
        }
    }
}

