using CloudStructures;
using CloudStructures.Structures;
using GameServer.Repositories;
using StackExchange.Redis;

namespace GameServer.Services;

/// <summary>구매 락 보유 토큰. Dispose 대신 <see cref="TradeCache.ReleaseLockAsync"/>로 명시 해제한다.</summary>
/// <param name="Acquired">락을 실제로 잡았는지. false면 경합 패배(TradeBusy).</param>
/// <param name="Degraded">Redis 장애로 락 없이 진행 중인지(축소 운전 — 정합성은 조건부 갱신이 보증).</param>
/// <param name="Token">락 소유자 식별값. 해제 시 이 값이 일치할 때만 지운다(남의 락 해제 방지).</param>
public sealed record TradeLockHandle(bool Acquired, bool Degraded, string Token);

/// <summary>
/// 거래소 Redis 계층(trade 기획서 §7.3·§7.4). 두 가지를 담당한다.
/// <para><b>목록 캐시</b> — <c>trade:index:{itemCode}</c>(Sorted Set, score=가격) + <c>trade:listing:{listingId}</c>(스냅샷 JSON).
/// 전역 공유 읽기인 목록 조회를 MySQL 없이 응답한다. 캐시는 <b>파생 데이터</b>이며 정합성 정본은 항상 MySQL이다.</para>
/// <para><b>구매 락</b> — <c>trade:lock:listing:{listingId}</c>(SET NX + TTL). 같은 등록에 몰린 요청을 DB 도달 전에 줄인다.</para>
/// 모든 Redis 호출은 실패해도 예외를 밖으로 던지지 않는다. 장애 시 목록은 MySQL 폴백, 구매는 락 없이 진행한다(§7.5 축소 운전).
/// </summary>
public sealed class TradeCache
{
    /// <summary>구매 락 TTL(trade 기획서 §7.4). 락 보유 프로세스가 죽어도 이 시간 뒤 자동 해제된다.</summary>
    private static readonly TimeSpan LockTtl = TimeSpan.FromSeconds(3);

    /// <summary>락 재시도 간격·횟수(§7.4). 짧은 경합은 흡수하고, 계속 실패하면 TradeBusy로 돌려보낸다.</summary>
    private static readonly TimeSpan LockRetryDelay = TimeSpan.FromMilliseconds(50);
    private const int LockRetryCount = 2;

    private readonly RedisConnection _redis;
    private readonly ILogger<TradeCache> _logger;

    /// <summary>Redis 연결·로거를 주입받는다.</summary>
    public TradeCache(RedisConnection redis, ILogger<TradeCache> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    // ── 목록 캐시 ──

    /// <summary>
    /// 캐시에서 해당 페이지의 등록 목록을 읽는다. 색인이 비었거나 스냅샷이 하나라도 없으면 null을 돌려
    /// 호출측이 MySQL로 폴백하게 한다(부분 캐시로 잘린 목록을 응답하지 않는다).
    /// hasMore 판정을 위해 pageSize+1건 구간을 읽는다.
    /// </summary>
    public async Task<(IReadOnlyList<TradeListingSnapshot> Listings, bool HasMore)?> TryGetPageAsync(
        int itemCode, int page, int pageSize)
    {
        try
        {
            var index = Index(itemCode);
            var start = (long)page * pageSize;
            var ids = await index.RangeByRankAsync(start, start + pageSize, Order.Ascending);
            if (ids.Length == 0)
            {
                return null; // 비어 있음 = 미적재일 수 있으므로 MySQL에서 확인·적재한다(lazy).
            }

            var snapshots = new List<TradeListingSnapshot>(ids.Length);
            foreach (var id in ids)
            {
                var entry = await Snapshot(id).GetAsync();
                if (!entry.HasValue || entry.Value is null)
                {
                    return null; // 스냅샷 결손 → 폴백
                }

                snapshots.Add(entry.Value.ToSnapshot());
            }

            var hasMore = snapshots.Count > pageSize;
            return (snapshots.Take(pageSize).ToList(), hasMore);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "거래소 목록 캐시 조회 실패 — MySQL로 폴백합니다.");
            return null;
        }
    }

    /// <summary>
    /// MySQL에서 읽은 목록을 캐시에 채운다(lazy 적재). 스냅샷 TTL은 등록 만료 시각까지로 두어
    /// 만료와 함께 자연히 사라지게 한다. 실패는 무시한다(다음 조회가 다시 시도).
    /// <para><paramref name="isCompleteSet"/>가 false면(뒤 페이지 조회 등 부분 결과) <b>색인을 만들지 않는다</b> —
    /// 부분 색인은 조회에서 완전한 캐시처럼 보여 나머지 등록을 가린다. 그 경우 계속 MySQL로 응답한다.</para>
    /// </summary>
    public async Task FillAsync(
        int itemCode, IReadOnlyList<TradeListingSnapshot> listings, long nowUnix, bool isCompleteSet)
    {
        if (listings.Count == 0 || !isCompleteSet)
        {
            return;
        }

        try
        {
            var index = Index(itemCode);
            foreach (var listing in listings)
            {
                await index.AddAsync(listing.ListingId, listing.Price, null, When.Always);
                await Snapshot(listing.ListingId).SetAsync(TradeListingCacheEntry.From(listing), SnapshotTtl(nowUnix));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "거래소 목록 캐시 적재 실패 — 캐시 없이 계속합니다.");
        }
    }

    /// <summary>
    /// 등록을 캐시에 추가한다(판매 등록 커밋 후). 전체 목록 색인(itemCode=0)과 아이템별 색인 양쪽에 반영한다.
    /// <para><b>색인이 아직 없으면 만들지 않고 건너뛴다.</b> 없는 색인에 이 등록만 넣으면 "원소 1개짜리 완전한 색인"처럼
    /// 보여, 조회가 캐시 적중으로 판단하고 MySQL의 나머지 등록을 영영 못 보게 된다(부분 색인 문제).
    /// 건너뛰면 다음 조회가 미적재로 보고 MySQL에서 전량을 읽어 채운다(lazy).</para>
    /// </summary>
    public async Task AddAsync(TradeListingSnapshot listing, long nowUnix)
    {
        try
        {
            await AddToExistingIndexAsync(0, listing);
            await AddToExistingIndexAsync(listing.ItemCode, listing);
            await Snapshot(listing.ListingId).SetAsync(TradeListingCacheEntry.From(listing), SnapshotTtl(nowUnix));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "거래소 목록 캐시 추가 실패(listingId {ListingId}) — 조회 시 MySQL 폴백으로 흡수됩니다.",
                listing.ListingId);
        }
    }

    /// <summary>이미 적재된 색인에만 원소를 추가한다. 색인이 없으면 부분 색인을 만들지 않기 위해 아무것도 하지 않는다.</summary>
    private async Task AddToExistingIndexAsync(int itemCode, TradeListingSnapshot listing)
    {
        var index = Index(itemCode);
        if (!await index.ExistsAsync())
        {
            return;
        }

        await index.AddAsync(listing.ListingId, listing.Price, null, When.Always);
    }

    /// <summary>등록을 캐시에서 제거한다(구매·취소·만료 커밋 후). 실패해도 거래는 되돌리지 않는다(§7.5).</summary>
    public async Task RemoveAsync(TradeListingSnapshot listing)
    {
        try
        {
            await Index(0).RemoveAsync(listing.ListingId);
            await Index(listing.ItemCode).RemoveAsync(listing.ListingId);
            await Snapshot(listing.ListingId).DeleteAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "거래소 목록 캐시 제거 실패(listingId {ListingId}) — TTL·재적재가 흡수합니다.",
                listing.ListingId);
        }
    }

    // ── 구매 락 ──

    /// <summary>
    /// 등록 단위 락을 SET NX + TTL로 시도한다(§7.4). 경합이면 짧게 재시도하고, 그래도 실패하면
    /// Acquired=false(호출측이 TradeBusy로 응답). Redis 장애면 Degraded=true로 <b>락 없이 진행</b>한다.
    /// </summary>
    public async Task<TradeLockHandle> AcquireLockAsync(long listingId)
    {
        var token = Guid.NewGuid().ToString("N");
        try
        {
            var key = Lock(listingId);
            for (var attempt = 0; attempt <= LockRetryCount; attempt++)
            {
                if (await key.SetAsync(token, LockTtl, When.NotExists))
                {
                    return new TradeLockHandle(true, false, token);
                }

                if (attempt < LockRetryCount)
                {
                    await Task.Delay(LockRetryDelay);
                }
            }

            return new TradeLockHandle(false, false, token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "거래소 구매 락 사용 불가(listingId {ListingId}) — 락 없이 진행합니다(축소 운전).", listingId);
            return new TradeLockHandle(false, true, token);
        }
    }

    /// <summary>
    /// 락을 해제한다. <b>값이 자기 토큰일 때만</b> 지운다 — TTL이 먼저 만료돼 다른 요청이 같은 키를 잡았을 수 있어,
    /// 값 비교 없이 지우면 남의 락을 해제하게 된다(§7.4).
    /// </summary>
    public async Task ReleaseLockAsync(long listingId, TradeLockHandle handle)
    {
        if (!handle.Acquired)
        {
            return;
        }

        try
        {
            var key = Lock(listingId);
            var current = await key.GetAsync();
            if (current.HasValue && current.Value == handle.Token)
            {
                await key.DeleteAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "거래소 구매 락 해제 실패(listingId {ListingId}) — TTL로 자연 만료됩니다.", listingId);
        }
    }

    // ── 키 구성 ──

    /// <summary>가격순 색인. itemCode 0 = 전체 목록 색인.</summary>
    private RedisSortedSet<long> Index(int itemCode) => new(_redis, $"trade:index:{itemCode}", null);

    /// <summary>등록 스냅샷(목록 응답 조립용).</summary>
    private RedisString<TradeListingCacheEntry> Snapshot(long listingId)
        => new(_redis, $"trade:listing:{listingId}", null);

    /// <summary>구매·취소·만료가 공유하는 등록 단위 락.</summary>
    private RedisString<string> Lock(long listingId) => new(_redis, $"trade:lock:listing:{listingId}", null);

    /// <summary>스냅샷 TTL. 등록 만료(3일)보다 넉넉히 잡되 무기한으로 두지 않는다.</summary>
    private static TimeSpan SnapshotTtl(long nowUnix) => TimeSpan.FromDays(3);
}

/// <summary>
/// 캐시에 담는 등록 스냅샷(직렬화 대상). CloudStructures 기본 직렬화를 쓰므로 공개 프로퍼티만 둔다.
/// </summary>
public sealed class TradeListingCacheEntry
{
    public long ListingId { get; set; }
    public long SellerUserId { get; set; }
    public int ItemCode { get; set; }
    public int EnhanceLevel { get; set; }
    public int Quantity { get; set; }
    public long Price { get; set; }
    public long CreatedAt { get; set; }

    /// <summary>리포지토리 스냅샷 → 캐시 엔트리.</summary>
    public static TradeListingCacheEntry From(TradeListingSnapshot s) => new()
    {
        ListingId = s.ListingId,
        SellerUserId = s.SellerUserId,
        ItemCode = s.ItemCode,
        EnhanceLevel = s.EnhanceLevel,
        Quantity = s.Quantity,
        Price = s.Price,
        CreatedAt = s.CreatedAt,
    };

    /// <summary>캐시 엔트리 → 리포지토리 스냅샷.</summary>
    public TradeListingSnapshot ToSnapshot()
        => new(ListingId, SellerUserId, ItemCode, EnhanceLevel, Quantity, Price, CreatedAt);
}
