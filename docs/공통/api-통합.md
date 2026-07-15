# API 통합 문서

> 지금까지 작성된 세부 기획서들의 **API 엔드포인트를 한곳에 모은 참조 문서**다. 요청/응답의 상세 스키마와 처리 규칙은 원 기획서(각 행의 "출처")가 **정본**이며, 본 문서는 전체 목록을 빠르게 보기 위한 집약본이다.

## 1. 공통 규약

- 모든 API는 **POST**. 응답은 `{ success, errorCode, message, data }` 형식이며 `success`는 `errorCode == 0`(`Success`)과 동치다.
- **인증**: 로그인 이후 요청은 body에 `{ userId, token, data }`를 담는다(헤더 미사용). 미들웨어가 `token`을 Redis `auth:token:{userId}`와 대조. **GameServer의 게임 API는 모두 인증이 필요**하므로 아래 GameServer 표(3장)에는 인증 칼럼을 두지 않는다. **무인증 예외**: AccountServer의 회원가입·로그인, 그리고 마스터 다운로드(`POST /api/master/download`, 로그인 이전 패치 단계에도 받아야 하므로).
- `errorCode`는 `TaskbarHero.Common`의 `GameErrorCode`이며 값 목록은 [GameErrorCode 통합 정의](error-code-정의.md) 참고.
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
| `POST /api/game/heartbeat` | 접속 시각 갱신(오프라인 경과 기준) | `{}` | `lastActiveAt` | — |

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
| `POST /api/game/inventory/enhance` | 장비 강화 단계 +1(재화 소모) | `{ itemId }` | `enhanceLevel`, `cost`, `balance` | `ItemNotFound(4001)`, `ItemNotEquippable(4003)`, `MaxEnhanceReached(4004)`, `InsufficientCurrency(4005)` |
| `POST /api/game/inventory/use` | 소모품 사용(서버 산출 지급) | `{ itemId, count }` | `consumed`, `gained` | `ItemNotFound(4001)`, `InsufficientQuantity(4006)`, `InventoryFull(4002)` |
| `POST /api/game/inventory/expand` | 인벤토리 용량 확장(골드 소모) | `{ count }` | `inventoryCapacity`, `cost`, `balance` | `InsufficientCurrency(4005)`, `InventoryCapacityMax(4008)` |
| `POST /api/game/inventory/move` | 인벤토리 배치 이동/교환(드래그 저장) | `{ itemId, toSlot }` | `moved`, `swapped` | `ItemNotFound(4001)`, `InvalidInventorySlot(4009)` |
| `POST /api/game/cube/combine` | 큐브 합성(동급 아이템→상위 등급) | `{ itemIds[] }` | `consumed`, `result`, `cube` | `CubeRecipeNotMet(4010)`, `CubeLevelInsufficient(4011)`, `ItemNotFound(4001)` |
| `POST /api/game/cube/dismantle` | 큐브 분해(아이템→골드 전환) | `{ items:[{itemId,count}] }` | `gold`, `cubeExp` | `ItemNotFound(4001)`, `InsufficientQuantity(4006)`, `ItemEquipped(4007)` |
| `POST /api/game/cube/craft` ⚠️보류 | 큐브 제작(레시피로 아이템 생성) | `{ recipeCode }` | `consumed`, `gained`, `cube` | `CubeRecipeNotMet(4010)`, `CubeLevelInsufficient(4011)`, `InsufficientCurrency(4005)` |
| `POST /api/game/box/open` | 랜덤 상자 열기(골드 가챠, 등급 확률 추첨→랜덤 아이템 지급). 현재 단발(`count`=1)만 처리, 다연속 예정 | `{ boxCode, count? }` | `rewards`, `gained`, `cost`, `balance` | `InsufficientCurrency(4005)`, `InvalidSaveData(2002)`, `InventoryFull(4002)`, `MasterDataNotLoaded(11001)` |

- 장비는 **캐릭터별**(장착 시 `characterId` 필수), 인벤토리·골드·큐브는 계정 공유. `cube/craft`는 우선순위 낮아 **보류**(도입 확정 시 명세 확정).

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

- 진입 응답은 스테이지의 **몬스터 구성(`monsters`)·보스(`boss`)** 를 포함. 클리어 보상(골드·경험치·전리품)은 서버가 마스터로 산출, 경험치는 3캐릭터 동일 지급(캐릭터별 **`isLevelUp`**), 골드는 계정. 이미 클리어한 스테이지는 재파밍 가능.

### 3.6 메일(보상)

> 출처: [메일 기획서](../세부/mail-기획서.md) 5장

| 경로 | 기능 | 요청 `data` | 응답 주요 | 주요 에러 |
|---|---|---|---|---|
| `POST /api/game/mail/list` | 우편함 목록 조회 | `{}` | `mails[]`(첨부·읽음·수령·만료 포함) | — |
| `POST /api/game/mail/claim` | 단건 메일 첨부 수령 | `{ mailId }` | `gained`, `balance` | `MailNotFound(9001)`, `MailAlreadyClaimed(9002)`, `MailExpired(9003)`, `InventoryFull(4002)` |
| `POST /api/game/mail/claim-all` | 수령 가능한 메일 일괄 수령 | `{}` | `claimedMailIds[]`, `gained`, `balance` | `InventoryFull(4002)` |

- 첨부(재화·아이템)는 서버가 지급하며 중복 수령 불가(수령 플래그+행 잠금). 만료 메일은 수령 거부. 발급은 거래소(4.8)·출석부(4.10)·운영이 담당.

### 3.7 출석부 보상

> 출처: [출석부 보상 시스템 기획서](../세부/attendance-기획서.md) 5장

| 경로 | 기능 | 요청 `data` | 응답 주요 | 주요 에러 |
|---|---|---|---|---|
| `POST /api/game/attendance/status` | 이번달 출석 현황 조회(읽기 전용) | `{}` | `yearMonth`, `today`, `todayClaimed`, `days[]` | `MasterDataNotLoaded(11001)` |
| `POST /api/game/attendance/claim` | 오늘자 출석 보상 획득(보상 **메일 발급**) | `{}` | `attendDate`, `day`, `reward`, `mailId` | `AttendanceAlreadyClaimed(10001)`, `MasterDataNotLoaded(11001)` |

- 날짜 경계는 서버 KST 자정, 하루 1회(`(user_id, attend_date)` 유니크). 획득 보상은 즉시 지급이 아니라 **메일(3.6)로 발급**되어 우편함 수령 시 계정 반영.

### 3.8 마스터 데이터

> 출처: [마스터 데이터 기획서](../세부/master-data-기획서.md) 8장

| 경로 | 기능 | 요청 `data` | 응답 주요 | 주요 에러 |
|---|---|---|---|---|
| `POST /api/master/download` | 마스터 데이터 다운로드(버전 상이 시만) | `{ clientVersion, tables?[] }` | `version`, `upToDate`, `tables{}` | `MasterDataNotLoaded(11001)`, `InvalidMasterRequest(11005)` |

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
- [메일 기획서](../세부/mail-기획서.md)
- [출석부 보상 시스템 기획서](../세부/attendance-기획서.md)
- [마스터 데이터 기획서](../세부/master-data-기획서.md)
- [GameErrorCode 통합 정의](error-code-정의.md)
