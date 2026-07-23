using GameServer.Data;
using SqlKata.Execution;
using TaskbarHero.Common.Dto;
using TaskbarHero.Common.MasterData;

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

/// <summary>아이템 정의(item_master). 장착 검증(타입·슬롯·클래스·레벨)과 드롭/스택에 사용한다.</summary>
public sealed record ItemDef(int ItemCode, int ItemType, int Grade, int StackMax, int EquipSlot, int ClassReq, int LevelReq);

/// <summary>스킬 정의(skill_master). 성장 검증(직업 소속·액티브/패시브·최대 레벨)에 사용한다. SkillType 1:액티브 2:패시브.</summary>
public sealed record SkillDef(int SkillCode, int ClassCode, int SkillType, int MaxLevel);

/// <summary>룬 정의(rune_master). 성장 검증(선행 룬·최대 레벨)에 사용한다. 레벨별 골드 비용은 rune_cost(자식)에서 조회한다.</summary>
public sealed record RuneDef(int RuneCode, int PrereqCode, int MaxLevel);

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

file sealed class ItemMasterRow
{
    public int ItemCode { get; set; }
    public int ItemType { get; set; }
    public int Grade { get; set; }
    public int StackMax { get; set; }
    public int EquipSlot { get; set; }
    public int ClassReq { get; set; }
    public int LevelReq { get; set; }
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

    // 드롭 풀: 등급 → 드롭 가능 아이템 코드 목록(재화 item_type=3 제외). 코드 → 아이템 정의.
    private IReadOnlyDictionary<int, List<int>> _itemsByGrade = new Dictionary<int, List<int>>();
    private IReadOnlyDictionary<int, ItemDef> _itemsByCode = new Dictionary<int, ItemDef>();

    // 인벤토리 확장 비용: index i(0-based) = 기본 용량 이후 (i+1)번째 칸을 여는 골드 비용(inventory_expand_master, step 오름차순).
    // 배열 길이 = 확장 가능한 총 칸 수이며, 상한 용량 = BaseInventoryCapacity + 길이.
    private IReadOnlyList<long> _expandCosts = new List<long>();

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

    /// <summary>item_code의 아이템 정의(타입·등급·스택·장착 슬롯·클래스/레벨 제한). 재화(type 3)·미로드 코드는 null.</summary>
    public ItemDef? GetItem(int itemCode)
        => _itemsByCode.TryGetValue(itemCode, out var def) ? def : null;

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
            _skillsByCode = await LoadSkillsAsync(db);
            _runesByCode = await LoadRunesAsync(db);
            _runeCosts = await LoadRuneCostsAsync(db);

            // 인벤토리 확장은 부가 기능이라 별도 try로 감싼다(테이블 부재 시 다른 마스터 적재까지 실패하지 않도록).
            _expandCosts = await LoadExpandCostsAsync(db);

            if (_classes.Count == 0 || _stagesById.Count == 0)
            {
                throw new InvalidOperationException("필수 마스터(class_master/stage_master)가 비어 있습니다.");
            }

            IsLoaded = true;
            _logger.LogInformation(
                "마스터 데이터 적재 완료: class {Classes} · stage {Stages} · reward {Rewards} · level {Levels} · dropGrades {Grades} · expandSlots {Expand} · skill {Skills} · rune {Runes} · runeCost {RuneCosts}",
                _classes.Count, _stagesById.Count, _rewardsByStageId.Count, _levelRequiredExp.Count, _itemsByGrade.Count, _expandCosts.Count, _skillsByCode.Count, _runesByCode.Count, _runeCosts.Count);
        }
        catch (Exception ex)
        {
            IsLoaded = false;
            _logger.LogError(ex, "마스터 데이터 적재 실패. 관련 요청은 MasterDataNotLoaded(10001)로 처리됩니다.");
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

    private static async Task<(Dictionary<int, List<int>>, Dictionary<int, ItemDef>)> LoadItemsAsync(QueryFactory db)
    {
        // 드롭 대상은 장비(1)·재료(2)만. 재화(3, 골드)는 제외. 장착 검증용으로 슬롯·클래스/레벨 제한도 함께 적재.
        var rows = await db.Query("item_master")
            .Select("item_code", "item_type", "grade", "stack_max", "equip_slot", "class_req", "level_req")
            .WhereIn("item_type", new[] { 1, 2 })
            .GetAsync<ItemMasterRow>();

        var byGrade = new Dictionary<int, List<int>>();
        var byCode = new Dictionary<int, ItemDef>();
        foreach (var row in rows)
        {
            byCode[row.ItemCode] = new ItemDef(
                row.ItemCode,
                row.ItemType,
                row.Grade,
                row.StackMax,
                row.EquipSlot,
                row.ClassReq,
                row.LevelReq);

            if (!byGrade.TryGetValue(row.Grade, out var list))
            {
                list = new List<int>();
                byGrade[row.Grade] = list;
            }

            list.Add(row.ItemCode);
        }

        return (byGrade, byCode);
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
            _logger.LogWarning(ex, "inventory_expand_master 적재 실패 — 인벤토리 확장은 상한 도달로 처리됩니다.");
            return new List<long>();
        }
    }
}
