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

/// <summary>스테이지 클리어 보상 정의. GradeProbs[0..4] = 등급 1~5 드롭 확률.</summary>
public sealed record StageRewardDef(long Gold, long Exp, double[] GradeProbs);

/// <summary>드롭 추첨 결과(전리품 1개).</summary>
public sealed record DroppedItem(int ItemCode, long Quantity, int ItemType, int StackMax);

/// <summary>
/// 마스터(정적) 데이터 인메모리 캐시. 서버 기동 시 마스터 DB에서 코드→정의 딕셔너리로 적재한다.
/// class_master(직업)에 더해 스테이지 진행/전투(스테이지 진입·클리어)에 필요한 마스터를 적재한다:
///   stage_master(+stage_spawn) · stage_reward · level_master · item_master(등급별 드롭 풀).
/// 로드 실패 시 IsLoaded=false로 두고, 관련 요청은 MasterDataNotLoaded(10001)로 처리한다.
/// </summary>
public sealed class MasterDataProvider
{
    private readonly MasterDbFactory _masterDbFactory;
    private readonly ILogger<MasterDataProvider> _logger;

    private IReadOnlyDictionary<int, ClassMaster> _classes = new Dictionary<int, ClassMaster>();
    private IReadOnlyDictionary<int, StageDef> _stagesById = new Dictionary<int, StageDef>();
    private IReadOnlyDictionary<int, StageRewardDef> _rewardsByStageId = new Dictionary<int, StageRewardDef>();
    private IReadOnlyDictionary<int, long> _levelRequiredExp = new Dictionary<int, long>();
    private int _maxLevel = 1;

    // 드롭 풀: 등급 → 드롭 가능 아이템 코드 목록(재화 item_type=3 제외). 아이템 정보(타입·스택).
    private IReadOnlyDictionary<int, List<int>> _itemsByGrade = new Dictionary<int, List<int>>();
    private IReadOnlyDictionary<int, (int itemType, int stackMax)> _itemInfo = new Dictionary<int, (int, int)>();

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

    /// <summary>레벨 L에서 L+1로 가는 데 필요한 경험치. 최대 레벨 이상은 0(더 오르지 않음).</summary>
    public long LevelRequiredExp(int level)
        => _levelRequiredExp.TryGetValue(level, out var req) ? req : 0;

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
                var info = _itemInfo[itemCode];
                return new DroppedItem(itemCode, 1, info.itemType, info.stackMax);
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
            (_levelRequiredExp, _maxLevel) = await LoadLevelsAsync(db);
            (_itemsByGrade, _itemInfo) = await LoadItemsAsync(db);

            if (_classes.Count == 0 || _stagesById.Count == 0)
            {
                throw new InvalidOperationException("필수 마스터(class_master/stage_master)가 비어 있습니다.");
            }

            IsLoaded = true;
            _logger.LogInformation(
                "마스터 데이터 적재 완료: class {Classes} · stage {Stages} · reward {Rewards} · level {Levels} · dropGrades {Grades}",
                _classes.Count, _stagesById.Count, _rewardsByStageId.Count, _levelRequiredExp.Count, _itemsByGrade.Count);
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
            .GetAsync();

        var classes = new Dictionary<int, ClassMaster>();
        foreach (var row in rows)
        {
            var master = new ClassMaster
            {
                classCode = Convert.ToInt32(row.class_code),
                name = (string)row.name,
                unlockType = Convert.ToInt32(row.unlock_type),
                baseStats = new Stats
                {
                    hp = Convert.ToInt64(row.hp),
                    atk = Convert.ToInt64(row.atk),
                    def = Convert.ToInt64(row.def),
                    moveSpeed = Convert.ToSingle(row.move_speed),
                    critChance = Convert.ToSingle(row.crit_chance),
                    critDamage = Convert.ToSingle(row.crit_damage),
                    cooldown = Convert.ToSingle(row.cooldown),
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
            .GetAsync();

        var spawnRows = await db.Query("stage_spawn")
            .Select("stage_id", "monster_code", "spawn_count")
            .OrderBy("stage_id", "monster_code")
            .GetAsync();

        var spawnsByStage = new Dictionary<int, List<StageSpawnDto>>();
        foreach (var sp in spawnRows)
        {
            int stageId = Convert.ToInt32(sp.stage_id);
            if (!spawnsByStage.TryGetValue(stageId, out var list))
            {
                list = new List<StageSpawnDto>();
                spawnsByStage[stageId] = list;
            }

            list.Add(new StageSpawnDto
            {
                monsterCode = Convert.ToInt32(sp.monster_code),
                count = Convert.ToInt32(sp.spawn_count),
            });
        }

        var stages = new Dictionary<int, StageDef>();
        foreach (var row in stageRows)
        {
            int stageId = Convert.ToInt32(row.stage_id);
            spawnsByStage.TryGetValue(stageId, out var spawns);
            stages[stageId] = new StageDef(
                stageId,
                Convert.ToInt32(row.act),
                Convert.ToInt32(row.difficulty),
                Convert.ToInt32(row.stage),
                Convert.ToInt32(row.boss_monster_code),
                Convert.ToInt32(row.background_type),
                spawns ?? new List<StageSpawnDto>());
        }

        return stages;
    }

    private static async Task<Dictionary<int, StageRewardDef>> LoadStageRewardsAsync(QueryFactory db)
    {
        var rows = await db.Query("stage_reward")
            .Select("stage_id", "reward_gold", "reward_exp",
                    "grade1_prob", "grade2_prob", "grade3_prob", "grade4_prob", "grade5_prob")
            .GetAsync();

        var rewards = new Dictionary<int, StageRewardDef>();
        foreach (var row in rows)
        {
            int stageId = Convert.ToInt32(row.stage_id);
            rewards[stageId] = new StageRewardDef(
                Convert.ToInt64(row.reward_gold),
                Convert.ToInt64(row.reward_exp),
                new double[]
                {
                    Convert.ToDouble(row.grade1_prob),
                    Convert.ToDouble(row.grade2_prob),
                    Convert.ToDouble(row.grade3_prob),
                    Convert.ToDouble(row.grade4_prob),
                    Convert.ToDouble(row.grade5_prob),
                });
        }

        return rewards;
    }

    private static async Task<(Dictionary<int, long>, int)> LoadLevelsAsync(QueryFactory db)
    {
        var rows = await db.Query("level_master").Select("level", "required_exp").GetAsync();
        var byLevel = new Dictionary<int, long>();
        var maxLevel = 1;
        foreach (var row in rows)
        {
            int level = Convert.ToInt32(row.level);
            byLevel[level] = Convert.ToInt64(row.required_exp);
            if (level > maxLevel)
            {
                maxLevel = level;
            }
        }

        return (byLevel, maxLevel);
    }

    private static async Task<(Dictionary<int, List<int>>, Dictionary<int, (int, int)>)> LoadItemsAsync(QueryFactory db)
    {
        // 드롭 대상은 장비(1)·재료(2)만. 재화(3, 골드)는 제외.
        var rows = await db.Query("item_master")
            .Select("item_code", "item_type", "grade", "stack_max")
            .WhereIn("item_type", new[] { 1, 2 })
            .GetAsync();

        var byGrade = new Dictionary<int, List<int>>();
        var info = new Dictionary<int, (int, int)>();
        foreach (var row in rows)
        {
            int itemCode = Convert.ToInt32(row.item_code);
            int itemType = Convert.ToInt32(row.item_type);
            int grade = Convert.ToInt32(row.grade);
            int stackMax = Convert.ToInt32(row.stack_max);

            info[itemCode] = (itemType, stackMax);
            if (!byGrade.TryGetValue(grade, out var list))
            {
                list = new List<int>();
                byGrade[grade] = list;
            }

            list.Add(itemCode);
        }

        return (byGrade, info);
    }
}
