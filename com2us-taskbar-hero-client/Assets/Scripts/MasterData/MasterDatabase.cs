using System;
using System.Collections.Generic;
using TaskbarHero.Common.MasterData;

namespace TaskbarHero.Client.MasterData
{
    /// <summary>
    /// 빌드에 번들된 테이블별 마스터 JSON 배열을 파싱해 "코드→객체" Dictionary 로
    /// 인덱싱해 보관하는 인메모리 캐시(마스터 데이터 기획서 §7.3 확장).
    ///
    /// 추출기(tools/master_data_export.py)가 내보내는 13개 테이블 전부를 담는다.
    /// Resources/파일 IO 에 의존하지 않고 "테이블명→JSON 문자열" 리졸버만 받으므로
    /// 로딩 소스(Resources/StreamingAssets/테스트 등)와 분리된다.
    /// </summary>
    public class MasterDatabase
    {
        // 테이블명(= 번들 JSON 파일명, 확장자 없음). 추출기 EXPORTERS 목록과 1:1.
        public const string TableEquipSlot = "equip_slot_master";
        public const string TableGrade = "grade_master";
        public const string TableClass = "class_master";
        public const string TableLevel = "level_master";
        public const string TableSkill = "skill_master";
        public const string TableRune = "rune_master";
        public const string TableItem = "item_master";
        public const string TableMonster = "monster_master";
        public const string TableStage = "stage_master";
        public const string TableStageReward = "stage_reward";
        public const string TableCube = "cube_master";
        public const string TableCubeRecipe = "cube_recipe";
        public const string TableAttendance = "attendance_master";
        public const string TableInventoryExpand = "inventory_expand_master";
        public const string TableCharacterCreateCost = "character_create_cost";
        public const string TableGacha = "gacha_master";
        public const string TableEnhance = "enhance_master";
        public const string TableBossRushMaster = "boss_rush_master";
        public const string TableBossRushRound = "boss_rush_round";
        public const string TableBossRushRankReward = "boss_rush_rank_reward";

        /// <summary>신규 계정 기본 인벤토리 용량(서버 BaseInventoryCapacity와 동일 계약). 확장 단계 산출에 사용.</summary>
        public const int BaseInventoryCapacity = 100;

        /// <summary>로드해야 하는 모든 테이블명(로더가 리소스 존재 여부 검사에 사용).</summary>
        public static readonly string[] AllTables =
        {
            TableEquipSlot, TableGrade, TableClass, TableLevel, TableSkill, TableRune,
            TableItem, TableMonster, TableStage, TableStageReward, TableCube,
            TableCubeRecipe, TableAttendance, TableInventoryExpand, TableCharacterCreateCost,
            TableGacha, TableEnhance, TableBossRushMaster, TableBossRushRound, TableBossRushRankReward,
        };

        public readonly Dictionary<int, EquipSlotMaster> EquipSlots = new Dictionary<int, EquipSlotMaster>();
        public readonly Dictionary<int, GradeMaster> Grades = new Dictionary<int, GradeMaster>();
        public readonly Dictionary<int, ClassMaster> Classes = new Dictionary<int, ClassMaster>();
        public readonly Dictionary<int, LevelMaster> Levels = new Dictionary<int, LevelMaster>();
        public readonly Dictionary<int, SkillMaster> Skills = new Dictionary<int, SkillMaster>();
        public readonly Dictionary<int, RuneMaster> Runes = new Dictionary<int, RuneMaster>();
        public readonly Dictionary<int, ItemMaster> Items = new Dictionary<int, ItemMaster>();
        public readonly Dictionary<int, MonsterMaster> Monsters = new Dictionary<int, MonsterMaster>();
        public readonly Dictionary<int, StageMaster> Stages = new Dictionary<int, StageMaster>();
        public readonly Dictionary<int, StageReward> StageRewards = new Dictionary<int, StageReward>();
        public readonly Dictionary<int, CubeMaster> Cubes = new Dictionary<int, CubeMaster>();
        public readonly Dictionary<int, CubeRecipe> CubeRecipes = new Dictionary<int, CubeRecipe>();
        public readonly Dictionary<int, AttendanceMaster> Attendances = new Dictionary<int, AttendanceMaster>();
        // 인벤토리 확장 비용(step→비용)·캐릭터 추가 생성 비용(characterId→비용). UI 사전 안내용.
        public readonly Dictionary<int, InventoryExpandCost> InventoryExpandCosts = new Dictionary<int, InventoryExpandCost>();
        public readonly Dictionary<int, CharacterCreateCost> CharacterCreateCosts = new Dictionary<int, CharacterCreateCost>();
        // 가챠(뽑기) 배너 정의(배너 이름·이미지·비용·등급 확률·후보·천장 규칙). 지금 열려 있는 배너 판정은
        // 서버(POST /api/game/gacha/banners)가 하고, 이 표는 그 목록을 그리는 정적 값을 제공한다.
        public readonly Dictionary<int, GachaMaster> Gachas = new Dictionary<int, GachaMaster>();
        // 장비 강화 단계별 비용·스탯 배율(enhance_master, key = 강화 단계 1~10). 서버는 단계만 권위로 확정하고
        // 배율은 응답에 담지 않으므로(§5.3), 표시·전투 계산에 쓰는 배율은 이 표에서 읽는다.
        public readonly Dictionary<int, EnhanceMaster> Enhances = new Dictionary<int, EnhanceMaster>();
        // 보스러시 라운드 정의(round → 배경·일반 몬스터 스폰·보스). 진입 화면의 라운드 보스 줄은 서버 호출 없이
        // 이 표로 그린다 — 실제 전투 스폰은 enter 응답을 따른다(보스러시 UI 기획서 2.1).
        public readonly Dictionary<int, BossRushRoundMaster> BossRushRounds = new Dictionary<int, BossRushRoundMaster>();
        // 보스러시 시즌 순위 보상(rankGroup → 구간·골드). 현재 1~3위 3행뿐이며 4위 이하는 행이 없다.
        public readonly Dictionary<int, BossRushRankReward> BossRushRankRewards = new Dictionary<int, BossRushRankReward>();

        /// <summary>보스러시 전역 규칙(boss_rush_master, 단일 행). 번들에 없으면 null.</summary>
        public BossRushMaster BossRush { get; private set; }

        /// <summary>파싱해 캐싱한 총 행 수(로드 검증·로그용).</summary>
        public int TotalRows { get; private set; }

        /// <summary>
        /// 모든 테이블을 파싱해 캐시를 채운다.
        /// <paramref name="jsonForTable"/> 는 테이블명(위 상수)을 받아 해당 JSON 배열 문자열을 돌려준다.
        /// 재호출 시 기존 캐시를 비우고 다시 채운다(idempotent).
        /// </summary>
        public void Load(Func<string, string> jsonForTable)
        {
            if (jsonForTable == null)
            {
                throw new ArgumentNullException(nameof(jsonForTable));
            }

            Clear();

            Fill(EquipSlots, Parse<EquipSlotMaster>(jsonForTable, TableEquipSlot), x => x.slot);
            Fill(Grades, Parse<GradeMaster>(jsonForTable, TableGrade), x => x.grade);
            Fill(Classes, Parse<ClassMaster>(jsonForTable, TableClass), x => x.classCode);
            Fill(Levels, Parse<LevelMaster>(jsonForTable, TableLevel), x => x.level);
            Fill(Skills, Parse<SkillMaster>(jsonForTable, TableSkill), x => x.skillCode);
            Fill(Runes, Parse<RuneMaster>(jsonForTable, TableRune), x => x.runeCode);
            Fill(Items, Parse<ItemMaster>(jsonForTable, TableItem), x => x.itemCode);
            Fill(Monsters, Parse<MonsterMaster>(jsonForTable, TableMonster), x => x.monsterCode);
            Fill(Stages, Parse<StageMaster>(jsonForTable, TableStage), x => x.stageId);
            Fill(StageRewards, Parse<StageReward>(jsonForTable, TableStageReward), x => x.stageId);
            Fill(Cubes, Parse<CubeMaster>(jsonForTable, TableCube), x => x.cubeLevel);
            Fill(CubeRecipes, Parse<CubeRecipe>(jsonForTable, TableCubeRecipe), x => x.recipeCode);
            Fill(Attendances, Parse<AttendanceMaster>(jsonForTable, TableAttendance), x => x.day);
            Fill(InventoryExpandCosts, Parse<InventoryExpandCost>(jsonForTable, TableInventoryExpand), x => x.step);
            Fill(CharacterCreateCosts, Parse<CharacterCreateCost>(jsonForTable, TableCharacterCreateCost), x => x.characterId);
            Fill(Gachas, Parse<GachaMaster>(jsonForTable, TableGacha), x => x.gachaCode);
            Fill(Enhances, Parse<EnhanceMaster>(jsonForTable, TableEnhance), x => x.enhanceLevel);
            Fill(BossRushRounds, Parse<BossRushRoundMaster>(jsonForTable, TableBossRushRound), x => x.round);
            Fill(BossRushRankRewards, Parse<BossRushRankReward>(jsonForTable, TableBossRushRankReward), x => x.rankGroup);
            var bossRushRows = Parse<BossRushMaster>(jsonForTable, TableBossRushMaster);
            BossRush = bossRushRows != null && bossRushRows.Length > 0 ? bossRushRows[0] : null;

            TotalRows =
                EquipSlots.Count + Grades.Count + Classes.Count + Levels.Count + Skills.Count +
                Runes.Count + Items.Count + Monsters.Count + Stages.Count + StageRewards.Count +
                Cubes.Count + CubeRecipes.Count + Attendances.Count +
                InventoryExpandCosts.Count + CharacterCreateCosts.Count + Gachas.Count +
                Enhances.Count + BossRushRounds.Count + BossRushRankRewards.Count +
                (BossRush != null ? 1 : 0);
        }

        /// <summary>모든 캐시를 비운다.</summary>
        public void Clear()
        {
            EquipSlots.Clear();
            Grades.Clear();
            Classes.Clear();
            Levels.Clear();
            Skills.Clear();
            Runes.Clear();
            Items.Clear();
            Monsters.Clear();
            Stages.Clear();
            StageRewards.Clear();
            Cubes.Clear();
            CubeRecipes.Clear();
            Attendances.Clear();
            InventoryExpandCosts.Clear();
            CharacterCreateCosts.Clear();
            Gachas.Clear();
            Enhances.Clear();
            BossRushRounds.Clear();
            BossRushRankRewards.Clear();
            BossRush = null;
            TotalRows = 0;
        }

        /// <summary>정의된 최대 강화 단계(enhance_master 행 수 = 현재 +10). 번들이 비면 0.</summary>
        public int MaxEnhanceLevel
        {
            get
            {
                int max = 0;
                foreach (var lv in Enhances.Keys)
                {
                    if (lv > max)
                    {
                        max = lv;
                    }
                }
                return max;
            }
        }

        /// <summary>
        /// 강화 단계에 해당하는 장비 옵션 스탯 배율. 0단계(미강화)나 번들에 없는 단계는 1(배율 없음).
        /// <para>서버는 단계만 확정하고 배율은 응답에 담지 않으므로(인벤토리/아이템/큐브 기획서 §5.3),
        /// 표시(상세 팝업·능력치)와 전투 계산 양쪽이 이 값을 같은 기준으로 써야 한다.</para>
        /// </summary>
        public float EnhanceMultiplier(int enhanceLevel)
        {
            if (enhanceLevel <= 0)
            {
                return 1f;
            }
            return Enhances.TryGetValue(enhanceLevel, out var row) && row.statMultiplier > 0f
                ? row.statMultiplier
                : 1f;
        }

        /// <summary>다음 강화 단계(현재 +1) 정의. 상한 도달(정의 없음)이면 null — 서버도 이때 MaxEnhanceReached(4004)로 거부한다.</summary>
        public EnhanceMaster NextEnhance(int currentLevel)
        {
            return Enhances.TryGetValue(currentLevel + 1, out var row) ? row : null;
        }

        /// <summary>현재 용량에서 다음 1칸 확장에 드는 골드 비용. 상한 도달(정의된 step 없음)이면 -1.</summary>
        public long NextExpandCost(int currentCapacity)
        {
            int step = currentCapacity - BaseInventoryCapacity + 1; // 이번에 여는 칸의 순번(1-based)
            return InventoryExpandCosts.TryGetValue(step, out var row) ? row.goldCost : -1L;
        }

        /// <summary>지정 슬롯(2~3) 캐릭터 생성 골드 비용. 1번 슬롯(무료) 등 정의 없으면 0.</summary>
        public long CharacterCreateCostOf(int characterId)
        {
            return CharacterCreateCosts.TryGetValue(characterId, out var row) ? row.goldCost : 0L;
        }

        /// <summary>가챠(배너) 정의. 번들에 없는 코드면 null(서버 마스터가 먼저 갱신된 경우 — 조용히 건너뛴다).</summary>
        public GachaMaster GachaOf(int gachaCode)
        {
            return Gachas.TryGetValue(gachaCode, out var row) ? row : null;
        }

        /// <summary>
        /// 그 배너의 등급별 기본 확률(0~1). 확률 = 등급 가중치 / 가중치 합이며 정규화하지 않는다
        /// (가챠 기획서 6.2). 천장 소프트 가산은 서버가 추첨 시점에 더하므로 여기에는 반영되지 않는다.
        /// </summary>
        public float GachaGradeChance(int gachaCode, int grade)
        {
            var gacha = GachaOf(gachaCode);
            if (gacha == null || gacha.gradeWeights == null)
            {
                return 0f;
            }
            int total = 0;
            int target = 0;
            foreach (var w in gacha.gradeWeights)
            {
                total += w.weight;
                if (w.grade == grade)
                {
                    target = w.weight;
                }
            }
            return total > 0 ? (float)target / total : 0f;
        }

        /// <summary>그 배너의 등급 슬롯에 들어 있는 지급 후보 수(확률 공시의 "후보 N종" 표시용).</summary>
        public int GachaPoolCount(int gachaCode, int grade)
        {
            var gacha = GachaOf(gachaCode);
            if (gacha == null || gacha.itemPool == null)
            {
                return 0;
            }
            int count = 0;
            foreach (var entry in gacha.itemPool)
            {
                if (entry.grade == grade)
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// 그 배너·등급의 천장 규칙 발동 회차. <paramref name="pityType"/>는 1:소프트 2:하드다.
        /// 규칙이 없으면 0("천장 없음")이다.
        /// </summary>
        public int GachaPityThreshold(int gachaCode, int grade, int pityType)
        {
            var gacha = GachaOf(gachaCode);
            if (gacha == null || gacha.pityRules == null)
            {
                return 0;
            }
            foreach (var rule in gacha.pityRules)
            {
                if (rule.grade == grade && rule.pityType == pityType)
                {
                    return rule.threshold;
                }
            }
            return 0;
        }

        /// <summary>
        /// 보스러시 라운드 정의를 <b>라운드 번호 오름차순</b>으로 돌려준다(진입 화면의 보스 5칸은 왼쪽이 라운드 1).
        /// 번들에 표가 없으면 빈 목록이다.
        /// </summary>
        public List<BossRushRoundMaster> BossRushRoundsOrdered()
        {
            var list = new List<BossRushRoundMaster>(BossRushRounds.Count);
            foreach (var row in BossRushRounds.Values)
            {
                list.Add(row);
            }
            list.Sort((a, b) => a.round.CompareTo(b.round));
            return list;
        }

        /// <summary>
        /// 시즌 순위 보상 구간을 <b>상위 구간부터</b> 돌려준다(현재 1위·2위·3위 3행). 4위 이하는 행이 없어
        /// 보상을 받지 않으므로 목록에 나타나지 않는다(보스러시 기획서 4.1).
        /// </summary>
        public List<BossRushRankReward> BossRushRankRewardsOrdered()
        {
            var list = new List<BossRushRankReward>(BossRushRankRewards.Count);
            foreach (var row in BossRushRankRewards.Values)
            {
                list.Add(row);
            }
            list.Sort((a, b) => a.rankGroup.CompareTo(b.rankGroup));
            return list;
        }

        /// <summary>
        /// 캐시 요약(디버그·로드 검증용): 테이블별 건수·총 행수와 중첩 배열
        /// (스킬 계수 / 스테이지 스폰 / 큐브 재료) 파싱 합계를 문자열로 돌려준다.
        /// </summary>
        public string Summary()
        {
            int skillCoefs = 0;
            foreach (var s in Skills.Values)
            {
                skillCoefs += s.coefs != null ? s.coefs.Length : 0;
            }

            int stageSpawns = 0;
            foreach (var s in Stages.Values)
            {
                stageSpawns += s.spawns != null ? s.spawns.Length : 0;
            }

            int cubeIngredients = 0;
            foreach (var r in CubeRecipes.Values)
            {
                cubeIngredients += r.ingredients != null ? r.ingredients.Length : 0;
            }

            int gachaPoolEntries = 0;
            foreach (var g in Gachas.Values)
            {
                gachaPoolEntries += g.itemPool != null ? g.itemPool.Length : 0;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[MasterDatabase] {AllTables.Length}개 테이블 / 총 {TotalRows}행");
            sb.AppendLine(
                $"equipSlots={EquipSlots.Count} grades={Grades.Count} classes={Classes.Count} " +
                $"levels={Levels.Count} skills={Skills.Count} runes={Runes.Count} items={Items.Count} " +
                $"monsters={Monsters.Count} stages={Stages.Count} stageRewards={StageRewards.Count} " +
                $"cubes={Cubes.Count} cubeRecipes={CubeRecipes.Count} attendances={Attendances.Count} " +
                $"gachas={Gachas.Count}");
            sb.AppendLine(
                $"중첩배열: skillCoefs={skillCoefs} stageSpawns={stageSpawns} cubeIngredients={cubeIngredients} " +
                $"gachaPool={gachaPoolEntries}");
            return sb.ToString();
        }

        /// <summary>디버그/검증용: 스킬 코드의 쿨다운(초). 없으면 -1.</summary>
        public float SkillCooldownOf(int skillCode)
        {
            return Skills.TryGetValue(skillCode, out var s) ? s.cooldown : -1f;
        }

        private static T[] Parse<T>(Func<string, string> jsonForTable, string table)
        {
            string json = jsonForTable(table);
            return JsonHelper.FromJsonArray<T>(json);
        }

        private static void Fill<T>(Dictionary<int, T> dict, T[] rows, Func<T, int> keySelector)
        {
            dict.Clear();
            if (rows == null)
            {
                return;
            }

            foreach (var row in rows)
            {
                dict[keySelector(row)] = row;
            }
        }
    }
}
