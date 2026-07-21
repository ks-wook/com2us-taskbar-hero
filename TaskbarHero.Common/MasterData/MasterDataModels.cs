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
    /// 스킬 레벨·타입별 계수 1행(skill_coefficient 자식 테이블).
    /// 번들 JSON 은 SkillMaster.coefs 배열로 직렬화된다.
    /// </summary>
    [Serializable]
    public struct SkillCoef
    {
        public int skillLevel;   // 1~maxLevel
        public int coefType;     // 1:공격 2:버프 3:디버프
        public float coef;       // 공격=데미지 배율, 버프/디버프=대상 스탯 배율
        public float duration;   // 버프/디버프 지속(초). 공격/상시 패시브는 0
    }

    /// <summary>스킬(skill_master). player_skill.skill_code 가 참조한다.</summary>
    [Serializable]
    public class SkillMaster
    {
        public int skillCode;
        public int classCode;     // 소속 직업(FK class_master)
        public string name;
        public int skillType;     // 1:액티브 2:패시브
        public SkillCoef[] coefs; // 레벨·타입별 계수(개수 = 타입 수 × maxLevel)
        public int maxLevel;
    }

    /// <summary>룬(rune_master, Rune Tree). player_rune.rune_code 가 참조한다.</summary>
    [Serializable]
    public class RuneMaster
    {
        public int runeCode;
        public string name;
        public int prereqCode;    // 선행 룬(0=루트)
        public long cost;
        public int maxLevel;
        public int statType;      // 1:공격력 2:방어력 3:체력 4:치명확률 5:치명피해 6:이동속도
        public float statValue;   // 레벨당 누적 상승량(%)
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

    /// <summary>출석부 일자별 보상(attendance_master).</summary>
    [Serializable]
    public class AttendanceMaster
    {
        public int day;            // 이달 며칠(1~31)
        public int rewardType;     // 1:골드 2:아이템 3:재화
        public int rewardCode;     // 아이템/재화 코드(item_master.item_code, 없으면 0)
        public long quantity;      // 지급 수량
    }
}
