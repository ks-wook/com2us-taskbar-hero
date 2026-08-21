using GameServer.MasterData;
using GameServer.Repositories.GameDb;
using GameServer.Repositories.GameDb.Interfaces;
using TaskbarHero.Common;
using TaskbarHero.Common.Dto;
using ZLogger;
using GameServer.Repositories.MasterDb;
using GameServer.Models;
using GameServer.Services.Interfaces;
using GameServer.Util;

namespace GameServer.Services;

/// <summary>
/// 거래소 처리(trade 기획서 §5·§6). <b>Redis를 쓰지 않는다</b> — 목록 조회는 전용 색인을 타는 MySQL 직접
/// 조회이고(§7.3), 등록·구매·취소·만료의 직렬화는 전부 MySQL 행 잠금이 담당한다(§7.4 — 구매·취소는
/// 조건부 갱신, 같은 아이템 중복 등록은 에스크로 DELETE).
/// 판매 대금·만료 반송은 메일로 지급한다 — 계정 반영은 우편함 수령 시.
/// </summary>
public sealed class TradeService : ITradeService
{
    private readonly ITradeRepository _tradeRepository;
    private readonly MasterDbProvider _masterData;
    private readonly ILogger<TradeService> _logger;

    /// <summary>의존성(거래 리포지토리·마스터 데이터·로거)을 주입받는다.</summary>
    public TradeService(
        ITradeRepository tradeRepository, MasterDbProvider masterData,
        ILogger<TradeService> logger)
    {
        _tradeRepository = tradeRepository;
        _masterData = masterData;
        _logger = logger;
    }

    /// <summary>
    /// 판매중 등록 목록을 조회한다(5.1). pageSize는 서버가 상한을 강제하며, 조회는 상태를 바꾸지 않는다.
    /// <para><paramref name="mine"/>=false(기본): <b>요청자 본인 등록을 제외</b>한 구매 대상 목록.
    /// true: <b>본인 등록만</b> 조회한다(판매 취소 화면용 — 제외 모드에서는 자기 등록의 listingId를 알 수 없다).</para>
    /// <para><b>캐시를 두지 않고, 필요한 한 페이지만 읽는다(§7.3).</b> 뷰어 필터·정렬·페이징을 전부 쿼리에서
    /// 처리하므로(<c>WHERE → ORDER BY → LIMIT</c>) 비용이 전체 등록 수와 무관하게 페이지 크기에 비례하고,
    /// 본인 등록이 섞여도 페이지 건수가 줄지 않는다. 한 건 더 읽어(<c>pageSize + 1</c>) hasMore를 판정한다.</para>
    /// <para><b>만료 시각이 지난 등록은 목록에서 빠진다(§7.6).</b> 요청 시각을 쿼리에 넘겨 걸러내므로,
    /// 만료 배치(1시간 주기)가 아직 돌지 않았어도 만료된 매물이 보이지 않는다.</para>
    /// </summary>
    public async Task<SaveResult> ListAsync(long userId, int itemCode, bool mine, int page, int pageSize)
    {
        var normalizedItem = Math.Max(0, itemCode);
        var normalizedSize = pageSize <= 0
            ? Constants.Trade.DefaultPageSize
            : Math.Min(pageSize, Constants.Trade.MaxPageSize);
        // 깊은 페이지 방어: OFFSET 은 앞의 행을 세어 버리므로 상한을 둔다. 넘으면 빈 페이지로 응답한다.
        var normalizedPage = Math.Clamp(page, 0, Constants.Trade.MaxOffset / normalizedSize);

        var now = DateTimeUtil.NowUnixSeconds();
        var pageItems = await _tradeRepository.GetActiveListingPageAsync(
            normalizedItem, userId, mine, normalizedPage * normalizedSize, normalizedSize + 1, now);

        bool hasMore = pageItems.Count > normalizedSize;
        if (hasMore)
        {
            pageItems = pageItems.Take(normalizedSize).ToList();
        }

        return Listed(pageItems, normalizedPage, normalizedSize, hasMore);
    }

    /// <summary>
    /// 인벤토리 아이템을 거래소에 등록한다(5.2). 한도·소유·장착·판매 가능 여부·가격 범위 검증과
    /// 에스크로 이동(인벤토리 제거 + 등록 생성)을 리포지토리 트랜잭션으로 원자 적용한다.
    /// <para><b>애플리케이션 락을 쓰지 않는다(§7.4).</b> 같은 아이템을 두 번 등록하려는 경합은 에스크로
    /// <c>DELETE</c>의 행 잠금이 막고(뒤에 온 쪽은 0행 → <c>ItemNotFound</c>), 동시 등록 한도(10건)는
    /// best-effort로 둔다 — 응답을 기다리지 않고 <b>서로 다른 아이템</b>을 겹쳐 보내야만 초과가 생기며,
    /// 초과해도 자산 정합성에 영향이 없고 판매·취소·만료로 스스로 수렴한다.</para>
    /// </summary>
    public async Task<SaveResult> RegisterAsync(long userId, long itemId, long price)
    {
        if (!_masterData.IsLoaded)
        {
            return new SaveResult(ErrorCode.MasterDataNotLoaded, string.Empty, null);
        }

        if (itemId <= 0 || price <= 0)
        {
            return new SaveResult(ErrorCode.InvalidSaveData, string.Empty, null);
        }

        var now = DateTimeUtil.NowUnixSeconds();
        var outcome = await _tradeRepository.ApplyRegisterAsync(
            userId, itemId, price, Constants.Trade.ListingLimit, now, now + Constants.Trade.ListingDurationSeconds);

        switch (outcome.Status)
        {
            case TradeRegisterStatus.ItemNotFound:
                return new SaveResult(ErrorCode.ItemNotFound, string.Empty, null);
            case TradeRegisterStatus.ItemEquipped:
                return new SaveResult(ErrorCode.ItemEquipped, string.Empty, null);
            case TradeRegisterStatus.NotSellable:
                return new SaveResult(ErrorCode.TradeNotSellable, string.Empty, null);
            case TradeRegisterStatus.PriceOutOfRange:
                return new SaveResult(ErrorCode.TradePriceOutOfRange, string.Empty, null);
            case TradeRegisterStatus.ListingLimitExceeded:
                return new SaveResult(ErrorCode.TradeListingLimitExceeded, string.Empty, null);
        }

        var listing = outcome.Listing!;

        _logger.ZLogInformation($"거래소 등록: userId {userId:@UserId}, listingId {listing.ListingId:@ListingId}, itemCode {listing.ItemCode:@ItemCode}, price {listing.Price:@Price}");

        return new SaveResult(ErrorCode.Success, "Registered", new TradeRegisterResultData
        {
            listingId = listing.ListingId,
            itemCode = listing.ItemCode,
            enhanceLevel = listing.EnhanceLevel,
            quantity = listing.Quantity,
            price = listing.Price,
            inventoryDelta = outcome.Delta,
        });
    }

    /// <summary>
    /// 등록을 구매한다(5.3). 트랜잭션 안에서 <b>조건부 갱신</b>(status=1일 때만 전이)으로 등록을 선점한 뒤
    /// 골드 차감·<b>구매 아이템 메일 발급(구매자)</b>·판매 대금 메일 발급(판매자)을 원자 적용한다.
    /// <para>동시 구매 직렬화는 <b>MySQL 행 잠금만</b>으로 처리한다 — 같은 등록에 두 요청이 도달하면 UPDATE가
    /// 줄을 세우고, 뒤에 온 쪽은 조건이 어긋나 0행을 받아 TradeAlreadyClosed가 된다(§7.4). 별도 Redis 락은 없다.</para>
    /// <para>아이템은 인벤토리에 즉시 넣지 않고 <b>우편함으로 지급</b>한다 — 구매 시점에 인벤토리 용량을 보지 않으므로
    /// 가방이 가득해도 거래가 성립하고, 적재는 플레이어가 메일을 수령할 때 이뤄진다.</para>
    /// </summary>
    public async Task<SaveResult> BuyAsync(long userId, long listingId)
    {
        if (!_masterData.IsLoaded)
        {
            return new SaveResult(ErrorCode.MasterDataNotLoaded, string.Empty, null);
        }

        if (listingId <= 0)
        {
            return new SaveResult(ErrorCode.InvalidSaveData, string.Empty, null);
        }

        var settlementTemplate = _masterData.GetMailTemplate(Constants.MailTemplate.TradeSettlement);
        var purchaseTemplate = _masterData.GetMailTemplate(Constants.MailTemplate.TradePurchase);
        if (settlementTemplate is null || purchaseTemplate is null)
        {
            _logger.ZLogError($"거래소 메일 템플릿 미정의: 대금 {settlementTemplate is not null:@HasSettlement}, 구매 아이템 {purchaseTemplate is not null:@HasPurchase} — mail_master 확인 필요");
            return new SaveResult(ErrorCode.MasterDataNotLoaded, string.Empty, null);
        }

        var now = DateTimeUtil.NowUnixSeconds();
        var outcome = await _tradeRepository.ApplyBuyAsync(
            userId, listingId,
            listing => MailUtil.Compose(
                purchaseTemplate, ItemLabel(listing.ItemCode), now,
                new[]
                {
                    new MailAttachment(
                        RewardTypeFor(listing.ItemCode), listing.ItemCode,
                        listing.Quantity, listing.EnhanceLevel),
                }),
            listing => MailUtil.Compose(
                settlementTemplate, ItemLabel(listing.ItemCode), now,
                new[] { new MailAttachment(Constants.RewardType.Gold, 0, SettlementAmount(listing.Price)) }),
            now);

        switch (outcome.Status)
        {
            case TradeCloseStatus.ListingNotFound:
                return new SaveResult(ErrorCode.TradeListingNotFound, string.Empty, null);
            case TradeCloseStatus.AlreadyClosed:
                return new SaveResult(ErrorCode.TradeAlreadyClosed, string.Empty, null);
            case TradeCloseStatus.SelfPurchase:
                return new SaveResult(ErrorCode.TradeSelfPurchase, string.Empty, null);
            case TradeCloseStatus.InsufficientGold:
                return new SaveResult(ErrorCode.InsufficientCurrency, string.Empty, null);
        }

        var bought = outcome.Listing!;

        _logger.ZLogInformation($"거래소 구매: buyerUserId {userId:@BuyerUserId}, listingId {bought.ListingId:@ListingId}, price {bought.Price:@Price}, 정산액 {SettlementAmount(bought.Price):@Settlement}, 아이템 메일 {outcome.ItemMailId:@MailId}");

        var data = new TradeBuyResultData
        {
            listingId = bought.ListingId,
            cost = new CurrencyDto { currencyType = Constants.Currency.GoldItemCode, amount = bought.Price },
            mailId = outcome.ItemMailId,
        };
        data.gained.items.Add(new TradeItemDto
        {
            itemCode = bought.ItemCode,
            enhanceLevel = bought.EnhanceLevel,
            quantity = bought.Quantity,
        });
        data.balance.Add(new CurrencyDto { currencyType = Constants.Currency.GoldItemCode, amount = outcome.GoldBalance });
        return new SaveResult(ErrorCode.Success, "Purchased", data);
    }

    /// <summary>
    /// 판매 중인 본인 등록을 취소한다(5.4). 조건부 갱신으로 등록을 선점한 뒤 아이템을 인벤토리로 복원한다.
    /// <para>구매·만료 배치와 같은 등록을 동시에 닫으려 해도 <b>MySQL 행 잠금</b>이 직렬화하므로 별도 락은 쓰지
    /// 않는다 — 먼저 닫은 쪽만 성공하고 뒤에 온 쪽은 TradeAlreadyClosed가 된다(§7.4).</para>
    /// </summary>
    public async Task<SaveResult> CancelAsync(long userId, long listingId)
    {
        if (listingId <= 0)
        {
            return new SaveResult(ErrorCode.InvalidSaveData, string.Empty, null);
        }

        var outcome = await _tradeRepository.ApplyCancelAsync(userId, listingId, DateTimeUtil.NowUnixSeconds());

        switch (outcome.Status)
        {
            case TradeCloseStatus.ListingNotFound:
                return new SaveResult(ErrorCode.TradeListingNotFound, string.Empty, null);
            case TradeCloseStatus.NotOwner:
                return new SaveResult(ErrorCode.TradeNotOwner, string.Empty, null);
            case TradeCloseStatus.AlreadyClosed:
                return new SaveResult(ErrorCode.TradeAlreadyClosed, string.Empty, null);
            case TradeCloseStatus.InventoryFull:
                return new SaveResult(ErrorCode.InventoryFull, string.Empty, null);
        }

        var listing = outcome.Listing!;

        _logger.ZLogInformation($"거래소 취소: userId {userId:@UserId}, listingId {listing.ListingId:@ListingId}, itemCode {listing.ItemCode:@ItemCode}");

        return new SaveResult(ErrorCode.Success, "Cancelled", new TradeCancelResultData
        {
            listingId = listing.ListingId,
            restored = new TradeItemDto
            {
                itemCode = listing.ItemCode,
                enhanceLevel = listing.EnhanceLevel,
                quantity = listing.Quantity,
            },
            inventoryDelta = outcome.Delta,
        });
    }

    /// <summary>판매 대금(수수료 20% 차감 후 판매자 수령액). 소수점은 버린다.</summary>
    public static long SettlementAmount(long price) => (long)Math.Floor(price * Constants.Trade.SellerShare);

    /// <summary>구매 아이템의 메일 첨부 종류. 재료(item_type=2)면 3(재료), 그 외(장비)는 2(아이템).</summary>
    private int RewardTypeFor(int itemCode)
        => _masterData.GetItem(itemCode)?.ItemType == Constants.ItemType.Material
            ? Constants.RewardType.Material
            : Constants.RewardType.Item;

    /// <summary>메일 문구(`{0}`)에 넣을 아이템 이름. 마스터에 없으면 코드를 문자열로 폴백한다.</summary>
    private string ItemLabel(int itemCode)
    {
        var name = _masterData.GetItem(itemCode)?.Name;
        return string.IsNullOrEmpty(name) ? itemCode.ToString() : name!;
    }

    /// <summary>목록 응답 조립.</summary>
    private static SaveResult Listed(
        IReadOnlyList<TradeListingSnapshot> listings, int page, int pageSize, bool hasMore)
    {
        var data = new TradeListResultData { page = page, pageSize = pageSize, hasMore = hasMore };
        foreach (var listing in listings)
        {
            data.listings.Add(new TradeListingDto
            {
                listingId = listing.ListingId,
                itemCode = listing.ItemCode,
                enhanceLevel = listing.EnhanceLevel,
                quantity = listing.Quantity,
                price = listing.Price,
                sellerUserId = listing.SellerUserId,
                createdAt = listing.CreatedAt,
            });
        }

        return new SaveResult(ErrorCode.Success, "OK", data);
    }
}
