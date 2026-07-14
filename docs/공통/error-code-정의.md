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
| 11000번대 | 마스터 데이터 | [마스터 데이터 기획서](../세부/master-data-기획서.md) 8.3 | 사용 중 |
| 그 외 | 성장·스테이지·재화·거래소·메일·출석 등 | (미작성) | 예약 |

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
| InvalidSaveData | 2002 | 저장 값 검증 실패(불가능한 값 등) |
| SaveVersionMismatch | 2003 | 세이브 스키마 버전 불일치 |
| PlayerAlreadyExists | 2004 | 이미 캐릭터가 존재(중복 생성) |
| InvalidClassCode | 2005 | 존재하지 않는 직업 코드 |

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

### 2.6 마스터 데이터 (11000번대)

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
        SaveVersionMismatch = 2003,
        PlayerAlreadyExists = 2004,
        InvalidClassCode = 2005,

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
- [마스터 데이터 기획서](../세부/master-data-기획서.md) — 8.3 에러 코드 (11000번대)
