using System.Data.Common;
using GameServer.Data;
using SqlKata.Execution;

namespace GameServer.Repositories;

/// <summary>거래소 등록 1건의 스냅샷(목록 응답·에스크로 반송 공용).</summary>
public sealed record TradeListingSnapshot(
    long ListingId, long SellerUserId, int ItemCode, int EnhanceLevel, int Quantity, long Price, long CreatedAt);

public enum TradeRegisterStatus
{
    Ok,
    ItemNotFound,        // 인벤토리에 없음(타인 아이템·이미 등록 중 포함)
    ItemEquipped,        // 장착 중
    NotSellable,         // item_master.sellable=0
    PriceOutOfRange,     // 기준가 ±20% 밖
    ListingLimitExceeded, // 계정 동시 등록 한도 초과
}

public enum TradeCloseStatus
{
    Ok,
    ListingNotFound, // 등록 없음
    AlreadyClosed,   // 이미 판매/취소(동시 경합 포함)
    NotOwner,        // 본인 등록 아님(취소)
    SelfPurchase,    // 자기 등록 구매
    InsufficientGold,
    InventoryFull,
}

/// <summary>판매 등록 결과. 성공 시 등록된 스냅샷을 함께 돌려준다.</summary>
public sealed record TradeRegisterOutcome(TradeRegisterStatus Status, TradeListingSnapshot? Listing)
{
    public static TradeRegisterOutcome Fail(TradeRegisterStatus status) => new(status, null);
}

/// <summary>구매 결과. 성공 시 거래된 등록 스냅샷·차감 후 골드 잔액·구매자에게 발급된 아이템 메일 id를 돌려준다.</summary>
public sealed record TradeBuyOutcome(
    TradeCloseStatus Status, TradeListingSnapshot? Listing, long GoldBalance, long ItemMailId)
{
    public static TradeBuyOutcome Fail(TradeCloseStatus status) => new(status, null, 0, 0);
}

/// <summary>판매 취소 결과. 성공 시 인벤토리로 복귀한 아이템 스냅샷을 돌려준다.</summary>
public sealed record TradeCancelOutcome(TradeCloseStatus Status, TradeListingSnapshot? Listing)
{
    public static TradeCancelOutcome Fail(TradeCloseStatus status) => new(status, null);
}

/// <summary>거래 아이템의 마스터 정보(판매 가능 여부·기준가·타입·스택). 서비스가 마스터에서 조회해 주입한다.</summary>
public sealed record TradeItemInfo(int ItemType, int StackMax, int Sellable, long BasePrice);

public interface ITradeRepository
{
    /// <summary>
    /// 판매중 등록을 가격 오름차순으로 조회한다(itemCode 0이면 전체). 다음 페이지 존재 판정을 위해 limit+1건을 읽는다.
    /// viewerUserId 기준으로 <b>쿼리 단계에서</b> 판매자를 거른다 — mine=false면 그 계정의 등록을 제외(구매 대상),
    /// mine=true면 그 계정의 등록만(취소 대상) 반환한다.
    /// </summary>
    Task<(IReadOnlyList<TradeListingSnapshot> Listings, bool HasMore)> GetActiveListingsAsync(
        int itemCode, long viewerUserId, bool mine, int page, int pageSize);

    /// <summary>지정 계정의 판매중 등록 수(idx_trade_seller). 목록 캐시 사용 가능 여부 판정에 쓴다.</summary>
    Task<int> CountActiveListingsBySellerAsync(long userId);

    /// <summary>등록 스냅샷 1건(캐시 적재용). 없으면 null.</summary>
    Task<TradeListingSnapshot?> GetListingAsync(long listingId);

    /// <summary>판매 등록(에스크로): 한도·아이템·가격 검증 → player_item 제거 → trade_listing 생성을 한 트랜잭션으로 적용한다.</summary>
    Task<TradeRegisterOutcome> ApplyRegisterAsync(
        long userId, long itemId, long price, Func<int, TradeItemInfo?> itemLookup,
        int listingLimit, long nowUnix, long expiresAt);

    /// <summary>
    /// 구매: 조건부 갱신 선점 → 골드 차감 → <b>구매 아이템 메일 발급(구매자)</b> → 판매 대금 메일 발급(판매자)을
    /// 한 트랜잭션으로 적용한다. 아이템은 인벤토리에 직접 넣지 않고 우편함으로 보낸다.
    /// </summary>
    Task<TradeBuyOutcome> ApplyBuyAsync(
        long buyerUserId, long listingId,
        Func<TradeListingSnapshot, MailDraft> composeItemMail,
        Func<TradeListingSnapshot, MailDraft> composeSettlementMail, long nowUnix);

    /// <summary>판매 취소: 본인·판매중 확인 → 조건부 갱신 선점 → 아이템 인벤토리 복원을 한 트랜잭션으로 적용한다.</summary>
    Task<TradeCancelOutcome> ApplyCancelAsync(
        long userId, long listingId, Func<int, TradeItemInfo?> itemLookup, long nowUnix);

    /// <summary>만료 배치 대상(판매중 + 만료 시각 경과) listing_id를 오름차순 최대 limit건 조회한다.</summary>
    Task<IReadOnlyList<long>> GetExpiredListingIdsAsync(long nowUnix, int limit);

    /// <summary>만료 처리: 조건부 갱신으로 취소 확정 → 아이템을 판매자에게 메일로 반송한다. 이미 닫혔으면 null.</summary>
    Task<TradeListingSnapshot?> ApplyExpireAsync(
        long listingId, Func<TradeListingSnapshot, MailDraft> composeReturnMail, long nowUnix);
}

/// <summary>
/// 거래소 세이브 접근 계층(taskbar_hero_game). SqlKata 쿼리 빌더 + 제네릭 매핑만 사용한다(dynamic 금지).
/// 상태 전이(구매·취소·만료)는 모두 <b>조건부 갱신(status=1일 때만 전이)</b>으로 선점해 이중 판매를 차단한다
/// (trade 기획서 §7.2). 선점은 재화·아이템 이동보다 항상 앞에 둔다.
/// </summary>
public sealed class TradeRepository : ITradeRepository
{
    private const int RowTypeItem = 1;
    private const int RowTypeCurrency = 2;
    private const int GoldItemCode = 1;
    private const int ItemTypeMaterial = 2;

    private const int StatusOnSale = 1;
    private const int StatusSold = 2;
    private const int StatusCancelled = 3;

    private readonly GameDbFactory _dbFactory;

    /// <summary>세이브 DB 커넥션 팩토리를 주입받는다.</summary>
    public TradeRepository(GameDbFactory dbFactory) => _dbFactory = dbFactory;

    /// <summary>
    /// 판매중 등록을 가격 오름차순·listing_id 보조 정렬로 페이징 조회한다(idx_trade_browse·idx_trade_price가 커버).
    /// pageSize+1건을 읽어 초과분 존재 여부로 hasMore를 판정한다(전체 COUNT 쿼리를 피한다).
    /// <para><b>판매자 필터는 WHERE에서 처리한다.</b> 조회 결과를 받아서 걸러내면 페이지마다 건수가 들쭉날쭉해지고
    /// OFFSET이 필터 전 기준이라 항목이 누락·중복된다 — 필터와 페이징은 같은 쿼리에서 끝내야 한다.</para>
    /// </summary>
    public async Task<(IReadOnlyList<TradeListingSnapshot> Listings, bool HasMore)> GetActiveListingsAsync(
        int itemCode, long viewerUserId, bool mine, int page, int pageSize)
    {
        await using var connection = _dbFactory.CreateConnection();
        await connection.OpenAsync();

        var db = _dbFactory.Create(connection);
        var query = db.Query("trade_listing")
            .Select("listing_id", "seller_user_id", "item_code", "enhance_level", "quantity", "price", "created_at")
            .Where("status", StatusOnSale);
        if (itemCode > 0)
        {
            query = query.Where("item_code", itemCode);
        }

        if (mine)
        {
            query = query.Where("seller_user_id", viewerUserId);
        }
        else if (viewerUserId > 0)
        {
            query = query.Where("seller_user_id", "<>", viewerUserId);
        }

        var rows = (await query
            .OrderBy("price").OrderBy("listing_id")
            .Offset((long)page * pageSize).Limit(pageSize + 1)
            .GetAsync<TradeListingRow>()).ToList();

        var hasMore = rows.Count > pageSize;
        var listings = rows.Take(pageSize)
            .Select(r => new TradeListingSnapshot(
                r.ListingId, r.SellerUserId, r.ItemCode, r.EnhanceLevel, r.Quantity, r.Price, r.CreatedAt))
            .ToList();
        return (listings, hasMore);
    }

    /// <summary>지정 계정의 판매중 등록 수. (seller_user_id, status) 색인만으로 끝나는 가벼운 조회다.</summary>
    public async Task<int> CountActiveListingsBySellerAsync(long userId)
    {
        await using var connection = _dbFactory.CreateConnection();
        await connection.OpenAsync();

        var db = _dbFactory.Create(connection);
        return await db.Query("trade_listing")
            .Where("seller_user_id", userId).Where("status", StatusOnSale)
            .CountAsync<int>();
    }

    /// <summary>등록 1건을 조회해 스냅샷으로 돌려준다(상태 무관 — 캐시 적재는 호출측이 판매중만 넣는다).</summary>
    public async Task<TradeListingSnapshot?> GetListingAsync(long listingId)
    {
        await using var connection = _dbFactory.CreateConnection();
        await connection.OpenAsync();

        var db = _dbFactory.Create(connection);
        var row = await db.Query("trade_listing")
            .Select("listing_id", "seller_user_id", "item_code", "enhance_level", "quantity", "price", "created_at")
            .Where("listing_id", listingId).Where("status", StatusOnSale)
            .FirstOrDefaultAsync<TradeListingRow>();
        return row is null
            ? null
            : new TradeListingSnapshot(
                row.ListingId, row.SellerUserId, row.ItemCode, row.EnhanceLevel,
                row.Quantity, row.Price, row.CreatedAt);
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
        long userId, long itemId, long price, Func<int, TradeItemInfo?> itemLookup,
        int listingLimit, long nowUnix, long expiresAt)
    {
        await using var connection = _dbFactory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var db = _dbFactory.Create(connection);

            // 1) 동시 등록 한도.
            var active = await db.Query("trade_listing")
                .Where("seller_user_id", userId).Where("status", StatusOnSale)
                .CountAsync<int>(transaction: transaction);
            if (active >= listingLimit)
            {
                await transaction.RollbackAsync();
                return TradeRegisterOutcome.Fail(TradeRegisterStatus.ListingLimitExceeded);
            }

            // 2) 소유 확인(본인 아이템 행만). 재화 행(row_type=2)은 거래 대상이 아니다.
            var item = await db.Query("player_item")
                .Select("player_item_id", "item_code", "quantity", "enhance_level")
                .Where("player_item_id", itemId).Where("user_id", userId).Where("row_type", RowTypeItem)
                .FirstOrDefaultAsync<PlayerItemRow>(transaction);
            if (item is null)
            {
                await transaction.RollbackAsync();
                return TradeRegisterOutcome.Fail(TradeRegisterStatus.ItemNotFound);
            }

            // 장착 중이면 등록 불가(장착 원장에 행이 있으면 장착 상태).
            var equipped = await db.Query("player_item_equipped")
                .Select("player_item_id").Where("player_item_id", itemId)
                .FirstOrDefaultAsync<long?>(transaction);
            if (equipped is not null)
            {
                await transaction.RollbackAsync();
                return TradeRegisterOutcome.Fail(TradeRegisterStatus.ItemEquipped);
            }

            // 3) 마스터 검증: 판매 가능 여부 → 가격 범위.
            var info = itemLookup(item.ItemCode);
            if (info is null || info.Sellable != 1 || info.BasePrice <= 0)
            {
                await transaction.RollbackAsync();
                return TradeRegisterOutcome.Fail(TradeRegisterStatus.NotSellable);
            }

            if (!IsPriceInRange(price, info.BasePrice))
            {
                await transaction.RollbackAsync();
                return TradeRegisterOutcome.Fail(TradeRegisterStatus.PriceOutOfRange);
            }

            // 4) 에스크로 이동: 인벤토리 행 제거(스택형도 행 전체 — 부분 판매 없음).
            //    조건에 user_id를 함께 걸어 동시 요청이 같은 행을 두 번 등록하지 못하게 한다(0행이면 경합 패배).
            var removed = await db.Query("player_item")
                .Where("player_item_id", itemId).Where("user_id", userId)
                .DeleteAsync(transaction);
            if (removed == 0)
            {
                await transaction.RollbackAsync();
                return TradeRegisterOutcome.Fail(TradeRegisterStatus.ItemNotFound);
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
                status = StatusOnSale,
                buyer_user_id = 0,
                created_at = nowUnix,
                expires_at = expiresAt,
                closed_at = 0,
            }, transaction);

            await transaction.CommitAsync();
            return new TradeRegisterOutcome(
                TradeRegisterStatus.Ok,
                new TradeListingSnapshot(listingId, userId, item.ItemCode, item.EnhanceLevel, quantity, price, nowUnix));
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

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
    {
        await using var connection = _dbFactory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var db = _dbFactory.Create(connection);

            // 1) 등록 확인.
            var row = await db.Query("trade_listing")
                .Select("listing_id", "seller_user_id", "item_code", "enhance_level", "quantity", "price",
                        "created_at", "status")
                .Where("listing_id", listingId)
                .FirstOrDefaultAsync<TradeListingStatusRow>(transaction);
            if (row is null)
            {
                await transaction.RollbackAsync();
                return TradeBuyOutcome.Fail(TradeCloseStatus.ListingNotFound);
            }

            if (row.Status != StatusOnSale)
            {
                await transaction.RollbackAsync();
                return TradeBuyOutcome.Fail(TradeCloseStatus.AlreadyClosed);
            }

            if (row.SellerUserId == buyerUserId)
            {
                await transaction.RollbackAsync();
                return TradeBuyOutcome.Fail(TradeCloseStatus.SelfPurchase);
            }

            // 2) 지불 능력·적재 여유 사전 확인.
            var gold = await LoadGoldAsync(db, transaction, buyerUserId);
            if (gold is null || gold.Value.Quantity < row.Price)
            {
                await transaction.RollbackAsync();
                return TradeBuyOutcome.Fail(TradeCloseStatus.InsufficientGold);
            }

            // 3) 선점(CAS): 판매중일 때만 판매완료로 전이.
            var claimed = await db.Query("trade_listing")
                .Where("listing_id", listingId).Where("status", StatusOnSale)
                .UpdateAsync(
                    new { status = StatusSold, buyer_user_id = buyerUserId, closed_at = nowUnix }, transaction);
            if (claimed == 0)
            {
                await transaction.RollbackAsync();
                return TradeBuyOutcome.Fail(TradeCloseStatus.AlreadyClosed);
            }

            var snapshot = new TradeListingSnapshot(
                row.ListingId, row.SellerUserId, row.ItemCode, row.EnhanceLevel,
                row.Quantity, row.Price, row.CreatedAt);

            // 4) 골드 차감.
            var balance = gold.Value.Quantity - row.Price;
            await db.Query("player_item").Where("player_item_id", gold.Value.PlayerItemId)
                .UpdateAsync(new { quantity = balance }, transaction);

            // 5) 구매 아이템 메일 발급(구매자). 인벤토리에 직접 넣지 않으므로 이 시점에 용량을 보지 않는다
            //    — 적재는 우편함 수령 시 이뤄지고, 그때 부족하면 InventoryFull로 거부된다(메일 6.1).
            var itemMailId = await MailRepository.InsertMailAsync(
                db, transaction, buyerUserId, composeItemMail(snapshot), nowUnix);

            // 6) 판매 대금 메일 발급(판매자).
            await MailRepository.InsertMailAsync(
                db, transaction, row.SellerUserId, composeSettlementMail(snapshot), nowUnix);

            await transaction.CommitAsync();
            return new TradeBuyOutcome(TradeCloseStatus.Ok, snapshot, balance, itemMailId);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// 판매 취소를 단일 트랜잭션으로 적용한다(trade 기획서 §6.2). 본인·판매중 확인 → 조건부 갱신 선점 →
    /// 아이템을 판매자 인벤토리에 복원한다. 복원 칸이 없으면 전체 롤백(InventoryFull).
    /// </summary>
    public async Task<TradeCancelOutcome> ApplyCancelAsync(
        long userId, long listingId, Func<int, TradeItemInfo?> itemLookup, long nowUnix)
    {
        await using var connection = _dbFactory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var db = _dbFactory.Create(connection);

            var row = await db.Query("trade_listing")
                .Select("listing_id", "seller_user_id", "item_code", "enhance_level", "quantity", "price",
                        "created_at", "status")
                .Where("listing_id", listingId)
                .FirstOrDefaultAsync<TradeListingStatusRow>(transaction);
            if (row is null)
            {
                await transaction.RollbackAsync();
                return TradeCancelOutcome.Fail(TradeCloseStatus.ListingNotFound);
            }

            if (row.SellerUserId != userId)
            {
                await transaction.RollbackAsync();
                return TradeCancelOutcome.Fail(TradeCloseStatus.NotOwner);
            }

            if (row.Status != StatusOnSale)
            {
                await transaction.RollbackAsync();
                return TradeCancelOutcome.Fail(TradeCloseStatus.AlreadyClosed);
            }

            var claimed = await db.Query("trade_listing")
                .Where("listing_id", listingId).Where("status", StatusOnSale)
                .UpdateAsync(new { status = StatusCancelled, closed_at = nowUnix }, transaction);
            if (claimed == 0)
            {
                await transaction.RollbackAsync();
                return TradeCancelOutcome.Fail(TradeCloseStatus.AlreadyClosed);
            }

            var snapshot = new TradeListingSnapshot(
                row.ListingId, row.SellerUserId, row.ItemCode, row.EnhanceLevel,
                row.Quantity, row.Price, row.CreatedAt);
            var stored = await StoreTradeItemAsync(db, transaction, userId, snapshot, itemLookup, nowUnix);
            if (!stored)
            {
                await transaction.RollbackAsync();
                return TradeCancelOutcome.Fail(TradeCloseStatus.InventoryFull);
            }

            await transaction.CommitAsync();
            return new TradeCancelOutcome(TradeCloseStatus.Ok, snapshot);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>만료 대상(판매중 + expires_at 경과)을 listing_id 오름차순으로 최대 limit건 조회한다(idx_trade_expire).</summary>
    public async Task<IReadOnlyList<long>> GetExpiredListingIdsAsync(long nowUnix, int limit)
    {
        await using var connection = _dbFactory.CreateConnection();
        await connection.OpenAsync();

        var db = _dbFactory.Create(connection);
        var ids = await db.Query("trade_listing")
            .Select("listing_id")
            .Where("status", StatusOnSale).Where("expires_at", "<", nowUnix)
            .OrderBy("listing_id").Limit(limit)
            .GetAsync<long>();
        return ids.ToList();
    }

    /// <summary>
    /// 만료 1건을 단일 트랜잭션으로 처리한다(trade 기획서 §7.6.2): 조건부 갱신으로 취소 확정 →
    /// 스냅샷 확보 → 판매자에게 반송 메일 발급. 그 사이 구매·취소로 이미 닫혔으면 null(스킵).
    /// 만료 반송은 아이템만 되돌리며 골드 이동은 없다.
    /// </summary>
    public async Task<TradeListingSnapshot?> ApplyExpireAsync(
        long listingId, Func<TradeListingSnapshot, MailDraft> composeReturnMail, long nowUnix)
    {
        await using var connection = _dbFactory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var db = _dbFactory.Create(connection);

            // 1) 선점(CAS): 아직 판매중이고 만료가 지난 등록만 취소로 전이.
            var closed = await db.Query("trade_listing")
                .Where("listing_id", listingId).Where("status", StatusOnSale)
                .Where("expires_at", "<", nowUnix)
                .UpdateAsync(new { status = StatusCancelled, closed_at = nowUnix }, transaction);
            if (closed == 0)
            {
                await transaction.RollbackAsync();
                return null;
            }

            // 2) 반송 스냅샷.
            var row = await db.Query("trade_listing")
                .Select("listing_id", "seller_user_id", "item_code", "enhance_level", "quantity", "price", "created_at")
                .Where("listing_id", listingId)
                .FirstOrDefaultAsync<TradeListingRow>(transaction);
            if (row is null)
            {
                await transaction.RollbackAsync();
                return null;
            }

            // 3) 반송 메일 발급(판매자). 인벤토리가 가득해도 안전하게 되돌리기 위해 메일을 쓴다.
            var snapshot = new TradeListingSnapshot(
                row.ListingId, row.SellerUserId, row.ItemCode, row.EnhanceLevel,
                row.Quantity, row.Price, row.CreatedAt);
            await MailRepository.InsertMailAsync(
                db, transaction, snapshot.SellerUserId, composeReturnMail(snapshot), nowUnix);

            await transaction.CommitAsync();
            return snapshot;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

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

    /// <summary>구매자의 골드 재화 행(행 id·잔액). 재화 행이 없으면 null(= 보유 골드 0).</summary>
    private static async Task<(long PlayerItemId, long Quantity)?> LoadGoldAsync(
        QueryFactory db, DbTransaction tx, long userId)
    {
        var row = await db.Query("player_item").Select("player_item_id", "quantity")
            .Where("user_id", userId).Where("row_type", RowTypeCurrency).Where("item_code", GoldItemCode)
            .FirstOrDefaultAsync<ItemIdQtyRow>(tx);
        return row is null ? null : (row.PlayerItemId, row.Quantity);
    }

    /// <summary>
    /// 거래 아이템(에스크로 스냅샷)을 대상 계정 인벤토리에 적재한다. 메일 첨부와 달리 <b>강화 단계를 보존</b>한다.
    /// 재료(스택형)는 기존 스택의 여유부터 채우고 남으면 새 칸, 장비는 1개당 1행으로 새 칸에 넣는다.
    /// 빈 칸이 부족하면 false(호출측 롤백).
    /// </summary>
    private static async Task<bool> StoreTradeItemAsync(
        QueryFactory db, DbTransaction tx, long userId, TradeListingSnapshot listing,
        Func<int, TradeItemInfo?> itemLookup, long nowUnix)
    {
        var info = itemLookup(listing.ItemCode);
        var itemType = info?.ItemType ?? 1;
        var stackMax = Math.Max(1, info?.StackMax ?? 1);

        var capacity = await db.Query("game_player").Select("inventory_capacity")
            .Where("user_id", userId).FirstOrDefaultAsync<int?>(tx) ?? 0;
        var used = (await db.Query("player_item").Select("slot")
            .Where("user_id", userId).WhereNotNull("slot").GetAsync<int>(tx)).ToHashSet();

        long remaining = listing.Quantity;

        // 재료(스택 가능): 기존 스택의 여유부터 채운다(새 칸 불필요). 강화 단계가 없는 종류다.
        if (itemType == ItemTypeMaterial && stackMax > 1)
        {
            var stacks = await db.Query("player_item").Select("player_item_id", "quantity")
                .Where("user_id", userId).Where("row_type", RowTypeItem).Where("item_code", listing.ItemCode)
                .Where("quantity", "<", stackMax)
                .GetAsync<ItemIdQtyRow>(tx);

            foreach (var stack in stacks)
            {
                if (remaining <= 0)
                {
                    break;
                }

                var room = stackMax - stack.Quantity;
                var add = Math.Min(room, remaining);
                await db.Query("player_item").Where("player_item_id", stack.PlayerItemId)
                    .UpdateAsync(new { quantity = stack.Quantity + add }, tx);
                remaining -= add;
            }
        }

        var perRow = itemType == ItemTypeMaterial ? stackMax : 1;
        while (remaining > 0)
        {
            var slot = FirstFreeSlot(used, capacity);
            if (slot < 0)
            {
                return false;
            }

            var put = Math.Min(perRow, remaining);
            await db.Query("player_item").InsertAsync(new
            {
                user_id = userId,
                row_type = RowTypeItem,
                item_code = listing.ItemCode,
                quantity = put,
                slot,
                enhance_level = listing.EnhanceLevel,
                acquired_at = nowUnix,
            }, tx);
            used.Add(slot);
            remaining -= put;
        }

        return true;
    }

    /// <summary>used 집합에서 [0, capacity) 범위의 가장 작은 빈 칸. 없으면 -1.</summary>
    private static int FirstFreeSlot(HashSet<int> used, int capacity)
    {
        for (var i = 0; i < capacity; i++)
        {
            if (!used.Contains(i))
            {
                return i;
            }
        }

        return -1;
    }

}

// ── DB 행 매핑용 POCO(제네릭 매핑 전용, dynamic 금지) ──

file sealed class TradeListingRow
{
    public long ListingId { get; set; }
    public long SellerUserId { get; set; }
    public int ItemCode { get; set; }
    public int EnhanceLevel { get; set; }
    public int Quantity { get; set; }
    public long Price { get; set; }
    public long CreatedAt { get; set; }
}

file sealed class TradeListingStatusRow
{
    public long ListingId { get; set; }
    public long SellerUserId { get; set; }
    public int ItemCode { get; set; }
    public int EnhanceLevel { get; set; }
    public int Quantity { get; set; }
    public long Price { get; set; }
    public long CreatedAt { get; set; }
    public int Status { get; set; }
}

file sealed class PlayerItemRow
{
    public long PlayerItemId { get; set; }
    public int ItemCode { get; set; }
    public long Quantity { get; set; }
    public int EnhanceLevel { get; set; }
}

file sealed class ItemIdQtyRow
{
    public long PlayerItemId { get; set; }
    public long Quantity { get; set; }
}
