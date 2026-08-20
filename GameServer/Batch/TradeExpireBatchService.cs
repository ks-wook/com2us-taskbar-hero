using GameServer.MasterData;
using GameServer.Repositories.GameDb;
using GameServer.Repositories.GameDb.Interfaces;
using GameServer.Services;
using ZLogger;
using GameServer.Repositories.MemoryDb.Interfaces;
using GameServer.Repositories.MasterDb;
using GameServer.Models;

namespace GameServer.Batch;

/// <summary>
/// 거래소 만료 배치(trade 기획서 7.6). 판매 기간(3일)이 지난 등록(`status=1 AND expires_at &lt; now`)을 자동 취소하고,
/// 에스크로 아이템을 판매자에게 <b>메일로 반송</b>한다(템플릿 202). 판매자가 오프라인이거나 인벤토리가 가득해도
/// 안전하게 되돌리기 위해 수동 취소와 달리 메일을 쓴다. 골드 이동은 없다.
/// 등록 1건 = 트랜잭션 1개로 처리하며, 구매·취소와의 충돌은 <b>조건부 갱신</b>(status=1 AND expires_at &lt; now일 때만
/// 전이)이 직렬화한다 — 그 사이 구매·취소로 닫힌 등록은 0행이 반영되어 스킵된다(별도 락 없음, §7.4).
/// <para><b>이 배치는 만료를 "판정"하지 않는다.</b> 만료 판정은 읽기 경로(목록·단건 조회·구매·등록 한도)가
/// <c>expires_at &gt; now</c>로 직접 하므로, 만료된 매물은 배치를 기다리지 않고 즉시 목록에서 빠지고 구매도 거부된다.
/// 배치가 남아서 하는 일은 <b>에스크로 아이템 반송과 status 정리</b>뿐이라 주기가 판매 기간(3일)의 정확도에
/// 영향을 주지 않는다(§7.6). 다만 <b>주기가 곧 판매자가 아이템을 되돌려받기까지의 지연 상한</b>이므로,
/// 3일을 기다린 판매자를 더 기다리게 하지 않도록 <b>1시간</b>으로 잡는다 — 대상 조회가
/// <c>idx_trade_expire</c>를 커버링으로 타고 0건이면 로그도 남기지 않아 빈 주기 비용이 사실상 없다.</para>
/// 설정: appsettings "TradeExpireBatch" 섹션(IntervalSeconds 기본 3600=1시간 · BatchSize 기본 1000).
/// </summary>
public sealed class TradeExpireBatchService : PeriodicBatchService
{
    /// <summary>기본 실행 주기 1시간. 만료 효력은 읽기 경로가 즉시 내므로 이 주기는 <b>반송 지연 상한</b>일 뿐이다.</summary>
    private const int DefaultIntervalSeconds = 60 * 60;

    /// <summary>
    /// 1주기 처리 상한. 주기(1시간)보다 넉넉히 잡아, 서버가 한동안 내려가 있다 올라왔을 때 밀린 만료 물량을
    /// 한 주기에 소화하게 한다. 초과분은 다음 주기로 이월된다.
    /// </summary>
    private const int DefaultBatchSize = 1000;

    /// <summary>만료 반송 메일 템플릿(mail_master 202, {0} = 아이템 표시값).</summary>
    private const int ReturnMailTemplateCode = 202;

    /// <summary>메일 첨부 reward_type — 1:골드 2:아이템 3:재료. 반송 아이템은 마스터 item_type으로 결정한다.</summary>
    private const int RewardTypeItem = 2;
    private const int RewardTypeMaterial = 3;
    private const int ItemTypeMaterial = 2;

    private readonly int _intervalSeconds;
    private readonly int _batchSize;
    private readonly MasterDbProvider _masterData;
    private readonly ILogger<TradeExpireBatchService> _logger;

    /// <summary>설정에서 실행 주기·1회 처리 상한을 읽고(없거나 0 이하이면 기본값), 마스터 데이터를 주입받는다.</summary>
    public TradeExpireBatchService(
        IServiceScopeFactory scopeFactory, IBatchLock batchLock, IConfiguration configuration,
        MasterDbProvider masterData, ILogger<TradeExpireBatchService> logger)
        : base(scopeFactory, batchLock, logger)
    {
        var interval = configuration.GetValue("TradeExpireBatch:IntervalSeconds", DefaultIntervalSeconds);
        var batchSize = configuration.GetValue("TradeExpireBatch:BatchSize", DefaultBatchSize);
        _intervalSeconds = interval > 0 ? interval : DefaultIntervalSeconds;
        _batchSize = batchSize > 0 ? batchSize : DefaultBatchSize;
        _masterData = masterData;
        _logger = logger;
    }

    protected override TimeSpan Interval => TimeSpan.FromSeconds(_intervalSeconds);

    protected override string BatchName => "거래소 만료 배치";

    protected override string BatchKey => "trade-expire";

    /// <summary>
    /// 1주기 작업(trade 기획서 7.6.2): 만료 대상을 상한까지 조회해 건별로 조건부 갱신 선점 → 반송 메일 발급 →
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
            _logger.ZLogError($"거래소 만료 반송 메일 템플릿 미정의: templateCode {ReturnMailTemplateCode:@TemplateCode} — mail_master 확인 필요");
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
                    skipped++; // 조건부 갱신 0행 — 그 사이 구매·취소로 이미 닫힘
                    continue;
                }

                processed++;
            }
            catch (Exception ex)
            {
                failed++;
                _logger.ZLogError(ex, $"거래소 만료 처리 실패(listingId {listingId:@ListingId}) — 다음 주기에 재시도합니다.");
            }
        }

        _logger.ZLogInformation($"거래소 만료 배치: 처리 {processed:@Processed}건, 스킵 {skipped:@Skipped}건, 실패 {failed:@Failed}건 (1회 상한 {_batchSize:@BatchSize}건)");
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

