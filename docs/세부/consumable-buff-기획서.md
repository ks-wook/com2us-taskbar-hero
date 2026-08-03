# 소모품 아이템 / 계정 버프 기획서

> 상위 문서: [서버 시스템 전체 개요](../서버-시스템-전체-개요.md) · 관련 도메인 4.4(인벤토리/아이템)의 확장
>
> 본 문서는 플레이어가 **소모품 아이템(`item_type=4`)을 사용하는 규칙**을 다루는 **소모품 도메인의 정본**이며, 현재 확정 범위는 계정 단위 획득량 버프(경험치 부스터·골드 부스터)다. **추후 추가되는 소모품은 별도 문서를 만들지 않고 이 문서에 이어서 정리한다**(1장 「문서 범위와 확장 원칙」). 아이템 보관·소모의 공통 규칙은 [인벤토리/아이템/큐브 기획서](inventory-item-cube-기획서.md), 소모품·버프의 정적 정의는 [마스터 데이터 기획서](master-data/master-data-기획서.md)(`item_master`·`consumable_master`), 버프가 적용되는 획득 경로는 [스테이지/전투 결과 기획서](stage-battle-기획서.md)를 참고한다. **오프라인(방치형) 정산에는 버프 배율을 적용하지 않는다**(6.3 확정).

## 목차

- [1. 개요](#1-개요)
- [2. 기능 설명](#2-기능-설명)
- [3. 요구사항](#3-요구사항)
- [4. 데이터 모델](#4-데이터-모델)
  - [4.1 저장 위치 결정 — MySQL 정본(확정)](#41-저장-위치-결정--mysql-정본확정)
  - [4.2 `player_buff` (신규 테이블)](#42-player_buff-신규-테이블)
  - [4.3 마스터 데이터 변경](#43-마스터-데이터-변경)
  - [4.4 공유 DTO / enum](#44-공유-dto--enum)
- [5. API 명세](#5-api-명세)
  - [5.1 소모품 사용 — `POST /api/game/consumable/use`](#51-소모품-사용--post-apigameconsumableuse)
  - [5.2 활성 버프 조회 — `POST /api/game/consumable/buffs`](#52-활성-버프-조회--post-apigameconsumablebuffs)
- [6. 처리 흐름](#6-처리-흐름)
  - [6.1 소모품 사용 (의사코드)](#61-소모품-사용-의사코드)
  - [6.2 버프 적용 — 스테이지 클리어 보상](#62-버프-적용--스테이지-클리어-보상)
  - [6.3 오프라인 보상 정산 — 배율 미적용(확정)](#63-오프라인-보상-정산--배율-미적용확정)
  - [6.4 만료 버프 정리 배치](#64-만료-버프-정리-배치)
  - [6.5 예외 / 엣지 케이스](#65-예외--엣지-케이스)
- [7. 에러 코드](#7-에러-코드)
- [8. 미결 사항 / TODO](#8-미결-사항--todo)
- [9. 참고](#9-참고)


## 1. 개요

- **목적**: 인벤토리에 보관되는 **소모품 아이템**(`item_type=4`)을 도입하고, 이를 사용해 일정 시간 동안 **경험치·골드 획득량이 증가하는 계정 버프**를 부여한다. 버프 배율은 보상 지급액에 직접 작용하는 이득이므로 잔여 시간 판정과 배율 계산을 모두 **서버가 확정**한다(클라이언트 보고 불신).
- **대상 서버**: `GameServer`(소모품 소모·버프 부여·보상 배율 반영), `TaskbarHero.Common`(`ItemType`·`BuffType` enum, 결과 DTO 공유). 인증은 AccountServer 발급 토큰을 GameServer 미들웨어가 검증한다.
- **문서 범위와 확장 원칙(중요)**:
  - **현재 확정 범위는 경험치 부스터·골드 부스터 2종**이다. 스탯 증가·드롭률 증가·즉시 회복 등 다른 효과는 아직 기획에 넣지 않았다(8장).
  - **본 문서는 소모품 도메인의 정본이다.** 추후 다른 종류의 소모품을 추가할 때는 **새 기획서를 만들지 않고 이 문서에 절을 추가**해 정리한다. 소모품이 늘어도 사용 요청 창구(`POST /api/game/consumable/use`)·마스터 정의(`consumable_master`)·에러 코드 블록(`4020~4029`)이 공유되므로, 문서를 쪼개면 같은 계약이 여러 문서에 흩어진다.
  - 확장 시 갱신 지점은 다음과 같다 — 4.3 마스터 데이터(신규 `item_code`·효과 종류), 4.4 공유 enum(`BuffType` 등 신규 값 **추가만**, 기존 값 변경 금지), 5.1 사용 API(응답 필드가 늘어나는 경우), 6장 처리 흐름(지속형이 아닌 **즉시 효과형**은 `player_buff`를 쓰지 않는 별도 흐름 절을 신설), 7장 에러 코드.
  - **서버 전체 버프 이벤트(운영성 배율)는 범위 밖**이다. 도입 시에는 플레이어 버프와 **별도 마스터 테이블**로 정의하고 서버 인메모리로 들고 있는 구조를 따른다(8장 결정 기록).
  - **소모품의 *획득* 경로는 본 문서 밖**이다. 상자·메일·출석 등 기존 지급 경로가 소모품을 지급하면 그 결과가 본 문서의 인벤토리 행으로 적재된다.
  - 버프가 **어떤 값에 곱해지는지**만 본 문서가 정하고, 골드·경험치 산출 원식 자체는 [스테이지/전투 결과 기획서](stage-battle-기획서.md)를 따른다.
- **관련 기획서**: [[inventory-item-cube-기획서]] (아이템 보관·소모 공통 규칙), [[master-data-기획서]] (`item_master`·`consumable_master`), [[stage-battle-기획서]] (클리어 보상 배율 — **유일한 적용 경로**), [[offline-reward-기획서]] (오프라인 정산 — 배율 미적용), [[save-data-기획서]] (코어 로드 스냅샷)

## 2. 기능 설명

- 플레이어는 인벤토리에서 **경험치 부스터** 또는 **골드 부스터**를 사용한다. 사용하면 아이템이 1개 소모되고, 그 즉시부터 마스터 데이터가 정의한 **지속시간 동안** 해당 획득량에 배율이 적용된다.
- 버프는 **계정 단위**다. 인벤토리·골드가 계정 공유인 것과 같은 범위이며, 경험치 버프도 특정 캐릭터가 아니라 **그 계정이 획득하는 모든 경험치**(파티 편성 캐릭터 전원)에 적용된다.
- **버프 시간은 벽시계(wall-clock)로 흐른다(확정).** 접속을 끊어도 시간이 계속 소모되며, 접속 중에만 흐르는 방식(정지·재개)은 채택하지 않는다.
- **버프는 스테이지 클리어 보상에만 적용된다(확정).** 오프라인(방치형) 정산 보상에는 배율을 붙이지 않는다(6.3). 부스터는 **접속해서 직접 스테이지를 도는 플레이**를 보상하는 소모품이며, 오프라인 보상은 이미 효율 50%·12시간 상한으로 조정된 소급 지급이므로 그 위에 배율을 얹지 않는다.
  - 따라서 **버프를 켜둔 채 로그아웃하면 그 시간은 그냥 소모된다**(버프 시간은 벽시계로 흐른다). 플레이어는 접속해 스테이지를 도는 동안 사용해야 이득을 얻는다 — 의도된 사용 압력이다.
- 서로 다른 종류의 버프(경험치·골드)는 **동시에 활성**될 수 있다. 같은 종류를 다시 사용하면 남은 시간에 **누적 연장**된다(4.2).

## 3. 요구사항

**기능 요구사항**
- 소모품 아이템 타입(`item_type=4`)을 도입하고, 소모품별 버프 효과(종류·배율·지속시간)를 마스터 데이터로 정의한다.
- 소모품 사용 요청을 받아 **아이템 1개 차감 + 버프 부여**를 하나의 트랜잭션으로 처리하고, 부여 결과와 계정의 활성 버프 전체를 응답한다.
- 활성 버프는 재접속 시 **코어 로드 응답에 포함**해 클라이언트가 잔여 시간을 UI에 표시할 수 있게 하고, 이후 버프 UI 재동기화를 위한 **전용 경량 조회 API**를 함께 제공한다(5.2).
- **스테이지 클리어 보상(골드·경험치)에만** 활성 버프 배율을 반영한다. 오프라인 정산 보상에는 반영하지 않는다(6.3).
- 만료된 버프 행은 주기 배치로 정리한다(6.4).

**비기능 요구사항**
- **서버 권위**: 버프 활성 여부·잔여 시간·배율은 모두 **서버 시각과 마스터 데이터**로 판정한다. 클라이언트가 보낸 배율·잔여 시간은 사용하지 않는다.
- **원자성**: "아이템 차감 + 버프 부여"는 `user_id` 단위 하나의 트랜잭션이다. 중도 실패 시 전체 롤백해 **아이템만 사라지거나 버프만 생기는 상태**를 만들지 않는다.
- **영속성**: 소모품은 인벤토리에서 이미 차감된 대가이므로, 그 결과인 버프 상태는 **유실되면 복구할 수 없다.** 휘발성 저장소에 단독으로 두지 않는다(4.1).
- **동시성/멱등성**: 같은 계정의 중복 사용 요청은 `player_buff` 행 잠금(PK `(user_id, buff_type)`)으로 직렬화되어 아이템 이중 차감·버프 이중 연장이 발생하지 않는다. 별도 분산 락은 두지 않는다 — 전역 공유 자원을 다투는 구조가 아니라 자기 행만 갱신하기 때문이다.

## 4. 데이터 모델

### 4.1 저장 위치 결정 — MySQL 정본(확정)

버프 상태는 **MySQL을 정본으로 저장한다.** Redis TTL 단독 저장은 채택하지 않는다. 근거:

| 판단 근거 | 내용 |
|---|---|
| **유실 시 복구 불가** | 버프는 인벤토리에서 아이템을 차감한 결과다. 저장소 재시작으로 사라지면 플레이어 손실이 되고, 사후 검증 근거도 남지 않는다. 버프 시간이 벽시계로 흐르므로(2장) 유실분을 재계산할 방법도 없다. |
| **정본-캐시 원칙 준수** | 본 프로젝트는 "캐시는 파생 데이터이며 정합성 정본은 항상 MySQL, Redis 실패 시 MySQL 폴백"을 원칙으로 한다(`GameServer/Services/TradeCache.cs`, [거래소 기획서](trade-기획서.md) 7.5). 버프를 Redis 단독으로 두면 폴백 대상이 없다. |
| **트랜잭션 경계 일치** | 보상 지급은 이미 `user_id` 단위 MySQL 트랜잭션에서 일어난다. 버프가 같은 DB에 있으면 배율 판정이 그 트랜잭션에 포함되어 별도 락 없이 정합성이 확보된다(6.2). |

- **Redis는 사용하지 않는다(확정).** 버프 조회는 항상 `user_id` 단건이고 전역 공유 읽기가 아니어서 거래소 목록과 같은 캐시 이점이 없다. 읽기 부하가 실제로 문제가 되면 그때 캐시를 얹되, 그 경우에도 캐시 값은 TTL이 아니라 `expires_at`이어야 한다.
- **만료 행 정리는 TTL이 아니라 배치가 담당한다.** 모든 조회·배율 판정이 `expires_at > now`로 필터하므로 정리가 늦어도 정확성에 영향이 없다(6.4).

### 4.2 `player_buff` (신규 테이블)

계정의 활성 버프 상태. [세이브 데이터 기획서](save-data-기획서.md) 3장 ERD에 신규 테이블로 추가한다.

| 필드 | 타입 | 설명 |
|---|---|---|
| `user_id` | bigint PK | 계정 식별자(FK `game_player.user_id`) |
| `buff_type` | int PK | 버프 종류(1:경험치 획득량 2:골드 획득량). `consumable_master.buff_type`과 동일 enum |
| `buff_value` | decimal(5,3) | 적용 배율(`1.500` = 획득량 150%). 사용한 소모품의 `consumable_master.buff_value` |
| `started_at` | bigint | 버프 시작 Unix ts(초). 클라이언트 버프 UI 표시(총 지속시간 대비 진행률)와 사후 검증 근거 |
| `expires_at` | bigint | 버프 만료 Unix ts(초). **활성 판정·배율 적용의 기준** |

- **PK `(user_id, buff_type)`** — 계정당 버프 종류별 1행. 이 PK가 행 잠금 단위이자 중복 사용 직렬화 장치다. 종류가 다른 버프(경험치·골드)는 서로 다른 행이라 **동시 활성**된다.
- **JSON 컬럼을 쓰지 않는다** — 버프는 반복 구조이므로 `game_player`에 JSON 컬럼으로 넣지 않고 별도 자식 테이블로 둔다(프로젝트 스키마 규칙).
- **활성 판정**: `expires_at > now`인 행만 활성이다. 만료 행은 남아 있어도 모든 조회·배율 판정에서 걸러지므로 무해하다(정리는 6.4).
- **인덱스**: PK만으로 충분하다(모든 조회가 `user_id` 단건). 정리 배치용으로 `expires_at` 보조 인덱스를 둔다.

**중첩(재사용) 규칙 — 기준안**

같은 `buff_type`의 버프를 다시 사용하면 **남은 시간에 누적 연장**한다.

```
newExpiresAt = max(now, 기존 expires_at) + consumable_master.duration_sec
```

- 기존 버프가 만료 전이면 잔여 시간에 더해지고(연장), 이미 만료됐으면 `now` 기준으로 새로 시작한다.
- `started_at`은 **기존 버프가 활성이면 유지**하고, 만료 상태에서 새로 시작하면 `now`로 갱신한다. 유지하는 이유는 연장이 "같은 버프가 계속 켜져 있는 상태"이므로 시작 시각이 뒤로 밀리면 UI 진행률과 사후 검증 근거가 흔들리기 때문이다.
- **누적 상한 24시간**: `newExpiresAt - now > 86400`이면 `BuffDurationLimitExceeded(4021)`로 거부하고 아이템을 차감하지 않는다(무한 축적 방지).
- `buff_value`는 사용한 소모품 값으로 **덮어쓴다**. 현재 버프 종류당 소모품이 1종이라 값이 항상 같으므로 충돌이 없다. 배율이 다른 상·하위 부스터를 추가하면 이 규칙을 재정의해야 한다(8장).

### 4.3 마스터 데이터 변경

**(1) `item_master.item_type`에 소모품(4) 추가**

기존 `1:장비 2:재료 3:재화`에 **`4:소모품`**을 추가한다. enum 신규 값 추가이므로 기존 계약을 깨지 않는다.

- 소모품 아이템 코드는 **`42xxx`** 대역을 쓴다(재료 `41xxx`에 이어지는 비장비 대역).
- 소모품은 장착·스탯 개념이 없어 `equip_slot`/`class_req`/`level_req`는 0, 스탯 컬럼(`hp`~`cooldown`)도 0이다. `grade`는 FK(`grade_master`) 제약을 만족시키기 위한 값이며 소모품 로직에는 쓰지 않는다.
- 스택 보관 대상이므로 `stack_max > 1`이다(재료와 동일 규칙, [인벤토리/아이템/큐브 기획서](inventory-item-cube-기획서.md) 4장 "비장비" 규칙에 소모품을 포함시킨다).

| item_code | name | item_type | grade | stack_max | sellable | base_price |
|---|---|---|---|---|---|---|
| 42001 | 경험치 부스터 | 4 | 1 | 99 | 0 | 0 |
| 42002 | 골드 부스터 | 4 | 1 | 99 | 0 | 0 |

> `sellable=0`(거래소 등록 불가)은 **기준안**이다. 버프 아이템이 거래 가능해지면 경제 영향이 커지므로 초기에는 거래를 막는다(8장).

**(2) `consumable_master` (신규 마스터 테이블)**

소모품 아이템이 부여하는 버프 효과 정의. `item_master`의 소모품 행과 1:1로 대응한다.

| 필드 | 타입 | 설명 |
|---|---|---|
| `item_code` | int PK | 소모품 아이템 코드(FK `item_master.item_code`, `item_type=4`) |
| `buff_type` | int | 버프 종류(1:경험치 획득량 2:골드 획득량) |
| `buff_value` | decimal(5,3) | 획득량 배율(`1.500` = 150%) |
| `duration_sec` | int | 지속시간(초) |

**담기는 데이터 (학습용 임시값)**

| item_code | name | buff_type | buff_value | duration_sec |
|---|---|---|---|---|
| 42001 | 경험치 부스터 | 1 (경험치 획득량) | 1.500 | 1800 (30분) |
| 42002 | 골드 부스터 | 2 (골드 획득량) | 1.500 | 1800 (30분) |

- 배율·지속시간은 스키마를 바꾸지 않고 **값만 조정**할 수 있다. 밸런스 확정은 8장.
- 소모품 효과를 `item_master`의 컬럼으로 넣지 않고 별도 테이블로 분리한 이유는, 장비 스탯 컬럼과 성격이 달라 대다수 아이템 행에 의미 없는 컬럼이 깔리기 때문이다(`enhance_master`·`cube_master`와 같은 분리 방식).

**(3) 마스터 로더 적재 기준 분리 (구현 시 필수)**

소모품을 `item_master`에 통합하면 **아이템 정의 조회**와 **등급 추첨 후보 풀**의 적재 기준이 갈라진다. 현재 `GameServer/MasterData/MasterDataProvider.cs`의 `LoadItemsAsync`는 하나의 쿼리(`item_type IN (1,2)`)로 두 사전(`_itemsByCode`·`_itemsByGrade`)을 동시에 만들므로, 그대로 두거나 단순히 `4`를 더하기만 하면 어느 쪽이든 결함이 된다.

| 적재 대상 | 용도 | 기준 | 상태 |
|---|---|---|---|
| `_itemsByCode` | 아이템 **정의 조회**(타입·`stack_max`·`sellable` 등) | **`item_type` 1·2·4** — 소모품이 없으면 사용 API가 `item_type=4` 확인·스택 적재를 못 한다 | 구현 완료 |
| `_itemsByGrade` | **스테이지 전리품** 드롭 후보 풀(`RollDrop`) | **`item_type` 1·2만** — 소모품은 스테이지 드롭으로 지급하지 않는다(아래 확정) | 구현 완료 |
| `_consumablesByCode` | 소모품 **버프 효과 조회**(`GetConsumable`) | `consumable_master` 전량 | 구현 완료 |
| 가챠 후보 풀 | 가챠 지급 후보 | **`gacha_item_pool` 마스터에서 별도 적재** — `item_master.grade`나 위 `_itemsByGrade`를 쓰지 않는다 | 가챠 미구현(예정) |

- **소모품 확률 지급 범위(확정)**: **가챠에는 포함**하고 **스테이지 전리품 드롭에는 포함하지 않는다.** 두 경로가 같은 사전을 공유하면 이 구분이 불가능하므로, 가챠 후보는 전용 마스터(`gacha_item_pool`)로 분리해 정의한다([마스터 데이터 기획서](master-data/master-data-기획서.md) 5.13 · [가챠 기획서](gacha-기획서.md) 4.1).
- **소모품의 `grade`는 그대로 둔다(확정).** `grade`는 `grade_master` FK 충족용 값이라 희귀도 의미가 없다(4.3-(1)). 가챠 출현 빈도는 아이템 등급을 고치는 대신 `gacha_item_pool`의 **등급 슬롯 배치**로 조절한다 — 그 테이블의 `grade`는 가챠 안에서의 추첨 슬롯이며 `item_master.grade`와 일치할 필요가 없다.
- 재화(`item_type=3`, 골드)는 모든 지급 후보 풀에서 제외한다(현행과 동일).

### 4.4 공유 DTO / enum

`TaskbarHero.Common`에 정의해 서버-클라이언트가 공유한다. 다른 게임 DTO와 동일한 규약(`[Serializable]` + public camelCase 필드)을 따른다.

```csharp
namespace TaskbarHero.Common
{
    // 획득량 버프 종류. 숫자 값은 클라이언트와의 계약이므로 변경 금지(신규 값 추가는 허용).
    public enum BuffType
    {
        ExpGain = 1,   // 경험치 획득량
        GoldGain = 2,  // 골드 획득량
    }
}
```

```csharp
namespace TaskbarHero.Common.Dto
{
    // 활성 버프 1건. 코어 로드(activeBuffs)·소모품 사용 응답·활성 버프 조회 응답이 공유한다.
    [Serializable]
    public class ActiveBuff
    {
        public int   buffType;   // BuffType (1:경험치 2:골드)
        public float buffValue;  // 획득량 배율(1.5 = 150%)
        public long  startedAt;  // 시작 Unix ts(초)
        public long  expiresAt;  // 만료 Unix ts(초)
    }

    // 소모품 사용 결과 (POST /api/game/consumable/use 성공 응답 data)
    [Serializable]
    public class ConsumableUseResult
    {
        public long itemId;            // 사용한 인벤토리 행(player_item_id)
        public int  itemCode;          // 사용한 소모품 코드
        public long remainingQuantity; // 차감 후 남은 수량(0이면 행 삭제됨)
        public ActiveBuff buff = new ActiveBuff();                       // 이번 사용으로 갱신된 버프
        public List<ActiveBuff> activeBuffs = new List<ActiveBuff>();    // 갱신 후 계정의 활성 버프 전체
    }

    // 활성 버프 조회 결과 (POST /api/game/consumable/buffs 성공 응답 data)
    [Serializable]
    public class ActiveBuffListResult
    {
        public long serverTime;   // 조회 시점 서버 Unix ts(초). 잔여 시간 계산 기준점
        public List<ActiveBuff> activeBuffs = new List<ActiveBuff>();
    }
}
```

- `buff_value`는 DB에서 `DECIMAL(5,3)`이므로 리포지토리 POCO에서 `decimal`로 받아 DTO의 `float`로 캐스팅한다(프로젝트 DB 매핑 규칙).
- `item_type`은 기존 공유 enum에 `Consumable = 4`를 추가한다([마스터 데이터 기획서](master-data/master-data-기획서.md) 5장 공통 규칙).

## 5. API 명세

**API 목록**

- [5.1 소모품 사용 — `POST /api/game/consumable/use`](#51-소모품-사용--post-apigameconsumableuse)
- [5.2 활성 버프 조회 — `POST /api/game/consumable/buffs`](#52-활성-버프-조회--post-apigameconsumablebuffs)

Base URL(개발): `http://localhost:5247` (GameServer). 인증 요청 공통 형식 `{ userId, token, data }`, 응답 `{ success, errorCode, message, data }`([세이브 데이터 기획서](save-data-기획서.md) 5장과 동일 규약, `success`는 `errorCode == 0`과 동치).

### 5.1 소모품 사용 — `POST /api/game/consumable/use`

인벤토리의 소모품 1개를 소모해 버프를 부여(또는 연장)한다. 배율·지속시간은 전적으로 마스터 데이터에서 서버가 읽는다(클라이언트 입력 없음).

**Request**
```json
{ "userId": 1, "token": "...", "data": { "itemId": 7001 } }
```

- `itemId`: 사용할 소모품이 담긴 인벤토리 행(`player_item.player_item_id`).
- **1회 호출당 1개 고정**이다. 수량 지정(`count`) 필드는 두지 않는다 — 인벤토리 용량 확장([인벤토리/아이템/큐브 기획서](inventory-item-cube-기획서.md#54-인벤토리-용량-확장--post-apigameinventoryexpand) 5.4)과 동일한 방침이며, 여러 개를 쓰려면 여러 번 호출한다.

**Response (성공, 200 OK)**
```json
{
  "success": true,
  "errorCode": 0,
  "message": "Consumable used",
  "data": {
    "itemId": 7001,
    "itemCode": 42001,
    "remainingQuantity": 4,
    "inventoryDelta": {
      "upserted": [ { "itemId": 7001, "slot": 9, "itemCode": 42001, "quantity": 4, "enhanceLevel": 0 } ],
      "removed": []
    },
    "buff": { "buffType": 1, "buffValue": 1.5, "startedAt": 1752350000, "expiresAt": 1752351800 },
    "activeBuffs": [
      { "buffType": 1, "buffValue": 1.5, "startedAt": 1752350000, "expiresAt": 1752351800 },
      { "buffType": 2, "buffValue": 1.5, "startedAt": 1752349000, "expiresAt": 1752350800 }
    ]
  }
}
```

| 필드 | 설명 |
|---|---|
| `itemId` / `itemCode` | 사용한 인벤토리 행과 소모품 코드 |
| `remainingQuantity` | 1 차감 후 남은 수량. `0`이면 해당 행이 삭제되어 가방 칸이 비었음을 뜻한다 |
| `inventoryDelta` | 가방 변경분([인벤토리/아이템/큐브 기획서](inventory-item-cube-기획서.md) 5.0 공통 규약). 수량이 남으면 그 행이 `upserted`에, 0이면 `removed`에 담긴다 |
| `buff` | 이번 사용으로 부여·연장된 버프의 최종 상태(연장이면 `expiresAt`만 늘어나고 `startedAt`은 유지) |
| `activeBuffs` | 갱신 후 계정의 **활성 버프 전체**(`expires_at > now`). 클라이언트가 이 배열만으로 버프 UI를 다시 그릴 수 있다 |

- **클라이언트는 이 응답만으로 가방과 버프 UI를 갱신한다 — 액션 뒤에 `/api/game/load`나 `/api/game/inventory/list`를 재조회하지 않는다**([인벤토리/아이템/큐브 기획서](inventory-item-cube-기획서.md) 5.0).
- 오류: `ItemNotFound(4001)`(인벤토리에 없거나 본인 아이템이 아님), `ItemNotConsumable(4020)`(`item_type≠4`), `InsufficientQuantity(4006)`(수량 0), `BuffDurationLimitExceeded(4021)`(누적 24시간 초과), `MasterDataNotLoaded(10001)`(`consumable_master` 미로드), `SaveNotFound(2001)`(세이브 없음).

### 5.2 활성 버프 조회 — `POST /api/game/consumable/buffs`

클라이언트는 현재 적용 중인 버프를 **상시 UI(버프 아이콘 + 잔여 시간)** 로 보여주므로, 활성 버프를 받는 창구는 **세 곳**이다.

| 창구 | 시점 | 담기는 곳 |
| --- | --- | --- |
| `POST /api/game/load` | 접속 직후 1회 | `activeBuffs` (코어 스냅샷) |
| `POST /api/game/consumable/use` | 소모품 사용 직후 | `activeBuffs` (갱신 후 전체) |
| `POST /api/game/consumable/buffs` | 그 이후 재동기화 | 응답 `data` 전체 |

**전용 조회 엔드포인트를 둔다.** 활성 버프는 계정당 최대 버프 종류 수(현재 2행)로 크기가 고정이라 코어 로드에도 포함하지만([세이브 데이터 기획서](save-data-기획서.md) 2장의 고정 크기 전량 반환 정책), 코어 로드는 캐릭터·재화·장비·스킬·룬을 전량 싣고 `offlineElapsedSec`(오프라인 정산 입력값)까지 함께 내려주는 **접속 시점 전용 무거운 호출**이다. 버프 2행을 다시 확인하려고 재호출할 대상이 아니므로, 재동기화용 **경량 조회**를 별도로 둔다.

**Request** — payload가 없어 공용 `AuthRequest`를 그대로 쓴다.
```json
{ "userId": 1, "token": "..." }
```

**Response (성공, 200 OK)**
```json
{
  "success": true, "errorCode": 0, "message": "Active buffs loaded",
  "data": {
    "serverTime": 1752351000,
    "activeBuffs": [
      { "buffType": 1, "buffValue": 1.5, "startedAt": 1752350000, "expiresAt": 1752351800 }
    ]
  }
}
```

- `expires_at > now`인 행만 담고 `buff_type` 오름차순으로 정렬한다. 활성 버프가 없으면 **빈 배열 + 성공**이다(오류가 아니다).
- **`serverTime`은 잔여 시간 계산의 기준점이다.** 클라이언트는 `expiresAt - serverTime`으로 남은 초를 얻고 그 값을 로컬에서 카운트다운하며, 로컬 시계 자체로 만료를 판정하지 않는다. 만료는 항상 서버가 확정하고, 클라이언트는 다음 응답에서 해당 항목이 사라지는 것으로 확인한다.
- 마스터 데이터를 참조하지 않는다 — 배율·지속시간은 버프 부여 시점에 이미 확정돼 `player_buff`에 저장돼 있으므로, 마스터 미로드 상태(`4001`)에서도 조회는 정상 동작한다.
- **호출 시점**: 버프 UI를 여는 순간, 앱이 백그라운드에서 복귀한 순간, 카운트다운이 0에 닿은 직후 등 재동기화가 필요한 때만 호출한다. **주기적 폴링은 하지 않는다** — 버프 상태는 소모품 사용 외에 서버 단독으로 바뀌지 않는다(버프 획득 경로가 늘어나면 이 항목을 갱신한다).
- 재접속 흐름상 **오프라인 정산(`/api/game/offline/claim`)보다 로드가 먼저 호출**되지만([오프라인 보상 정산 기획서](offline-reward-기획서.md) 6.2), 정산은 버프를 읽지도 바꾸지도 않으므로(6.3) 이 순서가 `activeBuffs`에 영향을 주지 않는다.
- 신규 에러 코드는 없다. 인증 실패 계열 외의 분기가 없으며, 조회 결과가 비어도 성공으로 응답한다.

## 6. 처리 흐름

### 6.1 소모품 사용 (의사코드)

```
요청 수신 → 토큰 검증(미들웨어)
BUFF_DURATION_CAP_SEC = 86400          # 누적 상한 24시간

트랜잭션(BEGIN, user_id 잠금)
  1) item = player_item[itemId] (행 잠금)
     if 없음 or item.user_id != userId: ItemNotFound(4001)
     if item.quantity < 1:              InsufficientQuantity(4006)
  2) master = item_master[item.item_code]
     if master.item_type != 4(소모품):   ItemNotConsumable(4020)
     cm = consumable_master[item.item_code]
     if 없음:                           MasterDataNotLoaded(10001)
  3) prev = player_buff[userId, cm.buff_type] (행 잠금)
     base       = max(now, prev?.expires_at ?? 0)      # 활성이면 잔여에 누적, 만료면 now부터
     newExpires = base + cm.duration_sec
     newStarted = (prev != null and prev.expires_at > now) ? prev.started_at : now
     if newExpires - now > BUFF_DURATION_CAP_SEC: BuffDurationLimitExceeded(4021)   # 아이템 미차감
  4) 아이템 차감: quantity -= 1
     if quantity == 0: DELETE player_item[itemId]      # 가방 칸 반납
  5) UPSERT player_buff(userId, cm.buff_type, cm.buff_value, newStarted, newExpires)
COMMIT → { itemId, itemCode, remainingQuantity, buff, activeBuffs }
```

- 3단계의 상한 검사를 **차감 전에** 둔다. 차감 후 검사하면 거부된 요청에서 아이템만 사라진다.
- 5단계는 `INSERT ... ON DUPLICATE KEY UPDATE`로 처리한다. PK `(user_id, buff_type)`가 중복 요청을 직렬화하므로 별도 락이 불필요하다(3장).
- 소모품은 스택 아이템이므로 수량이 0이 된 행은 삭제해 가방 칸을 반납한다(비장비 스택 규칙, [인벤토리/아이템/큐브 기획서](inventory-item-cube-기획서.md) 4장).

### 6.2 버프 적용 — 스테이지 클리어 보상

**버프가 적용되는 유일한 경로다.** 클리어는 **시점 이벤트**이므로 구간 계산이 없다. 지급 트랜잭션에서 활성 버프를 읽어 배율만 곱한다([스테이지/전투 결과 기획서](stage-battle-기획서.md) 5.2·6장 지급 단계).

```
gold = floor(stage_reward.reward_gold × multiplier(GoldGain))
exp  = floor(stage_reward.reward_exp  × multiplier(ExpGain))

multiplier(type):
    b = player_buff[userId, type]
    return (b != null and b.expires_at > now) ? b.buff_value : 1.0
```

- 버프 조회는 보상 지급과 **같은 트랜잭션**에서 수행한다. 배율 판정과 지급이 갈라지지 않는다.
- 경험치는 기존 규칙대로 **파티 편성 캐릭터 전원에게 동일 값**으로 지급하며, 배율은 그 값에 한 번 적용된다(캐릭터 수만큼 곱하지 않는다).
- 버프가 클리어 처리 **중간에** 만료되는 경우는 트랜잭션 시작 시각(`now`) 기준 단일 판정으로 처리한다(초 단위 경계는 플레이어에게 유리하게 반올림하지 않는다).

### 6.3 오프라인 보상 정산 — 배율 미적용(확정)

**오프라인(방치형) 정산 보상에는 버프 배율을 적용하지 않는다.** 정산식은 [오프라인 보상 정산 기획서](offline-reward-기획서.md) 6.1을 그대로 따르며 `player_buff`를 **읽지 않는다.**

```
gold = floor(effectiveSec × goldPerSec × OFFLINE_EFFICIENCY)   # 버프 배율 없음
exp  = floor(effectiveSec × expPerSec  × OFFLINE_EFFICIENCY)   # 버프 배율 없음
```

근거:

- **부스터의 목적이 능동 플레이 보상**이다. 오프라인 보상은 접속하지 않은 시간에 대한 소급 지급이며 이미 효율 50%·12시간 상한으로 조정돼 있다. 그 위에 배율을 얹으면 "버프를 켜고 로그아웃"이 최적 플레이가 되어 소모품이 방치를 강화하는 방향으로 작동한다.
- **소급 구간 계산이 사라져 규칙이 단순해진다.** 배율 판정이 "정산 구간 ∩ 버프 구간"이 아니라 **클리어 시점의 활성 여부** 하나로 통일되고, 만료 행을 보존해야 할 정확성 요건도 없어진다(6.4).
- 결과적으로 **버프를 켠 채 오프라인으로 보낸 시간은 손실**이다(2장). 이는 배율을 스테이지 클리어로 한정한 결과이며 의도된 동작이다.

### 6.4 만료 버프 정리 배치

만료 행은 조회 필터(`expires_at > now`)로 이미 걸러지므로 정리는 **저장 공간 회수 목적**이다. 기존 메일 GC(`GameServer/Batch/MailGcBatchService.cs`)와 동일한 `PeriodicBatchService` 파생 배치로 둔다.

```
DELETE FROM player_buff
 WHERE expires_at < now - BUFF_GC_MARGIN_SEC
```

- **정확성 요건이 없다.** 오프라인 소급 참조를 하지 않으므로(6.3) 만료 행을 언제 지워도 지급 결과가 달라지지 않는다. 만료 행이 지워진 뒤 같은 종류를 다시 사용하면 4.2의 연장 규칙이 `now` 기준 신규 부여로 동작하는데, 이는 만료 행이 남아 있을 때와 결과가 같다.
- `BUFF_GC_MARGIN_SEC`는 운영 조사·사후 검증용 여유분이다(기준안 1시간).
- 배치 주기는 하루 1회로 충분하다(행 수가 계정당 최대 버프 종류 수라 압박이 없다).

### 6.5 예외 / 엣지 케이스

- **소모품이 아닌 아이템 사용 시도**: `ItemNotConsumable(4020)`으로 거부한다. 장비·재료·재화 행 모두 해당한다.
- **누적 상한 초과**: 남은 시간 + 신규 지속시간이 24시간을 넘으면 `BuffDurationLimitExceeded(4021)`로 거부하고 **아이템을 차감하지 않는다.** 플레이어는 버프를 소모한 뒤 다시 사용한다.
- **동시 중복 사용 요청**: `player_buff` PK 행 잠금과 `player_item` 행 잠금으로 직렬화된다. 두 요청이 모두 성공하면 아이템 2개가 차감되고 지속시간이 2배 연장된 상태가 되며(정상), 재전송으로 인한 이중 차감은 트랜잭션 순서에 따라 하나씩 순차 반영된다.
- **버프 중 로그아웃 → 재접속**: 버프 시간은 벽시계로 흐르므로 오프라인 동안 계속 소모되고, 그 시간에 대한 보상 배율은 없다(6.3). 남은 시간이 있으면 재접속 후 스테이지 클리어에서 그 잔여분만 이득이 된다.
- **버프 적용 대상이 아닌 획득 경로**: 아래는 **배율을 적용하지 않는다(확정)**. 오프라인 정산은 능동 플레이가 아니고(6.3), 나머지는 확률·정액 보상이거나 플레이어 간 이전이라 배율이 경제를 왜곡한다.
  - **오프라인(방치형) 정산 보상**, 메일 첨부 수령(거래 대금·운영 지급), 출석부 보상, 큐브 분해 골드, 가챠 결과, 거래소 판매 대금, 신규 가입 지원금.
- **버프 종류가 늘어난 경우**: `activeBuffs`는 배열이므로 계약 변경 없이 행이 추가된다. 클라이언트는 알 수 없는 `buffType`을 무시하도록 구현한다.

## 7. 에러 코드

소모품/버프는 아이템 도메인의 확장이므로 **4000번대 블록 안에서 `4020~4029`를 소모품/버프에 할당**한다(기존 할당: `4001~4009` 인벤토리/아이템, `4010~4019` 큐브). 새 1000번 블록을 열지 않는다. 추가 시 [통합 정의](../공통/error-code-정의.md)도 함께 갱신한다.

| 이름 | 값 | 의미 |
|---|---|---|
| ItemNotConsumable | 4020 | 소모품이 아닌 아이템에 사용을 시도(`item_type≠4`) |
| BuffDurationLimitExceeded | 4021 | 버프 누적 지속시간이 상한(24시간)을 초과 |

**재사용하는 기존 코드** — 새 코드를 만들지 않는다.

| 코드 | 사용 상황 |
|---|---|
| `ItemNotFound(4001)` | 사용 대상 아이템이 인벤토리에 없거나 본인 아이템이 아님 |
| `InsufficientQuantity(4006)` | 소모품 수량 부족(0개) |
| `MasterDataNotLoaded(10001)` | `consumable_master`에 해당 소모품 정의가 없음/미로드 |
| `SaveNotFound(2001)` | 세이브 데이터 없음 |

## 8. 미결 사항 / TODO

- **버프 배율·지속시간 밸런스**: 현재 `1.500` 배율 / `1800`초(30분)는 **학습용 임시값**이다. 스키마를 바꾸지 않고 `consumable_master` 값만 조정해 확정한다. → [마스터 데이터 값](master-data/master-data-값.md) §15.
- **누적 상한(24시간)·GC 여유(1시간)**: 기준안이다. 상한을 바꿀 경우 5.1 에러 응답 조건과 6.1 의사코드의 상수를 함께 갱신한다.
- **중첩(재사용) 규칙**: "누적 연장 + `buff_value` 덮어쓰기"는 버프 종류당 소모품이 1종이라는 현재 전제에서 성립한다. **배율이 다른 상·하위 부스터**(예: 1.5배 / 2.0배)를 추가하면 정책을 재정의해야 한다 — 높은 배율 우선 / 별도 행 허용 / 낮은 배율 사용 거부 중 선택.
- **소모품 거래 가능 여부**: 현재 `sellable=0`(거래소 등록 불가)이 기준안이다. 버프 아이템의 거래 허용은 골드 경제에 직접 영향을 주므로 별도 판단이 필요하다.
- **소모품 획득 경로**: **가챠(뽑기)로 지급한다(확정)** — 후보는 `gacha_item_pool`에 42xxx 행으로 등록한다(4.3-(3), [가챠 기획서](gacha-기획서.md) 4.1). **스테이지 전리품 드롭으로는 지급하지 않는다(확정).** 그 외 경로(출석 보상·상점 판매 등)는 미정이며, 지급 경로가 `item_code` 42xxx를 명시적으로 지정하면 그대로 인벤토리에 적재된다. 가챠별 어느 등급 슬롯에 몇 개를 넣을지는 `gacha_master` 값 확정과 함께 정한다([마스터 데이터 값](master-data/master-data-값.md) §12, 현재 미작성).
- **소모품 종류 확장**: 스탯 증가·드롭률 증가·즉시 회복 등 다른 효과의 소모품은 아직 범위 밖이다. 도입 시 `consumable_master.buff_type`에 값을 추가하고(기존 값 변경 금지), 즉시 효과형(지속시간 0)은 `player_buff`를 쓰지 않는 별도 흐름이 필요하다. **추가 작업은 새 기획서가 아니라 본 문서에 절을 이어 붙이는 방식으로 한다**(1장 「문서 범위와 확장 원칙」의 갱신 지점 참고).
- **서버 전체 버프 이벤트 (범위 밖 · 방향 결정)**: 본 기획에는 넣지 않는다. 도입 시에는 플레이어 버프(`player_buff`)와 **분리**해 `global_buff_event`(`event_code`, `buff_type`, `starts_at`, `ends_at`) 형태의 **마스터/운영 테이블**로 정의하고, 행 수가 적으므로 **서버 인메모리로 들고** 주기 갱신한다. 계정마다 `player_buff` 행으로 뿌리지 않는다(계정 수만큼 쓰기 + 신규 가입자 누락). 이 구조를 택하는 이유는 `starts_at`을 미래 시각으로 **사전 등록**할 수 있어야 하기 때문이다. 개인 버프와의 합산 방식(곱연산/합연산)은 도입 시 확정한다.
- **클라이언트 잔여 시간 표시**: 서버-클라 시각 오차 보정 방식(로드 응답에 서버 시각을 함께 내릴지) 미정. 현재는 `expiresAt`(절대 시각)만 내려주고 클라이언트가 자체 시계로 카운트다운한다.

## 9. 참고

- [서버 시스템 전체 개요](../서버-시스템-전체-개요.md) — 도메인 4.4(인벤토리/아이템)
- [인벤토리/아이템/큐브 기획서](inventory-item-cube-기획서.md) — 아이템 보관·스택·소모 공통 규칙, 4000번대 에러 코드 블록
- [마스터 데이터 기획서](master-data/master-data-기획서.md) — `item_master`(`item_type=4`)·`consumable_master`
- [세이브 데이터 기획서](save-data-기획서.md) — `player_buff` 저장, 코어 로드 `activeBuffs`
- [스테이지/전투 결과 기획서](stage-battle-기획서.md) — 클리어 보상에 버프 배율 반영(유일한 적용 경로)
- [오프라인 보상 정산 기획서](offline-reward-기획서.md) — 정산식(버프 배율 미적용)
- [거래소 / 교역선 기획서](trade-기획서.md) — 7.5 축소 운전(정본=MySQL, 캐시=파생) 원칙
- [ErrorCode 통합 정의](../공통/error-code-정의.md) — 4020~4029 소모품/버프 할당
