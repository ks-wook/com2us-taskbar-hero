using GameServer.MasterData;
using GameServer.Repositories;
using GameServer.Repositories.Interfaces;
using TaskbarHero.Common;
using TaskbarHero.Common.Dto;
using ZLogger;

namespace GameServer.Services;

public interface IGachaService
{
    Task<SaveResult> GetBannersAsync(long userId);
    Task<SaveResult> PullAsync(long userId, int gachaCode, int pullType);
    Task<SaveResult> GetHistoryAsync(long userId, int gachaCode, long cursor, int limit);
}

/// <summary>
/// 가챠(뽑기) 처리(가챠 기획서 §5·6). 배너 노출 판정·등급/아이템 추첨·천장·10연 보장은 전부 서버가 확정하며
/// (서버 권위), 상태 변경은 리포지토리 트랜잭션으로 원자적으로 반영한다.
/// </summary>
public sealed class GachaService : IGachaService
{
    /// <summary>기록 조회 기본 페이지 크기(뽑기 건수).</summary>
    private const int HistoryDefaultLimit = 20;

    /// <summary>기록 조회 페이지 크기 상한. 과대 응답 방어를 위해 서버가 강제한다(기획서 §5.4).</summary>
    private const int HistoryMaxLimit = 50;

    private readonly IGachaRepository _gachaRepository;
    private readonly MasterDataProvider _masterData;
    private readonly ILogger<GachaService> _logger;

    /// <summary>의존성(가챠 리포지토리·마스터 데이터·가방 조회 캐시·로거)을 주입받는다.</summary>
    public GachaService(
        IGachaRepository gachaRepository, MasterDataProvider masterData,
        ILogger<GachaService> logger)
    {
        _gachaRepository = gachaRepository;
        _masterData = masterData;
        _logger = logger;
    }

    /// <summary>
    /// 지금 돌릴 수 있는 배너 목록을 반환한다(§5.1). 노출 판정은 서버 시각 기준이며(§6.1), 각 배너의
    /// 천장 진행도를 함께 담아 가챠 화면을 왕복 한 번으로 그리게 한다. 열려 있는 배너가 없으면 빈 목록(에러 아님).
    /// 이름·비용·확률은 클라이언트 번들 마스터에 있으므로 응답에 넣지 않는다.
    /// </summary>
    public async Task<SaveResult> GetBannersAsync(long userId)
    {
        if (!_masterData.IsLoaded)
        {
            return new SaveResult(ErrorCode.MasterDataNotLoaded, string.Empty, null);
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var banners = new List<GachaBannerDto>();

        foreach (var banner in _masterData.OpenGachaBanners(now))
        {
            var counts = await _gachaRepository.LoadCountersAsync(userId, banner.GachaCode, banner.PityGrades);
            banners.Add(new GachaBannerDto
            {
                gachaCode = banner.GachaCode,
                sortOrder = banner.SortOrder,
                openAt = banner.OpenAt,
                closeAt = banner.CloseAt,
                counters = ToCounterDtos(banner, counts),
            });
        }

        var data = new GachaBannerListResultData { serverTime = now, banners = banners };
        return new SaveResult(ErrorCode.Success, "OK", data);
    }

    /// <summary>
    /// 1연·10연 뽑기를 처리한다(§5.2). 마스터 로드·요청 상품 종류·배너 존재·노출을 확인한 뒤, 리포지토리 트랜잭션
    /// 안에서 비용 차감 → 회차별 추첨(천장 보정 포함) → 10연 보장 대체 → 지급 → 카운터 갱신 → 원장 적재를 원자적으로
    /// 반영한다.
    /// <para><paramref name="pullType"/>은 <b>어떤 상품을 사는가</b>일 뿐이고 <b>뽑는 횟수는 마스터
    /// (gacha_master.multi_count)에서 서버가 읽는다</b> — 확률·결과와 함께 서버 소유 값이라 요청에 횟수 필드를 두지 않는다.
    /// 정의되지 않은 값(1·2 외)은 InvalidRequest로 거부한다.</para>
    /// </summary>
    public async Task<SaveResult> PullAsync(long userId, int gachaCode, int pullType)
    {
        if (!_masterData.IsLoaded)
        {
            return new SaveResult(ErrorCode.MasterDataNotLoaded, string.Empty, null);
        }

        // 상품 종류는 공유 enum이 정의한 값만 받는다(신규 에러 코드를 만들지 않고 InvalidRequest를 재사용).
        if (pullType != (int)GachaPullType.Single && pullType != (int)GachaPullType.Multi)
        {
            return new SaveResult(ErrorCode.InvalidRequest, string.Empty, null);
        }

        var banner = _masterData.GetGacha(gachaCode);
        if (banner is null)
        {
            return new SaveResult(ErrorCode.GachaNotFound, string.Empty, null);
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // 목록 조회 시점에 열려 있었다는 사실이 뽑는 시점의 허가가 되지 않는다 — 비용 차감 전에 다시 판정한다(§6.1).
        if (!banner.IsOpenAt(now))
        {
            return new SaveResult(ErrorCode.GachaNotAvailable, string.Empty, null);
        }

        bool multi = pullType == (int)GachaPullType.Multi;
        long cost = multi ? banner.CostMulti : banner.CostSingle;
        int drawCount = multi ? banner.MultiCount : 1;

        var outcome = await _gachaRepository.ApplyPullAsync(
            userId, banner, pullType, cost,
            counters => RollAll(banner, drawCount, multi, counters),
            now);

        switch (outcome.Status)
        {
            case GachaPullStatus.InsufficientCurrency:
                return new SaveResult(ErrorCode.InsufficientCurrency, string.Empty, null);
            case GachaPullStatus.InventoryFull:
                return new SaveResult(ErrorCode.InventoryFull, string.Empty, null);
            case GachaPullStatus.PoolEmpty:
                // 플레이어 실수가 아니라 마스터 데이터 결함이므로 Error로 남긴다(기획서 §6.8).
                _logger.ZLogError($"가챠 후보 풀 없음(전체 롤백): {gachaCode:@GachaCode} pullType {pullType:@PullType}");
                return new SaveResult(ErrorCode.GachaPoolEmpty, string.Empty, null);
        }

        var data = new GachaPullResultData
        {
            gachaCode = banner.GachaCode,
            pullId = outcome.PullId,
            pullType = pullType,
            pulledAt = outcome.PulledAt,
            results = outcome.Entries.Select(e => new GachaResultItemDto
            {
                seq = e.Seq,
                grade = e.Grade,
                itemCode = e.ItemCode,
                quantity = e.Quantity,
                isPity = e.PityApplied,
                isGuaranteed = e.Guaranteed,
            }).ToList(),
            cost = new CurrencyDto { currencyType = banner.CostCurrencyCode, amount = outcome.CostAmount },
            balance = new List<CurrencyDto>
            {
                new CurrencyDto { currencyType = banner.CostCurrencyCode, amount = outcome.Balance },
            },
            counters = ToCounterDtos(banner, outcome.Counters),
            inventoryDelta = outcome.Delta,
        };

        return new SaveResult(ErrorCode.Success, "GachaPulled", data);
    }

    /// <summary>
    /// 뽑기 기록을 최신순 커서 페이징으로 조회한다(§5.4). 페이징 단위는 뽑기 요청(1연=1건, 10연=1건)이며
    /// 각 건이 회차별 결과를 품는다. limit은 1~50으로 clamp하고 잘못된 값은 에러가 아니라 보정 대상이다.
    /// 기록이 없으면 빈 목록으로 성공 응답한다.
    /// </summary>
    public async Task<SaveResult> GetHistoryAsync(long userId, int gachaCode, long cursor, int limit)
    {
        int size = limit <= 0 ? HistoryDefaultLimit : Math.Min(limit, HistoryMaxLimit);
        long safeCursor = cursor < 0 ? 0 : cursor;

        var page = await _gachaRepository.GetHistoryAsync(userId, Math.Max(gachaCode, 0), safeCursor, size);

        var data = new GachaHistoryResultData
        {
            pulls = page.Entries.Select(e => new GachaHistoryEntryDto
            {
                pullId = e.PullId,
                gachaCode = e.GachaCode,
                pullType = e.PullType,
                cost = new CurrencyDto { currencyType = e.CostCurrencyCode, amount = e.CostAmount },
                pulledAt = e.PulledAt,
                items = e.Items.Select(i => new GachaHistoryItemDto
                {
                    seq = i.Seq,
                    itemCode = i.ItemCode,
                    grade = i.Grade,
                    quantity = i.Quantity,
                    isPity = i.PityApplied,
                    isGuaranteed = i.Guaranteed,
                }).ToList(),
            }).ToList(),
            nextCursor = page.NextCursor,
            hasMore = page.HasMore,
        };

        return new SaveResult(ErrorCode.Success, "OK", data);
    }

    /// <summary>
    /// 회차별 추첨을 수행하는 트랜잭션 델리게이트(§6.2~6.4). 회차마다 천장 카운터를 평가·갱신하며
    /// (그래서 10연 3회차에 하드 천장이 터지면 그 자리에서 리셋된다), 10연이면 마지막에 보장 판정을 한다.
    /// 후보 풀이 비면 null을 반환해 호출측이 전체 롤백하게 한다.
    /// </summary>
    private IReadOnlyList<GachaPullEntry>? RollAll(
        GachaBannerDef banner, int drawCount, bool multi, IDictionary<int, int> counters)
    {
        var snapshot = new Dictionary<int, int>(counters);
        var entries = new List<GachaPullEntry>(drawCount);

        for (var seq = 1; seq <= drawCount; seq++)
        {
            var roll = _masterData.RollGacha(banner, snapshot);
            if (roll is null)
            {
                return null;
            }

            entries.Add(new GachaPullEntry(seq, roll.Grade, roll.ItemCode, roll.Quantity, roll.PityApplied, false));
            ApplyCounters(banner, snapshot, roll.Grade);
        }

        // 10연 보장: 보장 등급 이상이 하나도 없으면 마지막 회차를 보장 등급으로 대체한다(원래 결과는 버린다).
        int guaranteedGrade = banner.MultiGuaranteedGrade;
        if (multi && guaranteedGrade > 0 && entries.All(e => e.Grade < guaranteedGrade))
        {
            var replacement = _masterData.RollGuaranteed(banner, guaranteedGrade);
            if (replacement is null)
            {
                return null;
            }

            entries[^1] = new GachaPullEntry(
                drawCount, replacement.Grade, replacement.ItemCode, replacement.Quantity, false, true);

            // 대체된 등급으로 카운터를 다시 평가한다(마지막 회차의 원래 결과 반영을 되돌린 뒤 재적용).
            snapshot = new Dictionary<int, int>(counters);
            foreach (var entry in entries)
            {
                ApplyCounters(banner, snapshot, entry.Grade);
            }
        }

        foreach (var (grade, count) in snapshot)
        {
            counters[grade] = count;
        }

        return entries;
    }

    /// <summary>
    /// 한 회차 결과를 천장 카운터에 반영한다(§6.3). 등급 g의 카운터는 <b>g 이상</b>을 받으면 0으로 리셋하고
    /// 그렇지 않으면 1 증가한다 — 상위 등급을 받았는데 하위 등급 천장이 계속 쌓이면 같은 행운에 두 번 보상하게 된다.
    /// </summary>
    private static void ApplyCounters(GachaBannerDef banner, IDictionary<int, int> counters, int resultGrade)
    {
        foreach (var grade in banner.PityGrades)
        {
            counters[grade] = resultGrade >= grade ? 0 : (counters.TryGetValue(grade, out var c) ? c : 0) + 1;
        }
    }

    /// <summary>
    /// 천장 진행도를 응답 DTO로 변환한다. pityThreshold는 하드 천장 발동 회차(없으면 0)다.
    /// remainingToPity("천장까지 몇 회 남았는가")는 클라이언트가 뺄셈하지 않도록 서버가 계산해 담는다 —
    /// 천장 규칙이 없거나(threshold 0) 카운터가 기준을 넘어선 경우 모두 0으로 내려 음수가 나가지 않게 한다.
    /// </summary>
    private static List<GachaPityCounterDto> ToCounterDtos(
        GachaBannerDef banner, IReadOnlyDictionary<int, int> counters)
        => banner.PityGrades.Select(grade =>
        {
            int pityCount = counters.TryGetValue(grade, out var c) ? c : 0;
            int threshold = banner.HardThreshold(grade);
            return new GachaPityCounterDto
            {
                grade = grade,
                pityCount = pityCount,
                pityThreshold = threshold,
                remainingToPity = threshold <= 0 ? 0 : Math.Max(threshold - pityCount, 0),
            };
        }).ToList();

}
