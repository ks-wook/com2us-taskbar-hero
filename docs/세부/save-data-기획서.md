# 세이브 데이터 구조 / 저장 정책 기획서

> 상위 문서: [서버 시스템 전체 개요](../공통/서버-시스템-전체-개요.md) · 관련 도메인 4.2
>
> 인증/계정은 [계정/로그인 기획서](account-login-기획서.md) 참고. 본 문서는 인증된 플레이어의 **게임 진행 데이터(세이브)** 구조와 저장 정책을 다룬다.

## 1. 개요

- **목적**: 플레이어의 게임 진행 상태(직업·레벨·재화·인벤토리·성장)를 서버에 영속 저장하고, 접속 시 로드한다. 방치형 게임 특성상 **마지막 접속(활동) 시각**이 오프라인 보상 계산의 기준점이 되므로 세이브 구조의 핵심 요소로 다룬다.
- **대상 서버**: `GameServer`(진행 데이터 저장/로드), `TaskbarHero.Common`(공유 DTO/에러 코드). 인증은 AccountServer가 발급한 토큰을 GameServer 미들웨어가 검증(Redis 대조).
- **서버 권위 원칙**: 재화·성장·아이템 등 이득이 되는 값은 서버가 최종 확정한다. 클라이언트가 보고한 값을 그대로 저장하지 않는다(치트 방지).
- **범위 경계**: 인벤토리/아이템·성장(직업·스킬·룬)·큐브의 **세부 규칙**은 각 도메인 기획서에서 다룬다. 본 문서는 이들을 담는 **저장 구조와 저장 정책**에 집중한다.

## 2. 저장 아키텍처

```
GameServer
  ├─ 로드: MySQL(영속) → 클라이언트로 스냅샷 전달
  └─ 저장: 각 액션 API(캐릭터 생성·강화·스킬 레벨업·상점 구매 등)가
           서버 검증 후 자기 변경분을 그 요청 트랜잭션에서 MySQL 반영
           (별도의 일괄 저장 API는 두지 않음)
```

- **액션 단위 저장(확정)**: 게임 상태 변경은 **각 기능 API가 처리하는 그 시점에** 서버가 검증·반영한다. 클라이언트가 진행 상태를 모아 보내는 **범용 일괄 저장 API(`/api/game/save`)는 두지 않는다.** 저장 시점·값은 클라이언트가 아니라 각 액션의 서버 로직이 결정한다.
- **MySQL 단일 저장소(확정)**: 세이브 데이터는 **MySQL에만** 저장한다. 3장 ERD의 정규화 테이블 구조를 그대로 사용하며, JSON 스냅샷 컬럼 등 별도 저장 방식은 쓰지 않는다. 계정 도메인과 동일하게 시간 값은 **Unix timestamp(BIGINT, 초)**로 저장.
- **Redis 미도입(확정)**: 세이브 데이터에는 Redis 캐시 계층을 두지 않고 MySQL에 직접 읽고 쓴다. (인증 토큰 검증용 Redis는 별개 용도이며 세이브 저장과 무관하다.)
- **정합성**: 재화·인벤토리 변경은 트랜잭션으로 원자성을 보장한다.

## 3. 데이터 모델 (ERD)

`GameServer` 전용 MySQL 데이터베이스. 모든 테이블의 `user_id`는 계정(`AccountServer`의 `users.user_id`)과 동일한 식별자를 사용한다(서버 간 공유 키). 계정은 **캐릭터 슬롯 3개**를 가지며(3인 파티가 함께 전투, [성장 시스템 기획서](growth-기획서.md)), **직업·레벨·경험치·스킬·장비는 캐릭터별**, **인벤토리·골드·큐브는 계정 공유**다.

```mermaid
erDiagram
    game_player      ||--o{ player_character : has
    game_player      ||--o{ player_item      : owns
    game_player      ||--o{ player_rune      : has
    game_player      ||--|| player_cube      : has
    player_character ||--o{ player_item      : equips
    player_character ||--o{ player_skill     : has
    game_player      ||--o{ player_mail      : receives
    game_player      ||--o{ player_attendance : checks_in
    game_player      ||--o{ trade_listing    : sells
    player_mail      ||--o{ player_mail_reward : has

    game_player {
        bigint  user_id PK "계정 user_id"
        varchar nickname
        int     act "현재 Act(파티 공용)"
        int     stage "현재 스테이지(파티 공용)"
        int     difficulty "난이도 티어"
        int     max_stage_cleared "최고 클리어 스테이지"
        int     inventory_capacity "인벤토리 최대 용량(slot 수), 골드로 확장"
        bigint  last_active_at "Unix ts, 5분 주기 갱신, 오프라인 보상 기준"
        bigint  created_at
        bigint  updated_at
    }

    player_character {
        bigint  user_id FK
        int     character_id "캐릭터 슬롯(1~3)"
        int     class_code "직업"
        int     level
        bigint  exp
    }

    player_item {
        bigint  item_id PK
        bigint  user_id FK
        int     row_type "1:아이템 2:재화"
        int     code "item_master.item_code (재화 item_type=4 포함, 골드=1)"
        bigint  quantity "수량/재화 금액(재화가 커 bigint)"
        int     slot "인벤토리 배치(0-based). 재화는 NULL(용량 미집계)"
        int     enhance_level "장비 강화/각인 단계. 재화/비장비는 0"
        int     equipped_character_id "장착 캐릭터(1~3), NULL=미장착/재화"
        int     equipped_slot "장착 슬롯(equip_slot_master), NULL=미장착/재화"
        bigint  acquired_at
    }

    player_skill {
        bigint  user_id FK
        int     character_id "캐릭터 슬롯(1~3)"
        int     skill_code "스킬 코드(skill_master)"
        int     level "스킬 레벨"
        int     equipped "액티브 장착 여부(0/1), 캐릭터당 최대 2개"
    }

    player_rune {
        bigint  user_id FK
        int     rune_code "룬 코드(rune_master)"
        int     level "룬 레벨"
    }

    player_cube {
        bigint  user_id PK,FK
        int     cube_level
        bigint  cube_exp
    }

    player_mail {
        bigint  mail_id PK
        bigint  user_id FK
        int     category "1:운영 2:거래 3:출석 4:시스템"
        varchar title
        varchar body
        int     is_read "0/1"
        int     claimed "0/1 첨부 수령 여부"
        bigint  created_at
        bigint  expires_at "0이면 무기한"
        bigint  claimed_at "미수령 0"
    }

    player_mail_reward {
        bigint  mail_id FK
        int     seq "메일 내 첨부 번호"
        int     reward_type "1:골드 2:아이템 3:재료"
        int     reward_code "골드면 0"
        int     quantity
    }

    player_attendance {
        bigint  user_id FK
        int     attend_date "출석 일자 YYYYMMDD(KST 기준)"
        bigint  claimed_at "출석/보상 메일 발급 시각(Unix ts)"
    }

    trade_listing {
        bigint  listing_id PK
        bigint  seller_user_id FK "판매자 user_id"
        int     item_code "판매 아이템(item_master)"
        int     enhance_level "장비 강화 단계 스냅샷"
        int     quantity
        bigint  price "구매가(골드)"
        int     status "1:판매중 2:판매완료 3:취소(만료 포함)"
        bigint  buyer_user_id "미판매 0"
        bigint  created_at
        bigint  expires_at "만료(= created_at + 3일)"
        bigint  closed_at "미완료 0"
    }
```

- **PK/유니크**:
  - `player_character`: `(user_id, character_id)` 복합 PK. `character_id`는 1~3.
  - `player_skill`: `(user_id, character_id, skill_code)` 복합 PK. 캐릭터별 스킬 레벨·액티브 장착.
  - `player_rune`: `(user_id, rune_code)` 복합 PK. 룬은 **계정 공용**이라 `character_id`를 두지 않는다.
  - `player_mail`: `mail_id` PK, `user_id` 조회 인덱스. 계정 우편함.
  - `player_mail_reward`: `(mail_id, seq)` 복합 PK. 메일 첨부(0~N).
  - `player_attendance`: `(user_id, attend_date)` 복합 PK. 출석한 일자당 1행([출석부 보상 시스템 기획서](attendance-기획서.md)).
  - `trade_listing`: `listing_id` PK, `seller_user_id`·`(status, item_code)` 인덱스. 전역 거래소 등록(에스크로), 등록 아이템은 `player_item`에서 빠져 여기 스냅샷으로 보관([거래소 / 교역선 기획서](trade-기획서.md)).
  - `player_item`: `item_id` PK. `(user_id, slot)` 유니크 — 한 인벤토리 칸(slot)에는 아이템(스택) 한 행만 존재한다(재화 행은 `slot`이 NULL이라 무제한 공존). `(user_id, equipped_character_id, equipped_slot)` 유니크 — **한 캐릭터-장착슬롯에 아이템 하나**를 보장한다. 미장착·재화 행은 `equipped_character_id`/`equipped_slot`이 **NULL**이며, MySQL 유니크 인덱스는 NULL을 서로 다른 값으로 취급하므로 무제한 공존한다.
- **아이템·재화 통합(`row_type`)**: `player_item`은 `row_type`(1:아이템 2:재화)으로 아이템과 재화(골드 등)를 **한 테이블에** 담는다. `code`는 **모든 행이 `item_master.item_code`를 참조**하며(재화는 `item_master`의 `item_type=4` 항목, 골드=`item_code` 1 — 별도 `currency_master` 없음), `quantity`가 수량/재화 금액(재화가 커 `bigint`)이다. **재화 행은 계정에 재화 종류당 1행**이어야 하므로 `(user_id, row_type=2, code)` 유일성을 **서버가 보장**한다(MySQL 부분 유니크 인덱스 미지원. 아이템 행은 스택 분할로 `(user_id, code)`가 중복될 수 있어 전역 유니크를 걸 수 없다). 재화 행은 `slot`/`enhance_level`/`equipped_*`를 쓰지 않으며 **인벤토리 용량 집계에서 제외**한다.
- **캐릭터별 vs 계정 공유**: `player_character`·`player_skill`은 **캐릭터별**, `player_item`(아이템·재화)·`player_cube`·`player_rune`은 **계정 공유**다. 스킬은 캐릭터마다 다르게 찍고 룬은 계정 전체에 적용되므로 테이블을 분리한다. 아이템은 계정 공용 행(`player_item`)이되 장착만 캐릭터별이다 — `equipped_character_id`/`equipped_slot`으로 **어느 캐릭터의 어느 슬롯에 장착됐는지**를 그 행에 직접 표기하며, 한 아이템은 최대 한 캐릭터·한 슬롯에만 장착된다(별도 장착 테이블 없음).
- **인벤토리 배치 위치(`player_item.slot`)**: 아이템(스택)이 인벤토리 UI의 몇 번 칸에 있는지를 나타내는 위치 값(0-based)이다. 클라이언트 재접속 시 로드 스냅샷의 `slot`으로 **마지막 접속과 동일한 배치**를 복원한다. 플레이어가 드래그로 칸을 옮기면 그 변경은 배치 변경 API로 반영한다([인벤토리/아이템/큐브 기획서](inventory-item-cube-기획서.md) 5.6). `player_item.equipped_slot`(장착 슬롯)과는 다른 개념이다. 배치는 UI 레이아웃 값이므로 서버 권위 검증 대상은 아니나, 용량(`game_player.inventory_capacity`) 범위 안이고 칸이 중복되지 않는지는 검증한다.
- **아이템/스킬/룬 등의 코드 값**은 마스터(기획) 데이터를 참조한다([마스터 데이터 기획서](master-data-기획서.md), 도메인 4.11). 각 코드 컬럼이 어느 마스터 테이블을 참조하는지는 해당 문서 3장의 매핑 표를 참고한다.
- `player_character`/`player_item`/`player_skill`/`player_rune`/`player_cube`/`player_mail`/`player_attendance`/`trade_listing`의 **세부 필드·규칙**은 각 시스템 기획서(성장·인벤토리·[메일](mail-기획서.md)·[출석부](attendance-기획서.md)·[거래소](trade-기획서.md) 등)에서 확장한다. 본 ERD는 저장 골격이다.

## 4. 저장 정책

- **저장 시점 — 액션 단위(확정)**
  - **범용 일괄 저장 API 없음**: 진행 상태를 모아 저장하는 `/api/game/save` 같은 엔드포인트는 두지 않는다. 대신 **상태를 바꾸는 각 기능 API**(캐릭터 생성, 장비 장착/강화, 소모품 사용, 인벤토리 배치, 스킬 레벨업/초기화/장착, 룬 업그레이드, 큐브 합성/분해 등)가 처리 시점에 **자기 변경분을 그 요청 트랜잭션에서 DB에 반영**한다.
  - **서버 자동 저장 없음**: 서버는 주기적 자동 저장(autosave)을 하지 않는다. 저장은 위 액션이 일어날 때만 발생한다.
  - **스테이지 진행·경험치**: 자동 전투로 생기는 진행도(act/stage/max_stage_cleared)와 경험치·레벨은 **전투 결과 검증(도메인 4.6, 미작성)** 이 서버 권위로 산출·반영한다. 클라이언트가 임의 값을 올려 저장하지 않는다.
  - **마지막 접속 시각 주기 갱신(확정)**: 종료 시 로그아웃 요청으로 시각을 남기는 방식은 쓰지 않는다. 대신 클라이언트가 접속 후 **자동으로 5분 간격**으로 접속 시각 갱신 요청(`/api/game/heartbeat`)을 보내고, 서버는 `last_active_at`을 현재 서버 시각으로 갱신한다. 접속이 끊기면 마지막으로 갱신된 시각이 오프라인 경과 계산의 기준이 되며, 최대 오차는 갱신 주기(5분) 이내로 한정된다.
- **오프라인 기준 시각**: `last_active_at`(마지막 접속 시각)이 오프라인 보상 정산의 기준. 오프라인 보상 계산 규칙은 [오프라인 보상 정산 기획서](offline-reward-기획서.md) 참고.
- **서버 권위 검증**: 각 액션의 값은 서버 규칙·마스터 데이터로 재계산/검증 후 반영한다. 불가능한 증가폭·음수 재화 등은 거부한다(클라이언트 보고 불신).
- **동시성**: 동일 계정 단일 세션 정책([계정/로그인 기획서](account-login-기획서.md))에 따라 세이브 경합은 제한적이나, 각 액션 저장은 `user_id` 단위 트랜잭션으로 처리한다.

## 5. API 명세

Base URL(개발): `http://localhost:5247` (GameServer). 모든 API는 **POST**, 인증 요청 공통 형식 `{ userId, token, data }`를 사용한다(토큰은 body, [계정/로그인 기획서](account-login-기획서.md) 5장 참고). 응답은 `{ success, errorCode, message, data }` 형식이며, `errorCode`는 `TaskbarHero.Common`의 `GameErrorCode`(6장) 값이고 `success`는 `errorCode == 0`과 동치다(계정·마스터 기획서와 동일한 응답 규약).

---

### 5.1 세이브 로드 — `POST /api/game/load`

접속 후 전체 세이브 스냅샷을 로드한다.

**Request**
```json
{
  "userId": 1,
  "token": "MToxNzAwMDAwMDAwOmFCM2RFNmZHOWhKMWtM...",
  "data": {}
}
```

**Response (성공, 200 OK)**
```json
{
  "success": true,
  "errorCode": 0,
  "message": "Load successful",
  "data": {
    "player": {
      "nickname": "hero",
      "act": 2,
      "stage": 15,
      "difficulty": 1,
      "maxStageCleared": 214,
      "inventoryCapacity": 100,
      "lastActiveAt": 1752300000
    },
    "characters": [
      { "characterId": 1, "classCode": 1, "level": 42, "exp": 128500 },
      { "characterId": 2, "classCode": 2, "level": 40, "exp": 90000 },
      { "characterId": 3, "classCode": 3, "level": 38, "exp": 60000 }
    ],
    "currencies": [
      { "currencyType": 1, "amount": 9875421 }
    ],
    "inventory": [
      { "itemId": 5001, "slot": 0, "itemCode": 30012, "quantity": 1, "enhanceLevel": 3, "equippedCharacterId": 1, "equippedSlot": 1 }
    ],
    "skills": [
      { "characterId": 1, "skillCode": 101, "level": 5, "equipped": 1 }
    ],
    "runes": [
      { "runeCode": 205, "level": 3 }
    ],
    "cube": { "cubeLevel": 4, "cubeExp": 1200 },
    "offlineElapsedSec": 43200
  }
}
```

- `player`는 계정/파티 공용 값, `characters`는 3인 파티 각 캐릭터의 직업·레벨·경험치다. `skills`는 `characterId`로 소속 캐릭터를 표시하며(스킬 행의 `equipped=1`은 액티브 장착, 캐릭터당 최대 2개), `runes`는 계정 공용이다. `currencies`·`inventory`·`cube`도 계정 공유.
- **`currencies`·`inventory`의 저장 출처**: 둘 다 하나의 `player_item` 테이블에서 나온 **투영(projection)**이다. `currencies`는 `row_type=2`(재화) 행을 `{currencyType(=code), amount(=quantity)}`로, `inventory`는 `row_type=1`(아이템) 행을 매핑한 결과다. 저장은 통합돼 있으나 응답은 클라이언트 편의를 위해 두 배열로 나눠 내려준다.
- **장착 상태**: 별도 `equipment` 배열을 두지 않는다. `inventory`의 각 아이템 행이 `equippedCharacterId`/`equippedSlot`(미장착이면 `null`)을 직접 가지므로, 클라이언트는 `equippedCharacterId`가 채워진 아이템을 캐릭터별 장착 장비로 렌더링한다.
- `offlineElapsedSec`: `현재 서버 시각 - lastActiveAt`. 오프라인 보상 계산의 입력값(정산 규칙은 [오프라인 보상 정산 기획서](offline-reward-기획서.md)).

**Response (세이브 없음 — 최초 접속, 200 OK)**
```json
{
  "success": true,
  "errorCode": 0,
  "message": "New player",
  "data": { "isNew": true }
}
```

---

### 5.2 캐릭터 생성 — `POST /api/game/create-character`

캐릭터를 **한 번에 1개** 생성한다. 계정당 최대 3개(3인 파티)이며 **직업은 서로 중복될 수 없다**. 최초 호출 시 계정 세이브(`game_player`)가 함께 초기화되고, 서버가 **빈 슬롯에 `characterId`(1~3)를 배정**한다.

**Request**
```json
{
  "userId": 1,
  "token": "MToxNzAwMDAwMDAwOmFCM2RFNmZHOWhKMWtM...",
  "data": { "nickname": "hero", "classCode": 1 }
}
```

- `nickname`: **최초 캐릭터 생성(계정 초기화) 시에만** 사용하며, 이후 호출에서는 무시한다.
- `classCode`: 생성할 캐릭터의 직업. **이미 보유한 캐릭터의 직업과 중복될 수 없다.**

**Response (성공, 200 OK)**
```json
{
  "success": true,
  "errorCode": 0,
  "message": "Character created",
  "data": { "userId": 1, "characterId": 1, "classCode": 1, "level": 1 }
}
```

- `characterId`: 서버가 배정한 슬롯(1~3).
- 오류: `InvalidClassCode(2005)`(존재하지 않는 직업), `InvalidCharacterId(2006)`(이미 보유한 직업과 중복), `PlayerAlreadyExists(2004)`(슬롯 3개가 모두 차 더 이상 생성 불가).

---

### 5.3 접속 시각 갱신(heartbeat) — `POST /api/game/heartbeat`

클라이언트가 접속 후 **5분 간격으로 자동 호출**한다. 서버는 `last_active_at`을 현재 서버 시각으로 갱신한다. 접속이 끊기면 마지막으로 갱신된 값이 오프라인 경과 시간 계산의 기준이 된다.

> 계정 세션/토큰 무효화는 AccountServer의 `/api/auth/logout`([계정/로그인 기획서](account-login-기획서.md) 5.3)이 담당한다. 본 엔드포인트는 **GameServer의 마지막 접속 시각(`last_active_at`) 갱신** 전용이며, 별도의 GameServer 로그아웃 요청은 두지 않는다.

**Request**
```json
{
  "userId": 1,
  "token": "MToxNzAwMDAwMDAwOmFCM2RFNmZHOWhKMWtM...",
  "data": {}
}
```

**Response (성공, 200 OK)**
```json
{
  "success": true,
  "errorCode": 0,
  "message": "Heartbeat OK",
  "data": { "lastActiveAt": 1752343500 }
}
```

- `lastActiveAt`: 서버가 기록한 접속 시각(Unix ts). 클라이언트가 보낸 시각이 아니라 **서버 시각**을 사용한다(치트 방지).

## 6. 에러 코드 (신규 제안)

`TaskbarHero.Common/ErrorCode.cs`의 `GameErrorCode`에 추가 제안. 계정 도메인(1001~1006, [계정/로그인 기획서](account-login-기획서.md) 6장)과 중복되지 않도록 **세이브 도메인은 2000번대**를 사용한다.

| 이름 | 값 | 의미 |
|---|---|---|
| SaveNotFound | 2001 | 세이브 데이터 없음 |
| InvalidSaveData | 2002 | 액션 요청 값 검증 실패(불가능한 값·비정상 데이터) |
| PlayerAlreadyExists | 2004 | 캐릭터 슬롯 3개가 모두 차 더 생성 불가 |
| InvalidClassCode | 2005 | 존재하지 않는 직업 코드 |
| InvalidCharacterId | 2006 | 잘못된 캐릭터 슬롯(존재하지 않는 `characterId`, 생성 시 슬롯 개수 오류 또는 직업 중복) |

> `2003`(구 `SaveVersionMismatch`)은 세이브 스키마 버전(`data_version`) 제거로 폐기했다. 값 혼선을 막기 위해 재사용하지 않고 **결번**으로 둔다.

## 7. 미결 사항 / TODO

- **재화 종류 목록**: 골드 외 추가 재화 도입 여부·정의(향후 재화 정책과 연계, 재화는 `item_master`의 `item_type=4` 항목으로 정의).
- **heartbeat 주기(5분) 확정값 검토**: 5분 주기는 확정이나, 강제 종료 시 최대 5분의 접속 시각 오차가 오프라인 보상에 미치는 영향은 오프라인 보상 기획에서 함께 점검.

## 8. 참고

- [서버 시스템 전체 개요](../공통/서버-시스템-전체-개요.md) — 도메인 4.2(저장/로드), 4.3(오프라인 보상)
- [계정/로그인 기획서](account-login-기획서.md) — 인증 토큰(body 전달)·단일 세션·`user_id` 공유 키
