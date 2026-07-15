# 성장 시스템(직업 / 스킬 / 룬) 기획서

> 상위 문서: [서버 시스템 전체 개요](../공통/서버-시스템-전체-개요.md) · 관련 도메인 4.5
>
> 본 문서는 플레이어 캐릭터의 **성장 축 중 직업(클래스)·스킬·룬**을 서버 권위로 관리·검증하는 규칙을 다룬다. 저장 골격은 [세이브 데이터 기획서](save-data-기획서.md)(`game_player`·`player_character`·`player_skill`·`player_rune`), 정적 정의는 [마스터 데이터 기획서](master-data-기획서.md)(`class_master`·`skill_master`·`rune_master`)를 참고한다. 장비 강화는 본 문서 범위 밖이다([인벤토리/아이템/큐브 기획서](inventory-item-cube-기획서.md) 5.3).

## 1. 개요

- **목적**: 방치형 전투로 쌓은 경험치·골드를 캐릭터 성장으로 환산하는 순환을 서버 권위로 검증·반영한다. 성장은 **전투력에 직결되는 이득**이므로 레벨·스킬 레벨·룬 레벨은 모두 **서버가 최종 확정**하며 클라이언트 보고를 신뢰하지 않는다.
- **대상 서버**: `GameServer`(성장 상태 관리·검증·반영), `TaskbarHero.Common`(성장 분류 enum·결과 DTO 공유). 인증은 AccountServer 발급 토큰을 GameServer 미들웨어가 검증.
- **범위 경계**:
  - **직업 선택**은 각 캐릭터 생성 시 확정된다([세이브 데이터 기획서](save-data-기획서.md) 5.2 `create-character`). 본 문서는 직업이 성장에 미치는 역할(기본 스탯·보유 스킬)을 다룬다. **전직은 지원하지 않는다.**
  - **경험치 획득**(온라인 전투·오프라인 보상)의 산출은 본 문서 밖이다([스테이지/전투 결과](../공통/서버-시스템-전체-개요.md) 도메인 4.6, [오프라인 보상 정산 기획서](offline-reward-기획서.md)). 본 문서는 획득된 `exp`로 **레벨을 올리고, 레벨에 비례하는 스킬 포인트 총량·스탯을 확정**하는 부분을 책임진다.
  - **최종 전투력(스탯 합산)** 계산은 전투 도메인에서 다룬다. 본 문서는 각 성장 요소의 **레벨·해금 상태 관리**까지를 책임진다.
- **관련 기획서**: [[save-data-기획서]] (저장 골격), [[master-data-기획서]] (직업·스킬·룬 정적 정의), [[inventory-item-cube-기획서]] (장비 강화·클래스/레벨 제한), [[offline-reward-기획서]] (경험치 획득원), [[서버-시스템-전체-개요]] (도메인 4.5)

## 2. 기능 설명

- **캐릭터(3인 파티)**: 계정은 원작과 동일하게 **캐릭터 슬롯 3개**를 가지며, 3명의 캐릭터가 **함께 전투**한다. **직업·레벨·경험치·스킬·장비는 캐릭터별로 개별** 관리되며, 세 캐릭터의 **직업은 서로 중복될 수 없어**(생성 시 확정) 3종(기사·레인저·마법사)을 각 슬롯에 하나씩 둔다. API·저장 시 대상 캐릭터를 `characterId`(슬롯 1~3)로 지정한다. 반면 **인벤토리·골드·큐브·룬은 계정 단위로 공유**한다([세이브 데이터 기획서](save-data-기획서.md) 3장).
- **직업(클래스)**: 각 캐릭터 생성 시 **기사·레인저·마법사 3종**([마스터 데이터 기획서](master-data-기획서.md) 5.1 `class_master`) 중 하나를 고른다. 직업은 기본 스탯(`base_stats`)과 **보유 스킬 목록**(`skill_master.class_code`), 장비 클래스 제한([인벤토리/아이템/큐브 기획서](inventory-item-cube-기획서.md) 5.1)을 결정한다. 캐릭터는 전투 경험치로 **레벨(`level`)** 이 오르며, **사용 가능한 스킬 포인트 총량은 레벨에 비례**한다(별도 저장 없이 레벨에서 파생). **전직(직업 변경)은 지원하지 않는다.**
- **스킬**: 직업별로 보유하는 능력으로 **액티브/패시브**로 나뉜다(구분은 `skill_master`). **스킬 포인트를 소모**해 해당 캐릭터의 스킬 레벨(`player_skill.level`)을 올리며, 레벨별 효과는 `skill_master.effect_per_level`이 정의한다. 캐릭터는 자기 직업 소속 스킬만 올릴 수 있다. **액티브 스킬은 캐릭터당 최대 2개까지만 장착·사용**할 수 있고(패시브는 개수 제한 없이 상시 적용), **스킬 초기화(리셋)는 무료로 지원**한다(5.2).
- **룬(Rune Tree)**: **골드를 소모**해 올리는 장기 성장 축. 선행 룬을 해금해야 다음 룬을 열 수 있는 **트리 구조**(`rune_master.prereq_code`)이며, 각 룬은 레벨을 가진다. 직업과 무관한 **계정 공용**이다. **룬 초기화는 지원하지 않는다.**

## 3. 요구사항

**기능 요구사항**
- 성장 상태 조회는 별도 API를 두지 않고 [세이브 로드](save-data-기획서.md)(`POST /api/game/load`)가 `characters`(캐릭터별 직업·레벨·경험치)와 `skills`·`runes`(스킬·룬 레벨) 스냅샷을 반환한다. 사용 가능한 스킬 포인트는 레벨과 보유 스킬 레벨에서 **파생 계산**한다(저장하지 않음). 본 문서는 **상태를 바꾸는 액션**만 전용 엔드포인트로 제공한다.
- 스킬 레벨업(포인트 소모), 룬 업그레이드(골드 소모·선행 조건)를 각각 처리하고, 변경된 성장 상태·잔여 재화를 응답한다.
- 모든 성장 반영은 마스터 데이터 제약(코드 존재, 직업 소속, 최대 레벨, 선행 룬, 비용·포인트 충족)을 서버가 검증한 뒤 수행한다.
- 레벨업(경험치→레벨)은 경험치가 반영되는 시점(전투 결과 저장·오프라인 정산·세이브)에서 서버가 확정한다. 스킬 포인트 총량은 레벨에서 파생되므로 별도 지급·저장 처리가 없다.

**비기능 요구사항**
- **서버 권위**: 레벨·스킬 레벨·룬 레벨·골드 차감은 서버가 마스터 데이터로 계산·검증한다. 스킬 포인트는 레벨에서 파생하므로 별도 저장값을 신뢰할 필요가 없다. 클라이언트가 보낸 결과값은 신뢰하지 않는다.
- **원자성**: "재화/포인트 차감 + 성장 레벨 반영"은 하나의 `user_id` 단위 트랜잭션으로 처리한다. 중도 실패 시 전체 롤백한다.
- **동시성/멱등성**: 단일 세션 정책([계정/로그인 기획서](account-login-기획서.md))으로 경합은 제한적이나, 대상 행(`player_skill`/`player_rune`의 해당 행·`user_id`)에 잠금을 걸어 중복 레벨업·중복 차감을 막는다.

## 4. 데이터 모델

성장 저장의 핵심은 **어떤 스킬/룬이 몇 레벨인지**이며, 스킬 포인트는 저장하지 않고 캐릭터 레벨에서 파생한다. 스킬은 캐릭터별, 룬은 계정 공용이라 **테이블을 분리**한다 — 스킬은 `player_skill`(캐릭터별), 룬은 `player_rune`(계정 공용). **3인 파티 구조상 캐릭터별 데이터는 캐릭터 단위 키(`character_id`)를 가진다** — [세이브 데이터 기획서](save-data-기획서.md)에 `player_character` 테이블 신설, `player_skill`에 `character_id`·`equipped`(액티브 장착) 반영 완료. 장비 장착은 별도 테이블 없이 `player_item.equipped_character_id`로 캐릭터에 귀속한다.

| 테이블 | 역할 | 캐릭터별/공유 | 참조 마스터 |
|---|---|---|---|
| `player_character`(`(user_id, character_id)` PK, `class_code`, `level`, `exp`) | 직업·레벨·경험치(스킬 포인트 총량의 파생 근거) | **캐릭터별** | `class_master` |
| `player_skill`(`(user_id, character_id, skill_code)` PK, `level`, `equipped`) | 스킬 레벨·액티브 장착 여부 | **캐릭터별** | `skill_master` |
| `player_rune`(`(user_id, rune_code)` PK, `level`) | 룬 레벨 | **계정 공유** | `rune_master` |
| `player_item`(재화 행 `row_type=2`, `code`=골드 `item_code` 1) | 룬 업그레이드 골드 차감(`quantity` UPDATE) | 계정 공유 | `item_master`(재화 `item_type=4`) |

**성장 상태 규칙 (확정)**
- **직업(`player_character.class_code`, 캐릭터별)**: 각 캐릭터 생성 시 확정되며 이후 바뀌지 않는다(**전직 미지원**). `skill_master`에서 `class_code`가 일치하는 스킬만 해당 캐릭터의 보유 스킬이다.
- **레벨(`player_character.level`)·경험치(`player_character.exp`)**: 경험치가 임계치를 넘으면 레벨이 오른다. 임계치 곡선·레벨별 스탯·**레벨당 스킬 포인트 총량 계수**는 마스터 정의가 필요하다(8장 미결, `level_master` 제안).
- **스킬 레벨(`player_skill`, `(user_id, character_id, skill_code)`, 캐릭터별)**: 0(미습득)부터 `skill_master.max_level`까지. 대상 캐릭터의 스킬 포인트로 1레벨씩 올린다. 행이 없으면 레벨 0으로 간주하고, 첫 레벨업 시 행을 생성한다. **사용 가능 포인트 = 해당 캐릭터 레벨 비례 총량 − 그 캐릭터가 이미 투자한 포인트(보유 스킬 레벨로부터 계산)** 로 서버가 산출하므로, 스킬 포인트 잔량을 따로 저장하지 않는다. **스킬 초기화(5.2)** 시 해당 캐릭터의 스킬 행을 모두 제거해 포인트를 전량 회수한다(무료, `equipped`도 함께 해제). 액티브 스킬은 `equipped`(0/1)로 장착 여부를 저장하며 캐릭터당 최대 2개가 `equipped=1`이다(장착 설정은 5.3).
- **룬 레벨(`player_rune`, `(user_id, rune_code)`, 계정 공유)**: 0(미해금)부터 `rune_master.max_level`까지. 골드로 올리며, 선행 룬(`prereq_code`)이 **레벨 1 이상**이어야 해금(0→1) 가능하다. **룬 초기화는 지원하지 않는다.**

**공유 enum / DTO (TaskbarHero.Common)**
- 스킬(`player_skill`)과 룬(`player_rune`)은 별도 테이블로 저장하므로 성장 종류 구분용 `growth_type` 컬럼·enum은 두지 않는다.
- 성장 액션 결과 DTO(스킬/룬 레벨업 결과, 5장 응답 `data`)는 `TaskbarHero.Common`에 공유 DTO로 두는 것을 **제안**한다.

## 5. API 명세

Base URL(개발): `http://localhost:5247` (GameServer). 모든 API는 **POST**, 인증 요청 공통 형식 `{ userId, token, data }`, 응답 `{ success, errorCode, message, data }`([세이브 데이터 기획서](save-data-기획서.md) 5장과 동일 규약, `success`는 `errorCode == 0`과 동치). 아래 엔드포인트는 모두 **상태 변경 액션**이며, 조회는 `POST /api/game/load`를 사용한다.

### 5.1 스킬 레벨업 — `POST /api/game/growth/skill/levelup`

지정 캐릭터의 스킬 레벨을 1 올린다(그 캐릭터의 스킬 포인트 소모). 대상 스킬은 해당 캐릭터의 직업 소속이어야 한다.

**Request**
```json
{ "userId": 1, "token": "...", "data": { "characterId": 1, "skillCode": 101 } }
```

- `characterId`: 대상 캐릭터 슬롯(1~3).

**Response (성공, 200 OK)**
```json
{
  "success": true,
  "errorCode": 0,
  "message": "Skill leveled up",
  "data": {
    "skillCode": 101,
    "level": 6,
    "cost": { "skillPoint": 1 },
    "skillPoint": 3
  }
}
```

- `level`: 올린 뒤 스킬 레벨, `skillPoint`: 갱신 후 **사용 가능 스킬 포인트**(레벨 비례 총량 − 사용량, 서버가 파생 계산해 회신하는 값이며 저장 컬럼이 아님).
- **레벨당 소모 포인트는 1레벨당 1포인트로 고정(확정)**. 따라서 특정 캐릭터의 사용량 = 그 캐릭터 보유 스킬 레벨의 합이다.
- 오류: `InvalidGrowthTarget(5001)`(존재하지 않는 스킬 코드), `SkillClassMismatch(5004)`(대상 캐릭터 직업 소속이 아닌 스킬), `SkillMaxLevel(5002)`(최대 레벨 도달), `InsufficientSkillPoint(5003)`. 잘못된 `characterId`는 `InvalidCharacterId(2006)`로 거부한다([세이브 데이터 기획서](save-data-기획서.md) 6장).

### 5.2 스킬 초기화 — `POST /api/game/growth/skill/reset`

지정 캐릭터의 모든 스킬 레벨을 0으로 되돌려 투자한 스킬 포인트를 전량 회수한다. **비용은 발생하지 않는다(무료).** 룬에는 초기화가 없다.

**Request**
```json
{ "userId": 1, "token": "...", "data": { "characterId": 1 } }
```

**Response (성공, 200 OK)**
```json
{
  "success": true,
  "errorCode": 0,
  "message": "Skills reset",
  "data": {
    "characterId": 1,
    "resetSkillCount": 4,
    "skillPoint": 25
  }
}
```

- `resetSkillCount`: 초기화된 스킬 수(0이면 되돌릴 스킬이 없던 것으로 정상 처리), `skillPoint`: 초기화 후 사용 가능 스킬 포인트(= 해당 캐릭터 레벨 비례 총량 전액).
- 트랜잭션: 해당 캐릭터의 `player_skill` 행을 모두 삭제. 재화 변동 없음.
- 오류: 잘못된 `characterId`는 `InvalidCharacterId(2006)`로 거부한다([세이브 데이터 기획서](save-data-기획서.md) 6장).

### 5.3 액티브 스킬 장착 — `POST /api/game/growth/skill/equip`

지정 캐릭터의 **액티브 스킬 장착 목록(최대 2개)** 을 설정한다. `skillCodes`로 장착할 액티브 스킬을 지정하면 서버가 그 캐릭터의 액티브 장착을 통째로 교체한다(패시브는 장착 개념이 없어 대상 아님, 배운 즉시 상시 적용).

**Request**
```json
{ "userId": 1, "token": "...", "data": { "characterId": 1, "skillCodes": [101, 102] } }
```

- `skillCodes`: 장착할 액티브 스킬 코드 목록(**0~2개**). 각 코드는 그 캐릭터 직업의 **액티브 스킬**(`skill_master.skill_type=1`)이고 **레벨 ≥ 1(습득)** 이어야 한다. 빈 배열이면 전부 해제.

**Response (성공, 200 OK)**
```json
{
  "success": true,
  "errorCode": 0,
  "message": "Active skills equipped",
  "data": {
    "characterId": 1,
    "equipped": [101, 102]
  }
}
```

- `equipped`: 설정 후 장착된 액티브 스킬 코드 목록.
- 트랜잭션: 해당 캐릭터 `player_skill` 행의 `equipped`를 요청 목록에 맞춰 갱신(목록에 있으면 1, 없으면 0).
- 오류: `InvalidCharacterId(2006)`, `InvalidGrowthTarget(5001)`(존재하지 않는 스킬), `SkillClassMismatch(5004)`(대상 캐릭터 직업 소속 아님), `SkillNotActive(5005)`(패시브를 장착 시도), `SkillNotLearned(5006)`(레벨 0 미습득), `ActiveSkillLimitExceeded(5007)`(2개 초과).

### 5.4 룬 업그레이드 — `POST /api/game/growth/rune/upgrade`

골드를 소모해 지정 룬의 레벨을 1 올린다(0→1은 해금). 선행 룬(`prereq_code`)이 해금(레벨 ≥ 1)되어 있어야 한다.

**Request**
```json
{ "userId": 1, "token": "...", "data": { "runeCode": 205 } }
```

**Response (성공, 200 OK)**
```json
{
  "success": true,
  "errorCode": 0,
  "message": "Rune upgraded",
  "data": {
    "runeCode": 205,
    "level": 3,
    "cost": { "currencyType": 1, "amount": 5000 },
    "balance": [ { "currencyType": 1, "amount": 9870421 } ]
  }
}
```

- 한 번 요청에 **1레벨씩** 올라간다. `level`: 올린 뒤 룬 레벨, `balance`: 차감 후 골드 잔액. **골드 비용은 현재 룬 레벨에 비례해 증가**하며, 레벨별 비용 값은 `rune_master`에 명시된 정의를 서버가 그대로 사용한다(클라이언트 입력 불신).
- 오류: `InvalidGrowthTarget(5001)`(존재하지 않는 룬 코드), `RunePrereqNotMet(5010)`(선행 룬 미해금), `RuneMaxLevel(5011)`(최대 레벨 도달), `InsufficientCurrency(4005)`(골드 부족).

> 인증 오류(401), 마스터에 없는 코드 요청 등은 기존 미들웨어·마스터 도메인 코드를 따른다. 골드 부족은 재화 부족 공용 코드 `InsufficientCurrency(4005)`를 **재사용**한다([인벤토리/아이템/큐브 기획서](inventory-item-cube-기획서.md) 7장, 코드 값은 계약이므로 이동 금지).

## 6. 처리 흐름

### 6.1 스킬 레벨업 (의사코드)

```
요청 수신 → 토큰 검증(미들웨어)
트랜잭션(BEGIN, (user_id, characterId) 잠금)
  0) char = player_character[user_id, characterId]  # 없으면 InvalidCharacterId(2006)
  1) skill = skill_master[skillCode]           # 없으면 InvalidGrowthTarget(5001)
  2) if skill.class_code != char.class_code: SkillClassMismatch(5004)
  3) cur = player_skill[user_id, characterId, skillCode].level or 0
     if cur >= skill.max_level: SkillMaxLevel(5002)
  4) total = 레벨비례 스킬포인트 총량(char.level)            # 저장값 아님, 파생
     spent = Σ(그 캐릭터 보유 스킬 레벨 × 레벨당 소모 포인트)
     need  = 레벨당 소모 포인트(마스터/상수)
     if (total - spent) < need: InsufficientSkillPoint(5003)
  5) 반영: player_skill.level = cur + 1 (없으면 INSERT)       # 사용 포인트는 스킬 레벨에 내재, 별도 차감 없음
COMMIT → { characterId, skillCode, level, cost, skillPoint = total - (spent + need) }
```

**스킬 초기화**(5.2)는 위 잠금 아래에서 대상 캐릭터의 `player_skill` 행을 전부 삭제하고 커밋한다(비용·RNG 없음). 삭제 후 사용 가능 포인트는 그 캐릭터 레벨 비례 총량 전액으로 복원된다.

### 6.2 룬 업그레이드 (의사코드)

```
트랜잭션(BEGIN, user_id 잠금)
  1) rune = rune_master[runeCode]              # 없으면 InvalidGrowthTarget(5001)
  2) if rune.prereq_code != 0 and level(prereq_code) < 1: RunePrereqNotMet(5010)
  3) cur = player_rune[user_id, runeCode].level or 0          # 룬은 계정 공용
     if cur >= rune.max_level: RuneMaxLevel(5011)
  4) cost = 서버 산출(rune.cost, 현재 레벨 cur에 비례)
     if gold < cost: InsufficientCurrency(4005)
  5) 반영: gold -= cost; player_rune.level = cur + 1 (없으면 INSERT)
COMMIT → { runeCode, level, cost, balance }
```

### 6.3 레벨업(경험치→레벨) 반영

- 경험치는 전투 결과 저장·오프라인 정산·세이브 시점에 반영된다. 서버는 누적 `exp`가 다음 레벨 임계치를 넘으면 `level`을 올린다. 스킬 포인트 총량은 레벨에 비례해 자동으로 증가하므로 **별도 지급·저장 처리가 없다**(레벨당 계수는 미결).
- 이 처리는 경험치를 반영하는 각 도메인(4.6 전투, 4.3 오프라인, 4.2 세이브)의 트랜잭션 안에서 수행되며, 본 문서는 그 규칙(임계치·레벨당 스탯/포인트 계수)을 정의한다.

### 6.4 예외 / 엣지 케이스

- **직업 불일치 스킬 요청**: `SkillClassMismatch(5004)`. 클라이언트는 자기 직업 스킬만 노출하지만 서버가 재검증한다.
- **선행 룬 미해금 상태에서 상위 룬 요청**: `RunePrereqNotMet(5010)`.
- **최대 레벨 초과 요청**: 스킬 `SkillMaxLevel(5002)`, 룬 `RuneMaxLevel(5011)`.
- **포인트/골드 부족**: 스킬 `InsufficientSkillPoint(5003)`, 룬 `InsufficientCurrency(4005)`. 검증은 반영 전에 수행하고 부족 시 롤백.
- **동시 중복 요청**: 같은 스킬/룬 행(`player_skill`/`player_rune`)에 대한 레벨업이 겹치면 행 잠금으로 직렬화한다.

## 7. 에러 코드

`TaskbarHero.Common`의 `GameErrorCode`에 추가 제안. 도메인 4.5(성장)는 **5000번대**를 사용한다([통합 정의](../공통/error-code-정의.md) 블록 규약, 도메인 4.N → N000). 추가 시 통합 문서도 함께 갱신한다.

| 이름 | 값 | 의미 |
|---|---|---|
| InvalidGrowthTarget | 5001 | 존재하지 않는 스킬/룬 코드 |
| SkillMaxLevel | 5002 | 스킬이 최대 레벨에 도달 |
| InsufficientSkillPoint | 5003 | 스킬 포인트 부족 |
| SkillClassMismatch | 5004 | 해당 스킬이 대상 캐릭터 직업 소속이 아님 |
| SkillNotActive | 5005 | 액티브 스킬이 아님(패시브를 장착 시도) |
| SkillNotLearned | 5006 | 미습득(레벨 0) 스킬을 장착 시도 |
| ActiveSkillLimitExceeded | 5007 | 액티브 스킬 장착 한도(2개) 초과 |
| RunePrereqNotMet | 5010 | 선행 룬이 해금되지 않음 |
| RuneMaxLevel | 5011 | 룬이 최대 레벨에 도달 |

- `5001~5009`는 스킬/공통, `5010~5019`는 룬에 할당한다.
- 골드 부족은 신규 코드를 만들지 않고 `InsufficientCurrency(4005)`를 재사용한다.

## 8. 미결 사항 / TODO

- **마스터 데이터 반영 완료 · 밸런스 수치만 대기**: `level_master`(레벨별 요구 경험치·스탯·누적 스킬 포인트)와 `skill_master.skill_type`(1:액티브 2:패시브)를 [마스터 데이터 기획서](master-data-기획서.md)(`skill_master` 5.6·`level_master` 5.12)에 **테이블/필드로 반영 완료**. 남은 것은 **밸런스 수치**(레벨 곡선·레벨당 스킬 포인트·룬 레벨별 골드 비용) 확정뿐이다.
- **액티브 스킬 장착 (확정 · 반영 완료)**: 액티브 스킬은 **캐릭터당 최대 2개**만 장착·사용(패시브는 제한 없이 상시 적용). 장착은 API 5.3(`skill/equip`)으로 설정하고 `player_skill.equipped`(0/1)에 저장한다. 남은 미결: 액티브 스킬의 **전투 시 발동 순서·쿨다운 등 사용 규칙**은 전투 도메인(4.6)과 함께 정의.

## 9. 참고

- [서버 시스템 전체 개요](../공통/서버-시스템-전체-개요.md) — 도메인 4.5(성장), 4.6(전투=경험치 획득)
- [세이브 데이터 기획서](save-data-기획서.md) — `game_player`·`player_character`·`player_skill`·`player_rune` 저장 골격, 로드 스냅샷
- [마스터 데이터 기획서](master-data-기획서.md) — `class_master`·`skill_master`·`rune_master`
- [인벤토리/아이템/큐브 기획서](inventory-item-cube-기획서.md) — 장비 강화·장비 클래스/레벨 제한
- [GameErrorCode 통합 정의](../공통/error-code-정의.md) — 에러 코드 블록 규약(5000번대 성장)
