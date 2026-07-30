# 세이브 데이터 구조 / 저장 정책 기획서

> 상위 문서: [서버 시스템 전체 개요](../서버-시스템-전체-개요.md) · 관련 도메인 4.2
>
> 인증/계정은 [계정/로그인 기획서](account-login-기획서.md) 참고. 본 문서는 인증된 플레이어의 **게임 진행 데이터(세이브)** 구조와 저장 정책을 다룬다.

## 목차

- [1. 개요](#1-개요)
- [2. 저장 아키텍처](#2-저장-아키텍처)
- [3. 데이터 모델 (ERD)](#3-데이터-모델-erd)
- [4. 저장 정책](#4-저장-정책)
- [5. API 명세](#5-api-명세)
  - [5.1 코어 세이브 로드 — `POST /api/game/load`](#51-코어-세이브-로드--post-apigameload)
  - [5.2 인벤토리 페이지 조회 — `POST /api/game/inventory/list`](#52-인벤토리-페이지-조회--post-apigameinventorylist)
  - [5.3 캐릭터 생성 — `POST /api/game/create-character`](#53-캐릭터-생성--post-apigamecreate-character)
  - [5.4 접속 시각 갱신(heartbeat) — `POST /api/game/update-last-active`](#54-접속-시각-갱신heartbeat--post-apigameupdate-last-active)
  - [5.5 파티 편성 저장 — `POST /api/game/party/arrange`](#55-파티-편성-저장--post-apigamepartyarrange)
- [6. 에러 코드 (신규 제안)](#6-에러-코드-신규-제안)
- [7. 미결 사항 / TODO](#7-미결-사항--todo)
- [8. 참고](#8-참고)


## 1. 개요

- **목적**: 플레이어의 게임 진행 상태(직업·레벨·재화·인벤토리·성장)를 서버에 영속 저장하고, 접속 시 로드한다. 방치형 게임 특성상 **마지막 접속(활동) 시각**이 오프라인 보상 계산의 기준점이 되므로 세이브 구조의 핵심 요소로 다룬다.
- **대상 서버**: `GameServer`(진행 데이터 저장/로드), `TaskbarHero.Common`(공유 DTO/에러 코드). 인증은 AccountServer가 발급한 토큰을 GameServer 미들웨어가 검증(Redis 대조).
- **서버 권위 원칙**: 재화·성장·아이템 등 이득이 되는 값은 서버가 최종 확정한다. 클라이언트가 보고한 값을 그대로 저장하지 않는다(치트 방지).
- **범위 경계**: 인벤토리/아이템·성장(직업·스킬·룬)·큐브의 **세부 규칙**은 각 도메인 기획서에서 다룬다. 본 문서는 이들을 담는 **저장 구조와 저장 정책**에 집중한다.

## 2. 저장 아키텍처

```
GameServer
  ├─ 로드(2단계 분리)
  │    ├─ 코어 스냅샷: /api/game/load        → 크기가 고정된 데이터 전부(게임 시작에 필요)
  │    └─ 지연 로딩:   /api/game/inventory/list → 가방 아이템만 slot 커서 페이징
  └─ 저장: 각 액션 API(캐릭터 생성·강화·스킬 레벨업·상점 구매 등)가
           서버 검증 후 자기 변경분을 그 요청 트랜잭션에서 MySQL 반영
           (별도의 일괄 저장 API는 두지 않음)
```

- **로드 2단계 분리(확정)**: 로드는 **"게임 시작에 필요한 고정 크기 데이터"**와 **"필요할 때 여는 가변 크기 데이터"**로 나눈다. 분리 기준은 데이터 종류가 아니라 **증가 차수**다.

  | 구분 | 항목 | 행 수 | 조회 |
  |---|---|---|---|
  | 고정 크기 | `player` | 1 | `/api/game/load` |
  | 고정 크기 | `characters` | ≤ 직업 수(현재 4, 보유 캐릭터) | `/api/game/load` |
  | 고정 크기 | `cube` | 1 | `/api/game/load` |
  | 고정 크기 | `currencies` | 재화 종류 수 | `/api/game/load` |
  | 고정 크기 | `runes` | `rune_master` 정의 수 | `/api/game/load` |
  | 고정 상한 | `skills` | 보유 캐릭터 수 × 직업별 스킬 수 | `/api/game/load` |
  | 고정 상한 | `equipped` | ≤ 보유 캐릭터 수 × 6슬롯(현재 4 × 6 = 24) | `/api/game/load` |
  | **가변** | **가방 아이템** | **`game_player.inventory_capacity`만큼(확장으로 증가)** | **`/api/game/inventory/list` (페이징)** |

  `player_item`만 무한히 커지므로 **이 하나만** 분리·페이징한다. 나머지를 항목별로 쪼개면 왕복만 늘고 이득이 없다.
- **페이지 간 정합성 장치를 두지 않는다(확정)**: 페이징 도중 인벤토리가 바뀌는지를 서버가 감지하는 장치(변경 카운터·버전 토큰)는 두지 않는다. 단일 세션 정책상 그 경합을 일으킬 수 있는 주체가 사실상 같은 클라이언트뿐이고, 최악의 경우도 창고를 다시 열면 사라지는 표시 오차다. 반면 장치를 두면 **아이템을 건드리는 모든 트랜잭션이 그 장치와 결합**되어, 새 기능을 붙일 때 한 곳만 빠뜨려도 아무 오류 없이 검증이 무력화된다. 비용이 편익을 넘어선다고 판단해 두지 않으며, 대신 클라이언트가 `itemId` 기준 병합으로 흡수한다(5.2).
- **장착 정보는 코어에 포함(확정)**: 장착 행은 보유 캐릭터 수 × 6슬롯(현재 최대 24개)으로 상한이 고정이고, 캐릭터 스탯 계산의 입력이라 **전투 시작 전에 반드시 있어야 한다.** 별도 요청으로 빼면 코어 로드 후에도 전투를 시작하지 못하므로, `/api/game/load` 응답에 `equipped` 배열로 함께 내린다. 가방 아이템 페이징 결과와는 겹치지 않는다(장착 아이템은 인벤 칸을 점유하지 않으므로 페이징 대상에서 제외).
- **액션 단위 저장(확정)**: 게임 상태 변경은 **각 기능 API가 처리하는 그 시점에** 서버가 검증·반영한다. 클라이언트가 진행 상태를 모아 보내는 **범용 일괄 저장 API(`/api/game/save`)는 두지 않는다.** 저장 시점·값은 클라이언트가 아니라 각 액션의 서버 로직이 결정한다.
- **MySQL 단일 저장소(확정)**: 세이브 데이터는 **MySQL에만** 저장한다. 3장 ERD의 정규화 테이블 구조를 그대로 사용하며, JSON 스냅샷 컬럼 등 별도 저장 방식은 쓰지 않는다. 계정 도메인과 동일하게 시간 값은 **Unix timestamp(BIGINT, 초)**로 저장.
- **Redis 미도입(확정)**: 세이브 데이터에는 Redis 캐시 계층을 두지 않고 MySQL에 직접 읽고 쓴다. (인증 토큰 검증용 Redis는 별개 용도이며 세이브 저장과 무관하다.)
- **정합성**: 재화·인벤토리 변경은 트랜잭션으로 원자성을 보장한다.

## 3. 데이터 모델 (ERD)

`GameServer` 전용 MySQL 데이터베이스. 모든 테이블의 `user_id`는 계정(`AccountServer`의 `users.user_id`)과 동일한 식별자를 사용한다(서버 간 공유 키). 계정은 **파티 자리 3개**를 가지며(3인 파티가 함께 전투, [성장 시스템 기획서](growth-기획서.md)), 직업 중복이 불가하므로 **보유 캐릭터는 직업 수(현재 4)까지** 가질 수 있고 그중 3명을 편성한다(미편성은 `player_character.slot=0`, 5.5). **직업·레벨·경험치·스킬·장비는 캐릭터별**, **인벤토리·골드·큐브는 계정 공유**다.

```mermaid
erDiagram
    game_player      ||--o{ player_character : has
    game_player      ||--o{ player_item      : owns
    game_player      ||--o{ player_rune      : has
    game_player      ||--|| player_cube      : has
    game_player      ||--o{ player_buff      : "has active"
    player_item      ||--o| player_item_equipped : "equipped as"
    player_character ||--o{ player_item_equipped : equips
    player_character ||--o{ player_skill     : has
    game_player      ||--o{ player_mail      : receives
    game_player      ||--|| player_attendance : progresses
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
        int     character_id "캐릭터 고유 식별자(생성 순번), 불변"
        int     class_code "직업(중복 불가)"
        int     slot "파티 자리(0=미편성, 1~3)"
        int     gender "성별 1:남 2:여 (기본 1:남)"
        int     level
        bigint  exp
    }

    player_item {
        bigint  player_item_id PK
        bigint  user_id FK
        int     row_type "1:아이템 2:재화"
        int     item_code "item_master.item_code (재화 item_type=3 포함, 골드=1)"
        bigint  quantity "수량/재화 금액(재화가 커 bigint)"
        int     slot "인벤토리 배치(0-based). 재화·장착 중 장비는 NULL(용량 미집계)"
        int     enhance_level "장비 강화/각인 단계. 재화/비장비는 0"
        bigint  acquired_at
    }

    player_item_equipped {
        bigint  player_item_id PK "player_item.player_item_id, 장착 아이템 1개당 1행"
        bigint  user_id FK
        int     item_code "item_master.item_code (어떤 아이템인지)"
        int     enhance_level "장비 강화 단계"
        int     equipped_character_id "장착 캐릭터(character_id)"
        int     equipped_slot "장착 슬롯(equip_slot_master)"
    }

    player_skill {
        bigint  user_id FK
        int     character_id "캐릭터 고유 식별자"
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

    player_buff {
        bigint  user_id PK,FK
        int     buff_type PK "획득량 버프 종류 1:경험치 2:골드"
        decimal buff_value "획득량 배율(1.500=150%)"
        bigint  started_at "버프 시작 Unix ts(소급 구간 하한)"
        bigint  expires_at "버프 만료 Unix ts(소급 구간 상한)"
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
        bigint  user_id PK,FK
        int     attend_count "누적 출석일수(리셋 없음)"
        int     last_attend_date "마지막 보상 획득 일자 YYYYMMDD(KST), 0=없음"
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
  - `player_character`: `(user_id, character_id)` 복합 PK. `character_id`는 **캐릭터 고유 식별자**(생성 순번)이며 파티 자리가 아니다 — 파티 자리는 `slot`(0=미편성, 1~3)이 담고, `(user_id, class_code)` 유니크로 계정 내 직업 중복을 막는다. `slot` 1~3의 유일성은 MySQL 부분 유니크 인덱스 미지원으로 **서버가 트랜잭션에서 보장**한다(5.5). `gender`(성별 1:남 2:여)는 **캐릭터 생성 시 선택**해 저장하고 이후 변경하지 않으며, 외형(남/여 스프라이트)만 가르는 표현용 값이라 직업·레벨·스탯 계산에는 관여하지 않는다. 컬럼 기본값이 `1`(남)이므로 **성별 도입 이전에 생성된 캐릭터는 모두 남자**가 된다.
  - `player_skill`: `(user_id, character_id, skill_code)` 복합 PK. 캐릭터별 스킬 레벨·액티브 장착.
  - `player_rune`: `(user_id, rune_code)` 복합 PK. 룬은 **계정 공용**이라 `character_id`를 두지 않는다.
  - `player_buff`: `(user_id, buff_type)` 복합 PK — 계정당 버프 **종류별 1행**이며, 이 PK 행 잠금이 같은 종류의 중복 사용 요청을 직렬화한다(별도 분산 락 없음). 종류가 다른 버프(경험치·골드)는 별도 행이라 동시 활성된다. 활성 판정은 `expires_at > now`이고 만료 행은 조회에서 걸러지므로 즉시 삭제하지 않는다 — 오프라인 정산이 만료 버프의 유효 구간을 **소급 참조**하기 때문이다([소모품/버프 기획서](consumable-buff-기획서.md) 4.2·6.3). 정리 배치용 `expires_at` 보조 인덱스를 둔다.
  - `player_mail`: `mail_id` PK, `user_id` 조회 인덱스. 계정 우편함.
  - `player_mail_reward`: `(mail_id, seq)` 복합 PK. 메일 첨부(0~N).
  - `player_attendance`: `user_id` PK로 **계정당 1행**(출석 진행도). `attend_count`(누적 출석일수)로 일차를 산출하고(`% 30 + 1`, 30일 순환) `last_attend_date`로 하루 1회를 보장한다. 일자별 출석 이력은 저장하지 않는다([출석부 보상 시스템 기획서](attendance-기획서.md)).
  - `trade_listing`: `listing_id` PK, `(status, item_code, price)`·`(status, price)`·`(seller_user_id, status)`·`(status, expires_at)` 인덱스. 전역 거래소 등록(에스크로), 등록 아이템은 `player_item`에서 빠져 여기 스냅샷으로 보관([거래소 / 교역선 기획서](trade-기획서.md)). 목록 조회는 전역 공유 읽기이므로 Redis 목록 캐시를 상시 사용하고, 동시 구매는 Redis 락 + 조건부 갱신으로 직렬화한다(같은 기획서 7장).
  - `player_item`: `player_item_id` PK. `(user_id, slot)` 유니크 — 한 인벤토리 칸(slot)에는 아이템(스택) 한 행만 존재한다(재화 행은 `slot`이 NULL이라 무제한 공존). 이 유니크 인덱스는 **인벤토리 페이지 조회(5.2)의 keyset 커서 인덱스로 그대로 재사용**하므로, 페이징을 위해 추가하는 컬럼·인덱스가 없다.
  - `player_item_equipped`: `player_item_id` PK — 장착 중인 아이템만 행으로 존재하며 아이템당 최대 1행이라 한 아이템은 동시에 한 곳에만 장착된다. `(user_id, equipped_character_id, equipped_slot)` 유니크 — **한 캐릭터-장착슬롯에 아이템 하나**를 보장한다. 장착=INSERT, 해제=DELETE.
- **아이템·재화 통합(`row_type`)**: `player_item`은 `row_type`(1:아이템 2:재화)으로 아이템과 재화(골드 등)를 **한 테이블에** 담는다. `item_code`는 **모든 행이 `item_master.item_code`를 참조**하며(재화는 `item_master`의 `item_type=3` 항목, 골드=`item_code` 1 — 별도 `currency_master` 없음), `quantity`가 수량/재화 금액(재화가 커 `bigint`)이다. **재화 행은 계정에 재화 종류당 1행**이어야 하므로 `(user_id, row_type=2, item_code)` 유일성을 **서버가 보장**한다(MySQL 부분 유니크 인덱스 미지원. 아이템 행은 스택 분할로 `(user_id, item_code)`가 중복될 수 있어 전역 유니크를 걸 수 없다). 재화 행은 `slot`/`enhance_level`을 쓰지 않고 장착 대상도 아니며(`player_item_equipped`에 행이 생기지 않음) **인벤토리 용량 집계에서 제외**한다.
- **캐릭터별 vs 계정 공유**: `player_character`·`player_skill`은 **캐릭터별**, `player_item`(아이템·재화)·`player_cube`·`player_rune`은 **계정 공유**다. 스킬은 캐릭터마다 다르게 찍고 룬은 계정 전체에 적용되므로 테이블을 분리한다. 아이템은 계정 공용 행(`player_item`)이되 장착만 캐릭터별이다 — 장착 상태는 자식 테이블 `player_item_equipped`(`player_item`과 1:0..1)에 분리해, 그 행의 `equipped_character_id`/`equipped_slot`으로 **어느 캐릭터의 어느 슬롯에 장착됐는지**를 표기하며, 한 아이템은 최대 한 캐릭터·한 슬롯에만 장착된다.
- **인벤토리 배치 위치(`player_item.slot`)**: 아이템(스택)이 인벤토리 UI의 몇 번 칸에 있는지를 나타내는 위치 값(0-based)이다. 클라이언트 재접속 시 인벤토리 페이지 조회(5.2)가 내려준 `slot`으로 **마지막 접속과 동일한 배치**를 복원한다. `slot`은 배치 값인 동시에 **페이징 정렬키이자 커서**다. 플레이어가 드래그로 칸을 옮기면 그 변경은 배치 변경 API로 반영한다([인벤토리/아이템/큐브 기획서](inventory-item-cube-기획서.md) 5.5). `player_item_equipped.equipped_slot`(장착 슬롯)과는 다른 개념이다. 배치는 UI 레이아웃 값이므로 서버 권위 검증 대상은 아니나, 용량(`game_player.inventory_capacity`) 범위 안이고 칸이 중복되지 않는지는 검증한다.
- **`slot`이 NULL인 행(확정)**: **재화 행**(계정당 종류별 1행)과 **장착 중인 장비**다. 둘 다 가방 칸을 점유하지 않으므로 용량 집계와 가방 페이지 조회(5.2)에서 함께 빠진다. 장착 중 장비의 `slot`이 NULL이라는 점이 "장착한 장비는 가방을 차지하지 않는다"는 규칙을 저장 구조로 표현한 것이며, 조회 쿼리에 별도 제외 조건을 두지 않아도 되게 한다. 장착/해제 시의 칸 반납·재배치 규칙은 [인벤토리/아이템/큐브 기획서](inventory-item-cube-기획서.md) 5.1·5.2에 있다.
![창고/인벤토리 화면 — 슬롯 격자에 배치된 아이템과 인벤토리 용량](../images/save-data-창고_인벤토리.png)

- **아이템/스킬/룬 등의 코드 값**은 마스터(기획) 데이터를 참조한다([마스터 데이터 기획서](master-data/master-data-기획서.md), 도메인 4.10). 각 코드 컬럼이 어느 마스터 테이블을 참조하는지는 해당 문서 3장의 매핑 표를 참고한다.
- `player_character`/`player_item`/`player_item_equipped`/`player_skill`/`player_rune`/`player_cube`/`player_buff`/`player_mail`/`player_attendance`/`trade_listing`의 **세부 필드·규칙**은 각 시스템 기획서(성장·인벤토리·[소모품/버프](consumable-buff-기획서.md)·[메일](mail-기획서.md)·[출석부](attendance-기획서.md)·[거래소](trade-기획서.md) 등)에서 확장한다. 본 ERD는 저장 골격이다.

## 4. 저장 정책

- **저장 시점 — 액션 단위(확정)**
  - **범용 일괄 저장 API 없음**: 진행 상태를 모아 저장하는 `/api/game/save` 같은 엔드포인트는 두지 않는다. 대신 **상태를 바꾸는 각 기능 API**(캐릭터 생성, 장비 장착/강화, 인벤토리 배치, 스킬 레벨업/초기화/장착, 룬 업그레이드, 큐브 합성/분해 등)가 처리 시점에 **자기 변경분을 그 요청 트랜잭션에서 DB에 반영**한다.
  - **서버 자동 저장 없음**: 서버는 주기적 자동 저장(autosave)을 하지 않는다. 저장은 위 액션이 일어날 때만 발생한다.
  - **스테이지 진행·경험치**: 자동 전투로 생기는 진행도(act/stage/max_stage_cleared)와 경험치·레벨은 **전투 결과 검증(도메인 4.6, 미작성)** 이 서버 권위로 산출·반영한다. 클라이언트가 임의 값을 올려 저장하지 않는다.
  - **마지막 접속 시각 주기 갱신(확정)**: 종료 시 로그아웃 요청으로 시각을 남기는 방식은 쓰지 않는다. 대신 클라이언트가 접속 후 **자동으로 5분 간격**으로 접속 시각 갱신 요청(`/api/game/update-last-active`)을 보내고, 서버는 `last_active_at`을 현재 서버 시각으로 갱신한다. 접속이 끊기면 마지막으로 갱신된 시각이 오프라인 경과 계산의 기준이 되며, 최대 오차는 갱신 주기(5분) 이내로 한정된다.
- **오프라인 기준 시각**: `last_active_at`(마지막 접속 시각)이 오프라인 보상 정산의 기준. 오프라인 보상 계산 규칙은 [오프라인 보상 정산 기획서](offline-reward-기획서.md) 참고.
- **서버 권위 검증**: 각 액션의 값은 서버 규칙·마스터 데이터로 재계산/검증 후 반영한다. 불가능한 증가폭·음수 재화 등은 거부한다(클라이언트 보고 불신).
- **동시성**: 동일 계정 단일 세션 정책([계정/로그인 기획서](account-login-기획서.md))에 따라 세이브 경합은 제한적이나, 각 액션 저장은 `user_id` 단위 트랜잭션으로 처리한다.

## 5. API 명세

**API 목록**

- [5.1 코어 세이브 로드 — `POST /api/game/load`](#51-코어-세이브-로드--post-apigameload)
- [5.2 인벤토리 페이지 조회 — `POST /api/game/inventory/list`](#52-인벤토리-페이지-조회--post-apigameinventorylist)
- [5.3 캐릭터 생성 — `POST /api/game/create-character`](#53-캐릭터-생성--post-apigamecreate-character)
- [5.4 접속 시각 갱신(heartbeat) — `POST /api/game/update-last-active`](#54-접속-시각-갱신heartbeat--post-apigameupdate-last-active)
- [5.5 파티 편성 저장 — `POST /api/game/party/arrange`](#55-파티-편성-저장--post-apigamepartyarrange)

Base URL(개발): `http://localhost:5247` (GameServer). 모든 API는 **POST**, 인증 요청 공통 형식 `{ userId, token, data }`를 사용한다(토큰은 body, [계정/로그인 기획서](account-login-기획서.md) 5장 참고). 응답은 `{ success, errorCode, message, data }` 형식이며, `errorCode`는 `TaskbarHero.Common`의 `ErrorCode`(6장) 값이고 `success`는 `errorCode == 0`과 동치다(계정·마스터 기획서와 동일한 응답 규약).

> 5.1·5.2는 **조회 계열**이라 본 문서에 함께 명세한다. 5.2의 경로가 `/api/game/inventory/*`인 것은 인벤토리 컨트롤러(`GameInventoryController`)에 배치되기 때문이며, 규칙(페이징·정합성)은 세이브 로드 정책에 속한다.

---

### 5.1 코어 세이브 로드 — `POST /api/game/load`

접속 후 **게임 시작에 필요한 고정 크기 데이터 전부**를 한 번에 로드한다. **가방 아이템은 포함하지 않는다**(5.2로 지연 로딩).

**반환 항목(전량, 이 목록이 계약이다)**

| 필드 | 내용 | 출처 테이블 | 크기 |
|---|---|---|---|
| `player` | 닉네임·진행 좌표(act/stage/difficulty)·최고 클리어·인벤 용량·마지막 활동 시각 | `game_player` | 1행 |
| `characters` | 파티 캐릭터의 슬롯·직업·성별·레벨·경험치 | `player_character` | ≤3행 |
| `currencies` | 재화 종류별 보유량(골드 포함) | `player_item` (`row_type=2`) | 재화 종류 수 |
| `equipped` | 캐릭터별 장착 장비(아이템 식별자·코드·강화 단계·장착 캐릭터/슬롯) | `player_item_equipped` (단독 — `item_code`·`enhance_level`을 함께 보관하므로 조인 불필요) | ≤18행 |
| `skills` | 캐릭터별 보유 스킬 코드·레벨·액티브 장착 여부 | `player_skill` (`level > 0` — 초기화로 레벨 0이 된 행은 미습득으로 제외, [성장 기획서](growth-기획서.md) 4장) | 3 × 직업 스킬 수 |
| `runes` | 계정 공용 룬 코드·레벨 | `player_rune` | 룬 마스터 수 |
| `cube` | 큐브 레벨·경험치 | `player_cube` | 1행 |
| `activeBuffs` | 활성 획득량 버프(종류·배율·시작/만료 시각) | `player_buff` (`expires_at > now`) | ≤ 버프 종류 수(현재 2행) |
| `inventoryTotal` | 가방 아이템 행 수(용량 UI 표시·페이징 진행률용) | `player_item` (`row_type=1`) COUNT | 스칼라 |
| `offlineElapsedSec` | `현재 서버 시각 - lastActiveAt` | 산출값 | 스칼라 |

> **구현 규칙**: 위 목록은 `GameServer/Services/SaveService.cs`의 `LoadAsync` **XML 주석(`/// <summary>`)에 항목별로 그대로 기재**한다. 반환 항목을 추가·제거할 때 이 표와 그 주석을 함께 갱신한다(서비스 메서드 주석 규칙, `CLAUDE.md`).

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
      { "characterId": 1, "classCode": 1, "slot": 1, "gender": 1, "level": 42, "exp": 128500 },
      { "characterId": 2, "classCode": 2, "slot": 2, "gender": 2, "level": 40, "exp": 90000 },
      { "characterId": 3, "classCode": 3, "slot": 3, "gender": 1, "level": 38, "exp": 60000 },
      { "characterId": 4, "classCode": 4, "slot": 0, "gender": 1, "level": 15, "exp": 2400 }
    ],
    "currencies": [
      { "currencyType": 1, "amount": 9875421 }
    ],
    "equipped": [
      { "itemId": 5001, "itemCode": 30012, "enhanceLevel": 3, "equippedCharacterId": 1, "equippedSlot": 1 }
    ],
    "skills": [
      { "characterId": 1, "skillCode": 101, "level": 5, "equipped": 1 }
    ],
    "runes": [
      { "runeCode": 205, "level": 3 }
    ],
    "cube": { "cubeLevel": 4, "cubeExp": 1200 },
    "activeBuffs": [
      { "buffType": 1, "buffValue": 1.5, "startedAt": 1752350000, "expiresAt": 1752351800 }
    ],
    "inventoryTotal": 3872,
    "offlineElapsedSec": 43200
  }
}
```

- `player`는 계정/파티 공용 값, `characters`는 **보유 캐릭터 전체**(파티 편성 여부와 무관)의 직업·파티 자리(`slot` **0=미편성**, 1~3=파티 위치)·성별(`gender` 1:남 2:여, 외형 전용)·레벨·경험치다. 편성된 캐릭터(`slot` 1~3)가 앞에 자리 순으로, 미편성 캐릭터가 뒤에 온다(5.5). `skills`는 `characterId`로 소속 캐릭터를 표시하며(스킬 행의 `equipped=1`은 액티브 장착, 캐릭터당 최대 2개), `runes`는 계정 공용이다. `currencies`·`equipped`·`cube`도 계정 공유(장착 위치만 캐릭터별).
- **`currencies`의 저장 출처**: `player_item`의 `row_type=2`(재화) 행을 `{currencyType(=item_code), amount(=quantity)}`로 투영한 결과다. 재화 행은 `slot`이 NULL이라 가방 페이징(5.2) 대상에서 자동으로 빠지므로, **재화는 코어에서만 내려간다.**
- **`equipped`(장착 장비)**: `player_item_equipped` 행을 그대로 내려준다. 장착 중에는 `player_item.slot`이 NULL이라 가방 칸을 점유하지 않으므로 5.2의 페이지 결과와 **중복되지 않는다.** 클라이언트는 이 배열만으로 캐릭터별 장비 렌더링과 스탯 계산을 끝낼 수 있고, 가방을 로드하지 않은 상태에서도 전투를 시작할 수 있다.
- **가방 아이템은 포함하지 않는다**: 총 개수(`inventoryTotal`)만 내려준다. 실제 아이템 목록은 창고/인벤토리 UI를 열 때 5.2로 조회한다.
- **`activeBuffs`(활성 획득량 버프)**: `player_buff`에서 `expires_at > now`인 행만 담는다(없으면 빈 배열). 계정당 버프 종류 수만큼(현재 최대 2행)으로 **크기가 고정**이라 코어 로드가 전량 내려준다. 접속 이후의 버프 UI 재동기화는 코어 로드 재호출이 아니라 전용 경량 조회(`POST /api/game/consumable/buffs`)가 담당한다. 클라이언트는 `expiresAt`(절대 시각)으로 잔여 시간을 표시하되 **만료를 자체 확정하지 않는다** — 배율 적용 여부는 항상 서버 판정이다([소모품/버프 기획서](consumable-buff-기획서.md) 5.2).
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

세이브가 없는 계정은 인벤토리도 없으므로 클라이언트는 5.2를 호출하지 않고 캐릭터 생성(5.3)으로 진행한다.

---

### 5.2 인벤토리 페이지 조회 — `POST /api/game/inventory/list`

가방 아이템을 **`slot` 커서 기준 keyset 페이징**으로 조회한다. 창고/인벤토리 UI를 열 때 호출하며, 접속 직후에는 호출하지 않는다.

**Request**
```json
{
  "userId": 1,
  "token": "MToxNzAwMDAwMDAwOmFCM2RFNmZHOWhKMWtM...",
  "data": { "cursor": -1, "limit": 200 }
}
```

- `cursor`: 직전 페이지에서 받은 `nextCursor`. **첫 페이지는 `-1`**(`slot`이 0-based이므로 `slot > -1`이 곧 처음부터).
- `limit`: 페이지 크기. 서버가 **1~500으로 클램프**하며 기본값 200. 범위 밖 값은 거부하지 않고 클램프한다(클라 버전 차이로 로드가 실패하지 않게).

**Response (성공, 200 OK)**
```json
{
  "success": true,
  "errorCode": 0,
  "message": "Inventory page",
  "data": {
    "items": [
      { "itemId": 5002, "slot": 0, "itemCode": 30012, "quantity": 1, "enhanceLevel": 3 },
      { "itemId": 5003, "slot": 1, "itemCode": 20101, "quantity": 64, "enhanceLevel": 0 }
    ],
    "nextCursor": 199,
    "hasMore": true,
    "total": 3872
  }
}
```

- `items`: `slot` 오름차순. **가방 아이템(`row_type=1` 이면서 `slot`이 있는 행)만** 담는다 — 재화 행과 장착 중인 장비는 `slot`이 NULL이라 `slot > cursor` 비교에서 자동으로 빠지며, 각각 코어 로드(5.1)의 `currencies`·`equipped`로 내려간다. **같은 아이템이 가방 목록과 `equipped`에 동시에 나오지 않는다.** 그래서 항목에 장착 필드가 없다.
- `nextCursor`: 이 페이지 마지막 항목의 `slot`. `hasMore=false`면 의미 없다.
- `hasMore`: 다음 페이지 존재 여부.
- `total`: 가방 아이템 총 행 수(진행률 표시용). 그 페이지를 읽은 시점 기준의 **근사치**다.
- 세이브가 없는 계정이면 `SaveNotFound(2001)`.

**조회 규칙 (확정)**

- **keyset 페이징만 사용한다.** `OFFSET`은 뒤 페이지로 갈수록 앞 행을 전부 스캔하므로 금지한다. 서버 조회 조건은 다음과 같고, `(user_id, slot)` 유니크 인덱스(3장)가 이를 완전히 커버한다.

  ```
  WHERE user_id = :userId AND row_type = 1 AND slot > :cursor
  ORDER BY slot ASC
  LIMIT :limit
  ```

- **정렬·필터는 서버가 제공하지 않는다.** 정렬키를 늘리면 인덱스가 커버하지 못해 페이징 성능 이점이 사라진다. 등급/종류별 정렬·필터는 클라이언트가 받은 페이지에 대해 로컬로 처리한다.
- **한 페이지 안의 목록과 `total`은 같은 스냅샷이다.** 서버가 두 쿼리를 하나의 읽기 트랜잭션(REPEATABLE READ)에서 처리하므로 페이지 내부는 항상 정합적이다.

**페이지 사이의 정합성 — 서버는 검증하지 않는다 (확정)**

페이지와 페이지 사이에 인벤토리가 바뀌어도 서버는 감지하지 않는다. 변경 카운터·버전 토큰 같은 장치를 두지 않으며, **클라이언트가 병합 규칙으로 흡수한다.**

- **근거**: 단일 세션 정책([계정/로그인 기획서](account-login-기획서.md))상 한 계정의 클라이언트는 하나다. 거래 체결·만료 반송은 아이템이 **메일**로 가고, 메일 수령·큐브·장착·이동·거래 등록은 전부 유저 행동이라 페이징 요청과 순차다. 배치(`MailGcBatchService`·`TradeExpireBatchService`)는 가방을 건드리지 않는다. 남는 경합은 **방치 전투 전리품**뿐인데, 그것을 발생시키는 주체도 같은 클라이언트라 자기가 바꿨다는 사실을 이미 안다.
- **클라이언트 병합 규칙(계약)**: 페이지를 이어붙일 때 **`itemId`를 키로 중복을 제거하고 나중 페이지를 우선**한다. 이동으로 같은 아이템이 두 페이지에 걸쳐도 최종 위치 하나만 남는다.
- **남는 오차와 해소**: 이미 지나간 칸으로 아이템이 이동하면 이번 조회에서 안 보이고, 읽은 뒤 소모된 아이템은 유령으로 남는다. 둘 다 **창고를 다시 열면 해소**되며, 유령 아이템을 조작해도 서버가 `ItemNotFound(4001)`로 거부하므로 데이터가 깨지지 않는다. 클라이언트는 그 에러를 받으면 목록을 새로 고친다.
- **전리품을 직접 받은 직후**: 클라이언트가 페이징 중에 `/api/game/stage/clear` 응답으로 아이템 획득을 확인했다면, 그 페이징을 첫 페이지부터 다시 받는 편이 정확하다. 서버 지원 없이 클라이언트 판단으로 처리한다.

---

### 5.3 캐릭터 생성 — `POST /api/game/create-character`

캐릭터를 **한 번에 1개** 생성한다. **생성 가능 조건은 직업 중복 금지 하나뿐**이며(보유 수 상한을 따로 두지 않는다 — 직업 중복이 불가하므로 보유 상한은 자연히 직업 수, 현재 4가 된다), 최초 호출 시 계정 세이브(`game_player`)가 함께 초기화된다. 서버가 **`characterId`(캐릭터 고유 식별자, 생성 순번)를 배정**하고 **빈 파티 자리가 있으면 가장 앞자리에 자동 편성**한다(파티가 이미 3명이면 `slot=0` 미편성 상태로 보유만 하며, 이후 5.5로 편성한다). **최초 생성(계정 초기화)은 무료이고, 2번째 이후 생성은 정액 골드를 소모**한다(비용은 마스터 `character_create_cost`의 **생성 순번**별 명시값, 현재 전 순번 500,000골드 — [마스터 데이터 기획서](master-data/master-data-기획서.md)). 골드 확인·차감·캐릭터 삽입은 한 트랜잭션으로 원자적으로 처리한다.

> **`characterId`는 파티 자리가 아니다(중요)**: `characterId`는 캐릭터를 가리키는 **고유 식별자**로 생성 후 바뀌지 않으며(`player_skill`·`player_item_equipped`가 이 값을 참조), 파티 자리는 별도 값 `slot`이 담는다. 편성 저장은 5.5가 담당한다.

> **신규 가입 지원금 자동 발급**: 계정 세이브가 처음 만들어질 때(= 최초 캐릭터 생성) 서버가 **환영 메일을 같은 트랜잭션에서 발급**한다 — 문구는 `mail_master` 101, 첨부는 `newbie_reward_master`(현재 골드 10,000,000 + 경험치·골드 부스터 각 10개)이며 **무기한**이라 첫 접속이 늦어도 사라지지 않는다([메일 기획서](mail-기획서.md) 4장). 플레이어는 우편함에서 수령한다. `game_player`가 계정당 1행이므로 이 트랜잭션은 계정 생애에 **정확히 1회만** 성공하며, 그래서 지급 여부를 저장하는 컬럼 없이 중복 지급이 원천 차단된다. 지원금 정의(`newbie_reward_master`)가 비어 있으면 메일 없이 계정만 생성된다.

![캐릭터 생성 화면 — 직업 선택과 닉네임 입력](../images/save-data-캐릭터생성화면.png)

**Request**
```json
{
  "userId": 1,
  "token": "MToxNzAwMDAwMDAwOmFCM2RFNmZHOWhKMWtM...",
  "data": { "nickname": "hero", "classCode": 1, "gender": 1 }
}
```

- `nickname`: **최초 캐릭터 생성(계정 초기화) 시에만** 사용하며, 이후 호출에서는 무시한다.
- `classCode`: 생성할 캐릭터의 직업. **이미 보유한 캐릭터의 직업과 중복될 수 없다.**
- `gender`: 캐릭터 성별(`1`:남 `2`:여). **캐릭터마다 따로 고르며 중복 제약이 없다**(파티 3인이 모두 같은 성별이어도 된다). 외형(남/여 스프라이트)만 가르는 값이라 스탯·생성 비용에는 영향을 주지 않고, **생성 시 확정되어 이후 변경 API를 두지 않는다**. 그 외 값은 `InvalidGender(2007)`이며, 필드를 보내지 않으면 `1`(남)로 저장된다.

**Response (성공, 200 OK)**
```json
{
  "success": true,
  "errorCode": 0,
  "message": "Character created",
  "data": {
    "userId": 1, "characterId": 2, "classCode": 2, "gender": 2, "level": 1,
    "cost": { "currencyType": 1, "amount": 100000 },
    "balance": [ { "currencyType": 1, "amount": 900000 } ]
  }
}
```

- `characterId`: 서버가 배정한 캐릭터 고유 식별자(생성 순번).
- `slot`: 서버가 자동 배정한 파티 자리. 빈 자리가 없으면 **`0`(미편성)** 으로 생성되며, 이 경우 클라이언트는 5.5로 편성을 유도한다.
- `cost`·`balance`: 소모한 골드와 차감 후 잔액. **최초 생성은 무료라 `cost.amount=0`, `balance`는 빈 목록**이다. 생성 **전** 안내 비용은 클라이언트가 마스터 `character_create_cost` 번들에서 다음 생성 순번 값을 조회해 표시한다.
- 오류: `InvalidClassCode(2005)`(존재하지 않는 직업), `InvalidGender(2007)`(1·2 외의 성별 값), `InvalidCharacterId(2006)`(**이미 보유한 직업과 중복** — 전 직업을 보유한 상태의 생성 요청도 반드시 중복이므로 이 코드로 걸린다), `InsufficientCurrency(4005)`(생성 골드 부족).
- **파티가 가득 차 있어도 생성은 가능하다** — 보유와 편성이 분리되어 있으므로 새 캐릭터는 미편성(`slot=0`)으로 들어간다. 따라서 본 엔드포인트는 `PlayerAlreadyExists(2004)`를 반환하지 않는다.

---

### 5.4 접속 시각 갱신(heartbeat) — `POST /api/game/update-last-active`

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

---

### 5.5 파티 편성 저장 — `POST /api/game/party/arrange`

클라이언트 편성 UI에서 자리를 배치하고 **"저장"을 누른 결과를 1회 호출로 반영**한다. 요청은 "누구를 어디로 옮겨라"라는 **이동 절차가 아니라 저장 후의 파티 전체(스냅샷)** 다.

- **`members`가 곧 최종 파티다.** 목록에 담긴 캐릭터는 지정한 자리에 서고, **목록에 없는 보유 캐릭터는 자동으로 미편성(`slot=0`)** 이 된다. 그래서 추가·추방·교체·자리 바꾸기가 전부 이 하나로 표현된다 — 클라이언트는 "추가" 버튼을 눌렀을 때도 **이미 편성돼 있던 캐릭터까지 포함한 전체 목록**을 보낸다.
- **부분 이동 API를 두지 않는 이유**: 이동을 여러 번 나눠 보내면 중간 단계에서 자리 밀림이 생겨 최종 배치가 사용자 의도와 달라지고, 전원 교체처럼 파티가 잠시 비는 편성은 중간 단계에서 거부된다. 스냅샷은 최종 상태만 검증하므로 이런 문제가 없다.
- **골드를 소모하지 않는다.** 비용은 캐릭터 **생성 시에만** 발생한다(5.3).
- **캐릭터를 삭제하지 않는다.** 파티에서 내려도 행은 남고 레벨·경험치·스킬·장착 장비가 그대로 유지되므로 언제든 되돌릴 수 있다. 계정에서 캐릭터를 영구 삭제하는 수단은 제공하지 않는다.
- **미편성 캐릭터는 전투에 참가하지 않는다.** 스테이지 클리어·오프라인 보상의 **경험치는 편성된 캐릭터에게만** 지급된다(골드·아이템은 계정 공유라 영향 없음).
- **파티는 최소 1명**이다. `members`가 비어 있으면 전투를 시작할 수 없으므로 `CannotRemoveLastCharacter(2008)`로 거부한다.
- **멱등**: 같은 스냅샷을 몇 번 보내도 결과가 같다. 저장 버튼 연타·재전송에 안전하다.
- **원자성**: 한 트랜잭션에서 **계정의 편성을 전부 비운 뒤 목록대로 다시 세운다.** 두 캐릭터가 같은 자리를 스쳐 가는 중간 상태가 없으므로, `(user_id, slot)`에 부분 유니크 인덱스를 걸 수 없는 제약에도 자리 중복이 생기지 않는다.

**Request**
```json
{
  "userId": 1,
  "token": "MToxNzAwMDAwMDAwOmFCM2RFNmZHOWhKMWtM...",
  "data": {
    "members": [
      { "characterId": 3, "slot": 1 },
      { "characterId": 4, "slot": 2 },
      { "characterId": 1, "slot": 3 }
    ]
  }
}
```

- `members`: 저장 후의 파티 전체(**1~3개**). 순서는 무관하며 `slot` 값이 자리를 정한다.
- `members[].characterId`: 보유 캐릭터의 **고유 식별자**(파티 자리가 아니다).
- `members[].slot`: 파티 자리(**1~3**). 미편성은 값 `0`을 보내는 것이 아니라 **목록에서 빼는 것**으로 표현한다.
- 2명·1명만 담아도 된다(나머지는 미편성이 된다).

**Response (성공, 200 OK)**
```json
{
  "success": true,
  "errorCode": 0,
  "message": "Party arranged",
  "data": {
    "characters": [
      { "characterId": 3, "classCode": 3, "slot": 1, "gender": 1, "level": 38, "exp": 60000 },
      { "characterId": 4, "classCode": 4, "slot": 2, "gender": 1, "level": 15, "exp": 2400 },
      { "characterId": 1, "classCode": 1, "slot": 3, "gender": 1, "level": 42, "exp": 128500 },
      { "characterId": 2, "classCode": 2, "slot": 0, "gender": 2, "level": 40, "exp": 90000 }
    ]
  }
}
```

- `characters`: 갱신된 **보유 캐릭터 전체**를 편성 자리 순(미편성은 뒤)으로 회신한다. 클라이언트는 이 배열로 편성 패널을 다시 그리면 되고 별도의 재조회가 필요 없다.
- 오류:

  | 코드 | 조건 |
  |---|---|
  | `CannotRemoveLastCharacter(2008)` | `members`가 비어 있음(파티를 비울 수 없음) |
  | `CharacterNotFound(2009)` | 보유하지 않은 캐릭터가 목록에 있음 |
  | `PartySlotOccupied(2010)` | 인원 3명 초과 · `slot`이 1~3 범위 밖 · 같은 자리를 둘 이상이 지정 |
  | `InvalidCharacterId(2006)` | 같은 캐릭터를 두 자리에 지정 |

## 6. 에러 코드 (신규 제안)

`TaskbarHero.Common/ErrorCode.cs`의 `ErrorCode`에 추가 제안. 계정 도메인(1001~1006, [계정/로그인 기획서](account-login-기획서.md) 6장)과 중복되지 않도록 **세이브 도메인은 2000번대**를 사용한다.

| 이름 | 값 | 의미 |
|---|---|---|
| SaveNotFound | 2001 | 세이브 데이터 없음 |
| InvalidSaveData | 2002 | 액션 요청 값 검증 실패(불가능한 값·비정상 데이터) |
| PlayerAlreadyExists | 2004 | 더 생성할 수 있는 캐릭터가 없음(현재 캐릭터 생성 경로에서는 사용하지 않음) |
| InvalidClassCode | 2005 | 존재하지 않는 직업 코드 |
| InvalidCharacterId | 2006 | 잘못된 캐릭터 식별자(존재하지 않는 `characterId` 또는 이미 보유한 직업 중복 생성) |
| CannotRemoveLastCharacter | 2008 | 파티를 비울 수 없음(편성 목록이 비어 있음, 5.5) |
| CharacterNotFound | 2009 | 편성 목록에 보유하지 않은 캐릭터가 있음(5.5) |
| PartySlotOccupied | 2010 | 파티 자리 지정 오류(정원 초과·범위 밖·같은 자리 중복, 5.5) |

> `2003`(구 `SaveVersionMismatch`)은 세이브 스키마 버전(`data_version`) 제거로 폐기했다. 값 혼선을 막기 위해 재사용하지 않고 **결번**으로 둔다.

인벤토리 페이지 조회(5.2)는 세이브 없음(`SaveNotFound(2001)`) 외에 전용 에러 코드를 쓰지 않는다. 페이지 간 정합성을 검증하지 않으므로 그에 대응하는 코드도 없다.

## 7. 미결 사항 / TODO

- **재화 종류 목록**: 골드 외 추가 재화 도입 여부·정의(향후 재화 정책과 연계, 재화는 `item_master`의 `item_type=3` 항목으로 정의).
- **heartbeat 주기(5분) 확정값 검토**: 5분 주기는 확정이나, 강제 종료 시 최대 5분의 접속 시각 오차가 오프라인 보상에 미치는 영향은 오프라인 보상 기획에서 함께 점검.
- **페이지 크기 기본값(200) 검토**: Unity `JsonUtility` 역직렬화 비용과 왕복 횟수의 균형점은 실측으로 정한다. 계약(클램프 1~500)은 그대로 두고 기본값만 조정한다.
- **`skills`·`runes` 분리 축 예약**: 현재는 크기가 작아 코어에 유지한다. 분리가 필요해지면 `POST /api/game/growth/skills`(`characterId` 필터)·`POST /api/game/growth/runes`로 뺀다. 경로만 예약하고 구현하지 않는다.

## 8. 참고

- [서버 시스템 전체 개요](../서버-시스템-전체-개요.md) — 도메인 4.2(저장/로드), 4.3(오프라인 보상)
- [계정/로그인 기획서](account-login-기획서.md) — 인증 토큰(body 전달)·단일 세션·`user_id` 공유 키
- [인벤토리/아이템/큐브 기획서](inventory-item-cube-기획서.md) — 인벤토리 액션(장착·강화·이동·용량 확장)과 아이템 도메인 에러 코드
