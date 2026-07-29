# ErrorCode 통합 정의

> 상위 문서: [서버 시스템 전체 개요](서버-시스템-전체-개요.md)
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
| 4000번대 | 인벤토리 / 아이템 / 큐브 | [인벤토리/아이템/큐브 기획서](../세부/inventory-item-cube-기획서.md) 7장 | 사용 중 |
| 5000번대 | 성장(직업 / 스킬 / 룬) | [성장 시스템 기획서](../세부/growth-기획서.md) 7장 | 사용 중 |
| 6000번대 | 스테이지 / 전투 결과 | [스테이지/전투 결과 기획서](../세부/stage-battle-기획서.md) 7장 | 사용 중 |
| 7000번대 | 거래소 / 교역선 | [거래소 / 교역선 기획서](../세부/trade-기획서.md) 8장 | 사용 중 |
| 8000번대 | 메일(보상) | [메일 기획서](../세부/mail-기획서.md) 7장 | 사용 중 |
| 9000번대 | 출석부 보상 | [출석부 보상 시스템 기획서](../세부/attendance-기획서.md) 7장 | 사용 중 |
| 10000번대 | 마스터 데이터 | [마스터 데이터 기획서](../세부/master-data/master-data-기획서.md) 8 | 사용 중 |
| 11000번대 | 공통 / 시스템 | [서버 로깅 규칙](로깅-규칙.md) 6장(전역 예외 처리) | 사용 중 |

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
| InsufficientQuantity | 4006 | 아이템/재료 수량 부족 |
| ItemEquipped | 4007 | 장착 중이라 분해 불가 |
| InventoryCapacityMax | 4008 | 인벤토리 용량이 최대치에 도달(확장 불가) |
| InvalidInventorySlot | 4009 | 인벤토리 칸(slot) 번호가 잘못됨(용량 범위 밖 등) |
| CubeRecipeNotMet | 4010 | 큐브 합성/제작 조건(등급·개수·재료) 미충족 |
| CubeLevelInsufficient | 4011 | 큐브 레벨이 해당 연산 요구치 미만 |
| InventoryRevisionChanged | 4012 | 인벤토리 페이지 조회 도중 인벤토리가 변경됨(코어 로드부터 재조회 필요) |

- `InventoryRevisionChanged(4012)`는 가방 아이템 페이징(`POST /api/game/inventory/list`) 중 `game_player.inventory_revision`이 바뀌었을 때 반환한다([세이브 데이터 기획서](../세부/save-data-기획서.md) 5.2). 사용자 실수가 아닌 정상 경합이므로 클라이언트는 `POST /api/game/load`부터 재조회한다.

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

### 2.8 거래소 / 교역선 (7000번대)

| 이름 | 값 | 의미 |
|---|---|---|
| TradeListingNotFound | 7001 | 거래 등록이 없거나 접근 불가 |
| TradeNotSellable | 7002 | 판매 불가 아이템(`sellable=0`) |
| TradeNotOwner | 7003 | 본인 등록이 아님(취소 불가) |
| TradeSelfPurchase | 7004 | 자기 등록은 구매 불가 |
| TradeAlreadyClosed | 7005 | 이미 판매/취소된 등록 |
| TradePriceOutOfRange | 7006 | 등록 가격이 기준가 ±20% 범위 밖 |
| TradeListingLimitExceeded | 7007 | 계정 동시 등록 한도(10개) 초과 |
| TradeBusy | 7008 | 같은 등록에 다른 요청이 처리 중(재시도 가능) |

- 등록 아이템 없음·장착 중은 `ItemNotFound(4001)`·`ItemEquipped(4007)`, 구매 골드 부족은 `InsufficientCurrency(4005)`, 아이템 지급 용량 초과는 `InventoryFull(4002)`를 재사용한다.
- `TradeBusy(7008)`는 거래소 **Redis 구매 락** 획득에 재시도까지 실패했을 때 반환한다([거래소 기획서](../세부/trade-기획서.md) 7.4). HTTP 409. 재시도가 무의미한 `TradeAlreadyClosed(7005)`와 의미가 다르므로 혼용하지 않는다.

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
        InventoryRevisionChanged = 4012,

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

        // 거래소 / 교역선 (7000번대)
        TradeListingNotFound = 7001,
        TradeNotSellable = 7002,
        TradeNotOwner = 7003,
        TradeSelfPurchase = 7004,
        TradeAlreadyClosed = 7005,
        TradePriceOutOfRange = 7006,
        TradeListingLimitExceeded = 7007,
        TradeBusy = 7008,

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
- [거래소 / 교역선 기획서](../세부/trade-기획서.md) — 8장 에러 코드 (7000번대), 7장 성능 설계(Redis 캐시·경합 제어)
- [메일 기획서](../세부/mail-기획서.md) — 7장 에러 코드 (8000번대)
- [출석부 보상 시스템 기획서](../세부/attendance-기획서.md) — 7장 에러 코드 (9000번대)
- [마스터 데이터 기획서](../세부/master-data/master-data-기획서.md) — 8장 에러 코드 (10000번대)
