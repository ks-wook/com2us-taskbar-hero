# GameErrorCode 통합 정의

> 상위 문서: [서버 시스템 전체 개요](서버-시스템-전체-개요.md)
>
> 현재까지 작성된 기획서들에 등장한 **에러 코드를 한곳에 모은 참조 문서**다. 각 코드의 상세 맥락은 원 기획서를 따르며(아래 "출처"), 본 문서는 `TaskbarHero.Common`의 `GameErrorCode` enum에 반영할 **단일 목록**을 제공한다. 서버-클라이언트가 공유하는 계약이므로 **숫자 값은 변경하지 않는다.**

## 1. 규약

- 모든 API 응답은 `{ success, errorCode, message, data }` 형식이며, `errorCode`는 `GameErrorCode` 값이다. `success`는 `errorCode == 0`(Success)과 동치이고, `message`는 코드 설명 문구다.
- **도메인별 1000번 블록 할당**: 코드 충돌을 막기 위해 도메인마다 별도 번호대를 사용한다. 신규 기획서가 코드를 추가하면 본 문서를 갱신한다.

| 블록 | 도메인 | 출처 기획서 | 상태 |
|---|---|---|---|
| 0 | 공통 성공 | — | 사용 중 |
| 1000번대 | 계정 / 인증 | [계정/로그인 기획서](../세부/account-login-기획서.md) 6장 | 사용 중 |
| 2000번대 | 세이브 데이터 | [세이브 데이터 기획서](../세부/save-data-기획서.md) 6장 | 사용 중 |
| 3000번대 | 오프라인 보상 정산 | [오프라인 보상 정산 기획서](../세부/offline-reward-기획서.md) 7장 | 사용 중 |
| 4000번대 | 인벤토리 / 아이템 / 큐브 | [인벤토리/아이템/큐브 기획서](../세부/inventory-item-cube-기획서.md) 7장 | 사용 중 |
| 5000번대 | 성장(직업 / 스킬 / 룬) | [성장 시스템 기획서](../세부/growth-기획서.md) 7장 | 사용 중 |
| 6000번대 | 스테이지 / 전투 결과 | [스테이지/전투 결과 기획서](../세부/stage-battle-기획서.md) 7장 | 사용 중 |
| 8000번대 | 거래소 / 교역선 | [거래소 / 교역선 기획서](../세부/trade-기획서.md) 7장 | 사용 중 |
| 9000번대 | 메일(보상) | [메일 기획서](../세부/mail-기획서.md) 7장 | 사용 중 |
| 10000번대 | 출석부 보상 | [출석부 보상 시스템 기획서](../세부/attendance-기획서.md) 7장 | 사용 중 |
| 11000번대 | 마스터 데이터 | [마스터 데이터 기획서](../세부/master-data-기획서.md) 8.3 | 사용 중 |
| 7000번대 | 재화·상점(예약) | (미작성) | 예약 |

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
| PlayerAlreadyExists | 2004 | 캐릭터 슬롯 3개가 모두 차 더 생성 불가 |
| InvalidClassCode | 2005 | 존재하지 않는 직업 코드 |
| InvalidCharacterId | 2006 | 잘못된 캐릭터 슬롯(존재하지 않는 characterId·슬롯 개수 오류·직업 중복) |

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
| InsufficientQuantity | 4006 | 소모/재료 수량 부족 |
| ItemEquipped | 4007 | 장착 중이라 분해 불가 |
| InventoryCapacityMax | 4008 | 인벤토리 용량이 최대치에 도달(확장 불가) |
| InvalidInventorySlot | 4009 | 인벤토리 칸(slot) 번호가 잘못됨(용량 범위 밖 등) |
| CubeRecipeNotMet | 4010 | 큐브 합성/제작 조건(등급·개수·재료) 미충족 |
| CubeLevelInsufficient | 4011 | 큐브 레벨이 해당 연산 요구치 미만 |

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
| StageClearTooFast | 6004 | 최소 소요 시간 미충족(플레이 타당성 검증) |

- 전리품 인벤토리 초과는 신규 코드 없이 `InventoryFull(4002)`를 재사용한다.

### 2.8 거래소 / 교역선 (8000번대)

| 이름 | 값 | 의미 |
|---|---|---|
| TradeListingNotFound | 8001 | 거래 등록이 없거나 접근 불가 |
| TradeNotSellable | 8002 | 판매 불가 아이템(`sellable=0`) |
| TradeNotOwner | 8003 | 본인 등록이 아님(취소 불가) |
| TradeSelfPurchase | 8004 | 자기 등록은 구매 불가 |
| TradeAlreadyClosed | 8005 | 이미 판매/취소된 등록 |
| TradePriceOutOfRange | 8006 | 등록 가격이 기준가 ±20% 범위 밖 |
| TradeListingLimitExceeded | 8007 | 계정 동시 등록 한도(10개) 초과 |

- 등록 아이템 없음·장착 중은 `ItemNotFound(4001)`·`ItemEquipped(4007)`, 구매 골드 부족은 `InsufficientCurrency(4005)`, 아이템 지급 용량 초과는 `InventoryFull(4002)`를 재사용한다.

### 2.9 메일(보상) (9000번대)

| 이름 | 값 | 의미 |
|---|---|---|
| MailNotFound | 9001 | 메일이 없거나 본인 메일이 아님 |
| MailAlreadyClaimed | 9002 | 이미 첨부를 수령한 메일 |
| MailExpired | 9003 | 만료되어 수령 불가한 메일 |

- 첨부 아이템 인벤토리 초과는 신규 코드 없이 `InventoryFull(4002)`를 재사용한다.

### 2.10 출석부 보상 (10000번대)

| 이름 | 값 | 의미 |
|---|---|---|
| AttendanceAlreadyClaimed | 10001 | 오늘자 출석 보상을 이미 수령함 |

- 마스터 미로드/미정의는 신규 코드 없이 `MasterDataNotLoaded(11001)`를 재사용한다.

### 2.11 마스터 데이터 (11000번대)

| 이름 | 값 | 의미 |
|---|---|---|
| MasterDataNotLoaded | 11001 | 서버에 마스터 데이터가 로드되지 않음 |
| MasterDataVersionMismatch | 11002 | 게임 진행 요청 중 클라이언트-서버 마스터 버전 불일치 감지(즉시 로그아웃) |
| InvalidMasterRequest | 11005 | 잘못된 마스터 요청(존재하지 않는 테이블명·형식 오류) |

## 3. `TaskbarHero.Common/ErrorCode.cs` 반영안

현재 파일에는 `Success=0`, `UserNotFound=1001`, `InvalidPassword=1002`만 정의되어 있다. 위 목록을 반영하면 다음과 같다. (`netstandard2.0` 타깃 유지, 서버-클라이언트 공유)

```csharp
// TaskbarHero.Common/GameErrorCode.cs
namespace TaskbarHero.Common
{
    // 서버-클라이언트 공통 에러 코드
    // 숫자 값은 클라이언트와의 계약이므로 변경 금지
    public enum GameErrorCode
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
        StageClearTooFast = 6004,

        // 거래소 / 교역선 (8000번대)
        TradeListingNotFound = 8001,
        TradeNotSellable = 8002,
        TradeNotOwner = 8003,
        TradeSelfPurchase = 8004,
        TradeAlreadyClosed = 8005,
        TradePriceOutOfRange = 8006,
        TradeListingLimitExceeded = 8007,

        // 메일(보상) (9000번대)
        MailNotFound = 9001,
        MailAlreadyClaimed = 9002,
        MailExpired = 9003,

        // 출석부 보상 (10000번대)
        AttendanceAlreadyClaimed = 10001,

        // 마스터 데이터 (11000번대)
        MasterDataNotLoaded = 11001,
        MasterDataVersionMismatch = 11002,
        InvalidMasterRequest = 11005,
    }
}
```

## 4. 출처

- [계정/로그인 기획서](../세부/account-login-기획서.md) — 6장 에러 코드 (1000번대)
- [세이브 데이터 기획서](../세부/save-data-기획서.md) — 6장 에러 코드 (2000번대)
- [오프라인 보상 정산 기획서](../세부/offline-reward-기획서.md) — 7장 에러 코드 (3000번대)
- [인벤토리/아이템/큐브 기획서](../세부/inventory-item-cube-기획서.md) — 7장 에러 코드 (4000번대)
- [성장 시스템 기획서](../세부/growth-기획서.md) — 7장 에러 코드 (5000번대)
- [스테이지/전투 결과 기획서](../세부/stage-battle-기획서.md) — 7장 에러 코드 (6000번대)
- [거래소 / 교역선 기획서](../세부/trade-기획서.md) — 7장 에러 코드 (8000번대)
- [메일 기획서](../세부/mail-기획서.md) — 7장 에러 코드 (9000번대)
- [출석부 보상 시스템 기획서](../세부/attendance-기획서.md) — 7장 에러 코드 (10000번대)
- [마스터 데이터 기획서](../세부/master-data-기획서.md) — 8.3 에러 코드 (11000번대)
