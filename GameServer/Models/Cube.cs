namespace GameServer.Models;

// CubeRepository 전용 DB 행 매핑용 POCO(제네릭 매핑 전용, dynamic 금지).
// Dapper.MatchNamesWithUnderscores=true(Program.cs)로 snake_case 컬럼 → PascalCase 프로퍼티 매핑.

class PlayerItemBriefRow
{
    public long PlayerItemId { get; set; }
    public int ItemCode { get; set; }
    public int RowType { get; set; }
    public long Quantity { get; set; }
    public int? Slot { get; set; }
}

class CubeStateRow
{
    public int CubeLevel { get; set; }
    public long CubeExp { get; set; }
}

// ── 마스터 데이터 정의(불변). Repositories/MasterDb 가 적재하고 MasterDbProvider 가 조회한다. ──
/// <summary>큐브 레벨별 규칙(cube_master). 합성 소모 개수·등급 상승 허용·분해 골드 계수·다음 레벨 요구 경험치.</summary>
public sealed record CubeRule(int Level, long RequiredExp, int CombineGradeUp, int CombineCount, long GoldPerScrap);
/// <summary>큐브 제작 레시피 소모 재료 1행(cube_recipe_ingredient).</summary>
public sealed record RecipeIngredient(int MaterialCode, int Quantity);
/// <summary>큐브 제작 레시피(cube_recipe + 자식 재료). 결과 아이템·요구 큐브 레벨·비용·소모 재료 목록.</summary>
public sealed record RecipeDef(
    int RecipeCode, int ResultItemCode, int ResultQuantity, int ReqCubeLevel, long CostGold,
    IReadOnlyList<RecipeIngredient> Ingredients);

// ── 마스터 적재용 DB 행 매핑 POCO(제네릭 매핑 전용, dynamic 금지). snake_case→PascalCase는 Dapper 규칙으로 매핑.
//    DECIMAL 컬럼은 decimal로 받아 float/double로 캐스팅한다. 적재는 Repositories/MasterDb/MasterDbLoader. ──
class CubeMasterRow
{
    public int CubeLevel { get; set; }
    public long RequiredExp { get; set; }
    public int CombineGradeUp { get; set; }
    public int CombineCount { get; set; }
    public long GoldPerScrap { get; set; }
}
class CubeRecipeRow
{
    public int RecipeCode { get; set; }
    public int ResultItemCode { get; set; }
    public int ResultQuantity { get; set; }
    public int ReqCubeLevel { get; set; }
    public long CostGold { get; set; }
}
class CubeRecipeIngredientRow
{
    public int RecipeCode { get; set; }
    public int MaterialCode { get; set; }
    public int Quantity { get; set; }
}
