using System.Data.Common;
using GameServer.MasterData;
using GameServer.Models;
using GameServer.Repositories.GameDb.Interfaces;
using SqlKata.Execution;
using TaskbarHero.Common.Dto;

namespace GameServer.Repositories.GameDb;

/// <summary>
/// 거래소 등록 1건의 스냅샷(목록 응답·에스크로 반송 공용).
/// <para><c>ExpiresAt</c>은 판매 만료 시각(Unix ts)이며, 목록 캐시 스냅샷의 TTL 기준으로 쓴다 —
/// 등록이 만료되면 캐시 항목도 함께 사라지게 하기 위함이다(trade 기획서 §7.3).</para>
/// </summary>
public sealed record TradeListingSnapshot(
    long ListingId, long SellerUserId, int ItemCode, int EnhanceLevel, int Quantity, long Price,
    long CreatedAt, long ExpiresAt);

/// <summary>판매 등록 결과. 성공 시 등록된 스냅샷을 함께 돌려준다.</summary>
public sealed record TradeRegisterOutcome(TradeRegisterStatus Status, TradeListingSnapshot? Listing)
{
    /// <summary>가방 변경분(5.0). 커밋 전에 확정된 값이라 응답 조립·캐시 갱신에 추가 조회가 필요 없다.</summary>
    public InventoryDeltaDto Delta { get; init; } = new InventoryDeltaDto();

    /// <summary>
    /// 등록하려던 아이템의 마스터 코드·강화 단계. <b>가격 범위 거부에서도 채운다</b> — 그 거부를 이벤트 로그로
    /// 남기는데, "어떤 아이템에 얼마를 부르려 했나"가 곧 가격 제한 범위를 조정할 근거이기 때문이다(5.7).
    /// 등록이 성공하면 <see cref="Listing"/>이 같은 값을 담으므로 이 둘은 거부 경로 전용이다.
    /// </summary>
    public int AttemptedItemCode { get; init; }

    /// <inheritdoc cref="AttemptedItemCode"/>
    public int AttemptedEnhanceLevel { get; init; }

    public static TradeRegisterOutcome Fail(TradeRegisterStatus status) => new(status, null);
}

/// <summary>구매 결과. 성공 시 거래된 등록 스냅샷·차감 후 골드 잔액·구매자에게 발급된 아이템 메일 id를 돌려준다.</summary>
public sealed record TradeBuyOutcome(
    TradeCloseStatus Status, TradeListingSnapshot? Listing, long GoldBalance, long ItemMailId)
{
    /// <summary>
    /// 판매자에게 발급한 <b>판매 대금 메일</b> id. 구매 1건이 메일을 둘 발급하므로(구매자=아이템,
    /// 판매자=대금) 발급 이벤트도 둘 나가며, 그 둘을 가르는 값이다(5.8).
    /// </summary>
    public long SettlementMailId { get; init; }

    public static TradeBuyOutcome Fail(TradeCloseStatus status) => new(status, null, 0, 0);
}

/// <summary>
/// 만료 처리 결과. 만료된 등록 스냅샷과 <b>판매자에게 발급한 반송 메일 id</b>를 함께 돌려준다 —
/// 그 id가 이벤트 로그(<c>trade.close</c>)와 메일 원장을 잇는 축이다(5.7·5.8).
/// </summary>
public sealed record TradeExpireOutcome(TradeListingSnapshot Listing, long ReturnMailId);

/// <summary>판매 취소 결과. 성공 시 인벤토리로 복귀한 아이템 스냅샷을 돌려준다.</summary>
public sealed record TradeCancelOutcome(TradeCloseStatus Status, TradeListingSnapshot? Listing)
{
    /// <summary>가방 변경분(5.0). 커밋 전에 확정된 값이라 응답 조립·캐시 갱신에 추가 조회가 필요 없다.</summary>
    public InventoryDeltaDto Delta { get; init; } = new InventoryDeltaDto();

    public static TradeCancelOutcome Fail(TradeCloseStatus status) => new(status, null);
}

/// <summary>
/// 거래소 세이브 접근 계층(taskbar_hero_game). SqlKata 쿼리 빌더 + 제네릭 매핑만 사용한다(dynamic 금지).
/// 상태 전이(구매·취소·만료)는 모두 <b>조건부 갱신(status=1일 때만 전이)</b>으로 선점해 이중 판매를 차단한다
/// (trade 기획서 §7.2). 선점은 재화·아이템 이동보다 항상 앞에 둔다.
/// </summary>
public sealed class TradeRepository : GameDbBase, ITradeRepository
{
    private readonly IItemLookup _itemLookup;

    /// <summary>세이브 DB 커넥션 팩토리를 기반 클래스로 전달한다.</summary>
    public TradeRepository(GameDbFactory dbFactory, IItemLookup itemLookup) : base(dbFactory)
    {
        _itemLookup = itemLookup;
    }

    /// <summary>
    /// 판매중 등록 <b>한 페이지</b>를 가격 오름차순·listing_id 보조 정렬로 조회한다
    /// (<c>idx_trade_browse (status, item_code, price, listing_id)</c>·<c>idx_trade_price</c>가 커버).
    /// <para><b>뷰어 필터를 쿼리에 넣는다.</b> <paramref name="mine"/>=false면 <c>seller_user_id &lt;&gt; viewer</c>,
    /// true면 <c>= viewer</c>다. SQL의 처리 순서가 <c>WHERE → ORDER BY → LIMIT</c>이므로 <b>걸러낸 결과에서
    /// limit만큼 세어</b> 페이지 크기가 정확하다. 반대로 자른 뒤 애플리케이션에서 거르면(LIMIT 먼저 → 메모리 필터)
    /// 본인 등록이 섞인 페이지만 건수가 줄어든다.</para>
    /// <para><b>전량을 읽지 않는다.</b> 예전에는 캐시에 담을 완전 집합이 필요해 전량을 읽고 메모리에서
    /// 필터·페이징했는데, 캐시를 제거하면서 그 전제가 사라졌다(거래소 기획서 7.3).</para>
    /// <para><b>만료 시각이 지난 등록은 status가 아직 1이어도 제외한다</b>(<c>expires_at &gt; nowUnix</c>).
    /// 만료 판정을 배치 시점이 아니라 <b>읽는 시점</b>으로 두므로, 만료된 매물이 목록에 남아 있다가 구매 실패로
    /// 이어지는 구간이 없다 — 배치는 에스크로 반송·status 정리만 담당한다(거래소 기획서 7.6).
    /// 이 조건은 인덱스 <c>idx_trade_browse</c>의 선두 컬럼이 아니라 <b>잔여 술어</b>로 평가되지만, 걸러지는 행이
    /// 극소수(3일 지난 매물)라 페이지 조회 비용에 영향이 없다.</para>
    /// <para>호출측은 <c>limit</c>에 <b>페이지 크기 + 1</b>을 넘겨, 한 건 더 오는지로 <c>hasMore</c>를 판정한다.</para>
    /// </summary>
    public async Task<IReadOnlyList<TradeListingSnapshot>> GetActiveListingPageAsync(
        int itemCode, long viewerUserId, bool mine, int offset, int limit, long nowUnix)
    {
        using var db = Db();
        var query = db.Query("trade_listing")
            .Select("listing_id", "seller_user_id", "item_code", "enhance_level", "quantity", "price", "created_at", "expires_at")
            .Where("status", Constants.Trade.StatusOnSale).Where("expires_at", ">", nowUnix);
        if (itemCode > 0)
        {
            query = query.Where("item_code", itemCode);
        }

        // 뷰어 필터: 구매 대상 목록은 본인 등록 제외, 취소 화면은 본인 등록만.
        query = mine
            ? query.Where("seller_user_id", viewerUserId)
            : query.Where("seller_user_id", "<>", viewerUserId);

        var rows = await query
            .OrderBy("price").OrderBy("listing_id")
            .Offset(offset).Limit(limit)
            .GetAsync<TradeListingRow>();

        return rows
            .Select(r => new TradeListingSnapshot(
                r.ListingId, r.SellerUserId, r.ItemCode, r.EnhanceLevel, r.Quantity, r.Price,
                r.CreatedAt, r.ExpiresAt))
            .ToList();
    }

    /// <summary>
    /// 등록 1건을 조회해 스냅샷으로 돌려준다. <b>판매중(status=1)이고 만료 시각이 남은 등록만</b> 반환하므로,
    /// 목록 조회와 같은 기준으로 "지금 살 수 있는 매물"을 가리킨다(거래소 기획서 7.6).
    /// </summary>
    public async Task<TradeListingSnapshot?> GetListingAsync(long listingId, long nowUnix)
    {
        using var db = Db();
        var row = await db.Query("trade_listing")
            .Select("listing_id", "seller_user_id", "item_code", "enhance_level", "quantity", "price", "created_at", "expires_at")
            .Where("listing_id", listingId).Where("status", Constants.Trade.StatusOnSale).Where("expires_at", ">", nowUnix)
            .FirstOrDefaultAsync<TradeListingRow>();
        return row is null
            ? null
            : new TradeListingSnapshot(
                row.ListingId, row.SellerUserId, row.ItemCode, row.EnhanceLevel,
                row.Quantity, row.Price, row.CreatedAt, row.ExpiresAt);
    }

    /// <summary>
    /// 판매 등록을 단일 트랜잭션으로 적용한다(trade 기획서 §6.2). 검증 실패는 즉시 롤백 후 상태 코드로 반환한다.
    /// </summary>
    /// <remarks>
    /// 한 트랜잭션으로 묶는 작업(하나라도 실패하면 전부 롤백 — 아이템만 사라지고 등록이 없는 상태를 막는다):
    /// <para>1) trade_listing COUNT — 계정 동시 등록 한도 검사(idx_trade_seller)</para>
    /// <para>2) player_item SELECT — 소유·미장착 확인(장착 여부는 player_item_equipped 존재로 판정)</para>
    /// <para>3) 마스터 검증 — sellable=1 · 가격이 base_price ±20% 범위</para>
    /// <para>4) player_item DELETE — 에스크로 이동(스택형은 행 전체 수량)</para>
    /// <para>5) trade_listing INSERT — status=1, expires_at = now + 3일</para>
    /// </remarks>
    public async Task<TradeRegisterOutcome> ApplyRegisterAsync(
        long userId, long itemId, long price,
        int listingLimit, long nowUnix, long expiresAt)
        => await TransactionAsync<TradeRegisterOutcome>(async (db, transaction) =>
        {
            // 1) 동시 등록 한도. 만료 시각이 지난 등록은 배치가 아직 정리하지 않았어도 한도에서 제외한다
            //    (읽기 시점 만료 판정 — 그렇지 않으면 하루 1회 배치가 돌기 전까지 판매자의 등록 칸이 묶인다).
            var active = await db.Query("trade_listing")
                .Where("seller_user_id", userId).Where("status", Constants.Trade.StatusOnSale).Where("expires_at", ">", nowUnix)
                .CountAsync<int>(transaction: transaction);
            if (active >= listingLimit)
            {
                return TxResult<TradeRegisterOutcome>.Rollback(TradeRegisterOutcome.Fail(TradeRegisterStatus.ListingLimitExceeded));
            }

            // 2) 소유 확인(본인 아이템 행만). 재화 행(row_type=2)은 거래 대상이 아니다.
            var item = await db.Query("player_item")
                .Select("player_item_id", "item_code", "quantity", "enhance_level")
                .Where("player_item_id", itemId).Where("user_id", userId).Where("row_type", Constants.PlayerItemRow.Item)
                .FirstOrDefaultAsync<TradePlayerItemRow>(transaction);
            if (item is null)
            {
                return TxResult<TradeRegisterOutcome>.Rollback(TradeRegisterOutcome.Fail(TradeRegisterStatus.ItemNotFound));
            }

            // 장착 중이면 등록 불가(장착 원장에 행이 있으면 장착 상태).
            var equipped = await db.Query("player_item_equipped")
                .Select("player_item_id").Where("player_item_id", itemId)
                .FirstOrDefaultAsync<long?>(transaction);
            if (equipped is not null)
            {
                return TxResult<TradeRegisterOutcome>.Rollback(TradeRegisterOutcome.Fail(TradeRegisterStatus.ItemEquipped));
            }

            // 3) 마스터 검증: 판매 가능 여부 → 가격 범위.
            var info = _itemLookup.Attributes(item.ItemCode);
            if (info is null || info.Sellable != 1 || info.BasePrice <= 0)
            {
                return TxResult<TradeRegisterOutcome>.Rollback(TradeRegisterOutcome.Fail(TradeRegisterStatus.NotSellable));
            }

            if (!IsPriceInRange(price, info.BasePrice))
            {
                // 거부지만 시도한 아이템 맥락은 채워 돌려준다 — 이벤트 로그가 그대로 쓴다(5.7).
                return TxResult<TradeRegisterOutcome>.Rollback(
                    TradeRegisterOutcome.Fail(TradeRegisterStatus.PriceOutOfRange) with
                    {
                        AttemptedItemCode = item.ItemCode,
                        AttemptedEnhanceLevel = item.EnhanceLevel,
                    });
            }

            // 4) 에스크로 이동: 인벤토리 행 제거(스택형도 행 전체 — 부분 판매 없음).
            //    조건에 user_id를 함께 걸어 동시 요청이 같은 행을 두 번 등록하지 못하게 한다(0행이면 경합 패배).
            var removed = await db.Query("player_item")
                .Where("player_item_id", itemId).Where("user_id", userId)
                .DeleteAsync(transaction);
            if (removed == 0)
            {
                return TxResult<TradeRegisterOutcome>.Rollback(TradeRegisterOutcome.Fail(TradeRegisterStatus.ItemNotFound));
            }

            // 5) 등록 생성.
            var quantity = (int)Math.Max(1, item.Quantity);
            var listingId = await db.Query("trade_listing").InsertGetIdAsync<long>(new
            {
                seller_user_id = userId,
                item_code = item.ItemCode,
                enhance_level = item.EnhanceLevel,
                quantity,
                price,
                status = Constants.Trade.StatusOnSale,
                buyer_user_id = 0,
                created_at = nowUnix,
                expires_at = expiresAt,
                closed_at = 0,
            }, transaction);

            // 가방 변경분(5.0): 등록한 아이템 행은 에스크로로 옮겨져 인벤토리에서 사라진다.
            var delta = new InventoryDeltaDto();
            delta.removed.Add(itemId);

            return TxResult<TradeRegisterOutcome>.Commit(new TradeRegisterOutcome(
                TradeRegisterStatus.Ok,
                new TradeListingSnapshot(
                    listingId, userId, item.ItemCode, item.EnhanceLevel, quantity, price, nowUnix, expiresAt))
            {
                Delta = delta,
            });
        });

    /// <summary>
    /// 구매를 단일 트랜잭션으로 적용한다(trade 기획서 §6.1). 상태 전이를 재화 이동보다 먼저 확정해,
    /// 경합에서 진 요청이 헛일을 하지 않게 한다.
    /// </summary>
    /// <remarks>
    /// 한 트랜잭션으로 묶는 작업(하나라도 실패하면 전부 롤백):
    /// <para>1) trade_listing SELECT — 존재·판매중·자기 등록 여부 확인</para>
    /// <para>2) 골드 잔액 사전 확인(부족하면 선점 전에 거부해 등록을 헛되이 닫지 않는다)</para>
    /// <para>3) 조건부 갱신(status 1→2) — 동시 구매 중 한 명만 통과. 0행이면 이미 팔린 등록</para>
    /// <para>4) 구매자 골드 차감</para>
    /// <para>5) 구매 아이템 메일 발급(구매자, 강화 단계 보존) — 인벤토리에 직접 넣지 않는다</para>
    /// <para>6) 판매 대금 메일 발급(판매자, 수수료 차감액)</para>
    /// </remarks>
    public async Task<TradeBuyOutcome> ApplyBuyAsync(
        long buyerUserId, long listingId,
        Func<TradeListingSnapshot, MailDraft> composeItemMail,
        Func<TradeListingSnapshot, MailDraft> composeSettlementMail, long nowUnix)
        => await TransactionAsync<TradeBuyOutcome>(async (db, transaction) =>
        {
            // 1) 등록 확인.
            var row = await db.Query("trade_listing")
                .Select("listing_id", "seller_user_id", "item_code", "enhance_level", "quantity", "price",
                        "created_at", "expires_at", "status")
                .Where("listing_id", listingId)
                .FirstOrDefaultAsync<TradeListingStatusRow>(transaction);
            if (row is null)
            {
                return TxResult<TradeBuyOutcome>.Rollback(TradeBuyOutcome.Fail(TradeCloseStatus.ListingNotFound));
            }

            // 이미 닫힌 등록, 그리고 **만료 시각이 지난 등록**(배치가 아직 status를 4로 바꾸지 못한 상태)은 모두 거부한다.
            // 만료 판정을 배치 시점이 아니라 읽기 시점으로 두는 규칙이며(거래소 기획서 7.6), 판매 기간 3일이
            // 배치 주기(하루 1회)만큼 늘어나 보이는 문제를 없앤다. 사용자에게는 "이미 닫힌 등록"과 구분할 이유가
            // 없으므로 같은 상태 코드(→ TradeAlreadyClosed)로 응답한다.
            if (row.Status != Constants.Trade.StatusOnSale || row.ExpiresAt <= nowUnix)
            {
                return TxResult<TradeBuyOutcome>.Rollback(TradeBuyOutcome.Fail(TradeCloseStatus.AlreadyClosed));
            }

            if (row.SellerUserId == buyerUserId)
            {
                return TxResult<TradeBuyOutcome>.Rollback(TradeBuyOutcome.Fail(TradeCloseStatus.SelfPurchase));
            }

            // 2) 지불 능력·적재 여유 사전 확인. 실제 차감은 4)의 원자 갱신이 확정하며, 여기서 미리 보는 것은
            //    잔액이 모자란 요청이 3)에서 등록을 헛되이 닫지 않게 하려는 것이다.
            var gold = await LoadCurrencyBalanceAsync(
                db, transaction, buyerUserId, Constants.Currency.GoldItemCode);
            if (gold < row.Price)
            {
                // 거부지만 등록 스냅샷은 담아 돌려준다 — "이 가격에서 못 샀다"가 시세 신호라 로그로 남긴다(5.7).
                return TxResult<TradeBuyOutcome>.Rollback(new TradeBuyOutcome(
                    TradeCloseStatus.InsufficientGold,
                    new TradeListingSnapshot(
                        row.ListingId, row.SellerUserId, row.ItemCode, row.EnhanceLevel,
                        row.Quantity, row.Price, row.CreatedAt, row.ExpiresAt),
                    0, 0));
            }

            // 3) 선점(CAS): 판매중일 때만 판매완료로 전이.
            var claimed = await db.Query("trade_listing")
                .Where("listing_id", listingId).Where("status", Constants.Trade.StatusOnSale)
                .UpdateAsync(
                    new { status = Constants.Trade.StatusSold, buyer_user_id = buyerUserId, closed_at = nowUnix }, transaction);
            if (claimed == 0)
            {
                return TxResult<TradeBuyOutcome>.Rollback(TradeBuyOutcome.Fail(TradeCloseStatus.AlreadyClosed));
            }

            var snapshot = new TradeListingSnapshot(
                row.ListingId, row.SellerUserId, row.ItemCode, row.EnhanceLevel,
                row.Quantity, row.Price, row.CreatedAt, row.ExpiresAt);

            // 4) 골드 차감(잔액이 충분할 때만 깎는 원자 갱신).
            var balance = await TryDebitCurrencyAsync(
                db, transaction, buyerUserId, Constants.Currency.GoldItemCode, row.Price);
            if (balance is null)
            {
                // 2)와 이 시점 사이에 같은 계정의 다른 요청이 먼저 골드를 가져갔다. 전체 롤백이라 3)의 선점도 풀린다.
                return TxResult<TradeBuyOutcome>.Rollback(new TradeBuyOutcome(
                    TradeCloseStatus.InsufficientGold, snapshot, 0, 0));
            }

            // 5) 구매 아이템 메일 발급(구매자). 인벤토리에 직접 넣지 않으므로 이 시점에 용량을 보지 않는다
            //    — 적재는 우편함 수령 시 이뤄지고, 그때 부족하면 InventoryFull로 거부된다(메일 6.1).
            var itemMailId = await MailRepository.InsertMailAsync(
                db, transaction, buyerUserId, composeItemMail(snapshot), nowUnix);

            // 6) 판매 대금 메일 발급(판매자).
            var settlementMailId = await MailRepository.InsertMailAsync(
                db, transaction, row.SellerUserId, composeSettlementMail(snapshot), nowUnix);

            return TxResult<TradeBuyOutcome>.Commit(
                new TradeBuyOutcome(TradeCloseStatus.Ok, snapshot, balance.Value, itemMailId)
                {
                    SettlementMailId = settlementMailId,
                });
        });

    /// <summary>
    /// 판매 취소를 단일 트랜잭션으로 적용한다(trade 기획서 §6.2). 본인·판매중 확인 → 조건부 갱신 선점 →
    /// 아이템을 판매자 인벤토리에 복원한다. 복원 칸이 없으면 전체 롤백(InventoryFull).
    /// </summary>
    public async Task<TradeCancelOutcome> ApplyCancelAsync(
        long userId, long listingId, long nowUnix)
        => await TransactionAsync<TradeCancelOutcome>(async (db, transaction) =>
        {
            var row = await db.Query("trade_listing")
                .Select("listing_id", "seller_user_id", "item_code", "enhance_level", "quantity", "price",
                        "created_at", "expires_at", "status")
                .Where("listing_id", listingId)
                .FirstOrDefaultAsync<TradeListingStatusRow>(transaction);
            if (row is null)
            {
                return TxResult<TradeCancelOutcome>.Rollback(TradeCancelOutcome.Fail(TradeCloseStatus.ListingNotFound));
            }

            if (row.SellerUserId != userId)
            {
                return TxResult<TradeCancelOutcome>.Rollback(TradeCancelOutcome.Fail(TradeCloseStatus.NotOwner));
            }

            if (row.Status != Constants.Trade.StatusOnSale)
            {
                return TxResult<TradeCancelOutcome>.Rollback(TradeCancelOutcome.Fail(TradeCloseStatus.AlreadyClosed));
            }

            var claimed = await db.Query("trade_listing")
                .Where("listing_id", listingId).Where("status", Constants.Trade.StatusOnSale)
                .UpdateAsync(new { status = Constants.Trade.StatusCancelled, closed_at = nowUnix }, transaction);
            if (claimed == 0)
            {
                return TxResult<TradeCancelOutcome>.Rollback(TradeCancelOutcome.Fail(TradeCloseStatus.AlreadyClosed));
            }

            var snapshot = new TradeListingSnapshot(
                row.ListingId, row.SellerUserId, row.ItemCode, row.EnhanceLevel,
                row.Quantity, row.Price, row.CreatedAt, row.ExpiresAt);
            var delta = new InventoryDeltaDto();
            var stored = await StoreTradeItemAsync(db, transaction, userId, snapshot, nowUnix, delta);
            if (!stored)
            {
                // 거부지만 스냅샷은 담아 돌려준다 — 되돌릴 칸이 없어 취소가 막히는 빈도는 가방 용량 설계의 신호다(5.7).
                return TxResult<TradeCancelOutcome>.Rollback(
                    new TradeCancelOutcome(TradeCloseStatus.InventoryFull, snapshot));
            }

            return TxResult<TradeCancelOutcome>.Commit(new TradeCancelOutcome(TradeCloseStatus.Ok, snapshot) { Delta = delta });
        });

    /// <summary>만료 대상(판매중 + expires_at 경과)을 listing_id 오름차순으로 최대 limit건 조회한다(idx_trade_expire).</summary>
    public async Task<IReadOnlyList<long>> GetExpiredListingIdsAsync(long nowUnix, int limit)
    {
        using var db = Db();
        var ids = await db.Query("trade_listing")
            .Select("listing_id")
            .Where("status", Constants.Trade.StatusOnSale).Where("expires_at", "<", nowUnix)
            .OrderBy("listing_id").Limit(limit)
            .GetAsync<long>();
        return ids.ToList();
    }

    /// <summary>
    /// 만료 1건을 단일 트랜잭션으로 처리한다(trade 기획서 §7.6.2): 조건부 갱신으로 만료(status=4) 확정 →
    /// 스냅샷 확보 → 판매자에게 반송 메일 발급. 그 사이 구매·취소로 이미 닫혔으면 null(스킵).
    /// 만료 반송은 아이템만 되돌리며 골드 이동은 없다.
    /// </summary>
    public async Task<TradeExpireOutcome?> ApplyExpireAsync(
        long listingId, Func<TradeListingSnapshot, MailDraft> composeReturnMail, long nowUnix)
        => await TransactionAsync<TradeExpireOutcome?>(async (db, transaction) =>
        {
            // 1) 선점(CAS): 아직 판매중이고 만료가 지난 등록만 만료로 전이(수동 취소 3과 구분되는 4).
            var closed = await db.Query("trade_listing")
                .Where("listing_id", listingId).Where("status", Constants.Trade.StatusOnSale)
                .Where("expires_at", "<", nowUnix)
                .UpdateAsync(new { status = Constants.Trade.StatusExpired, closed_at = nowUnix }, transaction);
            if (closed == 0)
            {
                return TxResult<TradeExpireOutcome?>.Rollback(null);
            }

            // 2) 반송 스냅샷.
            var row = await db.Query("trade_listing")
                .Select("listing_id", "seller_user_id", "item_code", "enhance_level", "quantity", "price", "created_at", "expires_at")
                .Where("listing_id", listingId)
                .FirstOrDefaultAsync<TradeListingRow>(transaction);
            if (row is null)
            {
                return TxResult<TradeExpireOutcome?>.Rollback(null);
            }

            // 3) 반송 메일 발급(판매자). 인벤토리가 가득해도 안전하게 되돌리기 위해 메일을 쓴다.
            var snapshot = new TradeListingSnapshot(
                row.ListingId, row.SellerUserId, row.ItemCode, row.EnhanceLevel,
                row.Quantity, row.Price, row.CreatedAt, row.ExpiresAt);
            var returnMailId = await MailRepository.InsertMailAsync(
                db, transaction, snapshot.SellerUserId, composeReturnMail(snapshot), nowUnix);

            return TxResult<TradeExpireOutcome?>.Commit(new TradeExpireOutcome(snapshot, returnMailId));
        });

    // ── 내부 헬퍼 ──

    /// <summary>등록 가격이 기준가 ±20%(×0.8 ~ ×1.2) 범위인지. 경계값은 포함한다.</summary>
    private static bool IsPriceInRange(long price, long basePrice)
    {
        if (price <= 0)
        {
            return false;
        }

        // 정수 비교로 부동소수 오차를 피한다: price*10이 [basePrice*8, basePrice*12] 범위인지 본다.
        var scaled = price * 10;
        return scaled >= basePrice * 8 && scaled <= basePrice * 12;
    }

    /// <summary>
    /// 거래 아이템(에스크로 스냅샷)을 대상 계정 인벤토리에 적재한다. 메일 첨부와 달리 <b>강화 단계를 보존</b>한다.
    /// 재료(스택형)는 기존 스택의 여유부터 채우고 남으면 새 칸, 장비는 1개당 1행으로 새 칸에 넣는다.
    /// 빈 칸이 부족하면 false(호출측 롤백).
    /// </summary>
    private async Task<bool> StoreTradeItemAsync(
        QueryFactory db, DbTransaction tx, long userId, TradeListingSnapshot listing,
        long nowUnix, InventoryDeltaDto delta)
    {
        var info = _itemLookup.Attributes(listing.ItemCode);
        var itemType = info?.ItemType ?? 1;
        var stackMax = Math.Max(1, info?.StackMax ?? 1);

        var capacity = await InventorySlotAllocator.LoadCapacityAsync(db, tx, userId);
        var used = await InventorySlotAllocator.LoadUsedSlotsAsync(db, tx, userId);

        long remaining = listing.Quantity;

        // 재료(스택 가능): 기존 스택의 여유부터 채운다(새 칸 불필요). 강화 단계가 없는 종류다.
        if (itemType == Constants.ItemType.Material && stackMax > 1)
        {
            var stacks = await db.Query("player_item").Select("player_item_id", "quantity", "slot")
                .Where("user_id", userId).Where("row_type", Constants.PlayerItemRow.Item).Where("item_code", listing.ItemCode)
                .Where("quantity", "<", stackMax)
                .GetAsync<ItemIdQtySlotRow>(tx);

            foreach (var stack in stacks)
            {
                if (remaining <= 0)
                {
                    break;
                }

                var room = stackMax - stack.Quantity;
                var add = Math.Min(room, remaining);
                var merged = stack.Quantity + add;
                await db.Query("player_item").Where("player_item_id", stack.PlayerItemId)
                    .UpdateAsync(new { quantity = merged }, tx);
                delta.upserted.Add(new InventoryItemDto
                {
                    itemId = stack.PlayerItemId,
                    slot = stack.Slot ?? 0,
                    itemCode = listing.ItemCode,
                    quantity = merged,
                    enhanceLevel = listing.EnhanceLevel,
                });
                remaining -= add;
            }
        }

        var perRow = itemType == Constants.ItemType.Material ? stackMax : 1;
        while (remaining > 0)
        {
            if (!InventorySlotAllocator.TryFirstFree(used, capacity, out int slot))
            {
                return false;
            }

            var put = Math.Min(perRow, remaining);
            var newItemId = await db.Query("player_item").InsertGetIdAsync<long>(new
            {
                user_id = userId,
                row_type = Constants.PlayerItemRow.Item,
                item_code = listing.ItemCode,
                quantity = put,
                slot,
                enhance_level = listing.EnhanceLevel,
                acquired_at = nowUnix,
            }, tx);
            delta.upserted.Add(new InventoryItemDto
            {
                itemId = newItemId,
                slot = slot,
                itemCode = listing.ItemCode,
                quantity = put,
                enhanceLevel = listing.EnhanceLevel,
            });
            used.Add(slot);
            remaining -= put;
        }

        return true;
    }


    /// <summary>
    /// 재화 행(<c>player_item</c>, <c>row_type=2</c>)의 <c>player_item_id</c>를 찾는다(없으면 null).
    /// 잔액이 아니라 <b>행 번호</b>만 읽으므로 이 조회는 낡지 않는다 — 재화 행은 세이브를 만들 때 함께 만들어지고
    /// 이후 지워지지 않아 번호가 고정이다(<c>SaveRepository.CreatePlayerWithFirstCharacterAsync</c>).
    /// </summary>
    private static async Task<long?> FindCurrencyRowIdAsync(
        QueryFactory db, DbTransaction tx, long userId, int currencyCode)
        => await db.Query("player_item").Select("player_item_id")
            .Where("user_id", userId)
            .Where("row_type", Constants.PlayerItemRow.Currency)
            .Where("item_code", currencyCode)
            .FirstOrDefaultAsync<long?>(tx);

    /// <summary>갱신 직후 잔액을 기본키로 읽는다. 자기 트랜잭션이 그 행에 X락을 쥔 상태라 방금 확정한 값이 보인다.</summary>
    private static async Task<long> ReadCurrencyBalanceAsync(QueryFactory db, DbTransaction tx, long rowId)
        => await db.Query("player_item").Select("quantity")
            .Where("player_item_id", rowId)
            .FirstOrDefaultAsync<long?>(tx) ?? 0;

    /// <summary>계정의 재화 잔액을 읽는다(행이 없으면 0). 갱신 전 안내용 조회이며, 실제 차감은 갱신 문장이 확정한다.</summary>
    private static async Task<long> LoadCurrencyBalanceAsync(
        QueryFactory db, DbTransaction tx, long userId, int currencyCode)
        => await db.Query("player_item").Select("quantity")
            .Where("user_id", userId)
            .Where("row_type", Constants.PlayerItemRow.Currency)
            .Where("item_code", currencyCode)
            .FirstOrDefaultAsync<long?>(tx) ?? 0;

    /// <summary>
    /// 재화를 <paramref name="amount"/>만큼 차감한다. 잔액이 부족하면 <b>아무것도 바꾸지 않고</b> null을 돌려주고,
    /// 성공하면 차감 후 잔액을 돌려준다.
    /// <para><b>읽어서 계산한 값을 쓰지 않는다.</b> 잠금 없는 <c>SELECT</c>는 스냅샷 읽기라 동시에 도는 다른
    /// 트랜잭션의 변경을 보지 못한다 — 같은 계정의 요청 둘이 겹치면 양쪽이 같은 잔액을 읽고 각자 계산한 값을
    /// 덮어써 한쪽 차감이 사라진다. <c>quantity = quantity - @amount</c>로 DB가 직접 계산하게 하면 그 갱신은
    /// 최신 커밋값을 읽고 그 행을 잠그므로 겹칠 틈이 없고, 잔액 검사도 같은 문장의 <c>WHERE</c>가 겸한다.</para>
    /// <para><b>갱신은 기본키로 건다.</b> <c>WHERE user_id = ? AND row_type = 2 AND item_code = ?</c>로 바로 갱신하면
    /// 이 조건에 맞는 인덱스가 없어 그 계정의 아이템 행까지 훑으며 잠그고, 잠그는 순서가 엇갈리면 교착이 난다.</para>
    /// <para>상대값 갱신은 SqlKata 빌더로 표현되지 않아(증감폭을 <c>int</c>로만 받는다) 파라미터를 바인딩한
    /// 문장을 쓴다. 문자열을 조립하지는 않는다.</para>
    /// </summary>
    private static async Task<long?> TryDebitCurrencyAsync(
        QueryFactory db, DbTransaction tx, long userId, int currencyCode, long amount)
    {
        var rowId = await FindCurrencyRowIdAsync(db, tx, userId, currencyCode);

        // 비용 0(무료 경로)은 갱신할 것이 없다. 응답에 담을 현재 잔액만 돌려준다.
        if (amount <= 0)
        {
            return rowId is null ? 0 : await ReadCurrencyBalanceAsync(db, tx, rowId.Value);
        }

        if (rowId is null)
        {
            return null;
        }

        var affected = await db.StatementAsync(
            """
            UPDATE player_item SET quantity = quantity - @amount
             WHERE player_item_id = @rowId AND quantity >= @amount
            """,
            new { rowId, amount }, tx);

        // 0행 = 잔액 부족. 조건이 갱신 문장 안에 있으므로 부족하면 아무것도 바뀌지 않는다.
        return affected == 0 ? null : await ReadCurrencyBalanceAsync(db, tx, rowId.Value);
    }
}
