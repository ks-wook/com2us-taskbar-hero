using CloudStructures;
using GameServer.MasterData;
using GameServer.Repositories;
using GameServer.Services;

namespace GameServer.Batch;

/// <summary>
/// 거래소 만료 배치(trade 기획서 7.6). 판매 기간(3일)이 지난 등록(`status=1 AND expires_at &lt; now`)을 자동 취소하고,
/// 에스크로 아이템을 판매자에게 <b>메일로 반송</b>한다(템플릿 202). 판매자가 오프라인이거나 인벤토리가 가득해도
/// 안전하게 되돌리기 위해 수동 취소와 달리 메일을 쓴다. 골드 이동은 없다.
/// 등록 1건 = 락 1개 + 트랜잭션 1개로 처리하며, 구매·취소와 같은 락 키를 써서 같은 등록을 닫는 경로를 직렬화한다.
/// 설정: appsettings "TradeExpireBatch" 섹션(IntervalSeconds 기본 60 · BatchSize 기본 200).
/// </summary>
public sealed class TradeExpireBatchService : PeriodicBatchService
{
    private const int DefaultIntervalSeconds = 60;
    private const int DefaultBatchSize = 200;

    /// <summary>만료 반송 메일 템플릿(mail_master 202, {0} = 아이템 표시값).</summary>
    private const int ReturnMailTemplateCode = 202;

    /// <summary>메일 첨부 reward_type — 1:골드 2:아이템 3:재료. 반송 아이템은 마스터 item_type으로 결정한다.</summary>
    private const int RewardTypeItem = 2;
    private const int RewardTypeMaterial = 3;
    private const int ItemTypeMaterial = 2;

    private readonly int _intervalSeconds;
    private readonly int _batchSize;
    private readonly TradeCache _cache;
    private readonly MasterDataProvider _masterData;
    private readonly ILogger<TradeExpireBatchService> _logger;

    /// <summary>설정에서 실행 주기·1회 처리 상한을 읽고(없거나 0 이하이면 기본값), 캐시·마스터 데이터를 주입받는다.</summary>
    public TradeExpireBatchService(
        IServiceScopeFactory scopeFactory, RedisConnection redis, IConfiguration configuration,
        TradeCache cache, MasterDataProvider masterData, ILogger<TradeExpireBatchService> logger)
        : base(scopeFactory, redis, logger)
    {
        var interval = configuration.GetValue("TradeExpireBatch:IntervalSeconds", DefaultIntervalSeconds);
        var batchSize = configuration.GetValue("TradeExpireBatch:BatchSize", DefaultBatchSize);
        _intervalSeconds = interval > 0 ? interval : DefaultIntervalSeconds;
        _batchSize = batchSize > 0 ? batchSize : DefaultBatchSize;
        _cache = cache;
        _masterData = masterData;
        _logger = logger;
    }

    protected override TimeSpan Interval => TimeSpan.FromSeconds(_intervalSeconds);

    protected override string BatchName => "거래소 만료 배치";

    protected override string BatchKey => "trade-expire";

    /// <summary>
    /// 1주기 작업(trade 기획서 7.6.2): 만료 대상을 상한까지 조회해 건별로 락 → 조건부 갱신 선점 → 반송 메일 발급 →
    /// 캐시 제거를 수행한다. 건별 예외는 해당 건만 실패로 세고 다음 건을 계속 처리한다(주기 전체를 중단하지 않는다).
    /// 대상 0건이면 로그를 남기지 않는다(소음 방지).
    /// </summary>
    protected override async Task RunCycleAsync(IServiceScope scope, CancellationToken stoppingToken)
    {
        if (!_masterData.IsLoaded)
        {
            return; // 마스터 미적재면 반송 메일 문구를 만들 수 없다 — 다음 주기에 재시도.
        }

        var template = _masterData.GetMailTemplate(ReturnMailTemplateCode);
        if (template is null)
        {
            _logger.LogError(
                "거래소 만료 반송 메일 템플릿 미정의: templateCode {TemplateCode} — mail_master 확인 필요",
                ReturnMailTemplateCode);
            return;
        }

        var tradeRepository = scope.ServiceProvider.GetRequiredService<ITradeRepository>();

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var targets = await tradeRepository.GetExpiredListingIdsAsync(now, _batchSize);
        if (targets.Count == 0)
        {
            return;
        }

        var processed = 0;
        var skipped = 0;
        var failed = 0;

        foreach (var listingId in targets)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                break; // 종료 요청 — 처리 중인 건까지만 마무리하고 루프 종료
            }

            var handle = await _cache.AcquireLockAsync(listingId);
            if (!handle.Acquired && !handle.Degraded)
            {
                skipped++; // 구매·취소가 처리 중 — 다음 주기가 자연 재시도
                continue;
            }

            try
            {
                var expired = await tradeRepository.ApplyExpireAsync(
                    listingId,
                    listing => MailComposer.Compose(
                        template, ItemLabel(listing.ItemCode), now,
                        new[]
                        {
                            new MailAttachment(
                                RewardTypeFor(listing.ItemCode), listing.ItemCode,
                                listing.Quantity, listing.EnhanceLevel),
                        }),
                    now);

                if (expired is null)
                {
                    skipped++; // 그 사이 구매·취소로 이미 닫힘
                    continue;
                }

                await _cache.RemoveAsync(expired);
                processed++;
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogError(ex, "거래소 만료 처리 실패(listingId {ListingId}) — 다음 주기에 재시도합니다.", listingId);
            }
            finally
            {
                await _cache.ReleaseLockAsync(listingId, handle);
            }
        }

        _logger.LogInformation(
            "거래소 만료 배치: 처리 {Processed}건, 스킵 {Skipped}건, 실패 {Failed}건 (1회 상한 {BatchSize}건)",
            processed, skipped, failed, _batchSize);
    }

    /// <summary>반송 메일 문구(`{0}`)에 넣을 아이템 이름. 마스터에 없으면 코드를 문자열로 폴백한다.</summary>
    private string ItemLabel(int itemCode)
    {
        var name = _masterData.GetItem(itemCode)?.Name;
        return string.IsNullOrEmpty(name) ? itemCode.ToString() : name!;
    }

    /// <summary>반송 아이템의 메일 첨부 종류. 재료(item_type=2)면 3(재료), 그 외(장비)는 2(아이템).</summary>
    private int RewardTypeFor(int itemCode)
        => _masterData.GetItem(itemCode)?.ItemType == ItemTypeMaterial ? RewardTypeMaterial : RewardTypeItem;
}
