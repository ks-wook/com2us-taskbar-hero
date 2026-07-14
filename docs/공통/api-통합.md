# API 통합 문서

> 지금까지 작성된 세부 기획서들의 **API 엔드포인트를 한곳에 모은 참조 문서**다. 요청/응답의 상세 스키마와 처리 규칙은 원 기획서(각 행의 "출처")가 **정본**이며, 본 문서는 전체 목록을 빠르게 보기 위한 집약본이다.

## 1. 공통 규약

- 모든 API는 **POST**. 응답은 `{ success, errorCode, message, data }` 형식이며 `success`는 `errorCode == 0`(`Success`)과 동치다.
- **인증**: 로그인 이후 요청은 body에 `{ userId, token, data }`를 담는다(헤더 미사용). 미들웨어가 `token`을 Redis `auth:token:{userId}`와 대조. **GameServer의 게임 API는 모두 인증이 필요**하므로 아래 GameServer 표(3장)에는 인증 칼럼을 두지 않는다. **무인증 예외**: AccountServer의 회원가입·로그인, 그리고 마스터 다운로드(`POST /api/master/download`, 로그인 이전 패치 단계에도 받아야 하므로).
- `errorCode`는 `TaskbarHero.Common`의 `GameErrorCode`이며 값 목록은 [GameErrorCode 통합 정의](error-code-정의.md) 참고.
- **Base URL(개발)**: AccountServer `http://localhost:5160`, GameServer `http://localhost:5247`.

## 2. AccountServer API (`:5160`)

> 출처: [계정/로그인 기획서](../세부/account-login-기획서.md) 5장

| 경로 | 인증 | 요청 `data`(또는 body) | 응답 주요 | 주요 에러 |
|---|---|---|---|---|
| `POST /api/auth/signup` | 무인증 | `{ email, password, nickname }` | `userId` | `DuplicateEmail(1003)`, `InvalidRequest(1006)` |
| `POST /api/auth/login` | 무인증 | `{ email, password }` | `userId`, `token` | `UserNotFound(1001)`, `InvalidPassword(1002)` |
| `POST /api/auth/logout` | 인증 | `{}` | — | `InvalidToken(1004)`, `ExpiredToken(1005)` |

- 로그인은 단일 세션(토큰 UPSERT + Redis 덮어쓰기 → 기존 기기 자동 무효).

## 3. GameServer API (`:5247`)

### 3.1 세이브 / 진행

> 출처: [세이브 데이터 기획서](../세부/save-data-기획서.md) 5장

| 경로 | 요청 `data` | 응답 주요 | 주요 에러 |
|---|---|---|---|
| `POST /api/game/load` | `{}` | `player`, `characters[]`, `currencies[]`, `inventory[]`, `equipment[]`, `growth[]`, `cube`, `offlineElapsedSec` | (신규는 `{ isNew:true }`) |
| `POST /api/game/save` | 변경분(`player`, `characters[]`, `currencies[]` …) | `savedAt`, `dataVersion` | `InvalidSaveData(2002)` |
| `POST /api/game/create-character` | `{ nickname, classCode }` | `characterId`, `classCode`, `level` | `InvalidClassCode(2005)`, `InvalidCharacterId(2006)`, `PlayerAlreadyExists(2004)` |
| `POST /api/game/heartbeat` | `{}` | `lastActiveAt` | — |

- 캐릭터는 **한 번에 1개씩** 생성(`create-character`), 계정당 최대 3개·**직업 중복 불가**. `nickname`은 최초 생성 시에만 사용. 조회는 별도 API 없이 `load` 스냅샷 사용.

### 3.2 오프라인 보상

> 출처: [오프라인 보상 정산 기획서](../세부/offline-reward-기획서.md) 5장

| 경로 | 요청 `data` | 응답 주요 | 주요 에러 |
|---|---|---|---|
| `POST /api/game/offline/claim` | `{}` | `offlineElapsedSec`, `effectiveSec`, `capped`, `rewards{gold,exp}`, `characters[]`, `lastActiveAt` | `NoOfflineReward(3001)`, `OfflineRewardAlreadyClaimed(3002)` |

- 경험치는 **3캐릭터 모두에게 동일** 지급, 골드는 계정. 아이템 미지급.

### 3.3 인벤토리 / 아이템 / 큐브

> 출처: [인벤토리/아이템/큐브 기획서](../세부/inventory-item-cube-기획서.md) 5장

| 경로 | 요청 `data` | 응답 주요 | 주요 에러 |
|---|---|---|---|
| `POST /api/game/inventory/equip` | `{ characterId, inventoryId }` | `equipped`, `unequipped` | `ItemNotFound(4001)`, `ItemNotEquippable(4003)`, `ItemEquipped(4007)`, `InvalidCharacterId(2006)` |
| `POST /api/game/inventory/unequip` | `{ characterId, slot }` | `slot`, `inventoryId` | `ItemNotFound(4001)`, `InvalidCharacterId(2006)` |
| `POST /api/game/inventory/enhance` | `{ inventoryId }` | `enhanceLevel`, `cost`, `balance` | `ItemNotFound(4001)`, `ItemNotEquippable(4003)`, `MaxEnhanceReached(4004)`, `InsufficientCurrency(4005)` |
| `POST /api/game/inventory/use` | `{ inventoryId, count }` | `consumed`, `gained` | `ItemNotFound(4001)`, `InsufficientQuantity(4006)`, `InventoryFull(4002)` |
| `POST /api/game/inventory/expand` | `{ count }` | `inventoryCapacity`, `cost`, `balance` | `InsufficientCurrency(4005)`, `InventoryCapacityMax(4008)` |
| `POST /api/game/inventory/move` | `{ inventoryId, toSlot }` | `moved`, `swapped` | `ItemNotFound(4001)`, `InvalidInventorySlot(4009)` |
| `POST /api/game/cube/combine` | `{ inventoryIds[] }` | `consumed`, `result`, `cube` | `CubeRecipeNotMet(4010)`, `CubeLevelInsufficient(4011)`, `ItemNotFound(4001)` |
| `POST /api/game/cube/dismantle` | `{ items:[{inventoryId,count}] }` | `gold`, `cubeExp` | `ItemNotFound(4001)`, `InsufficientQuantity(4006)`, `ItemEquipped(4007)` |
| `POST /api/game/cube/craft` ⚠️보류 | `{ recipeCode }` | `consumed`, `gained`, `cube` | `CubeRecipeNotMet(4010)`, `CubeLevelInsufficient(4011)`, `InsufficientCurrency(4005)` |

- 장비는 **캐릭터별**(장착 시 `characterId` 필수), 인벤토리·골드·큐브는 계정 공유. `cube/craft`는 우선순위 낮아 **보류**(도입 확정 시 명세 확정).

### 3.4 성장 (직업 / 스킬 / 룬)

> 출처: [성장 시스템 기획서](../세부/growth-기획서.md) 5장

| 경로 | 요청 `data` | 응답 주요 | 주요 에러 |
|---|---|---|---|
| `POST /api/game/growth/skill/levelup` | `{ characterId, skillCode }` | `skillCode`, `level`, `cost`, `skillPoint` | `InvalidGrowthTarget(5001)`, `SkillClassMismatch(5004)`, `SkillMaxLevel(5002)`, `InsufficientSkillPoint(5003)`, `InvalidCharacterId(2006)` |
| `POST /api/game/growth/skill/reset` | `{ characterId }` | `resetSkillCount`, `skillPoint` | `InvalidCharacterId(2006)` |
| `POST /api/game/growth/skill/equip` | `{ characterId, skillCodes[] }`(0~2) | `equipped[]` | `SkillNotActive(5005)`, `SkillNotLearned(5006)`, `ActiveSkillLimitExceeded(5007)`, `SkillClassMismatch(5004)`, `InvalidGrowthTarget(5001)`, `InvalidCharacterId(2006)` |
| `POST /api/game/growth/rune/upgrade` | `{ runeCode }` | `runeCode`, `level`, `cost`, `balance` | `InvalidGrowthTarget(5001)`, `RunePrereqNotMet(5010)`, `RuneMaxLevel(5011)`, `InsufficientCurrency(4005)` |

- 스킬 레벨업 1레벨당 1포인트, 스킬 초기화 무료, 액티브 스킬 캐릭터당 2개 장착. 룬은 계정 공용·1레벨씩·레벨 비례 골드.

### 3.5 마스터 데이터

> 출처: [마스터 데이터 기획서](../세부/master-data-기획서.md) 8장

| 경로 | 요청 `data` | 응답 주요 | 주요 에러 |
|---|---|---|---|
| `POST /api/master/download` | `{ clientVersion, tables?[] }` | `version`, `upToDate`, `tables{}` | `MasterDataNotLoaded(11001)`, `InvalidMasterRequest(11005)` |

- **이 API만 무인증**이다(GameServer의 다른 게임 API는 모두 인증 필요, 1장 규약). 로그인 이전 패치 단계에도 받아야 하기 때문.
- 클라이언트 캐시 버전과 서버 `master_data_version`이 **다를 때만** 데이터 반환(같으면 `upToDate:true`).

## 4. 에러 코드

전체 코드 목록·블록 규약(도메인 4.N → N000)은 [GameErrorCode 통합 정의](error-code-정의.md) 참고. 공통 성공은 `0`, 인증 실패는 HTTP 401.

## 5. 출처 문서

- [계정/로그인 기획서](../세부/account-login-기획서.md)
- [세이브 데이터 기획서](../세부/save-data-기획서.md)
- [오프라인 보상 정산 기획서](../세부/offline-reward-기획서.md)
- [인벤토리/아이템/큐브 기획서](../세부/inventory-item-cube-기획서.md)
- [성장 시스템 기획서](../세부/growth-기획서.md)
- [마스터 데이터 기획서](../세부/master-data-기획서.md)
- [GameErrorCode 통합 정의](error-code-정의.md)
