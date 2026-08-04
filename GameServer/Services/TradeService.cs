using GameServer.MasterData;
using GameServer.Repositories;
using TaskbarHero.Common;
using TaskbarHero.Common.Dto;
using ZLogger;

namespace GameServer.Services;

public interface ITradeService
{
    Task<SaveResult> ListAsync(long userId, int itemCode, bool mine, int page, int pageSize);
    Task<SaveResult> RegisterAsync(long userId, long itemId, long price);
    Task<SaveResult> BuyAsync(long userId, long listingId);
    Task<SaveResult> CancelAsync(long userId, long listingId);
}

/// <summary>
/// 거래소 처리(trade 기획서 §5·§6). 목록 조회는 Redis 캐시 우선·MySQL 폴백이고, 상태를 바꾸는 구매·취소는
/// Redis 락으로 1차 차단한 뒤 MySQL 조건부 갱신으로 최종 직렬화한다(락은 혼잡 제어, 조건부 갱신은 정합성 보증).
/// 판매 대금·만료 반송은 메일로 지급한다 — 계정 반영은 우편함 수령 시.
/// </summary>
public sealed class TradeService : ITradeService
{
    /// <summary>거래 수수료 20% — 판매자는 판매가의 80%를 받는다(trade 기획서 §4 확정).</summary>
    private const double SellerShare = 0.8;

    /// <summary>계정당 동시 등록(판매중) 한도.</summary>
    private const int ListingLimit = 10;

    /// <summary>등록 유효기간 3일.</summary>
    private const long ListingDurationSeconds = 3 * 86_400;

    /// <summary>목록 페이지 크기 기본·상한(§7.2 — 과대 응답 방어).</summary>
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 100;

    /// <summary>판매 대금 메일 템플릿(mail_master 201, {0} = 아이템 표시값) — 판매자 수령.</summary>
    private const int SettlementMailTemplateCode = 201;

    /// <summary>구매 아이템 메일 템플릿(mail_master 203, {0} = 아이템 표시값) — 구매자 수령.</summary>
    private const int PurchaseMailTemplateCode = 203;

    /// <summary>메일 첨부 종류 — 1:골드 2:아이템 3:재료. 구매 아이템은 마스터 item_type으로 가른다.</summary>
    private const int RewardTypeGold = 1;
    private const int RewardTypeItem = 2;
    private const int RewardTypeMaterial = 3;
    private const int ItemTypeMaterial = 2;

    /// <summary>골드 재화 코드(player_item 재화 행 item_code).</summary>
    private const int GoldItemCode = 1;

    private readonly ITradeRepository _tradeRepository;
    private readonly TradeCache _cache;
    private readonly MasterDataProvider _masterData;
    private readonly ILogger<TradeService> _logger;

    /// <summary>의존성(거래 리포지토리·Redis 목록 캐시·마스터 데이터·가방 조회 캐시·로거)을 주입받는다.</summary>
    public TradeService(
        ITradeRepository tradeRepository, TradeCache cache, MasterDataProvider masterData,
        ILogger<TradeService> logger)
    {
        _tradeRepository = tradeRepository;
        _cache = cache;
        _masterData = masterData;
        _logger = logger;
    }

    /// <summary>
    /// 판매중 등록 목록을 조회한다(5.1). pageSize는 서버가 상한을 강제하며, 조회는 상태를 바꾸지 않는다.
    /// <para><paramref name="mine"/>=false(기본): <b>요청자 본인 등록을 제외</b>한 구매 대상 목록.
    /// true: <b>본인 등록만</b> 조회한다(판매 취소 화면용 — 제외 모드에서는 자기 등록의 listingId를 알 수 없다).</para>
    /// <para>목록 캐시의 색인은 항상 해당 itemCode의 <b>완전 집합</b>이고 스냅샷에 판매자가 들어 있으므로,
    /// 캐시가 적재돼 있으면 <b>뷰어별 필터와 페이징을 메모리에서 적용</b>해 DB를 타지 않고 응답한다(mine 모드 포함).
    /// 캐시가 비어 있을 때만 MySQL에서 <b>전량을 읽어 캐시를 채우고</b> 같은 방식으로 응답한다 —
    /// 이 프로젝트 규모(동시 등록 수가 <see cref="MaxPageSize"/>를 넘지 않는다)에서는 항상 캐시를 쓰는 것이 맞다.</para>
    /// </summary>
    public async Task<SaveResult> ListAsync(long userId, int itemCode, bool mine, int page, int pageSize)
    {
        var normalizedItem = Math.Max(0, itemCode);
        var normalizedPage = Math.Max(0, page);
        var normalizedSize = pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);

        // 1) 캐시 적중이면 DB를 아예 타지 않는다. 완전 집합이라 메모리 필터·페이징이 정확하다.
        var cached = await _cache.TryGetAllAsync(normalizedItem);
        if (cached is not null)
        {
            return PageFromCompleteSet(cached, userId, mine, normalizedPage, normalizedSize);
        }

        // 2) 미적재·Redis 장애 → MySQL에서 전량(뷰어 필터·페이징 없음)을 읽어 캐시를 채우고 응답한다.
        //    상한을 걸어 읽지 않는 이유: 잘라 읽으면 부분 집합을 완전 집합처럼 캐시해 나머지 등록을 영구히 가린다.
        var all = await _tradeRepository.GetActiveListingsAsync(normalizedItem);
        await _cache.FillAsync(normalizedItem, all, NowUnix());

        // 규모 가정(등록 전량이 한 페이지 상한 이내)이 깨지면 응답은 계속 정확하지만 메모리·Redis 사용량이 커진다.
        // 조용히 넘기지 않고 남겨서 페이징 방식을 다시 검토할 신호로 쓴다.
        if (all.Count > MaxPageSize)
        {
            _logger.ZLogWarning($"거래소 판매중 등록이 페이지 상한을 넘었습니다: itemCode {normalizedItem:@ItemCode}, 등록 {all.Count:@ListingCount}건, 상한 {MaxPageSize:@MaxPageSize} — 전량 캐시 방식 재검토 필요");
        }

        return PageFromCompleteSet(all, userId, mine, normalizedPage, normalizedSize);
    }

    /// <summary>
    /// 인벤토리 아이템을 거래소에 등록한다(5.2). 한도·소유·장착·판매 가능 여부·가격 범위 검증과
    /// 에스크로 이동(인벤토리 제거 + 등록 생성)을 리포지토리 트랜잭션으로 원자 적용하고, 커밋 후 캐시에 추가한다.
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

        var now = NowUnix();
        var outcome = await _tradeRepository.ApplyRegisterAsync(
            userId, itemId, price, LookupItem, ListingLimit, now, now + ListingDurationSeconds);

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
        await _cache.AddAsync(listing, now); // 커밋 이후에만 캐시를 고친다(§7.3)

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
    /// 커밋 후 캐시에서 등록을 제거한다.
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

        var settlementTemplate = _masterData.GetMailTemplate(SettlementMailTemplateCode);
        var purchaseTemplate = _masterData.GetMailTemplate(PurchaseMailTemplateCode);
        if (settlementTemplate is null || purchaseTemplate is null)
        {
            _logger.ZLogError($"거래소 메일 템플릿 미정의: 대금 {settlementTemplate is not null:@HasSettlement}, 구매 아이템 {purchaseTemplate is not null:@HasPurchase} — mail_master 확인 필요");
            return new SaveResult(ErrorCode.MasterDataNotLoaded, string.Empty, null);
        }

        var now = NowUnix();
        var outcome = await _tradeRepository.ApplyBuyAsync(
            userId, listingId,
            listing => MailComposer.Compose(
                purchaseTemplate, ItemLabel(listing.ItemCode), now,
                new[]
                {
                    new MailAttachment(
                        RewardTypeFor(listing.ItemCode), listing.ItemCode,
                        listing.Quantity, listing.EnhanceLevel),
                }),
            listing => MailComposer.Compose(
                settlementTemplate, ItemLabel(listing.ItemCode), now,
                new[] { new MailAttachment(RewardTypeGold, 0, SettlementAmount(listing.Price)) }),
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
        await _cache.RemoveAsync(bought);

        _logger.ZLogInformation($"거래소 구매: buyerUserId {userId:@BuyerUserId}, listingId {bought.ListingId:@ListingId}, price {bought.Price:@Price}, 정산액 {SettlementAmount(bought.Price):@Settlement}, 아이템 메일 {outcome.ItemMailId:@MailId}");

        var data = new TradeBuyResultData
        {
            listingId = bought.ListingId,
            cost = new CurrencyDto { currencyType = GoldItemCode, amount = bought.Price },
            mailId = outcome.ItemMailId,
        };
        data.gained.items.Add(new TradeItemDto
        {
            itemCode = bought.ItemCode,
            enhanceLevel = bought.EnhanceLevel,
            quantity = bought.Quantity,
        });
        data.balance.Add(new CurrencyDto { currencyType = GoldItemCode, amount = outcome.GoldBalance });
        return new SaveResult(ErrorCode.Success, "Purchased", data);
    }

    /// <summary>
    /// 판매 중인 본인 등록을 취소한다(5.4). 조건부 갱신으로 등록을 선점한 뒤 아이템을 인벤토리로 복원한다.
    /// 커밋 후 캐시에서 등록을 제거한다.
    /// <para>구매·만료 배치와 같은 등록을 동시에 닫으려 해도 <b>MySQL 행 잠금</b>이 직렬화하므로 별도 락은 쓰지
    /// 않는다 — 먼저 닫은 쪽만 성공하고 뒤에 온 쪽은 TradeAlreadyClosed가 된다(§7.4).</para>
    /// </summary>
    public async Task<SaveResult> CancelAsync(long userId, long listingId)
    {
        if (listingId <= 0)
        {
            return new SaveResult(ErrorCode.InvalidSaveData, string.Empty, null);
        }

        var outcome = await _tradeRepository.ApplyCancelAsync(userId, listingId, LookupItem, NowUnix());

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
        await _cache.RemoveAsync(listing);

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
    public static long SettlementAmount(long price) => (long)Math.Floor(price * SellerShare);

    /// <summary>구매 아이템의 메일 첨부 종류. 재료(item_type=2)면 3(재료), 그 외(장비)는 2(아이템).</summary>
    private int RewardTypeFor(int itemCode)
        => _masterData.GetItem(itemCode)?.ItemType == ItemTypeMaterial ? RewardTypeMaterial : RewardTypeItem;

    /// <summary>아이템 코드 → 거래 검증용 마스터 정보(없으면 null → 판매 불가 처리).</summary>
    private TradeItemInfo? LookupItem(int itemCode)
    {
        var item = _masterData.GetItem(itemCode);
        return item is null
            ? null
            : new TradeItemInfo(item.ItemType, item.StackMax, item.Sellable, item.BasePrice);
    }

    /// <summary>메일 문구(`{0}`)에 넣을 아이템 이름. 마스터에 없으면 코드를 문자열로 폴백한다.</summary>
    private string ItemLabel(int itemCode)
    {
        var name = _masterData.GetItem(itemCode)?.Name;
        return string.IsNullOrEmpty(name) ? itemCode.ToString() : name!;
    }

    /// <summary>
    /// 해당 itemCode의 <b>완전 집합</b>에 뷰어 필터와 페이징을 메모리에서 적용해 목록 응답을 만든다.
    /// mine=false면 본인 등록을 제외하고, true면 본인 등록만 남긴다. 정렬은 MySQL 경로와 동일하게
    /// 가격 오름차순 → listingId 순으로 고정해 페이지 경계가 흔들리지 않게 한다.
    /// <para>부분 집합에 쓰면 페이징이 어긋나므로 <b>완전 집합에만</b> 사용한다(캐시 적중 결과 또는 전량 조회 결과).</para>
    /// </summary>
    private static SaveResult PageFromCompleteSet(
        IReadOnlyList<TradeListingSnapshot> completeSet, long userId, bool mine, int page, int pageSize)
    {
        var filtered = completeSet
            .Where(l => mine ? l.SellerUserId == userId : l.SellerUserId != userId)
            .OrderBy(l => l.Price).ThenBy(l => l.ListingId)
            .ToList();

        var start = (long)page * pageSize;
        if (start >= filtered.Count)
        {
            return Listed(Array.Empty<TradeListingSnapshot>(), page, pageSize, false);
        }

        var pageItems = filtered.Skip((int)start).Take(pageSize).ToList();
        return Listed(pageItems, page, pageSize, start + pageItems.Count < filtered.Count);
    }

    /// <summary>목록 응답 조립(캐시·MySQL 경로 공용).</summary>
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

    /// <summary>현재 시각(Unix ts, 초). 거래·메일과 동일 기준.</summary>
    private static long NowUnix() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
