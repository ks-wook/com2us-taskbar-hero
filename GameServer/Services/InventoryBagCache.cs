using CloudStructures;
using CloudStructures.Structures;
using TaskbarHero.Common.Dto;
using ZLogger;

namespace GameServer.Services;

/// <summary>
/// 가방 조회 캐시(inventory-item-cube 기획서 §6.5). 키 <c>inv:bag:{userId}</c>에 그 계정의 가방 아이템
/// <b>전량 스냅샷</b>(slot 오름차순)을 담아 <c>POST /api/game/inventory/list</c>의 읽기를 흡수한다.
/// <para><b>정합 원칙 — "캐시는 MySQL과 같거나, 없다."</b> 정본은 항상 MySQL이고 캐시는 파생 데이터다.
/// 부분 갱신 상태를 남기지 않으며, 갱신에 실패하면 키를 삭제해 다음 조회가 MySQL에서 재적재하게 한다.</para>
/// <para><b>write-through</b>다 — 가방을 바꾸는 액션이 커밋한 뒤 <see cref="ApplyAsync"/>로 변경분을 캐시에도
/// 반영한다. 변경 시 삭제(write-invalidate)하지 않는 이유는 자동 전투 전리품이 계속 적재되어 캐시가 거의
/// 항상 무효가 되기 때문이다(적중률 0 수렴).</para>
/// 모든 Redis 호출은 실패해도 예외를 밖으로 던지지 않는다. 장애 시 조회는 MySQL 폴백, 갱신은 키 삭제로 흡수한다.
/// </summary>
public sealed class InventoryBagCache
{
    /// <summary>
    /// 스냅샷 TTL. 갱신·삭제가 전부 실패해 오염을 감지조차 못 하는 경우의 안전망이다.
    /// 이 시간이 지나면 스냅샷이 사라져 다음 조회가 MySQL에서 전량을 다시 읽는다.
    /// </summary>
    private static readonly TimeSpan SnapshotTtl = TimeSpan.FromMinutes(30);

    private readonly RedisConnection _redis;
    private readonly ILogger<InventoryBagCache> _logger;

    /// <summary>Redis 연결·로거를 주입받는다.</summary>
    public InventoryBagCache(RedisConnection redis, ILogger<InventoryBagCache> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    /// <summary>
    /// 캐시에서 그 계정의 가방 <b>전량</b>을 slot 오름차순으로 읽는다. 미적재이거나 Redis 장애면 null을 돌려
    /// 호출측이 MySQL로 폴백하게 한다(부분 캐시로 잘린 목록을 응답하지 않는다).
    /// </summary>
    public async Task<IReadOnlyList<InventoryItemDto>?> TryGetAsync(long userId)
    {
        try
        {
            var entry = await Bag(userId).GetAsync();
            if (!entry.HasValue || entry.Value is null)
            {
                return null;
            }

            return entry.Value.ToItems();
        }
        catch (Exception ex)
        {
            _logger.ZLogWarning(ex, $"가방 캐시 조회 실패(userId {userId:@UserId}) — MySQL로 폴백합니다.");
            return null;
        }
    }

    /// <summary>
    /// MySQL에서 읽은 <b>완전 집합</b>을 캐시에 채운다(lazy 적재). 실패는 무시한다(다음 조회가 다시 시도).
    /// <para><b>부분 집합을 넘기지 말 것.</b> 페이지 단위로 채우면 잘린 목록이 완전한 캐시처럼 보여
    /// 나머지 아이템을 영구히 가린다 — 호출측은 페이징하지 않은 전량 조회 결과만 넘긴다.</para>
    /// </summary>
    public async Task FillAsync(long userId, IReadOnlyList<InventoryItemDto> items)
    {
        try
        {
            await Bag(userId).SetAsync(BagCacheEntry.From(items), SnapshotTtl);
        }
        catch (Exception ex)
        {
            _logger.ZLogWarning(ex, $"가방 캐시 적재 실패(userId {userId:@UserId}) — 캐시 없이 계속합니다.");
        }
    }

    /// <summary>
    /// 액션이 커밋한 변경분을 캐시에 반영한다(write-through). <c>removed</c> → <c>upserted</c> 순으로
    /// itemId를 키 삼아 적용하므로 같은 델타를 두 번 적용해도 결과가 같다(멱등).
    /// <para><b>캐시가 없으면 만들지 않고 건너뛴다.</b> 변경분만 담긴 스냅샷은 "아이템 몇 개짜리 완전한 가방"처럼
    /// 보여 나머지를 영영 가린다(부분 적재 문제). 건너뛰면 다음 조회가 MySQL에서 전량을 읽어 채운다.</para>
    /// <para>적용 도중 실패하면 반쯤 갱신된 스냅샷을 남기지 않도록 키를 <b>삭제</b>한다.</para>
    /// </summary>
    public async Task ApplyAsync(long userId, InventoryDeltaDto? delta)
    {
        if (delta is null || (delta.upserted.Count == 0 && delta.removed.Count == 0))
        {
            return;
        }

        try
        {
            var bag = Bag(userId);
            var entry = await bag.GetAsync();
            if (!entry.HasValue || entry.Value is null)
            {
                return; // 미적재 — 부분 스냅샷을 만들지 않는다.
            }

            var byId = entry.Value.ToItems().ToDictionary(i => i.itemId);

            foreach (var itemId in delta.removed)
            {
                byId.Remove(itemId);
            }

            foreach (var item in delta.upserted)
            {
                byId[item.itemId] = item;
            }

            var merged = byId.Values.OrderBy(i => i.slot).ToList();
            await bag.SetAsync(BagCacheEntry.From(merged), SnapshotTtl);
        }
        catch (Exception ex)
        {
            _logger.ZLogWarning(ex, $"가방 캐시 갱신 실패(userId {userId:@UserId}) — 스냅샷을 삭제해 다음 조회가 재적재하게 합니다.");
            await RemoveAsync(userId);
        }
    }

    /// <summary>
    /// 스냅샷을 무효화한다. 델타를 만들지 않는 경로가 가방을 바꿨거나, 갱신이 실패했을 때 호출한다.
    /// 실패해도 TTL이 흡수하므로 예외를 던지지 않는다.
    /// </summary>
    public async Task RemoveAsync(long userId)
    {
        try
        {
            await Bag(userId).DeleteAsync();
        }
        catch (Exception ex)
        {
            _logger.ZLogWarning(ex, $"가방 캐시 삭제 실패(userId {userId:@UserId}) — TTL로 자연 만료됩니다.");
        }
    }

    /// <summary>계정별 가방 전량 스냅샷 키.</summary>
    private RedisString<BagCacheEntry> Bag(long userId) => new(_redis, $"inv:bag:{userId}", null);
}

/// <summary>
/// 캐시에 담는 가방 스냅샷(직렬화 대상). CloudStructures 기본 직렬화를 쓰므로 공개 프로퍼티만 둔다
/// (공유 DTO <see cref="InventoryItemDto"/>는 Unity JsonUtility 공유용 public 필드라 그대로 쓰지 않는다).
/// </summary>
public sealed class BagCacheEntry
{
    public List<BagItemEntry> Items { get; set; } = new List<BagItemEntry>();

    /// <summary>응답 DTO 목록 → 캐시 엔트리(slot 오름차순으로 정규화해 담는다).</summary>
    public static BagCacheEntry From(IReadOnlyList<InventoryItemDto> items) => new()
    {
        Items = items.OrderBy(i => i.slot).Select(i => new BagItemEntry
        {
            ItemId = i.itemId,
            Slot = i.slot,
            ItemCode = i.itemCode,
            Quantity = i.quantity,
            EnhanceLevel = i.enhanceLevel,
        }).ToList(),
    };

    /// <summary>캐시 엔트리 → 응답 DTO 목록(slot 오름차순).</summary>
    public List<InventoryItemDto> ToItems() => Items
        .OrderBy(i => i.Slot)
        .Select(i => new InventoryItemDto
        {
            itemId = i.ItemId,
            slot = i.Slot,
            itemCode = i.ItemCode,
            quantity = i.Quantity,
            enhanceLevel = i.EnhanceLevel,
        })
        .ToList();
}

/// <summary>캐시 스냅샷의 가방 행 1개(직렬화 대상이라 공개 프로퍼티).</summary>
public sealed class BagItemEntry
{
    public long ItemId { get; set; }
    public int Slot { get; set; }
    public int ItemCode { get; set; }
    public long Quantity { get; set; }
    public int EnhanceLevel { get; set; }
}
