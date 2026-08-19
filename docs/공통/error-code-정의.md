# ErrorCode 통합 정의

> 상위 문서: [서버 시스템 전체 개요](../서버-시스템-전체-개요.md)
>
> 현재까지 작성된 기획서들에 등장한 **에러 코드를 한곳에 모은 참조 문서**다. 각 코드의 상세 맥락은 원 기획서를 따르며(아래 "출처"), 본 문서는 `TaskbarHero.Common`의 `ErrorCode` enum에 반영할 **단일 목록**을 제공한다. 서버-클라이언트가 공유하는 계약이므로 **숫자 값은 변경하지 않는다.**

## 목차

- [1. 규약](#1-규약)
- [2. 전체 코드 목록](#2-전체-코드-목록)
- [3. `TaskbarHero.Common/ErrorCode.cs` 반영안](#3-taskbarherocommonerrorcodecs-반영안)
- [4. 출처](#4-출처)


## 1. 규약

- 모든 API 응답은 `{ success, errorCode, message, data }` 형식이며, `errorCode`는 `ErrorCode` 값이다. `success`는 `errorCode == 0`(Success)과 동치이고, `message`는 코드 설명 문구다.
- **도메인별 1000번 블록 할당**: 코드 충돌을 막기 위해 도메인마다 별도 번호대를 사용한다. 신규 기획서가 코드를 추가하면 본 문서를 갱신한다.

| 블록 | 도메인 | 출처 기획서 | 상태 |
|---|---|---|---|
| 0 | 공통 성공 | — | 사용 중 |
| 1000번대 | 계정 / 인증 | [계정/로그인 기획서](../세부/account-login-기획서.md) 6장 | 사용 중 |
| 2000번대 | 세이브 데이터 | [세이브 데이터 기획서](../세부/save-data-기획서.md) 6장 | 사용 중 |
| 3000번대 | 오프라인 보상 정산 | [오프라인 보상 정산 기획서](../세부/offline-reward-기획서.md) 7장 | 사용 중 |
| 4000번대 | 인벤토리 / 아이템 / 큐브 / 소모품·버프 | [인벤토리/아이템/큐브 기획서](../세부/inventory-item-cube-기획서.md) 7장, [소모품/버프 기획서](../세부/consumable-buff-기획서.md) 7장 | 사용 중 |
| 5000번대 | 성장(직업 / 스킬 / 룬) | [성장 시스템 기획서](../세부/growth-기획서.md) 7장 | 사용 중 |
| 6000번대 | 스테이지 / 전투 결과 | [스테이지/전투 결과 기획서](../세부/stage-battle-기획서.md) 7장 | 사용 중 |
| 7000번대 | 거래소 / 교역선 | [거래소 / 교역선 기획서](../세부/trade-기획서.md) 8장 | 사용 중 |
| 8000번대 | 메일(보상) | [메일 기획서](../세부/mail-기획서.md) 7장 | 사용 중 |
| 9000번대 | 출석부 보상 | [출석부 보상 시스템 기획서](../세부/attendance-기획서.md) 7장 | 사용 중 |
| 10000번대 | 마스터 데이터 | [마스터 데이터 기획서](../세부/master-data/master-data-기획서.md) 8 | 사용 중 |
| 11000번대 | 공통 / 시스템 | [서버 로깅 규칙](로깅-규칙.md) 6장(전역 예외 처리) | 사용 중 |
| 12000번대 | 가챠(뽑기) | [가챠 시스템 기획서](../세부/gacha-기획서.md) 7장 | 사용 중 |
| 13000번대 | 보스러시 / 랭킹 | [보스러시 / 랭킹 기획서](../세부/boss-rush-기획서.md) 7장 | 사용 중 |

## 2. 전체 코드 목록

### 2.1 공통

| 이름 | 값 | 의미 |
|---|---|---|
| Success | 0 | 성공 |

### 2.2 계정 / 인증 (1000번대)

| 이름 | 값 | 의미 |
|---|---|---|
| UserNotFound | 1001 | 계정 없음 |
| InvalidPassword | 1002 | 비밀번호 불일치 |
| DuplicateEmail | 1003 | 이메일(로그인 ID) 중복 |
| InvalidToken | 1004 | 토큰 무효(서명/형식 오류) |
| ExpiredToken | 1005 | 토큰 만료 또는 폐기됨 |
| InvalidRequest | 1006 | 요청 파라미터 오류(형식/길이) |

### 2.3 세이브 데이터 (2000번대)

| 이름 | 값 | 의미 |
|---|---|---|
| SaveNotFound | 2001 | 세이브 데이터 없음 |
| InvalidSaveData | 2002 | 액션 요청 값 검증 실패(불가능한 값·비정상 데이터) |
| PlayerAlreadyExists | 2004 | 더 생성할 수 있는 캐릭터가 없음(보유 직업이 전 직업을 채움) |
| InvalidClassCode | 2005 | 존재하지 않는 직업 코드 |
| InvalidCharacterId | 2006 | 잘못된 캐릭터 식별자(존재하지 않는 characterId·이미 보유한 직업 중복) |
| InvalidGender | 2007 | 정의되지 않은 성별 값(1:남 2:여 외) |
| CannotRemoveLastCharacter | 2008 | 파티를 비울 수 없음(편성 저장 목록이 비어 있음) |
| CharacterNotFound | 2009 | 편성 목록에 보유하지 않은 캐릭터가 있음 |
| PartySlotOccupied | 2010 | 파티 자리 지정 오류(정원 초과·범위 밖·같은 자리 중복) |

- `2003`(구 `SaveVersionMismatch`)은 세이브 스키마 버전(`data_version`) 제거로 폐기했다. 결번으로 두고 재사용하지 않는다.

### 2.4 오프라인 보상 정산 (3000번대)

| 이름 | 값 | 의미 |
|---|---|---|
| NoOfflineReward | 3001 | 정산할 오프라인 경과가 최소 기준 미만 |
| OfflineRewardAlreadyClaimed | 3002 | 이미 정산됨(동시 중복 요청) |

### 2.5 인벤토리 / 아이템 / 큐브 (4000번대)

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
| ItemNotConsumable | 4020 | 소모품이 아닌 아이템에 사용을 시도(`item_type≠4`) |
| BuffDurationLimitExceeded | 4021 | 버프 누적 지속시간이 상한(24시간)을 초과 |

- 블록 내 할당: `4001~4009` 인벤토리/아이템, `4010~4019` 큐브, `4020~4029` **소모품/버프**([소모품/버프 기획서](../세부/consumable-buff-기획서.md) 7장).
- `4012`(구 `InventoryRevisionChanged`)는 가방 페이지 조회(`POST /api/game/inventory/list`)의 페이지 간 정합성 검증 장치를 제거하며 **폐기**했다. 결번으로 두고 재사용하지 않는다.
- 소모품 사용은 위 2개 외에 신규 코드를 만들지 않고 `ItemNotFound(4001)`·`InsufficientQuantity(4006)`·`MasterDataNotLoaded(10001)`·`SaveNotFound(2001)`을 재사용한다.
- **가챠(뽑기)** 는 별도 도메인(4.11)이라 자체 블록(12000번대)을 쓰되, 비용 부족·지급 적재 실패는 이 블록의 `InsufficientCurrency(4005)`·`InventoryFull(4002)`를 재사용한다([가챠 기획서](../세부/gacha-기획서.md) 7장).

### 2.6 성장 (5000번대)

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

### 2.7 스테이지 / 전투 결과 (6000번대)

| 이름 | 값 | 의미 |
|---|---|---|
| StageNotFound | 6001 | 스테이지가 마스터에 없음 |
| StageLocked | 6002 | 아직 도달하지 못한 스테이지(스킵 진입 불가) |
| StageNotEntered | 6003 | 진입하지 않았거나 현재 진입 스테이지와 불일치 |

- `6004`는 결번이다. 결번으로 두고 재사용하지 않는다.
- 전리품 인벤토리 초과는 **에러가 아니다.** 전리품만 폐기하고 골드·경험치를 지급한 뒤 클리어를 성공 처리하므로, 스테이지 클리어에서 `InventoryFull(4002)`는 반환되지 않는다.

### 2.8 거래소 / 교역선 (7000번대)

| 이름 | 값 | 의미 |
|---|---|---|
| TradeListingNotFound | 7001 | 거래 등록이 없거나 접근 불가 |
| TradeNotSellable | 7002 | 판매 불가 아이템(`sellable=0`) |
| TradeNotOwner | 7003 | 본인 등록이 아님(취소 불가) |
| TradeSelfPurchase | 7004 | 자기 등록은 구매 불가 |
| TradeAlreadyClosed | 7005 | 이미 판매/취소된 등록 · **만료 시각이 지난 등록**(배치 정리 전 포함) |
| TradePriceOutOfRange | 7006 | 등록 가격이 기준가 ±20% 범위 밖 |
| TradeListingLimitExceeded | 7007 | 계정 동시 등록 한도(10개) 초과 |

- 등록 아이템 없음·장착 중은 `ItemNotFound(4001)`·`ItemEquipped(4007)`, 구매 골드 부족은 `InsufficientCurrency(4005)`, 아이템 지급 용량 초과는 `InventoryFull(4002)`를 재사용한다.
- **`7008`(구 `TradeBusy`)은 결번이다.** 판매 등록의 Redis 판매자 락을 제거하며 폐기했다([거래소 기획서](../세부/trade-기획서.md) 7.4). 재사용하지 않는다. 동시 구매에서 진 요청은 조건부 갱신 0행으로 `TradeAlreadyClosed(7005)`를, 같은 아이템을 겹쳐 등록하려다 진 요청은 에스크로 행 잠금으로 `ItemNotFound(4001)`를 받는다.

### 2.9 메일(보상) (8000번대)

| 이름 | 값 | 의미 |
|---|---|---|
| MailNotFound | 8001 | 메일이 없거나 본인 메일이 아님 |
| MailAlreadyClaimed | 8002 | 이미 첨부를 수령한 메일 |
| MailExpired | 8003 | 만료되어 수령 불가한 메일 |

- 첨부 아이템 인벤토리 초과는 신규 코드 없이 `InventoryFull(4002)`를 재사용한다.

### 2.10 출석부 보상 (9000번대)

| 이름 | 값 | 의미 |
|---|---|---|
| AttendanceAlreadyClaimed | 9001 | 오늘자 출석 보상을 이미 수령함 |

- `9002`(구 `AttendanceAllClaimed`)는 출석 사다리가 **30일차 이후 1일차부터 순환**하도록 바뀌어 소진 실패가 사라지면서 **폐기**했다. 결번으로 두고 재사용하지 않는다.
- 마스터 미로드/미정의는 신규 코드 없이 `MasterDataNotLoaded(10001)`를 재사용한다.

### 2.11 마스터 데이터 (10000번대)

| 이름 | 값 | 의미 |
|---|---|---|
| MasterDataNotLoaded | 10001 | 서버 기동 시 마스터 데이터가 로드되지 않음(자체 로드 실패) |

- `10002`(구 `MasterDataVersionMismatch`)·`10005`(구 `InvalidMasterRequest`)는 마스터 데이터를 **클라이언트 번들**로 전환하며 다운로드 API·런타임 버전 협상을 제거함에 따라 **폐기**했다. 결번으로 두고 재사용하지 않는다.

### 2.12 공통 / 시스템 (11000번대)

| 이름 | 값 | 의미 |
|---|---|---|
| ServerError | 11001 | 특정 도메인에 속하지 않는 서버 내부 오류. 전역 예외 처리기가 미처리 예외를 이 코드로 일반화(내부 정보 미노출)해 HTTP 500으로 응답한다. |

### 2.13 가챠(뽑기) (12000번대)

| 이름 | 값 | 의미 |
|---|---|---|
| GachaNotFound | 12001 | 마스터에 없는 `gachaCode` |
| GachaPoolEmpty | 12002 | 추첨된 등급 슬롯에 지급 후보가 없음(마스터 데이터 결함, 전체 롤백) |
| GachaNotAvailable | 12003 | 배너가 지금 열려 있지 않음(비노출·기간 밖). 비용 차감 전에 거부 |

- 가챠는 **도메인 4.11**이지만 블록 규약(도메인 4.N → N000)의 `11000`번대를 공통/시스템이 선점하고 있어 **12000번대**를 할당했다.
- 비용 골드 부족은 `InsufficientCurrency(4005)`, 지급 아이템 적재 실패는 `InventoryFull(4002)`, 마스터 미로드는 `MasterDataNotLoaded(10001)`를 재사용한다.
- **`GachaNotFound`(없는 코드)와 `GachaNotAvailable`(닫힌 배너)을 구분한다.** 전자는 클라이언트 번들과 서버 마스터가 어긋난 상황(갱신 안내), 후자는 정상적인 배너 개폐(목록 재조회)라 클라이언트 대응이 다르다.
- **배너 조회(`gacha/banners`)·뽑기 기록 조회(`gacha/history`)는 전용 코드를 두지 않는다** — 열려 있는 배너가 없거나 기록이 없으면 빈 목록으로 성공 응답하고 `cursor`·`limit`은 서버가 보정한다.

### 2.14 보스러시 / 랭킹 (13000번대)

| 이름 | 값 | 의미 |
|---|---|---|
| BossRushLocked | 13001 | 해금 조건 미달(`max_stage_cleared < unlock_stage_sequence`) |
| BossRushDailyLimitExceeded | 13002 | 오늘 도전 횟수를 모두 사용함 |
| BossRushRunNotFound | 13003 | 그 `runId`의 런이 없거나 본인 런이 아님 |
| BossRushRunAlreadyFinished | 13004 | 이미 종결된 런(중복 보고 · 동시 요청의 패자 · 만료된 런) |
| BossRushTimeout | 13005 | 보고된 `clearMs`가 제한 시간(10분)을 넘음 |
| BossRushInvalidProgress | 13006 | 라운드 보고가 형식·자기정합성 검증에 실패(누락·중복·합계가 `clearMs`와 불일치) |
| BossRushSeasonClosed | 13007 | 진행 중 시즌이 없음(시즌 정산 중) 또는 존재하지 않는 `seasonId` |

- 보스러시는 **도메인 4.12**이지만 블록 규약(도메인 4.N → N000)의 `12000`번대를 가챠가 이미 쓰고 있어(가챠는 4.11이나 `11000`번대를 공통/시스템이 선점해 12000으로 밀렸다) **13000번대**를 할당했다.
- 재사용: 세이브 없음은 `SaveNotFound(2001)`, 요청 필드 형식 오류는 `InvalidRequest(1006)`, 마스터 미로드는 `MasterDataNotLoaded(10001)`.
- **보스러시는 아이템을 지급하지도 재화를 소모하지도 않는다** — 클리어 보상이 골드·경험치뿐이라 `InventoryFull(4002)`·`InsufficientCurrency(4005)`가 발생하지 않는다. 아이템은 **시즌 순위 보상 메일**로만 지급되므로, 그 수령 단계의 오류는 메일 도메인 코드(`MailNotFound(8001)`·`MailAlreadyClaimed(8002)`·`MailExpired(8003)`·`InventoryFull(4002)`)를 따른다.
- **`BossRushTimeout(13005)`과 `BossRushInvalidProgress(13006)`은 대응이 다르다.** 전자는 정상적인 실패(제한 시간을 넘겼으니 다시 도전 안내), 후자는 **클라이언트 버그**(보고값이 내부적으로 앞뒤가 안 맞음)이므로 재시도 안내 없이 오류를 표시하고 서버는 Warning 로그를 남긴다.

## 3. `TaskbarHero.Common/ErrorCode.cs` 반영안

아래 전체 목록이 `TaskbarHero.Common/ErrorCode.cs`에 구현되어 있다(`netstandard2.0`, 서버-클라이언트 공유). 코드를 추가/폐기할 때는 이 문서와 해당 파일을 함께 갱신한다.

```csharp
// TaskbarHero.Common/ErrorCode.cs
namespace TaskbarHero.Common
{
    // 서버-클라이언트 공통 에러 코드
    // 숫자 값은 클라이언트와의 계약이므로 변경 금지
    public enum ErrorCode
    {
        Success = 0,

        // 계정 / 인증 (1000번대)
        UserNotFound = 1001,
        InvalidPassword = 1002,
        DuplicateEmail = 1003,
        InvalidToken = 1004,
        ExpiredToken = 1005,
        InvalidRequest = 1006,

        // 세이브 데이터 (2000번대)
        SaveNotFound = 2001,
        InvalidSaveData = 2002,
        // 2003: 구 SaveVersionMismatch — data_version 제거로 폐기(결번, 재사용 금지)
        PlayerAlreadyExists = 2004,
        InvalidClassCode = 2005,
        InvalidCharacterId = 2006,
        InvalidGender = 2007,
        CannotRemoveLastCharacter = 2008,
        CharacterNotFound = 2009,
        PartySlotOccupied = 2010,

        // 오프라인 보상 정산 (3000번대)
        NoOfflineReward = 3001,
        OfflineRewardAlreadyClaimed = 3002,

        // 인벤토리 / 아이템 / 큐브 (4000번대)
        ItemNotFound = 4001,
        InventoryFull = 4002,
        ItemNotEquippable = 4003,
        MaxEnhanceReached = 4004,
        InsufficientCurrency = 4005,
        InsufficientQuantity = 4006,
        ItemEquipped = 4007,
        InventoryCapacityMax = 4008,
        InvalidInventorySlot = 4009,
        CubeRecipeNotMet = 4010,
        CubeLevelInsufficient = 4011,
        // 4012(구 InventoryRevisionChanged): 가방 페이지 조회의 정합성 검증 장치 제거로 폐기(결번)

        // 소모품 / 버프 (4020~4029)
        ItemNotConsumable = 4020,
        BuffDurationLimitExceeded = 4021,

        // 성장 (5000번대)
        InvalidGrowthTarget = 5001,
        SkillMaxLevel = 5002,
        InsufficientSkillPoint = 5003,
        SkillClassMismatch = 5004,
        SkillNotActive = 5005,
        SkillNotLearned = 5006,
        ActiveSkillLimitExceeded = 5007,
        RunePrereqNotMet = 5010,
        RuneMaxLevel = 5011,

        // 스테이지 / 전투 결과 (6000번대)
        StageNotFound = 6001,
        StageLocked = 6002,
        StageNotEntered = 6003,
        // 6004: 결번(재사용 금지)

        // 거래소 / 교역선 (7000번대)
        TradeListingNotFound = 7001,
        TradeNotSellable = 7002,
        TradeNotOwner = 7003,
        TradeSelfPurchase = 7004,
        TradeAlreadyClosed = 7005,
        TradePriceOutOfRange = 7006,
        TradeListingLimitExceeded = 7007,
        // 7008(구 TradeBusy): 판매 등록의 Redis 판매자 락 제거로 폐기(결번, 재사용 금지)

        // 메일(보상) (8000번대)
        MailNotFound = 8001,
        MailAlreadyClaimed = 8002,
        MailExpired = 8003,

        // 출석부 보상 (9000번대)
        AttendanceAlreadyClaimed = 9001,
        // 9002(구 AttendanceAllClaimed): 30일차 이후 1일차부터 순환하도록 바뀌어 사다리 소진 실패가 없어짐(결번)

        // 마스터 데이터 (10000번대)
        MasterDataNotLoaded = 10001,
        // 10002(구 MasterDataVersionMismatch)·10005(구 InvalidMasterRequest): 마스터 클라 번들 전환으로 폐기(결번)

        // 공통 / 시스템 (11000번대)
        ServerError = 11001,

        // 가챠(뽑기) (12000번대 — 도메인 4.11이나 11000번대를 공통/시스템이 선점해 12000번대 할당)
        GachaNotFound = 12001,
        GachaPoolEmpty = 12002,
        GachaNotAvailable = 12003,

        // 보스러시 / 랭킹 (13000번대 — 도메인 4.12이나 12000번대를 가챠가 선점해 13000번대 할당)
        BossRushLocked = 13001,
        BossRushDailyLimitExceeded = 13002,
        BossRushRunNotFound = 13003,
        BossRushRunAlreadyFinished = 13004,
        BossRushTimeout = 13005,
        BossRushInvalidProgress = 13006,
        BossRushSeasonClosed = 13007,
    }
}
```

## 4. 출처

- [계정/로그인 기획서](../세부/account-login-기획서.md) — 6장 에러 코드 (1000번대)
- [세이브 데이터 기획서](../세부/save-data-기획서.md) — 6장 에러 코드 (2000번대)
- [오프라인 보상 정산 기획서](../세부/offline-reward-기획서.md) — 7장 에러 코드 (3000번대)
- [인벤토리/아이템/큐브 기획서](../세부/inventory-item-cube-기획서.md) — 7장 에러 코드 (4001~4019)
- [소모품 아이템 / 계정 버프 기획서](../세부/consumable-buff-기획서.md) — 7장 에러 코드 (4020~4029)
- [성장 시스템 기획서](../세부/growth-기획서.md) — 7장 에러 코드 (5000번대)
- [스테이지/전투 결과 기획서](../세부/stage-battle-기획서.md) — 7장 에러 코드 (6000번대)
- [거래소 / 교역선 기획서](../세부/trade-기획서.md) — 8장 에러 코드 (7000번대), 7장 성능 설계(Redis 캐시·경합 제어)
- [메일 기획서](../세부/mail-기획서.md) — 7장 에러 코드 (8000번대)
- [출석부 보상 시스템 기획서](../세부/attendance-기획서.md) — 7장 에러 코드 (9000번대)
- [마스터 데이터 기획서](../세부/master-data/master-data-기획서.md) — 8장 에러 코드 (10000번대)
- [가챠(뽑기) 시스템 기획서](../세부/gacha-기획서.md) — 7장 에러 코드 (12000번대)
- [보스러시 / 랭킹 기획서](../세부/boss-rush-기획서.md) — 7장 에러 코드 (13000번대)
