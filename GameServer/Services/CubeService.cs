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
/// 큐브(합성/분해/제작) 액션 처리(inventory-item-cube 기획서 §5.6·5.7·5.8). 결과·비용·보상은 서버가 마스터 데이터로
/// 확정하며(서버 권위), 상태 변경은 리포지토리 트랜잭션으로 원자적으로 반영한다. 큐브는 연산마다 경험치가 쌓여 레벨업한다.
/// </summary>
public sealed class CubeService : ICubeService
{
    private const int CombineExpPerGrade = 50;   // 합성: 입력 등급 × 이 값 = 획득 큐브 경험치
    private const int DismantleExpPerGrade = 20;  // 분해: 아이템당 등급 × 이 값 × 개수
    private const int CraftExp = 20;              // 제작: 1회 고정 획득 경험치
    private const int GoldCurrencyType = 1;       // 응답 balance의 재화 종류(골드)

    private readonly ICubeRepository _cubeRepository;
    private readonly MasterDbProvider _masterData;
    private readonly ILogger<CubeService> _logger;

    /// <summary>의존성(큐브 리포지토리·마스터 데이터·가방 조회 캐시·로거)을 주입받는다.</summary>
    public CubeService(
        ICubeRepository cubeRepository, MasterDbProvider masterData,
        ILogger<CubeService> logger)
    {
        _cubeRepository = cubeRepository;
        _masterData = masterData;
        _logger = logger;
    }

    /// <summary>
    /// 합성을 처리한다. 마스터 로드를 확인하고, 리포지토리 트랜잭션 안에서 입력 장비가 같은 등급이며(슬롯·클래스는 서로
    /// 달라도 됨) 큐브 규칙의 소모 개수와 일치하는지 검증한 뒤, 상위 등급 장비 1개를 서버 랜덤으로 생성한다(클래스·슬롯 무관).
    /// </summary>
    public async Task<SaveResult> CombineAsync(long userId, IReadOnlyList<long> itemIds)
    {
        if (!_masterData.IsLoaded)
        {
            return new SaveResult(ErrorCode.MasterDataNotLoaded, string.Empty, null);
        }

        var ids = itemIds ?? new List<long>();
        // 빈 목록·중복 id는 조건 미충족으로 거부(개수 정합은 큐브 규칙과 트랜잭션 내부에서 재검증).
        if (ids.Count == 0 || ids.Distinct().Count() != ids.Count)
        {
            return new SaveResult(ErrorCode.CubeRecipeNotMet, string.Empty, null);
        }

        var now = DateTimeUtil.NowUnixSeconds();
        var outcome = await _cubeRepository.ApplyCombineAsync(userId, ids, DecideCombine, now);

        switch (outcome.Status)
        {
            case CombineStatus.ItemNotFound:
                return new SaveResult(ErrorCode.ItemNotFound, string.Empty, null);
            case CombineStatus.ItemEquipped:
                return new SaveResult(ErrorCode.ItemEquipped, string.Empty, null);
            case CombineStatus.RecipeNotMet:
                return new SaveResult(ErrorCode.CubeRecipeNotMet, string.Empty, null);
        }

        var data = new CubeCombineResultData
        {
            consumed = ids.ToList(),
            result = new CombineResultDto
            {
                itemId = outcome.ResultItemId,
                itemCode = outcome.ResultItemCode,
                grade = outcome.ResultGrade,
            },
            cube = new CubeDto { cubeLevel = outcome.CubeLevel, cubeExp = outcome.CubeExp },
            inventoryDelta = outcome.Delta,
        };

        _logger.ZLogInformation($"큐브 합성: userId {userId:@UserId}, consumed {ids.Count:@Count}, resultItemCode {outcome.ResultItemCode:@ResultCode}, grade {outcome.ResultGrade:@Grade}");
        return new SaveResult(ErrorCode.Success, "Combined", data);
    }

    /// <summary>
    /// 분해를 처리한다. 마스터 로드를 확인하고, 리포지토리 트랜잭션 안에서 각 아이템의 소유·미장착·수량을 검증한 뒤
    /// 등급 비례 골드(gold_per_scrap × 등급 × 개수)를 적립하고 아이템을 차감한다. 획득 골드·큐브 경험치를 반환한다.
    /// </summary>
    public async Task<SaveResult> DismantleAsync(long userId, IReadOnlyList<CubeDismantleItemDto> items)
    {
        if (!_masterData.IsLoaded)
        {
            return new SaveResult(ErrorCode.MasterDataNotLoaded, string.Empty, null);
        }

        var list = items ?? new List<CubeDismantleItemDto>();
        var pairs = list.Select(i => (i.itemId, i.count)).ToList();
        // 빈 목록·중복 id는 잘못된 요청으로 거부.
        if (pairs.Count == 0 || pairs.Select(p => p.itemId).Distinct().Count() != pairs.Count)
        {
            return new SaveResult(ErrorCode.ItemNotFound, string.Empty, null);
        }

        var now = DateTimeUtil.NowUnixSeconds();
        var outcome = await _cubeRepository.ApplyDismantleAsync(userId, pairs, ComputeDismantleReward, now);

        switch (outcome.Status)
        {
            case DismantleStatus.ItemNotFound:
                return new SaveResult(ErrorCode.ItemNotFound, string.Empty, null);
            case DismantleStatus.ItemEquipped:
                return new SaveResult(ErrorCode.ItemEquipped, string.Empty, null);
            case DismantleStatus.InsufficientQuantity:
                return new SaveResult(ErrorCode.InsufficientQuantity, string.Empty, null);
        }

        var data = new CubeDismantleResultData
        {
            gold = outcome.Gold,
            cubeExp = outcome.CubeExp,
            cube = new CubeDto { cubeLevel = outcome.NewCubeLevel, cubeExp = outcome.NewCubeExp },
            balance = new List<CurrencyDto>
            {
                new CurrencyDto { currencyType = GoldCurrencyType, amount = outcome.GoldBalance },
            },
            inventoryDelta = outcome.Delta,
        };

        _logger.ZLogInformation($"큐브 분해: userId {userId:@UserId}, items {pairs.Count:@Count}, gold {outcome.Gold:@Gold}, cubeExp {outcome.CubeExp:@CubeExp}");
        return new SaveResult(ErrorCode.Success, "Dismantled", data);
    }

    /// <summary>
    /// 제작을 처리한다. 마스터 로드·레시피 존재를 확인하고, 리포지토리 트랜잭션 안에서 큐브 레벨·골드·재료를 검증한 뒤
    /// 골드·재료를 차감하고 결과 아이템을 지급한다. 소모 재료·획득 아이템·갱신된 큐브 상태를 반환한다.
    /// </summary>
    public async Task<SaveResult> CraftAsync(long userId, int recipeCode)
    {
        if (!_masterData.IsLoaded)
        {
            return new SaveResult(ErrorCode.MasterDataNotLoaded, string.Empty, null);
        }

        var recipe = _masterData.GetRecipe(recipeCode);
        if (recipe is null)
        {
            return new SaveResult(ErrorCode.CubeRecipeNotMet, string.Empty, null);
        }

        var resultItem = _masterData.GetItem(recipe.ResultItemCode);
        if (resultItem is null)
        {
            return new SaveResult(ErrorCode.CubeRecipeNotMet, string.Empty, null);
        }

        var now = DateTimeUtil.NowUnixSeconds();
        var outcome = await _cubeRepository.ApplyCraftAsync(
            userId, recipe, resultItem.ItemType, resultItem.StackMax, CraftExp, now);

        switch (outcome.Status)
        {
            case CraftStatus.CubeLevelInsufficient:
                return new SaveResult(ErrorCode.CubeLevelInsufficient, string.Empty, null);
            case CraftStatus.InsufficientCurrency:
                return new SaveResult(ErrorCode.InsufficientCurrency, string.Empty, null);
            case CraftStatus.RecipeNotMet:
                return new SaveResult(ErrorCode.CubeRecipeNotMet, string.Empty, null);
            case CraftStatus.InventoryFull:
                return new SaveResult(ErrorCode.InventoryFull, string.Empty, null);
        }

        var data = new CubeCraftResultData
        {
            consumed = recipe.Ingredients
                .Select(g => new ItemQuantityDto { itemCode = g.MaterialCode, quantity = g.Quantity })
                .ToList(),
            gained = new CubeCraftGainedDto
            {
                items = new List<ItemQuantityDto>
                {
                    new ItemQuantityDto { itemCode = recipe.ResultItemCode, quantity = recipe.ResultQuantity },
                },
            },
            cube = new CubeDto { cubeLevel = outcome.CubeLevel, cubeExp = outcome.CubeExp },
            balance = new List<CurrencyDto>
            {
                new CurrencyDto { currencyType = GoldCurrencyType, amount = outcome.GoldBalance },
            },
            inventoryDelta = outcome.Delta,
        };

        _logger.ZLogInformation($"큐브 제작: userId {userId:@UserId}, recipeCode {recipeCode:@RecipeCode}, resultItemCode {recipe.ResultItemCode:@ResultCode} x{recipe.ResultQuantity:@Quantity}");
        return new SaveResult(ErrorCode.Success, "Crafted", data);
    }

    /// <summary>
    /// 합성 판정(리포지토리 트랜잭션 델리게이트): 큐브 규칙(등급 상승 허용·소모 개수)과 입력 장비의 등급 일치(슬롯·클래스는
    /// 서로 달라도 됨)를 마스터로 검증하고, 통과 시 상위 등급 결과 아이템(클래스·슬롯 무관)을 서버 랜덤으로 선정한다.
    /// </summary>
    private CombineDecision DecideCombine(int cubeLevel, IReadOnlyList<CombineInput> inputs)
    {
        var rule = _masterData.GetCubeRule(cubeLevel);
        if (rule is null || rule.CombineGradeUp != 1 || inputs.Count != rule.CombineCount)
        {
            return CombineDecision.Reject();
        }

        var defs = inputs.Select(i => _masterData.GetItem(i.ItemCode)).ToList();
        if (defs.Any(d => d is null || d.ItemType != 1))
        {
            return CombineDecision.Reject();
        }

        var first = defs[0]!;
        // 같은 등급이면 되고 슬롯·클래스는 서로 달라도 된다.
        if (defs.Any(d => d!.Grade != first.Grade))
        {
            return CombineDecision.Reject();
        }

        var resultCode = _masterData.PickCombineResultCode(first.Grade);
        if (resultCode is null)
        {
            return CombineDecision.Reject();
        }

        return CombineDecision.Accept(resultCode.Value, first.Grade + 1, (long)CombineExpPerGrade * first.Grade);
    }

    /// <summary>분해 보상 산출(리포지토리 트랜잭션 델리게이트): 현재 큐브 레벨의 gold_per_scrap과 아이템 등급으로 골드·경험치 합계를 계산한다.</summary>
    private DismantleReward ComputeDismantleReward(int cubeLevel, IReadOnlyList<DismantleInput> inputs)
    {
        long rate = _masterData.GetCubeRule(cubeLevel)?.GoldPerScrap ?? 0;
        long gold = 0;
        long exp = 0;
        foreach (var input in inputs)
        {
            int grade = _masterData.GetItem(input.ItemCode)?.Grade ?? 1;
            gold += rate * grade * input.Count;
            exp += (long)DismantleExpPerGrade * grade * input.Count;
        }

        return new DismantleReward(gold, exp);
    }

}
