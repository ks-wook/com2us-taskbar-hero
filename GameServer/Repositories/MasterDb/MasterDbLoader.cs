using GameServer.Models;
using GameServer.Repositories.MasterDb.Interfaces;
using MySqlConnector;
using SqlKata.Compilers;
using SqlKata.Execution;
using TaskbarHero.Common.Dto;
using TaskbarHero.Common.MasterData;
using ZLogger;
using GachaPityRule = GameServer.Models.GachaPityRule;
using GameServer.MasterData;

namespace GameServer.Repositories.MasterDb;

/// <summary>
/// 마스터 DB 적재기. 커넥션 하나를 열어 마스터 테이블 전부를 읽고,
/// 조회에 바로 쓸 수 있는 딕셔너리·파생값으로 바꿔 <see cref="MasterDbSnapshot"/>에 담는다.
/// <para>행 매핑 POCO는 이 파일 안의 <c>file sealed class</c>로 둔다 — 적재 밖에서 쓸 일이 없다.</para>
/// </summary>
public sealed class MasterDbLoader : IMasterDbLoader
{
    private const int ItemTypeEquip = 1;      // item_master.item_type 1:장비
    private const int ItemTypeMaterial = 2;   // 2:재료
    private const int ItemTypeConsumable = 4; // 4:소모품(효과는 consumable_master)

    /// <summary>equip_slot_master의 무기 슬롯. 캐릭터 생성 시 지급하는 기본 장비가 이 슬롯이다.</summary>
    private const int EquipSlotWeapon = 1;

    /// <summary>기본 무기가 만족해야 하는 레벨 제한 상한. 갓 생성한 캐릭터는 레벨 1이므로 그 이하만 장착할 수 있다.</summary>
    private const int StartingCharacterLevel = 1;

    /// <summary>skill_master.skill_type의 액티브 값(1:액티브 2:패시브). 기본 습득 스킬은 액티브 중에서 고른다.</summary>
    private const int SkillTypeActive = 1;

    /// <summary>boss_rush_master는 콘텐츠 1행만 쓴다(보스러시 기획서 4.1).</summary>
    private const int BossRushContentId = 1;

    /// <summary>마스터 DB 연결 문자열. 소비처가 이 클래스뿐이라 별도 팩토리를 두지 않는다.</summary>
    private readonly string _connectionString;

    /// <summary>SqlKata MySQL 컴파일러(원시 SQL 조립 금지 — 쿼리 빌더로만 질의한다).</summary>
    private readonly Compiler _compiler = new MySqlCompiler();

    private readonly ILogger<MasterDbLoader> _logger;

    /// <summary>설정에서 마스터 DB 연결 문자열을 읽고 로거를 주입받는다.</summary>
    public MasterDbLoader(IConfiguration configuration, ILogger<MasterDbLoader> logger)
    {
        _connectionString = configuration.GetConnectionString("MasterDb")
            ?? throw new InvalidOperationException("ConnectionStrings:MasterDb 설정이 없습니다.");
        _logger = logger;
    }

    /// <summary>마스터 테이블 조회용 QueryFactory를 만든다. 적재는 기동 시 1회뿐이라 커넥션도 그때 하나만 연다.</summary>
    private QueryFactory CreateQueryFactory() => new(new MySqlConnection(_connectionString), _compiler);

    /// <summary>커넥션 하나로 마스터 테이블 전부를 읽어 스냅샷을 만든다. 실패는 예외로 올린다.</summary>
    public async Task<MasterDbSnapshot> LoadAllAsync()
    {
        using var db = CreateQueryFactory();

        var stages = await LoadStagesAsync(db);
        var (levelExp, maxLevel, levelSkillPoints) = await LoadLevelsAsync(db);
        var (itemsByGrade, itemsByCode) = await LoadItemsAsync(db);
        var skills = await LoadSkillsAsync(db);

        return new MasterDbSnapshot
        {
            Classes = await LoadClassesAsync(db),
            StagesById = stages,
            RewardsByStageId = await LoadStageRewardsAsync(db),
            LevelRequiredExp = levelExp,
            MaxLevel = maxLevel,
            LevelSkillPoints = levelSkillPoints,
            ItemsByGrade = itemsByGrade,
            ItemsByCode = itemsByCode,
            StartingWeaponByClass = BuildStartingWeapons(itemsByCode),
            ConsumablesByCode = await LoadConsumablesAsync(db),
            EnhanceByLevel = await LoadEnhanceRulesAsync(db),
            SkillsByCode = skills,
            StartingSkillByClass = BuildStartingSkills(skills),
            RunesByCode = await LoadRunesAsync(db),
            RuneCosts = await LoadRuneCostsAsync(db),
            CharacterCreateCosts = await LoadCharacterCreateCostsAsync(db),
            CubeRules = await LoadCubeRulesAsync(db),
            RecipesByCode = await LoadRecipesAsync(db),
            AttendanceByDay = await LoadAttendanceAsync(db),
            MailTemplates = await LoadMailTemplatesAsync(db),
            NewbieRewards = await LoadNewbieRewardsAsync(db),
            GachaByCode = await LoadGachaAsync(db),
            BossRushRule = await LoadBossRushRuleAsync(db),
            BossRushRounds = await LoadBossRushRoundsAsync(db),
            BossRushRankRewards = await LoadBossRushRankRewardsAsync(db),
            // 인벤토리 확장은 부가 기능이라 적재 실패를 흡수한다(테이블 부재 시 상한 도달로 처리).
            ExpandCosts = await LoadExpandCostsAsync(db),
        };
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
            .Select("stage_id", "act", "difficulty", "stage", "background_type")
            .GetAsync<StageMasterRow>();

        var spawnRows = await db.Query("stage_spawn")
            .Select("stage_id", "monster_code", "monster_level", "spawn_count", "is_boss")
            .OrderBy("stage_id", "monster_code")
            .GetAsync<StageSpawnRow>();

        // stage_spawn 은 일반 몬스터와 보스를 한 테이블에 담는다(is_boss). 스테이지 정의는 둘을
        // 나눠 들고 있으므로 여기서 갈라 담는다 — 보스 행은 스테이지당 최대 1개다.
        var spawnsByStage = new Dictionary<int, List<StageSpawnDto>>();
        var bossByStage = new Dictionary<int, (int Code, int Level)>();
        foreach (var sp in spawnRows)
        {
            if (sp.IsBoss != 0)
            {
                bossByStage[sp.StageId] = (sp.MonsterCode, sp.MonsterLevel);
                continue;
            }

            if (!spawnsByStage.TryGetValue(sp.StageId, out var list))
            {
                list = new List<StageSpawnDto>();
                spawnsByStage[sp.StageId] = list;
            }

            list.Add(new StageSpawnDto
            {
                monsterCode = sp.MonsterCode,
                monsterLevel = sp.MonsterLevel,
                count = sp.SpawnCount,
            });
        }

        var stages = new Dictionary<int, StageDef>();
        foreach (var row in stageRows)
        {
            spawnsByStage.TryGetValue(row.StageId, out var spawns);
            bossByStage.TryGetValue(row.StageId, out var boss);
            stages[row.StageId] = new StageDef(
                row.StageId,
                row.Act,
                row.Difficulty,
                row.Stage,
                boss.Code,
                boss.Level,
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
    /// <summary>
    /// 보스러시 전역 규칙을 읽는다(boss_rush_master, 단일 행 content_id=1). 행이 없으면 null을 돌려주고
    /// 호출측이 콘텐츠를 잠근다(BossRushLocked 대신 MasterDataNotLoaded로 안내하지 않도록 서비스가 판단).
    /// </summary>
    private static async Task<BossRushRuleDef?> LoadBossRushRuleAsync(QueryFactory db)
    {
        var row = await db.Query("boss_rush_master")
            .Select("round_count", "unlock_stage_sequence",
                    "season_period_days", "run_expire_sec", "rank_page_limit")
            .Where("content_id", BossRushContentId)
            .FirstOrDefaultAsync<BossRushMasterRow>();

        return row is null
            ? null
            : new BossRushRuleDef(
                row.RoundCount, row.UnlockStageSequence,
                row.SeasonPeriodDays, row.RunExpireSec, row.RankPageLimit);
    }

    /// <summary>
    /// 보스러시 라운드 정의를 읽는다(boss_rush_round + 자식 boss_rush_spawn). stage_master ↔ stage_spawn과
    /// 같은 부모-자식 구조이며, 자식 행을 is_boss로 갈라 일반 스폰 목록과 보스로 나눠 담는다
    /// (보스 행은 라운드당 최대 1개).
    /// </summary>
    private static async Task<Dictionary<int, BossRushRoundDef>> LoadBossRushRoundsAsync(QueryFactory db)
    {
        var roundRows = await db.Query("boss_rush_round")
            .Select("round", "background_type")
            .OrderBy("round")
            .GetAsync<BossRushRoundRow>();

        var spawnRows = await db.Query("boss_rush_spawn")
            .Select("round", "monster_code", "monster_level", "spawn_count", "is_boss")
            .OrderBy("round", "monster_code")
            .GetAsync<BossRushSpawnRow>();

        var spawnsByRound = new Dictionary<int, List<BossRushSpawnEntry>>();
        var bossByRound = new Dictionary<int, (int Code, int Level)>();
        foreach (var sp in spawnRows)
        {
            if (sp.IsBoss != 0)
            {
                bossByRound[sp.Round] = (sp.MonsterCode, sp.MonsterLevel);
                continue;
            }

            if (!spawnsByRound.TryGetValue(sp.Round, out var list))
            {
                list = new List<BossRushSpawnEntry>();
                spawnsByRound[sp.Round] = list;
            }

            list.Add(new BossRushSpawnEntry(sp.MonsterCode, sp.MonsterLevel, sp.SpawnCount));
        }

        var rounds = new Dictionary<int, BossRushRoundDef>();
        foreach (var row in roundRows)
        {
            spawnsByRound.TryGetValue(row.Round, out var spawns);
            bossByRound.TryGetValue(row.Round, out var boss);
            rounds[row.Round] = new BossRushRoundDef(
                row.Round, row.BackgroundType, boss.Code, boss.Level,
                spawns ?? new List<BossRushSpawnEntry>());
        }

        return rounds;
    }

    /// <summary>
    /// 보스러시 시즌 순위 보상 구간을 읽는다(boss_rush_rank_reward). 상위 구간이 먼저 오도록 rank_group
    /// 오름차순으로 담아, 정산이 앞에서부터 순위를 매칭한다. 4위 이하는 행이 없어 매칭에서 빠진다.
    /// </summary>
    private static async Task<List<BossRushRankRewardDef>> LoadBossRushRankRewardsAsync(QueryFactory db)
    {
        var rows = await db.Query("boss_rush_rank_reward")
            .Select("rank_group", "rank_from", "rank_to", "reward_gold")
            .OrderBy("rank_group")
            .GetAsync<BossRushRankRewardRow>();

        return rows
            .Select(r => new BossRushRankRewardDef(r.RankGroup, r.RankFrom, r.RankTo, r.RewardGold))
            .ToList();
    }

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

    /// <summary>
    /// 적재한 아이템 정의에서 <b>직업별 기본 무기</b>(캐릭터 생성 시 지급·장착할 무기)를 파생한다.
    /// 별도 마스터 테이블을 두지 않고 item_master에서 고르므로, 무기를 추가·수정해도 이 규칙이 그대로 따라간다.
    /// <para>후보 = 장비(item_type=1) · 무기 슬롯(equip_slot=1) · 그 직업 전용(class_req=class_code, 공용 0은 무기에 없음)
    /// · 레벨 1이 장착 가능(level_req ≤ 1)인 아이템. 그중 <b>등급이 가장 낮고 동급이면 item_code가 작은</b> 것을 고른다.</para>
    /// </summary>
    private static Dictionary<int, ItemDef> BuildStartingWeapons(IReadOnlyDictionary<int, ItemDef> itemsByCode)
        => itemsByCode.Values
            .Where(i => i.ItemType == ItemTypeEquip
                        && i.EquipSlot == EquipSlotWeapon
                        && i.ClassReq != 0
                        && i.LevelReq <= StartingCharacterLevel)
            .GroupBy(i => i.ClassReq)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(i => i.Grade).ThenBy(i => i.ItemCode).First());

    /// <summary>
    /// 적재한 스킬 정의에서 <b>직업별 기본 습득 스킬</b>(캐릭터 생성 시 레벨 1로 습득·장착할 액티브 스킬)을 파생한다.
    /// 기본 무기와 같은 방식으로 별도 마스터 테이블 없이 skill_master에서 고르므로, 스킬을 추가·수정해도 규칙이 그대로 따라간다.
    /// <para>후보 = 그 직업의 액티브 스킬(skill_type=1). 그중 <b>skill_code가 가장 작은</b> 것(코드 규약상 각 직업의 첫 액티브 x01)을 고른다.</para>
    /// </summary>
    private static Dictionary<int, SkillDef> BuildStartingSkills(IReadOnlyDictionary<int, SkillDef> skillsByCode)
        => skillsByCode.Values
            .Where(s => s.SkillType == SkillTypeActive)
            .GroupBy(s => s.ClassCode)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(s => s.SkillCode).First());

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
