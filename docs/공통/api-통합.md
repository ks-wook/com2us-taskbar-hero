# API 통합 문서

> 지금까지 작성된 세부 기획서들의 **API 엔드포인트를 한곳에 모은 참조 문서**다. 요청/응답의 상세 스키마와 처리 규칙은 원 기획서(각 행의 "출처")가 **정본**이며, 본 문서는 전체 목록을 빠르게 보기 위한 집약본이다.

## 목차

- [1. 공통 규약](#1-공통-규약)
- [2. AccountServer API](#2-accountserver-api-5160)
- [3. GameServer API](#3-gameserver-api-5247)
  - [3.1 세이브 / 진행](#31-세이브--진행)
  - [3.2 오프라인 보상](#32-오프라인-보상)
  - [3.3 인벤토리 / 아이템 / 큐브](#33-인벤토리--아이템--큐브)
  - [3.4 성장 (직업 / 스킬 / 룬)](#34-성장-직업--스킬--룬)
  - [3.5 스테이지 / 전투](#35-스테이지--전투)
  - [3.6 거래소 / 교역선](#36-거래소--교역선)
  - [3.7 메일(보상)](#37-메일보상)
  - [3.8 출석부 보상](#38-출석부-보상)
  - [3.9 가챠(뽑기)](#39-가챠뽑기)
  - [3.10 보스러시 / 랭킹](#310-보스러시--랭킹)
  - [3.11 관리 API (랭킹 캐시 적재)](#311-관리-api-랭킹-캐시-적재)
- [4. 에러 코드](#4-에러-코드)
- [5. 출처 문서](#5-출처-문서)

## 1. 공통 규약

- 모든 API는 **POST**. 응답은 `{ success, errorCode, message, data }` 형식이며 `success`는 `errorCode == 0`(`Success`)과 동치다.
- **인증**: 로그인 이후 요청은 body에 `{ userId, token, data }`를 담는다(헤더 미사용). 미들웨어가 `token`을 Redis `auth:token:{userId}`와 대조. **GameServer의 게임 API는 모두 인증이 필요**하므로 아래 GameServer 표(3장)에는 인증 칼럼을 두지 않는다. **무인증 예외**: AccountServer의 회원가입·로그인뿐이다(마스터 데이터는 클라이언트 번들이라 다운로드 API가 없다). **관리 API(3.11)는 이 인증 체계 밖**이다 — 게임 토큰이 아니라 `X-Admin-Key` 헤더로 보호하고 응답도 `errorCode`를 쓰지 않는다.
- `errorCode`는 `TaskbarHero.Common`의 `ErrorCode`이며 값 목록은 [ErrorCode 통합 정의](error-code-정의.md) 참고.
- **Base URL(개발)**: AccountServer `http://localhost:5160`, GameServer `http://localhost:5247`.

## 2. AccountServer API (`:5160`)

> 출처: [계정/로그인 기획서](../세부/account-login-기획서.md) 5장

| 경로 | 기능 | 인증 | 요청 `data`(또는 body) | 응답 주요 | 주요 에러 |
|---|---|---|---|---|---|
| `POST /api/auth/signup` | 계정 생성(회원가입) | 무인증 | `{ email, password, nickname }` | `userId` | `DuplicateEmail(1003)`, `InvalidRequest(1006)`, `NicknameTooLong(1007)` |
| `POST /api/auth/login` | 로그인·인증 토큰 발급 | 무인증 | `{ email, password }` | `userId`, `token` | `UserNotFound(1001)`, `InvalidPassword(1002)` |
| `POST /api/auth/logout` | 로그아웃(토큰 무효화) | 인증 | `{}` | — | `InvalidToken(1004)`, `ExpiredToken(1005)`, `InvalidRequest(1006)` |
| `POST /api/auth/validate` | 자동 로그인 검증(저장된 세션 유효성 확인) | 인증 | `{}` | `userId` | `InvalidToken(1004)`, `ExpiredToken(1005)`, `InvalidRequest(1006)` |

- 위 네 엔드포인트는 모두 `ServerError(11001)`(HTTP 500)를 낼 수 있다 — 서비스가 공개 메서드 전체를 try로 감싸 예외를 이 코드로 일반화한다(내부 정보 미노출). 도메인 에러가 아니라 장애 신호이므로 표에는 따로 적지 않는다.
- 로그인은 단일 세션(토큰 UPSERT + Redis 덮어쓰기 → 기존 기기 자동 무효).
- `validate`는 **읽기 전용**이다 — 토큰을 재발급하지도 TTL을 연장하지도 않으며(재발급은 단일 세션 정책상 다른 기기 세션을 끊는다), Redis `auth:token:{userId}`와 대조만 한다. 클라이언트는 타이틀 화면에서 저장된 `{ userId, token }`으로 호출해 성공이면 재로그인 없이 진입하고, `1004`(다른 기기 로그인으로 밀려남)·`1005`(만료·폐기)면 저장값을 버리고 로그인 화면을 띄운다([계정/로그인 기획서](../세부/account-login-기획서.md) 5.4).

## 3. GameServer API (`:5247`)

### 3.1 세이브 / 진행

> 출처: [세이브 데이터 기획서](../세부/save-data-기획서.md) 5장

| 경로 | 기능 | 요청 `data` | 응답 주요 | 주요 에러 |
|---|---|---|---|---|
| `POST /api/game/load` | 접속 시 코어 세이브 스냅샷 로드(고정 크기 데이터 전량, 가방 아이템 제외) | `{}` | `player`, `characters[]`, `currencies[]`, `equipped[]`, `skills[]`, `runes[]`, `cube`, `activeBuffs[]`, `inventoryTotal`, `offlineElapsedSec` | (신규는 `{ isNew:true }`) |
| `POST /api/game/inventory/list` | 가방 아이템 페이지 조회(`slot` 커서 keyset 페이징) | `{ cursor, limit }` | `items[]`, `nextCursor`, `hasMore`, `total` | `SaveNotFound(2001)` |
| `POST /api/game/create-character` | 캐릭터 1개 생성(빈 슬롯 배정 + 직업 기본 무기 장착 + 기본 액티브 스킬 습득·장착) | `{ nickname, classCode, gender }` | `characterId`, `classCode`, `gender`, `level`, `cost`, `balance` | `InvalidClassCode(2005)`, `InvalidCharacterId(2006)`, `InvalidGender(2007)`, `PlayerAlreadyExists(2004)`, `InsufficientCurrency(4005)`, `NicknameTooLong(1007)` |
| `POST /api/game/party/arrange` | 파티 편성 저장(저장 후 파티 **전체 스냅샷**) | `{ members:[{ characterId, slot }] }`(1~3개) | `characters[]`(보유 전체, 자리 순) | `CannotRemoveLastCharacter(2008)`, `CharacterNotFound(2009)`, `PartySlotOccupied(2010)`, `InvalidCharacterId(2006)`, `InvalidRequest(1006)` |
| `POST /api/game/update-last-active` | 접속 시각 갱신(heartbeat, 오프라인 경과 기준) | `{}` | `lastActiveAt` | — |

- **로드는 2단계**다. 크기가 고정된 데이터(플레이어·캐릭터·재화·장착 장비·스킬·룬·큐브)는 `load`가 한 번에 내려주고, 무한히 커질 수 있는 **가방 아이템만** `inventory/list`가 페이징한다. 접속 직후에는 `load`만 호출하고, 가방은 **창고 UI를 열 때마다** 조회한다(자동 전투 전리품이 계속 적재되므로 로컬 캐시를 신뢰하지 않는다). 이 조회는 **캐시 없이 MySQL을 직접 읽는다** — `cursor`·`limit`을 그대로 질의로 넘겨 `(user_id, slot)` 인덱스로 필요한 구간만 읽으므로 캐시를 둘 이유가 없다([인벤토리/아이템/큐브 기획서](../세부/inventory-item-cube-기획서.md) 6.5).
- **페이지 간 정합성은 서버가 검증하지 않는다.** 클라이언트가 페이지를 이어붙일 때 `itemId`를 키로 중복 제거하고 나중 페이지를 우선한다([세이브 데이터 기획서](../세부/save-data-기획서.md) 5.2).
- 캐릭터는 **한 번에 1개씩** 생성(`create-character`), 계정당 최대 3개·**직업 중복 불가**. `nickname`은 최초 생성 시에만 사용하며 **최대 12자**(초과 시 `NicknameTooLong(1007)`). 캐릭터·성장 상태 조회는 별도 API 없이 `load` 스냅샷 사용.
- **성별(`gender`)**: `1`(남)·`2`(여) 중 하나를 생성 시 함께 보내며, 그 외 값은 `InvalidGender(2007)`. 외형만 가르는 값이라 직업 중복 제약·스탯·비용에는 영향이 없고, 요청에 필드가 없으면 `1`(남)로 저장된다. **생성 이후 변경 API는 없다.** `load`의 `characters[]`에도 `gender`가 포함된다.
- **파티 편성은 이동 절차가 아니라 스냅샷**이다(`party/arrange`). `members`가 곧 최종 파티이며 **목록에 없는 보유 캐릭터는 자동으로 미편성(`slot=0`)** 이 되므로, 추가·추방·교체·자리 바꾸기가 이 호출 하나로 표현된다. 멱등이고 골드를 쓰지 않으며 캐릭터를 삭제하지도 않는다. 응답의 `characters[]`로 편성 화면을 다시 그리면 되고 재조회가 필요 없다.
- **기본 무기 장착 상태로 생성**: 생성되는 캐릭터는 그 직업의 **최저 등급 무기**(마스터 `item_master`에서 `equip_slot=1`·`class_req` 일치 중 가장 낮은 등급)를 1개 지급받아 무기 슬롯에 장착한 상태로 시작한다 — 캐릭터 삽입과 **같은 트랜잭션**이며, 장착 중이라 가방 칸을 쓰지 않는다(`inventoryTotal` 불변). 응답 필드는 늘지 않으므로 클라이언트는 생성 직후의 `load` 응답 `equipped[]`로 확인한다([세이브 데이터 기획서](../세부/save-data-기획서.md) 5.3).
- **생성 비용**: **1번 슬롯(최초 생성=계정 초기화)은 무료**, **2·3번 슬롯은 골드 소모**(비용은 마스터 `character_create_cost` 명시값, 서버 권위 차감). 골드 부족 시 `InsufficientCurrency(4005)`. 응답 `cost`(소모 골드)·`balance`(차감 후 잔액)를 회신하며, 클라이언트는 생성 전 안내 비용을 마스터 번들(`character_create_cost`의 다음 슬롯 값)로 표시한다.

### 3.2 오프라인 보상

> 출처: [오프라인 보상 정산 기획서](../세부/offline-reward-기획서.md) 5장

| 경로 | 기능 | 요청 `data` | 응답 주요 | 주요 에러 |
|---|---|---|---|---|
| `POST /api/game/offline/claim` | 오프라인 경과 보상(골드·경험치) 정산·지급 | `{}` | `offlineElapsedSec`, `effectiveSec`, `capped`, `rewards{gold,exp}`, `characters[]`, `lastActiveAt` | `NoOfflineReward(3001)`, `OfflineRewardAlreadyClaimed(3002)` |

- 경험치는 **3캐릭터 모두에게 동일** 지급, 골드는 계정. 아이템 미지급.

### 3.3 인벤토리 / 아이템 / 큐브

> 출처: [인벤토리/아이템/큐브 기획서](../세부/inventory-item-cube-기획서.md) 5장

| 경로 | 기능 | 요청 `data` | 응답 주요 | 주요 에러 |
|---|---|---|---|---|
| `POST /api/game/inventory/equip` | 지정 캐릭터에 장비 장착(스왑). 장착 아이템은 가방 칸 반납 | `{ characterId, itemId }` | `equipped`, `unequipped`, `unequippedBagSlot` | `ItemNotFound(4001)`, `ItemNotEquippable(4003)`, `ItemEquipped(4007)`, `InvalidCharacterId(2006)`, `InventoryFull(4002)` |
| `POST /api/game/inventory/unequip` | 지정 슬롯 장비 해제. 가방 빈 칸으로 복귀 | `{ characterId, slot }` | `slot`, `itemId`, `bagSlot` | `ItemNotFound(4001)`, `InvalidCharacterId(2006)`, `InventoryFull(4002)` |
| `POST /api/game/inventory/enhance` | 장비 강화 단계 +1(재화 소모, 확정 상승). 장착 중인 장비도 가능 | `{ itemId }` | `enhanceLevel`, `equipped`, `cost`, `balance` | `ItemNotFound(4001)`, `ItemNotEquippable(4003)`, `MaxEnhanceReached(4004)`, `InsufficientCurrency(4005)` |
| `POST /api/game/inventory/expand` | 인벤토리 용량 확장(골드 소모) | `{ count }` | `inventoryCapacity`, `cost`, `balance` | `InsufficientCurrency(4005)`, `InventoryCapacityMax(4008)` |
| `POST /api/game/inventory/move` | 인벤토리 배치 이동/교환(드래그 저장) | `{ itemId, toSlot }` | `moved`, `swapped` | `ItemNotFound(4001)`, `InvalidInventorySlot(4009)` |
| `POST /api/game/cube/combine` | 큐브 합성(동급 아이템 3개→상위 등급 1개, 슬롯·클래스 무관) | `{ itemIds[] }` | `consumed`, `result`, `cube`, `inventoryDelta` | `ItemNotFound(4001)`, `ItemEquipped(4007)`, `CubeRecipeNotMet(4010)` |
| `POST /api/game/cube/dismantle` | 큐브 분해(아이템→골드 전환) | `{ items:[{itemId,count}] }` | `gold`, `cubeExp`, `cube`, `balance`, `inventoryDelta` | `ItemNotFound(4001)`, `InsufficientQuantity(4006)`, `ItemEquipped(4007)`, `InvalidRequest(1006)` |
| `POST /api/game/cube/craft` | 큐브 제작(레시피로 아이템 생성) | `{ recipeCode }` | `consumed`, `gained`, `cube`, `balance`, `inventoryDelta` | `CubeRecipeNotMet(4010)`, `CubeLevelInsufficient(4011)`, `InsufficientCurrency(4005)`, `InventoryFull(4002)` |
| `POST /api/game/consumable/use` | 소모품 1개 사용 → 계정 획득량 버프 부여·연장(경험치·골드 부스터) | `{ itemId }` | `itemCode`, `remainingQuantity`, `buff`, `activeBuffs`, `inventoryDelta` | `ItemNotFound(4001)`, `ItemNotConsumable(4020)`, `InsufficientQuantity(4006)`, `BuffDurationLimitExceeded(4021)`, `MasterDataNotLoaded(10001)` |
| `POST /api/game/consumable/buffs` | 적용 중인 획득량 버프 조회(버프 UI 재동기화용 경량 조회) | 없음 | `serverTime`, `activeBuffs` | 인증 실패 계열만 |

- **가방을 바꾸는 액션은 변경분을 `inventoryDelta`(`upserted[]`·`removed[]`)로 응답에 담는다.** 클라이언트는 응답만으로 자기 로컬 가방 캐시를 갱신하며 **액션 뒤에 `/api/game/load`·`/api/game/inventory/list`를 재조회하지 않는다**(공통 규약: [인벤토리/아이템/큐브 기획서](../세부/inventory-item-cube-기획서.md) 5.0). 장착·해제·용량 확장은 기존 `equipped`/`unequipped`/`bagSlot`/`inventoryCapacity` 필드로 충분해 이 블록을 두지 않는다. 이 도메인 밖에서도 가방을 바꾸는 **`gacha/pull`(뽑기 지급)·`stage/clear`(전리품)·`mail/claim`·`mail/claim-all`(첨부)·`trade/register`·`trade/cancel`(에스크로 이동)** 이 같은 규약을 따르며, `trade/buy`는 구매 아이템이 우편함으로 가므로 이 블록이 없다.
- 장비는 **캐릭터별**(장착 시 `characterId` 필수), 인벤토리·골드·큐브는 계정 공유. 큐브 합성·분해·제작(`cube/*`)과 장비 강화(`inventory/enhance`)는 **구현 완료**.
- **강화**는 1회 호출당 1단계이며 비용·상한(현재 +10)은 마스터 `enhance_master`가 정의한다(서버 권위 차감, 실패·하락 없음). 장착 중인 장비도 해제 없이 강화하며, 이때 장착 정보의 강화 단계까지 함께 갱신되고 응답 `equipped`가 `true`로 온다. 스탯 배율은 응답에 없다 — 클라이언트가 마스터 번들의 `stat_multiplier`로 계산한다.
- `consumable/use`는 **1회 1개 고정**(수량 필드 없음)이며 버프도 **계정 단위**다. 활성 버프를 받는 창구는 세 곳 — 접속 직후는 코어 로드(`/api/game/load`)의 `activeBuffs`, 사용 직후는 `consumable/use` 응답, 이후 재동기화는 `consumable/buffs`([소모품/버프 기획서](../세부/consumable-buff-기획서.md) 5.2).
- 버프 배율은 **스테이지 클리어 보상(`stage/clear`)에만** 곱해진다. 오프라인 정산(`offline/claim`)·메일·출석·큐브 분해·거래 대금에는 적용하지 않는다(같은 문서 6.3·6.5).

### 3.4 성장 (직업 / 스킬 / 룬)

> 출처: [성장 시스템 기획서](../세부/growth-기획서.md) 5장

| 경로 | 기능 | 요청 `data` | 응답 주요 | 주요 에러 |
|---|---|---|---|---|
| `POST /api/game/growth/skill/levelup` | 스킬 레벨 +1(스킬 포인트 소모) | `{ characterId, skillCode }` | `skillCode`, `level`, `cost`, `skillPoint` | `InvalidGrowthTarget(5001)`, `SkillClassMismatch(5004)`, `SkillMaxLevel(5002)`, `InsufficientSkillPoint(5003)`, `InvalidCharacterId(2006)` |
| `POST /api/game/growth/skill/reset` | 스킬 초기화(무료, 포인트 회수) | `{ characterId }` | `resetSkillCount`, `skillPoint` | `InvalidCharacterId(2006)` |
| `POST /api/game/growth/skill/equip` | 액티브 스킬 장착(최대 2개 설정) | `{ characterId, skillCodes[] }`(0~2) | `equipped[]` | `SkillNotActive(5005)`, `SkillNotLearned(5006)`, `ActiveSkillLimitExceeded(5007)`, `SkillClassMismatch(5004)`, `InvalidGrowthTarget(5001)`, `InvalidCharacterId(2006)` |
| `POST /api/game/growth/rune/upgrade` | 룬 레벨 +1(골드 소모, 계정 공용) | `{ runeCode }` | `runeCode`, `level`, `cost`, `balance` | `InvalidGrowthTarget(5001)`, `RunePrereqNotMet(5010)`, `RuneMaxLevel(5011)`, `InsufficientCurrency(4005)` |

- 스킬 레벨업 1레벨당 1포인트, 스킬 초기화 무료, 액티브 스킬 캐릭터당 2개 장착. 룬은 계정 공용·1레벨씩·레벨 비례 골드.

### 3.5 스테이지 / 전투

> 출처: [스테이지/전투 결과 기획서](../세부/stage-battle-기획서.md) 5장

| 경로 | 기능 | 요청 `data` | 응답 주요 | 주요 에러 |
|---|---|---|---|---|
| `POST /api/game/stage/enter` | 스테이지 진입(진행 가능 검증) | `{ act, difficulty, stage }` | `stageId`, `monsters[]`, `boss`, `enteredAt` | `StageNotFound(6001)`, `StageLocked(6002)` |
| `POST /api/game/stage/clear` | 스테이지 클리어 → 보상 지급·진행도 갱신 | `{ act, difficulty, stage }` | `rewards`, `characters[]`(각 `isLevelUp`), `balance`, `progress`, `inventoryDelta` | `StageNotEntered(6003)` |
| `POST /api/game/stage/fail` | 파티 전멸 보고(기록 전용, 상태 불변) | `{ act, difficulty, stage, elapsedMs, remainingMonsterCount, reachedBoss }` | `stageId`, `failedAt` | `StageNotFound(6001)`, `StageNotEntered(6003)`, `InvalidRequest(1006)` |

- 진입 응답은 스테이지의 **몬스터 구성(`monsters`)·보스(`boss`)** 를 포함하며, 각 항목에 **등장 레벨(`monsterLevel`)** 이 실린다(스탯은 내려주지 않는다 — 클라가 `monster_master`의 레벨 1 기준값에 레벨 배율을 곱해 산출). 클리어 보상(골드·경험치·전리품)은 서버가 마스터로 산출, 경험치는 3캐릭터 동일 지급(캐릭터별 **`isLevelUp`**), 골드는 계정. 이미 클리어한 스테이지는 재파밍 가능(보상은 프런티어와 **동일**). 전리품이 인벤토리 용량을 초과하면 **전리품만 폐기하고 골드·경험치는 지급**하며 클리어는 성공한다(에러 없음, `rewards.items`·`inventoryDelta` 비움).
- **실패(`stage/fail`)는 기록 전용이다.** 전투가 클라이언트 권위라 서버는 전멸을 알 수 없어 클라이언트가 보고하며, 진행도·보상·재화·현재 진입 스테이지를 **하나도 바꾸지 않는다**(같은 스테이지 즉시 재도전 가능, 응답은 접수 확인뿐). 보고 값(`elapsedMs`·`remainingMonsterCount`·`reachedBoss`)은 **실질 난이도 측정용 관측치**이고 음수만 거부한다. 유실돼도 게임 상태에 영향이 없어 재시도하지 않는다.

### 3.6 거래소 / 교역선

> 출처: [거래소 / 교역선 기획서](../세부/trade-기획서.md) 5장

| 경로 | 기능 | 요청 `data` | 응답 주요 | 주요 에러 |
|---|---|---|---|---|
| `POST /api/game/trade/list` | 거래소 목록 조회(판매중 **&middot; 미만료**, 아이템 코드 검색). `mine=false`(기본) 본인 등록 제외 / `mine=true` 본인 등록만 | `{ itemCode?, mine?, page?, pageSize? }` | `listings[]`, `page`, `hasMore` | — |
| `POST /api/game/trade/register` | 판매 등록(에스크로) | `{ itemId, price }` | `listingId`, `itemCode`, `price`, `inventoryDelta` | `ItemNotFound(4001)`, `ItemEquipped(4007)`, `TradeNotSellable(7002)`, `TradePriceOutOfRange(7006)`, `TradeListingLimitExceeded(7007)`, `InvalidSaveData(2002)` |
| `POST /api/game/trade/buy` | 구매(골드 차감 → 아이템·대금 모두 메일 발급) | `{ listingId }` | `gained`, `cost`, `balance`, `mailId` | `TradeListingNotFound(7001)`, `TradeAlreadyClosed(7005)`, `TradeSelfPurchase(7004)`, `InsufficientCurrency(4005)` |
| `POST /api/game/trade/cancel` | 판매 취소(아이템 복귀) | `{ listingId }` | `restored`, `inventoryDelta` | `TradeListingNotFound(7001)`, `TradeNotOwner(7003)`, `TradeAlreadyClosed(7005)`, `InventoryFull(4002)` |

- 목록 조회는 **본인 등록을 쿼리 단계에서 제외**한다(자기 등록은 구매 불가). 판매 취소에 필요한 `listingId`는 `mine=true` 조회로 얻는다.
- 등록은 아이템을 인벤토리에서 거래소 보관(에스크로)으로 이동. 구매 시 **구매 아이템(구매자)·판매 대금(판매자) 모두 메일(3.7, `category=2` 거래)로 지급**되며 수령 시 계정에 반영된다. 구매 단계에서는 인벤토리 용량을 검사하지 않는다.
- 동시 구매 경합은 **MySQL 조건부 갱신**(`status=1`일 때만 전이)의 행 잠금으로 직렬화한다 — 뒤에 온 요청은 0행을 받아 `TradeAlreadyClosed(7005)`가 되며 복제·이중 판매가 불가하다. 취소·만료 배치도 같은 방식이며 **거래소 전 경로에 애플리케이션 락이 없다**. 판매 등록에서 같은 아이템을 겹쳐 등록하려는 경합도 에스크로 `DELETE`의 행 잠금이 막고 뒤에 온 요청이 `ItemNotFound(4001)`를 받는다([거래소 기획서](../세부/trade-기획서.md) 7.4).
- 목록 조회는 전역 공유 읽기지만 **캐시를 두지 않는다** — 전용 색인(`idx_trade_browse`·`idx_trade_price`)을 타는 MySQL 직접 조회이며, **뷰어 필터(`mine`)·정렬·페이징을 모두 쿼리에서 처리**해 필요한 한 페이지만 읽는다. `pageSize`는 서버가 상한을 강제하고, 깊은 페이지는 `page`를 clamp한다. 판단 근거는 [거래소 기획서](../세부/trade-기획서.md) 7.3.
- **만료 판정은 배치가 아니라 읽는 시점에 한다.** 목록·구매·등록 한도 쿼리가 `expires_at > now`를 함께 검사하므로, 만료 시각이 지난 등록은 만료 배치가 돌기 전이라도 목록에서 빠지고 구매는 `TradeAlreadyClosed(7005)`로 거부되며 판매자 등록 한도에서도 제외된다. 배치(**하루 1회**)는 에스크로 아이템 반송과 `status=4` 정리만 담당하므로, 주기는 판매 기간(3일) 정확도가 아니라 **반송 지연 상한**만 결정한다([거래소 기획서](../세부/trade-기획서.md) 7.6).

### 3.7 메일(보상)

> 출처: [메일 기획서](../세부/mail-기획서.md) 5장

| 경로 | 기능 | 요청 `data` | 응답 주요 | 주요 에러 |
|---|---|---|---|---|
| `POST /api/game/mail/list` | 우편함 목록 조회 | `{}` | `mails[]`(첨부·읽음·수령·만료 포함) | — |
| `POST /api/game/mail/claim` | 단건 메일 첨부 수령 | `{ mailId }` | `gained`, `balance`, `inventoryDelta` | `MailNotFound(8001)`, `MailAlreadyClaimed(8002)`, `MailExpired(8003)`, `InventoryFull(4002)` |
| `POST /api/game/mail/claim-all` | 수령 가능한 메일 일괄 수령 | `{}` | `claimedMailIds[]`, `gained`, `balance`, `inventoryDelta` | `InventoryFull(4002)` |

- 첨부(재화·아이템)는 서버가 지급하며 중복 수령 불가(수령 플래그+행 잠금). 만료 메일은 수령 거부. 발급은 거래소(4.7)·출석부(4.9)·운영이 담당.

### 3.8 출석부 보상

> 출처: [출석부 보상 시스템 기획서](../세부/attendance-기획서.md) 5장

| 경로 | 기능 | 요청 `data` | 응답 주요 | 주요 에러 |
|---|---|---|---|---|
| `POST /api/game/attendance/status` | 이번달 출석 진행도 조회(읽기 전용) | `{}` | `yearMonth`, `today`, `attendedCount`, `todayDay`, `todayClaimed`, `canClaim`, `days[]`(1~30일차) | `MasterDataNotLoaded(10001)` |
| `POST /api/game/attendance/claim` | 오늘자 출석 보상 획득(보상 **메일 발급**, 30일차 이후 1일차부터 순환) | `{}` | `attendDate`, `day`, `reward`, `mailId` | `AttendanceAlreadyClaimed(9001)`, `SaveNotFound(2001)`, `MasterDataNotLoaded(10001)` |

- 보상 **일차(`day`)는 날짜가 아니라 이번달 누적 출석 순번**(`이번달 출석 수 + 1`, 1~30)이다. 월중에 처음 접속해도 1일차 보상부터 순서대로 받으며, 달이 바뀌면 1일차로 리셋된다.
- 날짜 경계는 서버 KST 자정, 하루 1회(`(user_id, attend_date)` 유니크). 획득 보상은 즉시 지급이 아니라 **메일(3.6)로 발급**되어 우편함 수령 시 계정 반영.

### 3.9 가챠(뽑기)

> 출처: [가챠(뽑기) 시스템 기획서](../세부/gacha-기획서.md) 5장

| 경로 | 기능 | 요청 `data` | 응답 주요 | 주요 에러 |
|---|---|---|---|---|
| `POST /api/game/gacha/banners` | **가챠 배너 조회** — 지금 돌릴 수 있는 배너 목록(서버 시각으로 노출 판정) + 배너별 천장 진행도 | 없음 | `serverTime`, `banners[]`(`gachaCode`·`sortOrder`·`openAt`·`closeAt`·`counters[]`(`grade`·`pityCount`·`pityThreshold`·`remainingToPity`)) | `MasterDataNotLoaded(10001)` |
| `POST /api/game/gacha/pull` | **가챠 뽑기(1연·10연 공통)** — `pullType` 1:1연(`cost_single`, 1회) / 2:10연(`cost_multi`, `multi_count`회 + 보장 등급 대체). 등급 가중치 추첨 → 등급 슬롯 내 균등 선택 | `{ gachaCode, pullType }` | `gachaCode`, `pullId`, `pullType`, `pulledAt`, `results[]`, `cost`, `balance`, `counters[]`, `inventoryDelta` | `GachaNotFound(12001)`, `GachaNotAvailable(12003)`, `InvalidRequest(1006)`, `GachaPoolEmpty(12002)`, `InsufficientCurrency(4005)`, `InventoryFull(4002)`, `MasterDataNotLoaded(10001)` |
| `POST /api/game/gacha/history` | 뽑기 기록 조회(`pull_id` 커서 keyset 페이징, 최신순). 페이징 단위는 **뽑기 요청**(10연 1건 = 1행) | `{ gachaCode?, cursor?, limit? }` | `pulls[]`(각 `items[]` 포함), `nextCursor`, `hasMore` | 인증 실패 계열만 |

- **1연·10연은 한 엔드포인트에서 `pullType`으로 가른다.** 요청·응답 스키마와 처리 흐름이 같아 엔드포인트를 둘로 두면 같은 계약이 두 벌 생긴다. 다른 것은 비용·횟수·보장 적용뿐이며 셋 다 마스터 값이라 서버가 `pullType`에서 파생한다.
- **뽑을 횟수(`count`)는 요청 필드가 아니다.** 횟수는 확률·결과와 함께 서버 소유 값이며 `gacha_master.multi_count`에서 읽는다 — 클라이언트가 `10`을 박아 두면 마스터를 바꾸는 순간 깨지고, "몇 번 뽑는가"의 출처가 요청과 마스터 두 곳이 된다. 정의되지 않은 `pullType`은 `InvalidRequest(1006)`로 거부한다.
- **천장 진행도(`counters[]`)는 남은 횟수까지 서버가 계산해 내려준다.** `pityCount`/`pityThreshold`(게이지)와 함께 `remainingToPity`(= `pityThreshold - pityCount`, 하드 천장 규칙이 없거나 기준을 넘어서면 **0**)를 담아 클라이언트가 "천장까지 N회"를 뺄셈 없이 표시한다. 배너 조회와 뽑기 응답이 같은 필드 구성이다.
- **배너 목록은 서버가, 확률표는 클라 번들이 담당한다.** 마스터가 클라이언트 번들이라 이름·비용·등급 확률·후보 목록은 클라가 직접 그리고, 서버는 **번들만으로 알 수 없는 것**(지금 열려 있는 배너인가 · 내 천장이 얼마인가)만 내려준다. 확률표 조회(`gacha/detail` 류) API는 두지 않는다.
- **배너 노출 판정은 서버 시각 기준**이다(`gacha_master`의 `is_active`·`open_at`·`close_at`). **뽑기 요청도 노출을 다시 검증**하며, 목록을 받아 둔 사이 배너가 닫혔으면 비용 차감 전에 `GachaNotAvailable(12003)`으로 거부한다 — 클라이언트는 이 코드를 받으면 배너 목록을 재조회한다.
- 등급·아이템·천장·보장 판정은 전부 서버가 확정한다(요청에 결과·확률·횟수를 넣을 필드가 없다). 비용 차감 → 추첨 → 지급 → 카운터 갱신 → 기록 적재가 **하나의 트랜잭션**이며, 가방 용량 초과(`InventoryFull`) 시 골드 차감까지 전체 롤백된다.
- 기록 조회는 오프셋이 아니라 **커서 페이징**이다(append-only 로그라 `OFFSET`이 깊어질수록 비싸고, 조회 중 새 뽑기가 들어오면 기준이 밀린다). `limit`은 서버가 1~50으로 clamp하며 전체 건수(`total`)는 내려주지 않는다.

### 3.10 보스러시 / 랭킹

> 출처: [보스러시 / 랭킹 기획서](../세부/boss-rush-기획서.md) 5장

| 경로 | 기능 | 요청 `data` | 응답 주요 | 주요 에러 |
|---|---|---|---|---|
| `POST /api/game/boss-rush/info` | **보스러시 정보 조회** — 해금 여부·현 시즌·내 최고 기록·진행 중 런(서버만 아는 값) | `{}` | `serverTime`, `unlocked`, `unlockStageSequence`, `maxStageCleared`, `season`, `myRecord`(`bestClearMs`·`rank`), `activeRun`(`expiresAt`) | `SaveNotFound(2001)`, `MasterDataNotLoaded(10001)` |
| `POST /api/game/boss-rush/enter` | **도전 시작** — 런 개시(`runId` 발급). **횟수 차감 없음**(도전 무제한). 요청 파라미터 없음 | `{}` | `runId`, `seasonId`, `rounds[]`(`round`·`backgroundType`·`monsters[]`(`monsterCode`·`monsterLevel`·`count`)·`boss`) | `BossRushLocked(13001)`, `BossRushSeasonClosed(13007)`, `SaveNotFound(2001)`, `MasterDataNotLoaded(10001)` |
| `POST /api/game/boss-rush/clear` | **클리어 보고** — **클라이언트가 측정한 클리어 시간**을 받아 형식 검증 후 그대로 기록 + 시즌 최고 기록 갱신. **보상 지급 없음** | `{ runId, clearMs, rounds:[{ round, elapsedMs }] }`(1~5 전부, 합계 = `clearMs`) | `clearMs`, `isNewRecord`, `bestClearMs`, `rank` | `BossRushRunNotFound(13003)`, `BossRushRunAlreadyFinished(13004)`, `BossRushInvalidProgress(13006)`, `InvalidRequest(1006)` |
| `POST /api/game/boss-rush/rank` | **랭킹 목록 조회** — 시즌 **전체 등재 유저**를 오프셋 페이징으로 한 페이지씩 조회(순위 상한 없음). **뷰어 무관 데이터** | `{ seasonId?, offset?, limit? }` | `seasonId`, `seasonStatus`, `seasonEndAt`, `totalEntries`, `offset`, `limit`, `source`(1:Redis 2:MySQL 폴백), `entries[]`(`rank`·`userId`·`nickname`·`clearMs`·`recordedAt`) | `BossRushSeasonClosed(13007)`, `MasterDataNotLoaded(10001)` |
| `POST /api/game/boss-rush/my-rank` | **내 순위 조회** — 요청자 본인의 시즌 순위 1건(랭킹 UI의 "내 순위" 고정 영역용) | `{ seasonId? }` | `seasonId`, `totalEntries`, `source`, `myRank`(기록 없으면 `null`) | `BossRushSeasonClosed(13007)`, `MasterDataNotLoaded(10001)` |

- **도메인 세그먼트에 kebab-case(`boss-rush`)를 쓴다.** 기존 규약이 이미 액션 세그먼트에 kebab을 쓰고 있고(`update-last-active`·`claim-all`), 두 단어 도메인을 붙여 쓰면 읽기 어렵다.
- **액션 이름은 스테이지 도메인(3.5)과 같은 `enter`/`clear` 짝**이다 — 도전 개시가 `start`가 아니라 `boss-rush/enter`인 이유이며, 같은 "진입 → 클리어" 흐름을 두 콘텐츠가 다른 동사로 부르지 않게 한다. **개시 응답에 `startedAt`도 `timeLimitMs`도 없다** — 시간 측정이 클라 측으로 옮겨가 클라이언트가 서버 시각을 쓸 곳이 없고, `boss_rush_run.started_at`은 만료 판정·사후 관측용 내부 값으로만 남는다. 진행 중 런이 언제까지 보고 가능한지는 `boss-rush/info`의 `activeRun.expiresAt`이 알려 준다.
- **클리어 시간은 클라이언트가 측정해 보고하고, 서버는 그 값을 그대로 기록한다.** 전투가 클라 권위인 프로젝트에서 시간 측정만 서버로 가져오면 라운드 전환 연출·로딩·네트워크 지연이 전부 기록에 섞여 **같은 파티가 같은 전투를 해도 회선 상태로 순위가 갈린다** — 시간 경쟁 콘텐츠에서 그 오염이 조작 위험보다 크다고 판단했다([보스러시 기획서](../세부/boss-rush-기획서.md) 8장 확정).
- **서버 검증은 형식·자기정합성뿐이며 모두 `BossRushInvalidProgress(13006)`으로 묶인다** — `clearMs`가 **런 수명(`run_expire_sec` 30분)** 초과(런이 열려 있던 시간보다 긴 클리어는 자기모순), `rounds` 누락·중복·`elapsedMs ≤ 0`, **합계가 `clearMs`와 불일치**. **기록의 진위는 판정하지 않는다**(파티 전투력 역산 이론 하한 검증은 도입하지 않는다). **제한 시간과 일일 도전 횟수를 없앴으므로** 조작 방어는 **사후 관측**(런의 `finished_at − started_at`과 보고 `clearMs`의 괴리를 로그로 남긴다)에만 남는다 — 보상이 시즌 순위 보상(골드)뿐이라 총량 상한이 없어도 재화 경제에 파급되지 않는다(같은 문서 6.2).
- **실패·포기 보고 엔드포인트가 없다.** 5라운드를 못 깨면 클라이언트는 아무것도 보내지 않고, 그 런은 제한 시간이 지나 만료된다. 전멸을 서버가 확인할 수 없어 실패 보고는 순수 신뢰 경로가 되기 때문이다.
- **진입 응답이 5라운드 전부의 스폰 구성을 한 번에 내려준다.** 라운드 `r`은 Act `r`의 **일반 몬스터 무리 + Act 보스**로 구성되며(스테이지 진입의 `monsters`/`boss` 규약과 동일 — 서버가 `boss_rush_spawn`을 `is_boss`로 갈라 내려준다), 각 항목에 **등장 레벨**과 그 라운드의 `backgroundType`(Act `r`의 스테이지 배경 재활용)이 실린다. 스탯은 내려주지 않는다(클라가 `monster_master`의 레벨 1 기준값에 레벨 배율을 곱해 산출). 라운드마다 서버를 다시 부르지 않는 이유는 라운드 전환이 **전투 중**에 일어나 왕복이 곧 기록 오염이 되기 때문이며, 라운드 전환의 **포탈 이동은 클라이언트 연출**이라 서버 호출이 없고 그 시간은 `clearMs`에서 제외된다.
- **요청에 난이도·라운드 선택 파라미터가 없다.** 라운드 구성·보스 레벨·제한 시간은 전부 마스터 값이며, 클라이언트에 선택 축을 주면 곧 "쉬운 구성으로 빠른 기록"이 되어 랭킹이 무의미해진다.
- **보스러시 API는 재화·아이템을 지급하지 않는다.** 라운드별·완주 보상이 없어 `clear` 응답에 `rewards`·`characters`·`balance`·`inventoryDelta`가 없고, 이 호출이 바꾸는 것은 런 상태와 시즌 최고 기록뿐이다. 보상은 **시즌 순위 보상 메일**(3.7, 템플릿 501·`category=5` 랭킹)로만 나가며 **골드뿐**이다 — 가방 칸을 쓰지 않으므로 수령 단계에서도 `InventoryFull(4002)`이 발생하지 않는다.
- **현재 시즌 랭킹 조회(`rank`·`my-rank`)는 정상 경로에서 MySQL을 건드리지 않는다.** 순위·기록은 리더보드 ZSET(점수에 `clearMs`·`recordedAt`이 인코딩되어 있다), 표시 이름은 `player:nickname` 해시(`HMGET`, 미스만 `game_player`에서 부분 백필), 시즌 메타는 `bossrush:season:current` 해시에서 나온다. MySQL은 **캐시 미스·Redis 폴백·종료 시즌 조회**에서만 개입한다([보스러시 기획서](../세부/boss-rush-기획서.md) 4.3·6.3).
- **`source`는 "순위를 어디서 산출했는지"** 다 — `1`=Redis(`ZRANGE`/`ZRANK`) `2`=MySQL 폴백. **닉네임 캐시 미스나 종료 시즌 메타 조회로 MySQL을 거쳐도 `1`** 이며, "MySQL을 접근했는가"를 뜻하지 않는다.
- **랭킹 정본은 MySQL(`boss_rush_record`)이고 Redis Sorted Set(`rank:bossrush:{seasonId}`)은 순위 조회 전용 캐시**다. 캐시가 비면 **관리 API(3.11)로 정본에서 재적재**하며(서버가 스스로 하지 않는다), Redis 장애 시에는 MySQL 정렬 조회로 축소 운전한다(응답 `source=2`). 동점은 **먼저 달성한 쪽이 상위**이며, Redis 정렬이 이 규칙을 표현하도록 점수를 `clearMs × 10^7 + (recordedAt − season.start_at)`으로 인코딩한다(같은 문서 4.3·6.3).
- **순위 산출 순서**: `clear`는 **MySQL에 기록을 확정(커밋) → Redis 리더보드 갱신(ZADD) → 갱신된 리더보드에서 `ZRANK`로 순위 계산 → 순위를 담은 결과 반환** 순으로 처리한다. ZADD는 반드시 커밋 이후여야 하고(Redis에는 롤백이 없다), 기록을 갱신하지 못했어도(`isNewRecord=false`) `ZRANK`는 수행해 **현재 순위**를 내려준다(같은 문서 6.2).
- **랭킹 목록과 내 순위를 별도 엔드포인트로 나눈다.** 페이징을 도입한 이상 목록(`rank`)은 **페이지를 넘길 때마다** 호출되지만 내 순위(`my-rank`)는 페이지와 무관하게 한 번만 필요하므로, 합쳐 두면 같은 값을 페이지 수만큼 중복 계산·전송한다. 나눠 두면 목록 응답이 `(seasonId, offset, limit)`만으로 결정되는 **뷰어 무관 데이터**로 남는다. 서버 비용도 늘지 않는다 — 목록은 `ZRANGE`, 내 순위는 `ZRANK`+`ZSCORE`로 애초에 다른 연산이다. 클라이언트는 랭킹 UI를 열 때 두 API를 각각 호출하고, 내 페이지로 점프할 때는 `my-rank`의 `myRank.rank`로 `offset = floor((rank-1)/limit) × limit`을 계산해 `rank`를 호출한다([보스러시 기획서](../세부/boss-rush-기획서.md) 5.4·5.5).
- **랭킹 목록은 전체 등재 유저를 페이징으로 노출한다** — 상위 N위로 자르지 않으며 1위부터 꼴찌까지 `offset`으로 넘겨 볼 수 있다. `ZRANGE`가 O(log N + M)이라 깊은 오프셋도 반환 크기에만 비례해 싸기 때문이다(MySQL 폴백에서만 `OFFSET`이 비싸지지만 커버링 인덱스 안의 스캔이라 감수한다). 서버가 clamp하는 것은 **페이지 크기(`limit` ≤ 100)** 뿐이고, 페이지 이동은 클라이언트가 `totalEntries`와 `myRank.rank`(→ `offset = floor((rank-1)/limit) × limit`)로 계산한다(같은 문서 4.3·5.4).
- **페이지 간 스냅샷은 보장하지 않는다.** 각 페이지는 조회 시점 최신이고 페이지끼리 시점이 달라, 넘기는 사이 기록이 갱신되면 중복·누락이 생긴다. 그래서 클라이언트는 페이지를 **이어붙이지 않고 교체**하며(순위 번호가 항상 연속이라 불일치가 드러나지 않는다), `source`가 페이지 간에 바뀌면 처음부터 재조회하고, "마지막 페이지" 판정은 캐시값이 아니라 **매 응답의 `totalEntries`·`entries` 길이**로 한다([보스러시 기획서](../세부/boss-rush-기획서.md) 5.4·6.5).
- **시즌은 주간(KST 월요일 00:00 경계)** 이며, 정산 배치가 순위 보상 메일을 발급하고 다음 시즌을 개시한다. **정산 중(`seasonStatus=2`)에는 새 런을 받지 않는다**(`BossRushSeasonClosed(13007)` → 클라이언트는 잠시 뒤 `info` 재조회). **순위 보상은 1~3위에게 골드만** 발급되므로(4위 이하는 보상이 없다) 시즌당 순위 보상 메일은 3건이다.
- **지난 시즌 랭킹은 기간 제한 없이 조회된다.** `boss_rush_record`가 시즌별로 무기한 보존되고 정산이 `final_rank`를 확정하므로, Redis 캐시(7일 TTL)가 만료된 뒤에는 MySQL에서 `final_rank`를 그대로 읽는다(순위 재계산 없음, `source=2`).
- **버려진 런을 정리하는 배치는 두지 않는다.** 만료된 런에는 반송할 자산이 없어 배치가 할 일이 `status` 컬럼 정리뿐이므로, 거래소와 같은 규약으로 **만료 판정을 읽는 시점에** 한다(3.6) — `clear`는 `started_at + run_expire_sec`(30분)을 넘긴 런을 그 자리에서 `status=3`으로 종결하고 `BossRushRunAlreadyFinished(13004)`로 거부하며, `info`는 만료된 런을 `activeRun: null`로, `enter`는 남은 런을 자동 종결한다. **런 수명 30분은 게임 룰이 아니라** 보고가 네트워크 오류로 실패했을 때 재시도가 통하는 구간이자 버려진 런의 정리 기준이다([보스러시 기획서](../세부/boss-rush-기획서.md) 6.2).

> **마스터(기획) 데이터 다운로드 API는 두지 않는다.** 본 프로젝트는 학습 목적이므로 마스터 데이터는 **클라이언트에 번들로 포함**되고, 서버도 같은 원천을 기동 시 자체 로드한다(런타임 배포·버전 협상 없음, [마스터 데이터 기획서](../세부/master-data/master-data-기획서.md)).

### 3.11 관리 API (랭킹 캐시 적재)

> 출처: [보스러시 기획서](../세부/boss-rush-기획서.md) 6.3

| 경로 | 기능 | 인증 | 요청 | 응답 주요 | 주요 상태 코드 |
|---|---|---|---|---|---|
| `POST /api/admin/boss-rush/rank/warmup` | **보스러시 랭킹 캐시 적재** — 진행 중 시즌의 메타 캐시를 갱신하고, 리더보드가 적재되지 않았으면(적재 완료 마커 부재) 비운 뒤 `boss_rush_record`를 페이지 단위로 읽어 ZADD로 재구축 | `X-Admin-Key` 헤더 | 쿼리 `?force=true`(선택, 마커가 있어도 다시 적재) | `status`, `seasonId`, `restored`, `members` | `200` 성공 · `401` 키 불일치 · `404` 관리 API 미설정 · `503` Redis 접근 불가 |

- **응답 형식이 게임 API와 다르다**: `{ success, message, data }`이며 `errorCode`가 없다. 게임 클라이언트가 부르는 API가 아니므로 `ErrorCode` 체계에 새 코드를 만들지 않았고, 판정은 HTTP 상태와 `data.status`로 한다.
- **`data.status` 4종**: `no-season`(진행 중 시즌 없음 — 적재 대상이 없다, 정상) · `already-warm`(**적재 완료 마커가 있어** 건너뜀) · `restored`(MySQL에서 재구축) · `cache-unavailable`(Redis 접근 실패 또는 부분 적재 — 유일한 실패값이며 HTTP 503으로 나간다).
- **이 API는 기동 절차용이고, 평상시 복구는 이것을 기다리지 않는다.** 호출 주체는 부트스트랩 스크립트(`python server_up_with_docker.py`)이며 컨테이너·서버 기동을 확인한 뒤 한 번 호출한다. 그 밖의 시점에는 **랭킹 조회가 미적재 리더보드를 감지해 스스로 재적재를 건다** — 조회는 그 요청에서 MySQL로 정답을 내려주고 적재는 백그라운드로 돈다. 게임 API는 scale-out으로 N대가 뜨므로 중복 재구축은 락(`rank:bossrush:{seasonId}:rebuilding`, `SET NX` TTL 2분)으로 막는다.
- **몇 번을 호출해도 안전하다**(멱등). 정본이 MySQL이고 재적재는 리더보드를 **비우고 다시 채우며**(ZADD만으로는 정본에서 사라진 멤버가 남아 순위를 밀어낸다) 점수는 기록에서 결정론적으로 계산되므로, 같은 상태에 다시 호출하면 같은 리더보드가 된다.
- **키가 설정돼 있지 않으면 404로 닫힌다**(설정 `Admin:ApiKey`, 환경변수 `Admin__ApiKey`). 설정을 빼먹은 서버에 무인증 관리 API가 열려 있는 상태를 만들지 않고, 경로의 존재 자체를 감춘다.
- **적재 로직(점수 인코딩·키 이름)은 서버 코드에만 둔다.** 스크립트가 MySQL·Redis에 직접 붙어 ZADD하면 인코딩 규칙이 두 언어에 복제돼 조용히 어긋나므로, 스크립트는 "적재하라"고 지시만 한다.

## 4. 에러 코드

전체 코드 목록·블록 규약(도메인 4.N → N000)은 [ErrorCode 통합 정의](error-code-정의.md) 참고. 공통 성공은 `0`, 인증 실패는 HTTP 401.

- **가챠(뽑기)만 규약의 예외다**: 도메인 4.11이지만 `11000`번대를 공통/시스템이 선점해 **12000번대**를 쓴다(`GachaNotFound(12001)`·`GachaPoolEmpty(12002)`·`GachaNotAvailable(12003)`).
- **보스러시도 같은 이유로 밀렸다**: 도메인 4.12이지만 `12000`번대를 가챠가 이미 쓰고 있어 **13000번대**를 쓴다(`BossRushLocked(13001)` ~ `BossRushSeasonClosed(13007)`). 이 중 `13002`·`13005`는 **폐기**됐고 번호를 재사용하지 않는다.

## 5. 출처 문서

- [계정/로그인 기획서](../세부/account-login-기획서.md)
- [세이브 데이터 기획서](../세부/save-data-기획서.md)
- [오프라인 보상 정산 기획서](../세부/offline-reward-기획서.md)
- [인벤토리/아이템/큐브 기획서](../세부/inventory-item-cube-기획서.md)
- [소모품 아이템 / 계정 버프 기획서](../세부/consumable-buff-기획서.md)
- [성장 시스템 기획서](../세부/growth-기획서.md)
- [스테이지/전투 결과 기획서](../세부/stage-battle-기획서.md)
- [거래소 / 교역선 기획서](../세부/trade-기획서.md)
- [메일 기획서](../세부/mail-기획서.md)
- [출석부 보상 시스템 기획서](../세부/attendance-기획서.md)
- [마스터 데이터 기획서](../세부/master-data/master-data-기획서.md)
- [가챠(뽑기) 시스템 기획서](../세부/gacha-기획서.md)
- [보스러시 / 랭킹 기획서](../세부/boss-rush-기획서.md)
- [ErrorCode 통합 정의](error-code-정의.md)
