using CloudStructures;
using CloudStructures.Structures;
using GameServer.Repositories;
using StackExchange.Redis;
using ZLogger;

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

    /// <summary>
    /// 목록 색인 TTL(§7.3). 캐시 정리(<see cref="RemoveAsync"/>)가 <b>전부 실패</b>해 오염을 감지조차 못 하는 경우
    /// (Redis 불통 중 구매 성공 → 이후 복구)의 유일한 안전망이다. 이 시간이 지나면 색인이 사라져 다음 조회가
    /// MySQL에서 전량을 다시 읽고 색인을 새로 만든다.
    /// <para><b>적재 시점 기준 절대 만료</b>다 — 조회·등록으로 갱신하지 않는다(갱신하면 인기 아이템의 색인이
    /// 영구히 만료되지 않아 안전망이 무력화된다).</para>
    /// </summary>
    private static readonly TimeSpan IndexTtl = TimeSpan.FromHours(24);

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
    /// 캐시에서 해당 itemCode의 <b>판매중 등록 전량</b>을 읽는다. 색인이 비었거나 스냅샷이 하나라도 없으면
    /// null을 돌려 호출측이 MySQL로 폴백하게 한다(부분 캐시로 잘린 목록을 응답하지 않는다).
    /// <para>색인은 완전 집합으로만 적재·유지되므로(<see cref="FillAsync"/>·<see cref="AddToExistingIndexAsync"/>·
    /// <see cref="RemoveAsync"/>), 호출측은 이 결과에 <b>뷰어별 필터(본인 제외/본인만)와 페이징을 메모리에서</b>
    /// 적용해도 정확하다 — 그래서 목록 조회가 DB를 타지 않는다.</para>
    /// <para>스냅샷은 한 건씩 순차로 읽지 않고 <b>한꺼번에 요청</b>해 왕복 수를 줄인다.</para>
    /// </summary>
    public async Task<IReadOnlyList<TradeListingSnapshot>?> TryGetAllAsync(int itemCode)
    {
        try
        {
            var ids = await Index(itemCode).RangeByRankAsync(0, -1, Order.Ascending);
            if (ids.Length == 0)
            {
                return null; // 비어 있음 = 미적재일 수 있으므로 MySQL에서 확인·적재한다(lazy).
            }

            var entries = await Task.WhenAll(ids.Select(async id => await Snapshot(id).GetAsync()));

            var snapshots = new List<TradeListingSnapshot>(entries.Length);
            foreach (var entry in entries)
            {
                if (!entry.HasValue || entry.Value is null)
                {
                    return null; // 스냅샷 결손 → 폴백
                }

                snapshots.Add(entry.Value.ToSnapshot());
            }

            return snapshots;
        }
        catch (Exception ex)
        {
            _logger.ZLogWarning(ex, $"거래소 목록 캐시 조회 실패 — MySQL로 폴백합니다.");
            return null;
        }
    }

    /// <summary>
    /// MySQL에서 읽은 <b>완전 집합</b>을 캐시에 채운다(lazy 적재). 스냅샷 TTL은 등록 만료 시각까지로 두어
    /// 만료와 함께 자연히 사라지게 한다. 실패는 무시한다(다음 조회가 다시 시도).
    /// <para><b>부분 집합을 넘기지 말 것.</b> 부분 색인은 조회에서 완전한 캐시처럼 보여 나머지 등록을 영구히 가린다
    /// (그래서 호출측은 페이징·필터를 걸지 않은 전량 조회 결과만 넘긴다).</para>
    /// </summary>
    public async Task FillAsync(int itemCode, IReadOnlyList<TradeListingSnapshot> listings, long nowUnix)
    {
        if (listings.Count == 0)
        {
            return;
        }

        try
        {
            // 색인을 지우고 다시 만든다(덮어쓰기가 아니라 재구축). 원소를 더하기만 하면 정리에 실패해 남은
            // 고아 id가 계속 남아, 그 id의 스냅샷 결손 때문에 이 itemCode가 영구히 캐시 미스가 된다.
            var index = Index(itemCode);
            await index.DeleteAsync();

            // 일괄 추가 + 이때 한 번만 TTL을 건다(적재 시점 기준 절대 만료).
            var entries = listings
                .Select(l => new RedisSortedSetEntry<long>(l.ListingId, l.Price))
                .ToArray();
            await index.AddAsync(entries, IndexTtl, When.Always);

            // 스냅샷도 읽기와 같이 한꺼번에 요청한다(등록 수만큼 순차 왕복하지 않는다).
            await Task.WhenAll(listings.Select(async listing =>
                await Snapshot(listing.ListingId)
                    .SetAsync(TradeListingCacheEntry.From(listing), SnapshotTtl(listing.ExpiresAt, nowUnix))));
        }
        catch (Exception ex)
        {
            _logger.ZLogWarning(ex, $"거래소 목록 캐시 적재 실패 — 캐시 없이 계속합니다.");
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
            await Snapshot(listing.ListingId)
                .SetAsync(TradeListingCacheEntry.From(listing), SnapshotTtl(listing.ExpiresAt, nowUnix));
        }
        catch (Exception ex)
        {
            _logger.ZLogWarning(ex, $"거래소 목록 캐시 추가 실패(listingId {listing.ListingId:@ListingId}) — 조회 시 MySQL 폴백으로 흡수됩니다.");
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

    /// <summary>
    /// 등록을 캐시에서 제거한다(구매·취소·만료 커밋 후). 실패해도 거래는 되돌리지 않는다(§7.5) —
    /// 커밋이 이미 끝났고, 캐시는 파생 데이터이며 이중 판매는 MySQL 조건부 갱신이 막는다.
    /// <para><b>스냅샷을 먼저 지운다.</b> 중간에 실패해 색인에 id가 남아도, 스냅샷이 없으면 다음 조회가
    /// 결손을 감지해(<see cref="TryGetAllAsync"/>가 null) MySQL에서 전량을 다시 읽고 색인을 재구축한다.
    /// 반대 순서(색인 먼저)면 스냅샷이 남아 <b>팔린 등록이 계속 목록에 보인다</b>.</para>
    /// </summary>
    public async Task RemoveAsync(TradeListingSnapshot listing)
    {
        try
        {
            await Snapshot(listing.ListingId).DeleteAsync();
            await Index(0).RemoveAsync(listing.ListingId);
            await Index(listing.ItemCode).RemoveAsync(listing.ListingId);
        }
        catch (Exception ex)
        {
            _logger.ZLogWarning(ex, $"거래소 목록 캐시 제거 실패(listingId {listing.ListingId:@ListingId}) — TTL·재적재가 흡수합니다.");
        }
    }

    // ── 구매 락 ──

    /// <summary>
    /// 등록 단위 락을 SET NX + TTL로 시도한다(§7.4). 경합이면 짧게 재시도하고, 그래도 실패하면
    /// Acquired=false(호출측이 TradeBusy로 응답). Redis 장애면 Degraded=true로 <b>락 없이 진행</b>한다.
    /// </summary>
    public Task<TradeLockHandle> AcquireLockAsync(long listingId) => AcquireAsync(Lock(listingId));

    /// <summary>
    /// 판매자 단위 락을 시도한다(판매 등록 전용). 등록은 아직 listingId가 없어 등록 단위 락을 쓸 수 없고,
    /// 경합 대상이 <b>그 계정의 동시 등록 수</b>라서 판매자 키로 잡는다 — 같은 계정의 동시 등록을 직렬화해
    /// "한도 검사와 삽입 사이에 다른 요청이 끼어들어 한도를 넘기는" 경합을 막는다.
    /// <para><b>보증이 아니라 완화다.</b> Redis 장애 시에는 락 없이 진행하므로(축소 운전) 경합이 다시 가능해진다.
    /// 초과 결과가 무해(한도 10건이 11건이 되는 정도)하고 그 대가로 가용성을 지키는 선택이다(§7.4).</para>
    /// </summary>
    public Task<TradeLockHandle> AcquireSellerLockAsync(long userId) => AcquireAsync(SellerLock(userId));

    /// <summary>락 키 하나에 대해 SET NX + TTL 획득을 재시도하며 시도한다(등록 단위·판매자 단위 공용).</summary>
    private async Task<TradeLockHandle> AcquireAsync(RedisString<string> key)
    {
        var token = Guid.NewGuid().ToString("N");
        try
        {
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
            _logger.ZLogWarning(ex, $"거래소 락 사용 불가(key {key.Key.ToString():@LockKey}) — 락 없이 진행합니다(축소 운전).");
            return new TradeLockHandle(false, true, token);
        }
    }

    /// <summary>
    /// 락을 해제한다. <b>값이 자기 토큰일 때만</b> 지운다 — TTL이 먼저 만료돼 다른 요청이 같은 키를 잡았을 수 있어,
    /// 값 비교 없이 지우면 남의 락을 해제하게 된다(§7.4).
    /// </summary>
    public Task ReleaseLockAsync(long listingId, TradeLockHandle handle)
        => ReleaseAsync(Lock(listingId), handle);

    /// <summary>판매자 단위 락(판매 등록)을 해제한다. 규약은 등록 단위 락과 동일하다.</summary>
    public Task ReleaseSellerLockAsync(long userId, TradeLockHandle handle)
        => ReleaseAsync(SellerLock(userId), handle);

    /// <summary>락 키 하나를 자기 토큰일 때만 지운다(등록 단위·판매자 단위 공용).</summary>
    private async Task ReleaseAsync(RedisString<string> key, TradeLockHandle handle)
    {
        if (!handle.Acquired)
        {
            return;
        }

        try
        {
            var current = await key.GetAsync();
            if (current.HasValue && current.Value == handle.Token)
            {
                await key.DeleteAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.ZLogWarning(ex, $"거래소 락 해제 실패(key {key.Key.ToString():@LockKey}) — TTL로 자연 만료됩니다.");
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

    /// <summary>판매 등록이 쓰는 판매자 단위 락(동시 등록 한도 검사를 직렬화).</summary>
    private RedisString<string> SellerLock(long userId) => new(_redis, $"trade:lock:seller:{userId}", null);

    /// <summary>
    /// 스냅샷 TTL — <b>등록 만료 시각까지</b>(trade 기획서 §7.3). 등록이 만료되면 스냅샷도 함께 사라지므로
    /// 색인에 남은 id는 결손으로 감지되어 다음 조회가 전량을 다시 읽는다.
    /// <para>이미 만료 시각을 지난 등록(만료 배치가 아직 닫지 않은 구간)은 최소값 1초로 둔다 — 0 이하를 넘기면
    /// TTL 없이 영구 보관되어 유령 항목이 남는다.</para>
    /// </summary>
    private static TimeSpan SnapshotTtl(long expiresAt, long nowUnix)
        => TimeSpan.FromSeconds(Math.Max(1, expiresAt - nowUnix));
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
    public long ExpiresAt { get; set; }

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
        ExpiresAt = s.ExpiresAt,
    };

    /// <summary>캐시 엔트리 → 리포지토리 스냅샷.</summary>
    public TradeListingSnapshot ToSnapshot()
        => new(ListingId, SellerUserId, ItemCode, EnhanceLevel, Quantity, Price, CreatedAt, ExpiresAt);
}
