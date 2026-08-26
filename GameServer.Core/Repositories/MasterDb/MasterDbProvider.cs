using System.Diagnostics;
using GameServer.Logging;
using GameServer.Models;
using GameServer.Repositories.MasterDb.Interfaces;
using TaskbarHero.Common.Dto;
using TaskbarHero.Common.MasterData;
using ZLogger;
using GachaPityRule = GameServer.Models.GachaPityRule;
using GameServer.MasterData;

namespace GameServer.Repositories.MasterDb;

/// <summary>
/// 마스터(정적) 데이터 인메모리 캐시. 서버 기동 시 마스터 DB에서 코드→정의 딕셔너리로 적재한다.
/// class_master(직업)에 더해 스테이지 진행/전투(스테이지 진입·클리어)에 필요한 마스터를 적재한다:
///   stage_master(+stage_spawn) · stage_reward · level_master · item_master(등급별 드롭 풀).
/// 로드 실패 시 IsLoaded=false로 두고, 관련 요청은 MasterDataNotLoaded(10001)로 처리한다.
/// </summary>
public sealed class MasterDbProvider
{
    private readonly IMasterDbLoader _loader;
    private readonly ILogger<MasterDbProvider> _logger;
    private readonly IEventLogger _eventLogger;

    private IReadOnlyDictionary<int, ClassMaster> _classes = new Dictionary<int, ClassMaster>();
    private IReadOnlyDictionary<int, StageDef> _stagesById = new Dictionary<int, StageDef>();
    private IReadOnlyDictionary<int, StageRewardDef> _rewardsByStageId = new Dictionary<int, StageRewardDef>();
    private IReadOnlyDictionary<int, long> _levelRequiredExp = new Dictionary<int, long>();
    private IReadOnlyDictionary<int, int> _levelSkillPoints = new Dictionary<int, int>();
    private int _maxLevel = 1;

    // 성장(스킬·룬) 정의: 코드 → 정의.
    private IReadOnlyDictionary<int, SkillDef> _skillsByCode = new Dictionary<int, SkillDef>();

    // 클래스별 기본 습득 스킬: class_code → 그 직업의 첫 번째 액티브 스킬(skill_type=1 중 skill_code가 가장 작은 것) 정의.
    //   캐릭터 생성 시 이 스킬을 레벨 1로 습득·장착한 상태로 시작한다(skill_master에서 파생).
    private IReadOnlyDictionary<int, SkillDef> _startingSkillByClass = new Dictionary<int, SkillDef>();
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

    // 클래스별 기본 무기: class_code → 그 직업 전용 무기(equip_slot=1) 중 가장 등급이 낮은 아이템 정의.
    //   캐릭터 생성 시 이 무기를 1개 지급하고 무기 슬롯에 장착한 상태로 시작한다(item_master에서 파생).
    private IReadOnlyDictionary<int, ItemDef> _startingWeaponByClass = new Dictionary<int, ItemDef>();

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

    /// <summary>보스러시 전역 규칙. 마스터에 행이 없으면 null(콘텐츠 미구성).</summary>
    private BossRushRuleDef? _bossRushRule;

    /// <summary>보스러시 라운드 정의(round → 배경·스폰·보스).</summary>
    private IReadOnlyDictionary<int, BossRushRoundDef> _bossRushRounds = new Dictionary<int, BossRushRoundDef>();

    /// <summary>보스러시 시즌 순위 보상 구간(rank_group 오름차순 = 상위 구간 먼저).</summary>
    private IReadOnlyList<BossRushRankRewardDef> _bossRushRankRewards = new List<BossRushRankRewardDef>();

    /// <summary>보스러시 전역 규칙(제한 시간·일일 횟수·해금 순번 등). 마스터에 없으면 null.</summary>
    public BossRushRuleDef? BossRushRule => _bossRushRule;

    /// <summary>
    /// 보스러시 라운드 구성을 라운드 번호 순으로 반환한다. 전역 규칙의 round_count만큼만 내려주므로
    /// 마스터에 여분 라운드가 있어도 응답에 섞이지 않는다(빈 목록이면 콘텐츠 미구성).
    /// </summary>
    public IReadOnlyList<BossRushRoundDef> BossRushRounds()
    {
        var count = _bossRushRule?.RoundCount ?? 0;
        if (count <= 0)
        {
            return Array.Empty<BossRushRoundDef>();
        }

        var rounds = new List<BossRushRoundDef>(count);
        for (var round = 1; round <= count; round++)
        {
            if (_bossRushRounds.TryGetValue(round, out var def))
            {
                rounds.Add(def);
            }
        }

        return rounds;
    }

    /// <summary>지정 순위가 속한 시즌 순위 보상 구간. 매칭 구간이 없으면(4위 이하) null = 보상 없음.</summary>
    public BossRushRankRewardDef? BossRushRankRewardFor(int rank)
        => _bossRushRankRewards.FirstOrDefault(r => r.Contains(rank));

    /// <summary>적재기와 로거를 주입받는다. 이 클래스는 DB를 직접 만지지 않는다(적재는 로더 몫).</summary>
    public MasterDbProvider(IMasterDbLoader loader, ILogger<MasterDbProvider> logger, IEventLogger eventLogger)
    {
        _loader = loader;
        _logger = logger;
        _eventLogger = eventLogger;
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

    /// <summary>
    /// 직업의 <b>기본 무기</b> 정의(캐릭터 생성 시 지급·장착). 그 직업 전용 무기(item_type=1·equip_slot=1·class_req=classCode)
    /// 가운데 등급이 가장 낮고(동급이면 item_code가 작은) 아이템이며, 레벨 1이 장착할 수 없는 무기(level_req &gt; 1)는 후보에서 제외한다.
    /// 후보가 없으면(직업 전용 무기 미정의) null — 이때는 맨손으로 생성한다.
    /// </summary>
    public ItemDef? StartingWeapon(int classCode)
        => _startingWeaponByClass.TryGetValue(classCode, out var def) ? def : null;

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
        var step = currentCapacity - Constants.Inventory.BaseCapacity + 1; // 이번에 열 칸의 순번(1-based)
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

    /// <summary>
    /// 직업의 <b>기본 습득 스킬</b> 정의(캐릭터 생성 시 레벨 1로 습득·장착). 그 직업의 액티브 스킬(skill_type=1) 가운데
    /// <b>skill_code가 가장 작은</b>(= 첫 번째) 스킬이며, 후보가 없으면(액티브 미정의) null — 이때는 스킬 없이 생성한다.
    /// </summary>
    public SkillDef? StartingSkill(int classCode)
        => _startingSkillByClass.TryGetValue(classCode, out var def) ? def : null;

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
            .Where(d => d.ItemType == Constants.ItemType.Equip && d.Grade == inputGrade + 1)
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
            .Where(r => r.PityType == Constants.Gacha.PityTypeHard && PullNo(r.Grade) >= r.Threshold)
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
        foreach (var rule in banner.PityRules.Where(r => r.PityType == Constants.Gacha.PityTypeSoft))
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

    /// <summary>
    /// 마스터 데이터를 인메모리로 적재한다(기동 시 1회). 적재 자체는 <see cref="IMasterDbLoader"/>가 하고,
    /// 이 메서드는 결과를 받아 담은 뒤 <b>필수 마스터 검증과 적재 성공 여부(IsLoaded)</b>를 판정한다.
    /// 실패는 Error로 남기고 IsLoaded=false로 두어 관련 요청이 MasterDataNotLoaded(10001)가 되게 한다.
    /// </summary>
    public async Task LoadAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var snapshot = await _loader.LoadAllAsync();

            _classes = snapshot.Classes;
            _stagesById = snapshot.StagesById;
            _rewardsByStageId = snapshot.RewardsByStageId;
            _levelRequiredExp = snapshot.LevelRequiredExp;
            _maxLevel = snapshot.MaxLevel;
            _levelSkillPoints = snapshot.LevelSkillPoints;
            _itemsByGrade = snapshot.ItemsByGrade;
            _itemsByCode = snapshot.ItemsByCode;
            _startingWeaponByClass = snapshot.StartingWeaponByClass;
            _consumablesByCode = snapshot.ConsumablesByCode;
            _enhanceByLevel = snapshot.EnhanceByLevel;
            _skillsByCode = snapshot.SkillsByCode;
            _startingSkillByClass = snapshot.StartingSkillByClass;
            _runesByCode = snapshot.RunesByCode;
            _runeCosts = snapshot.RuneCosts;
            _characterCreateCosts = snapshot.CharacterCreateCosts;
            _cubeRules = snapshot.CubeRules;
            _recipesByCode = snapshot.RecipesByCode;
            _attendanceByDay = snapshot.AttendanceByDay;
            _mailTemplates = snapshot.MailTemplates;
            _newbieRewards = snapshot.NewbieRewards;
            _gachaByCode = snapshot.GachaByCode;
            _bossRushRule = snapshot.BossRushRule;
            _bossRushRounds = snapshot.BossRushRounds;
            _bossRushRankRewards = snapshot.BossRushRankRewards;
            _expandCosts = snapshot.ExpandCosts;

            if (_classes.Count == 0 || _stagesById.Count == 0)
            {
                throw new InvalidOperationException("필수 마스터(class_master/stage_master)가 비어 있습니다.");
            }

            IsLoaded = true;
            stopwatch.Stop();

            // 적재 이벤트(5.11). 테이블별 행 수를 컬럼으로 펼치지 않고 두 값으로 요약한다 —
            // 마스터가 늘 때마다 로그 스키마가 따라 늘어나는 구조를 만들지 않기 위해서다.
            var loadedCounts = LoadedCounts();
            _eventLogger.Action(
                Constants.EventLog.Tags.MasterLoad, null,
                new MasterLoadEvent(loadedCounts.Length, loadedCounts.Sum(), stopwatch.ElapsedMilliseconds));

            _logger.ZLogInformation($"마스터 데이터 적재 완료: class {_classes.Count:@Classes} · stage {_stagesById.Count:@Stages} · reward {_rewardsByStageId.Count:@Rewards} · level {_levelRequiredExp.Count:@Levels} · item {_itemsByCode.Count:@Items} · dropGrades {_itemsByGrade.Count:@Grades} · consumable {_consumablesByCode.Count:@Consumables} · enhance {_enhanceByLevel.Count:@Enhances} · expandSlots {_expandCosts.Count:@Expand} · skill {_skillsByCode.Count:@Skills} · rune {_runesByCode.Count:@Runes} · runeCost {_runeCosts.Count:@RuneCosts} · charCost {_characterCreateCosts.Count:@CharCosts} · cube {_cubeRules.Count:@Cubes} · recipe {_recipesByCode.Count:@Recipes} · attendance {_attendanceByDay.Count:@Attendances} · mailTemplate {_mailTemplates.Count:@MailTemplates} · newbieReward {_newbieRewards.Count:@NewbieRewards} · gacha {_gachaByCode.Count:@Gachas} · bossRushRound {_bossRushRounds.Count:@BossRushRounds} · bossRushRankReward {_bossRushRankRewards.Count:@BossRushRankRewards}");
        }
        catch (Exception ex)
        {
            IsLoaded = false;
            _logger.ZLogError(ex, $"마스터 데이터 적재 실패. 관련 요청은 MasterDataNotLoaded(10001)로 처리됩니다.");
        }
    }

    /// <summary>
    /// 적재된 마스터 컬렉션별 행 수. 길이가 곧 <c>table_count</c>, 합이 <c>row_count</c>이며
    /// <c>master.load</c> 이벤트의 두 값을 만든다(5.11). 마스터를 추가하면 여기에도 한 줄 넣는다.
    /// </summary>
    private int[] LoadedCounts() => new[]
    {
        _classes.Count, _stagesById.Count, _rewardsByStageId.Count, _levelRequiredExp.Count,
        _levelSkillPoints.Count, _itemsByCode.Count, _itemsByGrade.Count, _startingWeaponByClass.Count,
        _consumablesByCode.Count, _enhanceByLevel.Count, _expandCosts.Count, _skillsByCode.Count,
        _startingSkillByClass.Count, _runesByCode.Count, _runeCosts.Count, _characterCreateCosts.Count,
        _cubeRules.Count, _recipesByCode.Count, _attendanceByDay.Count, _mailTemplates.Count,
        _newbieRewards.Count, _gachaByCode.Count, _bossRushRounds.Count, _bossRushRankRewards.Count,
    };
}
