# 출석부 보상 시스템 기획서

> 상위 문서: [서버 시스템 전체 개요](../서버-시스템-전체-개요.md) · 관련 도메인 4.9
>
> 본 문서는 **일차별 접속 보상(출석 체크)**을 서버 권위로 다룬다. 플레이어는 이번달 출석 현황을 조회하고, 하루 1회 오늘자 출석 보상을 획득한다. 보상은 **날짜가 아니라 이번달 누적 출석 순번(일차)** 으로 결정되며 — **첫 출석은 1일차 보상부터**, 다음 출석은 2일차… 최대 **30일차**까지 순서대로 받는다. **보상은 즉시 계정에 지급하지 않고 [메일(4.8)](mail-기획서.md)로 발급**하며, 실제 재화·아이템 반영은 메일 수령 시 이루어진다.

## 목차

- [1. 개요](#1-개요)
- [2. 기능 설명](#2-기능-설명)
- [3. 요구사항](#3-요구사항)
- [4. 데이터 모델](#4-데이터-모델)
- [5. API 명세](#5-api-명세)
  - [5.1 이번달 출석 현황 조회 — `POST /api/game/attendance/status`](#51-이번달-출석-현황-조회--post-apigameattendancestatus)
  - [5.2 출석 보상 획득 — `POST /api/game/attendance/claim`](#52-출석-보상-획득--post-apigameattendanceclaim)
- [6. 처리 흐름](#6-처리-흐름)
- [7. 에러 코드](#7-에러-코드)
- [8. 미결 사항 / TODO](#8-미결-사항--todo)
- [9. 참고](#9-참고)


## 1. 개요

![출석 보상 화면 - 일자별 출석 보상 달력](../images/attendance-출석보상.png)

- **목적**: 방치형 게임의 리텐션 장치로, 매일 접속한 플레이어에게 **일차별 출석 보상**을 준다. 하루 1회 수령·날짜 경계·월 리셋·**일차 산출**을 **서버가 판정**해 클라이언트 조작(중복 수령·날짜 위조)을 막는다. 보상은 우편함으로 발급해 지급-수령 원장을 메일 시스템이 관리하게 한다.
- **일차 = 누적 출석 순번**: 보상 일차는 **날짜(day-of-month)가 아니라 이번달 몇 번째 출석인지**로 정한다. 7월 28일에 이번달 처음 접속했다면 28일차가 아니라 **1일차 보상**을 받고, 다음 출석일에 2일차를 받는다. 월중 유입·중간 이탈 플레이어도 보상 사다리를 처음부터 밟게 해 초반 보상 손실을 없앤다.
- **대상 서버**: `GameServer`(출석 상태 관리·검증, 보상 메일 발급), `TaskbarHero.Common`(출석 현황·결과 DTO·에러 코드 공유). 인증은 AccountServer 발급 토큰을 GameServer 미들웨어가 검증.
- **범위 경계**:
  - **보상의 실제 지급(재화·아이템 계정 반영)은 본 문서 밖**이다. 출석 보상은 **메일 발급**으로 위임하며([메일 기획서](mail-기획서.md)), 플레이어가 우편함에서 첨부를 수령할 때 계정에 반영된다. 본 문서는 "출석 판정 + 보상 메일 발급"까지 책임진다.
  - **일차별 보상 내용(무엇을 얼마나)**은 마스터 데이터(`attendance_master`, 1~30일차)가 정의한다([마스터 데이터 기획서](master-data/master-data-기획서.md)).
- **관련 기획서**: [[mail-기획서]] (보상 메일 발급·수령), [[save-data-기획서]] (출석 기록 저장), [[master-data-기획서]] (`attendance_master`), [[서버-시스템-전체-개요]] (도메인 4.9)

## 2. 기능 설명

- **이번달 출석 현황 조회**: 클라이언트가 출석 달력을 그리기 위해, **1~30일차** 각 일차의 보상 정의와 **수령 여부**, 이번달 누적 출석 수, 오늘 받게 될 일차·오늘 수령 가능 여부를 받는다.
- **출석 보상 획득**: 플레이어가 오늘자 출석 보상을 요청하면, 서버가 **오늘 미수령**임을 확인하고 **이번달 출석 횟수 + 1**을 일차로 산출해 출석 기록을 남긴 뒤 그 일차의 보상을 **메일로 발급**한다. 하루 1회만 가능하다.
- **일차 진행**: 일차는 **순차적으로만** 올라간다. 하루 걸렀다고 일차가 건너뛰지 않으며(3일차 수령 후 일주일 쉬어도 다음 출석은 4일차), 놓친 일차가 사라지지도 않는다. 이번달 **30일차까지 모두 받으면** 그 달에는 더 받을 보상이 없다.
- **날짜 경계·월 리셋**: 출석 일자는 **서버 기준 KST(UTC+9) 자정**으로 나뉜다. 출석 진행도는 **월 단위**이며, 달이 바뀌면 **다시 1일차부터** 시작한다. 수령은 **오늘 1회**만 대상으로 한다(지난달 조회, 놓친 과거일 소급 수령은 제공하지 않는다).

> 방치형 특성상 클라이언트가 오래 백그라운드로 떠 있을 수 있으므로, "오늘"의 판정은 **요청 수신 시점의 서버 시각**을 기준으로 하며 클라이언트가 보낸 날짜/시각은 신뢰하지 않는다.

## 3. 요구사항

**기능 요구사항**
- 이번달 출석 현황 조회, 오늘자 출석 보상 획득을 제공한다.
- 보상 **일차는 `이번달 출석 횟수 + 1`** 로 서버가 산출한다(날짜와 무관 — 첫 출석은 항상 1일차).
- 출석 보상 내용은 `attendance_master`(1~30일차)가 정의한 값을 **서버가 확정**한다(클라이언트 입력 없음).
- 오늘자 출석은 **하루 1회**만 가능하다(중복 수령 거부).
- 이번달 **최종 일차(30일차)까지 모두 수령**했으면 그 달의 추가 수령을 거부한다(`AttendanceAllClaimed(9002)`).
- 획득한 보상은 **메일로 발급**한다([메일 기획서](mail-기획서.md) `player_mail`·`player_mail_reward`, `category=3`(출석)). 발급 메일은 **출석 시점으로부터 7일 후 만료**된다.

**비기능 요구사항**
- **서버 권위**: "오늘" 판정·일차 산출·일차별 보상은 서버가 정한다. 클라이언트가 보고한 날짜/일차/보상을 신뢰하지 않는다.
- **원자성·멱등성**: "일차 산출 + 출석 기록 삽입 + 보상 메일 발급"은 하나의 트랜잭션으로 처리한다. 일차 산출(이번달 출석 수 집계)도 **같은 트랜잭션 안에서** 수행해 삽입과의 사이에 다른 요청이 끼어들지 못하게 한다. `(user_id, attend_date)`에 유니크 제약을 두어 동일 요청 재전송·동시 요청 시 이중 발급을 막는다(이미 출석이면 거부).
- **타임존 고정**: 날짜 경계는 KST(UTC+9) 자정으로 고정한다. 저장 시각은 프로젝트 공통 규약대로 Unix timestamp(BIGINT, 초)를 쓰되, 출석 "일자"는 KST 기준 `YYYYMMDD`로 산출한다.

## 4. 데이터 모델

**새 세이브 테이블 1개**(`player_attendance`)와 **새 마스터 테이블 1개**(`attendance_master`)를 요구한다. 보상 지급 자체는 기존 메일 테이블(`player_mail`·`player_mail_reward`)을 재사용한다.

```mermaid
erDiagram
    game_player ||--o{ player_attendance : checks_in

    player_attendance {
        bigint  user_id FK
        int     attend_date "출석 일자 YYYYMMDD(KST 기준)"
        bigint  claimed_at "출석/발급 시각(Unix ts)"
    }
```

| 테이블 | 역할 | 참조 |
|---|---|---|
| `player_attendance`(`(user_id, attend_date)` PK) | 계정별 **출석한 일자** 기록(행 존재 = 그날 수령함) | — |
| `attendance_master`(`day` PK) | **일차별 보상 정의**(출석 순번 1~30) | `item_master`(아이템/재료) |

- **출석 = 행 1개**: 플레이어가 오늘 출석 보상을 받으면 `(user_id, 오늘 YYYYMMDD)` 행이 1개 생긴다. 그날 이미 행이 있으면 중복 수령이다.
- **일차는 저장하지 않고 집계로 산출**: `player_attendance`에 일차 컬럼을 두지 않는다. **이번달 행 수(`COUNT`)가 곧 수령 완료한 일차 수**이고, 다음 일차 = `행 수 + 1`이다. 일차를 별도 컬럼으로 중복 저장하지 않으므로 기록과 진행도가 어긋날 수 없다(단일 진실 원천).
- **일차별 보상(`attendance_master`)**: `day`(1~30, **출석 순번** — 날짜가 아님)별로 `reward_type`(1:골드 2:아이템 3:재료)·`reward_code`(골드면 0)·`quantity`를 정의한다. `reward_type`/`reward_code` 규약은 [메일 기획서](mail-기획서.md) 4장 첨부 규칙과 동일하다(값 변경 금지 enum 공유).
- **보상 지급은 메일로**: 출석 획득 시 서버가 `player_mail`(1건, `category=3`)과 `player_mail_reward`(그 일차 보상 1건)를 생성한다. 실제 재화·아이템은 플레이어가 우편함에서 수령(`/api/game/mail/claim`)할 때 계정(`player_item`)에 반영된다.

**공유 enum / DTO (TaskbarHero.Common)**
- `reward_type`(1:골드 2:아이템 3:재료)은 메일·인벤토리·마스터 데이터와 동일 값으로 고정한다.
- 출석 현황·획득 결과 DTO는 `TaskbarHero.Common`에 공유 DTO로 두는 것을 **제안**한다(5장 응답 스키마).

## 5. API 명세

**API 목록**

- [5.1 이번달 출석 현황 조회 — `POST /api/game/attendance/status`](#51-이번달-출석-현황-조회--post-apigameattendancestatus)
- [5.2 출석 보상 획득 — `POST /api/game/attendance/claim`](#52-출석-보상-획득--post-apigameattendanceclaim)

Base URL(개발): `http://localhost:5247` (GameServer). 모든 API는 **POST**, 인증 요청 공통 형식 `{ userId, token, data }`, 응답 `{ success, errorCode, message, data }`([세이브 데이터 기획서](save-data-기획서.md) 5장과 동일 규약).

### 5.1 이번달 출석 현황 조회 — `POST /api/game/attendance/status`

이번달(서버 KST 기준) 출석 진행도와 **1~30일차 보상 사다리**를 반환한다. 조회는 상태를 바꾸지 않는다(출석 처리 아님).

**Request**
```json
{ "userId": 1, "token": "...", "data": {} }
```

**Response (성공, 200 OK)** — 7월 28일에 이번달 두 번째로 접속(1일차 수령 완료, 오늘 미수령)한 예:
```json
{
  "success": true,
  "errorCode": 0,
  "message": "OK",
  "data": {
    "yearMonth": 202607,
    "today": 20260728,
    "attendedCount": 1,
    "todayDay": 2,
    "todayClaimed": false,
    "canClaim": true,
    "days": [
      { "day": 1, "rewardType": 1, "rewardCode": 0,     "quantity": 1000, "claimed": true },
      { "day": 2, "rewardType": 1, "rewardCode": 0,     "quantity": 1500, "claimed": false },
      { "day": 3, "rewardType": 3, "rewardCode": 41001, "quantity": 3,    "claimed": false }
    ]
  }
}
```

| 필드 | 의미 |
|---|---|
| `yearMonth` / `today` | 서버가 판정한 이번달(YYYYMM)·오늘(YYYYMMDD), KST 기준 |
| `attendedCount` | 이번달 누적 출석 횟수 = **수령 완료한 일차 수**(0~30) |
| `todayDay` | 오늘 해당하는 일차. 오늘 미수령이면 `attendedCount + 1`, 이미 수령했으면 오늘 받은 일차. **이번달 30일차를 모두 받았으면 `0`** |
| `todayClaimed` | 오늘자 출석을 이미 수령했는지 |
| `canClaim` | 오늘 수령 가능 여부(`!todayClaimed && todayDay >= 1`). 클라이언트 수령 버튼 활성 조건 |
| `days[]` | `attendance_master` **전체 1~30일차**의 보상과 수령 여부. `claimed = (day <= attendedCount)` — 앞에서부터 순서대로 채워진다 |

- 세이브(`game_player`)가 없어도 빈 진행도(`attendedCount=0`, `todayDay=1`)로 정상 응답한다.
- 오류: 마스터 미로드 시 `MasterDataNotLoaded(10001)`.

### 5.2 출석 보상 획득 — `POST /api/game/attendance/claim`

오늘자 출석 보상을 받는다. 서버가 오늘 미수령을 확인하고, **이번달 출석 횟수 + 1**을 일차로 산출해 출석 기록을 남긴 뒤 그 일차의 보상을 **메일로 발급**한다(즉시 계정 지급 아님).

**Request**
```json
{ "userId": 1, "token": "...", "data": {} }
```

**Response (성공, 200 OK)** — 7월 28일에 이번달 첫 출석 → **1일차** 보상:
```json
{
  "success": true,
  "errorCode": 0,
  "message": "Attended",
  "data": {
    "attendDate": 20260728,
    "day": 1,
    "reward": { "rewardType": 1, "rewardCode": 0, "quantity": 1000 },
    "mailId": 8123
  }
}
```

- `attendDate`: 출석한 **날짜**(YYYYMMDD). `day`: 이번에 받은 **일차**(1~30). 이 둘은 서로 무관하다(28일에 1일차 수령 가능).
- `reward`: 이번에 발급된 보상(서버 산출). `mailId`: 발급된 보상 메일. 실제 재화·아이템은 **우편함에서 수령**해야 계정에 반영된다([메일 기획서](mail-기획서.md) 5.2).
- 오류: `AttendanceAlreadyClaimed(9001)`(오늘 이미 수령), `AttendanceAllClaimed(9002)`(이번달 30일차까지 모두 수령), `SaveNotFound(2001)`(캐릭터 생성 전 — 계정 세이브 없음), 마스터 미로드 시 `MasterDataNotLoaded(10001)`.

> 인증 오류(401) 등은 기존 미들웨어를 따른다. 수령은 오늘 1회만 대상이며, 지난달 조회·과거일 소급 수령은 제공하지 않는다.

## 6. 처리 흐름

### 6.1 출석 보상 획득 (의사코드)

```
요청 수신 → 토큰 검증(미들웨어)
now   = 서버 현재 시각
today = KST(now) → YYYYMMDD            # 서버 권위 날짜 판정
maxDay = attendance_master의 최대 day (= 30)
트랜잭션(BEGIN)
  1) if game_player[user_id] 없음: SaveNotFound(2001)            # 캐릭터 생성 전(출석 기록 FK 대상)
  2) if player_attendance[user_id, today] 존재: AttendanceAlreadyClaimed(9001)
  3) count = COUNT(player_attendance[user_id, 이번달 범위])       # 이번달 누적 출석 수 = 수령 완료 일차 수
     day   = count + 1                                          # ★ 일차 = 순번(날짜 아님)
  4) if day > maxDay: AttendanceAllClaimed(9002)                # 이번달 보상 사다리 소진
  5) reward = attendance_master[day]                            # 없으면 MasterDataNotLoaded(10001)
  6) INSERT player_attendance(user_id, today, claimed_at=now)   # 유니크 (user_id, attend_date)
  7) 보상 메일 발급:
       INSERT player_mail(user_id, category=3, title/body, created_at=now, expires_at=now+7일, claimed=0)
       INSERT player_mail_reward(mail_id, seq=1, reward_type, reward_code, quantity)  # reward 기준
COMMIT → { attendDate: today, day, reward, mailId }
```

- **3)의 집계를 트랜잭션 밖에서 하지 않는다.** 일차 산출과 기록 삽입 사이에 다른 요청이 끼어들면 같은 일차가 두 번 발급될 수 있다.
- 실제 재화·아이템 지급은 여기서 하지 않는다. 플레이어가 우편함에서 수령할 때 반영된다(메일 6.1).
- 발급 메일의 만료는 **출석 시점(발급)으로부터 7일**로 확정한다(`expires_at = created_at + 7일`). 7일 안에 우편함에서 수령하지 않으면 만료되어 수령할 수 없다([메일 기획서](mail-기획서.md) 만료 규칙).

![출석 보상이 메일로 발급되어 우편함에서 수령하는 화면](../images/attendance-보상메일수령.png)

### 6.2 예외 / 엣지 케이스

- **하루 중복 수령**: `(user_id, today)` 행이 이미 있으면 `AttendanceAlreadyClaimed(9001)`. 같은 날 동시 요청은 PK 유니크 제약(중복 키 → 경합 패배)으로 직렬화해 이중 발급을 막는다.
- **월중 첫 접속**: 7월 28일에 이번달 처음 접속해도 `count=0 → day=1`이라 **1일차 보상**을 받는다(구 사양에서는 28일차 보상을 받았다). 이것이 이번 개편의 핵심 동작이다.
- **띄엄띄엄 출석**: 하루 걸러도 일차는 건너뛰지 않는다. 3일차까지 받고 일주일 쉬었다면 다음 출석은 **4일차**다.
- **이번달 사다리 소진(30일차 완료)**: `day > 30`이면 `AttendanceAllClaimed(9002)`. 31일이 있는 달에 하루도 빠짐없이 출석하면 마지막 하루가 여기에 해당한다. 현황 조회는 `todayDay=0`·`canClaim=false`로 응답한다.
- **자정 경계 요청**: "오늘"은 요청 수신 시점 서버 KST 기준으로 판정한다. 클라이언트가 보낸 날짜는 무시한다. 자정을 사이에 둔 서로 다른 날짜의 동시 요청은 PK가 막지 못하지만, 일차 집계를 같은 트랜잭션에서 수행하므로 실질적으로 직렬화된다(프로젝트 공통 정책상 `game_player` 행 잠금은 걸지 않는다 — `StageRepository`와 동일).
- **월 리셋**: 달이 바뀌면 집계 범위(이달 1일~말일)가 바뀌어 `count=0`이 되므로 **다시 1일차부터** 시작한다(과거 달 `player_attendance` 행은 유지되나 집계·조회 대상이 아니다). 놓친 일차는 소급 수령할 수 없다.
- **마스터 미정의 일차**: 산출된 `day`가 `attendance_master`에 없으면 `MasterDataNotLoaded(10001)`로 거부한다(정상 운영에선 1~30 전부 정의).
- **캐릭터 생성 전 수령 시도**: `player_attendance`는 `game_player`를 FK로 참조하므로, 계정 세이브가 없으면 삽입 자체가 불가능하다. 트랜잭션 첫 단계에서 세이브 존재를 확인해 `SaveNotFound(2001)`로 거부한다(다른 게임 API와 동일 규약). 현황 조회(5.1)는 세이브가 없어도 빈 진행도를 정상 반환한다.

## 7. 에러 코드

`TaskbarHero.Common`의 `ErrorCode`에 추가 제안. 도메인 4.9(출석부)은 **9000번대**를 사용한다([통합 정의](../공통/error-code-정의.md) 블록 규약, 도메인 4.N → N000). 추가 시 통합 문서도 함께 갱신한다.

| 이름 | 값 | 의미 |
|---|---|---|
| AttendanceAlreadyClaimed | 9001 | 오늘자 출석 보상을 이미 수령함 |
| AttendanceAllClaimed | 9002 | 이번달 출석 보상(30일차)을 모두 수령함 |

- 마스터 데이터 미로드/미정의는 신규 코드 없이 `MasterDataNotLoaded(10001)`([마스터 데이터 기획서](master-data/master-data-기획서.md))를 재사용한다.
- 계정 세이브(`game_player`) 미생성도 신규 코드 없이 `SaveNotFound(2001)`([세이브 데이터 기획서](save-data-기획서.md))을 재사용한다.

## 8. 미결 사항 / TODO

미결 사항 없음 — 전부 확정되었다.

> **범위에서 제외(구현 안 함)**: 연속 출석 보너스, 과거일 소급 수령, 다른 달(지난달) 출석 현황 조회.
> **확정 사항**: 보상 일차는 **날짜가 아닌 이번달 누적 출석 순번**(첫 출석 = 1일차), 사다리 길이는 **30일차**, 월 단위 리셋. 날짜 경계 타임존은 **KST(UTC+9)**, 출석 보상 메일 만료는 **발급(출석) 시점으로부터 7일**. **일차별 보상 구성**은 [마스터 데이터 값 문서 §13](master-data/master-data-값.md#13-attendance_master-출석부-보상)에 1~30일차 전부 확정(평일 골드, 주간 마일스톤 7·14·21·28일차 상급 재료 강화, 15일차 강철 투구·30일차 코스믹 투구 장비 — DDL·시드는 [`master-data-schema.sql`](master-data/master-data-schema.sql) 13번).
>
> **개정 이력(2026-07-28)**: 일차 기준을 **day-of-month(1~31) → 누적 출석 순번(1~30)** 으로 변경. 월중 첫 접속 시 그 날짜에 해당하는 후반 보상을 바로 받던 문제(예: 28일 첫 접속 → 28일차 보상)를 없애고, 누구나 1일차부터 순서대로 받도록 했다.

## 9. 참고

- [서버 시스템 전체 개요](../서버-시스템-전체-개요.md) — 도메인 4.9(출석부), 4.8(메일)
- [메일 기획서](mail-기획서.md) — 보상 메일 발급·수령(`player_mail`·`player_mail_reward`, `category=3`)
- [세이브 데이터 기획서](save-data-기획서.md) — `player_attendance` 저장 골격
- [마스터 데이터 기획서](master-data/master-data-기획서.md) — `attendance_master` 일자별 보상 정의
- [ErrorCode 통합 정의](../공통/error-code-정의.md) — 에러 코드 블록 규약(9000번대 출석부)
