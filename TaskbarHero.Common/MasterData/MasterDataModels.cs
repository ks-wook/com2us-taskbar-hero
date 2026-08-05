using System;

namespace TaskbarHero.Common.MasterData
{
    // =====================================================================
    // 마스터 데이터 POCO (서버-클라 공유 계약, 마스터 데이터 기획서 §7.1)
    //
    // - 번들 JSON(Assets/Resources/MasterData/*.json)은 5장 필드를 camelCase 로
    //   표기한 "테이블별 배열"이다. 여기 필드명·타입은 그 JSON 과 1:1 로 대응한다.
    // - Unity JsonUtility 호환을 위해 [Serializable] + public 필드로 정의한다.
    //   (netstandard2.0, Unity 의존성 없음)
    // - 정본: docs/세부/master-data/master-data-{기획서.md §5·§7, schema.sql, 값.md}
    //   추출기: tools/master_data_export.py (EXPORTERS 목록이 파일명↔모델 매핑)
    // =====================================================================

    /// <summary>
    /// 공통 캐릭터 스탯(base_stats / stat_bonus). 없는 필드는 0.
    /// class_master·level_master·item_master 가 공유한다(기획서 5.1).
    /// </summary>
    [Serializable]
    public struct Stats
    {
        public long hp;          // 체력
        public long atk;         // 공격
        public long def;         // 방어
        public float moveSpeed;  // 이동속도
        public float critChance; // 치명확률 (0~1)
        public float critDamage; // 치명데미지 배율 (1.5 = 150%)
        public float cooldown;   // 재사용 대기시간(초, 음수=감소)
    }

    /// <summary>장착 슬롯(equip_slot_master). item_master.equipSlot 이 참조한다.</summary>
    [Serializable]
    public class EquipSlotMaster
    {
        public int slot;         // 슬롯 코드(1~6)
        public string name;      // 슬롯 이름
    }

    /// <summary>등급/희귀도(grade_master). item_master.grade 가 참조한다(1~5).</summary>
    [Serializable]
    public class GradeMaster
    {
        public int grade;        // 등급(1~5, 클수록 고등급)
        public string name;      // 등급 이름(노말·고급·희귀·영웅·전설)
    }

    /// <summary>직업(class_master). player_character.class_code 가 참조한다.</summary>
    [Serializable]
    public class ClassMaster
    {
        public int classCode;
        public string name;
        public string description; // 직업 설명(클라 표시용). 서버는 로드하지 않는다(skill_master.description 과 동일 취급)
        public int unlockType;   // 0:기본 1:해금 2:유료
        public Stats baseStats;
    }

    /// <summary>레벨별 요구 경험치·스탯 보너스·스킬 포인트(level_master).</summary>
    [Serializable]
    public class LevelMaster
    {
        public int level;
        public long requiredExp;  // L→L+1 요구 경험치(최대 레벨은 0)
        public int skillPoints;   // 해당 레벨 도달 시 지급 스킬 포인트
        public Stats statBonus;   // 해당 레벨 누적 스탯 보너스(hp/atk/def 만 값)
    }

    /// <summary>아이템·재화 정의(item_master). player_item.item_code 등이 참조한다.</summary>
    [Serializable]
    public class ItemMaster
    {
        public int itemCode;      // 장비는 5자리 인코딩, 재료 41xxx, 골드 1
        public string name;
        public int itemType;      // 1:장비 2:재료 3:재화(골드)
        public int grade;         // 등급(1~5, FK grade_master)
        public int equipSlot;     // 장비일 때 장착 슬롯, 비장비 0
        public int classReq;      // 착용 가능 클래스(0=제한 없음)
        public int levelReq;      // 착용 요구 레벨(5레벨 단위, 0=제한 없음)
        public int stackMax;      // 최대 겹침 수량(장비 1)
        public Stats baseStats;   // 장비 옵션 스탯(비장비 0)
        public int sellable;      // 거래소 판매 가능(0/1)
        public long basePrice;    // 거래소 기준가(±20% 등록), 0=거래 불가
    }

    /// <summary>
    /// 장비 강화 단계별 규칙(enhance_master). player_item.enhance_level·player_item_equipped.enhance_level 이 참조한다.
    /// 한 행 = "그 단계로 올릴 때의 비용" + "그 단계에 도달했을 때의 스탯 배율"이며, 0단계(미강화)는 배율 1.0이라 행이 없다.
    /// 강화는 실패·하락·파괴가 없다(비용을 내면 확정 상승, 인벤토리/아이템/큐브 기획서 §5.3).
    /// </summary>
    [Serializable]
    public class EnhanceMaster
    {
        public int enhanceLevel;       // 강화 단계(1~10)
        public long cost;              // 이 단계로 올리는 데 드는 재화량
        public int currencyType;       // 소모 재화 item_code(골드 = 1)
        public float statMultiplier;   // 이 단계에서 장비 baseStats 전체에 곱할 배율(1.05 = 105%)
    }

    /// <summary>
    /// 스킬 레벨·타입별 계수 1행(skill_coefficient 자식 테이블).
    /// 번들 JSON 은 SkillMaster.coefs 배열로 직렬화된다.
    /// </summary>
    [Serializable]
    public struct SkillCoef
    {
        public int skillLevel;   // 1~maxLevel
        public int coefType;     // 1:공격 2:버프 3:디버프 4:자원 소모(체력) 5:흡혈
        public float coef;       // 공격=데미지 배율, 버프/디버프=대상 스탯 배율, 자원 소모=현재 체력 대비 소모 비율, 흡혈=가한 피해 대비 회복 비율
        public float duration;   // 효과 지속(초). 공격·자원 소모(즉시 1회)·상시 패시브는 0
    }

    /// <summary>스킬(skill_master). player_skill.skill_code 가 참조한다.</summary>
    [Serializable]
    public class SkillMaster
    {
        public int skillCode;
        public int classCode;     // 소속 직업(FK class_master)
        public string name;
        public string description; // 스킬 설명(클라 표시용)
        public int skillType;     // 1:액티브 2:패시브
        public int statType;      // 버프/디버프가 작용하는 대상 능력치(rune 동일 enum 1~7). 순수 공격 데미지 스킬은 0.
                                  // coefType 4(자원 소모)·5(흡혈)는 대상이 타입으로 확정돼 이 값을 쓰지 않는다
        public SkillCoef[] coefs; // 레벨·타입별 계수(개수 = 타입 수 × maxLevel). 광전사의 힘(402)처럼 한 레벨에 여러 타입을 가질 수 있다
        public int maxLevel;
        public float cooldown;    // 스킬 재사용 대기시간(초). 패시브는 0
    }

    /// <summary>
    /// 룬 레벨별 골드 비용 1행(rune_cost 자식 테이블). 번들 JSON 은 RuneMaster.costs 배열로 직렬화된다.
    /// 클라이언트는 이 값을 그대로 표시하며, 서버도 동일 값으로 비용을 차감한다(공식 파생 아님).
    /// </summary>
    [Serializable]
    public struct RuneCost
    {
        public int level;   // 목표 레벨(1~maxLevel): 이 레벨로 올릴 때 드는 비용
        public long cost;   // 골드 비용
    }

    /// <summary>룬(rune_master, Rune Tree). player_rune.rune_code 가 참조한다.</summary>
    [Serializable]
    public class RuneMaster
    {
        public int runeCode;
        public string name;
        public int prereqCode;    // 선행 룬(0=루트)
        public RuneCost[] costs;  // 레벨별 골드 비용(rune_cost 자식). 개수 = maxLevel
        public int maxLevel;
        public int statType;      // 1:공격력 2:방어력 3:체력 4:치명확률 5:치명피해 6:이동속도 7:재사용 대기시간
        public float statValue;   // 레벨당 누적 상승량(%). stat_type=7(재사용 대기시간)은 감소 방향
    }

    /// <summary>몬스터 전투 스탯(monster_master). 보상은 stage_reward 가 담당.</summary>
    [Serializable]
    public class MonsterMaster
    {
        public int monsterCode;
        public string name;
        public long hp;
        public long attack;
    }

    /// <summary>스테이지 스폰 1행(stage_spawn 자식). StageMaster.spawns 배열로 직렬화.</summary>
    [Serializable]
    public struct Spawn
    {
        public int monsterCode;
        public int count;
    }

    /// <summary>스테이지 구성(stage_master). game_player 의 act/stage/difficulty 가 참조.</summary>
    [Serializable]
    public class StageMaster
    {
        public int stageId;
        public int act;
        public int difficulty;
        public int stage;
        public Spawn[] spawns;       // 등장 일반 몬스터(stage_spawn 자식)
        public int bossMonsterCode;  // 0=보스 없음
    }

    /// <summary>스테이지 클리어 보상(stage_reward). stageId 로 StageMaster 와 1:1.</summary>
    [Serializable]
    public class StageReward
    {
        public int stageId;
        public long rewardGold;
        public long rewardExp;
        public float grade1Prob;   // 등급 1~5 아이템 드롭 확률(0~1, 합<1이면 미드롭)
        public float grade2Prob;
        public float grade3Prob;
        public float grade4Prob;
        public float grade5Prob;
    }

    /// <summary>큐브 레벨별 규칙(cube_master). player_cube.cube_level 이 참조한다.</summary>
    [Serializable]
    public class CubeMaster
    {
        public int cubeLevel;
        public long requiredExp;    // 다음 레벨 요구 경험치(최대 레벨 0)
        public int combineGradeUp;  // 합성 등급 상승 여부(0/1)
        public int combineCount;    // 합성 소모 개수
        public long goldPerScrap;   // 분해 골드 계수
    }

    /// <summary>큐브 제작 소모 재료 1행(cube_recipe_ingredient 자식). CubeRecipe.ingredients 배열.</summary>
    [Serializable]
    public struct CubeIngredient
    {
        public int materialCode;   // 소모 재료(item_master.item_code, item_type=2)
        public int quantity;
    }

    /// <summary>큐브 제작 레시피(cube_recipe).</summary>
    [Serializable]
    public class CubeRecipe
    {
        public int recipeCode;
        public int resultItemCode;      // 제작 결과 아이템(item_master)
        public int resultQuantity;
        public int reqCubeLevel;        // 요구 큐브 레벨(cube_master)
        public long costGold;
        public CubeIngredient[] ingredients;
    }

    /// <summary>
    /// 캐릭터 추가 생성 비용(character_create_cost). characterId = 생성 슬롯(2~3), 1번은 무료라 행 없음.
    /// 클라이언트가 "다음 캐릭터 생성 N 골드" 안내에 사용하고, 서버도 동일 값으로 차감한다.
    /// </summary>
    [Serializable]
    public class CharacterCreateCost
    {
        public int characterId;   // 생성 슬롯(2~3)
        public long goldCost;     // 골드 비용
    }

    /// <summary>
    /// 인벤토리 확장 칸별 골드 비용(inventory_expand_master). step = 기본 용량 이후 여는 칸의 순번(1-based).
    /// 클라이언트가 "다음 칸 확장 N 골드" 안내에 사용하고, 서버도 동일 값으로 차감한다.
    /// </summary>
    [Serializable]
    public class InventoryExpandCost
    {
        public int step;          // 확장 단계(1-based)
        public long goldCost;     // 그 칸을 여는 골드 비용
    }

    /// <summary>출석부 일자별 보상(attendance_master).</summary>
    [Serializable]
    public class AttendanceMaster
    {
        public int day;            // 이달 며칠(1~31)
        public int rewardType;     // 1:골드 2:아이템 3:재화
        public int rewardCode;     // 아이템/재화 코드(item_master.item_code, 없으면 0)
        public long quantity;      // 지급 수량
    }

    /// <summary>가챠 등급별 추첨 가중치(gacha_grade_weight). 확률 = weight / 그 배너의 weight 합.</summary>
    [Serializable]
    public struct GachaGradeWeight
    {
        public int grade;          // 배너 안에서의 추첨 등급 슬롯(1~5)
        public int weight;         // 가중치(정규화하지 않는다)
    }

    /// <summary>가챠 등급 슬롯의 지급 후보 한 건(gacha_item_pool). 슬롯 안에서는 균등 추첨.</summary>
    [Serializable]
    public struct GachaItemPoolEntry
    {
        public int grade;          // 추첨 등급 슬롯(item_master.grade 와 일치할 필요가 없다)
        public int itemCode;       // 지급 아이템(item_master.item_code)
        public int quantity;       // 1회 지급 수량
    }

    /// <summary>
    /// 가챠 천장 규칙 한 건(gacha_pity_rule). 같은 등급에 소프트·하드가 각각 한 행으로 온다.
    /// threshold 는 "이번 뽑기의 회차 번호"(pity_count + 1)와 비교하는 값이다.
    /// </summary>
    [Serializable]
    public struct GachaPityRule
    {
        public int grade;          // 천장 대상 등급
        public int pityType;       // 1:소프트(가중치 가산) 2:하드(확정 지급)
        public int threshold;      // 발동 회차(기준값 소프트 70 / 하드 90)
        public float probStep;     // 소프트 전용 — 발동 후 1회당 올릴 확률(%p, 0~1). 하드는 0
    }

    /// <summary>
    /// 가챠(뽑기) 배너 정의(gacha_master + 자식 3종을 배열로 중첩). 배너 이름·이미지·비용·등급 확률·
    /// 후보 목록은 정적 값이라 이 번들에서 읽고, "지금 열려 있는가 · 내 천장이 얼마인가"만 서버가
    /// 내려준다(가챠 기획서 §5 서두). 노출 판정(isActive·기간)은 서버 권위이므로 클라이언트는 표시에만 쓴다.
    /// </summary>
    [Serializable]
    public class GachaMaster
    {
        public int gachaCode;              // 가챠(배너) 코드
        public string name;                // 배너 이름(UI 표시)
        public string bannerImage;         // 배너 이미지 리소스 키
        public int isActive;               // 노출 스위치(0/1) — 최종 판정은 서버
        public long openAt;                // 노출 시작 Unix ts(0 = 시작 제한 없음)
        public long closeAt;               // 노출 종료 Unix ts(0 = 종료 없음 = 상시 배너)
        public int sortOrder;              // 목록 표시 순서(오름차순)
        public int costCurrencyCode;       // 비용 재화 item_code(골드 = 1)
        public long costSingle;            // 1연 1회 비용
        public long costMulti;             // 10연 1회 비용(묶음 할인 반영 — costSingle × multiCount 와 무관)
        public int multiCount;             // 10연 1회에 뽑는 횟수(현재 10)
        public int multiGuaranteedGrade;   // 10연 보장 최소 등급(0 = 보장 없음)
        public int pickupItemCode;         // 픽업 아이템(0 = 상시 배너)
        public GachaGradeWeight[] gradeWeights;
        public GachaItemPoolEntry[] itemPool;
        public GachaPityRule[] pityRules;
    }
}
