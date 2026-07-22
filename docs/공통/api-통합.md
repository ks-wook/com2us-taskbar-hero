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
- [4. 에러 코드](#4-에러-코드)
- [5. 출처 문서](#5-출처-문서)

## 1. 공통 규약

- 모든 API는 **POST**. 응답은 `{ success, errorCode, message, data }` 형식이며 `success`는 `errorCode == 0`(`Success`)과 동치다.
- **인증**: 로그인 이후 요청은 body에 `{ userId, token, data }`를 담는다(헤더 미사용). 미들웨어가 `token`을 Redis `auth:token:{userId}`와 대조. **GameServer의 게임 API는 모두 인증이 필요**하므로 아래 GameServer 표(3장)에는 인증 칼럼을 두지 않는다. **무인증 예외**: AccountServer의 회원가입·로그인뿐이다(마스터 데이터는 클라이언트 번들이라 다운로드 API가 없다).
- `errorCode`는 `TaskbarHero.Common`의 `ErrorCode`이며 값 목록은 [ErrorCode 통합 정의](error-code-정의.md) 참고.
- **Base URL(개발)**: AccountServer `http://localhost:5160`, GameServer `http://localhost:5247`.

## 2. AccountServer API (`:5160`)

> 출처: [계정/로그인 기획서](../세부/account-login-기획서.md) 5장

| 경로 | 기능 | 인증 | 요청 `data`(또는 body) | 응답 주요 | 주요 에러 |
|---|---|---|---|---|---|
| `POST /api/auth/signup` | 계정 생성(회원가입) | 무인증 | `{ email, password, nickname }` | `userId` | `DuplicateEmail(1003)`, `InvalidRequest(1006)` |
| `POST /api/auth/login` | 로그인·인증 토큰 발급 | 무인증 | `{ email, password }` | `userId`, `token` | `UserNotFound(1001)`, `InvalidPassword(1002)` |
| `POST /api/auth/logout` | 로그아웃(토큰 무효화) | 인증 | `{}` | — | `InvalidToken(1004)`, `ExpiredToken(1005)` |

- 로그인은 단일 세션(토큰 UPSERT + Redis 덮어쓰기 → 기존 기기 자동 무효).

## 3. GameServer API (`:5247`)

### 3.1 세이브 / 진행

> 출처: [세이브 데이터 기획서](../세부/save-data-기획서.md) 5장

| 경로 | 기능 | 요청 `data` | 응답 주요 | 주요 에러 |
|---|---|---|---|---|
| `POST /api/game/load` | 접속 시 전체 세이브 스냅샷 로드 | `{}` | `player`, `characters[]`, `currencies[]`, `inventory[]`(장착 상태 포함), `skills[]`, `runes[]`, `cube`, `offlineElapsedSec` | (신규는 `{ isNew:true }`) |
| `POST /api/game/create-character` | 캐릭터 1개 생성(빈 슬롯 배정) | `{ nickname, classCode }` | `characterId`, `classCode`, `level` | `InvalidClassCode(2005)`, `InvalidCharacterId(2006)`, `PlayerAlreadyExists(2004)` |
| `POST /api/game/update-last-active` | 접속 시각 갱신(heartbeat, 오프라인 경과 기준) | `{}` | `lastActiveAt` | — |

- 캐릭터는 **한 번에 1개씩** 생성(`create-character`), 계정당 최대 3개·**직업 중복 불가**. `nickname`은 최초 생성 시에만 사용. 조회는 별도 API 없이 `load` 스냅샷 사용.

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
| `POST /api/game/inventory/equip` | 지정 캐릭터에 장비 장착(스왑) | `{ characterId, itemId }` | `equipped`, `unequipped` | `ItemNotFound(4001)`, `ItemNotEquippable(4003)`, `ItemEquipped(4007)`, `InvalidCharacterId(2006)` |
| `POST /api/game/inventory/unequip` | 지정 슬롯 장비 해제 | `{ characterId, slot }` | `slot`, `itemId` | `ItemNotFound(4001)`, `InvalidCharacterId(2006)` |
| `POST /api/game/inventory/enhance` ⚠️보류 | 장비 강화 단계 +1(재화 소모) | `{ itemId }` | `enhanceLevel`, `cost`, `balance` | `ItemNotFound(4001)`, `ItemNotEquippable(4003)`, `MaxEnhanceReached(4004)`, `InsufficientCurrency(4005)` |
| `POST /api/game/inventory/expand` | 인벤토리 용량 확장(골드 소모) | `{ count }` | `inventoryCapacity`, `cost`, `balance` | `InsufficientCurrency(4005)`, `InventoryCapacityMax(4008)` |
| `POST /api/game/inventory/move` | 인벤토리 배치 이동/교환(드래그 저장) | `{ itemId, toSlot }` | `moved`, `swapped` | `ItemNotFound(4001)`, `InvalidInventorySlot(4009)` |
| `POST /api/game/cube/combine` | 큐브 합성(동급 아이템→상위 등급) | `{ itemIds[] }` | `consumed`, `result`, `cube` | `CubeRecipeNotMet(4010)`, `CubeLevelInsufficient(4011)`, `ItemNotFound(4001)` |
| `POST /api/game/cube/dismantle` | 큐브 분해(아이템→골드 전환) | `{ items:[{itemId,count}] }` | `gold`, `cubeExp` | `ItemNotFound(4001)`, `InsufficientQuantity(4006)`, `ItemEquipped(4007)` |
| `POST /api/game/cube/craft` ⚠️보류 | 큐브 제작(레시피로 아이템 생성) | `{ recipeCode }` | `consumed`, `gained`, `cube` | `CubeRecipeNotMet(4010)`, `CubeLevelInsufficient(4011)`, `InsufficientCurrency(4005)` |
| `POST /api/game/box/open` | 랜덤 상자 열기(골드 가챠, 등급 확률 추첨→랜덤 아이템 지급). 현재 단발(`count`=1)만 처리, 다연속 예정 | `{ boxCode, count? }` | `rewards`, `gained`, `cost`, `balance` | `InsufficientCurrency(4005)`, `InvalidSaveData(2002)`, `InventoryFull(4002)`, `MasterDataNotLoaded(10001)` |

- 장비는 **캐릭터별**(장착 시 `characterId` 필수), 인벤토리·골드·큐브는 계정 공유. `inventory/enhance`(장비 강화)와 `cube/craft`(큐브 제작)는 **보류**(도입 확정 시 명세 확정).

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
| `POST /api/game/stage/clear` | 스테이지 클리어 → 보상 지급·진행도 갱신 | `{ act, difficulty, stage }` | `rewards`, `characters[]`(각 `isLevelUp`), `balance`, `progress` | `StageNotEntered(6003)`, `StageClearTooFast(6004)`, `InventoryFull(4002)` |

- 진입 응답은 스테이지의 **몬스터 구성(`monsters`)·보스(`boss`)** 를 포함. 클리어 보상(골드·경험치·전리품)은 서버가 마스터로 산출, 경험치는 3캐릭터 동일 지급(캐릭터별 **`isLevelUp`**), 골드는 계정. 이미 클리어한 스테이지는 재파밍 가능(보상은 프런티어와 **동일**). 전리품이 인벤토리 용량을 초과하면 지급하지 않고 클리어를 `InventoryFull(4002)`로 거부(전체 롤백).

### 3.6 거래소 / 교역선

> 출처: [거래소 / 교역선 기획서](../세부/trade-기획서.md) 5장

| 경로 | 기능 | 요청 `data` | 응답 주요 | 주요 에러 |
|---|---|---|---|---|
| `POST /api/game/trade/list` | 거래소 목록 조회(판매중, 아이템 코드 검색) | `{ itemCode?, page?, pageSize? }` | `listings[]`, `page`, `hasMore` | — |
| `POST /api/game/trade/register` | 판매 등록(에스크로) | `{ itemId, price }` | `listingId`, `itemCode`, `price` | `ItemNotFound(4001)`, `ItemEquipped(4007)`, `TradeNotSellable(7002)`, `TradePriceOutOfRange(7006)`, `TradeListingLimitExceeded(7007)`, `InvalidSaveData(2002)` |
| `POST /api/game/trade/buy` | 구매(골드→아이템, 대금 메일) | `{ listingId }` | `gained`, `cost`, `balance` | `TradeListingNotFound(7001)`, `TradeAlreadyClosed(7005)`, `TradeSelfPurchase(7004)`, `InsufficientCurrency(4005)`, `InventoryFull(4002)` |
| `POST /api/game/trade/cancel` | 판매 취소(아이템 복귀) | `{ listingId }` | `restored` | `TradeListingNotFound(7001)`, `TradeNotOwner(7003)`, `TradeAlreadyClosed(7005)`, `InventoryFull(4002)` |

- 등록은 아이템을 인벤토리에서 거래소 보관(에스크로)으로 이동. 구매 시 아이템은 구매자 인벤토리로 즉시, **판매 대금(수수료 차감)은 판매자에게 메일(3.7, `category=2` 거래)로** 지급. 동시 구매 경합은 `listing_id` 행 잠금으로 직렬화(복제·이중 판매 불가).

### 3.7 메일(보상)

> 출처: [메일 기획서](../세부/mail-기획서.md) 5장

| 경로 | 기능 | 요청 `data` | 응답 주요 | 주요 에러 |
|---|---|---|---|---|
| `POST /api/game/mail/list` | 우편함 목록 조회 | `{}` | `mails[]`(첨부·읽음·수령·만료 포함) | — |
| `POST /api/game/mail/claim` | 단건 메일 첨부 수령 | `{ mailId }` | `gained`, `balance` | `MailNotFound(8001)`, `MailAlreadyClaimed(8002)`, `MailExpired(8003)`, `InventoryFull(4002)` |
| `POST /api/game/mail/claim-all` | 수령 가능한 메일 일괄 수령 | `{}` | `claimedMailIds[]`, `gained`, `balance` | `InventoryFull(4002)` |

- 첨부(재화·아이템)는 서버가 지급하며 중복 수령 불가(수령 플래그+행 잠금). 만료 메일은 수령 거부. 발급은 거래소(4.7)·출석부(4.9)·운영이 담당.

### 3.8 출석부 보상

> 출처: [출석부 보상 시스템 기획서](../세부/attendance-기획서.md) 5장

| 경로 | 기능 | 요청 `data` | 응답 주요 | 주요 에러 |
|---|---|---|---|---|
| `POST /api/game/attendance/status` | 이번달 출석 현황 조회(읽기 전용) | `{}` | `yearMonth`, `today`, `todayClaimed`, `days[]` | `MasterDataNotLoaded(10001)` |
| `POST /api/game/attendance/claim` | 오늘자 출석 보상 획득(보상 **메일 발급**) | `{}` | `attendDate`, `day`, `reward`, `mailId` | `AttendanceAlreadyClaimed(9001)`, `MasterDataNotLoaded(10001)` |

- 날짜 경계는 서버 KST 자정, 하루 1회(`(user_id, attend_date)` 유니크). 획득 보상은 즉시 지급이 아니라 **메일(3.6)로 발급**되어 우편함 수령 시 계정 반영.

> **마스터(기획) 데이터 다운로드 API는 두지 않는다.** 본 프로젝트는 학습 목적이므로 마스터 데이터는 **클라이언트에 번들로 포함**되고, 서버도 같은 원천을 기동 시 자체 로드한다(런타임 배포·버전 협상 없음, [마스터 데이터 기획서](../세부/master-data/master-data-기획서.md)).

## 4. 에러 코드

전체 코드 목록·블록 규약(도메인 4.N → N000)은 [ErrorCode 통합 정의](error-code-정의.md) 참고. 공통 성공은 `0`, 인증 실패는 HTTP 401.

## 5. 출처 문서

- [계정/로그인 기획서](../세부/account-login-기획서.md)
- [세이브 데이터 기획서](../세부/save-data-기획서.md)
- [오프라인 보상 정산 기획서](../세부/offline-reward-기획서.md)
- [인벤토리/아이템/큐브 기획서](../세부/inventory-item-cube-기획서.md)
- [성장 시스템 기획서](../세부/growth-기획서.md)
- [거래소 / 교역선 기획서](../세부/trade-기획서.md)
- [메일 기획서](../세부/mail-기획서.md)
- [출석부 보상 시스템 기획서](../세부/attendance-기획서.md)
- [마스터 데이터 기획서](../세부/master-data/master-data-기획서.md)
- [ErrorCode 통합 정의](error-code-정의.md)
