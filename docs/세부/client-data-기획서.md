# 클라이언트 보유 기획 데이터 & Unity 연동 기획서

> 상위 문서: [서버 시스템 전체 개요](../공통/서버-시스템-전체-개요.md) · 관련 도메인 4.11(정적 데이터 제공)
>
> 본 문서는 **Unity 클라이언트가 로컬에 들고 있어야 하는 기획(마스터) 데이터**의 범위·스키마·예제와, 이를 **Unity에서 바로 사용할 수 있는 C# 데이터 클래스/로더 코드**를 정리한다. 마스터 데이터의 **정본은 서버**([마스터 데이터 기획서](master-data-기획서.md))이며, 본 문서는 그중 클라이언트가 소비하는 부분과 사용 방법을 다룬다.

## 1. 개요

- **목적**: 방치형 게임 특성상 클라이언트는 **자동 전투 연출·전투력 계산·UI 표시**를 로컬에서 수행해야 한다. 이를 위해 클라이언트는 몬스터 스탯, 클래스 기본 공격력, 스킬 계수, 레벨 곡선, 아이템/강화 배율 등 **기획 데이터를 로컬 캐시로 보유**한다. 본 문서는 그 데이터의 목록·스키마·예제와 Unity 연동 코드를 제공해, 클라이언트 구현이 서버 마스터 데이터와 어긋나지 않게 한다.
- **대상**: **Unity 클라이언트**(데이터 로딩·전투 시뮬레이션·UI), `TaskbarHero.Common`(서버-클라 공유 데이터 클래스·enum, `netstandard2.0`), `GameServer`(마스터 데이터 원천·배포).
- **서버 권위 원칙(중요)**: 클라이언트가 이 데이터로 하는 계산은 **연출·예측용**이다. 오프라인 보상·스테이지 클리어 보상·성장 결과 등 **이득이 되는 값의 최종 확정은 서버가 동일 마스터 데이터로 재계산**한다([서버 시스템 전체 개요](../공통/서버-시스템-전체-개요.md) 5장, [스테이지/전투 결과 기획서](stage-battle-기획서.md)). 따라서 클라이언트 데이터는 **서버와 동일 버전**이어야 한다(3장 버전 일치).
- **관련 기획서**: [[master-data-기획서]] (마스터 데이터 정본·다운로드 API), [[stage-battle-기획서]] (전투 결과 검증), [[growth-기획서]] (스킬·룬 성장), [[save-data-기획서]] (플레이어 세이브 = 동적 데이터), [[서버-시스템-전체-개요]] (도메인 4.11)

## 2. 클라이언트가 보유하는 데이터 범위

클라이언트는 접속 시 `POST /api/master/download`로 마스터 데이터를 받아 **로컬 캐시**에 저장하고, 보유 `master_data_version`이 서버와 **다를 때만** 재다운로드한다([마스터 데이터 기획서](master-data-기획서.md) 6·8장). 아래는 마스터 테이블별 **클라이언트 보유 목적** 분류다.

| 마스터 테이블 | 클라 보유 목적 | 분류 |
|---|---|---|
| `class_master` | 클래스 기본 스탯(체력·공격·방어·이동속도·치명확률·치명데미지·쿨다운) | 🔴 전투 필수 |
| `level_master` | 레벨별 스탯 보너스·요구 경험치 | 🔴 전투 필수 |
| `item_master` | 장비 기본 옵션(스탯) + **재화(골드 등) 정의**(`item_type=4`) | 🔴 전투 필수 |
| `enhance_master` | 강화 단계별 스탯 배율 | 🔴 전투 필수 |
| `skill_master` | 스킬 계수(레벨별 효과) | 🔴 전투 필수 |
| `rune_master` | 룬 효과(% 보너스) | 🔴 전투 필수 |
| `monster_master` | 몬스터 스탯(HP·공격력) | 🔴 전투 필수 |
| `stage_master` | 스테이지 스폰·보스·기본 보상 | 🔴 전투 필수 |
| `drop_table_master` | 드롭 항목(표시·예측용, 확정은 서버) | 🟡 표시용 |
| `cube_master` | 큐브 합성/분해/제작 규칙 표시 | 🟡 표시용 |
| `equip_slot_master` | 슬롯 이름 표시 | 🟡 표시용 |
| `box_master` | 상자 등급 확률 안내 표시 | 🟡 표시용 |
| `attendance_master` | 출석 달력 보상 표시 | 🟡 표시용 |
| `pet_master` | 펫 해금 조건·효과 표시(시스템 도입 시) | 🟡 표시용 |

- 🔴 **전투 필수**: 자동 전투 연출·전투력/데미지 계산에 직접 쓰인다. 3·4·5장에서 스키마·코드·계산 예시를 다룬다.
- 🟡 **표시용**: UI 안내·미리보기용. 실제 결과(드롭·상자·큐브 RNG)는 서버가 확정한다.
- **동적 데이터(플레이어 세이브)는 여기 포함되지 않는다**: 캐릭터 레벨·장착·인벤토리·재화 등은 `POST /api/game/load` 스냅샷으로 받는다([세이브 데이터 기획서](save-data-기획서.md) 5장). 클라 전투 계산은 "마스터 데이터(정적) + 세이브(동적)"를 결합한다.

## 3. 전투/성장 핵심 데이터 스키마 & 예제

클라이언트가 받는 JSON은 `POST /api/master/download` 응답 규약을 따른다(필드명 **camelCase**, [마스터 데이터 기획서](master-data-기획서.md) 8장). 각 테이블의 필드 정의는 정본(마스터 데이터 기획서 5장)을 따르며, 아래는 클라이언트 소비 관점의 요약과 예제 payload다.

### 3.1 `class_master` — 클래스 기본 스탯
```json
[
  { "classCode": 1, "name": "Knight", "unlockType": 0, "baseStats": { "hp": 120, "atk": 10, "def": 8, "moveSpeed": 3.0, "critChance": 0.05, "critDamage": 1.5, "cooldown": 1.2 } },
  { "classCode": 2, "name": "Ranger", "unlockType": 0, "baseStats": { "hp": 90,  "atk": 14, "def": 5, "moveSpeed": 4.0, "critChance": 0.10, "critDamage": 1.5, "cooldown": 0.9 } },
  { "classCode": 3, "name": "Mage",   "unlockType": 0, "baseStats": { "hp": 85,  "atk": 16, "def": 4, "moveSpeed": 3.2, "critChance": 0.08, "critDamage": 1.7, "cooldown": 1.5 } }
]
```
- **캐릭터 스탯 구성**: 캐릭터 스탯은 `hp`(체력)·`atk`(공격)·`def`(방어)·`moveSpeed`(이동속도)·`critChance`(치명확률, 0~1)·`critDamage`(치명데미지 배율, `1.5`=150%)·`cooldown`(재사용 대기시간, 초)으로 구성한다. `class_master.baseStats`(기본값), `level_master.statBonus`(레벨 보너스), `item_master.baseStats`(장비 옵션)가 모두 이 스탯 구조를 공유하며, 없는 필드는 0이다.

### 3.2 `level_master` — 레벨 곡선
```json
[
  { "level": 1, "requiredExp": 100, "skillPoints": 1, "statBonus": { "hp": 10, "atk": 2, "def": 1 } },
  { "level": 2, "requiredExp": 250, "skillPoints": 2, "statBonus": { "hp": 20, "atk": 4, "def": 2 } },
  { "level": 3, "requiredExp": 500, "skillPoints": 3, "statBonus": { "hp": 30, "atk": 6, "def": 3 } }
]
```
- `statBonus`는 해당 레벨 도달 시의 **누적 기본 스탯 보너스**(정본 5.13). `skillPoints`는 그 레벨의 누적 스킬 포인트 총량.

### 3.3 `item_master` — 아이템·재화 정의
```json
[
  { "itemCode": 1,     "name": "골드",       "itemType": 4, "grade": 1, "equipSlot": 0, "classReq": 0, "levelReq": 0,  "stackMax": 0, "baseStats": {}, "sellable": 0 },
  { "itemCode": 30012, "name": "강철 대검",   "itemType": 1, "grade": 3, "equipSlot": 1, "classReq": 1, "levelReq": 15, "stackMax": 1, "baseStats": { "atk": 45, "cooldown": -0.1 }, "sellable": 1 },
  { "itemCode": 30105, "name": "코스믹 투구", "itemType": 1, "grade": 6, "equipSlot": 2, "classReq": 0, "levelReq": 40, "stackMax": 1, "baseStats": { "hp": 220, "def": 30 }, "sellable": 1 },
  { "itemCode": 30240, "name": "예리한 반지", "itemType": 1, "grade": 4, "equipSlot": 6, "classReq": 0, "levelReq": 20, "stackMax": 1, "baseStats": { "critChance": 0.05, "critDamage": 0.2 }, "sellable": 1 }
]
```
- **`item_type=4`(재화)**: 골드 등 소비 재화도 `item_master`로 정의한다(별도 `currency_master` 없음). **골드는 `itemCode=1`**로 고정한다. 재화는 장착·스택 개념이 없어 관련 필드는 0이고, 보유 잔액은 세이브 `player_item` 재화 행(`row_type=2`)의 `quantity`에 담긴다. API 응답의 `currencyType`/`cost`/`balance` 값은 이 재화 `itemCode`(골드=1)를 가리킨다.
- 장비도 캐릭터 스탯 구조를 공유하므로 무기의 공격력·쿨다운 감소, 반지의 치명확률·치명데미지 등 어떤 스탯이든 옵션으로 가질 수 있다(예: `cooldown: -0.1`은 재사용 대기시간 0.1초 감소).
- 비장비(재료/소모품)는 `baseStats`가 비어 있고 전투 계산에 쓰이지 않는다.

### 3.4 `enhance_master` — 강화 배율
```json
[
  { "enhanceLevel": 1, "cost": 1000, "currencyType": 1, "statMultiplier": { "atk": 1.05 } },
  { "enhanceLevel": 2, "cost": 3000, "currencyType": 1, "statMultiplier": { "atk": 1.10 } },
  { "enhanceLevel": 3, "cost": 8000, "currencyType": 1, "statMultiplier": { "atk": 1.18 } }
]
```
- 장비의 `baseStats`에 해당 `enhanceLevel`의 `statMultiplier`를 곱해 최종 스탯을 얻는다.

### 3.5 `skill_master` — 스킬 계수
```json
[
  { "skillCode": 101, "classCode": 1, "name": "방패 강타", "skillType": 1, "maxLevel": 10, "effectPerLevel": [ { "dmg": 120 }, { "dmg": 150 }, { "dmg": 185 } ] },
  { "skillCode": 110, "classCode": 1, "name": "강철 피부", "skillType": 2, "maxLevel": 5,  "effectPerLevel": [ { "defPct": 0.05 }, { "defPct": 0.09 } ] },
  { "skillCode": 201, "classCode": 2, "name": "정조준 사격", "skillType": 1, "maxLevel": 10, "effectPerLevel": [ { "dmg": 180 } ] }
]
```
- **스킬 계수**: `effectPerLevel[레벨-1]`이 그 레벨의 효과다. 데미지 스킬은 `dmg`가 **공격력 대비 %**(예: `120` = 공격력의 120%)이며, 데미지 = `캐릭터 공격력 × dmg / 100`. `skillType=1`은 액티브(캐릭터당 2개 장착), `2`는 패시브(상시). 데미지 외 효과(`defPct`, `aggro` 등)는 스킬마다 다른 키를 가지므로 4장 `SkillEffect`에 선택 필드로 모은다(8장 미결).

### 3.6 `rune_master` — 룬 % 보너스
```json
[
  { "runeCode": 205, "name": "공격력 I", "prereqCode": 0,   "cost": 5000,  "maxLevel": 20, "effect": { "atkPct": 0.02 } },
  { "runeCode": 210, "name": "치명타",   "prereqCode": 205, "cost": 15000, "maxLevel": 10, "effect": { "critPct": 0.01 } }
]
```
- 룬 효과는 **레벨당 누적 %**로 적용한다(예: `atkPct 0.02` × 룬 레벨).

### 3.7 `monster_master` — 몬스터 스탯
```json
[
  { "monsterCode": 9001, "name": "슬라임",   "hp": 500,   "attack": 20,  "dropTableCode": 7001 },
  { "monsterCode": 9010, "name": "늑대",     "hp": 1200,  "attack": 55,  "dropTableCode": 7002 },
  { "monsterCode": 9099, "name": "Act1 보스","hp": 25000, "attack": 180, "dropTableCode": 7050 }
]
```

### 3.8 `stage_master` — 스테이지 스폰
```json
[
  {
    "stageId": 1010001, "act": 1, "difficulty": 1, "stage": 1,
    "rewardGold": 100, "rewardExp": 50, "dropTableCode": 7001,
    "spawns": [ { "monsterCode": 9001, "count": 8 }, { "monsterCode": 9010, "count": 3 } ],
    "bossMonsterCode": 0
  }
]
```
- 스테이지 진입 시 실제 스폰·보스 정보는 서버 진입 응답으로도 내려온다([스테이지/전투 결과 기획서](stage-battle-기획서.md) 5.1). 클라는 스폰 구성으로 전투 연출을 구성한다.

## 4. Unity(C#) 데이터 클래스 & 로더

**설계 원칙**
- **데이터 클래스(POCO)는 `TaskbarHero.Common`(`netstandard2.0`)에 둔다** — 서버-클라 공유, Unity 의존성 없음. `[System.Serializable]` + **public 필드**(Unity `JsonUtility` 요구사항)로 정의한다.
- **로더(`JsonUtility` 사용)는 Unity 클라이언트 측**에 둔다(`UnityEngine.JsonUtility`는 `TaskbarHero.Common`에 넣지 않는다). `JsonUtility`는 최상위 배열·`Dictionary`를 직접 파싱하지 못하므로 **배열 래핑 헬퍼**로 파싱한 뒤 코드→객체 `Dictionary`로 인덱싱한다.
- 외부 패키지 없이 동작한다. 임의 구조(가변 effect 등)가 필요하면 Newtonsoft(`com.unity.nuget.newtonsoft-json`)로 대체할 수 있다.

### 4.1 공유 데이터 클래스 (`TaskbarHero.Common`)

```csharp
using System;

namespace TaskbarHero.Common.MasterData
{
    // 공통 캐릭터 스탯 (base_stats / stat_bonus). 없는 필드는 0으로 채워진다.
    [Serializable]
    public struct Stats
    {
        public long  hp;          // 체력
        public long  atk;         // 공격
        public long  def;         // 방어
        public float moveSpeed;   // 이동속도
        public float critChance;  // 치명확률 (0~1, 예: 0.15 = 15%)
        public float critDamage;  // 치명데미지 배율 (예: 1.5 = 150%)
        public float cooldown;    // 재사용 대기시간(공격/스킬 주기, 초)
    }

    [Serializable]
    public class ClassMaster
    {
        public int classCode;
        public string name;
        public int unlockType;   // 0:기본 1:해금 2:유료
        public Stats baseStats;
    }

    [Serializable]
    public class LevelMaster
    {
        public int level;
        public long requiredExp;
        public int skillPoints;
        public Stats statBonus;   // 해당 레벨 누적 스탯 보너스
    }

    [Serializable]
    public class ItemMaster
    {
        public int itemCode;
        public string name;
        public int itemType;      // 1:장비 2:재료 3:소모품 4:재화(골드 등)
        public int grade;
        public int equipSlot;
        public int classReq;      // 0=전 클래스
        public int levelReq;      // 5레벨 단위, 0=제한 없음
        public int stackMax;
        public Stats baseStats;
        public int sellable;      // 0/1
    }

    [Serializable] public struct StatMultiplier { public float hp; public float atk; public float def; }

    [Serializable]
    public class EnhanceMaster
    {
        public int enhanceLevel;
        public long cost;
        public int currencyType;
        public StatMultiplier statMultiplier;   // 예: atk=1.05
    }

    // 스킬 레벨별 효과. 스킬마다 쓰는 키가 다르므로 선택 필드로 모은다(없으면 0).
    [Serializable]
    public struct SkillEffect
    {
        public float dmg;      // 데미지 스킬: 공격력 대비 %(예: 120 = 120%)
        public float defPct;   // 방어 패시브 등
        public float aggro;    // 도발 계수 등
        public float critPct;
    }

    [Serializable]
    public class SkillMaster
    {
        public int skillCode;
        public int classCode;
        public string name;
        public int skillType;             // 1:액티브 2:패시브
        public int maxLevel;
        public SkillEffect[] effectPerLevel;   // index 0 = 1레벨
    }

    [Serializable] public struct RuneEffect { public float atkPct; public float critPct; }

    [Serializable]
    public class RuneMaster
    {
        public int runeCode;
        public string name;
        public int prereqCode;   // 0=루트
        public long cost;
        public int maxLevel;
        public RuneEffect effect;   // 레벨당 누적 %
    }

    [Serializable]
    public class MonsterMaster
    {
        public int monsterCode;
        public string name;
        public long hp;
        public long attack;
        public int dropTableCode;
    }

    [Serializable] public struct Spawn { public int monsterCode; public int count; }

    [Serializable]
    public class StageMaster
    {
        public int stageId;
        public int act;
        public int difficulty;
        public int stage;
        public long rewardGold;
        public long rewardExp;
        public int dropTableCode;
        public Spawn[] spawns;
        public int bossMonsterCode;   // 0=보스 없음
    }
}
```

### 4.2 JSON 배열 파싱 헬퍼 (Unity)

```csharp
using UnityEngine;

// JsonUtility는 최상위 배열("[ ... ]")을 파싱하지 못하므로 객체로 감싸 파싱한다.
public static class JsonHelper
{
    [System.Serializable] private class Wrapper<T> { public T[] items; }

    public static T[] FromJsonArray<T>(string arrayJson)
    {
        string wrapped = "{\"items\":" + arrayJson + "}";
        return JsonUtility.FromJson<Wrapper<T>>(wrapped).items;
    }
}
```

### 4.3 마스터 DB 로더 (Unity)

```csharp
using System;
using System.Collections.Generic;
using TaskbarHero.Common.MasterData;

// 다운로드된 테이블별 JSON 배열을 코드→객체 Dictionary로 인덱싱해 보관한다.
public class MasterDatabase
{
    public int version;   // master_data_version

    public readonly Dictionary<int, ClassMaster>   Classes  = new Dictionary<int, ClassMaster>();
    public readonly Dictionary<int, LevelMaster>   Levels   = new Dictionary<int, LevelMaster>();
    public readonly Dictionary<int, ItemMaster>    Items    = new Dictionary<int, ItemMaster>();
    public readonly Dictionary<int, EnhanceMaster> Enhances = new Dictionary<int, EnhanceMaster>();
    public readonly Dictionary<int, SkillMaster>   Skills   = new Dictionary<int, SkillMaster>();
    public readonly Dictionary<int, RuneMaster>    Runes    = new Dictionary<int, RuneMaster>();
    public readonly Dictionary<int, MonsterMaster> Monsters = new Dictionary<int, MonsterMaster>();
    public readonly Dictionary<int, StageMaster>   Stages   = new Dictionary<int, StageMaster>();

    // 각 인자는 /api/master/download 응답의 테이블별 JSON 배열 문자열
    public void Load(string classesJson, string levelsJson, string itemsJson, string enhancesJson,
                     string skillsJson, string runesJson, string monstersJson, string stagesJson, int dataVersion)
    {
        version = dataVersion;
        Fill(Classes,  JsonHelper.FromJsonArray<ClassMaster>(classesJson),   c => c.classCode);
        Fill(Levels,   JsonHelper.FromJsonArray<LevelMaster>(levelsJson),    l => l.level);
        Fill(Items,    JsonHelper.FromJsonArray<ItemMaster>(itemsJson),      i => i.itemCode);
        Fill(Enhances, JsonHelper.FromJsonArray<EnhanceMaster>(enhancesJson),e => e.enhanceLevel);
        Fill(Skills,   JsonHelper.FromJsonArray<SkillMaster>(skillsJson),    s => s.skillCode);
        Fill(Runes,    JsonHelper.FromJsonArray<RuneMaster>(runesJson),      r => r.runeCode);
        Fill(Monsters, JsonHelper.FromJsonArray<MonsterMaster>(monstersJson),m => m.monsterCode);
        Fill(Stages,   JsonHelper.FromJsonArray<StageMaster>(stagesJson),    s => s.stageId);
    }

    private static void Fill<T>(Dictionary<int, T> dict, T[] rows, Func<T, int> keySelector)
    {
        dict.Clear();
        if (rows == null) return;
        foreach (var row in rows) dict[keySelector(row)] = row;
    }
}
```

> 다운로드/버전 비교/로컬 캐시 저장 흐름은 [마스터 데이터 기획서](master-data-기획서.md) 6·8장을 따른다. 서버 `master_data_version`과 로컬 `version`이 다르면 재다운로드 후 `Load(...)`를 다시 호출한다.

## 5. 사용 예시 — 전투력·스킬 데미지 계산 (클라이언트)

> 아래 계산식은 **구조 설명용 예시**다. 실제 밸런스 공식(스탯 합산 순서, 방어 감산, 치명타 등)은 전투 기획에서 확정한다(8장 미결). 서버도 동일 공식으로 재계산해 보상을 확정한다.

```csharp
using System.Collections.Generic;
using TaskbarHero.Common.MasterData;

// 세이브 스냅샷(/api/game/load)에서 오는 동적 데이터의 최소 형태
public struct EquippedItem { public int itemCode; public int enhanceLevel; }
public struct OwnedRune    { public int runeCode; public int level; }

public class CombatCalculator
{
    private readonly MasterDatabase db;
    public CombatCalculator(MasterDatabase database) { db = database; }

    // 캐릭터 종합 스탯 = 클래스 기본 + 레벨 보너스 + 장비(공격력에 강화 배율) + 룬 %
    public Stats AggregateStats(int classCode, int level, IEnumerable<EquippedItem> equips, IEnumerable<OwnedRune> runes)
    {
        Stats s = db.Classes[classCode].baseStats;                      // 클래스 기본 스탯

        if (db.Levels.TryGetValue(level, out var lv))                   // 레벨 누적 보너스
        {
            s.hp += lv.statBonus.hp; s.atk += lv.statBonus.atk; s.def += lv.statBonus.def;
            s.moveSpeed += lv.statBonus.moveSpeed; s.critChance += lv.statBonus.critChance;
            s.critDamage += lv.statBonus.critDamage; s.cooldown += lv.statBonus.cooldown;
        }

        foreach (var e in equips)                                       // 장비 합(공격력에 강화 배율 적용)
        {
            if (!db.Items.TryGetValue(e.itemCode, out var im)) continue;
            float atkMult = 1f;
            if (db.Enhances.TryGetValue(e.enhanceLevel, out var en) && en.statMultiplier.atk > 0f)
                atkMult = en.statMultiplier.atk;
            s.hp += im.baseStats.hp; s.atk += (long)(im.baseStats.atk * atkMult); s.def += im.baseStats.def;
            s.moveSpeed += im.baseStats.moveSpeed; s.critChance += im.baseStats.critChance;
            s.critDamage += im.baseStats.critDamage; s.cooldown += im.baseStats.cooldown;
        }

        float atkPct = 0f;                                              // 룬 공격력 % 합
        foreach (var r in runes)
            if (db.Runes.TryGetValue(r.runeCode, out var rm))
                atkPct += rm.effect.atkPct * r.level;
        s.atk = (long)(s.atk * (1f + atkPct));

        return s;
    }

    // 액티브 스킬 1히트 기본 데미지 = 공격력 × (레벨 계수 / 100)
    public long SkillDamage(long attack, int skillCode, int skillLevel)
    {
        var sk = db.Skills[skillCode];
        int idx = skillLevel - 1;
        if (idx < 0) idx = 0;
        if (idx >= sk.effectPerLevel.Length) idx = sk.effectPerLevel.Length - 1;
        return (long)(attack * sk.effectPerLevel[idx].dmg / 100f);      // 120 → 1.2배
    }

    // 치명타 기대 데미지 = 기본 × (1 + 치명확률 × (치명데미지 - 1))
    public long ExpectedSkillDamage(Stats attacker, int skillCode, int skillLevel)
    {
        long baseDmg = SkillDamage(attacker.atk, skillCode, skillLevel);
        float p = attacker.critChance; if (p < 0f) p = 0f; if (p > 1f) p = 1f;
        float critMul = attacker.critDamage <= 0f ? 1f : attacker.critDamage;
        return (long)(baseDmg * (1f + p * (critMul - 1f)));
    }

    // 초당 데미지(연출/예측용) = 기대 데미지 / 재사용 대기시간(초)
    public double SkillDps(Stats attacker, int skillCode, int skillLevel)
    {
        float cd = attacker.cooldown <= 0f ? 1f : attacker.cooldown;    // cooldown은 공격/스킬 주기(초)
        return ExpectedSkillDamage(attacker, skillCode, skillLevel) / (double)cd;
    }

    // 몬스터를 처치하는 데 필요한 타격 수(연출용 예측)
    public int HitsToKill(long damagePerHit, int monsterCode)
    {
        long hp = db.Monsters[monsterCode].hp;
        if (damagePerHit <= 0) return int.MaxValue;
        return (int)((hp + damagePerHit - 1) / damagePerHit);
    }
}
```

**사용 흐름 요약**
1. 접속 → `POST /api/master/download`로 마스터 JSON 수신 → `MasterDatabase.Load(...)`.
2. `POST /api/game/load`로 세이브 스냅샷(캐릭터 레벨·장착·룬 등) 수신.
3. 마스터 + 세이브를 결합해 `CombatCalculator`로 전투력·데미지·처치 시간을 산출 → 자동 전투 연출.
4. 서버는 클리어·오프라인 보상 등을 **자체 재계산**으로 확정한다(클라 계산은 신뢰하지 않음).

## 6. 미결 사항 / TODO

- **전투 공식 확정**: 스탯 합산 순서, 방어(`def`) 감산식, 치명타(`critChance`/`critDamage`) 적용, 이동속도(`moveSpeed`)·재사용 대기시간(`cooldown`)의 전투 반영(공격 주기·연출), 스킬 대상 판정 등 정확한 전투 규칙은 전투 기획에서 확정한다. 5장 코드는 예시.
- **비공격 스탯 강화·룬**: 현재 예시에서 강화(`enhance_master.statMultiplier`)는 공격력 배율만, 룬은 공격력 %만 반영한다. 방어·치명·이동속도·쿨다운에 대한 강화/룬 효과를 도입할지, 스탯별 배율/가산 규칙을 확정한다.
- **스킬/룬 effect 스키마 확정**: 스킬 `effectPerLevel`·룬 `effect`의 키 집합(데미지·방어·도발·치명타 외)을 확정하고, `SkillEffect`/`RuneEffect` 선택 필드를 그에 맞춘다. 키가 크게 가변적이면 Newtonsoft 기반 파싱으로 전환 검토.
- **JSON 필드명 규약 고정**: 다운로드 payload는 camelCase 기준(마스터 데이터 기획서 8장). 원천(CSV/JSON) 키가 snake_case인 경우 서버 직렬화 시 camelCase로 정규화하는 규칙을 마스터 데이터 배포에서 확정한다.
- **데이터 클래스 배치 확정**: 4.1 POCO를 `TaskbarHero.Common`에 둘지(서버 공용) 클라 전용으로 둘지 — 현재는 공용(`netstandard2.0`) 권장. 서버가 자체 마스터 로딩에 같은 클래스를 재사용할지 확정.
- **표시용(🟡) 데이터의 클라 보유 범위**: `drop_table_master` 등은 미리보기만 하고 확정은 서버다. 클라에 확률 원본을 노출할지(치트/역설계 우려) 여부 검토.

## 7. 참고

- [마스터 데이터 기획서](master-data-기획서.md) — 마스터 테이블 정본 정의·다운로드 API(`/api/master/download`)·버전 관리
- [스테이지/전투 결과 기획서](stage-battle-기획서.md) — 전투 결과 서버 검증(클라 계산은 예측)
- [성장 시스템 기획서](growth-기획서.md) — 스킬·룬 성장 규칙
- [세이브 데이터 기획서](save-data-기획서.md) — 동적 데이터(`/api/game/load` 스냅샷)
- [서버 시스템 전체 개요](../공통/서버-시스템-전체-개요.md) — 도메인 4.11, 서버 권위 원칙
