using GameServer.Data;
using SqlKata.Execution;
using TaskbarHero.Common.Dto;
using TaskbarHero.Common.MasterData;
using ZLogger;

namespace GameServer.MasterData;

/// <summary>스테이지 정의(구성). 스폰·보스·배경.</summary>
public sealed record StageDef(
    int StageId, int Act, int Difficulty, int Stage,
    int BossMonsterCode, int BackgroundType,
    IReadOnlyList<StageSpawnDto> Spawns);

/// <summary>스테이지 클리어 보상 정의. GradeProbs[i] = 등급 (i+1) 드롭 확률(길이 = 최대 등급, stage_reward_drop 기준).</summary>
public sealed record StageRewardDef(long Gold, long Exp, double[] GradeProbs);

/// <summary>드롭 추첨 결과(전리품 1개).</summary>
public sealed record DroppedItem(int ItemCode, long Quantity, int ItemType, int StackMax);

/// <summary>아이템 정의(item_master). 장착 검증(타입·슬롯·클래스·레벨)·드롭/스택·거래 검증에 사용한다.
/// Name은 서버가 만드는 문구(거래 메일 제목·본문 등)에 아이템을 사람이 읽는 이름으로 표기하기 위해 적재한다.</summary>
public sealed record ItemDef(
    int ItemCode, string Name, int ItemType, int Grade, int StackMax, int EquipSlot, int ClassReq, int LevelReq,
    int Sellable, long BasePrice);

/// <summary>
/// 소모품 버프 효과 정의(consumable_master). item_master의 소모품 행(item_type=4)과 1:1이다.
/// BuffValue는 획득량 배율(1.5 = 150%), DurationSec은 지속시간(초, 벽시계 경과 — 오프라인 중에도 소모).
/// </summary>
public sealed record ConsumableDef(int ItemCode, int BuffType, float BuffValue, int DurationSec);

/// <summary>
/// 장비 강화 단계별 규칙(enhance_master). 한 행 = "그 단계로 올릴 때의 비용"(Cost·CurrencyCode)과
/// "그 단계에 도달했을 때의 스탯 배율"(StatMultiplier)이다. 0단계(미강화)는 배율 1.0이라 행이 없고,
/// 정의된 최대 단계 다음이 없으면 강화 불가(MaxEnhanceReached)다.
/// </summary>
public sealed record EnhanceRule(int EnhanceLevel, long Cost, int CurrencyCode, float StatMultiplier);

/// <summary>스킬 정의(skill_master). 성장 검증(직업 소속·액티브/패시브·최대 레벨)에 사용한다. SkillType 1:액티브 2:패시브.</summary>
public sealed record SkillDef(int SkillCode, int ClassCode, int SkillType, int MaxLevel);

/// <summary>룬 정의(rune_master). 성장 검증(선행 룬·최대 레벨)에 사용한다. 레벨별 골드 비용은 rune_cost(자식)에서 조회한다.</summary>
public sealed record RuneDef(int RuneCode, int PrereqCode, int MaxLevel);

/// <summary>큐브 레벨별 규칙(cube_master). 합성 소모 개수·등급 상승 허용·분해 골드 계수·다음 레벨 요구 경험치.</summary>
public sealed record CubeRule(int Level, long RequiredExp, int CombineGradeUp, int CombineCount, long GoldPerScrap);

/// <summary>큐브 제작 레시피 소모 재료 1행(cube_recipe_ingredient).</summary>
public sealed record RecipeIngredient(int MaterialCode, int Quantity);

/// <summary>큐브 제작 레시피(cube_recipe + 자식 재료). 결과 아이템·요구 큐브 레벨·비용·소모 재료 목록.</summary>
public sealed record RecipeDef(
    int RecipeCode, int ResultItemCode, int ResultQuantity, int ReqCubeLevel, long CostGold,
    IReadOnlyList<RecipeIngredient> Ingredients);

/// <summary>출석부 일차별 보상 정의(attendance_master). Day는 날짜가 아니라 이번달 누적 출석 순번(1~30).
/// RewardType 1:골드 2:아이템 3:재료(메일 첨부와 동일 enum), 골드는 RewardCode 0.</summary>
public sealed record AttendanceRewardDef(int Day, int RewardType, int RewardCode, int Quantity);

/// <summary>메일 발급 문구 템플릿(mail_master). 발급 메일의 category·제목/본문 형식·만료 일수를 확정한다(mail 기획서 §4·§6.4).</summary>
public sealed record MailTemplateDef(int TemplateCode, int Category, string TitleFormat, string BodyFormat, int ValidDays);

/// <summary>신규 가입 지원금 첨부 1건(newbie_reward_master). 계정 초기화 시 발급하는 환영 메일에 그대로 담긴다.
/// RewardType 1:골드 2:아이템 3:재료(메일 첨부·출석 보상과 동일 enum), 골드는 RewardCode 0.</summary>
public sealed record NewbieRewardDef(int Seq, int RewardType, int RewardCode, long Quantity);

/// <summary>가챠 등급 슬롯의 지급 후보 1건(gacha_item_pool). Quantity는 1회 지급 수량.</summary>
public sealed record GachaPoolEntry(int ItemCode, int Quantity);

/// <summary>
/// 가챠 천장 규칙 1건(gacha_pity_rule). PityType 1:소프트(확률 가산) 2:하드(확정 지급).
/// Threshold는 <b>이번 뽑기의 회차 번호</b>(player_gacha_counter.pity_count + 1)와 비교한다 —
/// 누적 미획득 횟수와 직접 비교하면 한 회차 늦게 발동한다(가챠 기획서 §4.1·6.3).
/// <para><see cref="ProbStep"/>은 소프트 전용으로 <b>발동 후 회차 1번당 올릴 확률(%p, 0~1)</b>이다.
/// 하드 규칙은 0이다.</para>
/// </summary>
public sealed record GachaPityRule(int Grade, int PityType, int Threshold, double ProbStep);

/// <summary>
/// 가챠(뽑기) 배너 정의(gacha_master + 자식 3종). 한 행이 하나의 배너다.
/// <para><b>노출 조건</b>: IsActive=1 AND (OpenAt=0 or now&gt;=OpenAt) AND (CloseAt=0 or now&lt;CloseAt).
/// 판정은 서버 시각 기준이며 배너 조회·뽑기가 같은 조건을 쓴다(기획서 §6.1).</para>
/// <para><b>PickupItemCode</b>는 픽업 대상 <i>선언</i>이며 추첨식에 들어가지 않는다. 픽업 배너는 최고 등급 슬롯의
/// 후보를 그 아이템 1종으로 두므로, 균등 추첨이 그대로 확정을 만든다(기획서 §4.1). 0이면 상시 배너.</para>
/// </summary>
public sealed record GachaBannerDef(
    int GachaCode, string Name, int IsActive, long OpenAt, long CloseAt, int SortOrder,
    int CostCurrencyCode, long CostSingle, long CostMulti, int MultiCount, int MultiGuaranteedGrade,
    int PickupItemCode,
    IReadOnlyDictionary<int, int> GradeWeights,
    IReadOnlyDictionary<int, List<GachaPoolEntry>> PoolByGrade,
    IReadOnlyList<GachaPityRule> PityRules)
{
    /// <summary>지정 시각에 이 배너가 열려 있는지(노출 조건, 기획서 §6.1).</summary>
    public bool IsOpenAt(long nowUnix)
        => IsActive == 1
           && (OpenAt == 0 || nowUnix >= OpenAt)
           && (CloseAt == 0 || nowUnix < CloseAt);

    /// <summary>천장 규칙이 걸린 등급 목록(소프트·하드가 같은 등급에 있으면 한 번만). 오름차순.</summary>
    public IReadOnlyList<int> PityGrades => PityRules.Select(r => r.Grade).Distinct().OrderBy(g => g).ToList();

    /// <summary>등급의 하드 천장 발동 회차. 하드 규칙이 없으면 0(클라이언트 게이지 표시용).</summary>
    public int HardThreshold(int grade)
        => PityRules.FirstOrDefault(r => r.Grade == grade && r.PityType == GachaPityTypes.Hard)?.Threshold ?? 0;
}

/// <summary>gacha_pity_rule.pity_type 값(마스터 enum). 서버 내부 판정용이라 공유 계약에는 두지 않는다.</summary>
public static class GachaPityTypes
{
    public const int Soft = 1;
    public const int Hard = 2;
}

/// <summary>가챠 1회 추첨 결과(서버 RNG 확정). PityApplied는 하드 천장으로 등급이 확정된 회차임을 뜻한다.</summary>
public sealed record GachaRoll(int Grade, int ItemCode, int Quantity, bool PityApplied, bool Guaranteed);

// ── DB 행 매핑용 POCO(제네릭 매핑 전용, dynamic 금지). snake_case→PascalCase는 Dapper 규칙으로 매핑.
//    DECIMAL 컬럼은 decimal로 받아 float/double로 캐스팅한다. ──
file sealed class ClassMasterRow
{
    public int ClassCode { get; set; }
    public string Name { get; set; } = string.Empty;
    public int UnlockType { get; set; }
    public long Hp { get; set; }
    public long Atk { get; set; }
    public long Def { get; set; }
    public decimal MoveSpeed { get; set; }
    public decimal CritChance { get; set; }
    public decimal CritDamage { get; set; }
    public decimal Cooldown { get; set; }
}

file sealed class StageMasterRow
{
    public int StageId { get; set; }
    public int Act { get; set; }
    public int Difficulty { get; set; }
    public int Stage { get; set; }
    public int BossMonsterCode { get; set; }
    public int BackgroundType { get; set; }
}

file sealed class StageSpawnRow
{
    public int StageId { get; set; }
    public int MonsterCode { get; set; }
    public int SpawnCount { get; set; }
}

file sealed class StageRewardScalarRow
{
    public int StageId { get; set; }
    public long RewardGold { get; set; }
    public long RewardExp { get; set; }
}

file sealed class StageRewardDropRow
{
    public int StageId { get; set; }
    public int Grade { get; set; }
    public decimal DropProb { get; set; }
}

file sealed class LevelMasterRow
{
    public int Level { get; set; }
    public long RequiredExp { get; set; }
    public int SkillPoints { get; set; }
}

file sealed class EnhanceMasterRow
{
    public int EnhanceLevel { get; set; }
    public long Cost { get; set; }
    public int CurrencyType { get; set; }
    public decimal StatMultiplier { get; set; } // DECIMAL(5,3) → decimal로 받아 float로 캐스팅
}

file sealed class SkillMasterRow
{
    public int SkillCode { get; set; }
    public int ClassCode { get; set; }
    public int SkillType { get; set; }
    public int MaxLevel { get; set; }
}

file sealed class RuneMasterRow
{
    public int RuneCode { get; set; }
    public int PrereqCode { get; set; }
    public int MaxLevel { get; set; }
}

file sealed class RuneCostRow
{
    public int RuneCode { get; set; }
    public int Level { get; set; }
    public long Cost { get; set; }
}

file sealed class CharacterCreateCostRow
{
    public int CharacterId { get; set; }
    public long GoldCost { get; set; }
}

file sealed class CubeMasterRow
{
    public int CubeLevel { get; set; }
    public long RequiredExp { get; set; }
    public int CombineGradeUp { get; set; }
    public int CombineCount { get; set; }
    public long GoldPerScrap { get; set; }
}

file sealed class CubeRecipeRow
{
    public int RecipeCode { get; set; }
    public int ResultItemCode { get; set; }
    public int ResultQuantity { get; set; }
    public int ReqCubeLevel { get; set; }
    public long CostGold { get; set; }
}

file sealed class CubeRecipeIngredientRow
{
    public int RecipeCode { get; set; }
    public int MaterialCode { get; set; }
    public int Quantity { get; set; }
}

file sealed class ItemMasterRow
{
    public int ItemCode { get; set; }
    public string Name { get; set; } = string.Empty;
    public int ItemType { get; set; }
    public int Grade { get; set; }
    public int StackMax { get; set; }
    public int EquipSlot { get; set; }
    public int ClassReq { get; set; }
    public int LevelReq { get; set; }
    public int Sellable { get; set; }
    public long BasePrice { get; set; }
}

file sealed class ConsumableMasterRow
{
    public int ItemCode { get; set; }
    public int BuffType { get; set; }
    public decimal BuffValue { get; set; } // DECIMAL(5,3) → decimal로 받아 float로 캐스팅
    public int DurationSec { get; set; }
}

file sealed class AttendanceMasterRow
{
    public int Day { get; set; }
    public int RewardType { get; set; }
    public int RewardCode { get; set; }
    public int Quantity { get; set; }
}

file sealed class NewbieRewardRow
{
    public int Seq { get; set; }
    public int RewardType { get; set; }
    public int RewardCode { get; set; }
    public long Quantity { get; set; }
}

file sealed class MailMasterRow
{
    public int MailTemplateCode { get; set; }
    public int Category { get; set; }
    public string TitleFormat { get; set; } = string.Empty;
    public string BodyFormat { get; set; } = string.Empty;
    public int ValidDays { get; set; }
}

file sealed class GachaMasterRow
{
    public int GachaCode { get; set; }
    public string Name { get; set; } = string.Empty;
    public int IsActive { get; set; }
    public long OpenAt { get; set; }
    public long CloseAt { get; set; }
    public int SortOrder { get; set; }
    public int CostCurrencyCode { get; set; }
    public long CostSingle { get; set; }
    public long CostMulti { get; set; }
    public int MultiCount { get; set; }
    public int MultiGuaranteedGrade { get; set; }
    public int PickupItemCode { get; set; }
}

file sealed class GachaGradeWeightRow
{
    public int GachaCode { get; set; }
    public int Grade { get; set; }
    public int Weight { get; set; }
}

file sealed class GachaItemPoolRow
{
    public int GachaCode { get; set; }
    public int Grade { get; set; }
    public int ItemCode { get; set; }
    public int Quantity { get; set; }
}

file sealed class GachaPityRuleRow
{
    public int GachaCode { get; set; }
    public int Grade { get; set; }
    public int PityType { get; set; }
    public int Threshold { get; set; }
    public decimal ProbStep { get; set; }   // DECIMAL 컬럼 → decimal 로 받아 double 로 캐스팅
}

/// <summary>
/// 마스터(정적) 데이터 인메모리 캐시. 서버 기동 시 마스터 DB에서 코드→정의 딕셔너리로 적재한다.
/// class_master(직업)에 더해 스테이지 진행/전투(스테이지 진입·클리어)에 필요한 마스터를 적재한다:
///   stage_master(+stage_spawn) · stage_reward · level_master · item_master(등급별 드롭 풀).
/// 로드 실패 시 IsLoaded=false로 두고, 관련 요청은 MasterDataNotLoaded(10001)로 처리한다.
/// </summary>
public sealed class MasterDataProvider
{
    /// <summary>신규 계정 기본 인벤토리 용량(점유 slot 수). 골드로 1칸씩 확장(inventory_expand_master).</summary>
    public const int BaseInventoryCapacity = 100;

    private const int ItemTypeEquip = 1;      // item_master.item_type 1:장비
    private const int ItemTypeMaterial = 2;   // 2:재료
    private const int ItemTypeConsumable = 4; // 4:소모품(효과는 consumable_master)

    private readonly MasterDbFactory _masterDbFactory;
    private readonly ILogger<MasterDataProvider> _logger;

    private IReadOnlyDictionary<int, ClassMaster> _classes = new Dictionary<int, ClassMaster>();
    private IReadOnlyDictionary<int, StageDef> _stagesById = new Dictionary<int, StageDef>();
    private IReadOnlyDictionary<int, StageRewardDef> _rewardsByStageId = new Dictionary<int, StageRewardDef>();
    private IReadOnlyDictionary<int, long> _levelRequiredExp = new Dictionary<int, long>();
    private IReadOnlyDictionary<int, int> _levelSkillPoints = new Dictionary<int, int>();
    private int _maxLevel = 1;

    // 성장(스킬·룬) 정의: 코드 → 정의.
    private IReadOnlyDictionary<int, SkillDef> _skillsByCode = new Dictionary<int, SkillDef>();
    private IReadOnlyDictionary<int, RuneDef> _runesByCode = new Dictionary<int, RuneDef>();

    // 룬 레벨별 골드 비용: (rune_code, level) → cost(rune_cost 자식 테이블, 명시값).
    private IReadOnlyDictionary<(int rune, int level), long> _runeCosts = new Dictionary<(int, int), long>();

    // 캐릭터 추가 생성 비용: character_id(슬롯 2~3) → 골드 비용(character_create_cost). 1번(최초 생성)은 무료.
    private IReadOnlyDictionary<int, long> _characterCreateCosts = new Dictionary<int, long>();

    // 큐브: 레벨별 규칙(cube_master), 제작 레시피(cube_recipe + 자식).
    private IReadOnlyDictionary<int, CubeRule> _cubeRules = new Dictionary<int, CubeRule>();
    private IReadOnlyDictionary<int, RecipeDef> _recipesByCode = new Dictionary<int, RecipeDef>();

    // 아이템 정의: 코드 → 정의. 인벤토리에 존재할 수 있는 전 타입(장비 1·재료 2·소모품 4)을 담는다.
    //   소모품이 빠지면 사용 API가 item_type=4 확인·스택 적재를 못 하고, 메일 첨부 적재가 "장비·스택1"로 오인한다.
    private IReadOnlyDictionary<int, ItemDef> _itemsByCode = new Dictionary<int, ItemDef>();

    // 스테이지 전리품 드롭 풀: 등급 → 드롭 후보 코드 목록. 정의 사전과 달리 **장비(1)·재료(2)만** 담는다.
    //   소모품(4)은 스테이지 드롭으로 지급하지 않는다(소모품/버프 기획서 §4.3-(3) — 확률 지급은 상자 가챠 전용
    //   마스터 gacha_item_pool로 정의한다). 재화(3)는 양쪽 모두 제외.
    private IReadOnlyDictionary<int, List<int>> _itemsByGrade = new Dictionary<int, List<int>>();

    // 소모품 버프 효과: item_code → 정의(consumable_master).
    private IReadOnlyDictionary<int, ConsumableDef> _consumablesByCode = new Dictionary<int, ConsumableDef>();

    // 장비 강화: enhance_level → 그 단계의 규칙(비용·소모 재화·스탯 배율, enhance_master).
    private IReadOnlyDictionary<int, EnhanceRule> _enhanceByLevel = new Dictionary<int, EnhanceRule>();

    // 인벤토리 확장 비용: index i(0-based) = 기본 용량 이후 (i+1)번째 칸을 여는 골드 비용(inventory_expand_master, step 오름차순).
    // 배열 길이 = 확장 가능한 총 칸 수이며, 상한 용량 = BaseInventoryCapacity + 길이.
    private IReadOnlyList<long> _expandCosts = new List<long>();

    // 출석부: day(출석 일차 1~30, 누적 출석 순번) → 그 일차 보상 정의(attendance_master).
    private IReadOnlyDictionary<int, AttendanceRewardDef> _attendanceByDay = new Dictionary<int, AttendanceRewardDef>();

    // 메일 발급 템플릿: mail_template_code → 정의(mail_master, 서버 전용 마스터).
    private IReadOnlyDictionary<int, MailTemplateDef> _mailTemplates = new Dictionary<int, MailTemplateDef>();

    // 신규 가입 지원금 첨부(newbie_reward_master, seq 오름차순). 계정 초기화 시 환영 메일에 담는다.
    private IReadOnlyList<NewbieRewardDef> _newbieRewards = new List<NewbieRewardDef>();

    // 가챠 배너: gacha_code → 정의(gacha_master + 자식 가중치·후보·천장). 적재 시 유효성 검증을 통과한 배너만 담는다.
    private IReadOnlyDictionary<int, GachaBannerDef> _gachaByCode = new Dictionary<int, GachaBannerDef>();

    public MasterDataProvider(MasterDbFactory masterDbFactory, ILogger<MasterDataProvider> logger)
    {
        _masterDbFactory = masterDbFactory;
        _logger = logger;
    }

    /// <summary>기동 시 1회 적재 성공 여부. false면 관련 요청에 MasterDataNotLoaded를 반환한다.</summary>
    public bool IsLoaded { get; private set; }

    public int MaxLevel => _maxLevel;

    public bool IsValidClass(int classCode) => _classes.ContainsKey(classCode);

    public ClassMaster? GetClass(int classCode)
        => _classes.TryGetValue(classCode, out var c) ? c : null;

    /// <summary>(act,difficulty,stage) 좌표의 스테이지 정의. 없으면 null.</summary>
    public StageDef? GetStage(int act, int difficulty, int stage)
        => _stagesById.TryGetValue(StageCoords.StageId(act, difficulty, stage), out var s) ? s : null;

    public StageRewardDef? GetStageReward(int stageId)
        => _rewardsByStageId.TryGetValue(stageId, out var r) ? r : null;

    /// <summary>item_code의 아이템 정의(타입·등급·스택·장착 슬롯·클래스/레벨 제한).
    /// 인벤토리에 존재할 수 있는 전 타입(장비 1·재료 2·소모품 4)을 담으며, 재화(type 3)·미로드 코드는 null.</summary>
    public ItemDef? GetItem(int itemCode)
        => _itemsByCode.TryGetValue(itemCode, out var def) ? def : null;

    /// <summary>소모품(item_type=4)의 버프 효과 정의(consumable_master). 소모품이 아니거나 미정의 코드는 null.</summary>
    public ConsumableDef? GetConsumable(int itemCode)
        => _consumablesByCode.TryGetValue(itemCode, out var def) ? def : null;

    /// <summary>강화 단계 level의 규칙(그 단계로 올리는 비용·소모 재화·도달 시 스탯 배율). 정의가 없으면 null.</summary>
    public EnhanceRule? GetEnhance(int enhanceLevel)
        => _enhanceByLevel.TryGetValue(enhanceLevel, out var rule) ? rule : null;

    /// <summary>정의된 최대 강화 단계(= enhance_master의 최대 enhance_level, 현재 10). 정의가 비었으면 0(강화 불가).</summary>
    public int MaxEnhanceLevel => _enhanceByLevel.Count == 0 ? 0 : _enhanceByLevel.Keys.Max();

    /// <summary>현재 용량에서 1칸 확장 가능 여부와 그 비용을 산출한다.
    /// 확장할 칸의 step = currentCapacity - 기본 용량 + 1이며, 상한(=기본 용량 + 확장 정의 수)을 넘으면 불가(false).</summary>
    public (bool ok, long cost) PlanExpandOne(int currentCapacity)
    {
        var step = currentCapacity - BaseInventoryCapacity + 1; // 이번에 열 칸의 순번(1-based)
        if (step < 1 || step > _expandCosts.Count)
        {
            return (false, 0); // 상한 도달(또는 확장 정의 없음)
        }

        return (true, _expandCosts[step - 1]);
    }

    /// <summary>레벨 L에서 L+1로 가는 데 필요한 경험치. 최대 레벨 이상은 0(더 오르지 않음).</summary>
    public long LevelRequiredExp(int level)
        => _levelRequiredExp.TryGetValue(level, out var req) ? req : 0;

    /// <summary>레벨 L에서 사용 가능한 누적 스킬 포인트 총량(level_master.skill_points). 저장값이 아니라 레벨에서 파생하는 총량이다.</summary>
    public int SkillPointsForLevel(int level)
        => _levelSkillPoints.TryGetValue(level, out var pts) ? pts : 0;

    /// <summary>skill_code의 스킬 정의(직업·액티브/패시브·최대 레벨). 없으면 null.</summary>
    public SkillDef? GetSkill(int skillCode)
        => _skillsByCode.TryGetValue(skillCode, out var s) ? s : null;

    /// <summary>rune_code의 룬 정의(선행 룬·비용·최대 레벨). 없으면 null.</summary>
    public RuneDef? GetRune(int runeCode)
        => _runesByCode.TryGetValue(runeCode, out var r) ? r : null;

    /// <summary>현재 룬 레벨(cur)에서 다음 레벨(cur+1)로 올릴 때 드는 골드 비용. rune_cost(자식)에 명시된 레벨별 값을 그대로 조회한다(공식 파생 아님). 정의가 없으면(최대 레벨 초과 등) long.MaxValue(사실상 불가).</summary>
    public long RuneUpgradeCost(RuneDef rune, int currentLevel)
        => _runeCosts.TryGetValue((rune.RuneCode, currentLevel + 1), out var cost) ? cost : long.MaxValue;

    /// <summary>지정 슬롯(character_id, 2~3)의 캐릭터 추가 생성 골드 비용. 정의가 없으면(1번 슬롯 등) 0(무료). character_create_cost 명시값이다.</summary>
    public long CharacterCreateCost(int characterId)
        => _characterCreateCosts.TryGetValue(characterId, out var cost) ? cost : 0;

    /// <summary>큐브 레벨별 규칙(합성 개수·등급 상승·분해 골드 계수·요구 경험치). 없으면 null.</summary>
    public CubeRule? GetCubeRule(int cubeLevel)
        => _cubeRules.TryGetValue(cubeLevel, out var r) ? r : null;

    /// <summary>큐브 레벨 L에서 L+1로 가는 데 필요한 경험치. 최대 레벨(정의상 0) 이상은 0(더 오르지 않음).</summary>
    public long CubeRequiredExp(int cubeLevel)
        => _cubeRules.TryGetValue(cubeLevel, out var r) ? r.RequiredExp : 0;

    /// <summary>큐브 제작 레시피(결과·요구 큐브 레벨·비용·소모 재료). 없으면 null.</summary>
    public RecipeDef? GetRecipe(int recipeCode)
        => _recipesByCode.TryGetValue(recipeCode, out var r) ? r : null;

    /// <summary>출석 day일차(1~30, 이번달 누적 출석 순번 — 날짜가 아님)의 보상 정의(attendance_master).
    /// 미정의 일차는 null(호출측이 MasterDataNotLoaded로 거부).</summary>
    public AttendanceRewardDef? GetAttendanceReward(int day)
        => _attendanceByDay.TryGetValue(day, out var r) ? r : null;

    /// <summary>attendance_master에 정의된 일차(day) 목록(오름차순). 출석 보상 사다리 구성에 사용한다.</summary>
    public IReadOnlyCollection<int> AttendanceDays => _attendanceByDay.Keys.OrderBy(d => d).ToList();

    /// <summary>출석 보상 사다리의 마지막 일차(= attendance_master의 최대 day, 현재 30) = <b>순환 주기</b>.
    /// 이 일차까지 받으면 다음 출석은 다시 1일차다(day = 출석 수 % 이 값 + 1). 정의가 비었으면 0.</summary>
    public int MaxAttendanceDay => _attendanceByDay.Count == 0 ? 0 : _attendanceByDay.Keys.Max();

    /// <summary>메일 발급 템플릿(mail_master). 없으면 null(호출측이 MasterDataNotLoaded로 거부).</summary>
    public MailTemplateDef? GetMailTemplate(int templateCode)
        => _mailTemplates.TryGetValue(templateCode, out var t) ? t : null;

    /// <summary>신규 가입 지원금 첨부 목록(newbie_reward_master, seq 오름차순). 정의가 없으면 빈 목록(지급 없음).</summary>
    public IReadOnlyList<NewbieRewardDef> NewbieRewards => _newbieRewards;

    /// <summary>
    /// 합성 결과 아이템 코드를 서버가 산출한다: (입력 등급 + 1) 장비 중 하나를 무작위 선택(슬롯·클래스 무관).
    /// 상위 등급 후보가 없으면(최대 등급 등) null → 호출측이 CubeRecipeNotMet으로 거부한다.
    /// </summary>
    public int? PickCombineResultCode(int inputGrade)
    {
        var candidates = _itemsByCode.Values
            .Where(d => d.ItemType == ItemTypeEquip && d.Grade == inputGrade + 1)
            .Select(d => d.ItemCode)
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        return candidates[Random.Shared.Next(candidates.Count)];
    }

    /// <summary>gacha_code의 배너 정의. 없으면 null(→ GachaNotFound).</summary>
    public GachaBannerDef? GetGacha(int gachaCode)
        => _gachaByCode.TryGetValue(gachaCode, out var def) ? def : null;

    /// <summary>
    /// 지정 시각에 열려 있는 배너 목록을 노출 순서(sort_order → gacha_code)로 반환한다(기획서 §6.1).
    /// 마스터는 인메모리라 DB 조회가 없고, 배너 수가 적어 매 요청 선형 훑기로 충분하다.
    /// </summary>
    public IReadOnlyList<GachaBannerDef> OpenGachaBanners(long nowUnix)
        => _gachaByCode.Values
            .Where(b => b.IsOpenAt(nowUnix))
            .OrderBy(b => b.SortOrder).ThenBy(b => b.GachaCode)
            .ToList();

    /// <summary>
    /// 가챠 1회 추첨(기획서 §6.2). 2단계다 — ①하드 천장 확인 → ②소프트 천장 가중치 가산 → ③등급 추첨 → ④슬롯 내 균등 추첨.
    /// <para><paramref name="pityCounts"/>는 등급 → 누적 미획득 횟수이며, 판정은 <b>이번 회차 번호</b>(count+1)로 한다.</para>
    /// <para>픽업 분기는 없다 — 픽업 배너는 최고 등급 슬롯 후보가 1종이라 ④의 균등 추첨이 그대로 확정을 만든다.</para>
    /// 추첨된 등급 슬롯에 후보가 없으면 null(→ 호출측이 GachaPoolEmpty(12002)로 전체 롤백).
    /// </summary>
    public GachaRoll? RollGacha(GachaBannerDef banner, IReadOnlyDictionary<int, int> pityCounts)
    {
        int PullNo(int grade) => (pityCounts.TryGetValue(grade, out var c) ? c : 0) + 1;

        // 1) 하드 천장 — 도달했으면 추첨 없이 등급 확정(동시 도달 시 가장 높은 등급).
        var hard = banner.PityRules
            .Where(r => r.PityType == GachaPityTypes.Hard && PullNo(r.Grade) >= r.Threshold)
            .OrderByDescending(r => r.Grade)
            .FirstOrDefault();
        if (hard is not null)
        {
            return PickFromSlot(banner, hard.Grade, pityApplied: true, guaranteed: false);
        }

        // 2) 소프트 천장 — 발동 이후 회차마다 그 등급의 <b>확률</b>을 ProbStep 만큼 올린다(기획서 §4.1).
        //    목표 확률을 가중치로 환산해 더하므로 추첨 엔진은 하나(가중치 추첨)로 유지되고,
        //    나머지 등급 확률은 정규화 없이 자동으로 비례 감소한다.
        var weights = new Dictionary<int, long>();
        foreach (var (grade, weight) in banner.GradeWeights)
        {
            weights[grade] = weight;
        }

        long baseTotal = banner.GradeWeights.Values.Sum();
        foreach (var rule in banner.PityRules.Where(r => r.PityType == GachaPityTypes.Soft))
        {
            int k = PullNo(rule.Grade) - rule.Threshold + 1;
            if (k <= 0 || rule.ProbStep <= 0 || !banner.GradeWeights.TryGetValue(rule.Grade, out int baseWeight))
            {
                continue;
            }

            // 목표 확률 = 그 등급 기본 확률 + 발동 후 회차 수 × ProbStep.
            double targetP = (double)baseWeight / baseTotal + k * rule.ProbStep;
            if (targetP >= 1.0)
            {
                // 소프트만으로 100%에 도달 — 하드와 같은 확정 경로로 지급한다(하드가 먼저 걸리는 게 정상이며,
                // 이 분기는 하드 threshold 를 소프트 도달 지점보다 크게 잡은 마스터에서만 쓰인다).
                return PickFromSlot(banner, rule.Grade, pityApplied: true, guaranteed: false);
            }

            // 목표 확률 p 를 만드는 가산량: p = (w + up) / (total + up)  →  up = (p·total − w) / (1 − p).
            // 가중치 가산 방식만으로는 p 가 1에 닿지 못하므로(분자·분모에 같은 up 이 더해진다) 확률로 정의한다.
            long up = (long)Math.Round((targetP * baseTotal - baseWeight) / (1.0 - targetP));
            if (up > 0)
            {
                weights[rule.Grade] = baseWeight + up;
            }
        }

        // 소프트 규칙이 여러 등급에 동시에 걸리면 위 환산이 서로의 분모를 반영하지 못해 근사가 된다.
        // 현재 확정 설계는 최고 등급 1개에만 소프트를 두므로 정확하다(여러 등급이 필요해지면 재설계).

        // 3) 등급 추첨(누적 가중치). 순회 순서를 등급 오름차순으로 고정해 결과가 재현·검증 가능하게 한다.
        long total = weights.Values.Sum();
        if (total <= 0)
        {
            return null;
        }

        long roll = Random.Shared.NextInt64(0, total);
        long acc = 0;
        foreach (var (grade, weight) in weights.OrderBy(w => w.Key))
        {
            acc += weight;
            if (roll < acc)
            {
                return PickFromSlot(banner, grade, pityApplied: false, guaranteed: false);
            }
        }

        return null; // 가중치 합 계산과 어긋난 경우(도달 불가) — 마스터 결함으로 취급한다.
    }

    /// <summary>
    /// 10연 보장 대체용 추첨(기획서 §6.4). 보장 등급 슬롯에서 균등 추첨하며 결과에 Guaranteed 플래그를 세운다.
    /// 후보가 없으면 null(→ GachaPoolEmpty).
    /// </summary>
    public GachaRoll? RollGuaranteed(GachaBannerDef banner, int grade)
        => PickFromSlot(banner, grade, pityApplied: false, guaranteed: true);

    /// <summary>
    /// 등급 슬롯의 지급 후보 중 하나를 <b>균등</b>하게 고른다(기획서 §6.2-④).
    /// 픽업 배너의 최고 등급 슬롯은 후보가 1종이라 이 균등 추첨이 곧 확정이다. 후보가 없으면 null.
    /// </summary>
    private static GachaRoll? PickFromSlot(GachaBannerDef banner, int grade, bool pityApplied, bool guaranteed)
    {
        if (!banner.PoolByGrade.TryGetValue(grade, out var pool) || pool.Count == 0)
        {
            return null;
        }

        var pick = pool[Random.Shared.Next(pool.Count)];
        return new GachaRoll(grade, pick.ItemCode, pick.Quantity, pityApplied, guaranteed);
    }

    /// <summary>등급별 확률로 전리품 1개를 추첨한다. 미드롭이면 null. (서버 권위 RNG)</summary>
    public DroppedItem? RollDrop(StageRewardDef reward)
    {
        var roll = Random.Shared.NextDouble();
        var cumulative = 0.0;
        for (var i = 0; i < reward.GradeProbs.Length; i++)
        {
            cumulative += reward.GradeProbs[i];
            if (roll < cumulative)
            {
                var grade = i + 1; // GradeProbs[0] = 등급 1
                if (!_itemsByGrade.TryGetValue(grade, out var pool) || pool.Count == 0)
                {
                    return null; // 해당 등급 드롭 풀이 비면 미드롭
                }

                var itemCode = pool[Random.Shared.Next(pool.Count)];
                var def = _itemsByCode[itemCode];
                return new DroppedItem(itemCode, 1, def.ItemType, def.StackMax);
            }
        }

        return null; // 확률 합 미만 구간 → 미드롭
    }

    /// <summary>마스터 DB에서 필요한 테이블을 읽어 인메모리로 적재한다. 기동 시 1회 호출.</summary>
    public async Task LoadAsync()
    {
        try
        {
            using var db = _masterDbFactory.Create();

            _classes = await LoadClassesAsync(db);
            _stagesById = await LoadStagesAsync(db);
            _rewardsByStageId = await LoadStageRewardsAsync(db);
            (_levelRequiredExp, _maxLevel, _levelSkillPoints) = await LoadLevelsAsync(db);
            (_itemsByGrade, _itemsByCode) = await LoadItemsAsync(db);
            _consumablesByCode = await LoadConsumablesAsync(db);
            _enhanceByLevel = await LoadEnhanceRulesAsync(db);
            _skillsByCode = await LoadSkillsAsync(db);
            _runesByCode = await LoadRunesAsync(db);
            _runeCosts = await LoadRuneCostsAsync(db);
            _characterCreateCosts = await LoadCharacterCreateCostsAsync(db);
            _cubeRules = await LoadCubeRulesAsync(db);
            _recipesByCode = await LoadRecipesAsync(db);
            _attendanceByDay = await LoadAttendanceAsync(db);
            _mailTemplates = await LoadMailTemplatesAsync(db);
            _newbieRewards = await LoadNewbieRewardsAsync(db);
            _gachaByCode = await LoadGachaAsync(db);

            // 인벤토리 확장은 부가 기능이라 별도 try로 감싼다(테이블 부재 시 다른 마스터 적재까지 실패하지 않도록).
            _expandCosts = await LoadExpandCostsAsync(db);

            if (_classes.Count == 0 || _stagesById.Count == 0)
            {
                throw new InvalidOperationException("필수 마스터(class_master/stage_master)가 비어 있습니다.");
            }

            IsLoaded = true;
            _logger.ZLogInformation($"마스터 데이터 적재 완료: class {_classes.Count:@Classes} · stage {_stagesById.Count:@Stages} · reward {_rewardsByStageId.Count:@Rewards} · level {_levelRequiredExp.Count:@Levels} · item {_itemsByCode.Count:@Items} · dropGrades {_itemsByGrade.Count:@Grades} · consumable {_consumablesByCode.Count:@Consumables} · enhance {_enhanceByLevel.Count:@Enhances} · expandSlots {_expandCosts.Count:@Expand} · skill {_skillsByCode.Count:@Skills} · rune {_runesByCode.Count:@Runes} · runeCost {_runeCosts.Count:@RuneCosts} · charCost {_characterCreateCosts.Count:@CharCosts} · cube {_cubeRules.Count:@Cubes} · recipe {_recipesByCode.Count:@Recipes} · attendance {_attendanceByDay.Count:@Attendances} · mailTemplate {_mailTemplates.Count:@MailTemplates} · newbieReward {_newbieRewards.Count:@NewbieRewards} · gacha {_gachaByCode.Count:@Gachas}");
        }
        catch (Exception ex)
        {
            IsLoaded = false;
            _logger.ZLogError(ex, $"마스터 데이터 적재 실패. 관련 요청은 MasterDataNotLoaded(10001)로 처리됩니다.");
        }
    }

    private static async Task<Dictionary<int, ClassMaster>> LoadClassesAsync(QueryFactory db)
    {
        var rows = await db.Query("class_master")
            .Select("class_code", "name", "unlock_type",
                    "hp", "atk", "def", "move_speed", "crit_chance", "crit_damage", "cooldown")
            .GetAsync<ClassMasterRow>();

        var classes = new Dictionary<int, ClassMaster>();
        foreach (var row in rows)
        {
            var master = new ClassMaster
            {
                classCode = row.ClassCode,
                name = row.Name,
                unlockType = row.UnlockType,
                baseStats = new Stats
                {
                    hp = row.Hp,
                    atk = row.Atk,
                    def = row.Def,
                    moveSpeed = (float)row.MoveSpeed,
                    critChance = (float)row.CritChance,
                    critDamage = (float)row.CritDamage,
                    cooldown = (float)row.Cooldown,
                },
            };
            classes[master.classCode] = master;
        }

        return classes;
    }

    private static async Task<Dictionary<int, StageDef>> LoadStagesAsync(QueryFactory db)
    {
        var stageRows = await db.Query("stage_master")
            .Select("stage_id", "act", "difficulty", "stage", "boss_monster_code", "background_type")
            .GetAsync<StageMasterRow>();

        var spawnRows = await db.Query("stage_spawn")
            .Select("stage_id", "monster_code", "spawn_count")
            .OrderBy("stage_id", "monster_code")
            .GetAsync<StageSpawnRow>();

        var spawnsByStage = new Dictionary<int, List<StageSpawnDto>>();
        foreach (var sp in spawnRows)
        {
            if (!spawnsByStage.TryGetValue(sp.StageId, out var list))
            {
                list = new List<StageSpawnDto>();
                spawnsByStage[sp.StageId] = list;
            }

            list.Add(new StageSpawnDto
            {
                monsterCode = sp.MonsterCode,
                count = sp.SpawnCount,
            });
        }

        var stages = new Dictionary<int, StageDef>();
        foreach (var row in stageRows)
        {
            spawnsByStage.TryGetValue(row.StageId, out var spawns);
            stages[row.StageId] = new StageDef(
                row.StageId,
                row.Act,
                row.Difficulty,
                row.Stage,
                row.BossMonsterCode,
                row.BackgroundType,
                spawns ?? new List<StageSpawnDto>());
        }

        return stages;
    }

    private static async Task<Dictionary<int, StageRewardDef>> LoadStageRewardsAsync(QueryFactory db)
    {
        // 스칼라 보상(골드·경험치).
        var rewardRows = await db.Query("stage_reward")
            .Select("stage_id", "reward_gold", "reward_exp")
            .GetAsync<StageRewardScalarRow>();

        // 등급별 드롭 확률(자식 테이블). 확률 0 등급은 행이 없으므로 배열에서 0으로 남는다.
        var dropRows = await db.Query("stage_reward_drop")
            .Select("stage_id", "grade", "drop_prob")
            .GetAsync<StageRewardDropRow>();

        // stage_id → (grade → prob). 최대 등급을 파악해 확률 배열 길이를 정한다(등급 추가 시 스키마·코드 불변).
        var dropsByStage = new Dictionary<int, Dictionary<int, double>>();
        var maxGrade = 0;
        foreach (var d in dropRows)
        {
            if (!dropsByStage.TryGetValue(d.StageId, out var map))
            {
                map = new Dictionary<int, double>();
                dropsByStage[d.StageId] = map;
            }

            map[d.Grade] = (double)d.DropProb;
            if (d.Grade > maxGrade)
            {
                maxGrade = d.Grade;
            }
        }

        var rewards = new Dictionary<int, StageRewardDef>();
        foreach (var row in rewardRows)
        {
            var probs = new double[maxGrade]; // index i = 등급 (i+1) 확률, 정의 없는 등급은 0
            if (dropsByStage.TryGetValue(row.StageId, out var map))
            {
                foreach (var kv in map)
                {
                    if (kv.Key >= 1 && kv.Key <= maxGrade)
                    {
                        probs[kv.Key - 1] = kv.Value;
                    }
                }
            }

            rewards[row.StageId] = new StageRewardDef(row.RewardGold, row.RewardExp, probs);
        }

        return rewards;
    }

    private static async Task<(Dictionary<int, long>, int, Dictionary<int, int>)> LoadLevelsAsync(QueryFactory db)
    {
        var rows = await db.Query("level_master").Select("level", "required_exp", "skill_points").GetAsync<LevelMasterRow>();
        var byLevel = new Dictionary<int, long>();
        var skillPoints = new Dictionary<int, int>();
        var maxLevel = 1;
        foreach (var row in rows)
        {
            byLevel[row.Level] = row.RequiredExp;
            skillPoints[row.Level] = row.SkillPoints;
            if (row.Level > maxLevel)
            {
                maxLevel = row.Level;
            }
        }

        return (byLevel, maxLevel, skillPoints);
    }

    /// <summary>skill_master를 skill_code → 정의(직업·액티브/패시브·최대 레벨)로 적재한다.</summary>
    private static async Task<Dictionary<int, SkillDef>> LoadSkillsAsync(QueryFactory db)
    {
        var rows = await db.Query("skill_master")
            .Select("skill_code", "class_code", "skill_type", "max_level")
            .GetAsync<SkillMasterRow>();

        var byCode = new Dictionary<int, SkillDef>();
        foreach (var row in rows)
        {
            byCode[row.SkillCode] = new SkillDef(row.SkillCode, row.ClassCode, row.SkillType, row.MaxLevel);
        }

        return byCode;
    }

    /// <summary>rune_master를 rune_code → 정의(선행 룬·최대 레벨)로 적재한다. 레벨별 비용은 LoadRuneCostsAsync가 별도 적재한다.</summary>
    private static async Task<Dictionary<int, RuneDef>> LoadRunesAsync(QueryFactory db)
    {
        var rows = await db.Query("rune_master")
            .Select("rune_code", "prereq_code", "max_level")
            .GetAsync<RuneMasterRow>();

        var byCode = new Dictionary<int, RuneDef>();
        foreach (var row in rows)
        {
            byCode[row.RuneCode] = new RuneDef(row.RuneCode, row.PrereqCode, row.MaxLevel);
        }

        return byCode;
    }

    /// <summary>rune_cost(자식)를 (rune_code, level) → 골드 비용으로 적재한다. 레벨별 비용은 명시값이다(공식 파생 아님).</summary>
    private static async Task<Dictionary<(int, int), long>> LoadRuneCostsAsync(QueryFactory db)
    {
        var rows = await db.Query("rune_cost")
            .Select("rune_code", "level", "cost")
            .GetAsync<RuneCostRow>();

        var byRuneLevel = new Dictionary<(int, int), long>();
        foreach (var row in rows)
        {
            byRuneLevel[(row.RuneCode, row.Level)] = row.Cost;
        }

        return byRuneLevel;
    }

    /// <summary>character_create_cost를 character_id(슬롯 2~3) → 골드 비용으로 적재한다. 1번 슬롯(최초 생성)은 무료라 행이 없다.</summary>
    private static async Task<Dictionary<int, long>> LoadCharacterCreateCostsAsync(QueryFactory db)
    {
        var rows = await db.Query("character_create_cost")
            .Select("character_id", "gold_cost")
            .GetAsync<CharacterCreateCostRow>();

        var byCharacter = new Dictionary<int, long>();
        foreach (var row in rows)
        {
            byCharacter[row.CharacterId] = row.GoldCost;
        }

        return byCharacter;
    }

    /// <summary>cube_master를 cube_level → 규칙(합성 개수·등급 상승·분해 계수·요구 경험치)으로 적재한다.</summary>
    private static async Task<Dictionary<int, CubeRule>> LoadCubeRulesAsync(QueryFactory db)
    {
        var rows = await db.Query("cube_master")
            .Select("cube_level", "required_exp", "combine_grade_up", "combine_count", "gold_per_scrap")
            .GetAsync<CubeMasterRow>();

        var byLevel = new Dictionary<int, CubeRule>();
        foreach (var row in rows)
        {
            byLevel[row.CubeLevel] = new CubeRule(
                row.CubeLevel, row.RequiredExp, row.CombineGradeUp, row.CombineCount, row.GoldPerScrap);
        }

        return byLevel;
    }

    /// <summary>cube_recipe + cube_recipe_ingredient(자식)를 recipe_code → 레시피(결과·요구 큐브 레벨·비용·소모 재료)로 적재한다.</summary>
    /// <summary>
    /// gacha_master + 자식 3종(gacha_grade_weight·gacha_item_pool·gacha_pity_rule)을 배너 코드 → 정의로 적재한다.
    /// <para><b>유효성 검증을 통과한 배너만 담는다</b>(기획서 §4.1). 위반한 배너는 Error 로그를 남기고 제외하며,
    /// 서버 전체를 내리지는 않는다 — 배너 하나의 값 오류로 게임 전체가 멈추는 편이 더 나쁘고, 제외된 배너는
    /// 목록에 나오지 않아 뽑을 수 없으므로 잘못된 확률로 재화를 받는 일이 없다.</para>
    /// 검증 항목: ①가중치가 있는 모든 등급에 후보 1개 이상 ②하드 threshold &gt; multi_count
    /// ③소프트 threshold &lt; 하드 threshold ④픽업(pickup_item_code≠0)이면 close_at≠0이고 그 배너 최고 등급 슬롯
    /// 후보가 정확히 그 아이템 하나.
    /// </summary>
    private async Task<Dictionary<int, GachaBannerDef>> LoadGachaAsync(QueryFactory db)
    {
        var bannerRows = await db.Query("gacha_master")
            .Select("gacha_code", "name", "is_active", "open_at", "close_at", "sort_order",
                    "cost_currency_code", "cost_single", "cost_multi", "multi_count",
                    "multi_guaranteed_grade", "pickup_item_code")
            .GetAsync<GachaMasterRow>();

        var weightRows = await db.Query("gacha_grade_weight")
            .Select("gacha_code", "grade", "weight").OrderBy("gacha_code", "grade")
            .GetAsync<GachaGradeWeightRow>();

        var poolRows = await db.Query("gacha_item_pool")
            .Select("gacha_code", "grade", "item_code", "quantity").OrderBy("gacha_code", "grade", "item_code")
            .GetAsync<GachaItemPoolRow>();

        var pityRows = await db.Query("gacha_pity_rule")
            .Select("gacha_code", "grade", "pity_type", "threshold", "prob_step")
            .OrderBy("gacha_code", "grade", "pity_type")
            .GetAsync<GachaPityRuleRow>();

        var weightsByGacha = new Dictionary<int, Dictionary<int, int>>();
        foreach (var row in weightRows)
        {
            if (row.Weight <= 0)
            {
                continue; // 가중치 0인 등급은 추첨 대상이 아니므로 후보 검증에서도 제외된다.
            }

            if (!weightsByGacha.TryGetValue(row.GachaCode, out var map))
            {
                map = new Dictionary<int, int>();
                weightsByGacha[row.GachaCode] = map;
            }

            map[row.Grade] = row.Weight;
        }

        var poolByGacha = new Dictionary<int, Dictionary<int, List<GachaPoolEntry>>>();
        foreach (var row in poolRows)
        {
            if (!poolByGacha.TryGetValue(row.GachaCode, out var byGrade))
            {
                byGrade = new Dictionary<int, List<GachaPoolEntry>>();
                poolByGacha[row.GachaCode] = byGrade;
            }

            if (!byGrade.TryGetValue(row.Grade, out var list))
            {
                list = new List<GachaPoolEntry>();
                byGrade[row.Grade] = list;
            }

            list.Add(new GachaPoolEntry(row.ItemCode, Math.Max(row.Quantity, 1)));
        }

        var pityByGacha = new Dictionary<int, List<GachaPityRule>>();
        foreach (var row in pityRows)
        {
            if (!pityByGacha.TryGetValue(row.GachaCode, out var list))
            {
                list = new List<GachaPityRule>();
                pityByGacha[row.GachaCode] = list;
            }

            list.Add(new GachaPityRule(row.Grade, row.PityType, row.Threshold, (double)row.ProbStep));
        }

        var byCode = new Dictionary<int, GachaBannerDef>();
        foreach (var row in bannerRows)
        {
            weightsByGacha.TryGetValue(row.GachaCode, out var weights);
            poolByGacha.TryGetValue(row.GachaCode, out var pool);
            pityByGacha.TryGetValue(row.GachaCode, out var pity);

            var banner = new GachaBannerDef(
                row.GachaCode, row.Name, row.IsActive, row.OpenAt, row.CloseAt, row.SortOrder,
                row.CostCurrencyCode, row.CostSingle, row.CostMulti, Math.Max(row.MultiCount, 1),
                row.MultiGuaranteedGrade, row.PickupItemCode,
                weights ?? new Dictionary<int, int>(),
                pool ?? new Dictionary<int, List<GachaPoolEntry>>(),
                pity ?? new List<GachaPityRule>());

            var reason = ValidateGacha(banner);
            if (reason is not null)
            {
                _logger.ZLogError($"가챠 배너 마스터 검증 실패로 제외: {row.GachaCode:@GachaCode} — {reason:@Reason}");
                continue;
            }

            byCode[row.GachaCode] = banner;
        }

        return byCode;
    }

    /// <summary>가챠 배너 정의의 유효성을 검증한다. 통과하면 null, 위반하면 사람이 읽는 사유 문구를 반환한다.</summary>
    private static string? ValidateGacha(GachaBannerDef b)
    {
        if (b.GradeWeights.Count == 0)
        {
            return "등급 가중치(gacha_grade_weight)가 없음";
        }

        // ① 가중치가 있는 모든 등급에 후보가 1개 이상 있어야 한다(비용을 먼저 받으므로 미지급으로 넘길 수 없다).
        foreach (var grade in b.GradeWeights.Keys)
        {
            if (!b.PoolByGrade.TryGetValue(grade, out var pool) || pool.Count == 0)
            {
                return $"등급 {grade} 슬롯에 지급 후보(gacha_item_pool)가 없음";
            }
        }

        // 10연 보장 등급도 대체 추첨 대상이므로 후보가 있어야 한다.
        if (b.MultiGuaranteedGrade > 0
            && (!b.PoolByGrade.TryGetValue(b.MultiGuaranteedGrade, out var gp) || gp.Count == 0))
        {
            return $"10연 보장 등급 {b.MultiGuaranteedGrade} 슬롯에 지급 후보가 없음";
        }

        foreach (var grade in b.PityGrades)
        {
            int hard = b.HardThreshold(grade);

            // ② 하드 천장은 10연 1회보다 커야 한다(작으면 한 번의 10연에서 하드가 두 번 터진다).
            if (hard > 0 && hard <= b.MultiCount)
            {
                return $"등급 {grade} 하드 천장 threshold({hard}) <= multi_count({b.MultiCount})";
            }

            // ③ 소프트는 하드보다 앞서야 한다(뒤면 상승 구간 없이 하드만 동작한다).
            var soft = b.PityRules.FirstOrDefault(r => r.Grade == grade && r.PityType == GachaPityTypes.Soft);
            if (soft is not null && hard > 0 && soft.Threshold >= hard)
            {
                return $"등급 {grade} 소프트 threshold({soft.Threshold}) >= 하드 threshold({hard})";
            }
        }

        // ④ 픽업 = 한정이며, 픽업 배너의 최고 등급 슬롯 후보는 그 아이템 하나여야 한다.
        if (b.PickupItemCode != 0)
        {
            if (b.CloseAt == 0)
            {
                return "픽업 배너인데 close_at=0(기간 없음) — 픽업은 한정 배너다";
            }

            int topGrade = b.GradeWeights.Keys.Max();
            if (!b.PoolByGrade.TryGetValue(topGrade, out var top)
                || top.Count != 1 || top[0].ItemCode != b.PickupItemCode)
            {
                return $"픽업 배너의 최고 등급 {topGrade} 슬롯 후보가 pickup_item_code({b.PickupItemCode}) 1종이 아님";
            }
        }

        return null;
    }

    private static async Task<Dictionary<int, RecipeDef>> LoadRecipesAsync(QueryFactory db)
    {
        var recipeRows = await db.Query("cube_recipe")
            .Select("recipe_code", "result_item_code", "result_quantity", "req_cube_level", "cost_gold")
            .GetAsync<CubeRecipeRow>();

        var ingredientRows = await db.Query("cube_recipe_ingredient")
            .Select("recipe_code", "material_code", "quantity")
            .OrderBy("recipe_code", "material_code")
            .GetAsync<CubeRecipeIngredientRow>();

        var ingredientsByRecipe = new Dictionary<int, List<RecipeIngredient>>();
        foreach (var ing in ingredientRows)
        {
            if (!ingredientsByRecipe.TryGetValue(ing.RecipeCode, out var list))
            {
                list = new List<RecipeIngredient>();
                ingredientsByRecipe[ing.RecipeCode] = list;
            }

            list.Add(new RecipeIngredient(ing.MaterialCode, ing.Quantity));
        }

        var byCode = new Dictionary<int, RecipeDef>();
        foreach (var row in recipeRows)
        {
            ingredientsByRecipe.TryGetValue(row.RecipeCode, out var ingredients);
            byCode[row.RecipeCode] = new RecipeDef(
                row.RecipeCode, row.ResultItemCode, row.ResultQuantity, row.ReqCubeLevel, row.CostGold,
                ingredients ?? new List<RecipeIngredient>());
        }

        return byCode;
    }

    /// <summary>
    /// item_master를 두 사전으로 적재한다. <b>적재 기준이 서로 다르므로 한 필터로 결정하지 않는다</b>
    /// (소모품/버프 기획서 §4.3-(3)):
    /// <para>· <b>정의 사전</b>(byCode) — 인벤토리에 존재할 수 있는 전 타입(장비 1·재료 2·<b>소모품 4</b>).
    ///   소모품이 빠지면 사용 API가 item_type 확인·스택 적재를 못 하고, 메일 첨부가 "장비·스택1"로 오인 적재된다.</para>
    /// <para>· <b>드롭 후보 풀</b>(byGrade) — <b>장비·재료만</b>. 소모품을 넣으면 스테이지 전리품에서 확률로 지급되는데,
    ///   소모품의 확률 지급은 가챠(gacha_item_pool) 전용이다. 소모품의 grade는 grade_master FK 충족용 값이라
    ///   희귀도 의미가 없어 추첨 축으로 쓸 수 없다.</para>
    /// 재화(3, 골드)는 양쪽 모두에서 제외한다. 장착 검증용 슬롯·클래스/레벨 제한도 정의 사전에 함께 담는다.
    /// </summary>
    private static async Task<(Dictionary<int, List<int>>, Dictionary<int, ItemDef>)> LoadItemsAsync(QueryFactory db)
    {
        var rows = await db.Query("item_master")
            .Select("item_code", "name", "item_type", "grade", "stack_max", "equip_slot", "class_req", "level_req",
                    "sellable", "base_price")
            .WhereIn("item_type", new[] { ItemTypeEquip, ItemTypeMaterial, ItemTypeConsumable })
            .GetAsync<ItemMasterRow>();

        var byGrade = new Dictionary<int, List<int>>();
        var byCode = new Dictionary<int, ItemDef>();
        foreach (var row in rows)
        {
            byCode[row.ItemCode] = new ItemDef(
                row.ItemCode,
                row.Name,
                row.ItemType,
                row.Grade,
                row.StackMax,
                row.EquipSlot,
                row.ClassReq,
                row.LevelReq,
                row.Sellable,
                row.BasePrice);

            // 드롭 후보는 장비·재료만(소모품 제외).
            if (row.ItemType != ItemTypeEquip && row.ItemType != ItemTypeMaterial)
            {
                continue;
            }

            if (!byGrade.TryGetValue(row.Grade, out var list))
            {
                list = new List<int>();
                byGrade[row.Grade] = list;
            }

            list.Add(row.ItemCode);
        }

        return (byGrade, byCode);
    }

    /// <summary>consumable_master를 item_code → 버프 효과 정의로 적재한다(소모품 사용 API가 배율·지속시간을 여기서 읽는다).</summary>
    private static async Task<Dictionary<int, ConsumableDef>> LoadConsumablesAsync(QueryFactory db)
    {
        var rows = await db.Query("consumable_master")
            .Select("item_code", "buff_type", "buff_value", "duration_sec")
            .GetAsync<ConsumableMasterRow>();

        var byCode = new Dictionary<int, ConsumableDef>();
        foreach (var row in rows)
        {
            // DECIMAL 컬럼은 POCO에서 decimal로 받아 float로 캐스팅한다(프로젝트 DB 매핑 규칙).
            byCode[row.ItemCode] = new ConsumableDef(row.ItemCode, row.BuffType, (float)row.BuffValue, row.DurationSec);
        }

        return byCode;
    }

    /// <summary>
    /// enhance_master를 enhance_level → 강화 규칙(비용·소모 재화·스탯 배율)으로 적재한다.
    /// 강화 API가 "현재 단계 + 1" 행을 찾아 비용을 확정하며, 행이 없으면 최대 단계로 판정한다.
    /// </summary>
    private static async Task<Dictionary<int, EnhanceRule>> LoadEnhanceRulesAsync(QueryFactory db)
    {
        var rows = await db.Query("enhance_master")
            .Select("enhance_level", "cost", "currency_type", "stat_multiplier")
            .GetAsync<EnhanceMasterRow>();

        var byLevel = new Dictionary<int, EnhanceRule>();
        foreach (var row in rows)
        {
            // DECIMAL 컬럼은 POCO에서 decimal로 받아 float로 캐스팅한다(프로젝트 DB 매핑 규칙).
            byLevel[row.EnhanceLevel] = new EnhanceRule(
                row.EnhanceLevel, row.Cost, row.CurrencyType, (float)row.StatMultiplier);
        }

        return byLevel;
    }

    /// <summary>attendance_master를 day(출석 일차) → 보상 정의로 적재한다(정상 운영에선 1~30 전부 정의).</summary>
    private static async Task<Dictionary<int, AttendanceRewardDef>> LoadAttendanceAsync(QueryFactory db)
    {
        var rows = await db.Query("attendance_master")
            .Select("day", "reward_type", "reward_code", "quantity")
            .GetAsync<AttendanceMasterRow>();

        var byDay = new Dictionary<int, AttendanceRewardDef>();
        foreach (var row in rows)
        {
            byDay[row.Day] = new AttendanceRewardDef(row.Day, row.RewardType, row.RewardCode, row.Quantity);
        }

        return byDay;
    }

    /// <summary>mail_master(발급 문구 템플릿)를 mail_template_code → 정의로 적재한다. 서버 전용 마스터(클라 번들 제외).</summary>
    private static async Task<Dictionary<int, MailTemplateDef>> LoadMailTemplatesAsync(QueryFactory db)
    {
        var rows = await db.Query("mail_master")
            .Select("mail_template_code", "category", "title_format", "body_format", "valid_days")
            .GetAsync<MailMasterRow>();

        var byCode = new Dictionary<int, MailTemplateDef>();
        foreach (var row in rows)
        {
            byCode[row.MailTemplateCode] = new MailTemplateDef(
                row.MailTemplateCode, row.Category, row.TitleFormat, row.BodyFormat, row.ValidDays);
        }

        return byCode;
    }

    /// <summary>newbie_reward_master(신규 가입 지원금 첨부)를 seq 오름차순으로 적재한다.
    /// 계정 초기화 시 발급하는 환영 메일의 첨부가 되며, 행이 없으면 지원금 없이 계정만 생성된다.</summary>
    private static async Task<List<NewbieRewardDef>> LoadNewbieRewardsAsync(QueryFactory db)
    {
        var rows = await db.Query("newbie_reward_master")
            .Select("seq", "reward_type", "reward_code", "quantity")
            .OrderBy("seq")
            .GetAsync<NewbieRewardRow>();

        return rows.Select(r => new NewbieRewardDef(r.Seq, r.RewardType, r.RewardCode, r.Quantity)).ToList();
    }

    /// <summary>
    /// inventory_expand_master를 step 오름차순으로 읽어 칸별 확장 비용 목록을 만든다.
    /// 인벤토리 확장은 부가 기능이므로 이 테이블이 없거나 조회에 실패해도 다른 마스터 적재까지 막지 않도록
    /// 자체 try로 감싸고, 실패 시 빈 목록(확장 불가)을 반환한다.
    /// </summary>
    private async Task<List<long>> LoadExpandCostsAsync(QueryFactory db)
    {
        try
        {
            var costs = await db.Query("inventory_expand_master")
                .Select("gold_cost")
                .OrderBy("step")
                .GetAsync<long>();

            return costs.ToList();
        }
        catch (Exception ex)
        {
            _logger.ZLogWarning(ex, $"inventory_expand_master 적재 실패 — 인벤토리 확장은 상한 도달로 처리됩니다.");
            return new List<long>();
        }
    }
}
