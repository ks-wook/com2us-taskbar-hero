# 인벤토리 / 아이템 / 큐브 시스템 기획서

> 상위 문서: [서버 시스템 전체 개요](../서버-시스템-전체-개요.md) · 관련 도메인 4.4
>
> 본 문서는 플레이어가 보유한 **아이템의 저장·장착·강화·소모**와 **큐브(Hero-dric Cube)의 합성/분해/제작**을 서버 권위로 처리하는 규칙을 다룬다. 저장 골격은 [세이브 데이터 기획서](save-data-기획서.md)(`player_item`·`player_cube`), 아이템·강화·큐브의 정적 정의는 [마스터 데이터 기획서](master-data/master-data-기획서.md)(`item_master`·`enhance_master`·`cube_master`)를 참고한다.

## 목차

- [1. 개요](#1-개요)
- [2. 기능 설명](#2-기능-설명)
- [3. 요구사항](#3-요구사항)
- [4. 데이터 모델](#4-데이터-모델)
- [5. API 명세](#5-api-명세)
  - [5.1 장착 — `POST /api/game/inventory/equip`](#51-장착--post-apigameinventoryequip)
  - [5.2 장착 해제 — `POST /api/game/inventory/unequip`](#52-장착-해제--post-apigameinventoryunequip)
  - [5.3 강화 — `POST /api/game/inventory/enhance`](#53-강화--post-apigameinventoryenhance)
  - [5.4 인벤토리 용량 확장 — `POST /api/game/inventory/expand`](#54-인벤토리-용량-확장--post-apigameinventoryexpand)
  - [5.5 인벤토리 배치 변경(이동/교환) — `POST /api/game/inventory/move`](#55-인벤토리-배치-변경이동교환--post-apigameinventorymove)
  - [5.6 큐브 합성 — `POST /api/game/cube/combine`](#56-큐브-합성--post-apigamecubecombine)
  - [5.7 큐브 분해 — `POST /api/game/cube/dismantle`](#57-큐브-분해--post-apigamecubedismantle)
  - [5.8 큐브 제작 — `POST /api/game/cube/craft`](#58-큐브-제작--post-apigamecubecraft)
  - [5.9 랜덤 상자 열기 (골드 가챠) — `POST /api/game/box/open`](#59-랜덤-상자-열기-골드-가챠--post-apigameboxopen)
- [6. 처리 흐름](#6-처리-흐름)
- [7. 에러 코드](#7-에러-코드)
- [8. 미결 사항 / TODO](#8-미결-사항--todo)
- [9. 참고](#9-참고)


## 1. 개요

- **목적**: 방치형 전투로 획득한 장비·재료를 **인벤토리에 보관·정리**하고, 장비를 **장착/강화**해 전투력을 올리며, **큐브**로 아이템을 합성(등급 상승)·분해(골드 전환)·제작하는 성장 순환을 서버 권위로 검증·반영한다. 아이템은 실질적 가치(거래소 판매·전투력)를 가지므로 모든 수량·등급·강화 결과는 **서버가 최종 확정**한다(클라이언트 보고 불신).
- **대상 서버**: `GameServer`(인벤토리/장비/큐브 상태 관리·검증·반영), `TaskbarHero.Common`(분류 enum·결과 DTO 공유). 인증은 AccountServer 발급 토큰을 GameServer 미들웨어가 검증.
- **범위 경계**:
  - **아이템 *획득*(드롭)의 확정은 본 문서 밖**이다. 온라인 자동 전투의 전리품 산출은 [스테이지/전투 결과 검증](../서버-시스템-전체-개요.md)(도메인 4.6)이 서버 권위로 계산하며, 그 결과가 본 문서의 인벤토리에 적재된다. 오프라인 보상은 **아이템을 지급하지 않는다**([오프라인 보상 정산 기획서](offline-reward-기획서.md) 2장).
  - **장비 스탯의 최종 합산·전투력 계산**은 성장/전투 도메인에서 다룬다. 본 문서는 어떤 아이템이 어느 슬롯에 장착되어 있고 강화 단계가 얼마인지의 **상태 관리**까지를 책임진다.
  - **아이템의 플레이어 간 거래**는 [거래소/교역선](../서버-시스템-전체-개요.md)(도메인 4.7)에서 다룬다. 본 문서의 "분해"는 큐브를 통한 **골드 전환**이며 거래소 판매와 다르다.
- **관련 기획서**: [[save-data-기획서]] (저장 골격·서버 검증), [[master-data-기획서]] (아이템·강화·큐브 정적 정의), [[offline-reward-기획서]] (아이템 미지급), [[서버-시스템-전체-개요]] (도메인 4.4)

![인벤토리 전체 화면 — 획득한 아이템이 칸(slot)에 배치된 모습](../images/inventory-item-cube-인벤토리_전체.png)

## 2. 기능 설명

- **인벤토리 보관**: 획득한 아이템은 인벤토리에 쌓인다. 장비(`item_type=1`)는 개별 슬롯(강화 단계가 개체별로 다르므로 겹치지 않음), 재료(`item_type=2`)는 `stack_max`까지 **겹쳐서(stack)** 보관한다.

![장비 아이템 — 개체별 슬롯에 개별 저장되는 장비](../images/inventory-item-cube-장비아이템.png)

![재료 아이템 — stack_max까지 한 칸에 겹쳐 쌓이는 재료](../images/inventory-item-cube-재료아이템.png)

- **장착 / 해제**: 장비 아이템을 슬롯(무기·보조무기·투구·갑옷·장갑·신발, [마스터 데이터 기획서](master-data/master-data-기획서.md) 5.2)에 장착/해제한다. 이미 장착된 슬롯에 새 장비를 끼우면 기존 장비는 인벤토리로 되돌아온다(스왑).
- **강화**: 장비를 재화(골드 등)를 소모해 강화 단계(`enhance_level`)를 올린다. 단계별 비용·스탯 배율은 `enhance_master`가 정의한다.
- **랜덤 상자 열기(골드 가챠)**: 플레이어가 원할 때 **골드를 소모**해 상자를 연다(가챠). 서버가 골드를 차감한 뒤 상자 정의(`box_master`)의 **등급 확률**로 등급을 추첨하고 그 등급의 아이템 중 하나를 무작위로 뽑아 **랜덤 등급의 랜덤 아이템**을 지급한다. 상자는 인벤토리에 적재되는 아이템(`item_type`)이 아니라, **골드를 소비해 즉시 보상을 산출**하는 방식이다. (원작은 몬스터 처치 시 확률로 오픈 기회를 얻는 방식이나, 본 프로젝트는 골드 소모형 가챠로 대체한다.)
- **큐브(Hero-dric Cube)**: 원작의 성장형 큐브. 사용할수록 큐브 자신이 성장(`cube_level`)한다. 필요 없는 아이템은 **분해**로 골드로 전환한다(별도 폐기 기능은 두지 않음).
  - **합성(combine)**: 같은 등급의 아이템 여러 개를 소모해 **한 등급 높은** 아이템을 만든다(`cube_master.combine_grade_up`·`combine_count`). **슬롯·클래스는 서로 달라도 된다.**
  - **분해(dismantle)**: 아이템을 분해해 **골드로 전환**한다(`cube_master.gold_per_scrap`).
  - **제작(craft)**: 재료를 소모해 지정 아이템을 만든다(레시피 기반).

## 3. 요구사항

**기능 요구사항**
- 인벤토리 조회는 별도 API를 두지 않고 [세이브 로드](save-data-기획서.md)(`POST /api/game/load`)가 전체 인벤토리·장비·큐브 스냅샷을 반환한다. 본 문서는 **상태를 바꾸는 액션**만 전용 엔드포인트로 제공한다.
- 장착/해제, 강화, 상자 개봉, 큐브 합성/분해/제작을 각각 처리하고, 결과(변경된 인벤토리·재화·큐브 상태)를 응답한다.
- 큐브 합성 등 **결과가 확률/규칙에 따라 결정되는 연산은 서버가 산출**하고 클라이언트는 결과만 받는다.
- 모든 재화 소모·아이템 증감은 마스터 데이터 제약(존재 여부, `stack_max`, 최대 강화 단계, 슬롯-아이템 타입 정합성, 장비 클래스·레벨 제한)을 서버가 검증한 뒤 반영한다.

**비기능 요구사항**
- **서버 권위**: 수량·등급·강화 단계·개봉 결과는 서버가 마스터 데이터로 계산·검증한다. 클라이언트가 보낸 결과값은 신뢰하지 않는다.
- **원자성**: "재화 차감 + 아이템 증감(+장비/큐브 상태 변경)"은 하나의 `user_id` 단위 트랜잭션으로 처리한다. 중도 실패 시 전체 롤백하여 재화만 빠지거나 아이템만 생기는 상태를 막는다.
- **동시성/멱등성**: 단일 세션 정책([계정/로그인 기획서](account-login-기획서.md))으로 경합은 제한적이나, 대상 행(`player_item_id`/`user_id`)에 잠금을 걸어 같은 아이템에 대한 중복 강화·중복 소모를 막는다. 개별 액션은 서버가 대상 상태를 확인 후 반영하므로 동일 요청 재전송 시 이미 소모/장착된 상태면 해당 에러 코드로 거부된다.

## 4. 데이터 모델

본 시스템은 [세이브 데이터 기획서](save-data-기획서.md) 3장의 기존 테이블을 사용하며, **새 영속 테이블을 요구하지 않는다.** 다만 인벤토리 용량 확장(5.4)을 위해 `game_player`에 컬럼 1개(`inventory_capacity`)를 추가한다. 아래는 본 도메인 관점에서 각 테이블의 역할과 이 문서에서 확정/제안하는 세부 규칙이다.

| 테이블 | 역할 | 참조 마스터 |
|---|---|---|
| `player_item`(`player_item_id` PK, `user_id`, `row_type`, `item_code`, `quantity`, `slot`, `enhance_level`, `acquired_at`) | 보유 **아이템·재화 통합** 테이블 (**계정 공유**). `row_type`(1:아이템 2:재화)로 구분, `item_code`는 **모든 행이 `item_master.item_code`**(재화는 `item_type=3`, 골드=1), `quantity`는 수량/재화 금액(`bigint`). 보유 상태만 담고 장착 여부는 `player_item_equipped`로 분리 | `item_master`, `enhance_master` |
| `player_item_equipped`(`player_item_id` PK, `user_id`, `item_code`, `enhance_level`, `equipped_character_id`, `equipped_slot`) | **장착 중 아이템**만 담는 자식 테이블(`player_item`과 1:0..1). 행이 존재하면 장착 중 | `item_master`, `enhance_master`, `equip_slot_master` |
| `player_cube`(`user_id` PK, `cube_level`, `cube_exp`) | 큐브 성장 상태 (**계정 공유**) | `cube_master` |
| `game_player`(`inventory_capacity` 신규 컬럼) | 계정 인벤토리 최대 용량(골드로 확장) | — |

> **장착 상태는 별도 테이블(`player_item_equipped`)로 분리한다.** 아이템 보유(`player_item`)와 장착 위치는 관심사가 다르고, 아이템 행에 장착 컬럼을 두면 대다수 미장착 행에 NULL이 깔려 의미가 흐려진다. 장착 중인 아이템만 `player_item_equipped`에 행으로 두어 **장착=INSERT / 해제=DELETE**로 처리하고, PK `player_item_id`로 "한 아이템은 한 곳에만 장착", `(user_id, equipped_character_id, equipped_slot)` 유니크 인덱스로 "한 캐릭터-슬롯당 아이템 하나"를 보장한다([세이브 데이터 기획서](save-data-기획서.md) 3장).

> **재화(골드 등)도 `player_item`에 통합한다(별도 `player_currency`·`currency_master` 없음).** 재화는 `row_type=2` 행으로 저장하고 `item_code`=재화의 `item_code`(`item_master` `item_type=3`, 골드=1), `quantity`=재화 금액이다. 강화/제작 비용 차감, 분해 골드 적립, 용량 확장·상자 오픈 비용 차감 등 **재화 증감은 해당 재화 행의 `quantity` UPDATE**로 처리한다. 재화 행은 `slot`이 NULL이라 인벤토리 용량 집계에서 제외되며, 계정당 재화 종류당 1행 유일성은 서버가 보장한다([세이브 데이터 기획서](save-data-기획서.md) 3장). API 응답의 `cost`/`balance`/`currencies`는 이 재화 행에서 파생한다.

![골드(재화) 표시 — player_item에 통합 저장되는 골드](../images/inventory-item-cube-골드.png)

- **캐릭터별/계정 공유**: 계정은 캐릭터 슬롯 3개(3인 파티, [성장 시스템 기획서](growth-기획서.md))를 가진다. **인벤토리·골드·큐브는 계정 공유**(위 표 `user_id` 단위)이고, **장비 장착만 캐릭터별**이다(`equipped_character_id` 1~3). 한 아이템 행(`player_item_id`)은 계정 공용이지만 **동시에 한 캐릭터·한 슬롯에만 장착**된다.

**보관/스택 규칙 (확정)**
- **배치 위치(`slot`)**: 각 행은 인벤토리 UI의 특정 칸(`slot`, 0-based)에 놓인다. `(user_id, slot)`은 유니크하며 한 칸에는 한 행만 존재한다. 재접속 시 [세이브 로드](save-data-기획서.md)가 `slot`을 함께 내려 **마지막 접속과 동일한 배치를 복원**한다. 획득 시 서버는 빈 `slot`에 배치하고, 빈 칸이 없으면(용량 초과) `InventoryFull(4002)`. `player_item_equipped.equipped_slot`(장착 슬롯)과는 별개 개념이다.
- **장비(`item_type=1`)**: `stack_max=1`. 개체마다 `enhance_level`이 다를 수 있으므로 **1개당 1 행(row)**으로 저장하며 겹치지 않는다. `player_item_id`가 개체 식별자다.
- **비장비(`item_type=2`)**: 동일 `item_code`는 `stack_max`까지 한 행에 `quantity`로 누적한다. 초과분은 새 행으로 분할한다.
- **장착 중 아이템**: `player_item_equipped`에 행이 있는 아이템이 "장착 중"이다. 장착 중 아이템은 인벤토리(계정 공용)에 그대로 존재하되 분해·거래 대상에서 제외한다(해제 후 가능). `player_item_equipped`의 PK가 `player_item_id`라 같은 아이템을 둘 이상의 캐릭터가 동시에 장착할 수 없다.

**공유 enum / DTO (TaskbarHero.Common)**
- `item_type`(1:장비 2:재료 3:재화), `reward_type`(1:골드 2:아이템 3:재료), `equip_slot` 등 분류 코드는 [마스터 데이터 기획서](master-data/master-data-기획서.md) 5장 공통 규칙에 따라 `TaskbarHero.Common`에 enum으로 고정한다(값 변경 금지).
- 액션 결과 DTO(장착 결과·강화 결과·큐브 결과 등, 5장 응답 `data` 구조)는 `TaskbarHero.Common`에 공유 DTO로 두는 것을 **제안**한다. 구체 필드는 5장 응답 스키마를 따르며, 클라이언트 UI 갱신에 사용한다.

**확정 필드 — 세이브 데이터 기획서 ERD 반영 필요:**
- **인벤토리 용량(`game_player.inventory_capacity`, int)**: 플레이어별 인벤토리 최대 슬롯 수. 기본값에서 시작해 **골드 소모로 확장**한다(5.4). 확장분이 플레이어마다 달라지므로 상수가 아닌 플레이어 단위 컬럼으로 저장한다. → [세이브 데이터 기획서](save-data-기획서.md) `game_player.inventory_capacity`로 반영 완료.

## 5. API 명세

**API 목록**

- [5.1 장착 — `POST /api/game/inventory/equip`](#51-장착--post-apigameinventoryequip)
- [5.2 장착 해제 — `POST /api/game/inventory/unequip`](#52-장착-해제--post-apigameinventoryunequip)
- [5.3 강화 — `POST /api/game/inventory/enhance`](#53-강화--post-apigameinventoryenhance)
- [5.4 인벤토리 용량 확장 — `POST /api/game/inventory/expand`](#54-인벤토리-용량-확장--post-apigameinventoryexpand)
- [5.5 인벤토리 배치 변경(이동/교환) — `POST /api/game/inventory/move`](#55-인벤토리-배치-변경이동교환--post-apigameinventorymove)
- [5.6 큐브 합성 — `POST /api/game/cube/combine`](#56-큐브-합성--post-apigamecubecombine)
- [5.7 큐브 분해 — `POST /api/game/cube/dismantle`](#57-큐브-분해--post-apigamecubedismantle)
- [5.8 큐브 제작 — `POST /api/game/cube/craft`](#58-큐브-제작--post-apigamecubecraft)
- [5.9 랜덤 상자 열기 (골드 가챠) — `POST /api/game/box/open`](#59-랜덤-상자-열기-골드-가챠--post-apigameboxopen)

Base URL(개발): `http://localhost:5247` (GameServer). 모든 API는 **POST**, 인증 요청 공통 형식 `{ userId, token, data }`, 응답 `{ success, errorCode, message, data }`([세이브 데이터 기획서](save-data-기획서.md) 5장과 동일 규약, `success`는 `errorCode == 0`과 동치). 아래 엔드포인트는 모두 **상태 변경 액션**이며, 조회는 `POST /api/game/load`를 사용한다.

> 이 액션 엔드포인트들은 **RNG·비용을 수반하는 서버 권위 연산**이며, 각 액션이 자기 변경분을 그 요청 트랜잭션에서 직접 저장한다(별도의 일괄 저장 API는 없음, [세이브 데이터 기획서](save-data-기획서.md) 4장). 예를 들어 큐브 합성 결과는 클라이언트가 보고하는 것이 아니라 서버가 산출해 반영한다.

### 5.1 장착 — `POST /api/game/inventory/equip`

지정 캐릭터에게 아이템을 장착한다. 장착 슬롯은 아이템의 `item_master.equip_slot`에서 파생하며, 서버는 `player_item_equipped`에 대상 아이템의 장착 행(`equipped_character_id`/`equipped_slot`)을 INSERT한다. 그 캐릭터의 같은 슬롯에 이미 장착된 장비가 있으면 그 장착 행을 DELETE해 스왑한다. 장비의 **클래스 제한**(`item_master.class_req`, `0`은 전 클래스 공용)이 **대상 캐릭터의 직업**(`player_character.class_code`, 기사/레인저/마법사)과 일치해야 하고, 그 캐릭터 `level`이 **요구 레벨**(`item_master.level_req`, **5레벨 단위**, `0`은 제한 없음) 이상이어야 하며, 어느 하나라도 위반하면 `ItemNotEquippable(4003)`로 거부한다.

**Request**
```json
{ "userId": 1, "token": "...", "data": { "characterId": 1, "itemId": 5001 } }
```

- `characterId`: 장착할 캐릭터 슬롯(1~3). `itemId`: 장착할 아이템(`player_item.player_item_id`).

**Response (성공, 200 OK)**
```json
{
  "success": true,
  "errorCode": 0,
  "message": "Equipped",
  "data": {
    "characterId": 1,
    "equipped": { "slot": 1, "itemId": 5001 },
    "unequipped": { "slot": 1, "itemId": 4900 }
  }
}
```

- `unequipped`: 스왑으로 미장착 상태로 되돌아온 기존 장비(없으면 `null`).
- 오류: `ItemNotFound(4001)`(인벤토리에 없음), `ItemNotEquippable(4003)`(장비가 아니거나 슬롯·클래스·레벨 부적합), `ItemEquipped(4007)`(다른 캐릭터가 이미 장착 중), `InvalidCharacterId(2006)`(잘못된 `characterId`).

### 5.2 장착 해제 — `POST /api/game/inventory/unequip`

지정 캐릭터의 지정 장착 슬롯 장비를 해제해 미장착 상태로 되돌린다(`player_item_equipped`의 해당 장착 행을 DELETE).

**Request**
```json
{ "userId": 1, "token": "...", "data": { "characterId": 1, "slot": 1 } }
```

- `slot`: 해제할 **장착 슬롯**(`equipped_slot`, 예: 무기=1).

**Response (성공, 200 OK)**
```json
{ "success": true, "errorCode": 0, "message": "Unequipped", "data": { "characterId": 1, "slot": 1, "itemId": 5001 } }
```

- 해당 캐릭터의 슬롯이 비어 있으면 `ItemNotFound(4001)`, 잘못된 `characterId`는 `InvalidCharacterId(2006)`.

### 5.3 강화 — `POST /api/game/inventory/enhance`

> **상태: 보류.** 장비 강화는 `enhance_master`(강화 단계별 비용·배율) 값이 아직 확정되지 않아 현재 **보류**한다([마스터 데이터 값](master-data/master-data-값.md) §7 미작성). 아래 명세는 도입이 확정될 경우의 기준안이다.

장비의 강화 단계를 1 올린다. 비용·배율은 `enhance_master`의 다음 단계 정의를 따른다.

**Request**
```json
{ "userId": 1, "token": "...", "data": { "itemId": 5001 } }
```

**Response (성공, 200 OK)**
```json
{
  "success": true,
  "errorCode": 0,
  "message": "Enhanced",
  "data": {
    "itemId": 5001,
    "enhanceLevel": 4,
    "cost": { "currencyType": 1, "amount": 8000 },
    "balance": [ { "currencyType": 1, "amount": 9867421 } ]
  }
}
```

- 트랜잭션: 재화 차감 → `enhance_level += 1`. 강화 성공/실패 확률 도입 여부는 8장 미결(현행은 비용 지불 시 **확정 상승**으로 가정).
- 오류: `ItemNotFound(4001)`, `ItemNotEquippable(4003)`(장비만 강화 가능), `MaxEnhanceReached(4004)`(다음 단계가 `enhance_master`에 없음), `InsufficientCurrency(4005)`.

### 5.4 인벤토리 용량 확장 — `POST /api/game/inventory/expand`

골드를 소모해 인벤토리 최대 용량(`game_player.inventory_capacity`)을 늘린다. **확장은 1회 호출당 무조건 1칸**이며, 여는 칸별 골드 비용·최대 상한은 마스터 데이터(`inventory_expand_master`)가 정의하고 서버가 산출한다(클라이언트 입력 불신). 여는 칸의 비용은 `step = 현재 용량 − 기본 용량(100) + 1`의 `inventory_expand_master.gold_cost`이며, 상한 = 기본 용량 + 확장 정의 행 수(현재 20 → **상한 120**)다. 비용은 현재 전 칸 **정액(10,000골드)**이나, 칸별 누진으로 확장할 수 있다(값만 조정).

![인벤토리 용량 확장 화면 — 골드로 최대 슬롯 수를 늘리는 UI](../images/inventory-item-cube-인벤토리_확장.png)

**Request** — 추가 데이터 없이 인증 정보만 보낸다(1칸 확장 고정).
```json
{ "userId": 1, "token": "..." }
```

> 초기 스펙에 있던 `count`(다단계 확장) 필드는 "**1회 1칸 고정**" 결정으로 제거했다.

**Response (성공, 200 OK)**
```json
{
  "success": true,
  "errorCode": 0,
  "message": "Expanded",
  "data": {
    "inventoryCapacity": 101,
    "cost": { "currencyType": 1, "amount": 10000 },
    "balance": [ { "currencyType": 1, "amount": 9825421 } ]
  }
}
```

- 트랜잭션: 비용 골드 차감 → `inventory_capacity += 1`. 부분 실패 시 전체 롤백.
- `inventoryCapacity`는 확장 후 최종 용량, `cost`는 이번에 차감된 골드다.
- 오류: `InsufficientCurrency(4005)`(골드 부족), `InventoryCapacityMax(4008)`(이미 상한에 도달해 더 이상 확장 불가).

### 5.5 인벤토리 배치 변경(이동/교환) — `POST /api/game/inventory/move`

플레이어가 인벤토리에서 아이템을 **드래그해 다른 칸으로 옮긴** 결과를 서버에 저장한다. 목표 칸이 비어 있으면 이동, 다른 아이템이 있으면 두 칸을 **교환(swap)**한다. 배치는 UI 레이아웃 값이라 RNG·비용이 없으므로 범용 저장과 성격이 다르지만, 소유·용량 범위·칸 유효성을 서버가 검증해야 하므로 전용 액션 엔드포인트로 둔다.

**Request**
```json
{ "userId": 1, "token": "...", "data": { "itemId": 5001, "toSlot": 7 } }
```

- `itemId`: 옮길 아이템(행), `toSlot`: 이동 목표 칸(0-based, 용량 범위 내).

**Response (성공, 200 OK)**
```json
{
  "success": true,
  "errorCode": 0,
  "message": "Moved",
  "data": {
    "moved": { "itemId": 5001, "slot": 7 },
    "swapped": { "itemId": 4950, "slot": 0 }
  }
}
```

- `moved`: 옮겨진 아이템의 최종 `slot`. `swapped`: 목표 칸에 있던 아이템이 원래 칸으로 밀려난 결과(목표 칸이 비어 있었으면 `null`).
- 트랜잭션: 두 행의 `slot` 갱신을 하나의 트랜잭션으로 처리해 `(user_id, slot)` 유니크 위반이 생기지 않게 한다.
- 오류: `ItemNotFound(4001)`(대상 아이템이 인벤토리에 없음), `InvalidInventorySlot(4009)`(`toSlot`이 용량 범위 밖이거나 잘못된 값).

### 5.6 큐브 합성 — `POST /api/game/cube/combine`

같은 등급의 아이템 여러 개를 소모해 한 등급 높은 아이템을 만든다(슬롯·클래스는 서로 달라도 된다).

![큐브 합성 화면 — 같은 등급 아이템을 소모해 상위 등급을 생성](../images/inventory-item-cube-큐브합성.png)

**Request**
```json
{ "userId": 1, "token": "...", "data": { "itemIds": [4801, 4802, 4803] } }
```

**Response (성공, 200 OK)**
```json
{
  "success": true,
  "errorCode": 0,
  "message": "Combined",
  "data": {
    "consumed": [4801, 4802, 4803],
    "result": { "itemId": 5300, "itemCode": 30120, "grade": 4 },
    "cube": { "cubeLevel": 4, "cubeExp": 1350 }
  }
}
```

- **입력/결과 규칙(확정·구현)**: 입력은 **모두 장비(item_type=1)이며 같은 등급**이어야 하고 **슬롯·클래스 제한(`class_req`)은 서로 달라도 된다**, 개수는 현재 큐브 레벨의 `combine_count`(현재 5레벨 모두 3)와 일치해야 한다. 결과는 **(입력 등급+1) 장비 하나를 서버가 무작위로 선정(슬롯·클래스 무관)**해 지급한다(입력 3개 삭제로 빈 칸이 생겨 항상 적재). 등급 5 입력은 상위 등급 후보가 없어 `CubeRecipeNotMet(4010)`. 큐브 경험치 `50 × 입력 등급`을 획득해 누적한다(레벨업 시 이월). 응답 `cube`는 갱신 후 큐브 상태.
- 오류: `ItemNotFound(4001)`(입력 일부 미보유/재화 행), `ItemEquipped(4007)`(입력 중 장착 중), `CubeRecipeNotMet(4010)`(등급 불일치·개수 불일치·최대 등급).

### 5.7 큐브 분해 — `POST /api/game/cube/dismantle`

아이템을 분해해 골드로 전환한다(`cube_master.gold_per_scrap` 기준, 서버 산출).

![큐브 분해(연금술) 화면 — 아이템을 골드로 전환](../images/inventory-item-cube-큐브판매%28연금술%29.png)

**Request**
```json
{ "userId": 1, "token": "...", "data": { "items": [ { "itemId": 4700, "count": 1 } ] } }
```

**Response (성공, 200 OK)**
```json
{
  "success": true,
  "errorCode": 0,
  "message": "Dismantled",
  "data": {
    "gold": 300,
    "cubeExp": 60
  }
}
```

| 필드 | 설명 |
|---|---|
| `gold` | 이번 분해로 **획득한 골드**(서버 산출 합계) |
| `cubeExp` | 이번 분해로 **획득한 큐브 경험치**(증가분) |

- **산출 공식(확정·구현)**: 아이템당 골드 = `gold_per_scrap(현재 큐브 레벨) × 아이템 등급 × 개수`, 큐브 경험치 = `20 × 아이템 등급 × 개수`. 여러 아이템은 합산한다. `items[].count`는 장비(스택 1)는 1, 재료 스택은 보유 수량 이하. 장착 중 아이템은 `ItemEquipped(4007)`로 거부한다.
- 소모된 아이템·재화 잔액·큐브 누적 상태는 응답에 담지 않는다. 클라이언트는 획득분만 표시하고, 최신 인벤토리/큐브 스냅샷이 필요하면 `POST /api/game/load`로 재조회한다.
- 오류: `ItemNotFound(4001)`, `InsufficientQuantity(4006)`, `ItemEquipped(4007)`.

### 5.8 큐브 제작 — `POST /api/game/cube/craft`

> **상태: 구현 완료.** 레시피(`cube_recipe` + `cube_recipe_ingredient`) 기반으로 재료·골드를 소모해 지정 아이템을 만든다.

레시피에 따라 재료를 소모해 지정 아이템을 만든다.

![큐브 제작 화면 1 — 레시피 선택 및 재료 확인](../images/inventory-item-cube-큐브제작1.png)

![큐브 제작 화면 2 — 제작 결과 및 소모 재료](../images/inventory-item-cube-큐브제작2.png)

**Request**
```json
{ "userId": 1, "token": "...", "data": { "recipeCode": 8001 } }
```

**Response (성공, 200 OK)**
```json
{
  "success": true,
  "errorCode": 0,
  "message": "Crafted",
  "data": {
    "consumed": [ { "itemCode": 41001, "quantity": 5 } ],
    "gained": { "items": [ { "itemCode": 30500, "quantity": 1 } ] },
    "cube": { "cubeLevel": 5, "cubeExp": 20 }
  }
}
```

- **규칙(확정·구현)**: `recipeCode`로 `cube_recipe`(결과 아이템·수량·요구 큐브 레벨·비용 골드)와 자식 `cube_recipe_ingredient`(소모 재료·수량)를 조회한다. 현재 큐브 레벨 ≥ `req_cube_level`, 골드 ≥ `cost_gold`, 재료 보유 ≥ 요구량을 모두 만족하면 골드·재료를 차감하고 결과 아이템을 지급한다(빈 칸 부족 시 `InventoryFull`). 큐브 경험치 `20`(고정)을 획득한다. 응답 `cube`는 갱신 후 큐브 상태.
- 오류: `CubeRecipeNotMet(4010)`(없는 레시피·재료 부족), `CubeLevelInsufficient(4011)`(큐브 레벨 미달), `InsufficientCurrency(4005)`(비용 골드 부족), `InventoryFull(4002)`(결과 적재 용량 부족).

### 5.9 랜덤 상자 열기 (골드 가챠) — `POST /api/game/box/open`

플레이어가 **골드를 소모**해 원할 때 상자를 연다(가챠). 서버는 상자 정의(`box_master`)의 오픈 비용(골드)을 차감한 뒤, `grade_weights`로 등급을 추첨하고 뽑힌 등급에 속한 `item_master` 아이템 중 하나를 무작위로 선택해 **랜덤 등급의 랜덤 아이템**을 지급한다. 비용 차감·등급/아이템 추첨·지급은 하나의 트랜잭션으로 처리하며 전적으로 **서버가 산출**한다(클라이언트 입력 불신). 요청의 `count`로 오픈 횟수를 받도록 스키마를 **미리 정의**해 두었으며, **현재는 단발(`count`=1)만 처리**하고 다연속 오픈(10연차 등)은 예정 사항이다(8장).

![랜덤 상자 열기(골드 가챠) 화면 — 골드를 소모해 랜덤 등급의 아이템을 획득](../images/inventory-item-cube-랜덤가챠기능.png)

**Request**
```json
{ "userId": 1, "token": "...", "data": { "boxCode": 60001, "count": 1 } }
```

- `boxCode`: 열 상자 종류(`box_master.box_code`). 상자마다 오픈 비용·등급 확률·지급 아이템 풀이 다르다.
- `count`: (선택, 기본 1) 오픈 횟수. **다연속 기능을 위해 미리 둔 필드**이며 현재는 `1`만 처리한다(다연속은 8장 예정). 비용은 `오픈 비용 × count`를 한 번에 차감한다.

**Response (성공, 200 OK)**
```json
{
  "success": true,
  "errorCode": 0,
  "message": "BoxOpened",
  "data": {
    "boxCode": 60001,
    "rewards": [ { "grade": 4, "itemCode": 30105, "quantity": 1 } ],
    "gained": { "items": [ { "itemCode": 30105, "quantity": 1 } ] },
    "cost": { "currencyType": 1, "amount": 10000 },
    "balance": [ { "currencyType": 1, "amount": 9890421 } ]
  }
}
```

- `rewards`: 추첨된 등급·아이템 **목록**(현재 단발이라 1개, 다연속 도입 시 `count`개). `gained`: 실제 인벤토리에 적재된 결과(동일 아이템은 스택 병합). `cost`: 이번에 차감된 골드 합계, `balance`: 차감 후 잔액.
- 오류: `InsufficientCurrency(4005)`(골드 부족), `InvalidSaveData(2002)`(존재하지 않는 `boxCode`), 지급 결과가 인벤토리 용량을 초과하면 `InventoryFull(4002)`, 마스터 미로드 시 `MasterDataNotLoaded(10001)`.

> 인증 오류(401), 마스터에 없는 코드 요청 등은 기존 미들웨어·`InvalidSaveData(2002)`/마스터 도메인 코드를 따른다.

## 6. 처리 흐름

### 6.1 공통 트랜잭션 골격 (의사코드)

```
요청 수신 → 토큰 검증(미들웨어)
트랜잭션(BEGIN, user_id 잠금)
  1) 대상 조회: player_item_id / slot / 재화 잔액 로드 (행 잠금)
  2) 마스터 검증: item_master·enhance_master·cube_master 제약 확인
  3) 규칙 판정: 슬롯 정합성 / 다음 강화 단계 존재 / 합성 조건 / 수량·비용 충족
     └ 위반 시 ROLLBACK + 해당 ErrorCode 반환
  4) (RNG 연산) 큐브 합성 결과를 서버가 산출
  5) 반영: 재화 차감/적립, 인벤토리 증감(스택 병합/분할), 장착·큐브 상태 갱신
COMMIT → 변경된 상태를 응답 data로 반환
```

- 4·5단계 결과는 전부 서버가 확정한 값이며, 클라이언트는 응답으로만 인벤토리를 갱신한다.

### 6.2 장착 스왑 순서

```
equip(characterId, itemId):
  char = player_character[user_id, characterId]     # 없으면 InvalidCharacterId(2006)
  item = player_item[itemId]
  if item 없음: ItemNotFound(4001)
  if player_item_equipped[itemId] 존재: ItemEquipped(4007)   # 이미 어딘가에 장착 중
  if item.item_type != 장비 or 슬롯 부적합
     or (class_req≠0 and class_req≠char.class_code) or char.level < level_req: ItemNotEquippable(4003)
  slot = item_master[item.item_code].equip_slot
  prev = player_item_equipped[user_id, equipped_character_id=characterId, equipped_slot=slot]  # 있으면 스왑 대상
  if prev: DELETE player_item_equipped[prev.player_item_id]   # 미장착으로 복귀(장착 행 삭제)
  INSERT player_item_equipped(player_item_id=itemId, user_id, item_code=item.item_code, enhance_level=item.enhance_level, characterId, slot)  # 장착
  return { characterId, equipped: {slot, itemId}, unequipped: prev }
```

### 6.3 예외 / 엣지 케이스

- **장착 중 아이템 분해/거래 시도**: `ItemEquipped(4007)`로 거부. 해제 후 처리한다.
- **스택 초과 획득**: 지급 시 `stack_max`까지 채우고 초과분은 새 행으로 분할. 인벤토리 용량(`game_player.inventory_capacity`)을 초과하면 `InventoryFull(4002)`. 용량은 골드로 확장할 수 있다(5.4).
- **최대 강화 초과**: 다음 `enhance_level`이 `enhance_master`에 없으면 `MaxEnhanceReached(4004)`.
- **재화/재료 부족**: 비용 재화 부족은 `InsufficientCurrency(4005)`, 아이템/재료 수량 부족은 `InsufficientQuantity(4006)`. 검증은 반영 전에 수행하고 부족 시 롤백.
- **동시 중복 요청**: 같은 `player_item_id`에 대한 강화/소모/분해가 겹치면 행 잠금으로 직렬화하여 이중 소모를 방지한다.
- **큐브 조건 미충족**: 합성/제작의 등급·개수·재료·큐브 레벨 조건 위반은 `CubeRecipeNotMet(4010)`/`CubeLevelInsufficient(4011)`.

### 6.4 랜덤 상자 열기 (골드 가챠, 의사코드)

```
요청 수신 → 토큰 검증(미들웨어)
count = 요청.count ?? 1        # 현재는 1만 처리(다연속은 예정)
트랜잭션(BEGIN, user_id 잠금)
  1) box = box_master[boxCode]                       # 없으면 InvalidSaveData(2002)
  2) cost = box.open_cost × count
     if player_item(재화, item_code=골드).quantity < cost: InsufficientCurrency(4005)
  3) 골드 차감: player_item(재화, item_code=골드).quantity -= cost
  4) for _ in 1..count:                              # 현재 count=1
       grade = 가중치 추첨(box.grade_weights)         # 서버 RNG
       item  = 무작위 선택(item_master where grade == grade [, 상자 지급 풀])  # 서버 RNG
       rewards += { grade, itemCode, quantity }
  5) 지급: player_item 적재(스택/용량 규칙); 용량 초과 시 InventoryFull(4002)
COMMIT → { boxCode, rewards, gained, cost, balance }
```

- 등급·아이템 추첨은 전부 서버가 확정하며, 클라이언트는 응답으로만 결과를 반영한다.
- **오픈 트리거가 골드 소모**이므로 서버가 비용 검증·차감으로 오픈을 통제한다(별도 오픈 기회 관리 불필요). 골드 차감과 아이템 지급은 하나의 트랜잭션이며, 인벤토리 용량 초과 등 실패 시 **골드 차감까지 전체 롤백**한다.
- `count`는 계약에 미리 두었을 뿐 현재 로직은 `1`만 처리한다. 다연속(10연차) 도입 시 `count`>1 처리와 관련 정책을 확정한다(8장).

## 7. 에러 코드

`TaskbarHero.Common`의 `ErrorCode`에 추가 제안. 도메인 4.4(인벤토리/아이템/큐브)는 **4000번대**를 사용한다([통합 정의](../공통/error-code-정의.md) 블록 규약, 도메인 4.N → N000). 추가 시 통합 문서도 함께 갱신한다.

| 이름 | 값 | 의미 |
|---|---|---|
| ItemNotFound | 4001 | 대상 아이템이 인벤토리/슬롯에 없음 |
| InventoryFull | 4002 | 인벤토리 용량 초과 |
| ItemNotEquippable | 4003 | 장비가 아니거나 슬롯·클래스·레벨 부적합 |
| MaxEnhanceReached | 4004 | 최대 강화 단계 도달(다음 단계 없음) |
| InsufficientCurrency | 4005 | 비용 재화 부족(강화/제작) |
| InsufficientQuantity | 4006 | 아이템/재료 수량 부족 |
| ItemEquipped | 4007 | 장착 중이라 분해 불가 |
| InventoryCapacityMax | 4008 | 인벤토리 용량이 최대치에 도달(확장 불가) |
| InvalidInventorySlot | 4009 | 인벤토리 칸(slot) 번호가 잘못됨(용량 범위 밖 등) |
| CubeRecipeNotMet | 4010 | 큐브 합성/제작 조건(등급·개수·재료) 미충족 |
| CubeLevelInsufficient | 4011 | 큐브 레벨이 해당 연산 요구치 미만 |

- `4001~4009`는 인벤토리/아이템, `4010~4019`는 큐브에 할당한다.
- `InsufficientCurrency(4005)`는 재화 부족을 처음 다루는 도메인으로서 본 블록에 정의한다. 재화 부족이 필요한 다른 도메인(예: 성장의 룬 업그레이드)은 이 코드를 **재정의하지 않고 그대로 재사용**한다(코드 값은 계약이므로 이동 금지).
- **랜덤 상자 열기(5.9)**는 신규 에러 코드를 추가하지 않고 `InsufficientCurrency(4005)`(골드 부족)·`InvalidSaveData(2002)`(잘못된 `boxCode`)·`InventoryFull(4002)`·`MasterDataNotLoaded(10001)`를 재사용한다.

## 8. 미결 사항 / TODO

- **인벤토리 용량 정책 (확정·구현)**: 플레이어 단위 컬럼(`game_player.inventory_capacity`)에 저장하고 **골드 소모로 확장**한다(API 5.4). 용량은 **점유 slot(=`player_item` 행) 수** 기준이며, 스택은 수량과 무관하게 1 slot을 차지한다. → [세이브 데이터 기획서](save-data-기획서.md) `game_player.inventory_capacity`에 반영 완료. 세부 확정: **기본 용량 100**, 확장은 **1회당 1칸 고정**, 여는 칸별 비용·상한은 `inventory_expand_master`(step·gold_cost)로 정의(현재 20칸·칸당 10,000골드 정액 → 상한 120, 학습용 임시값). 비용 곡선은 값만 조정하면 누진 전환 가능.
- **강화 성공 확률**: 현행은 비용 지불 시 확정 상승으로 가정. 실패/하락/파괴 확률 도입 시 `enhance_master`에 확률 필드 추가 및 본 문서 5.3 갱신.
- **큐브 합성/분해/제작 상세 규칙 (확정·구현)**: 합성 소모 개수(`combine_count`)·등급 상승 규칙·분해 골드 계수와 제작 레시피(`cube_recipe`/`cube_recipe_ingredient`)는 **확정**([마스터 데이터 값](master-data/master-data-값.md) §8). 미결이던 세부도 아래로 **확정·구현**했다:
  - **합성 결과 아이템 선정**: **(입력 등급+1) 장비 중 서버 무작위 1개**(슬롯·클래스 무관, 확률 개입 없음, 균등). 입력은 같은 등급이면 되고 **슬롯·클래스는 서로 달라도 된다**. 등급 5는 상위 없음 → `CubeRecipeNotMet`.
  - **큐브 경험치 획득량**: 합성 `50 × 입력 등급`, 분해 `20 × 등급 × 개수`, 제작 `20`(고정). 학습용 임시값(스키마 불변, 값만 조정).
  - **`cube_level` 효과·성장**: `cube_exp` 누적이 `cube_master.required_exp(cube_level)` 이상이면 레벨업(초과분 이월, 최대 5). 레벨은 분해 골드 계수(`gold_per_scrap`)와 제작 요구 레벨(`req_cube_level`) 게이팅에 작용한다(합성 개수는 현재 전 레벨 3 고정).
- **장비 클래스 제한 (확정)**: 장비는 착용 가능한 **클래스 제한**을 가진다. 현재 클래스는 **기사·레인저·마법사 3종으로 확정**([마스터 데이터 기획서](master-data/master-data-기획서.md) 5.1 `class_master`)이며, **추후 확인 후 클래스를 추가할 예정**이다. 각 장비가 어느 클래스용인지는 `item_master.class_req`로 정의한다(`0`이면 전 클래스 공용, [마스터 데이터 기획서](master-data/master-data-기획서.md) 5.3에 반영 완료). 장착(5.1) 시 서버가 `class_req`(≠0)을 **대상 캐릭터 클래스**(`player_character.class_code`)와 대조해 불일치면 `ItemNotEquippable(4003)`로 거부한다.
- **다연속 오픈(10연차) — 예정**: 요청 `count`와 응답 `rewards` 배열은 **다연속 확장을 위해 계약에 미리 반영**했다(5.9). 현재 서버 로직은 `count`=1(단발)만 처리하며, 추후 10연차 등 다연속 오픈 로직을 구현할 때 `count`>1 처리(비용 `오픈 비용 × count`)와 묶음 할인·등급 보장(천장) 여부를 함께 확정한다.
- **상자 오픈 비용·등급 확률·지급 아이템 풀 (`box_master`)**: 오픈 비용(`open_cost`, 골드), `grade_weights`(등급별 추첨 가중치), 지급 대상 아이템 풀 — 전체 `item_master.grade` 필터로 할지 상자별 화이트리스트로 할지, 등급 내 아이템 선택이 균등인지 가중치인지 — 및 수량 규칙은 [마스터 데이터 기획서](master-data/master-data-기획서.md)에서 확정한다.
- **오픈 상자 종류의 노출 방식**: 어떤 상자(`box_code`)를 어디서(상점/특정 UI) 열 수 있는지, 상자별 해금 조건이 있는지.
- **장비 레벨 제한 (확정)**: 장비는 착용 요구 레벨을 가지며, **레벨 단위는 5레벨(5의 배수)** 로 확정한다(예: 15, 40). `item_master.level_req`로 정의하고(`0`이면 제한 없음, [마스터 데이터 기획서](master-data/master-data-기획서.md) 5.3에 반영 완료), 장착(5.1) 시 **대상 캐릭터의 `level`**이 `level_req` 미만이면 `ItemNotEquippable(4003)`로 거부한다. 요구 레벨별 스탯 곡선 등 밸런스 수치는 아이템/직업 기획서에서 확정.

## 9. 참고

- [서버 시스템 전체 개요](../서버-시스템-전체-개요.md) — 도메인 4.4(인벤토리/아이템), 4.6(전투 결과=아이템 획득 산출), 4.7(거래소)
- [세이브 데이터 기획서](save-data-기획서.md) — `player_item`·`player_cube` 저장 골격, 로드 스냅샷
- [마스터 데이터 기획서](master-data/master-data-기획서.md) — `item_master`·`equip_slot_master`·`enhance_master`·`cube_master`·`stage_reward`(전리품 드롭)·`box_master`(랜덤 상자)
- [오프라인 보상 정산 기획서](offline-reward-기획서.md) — 오프라인 아이템 미지급
- [ErrorCode 통합 정의](../공통/error-code-정의.md) — 에러 코드 블록 규약(4000번대 인벤토리/아이템/큐브)
