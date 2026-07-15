# 계정 / 로그인 기획서

> 상위 문서: [서버 시스템 전체 개요](../공통/서버-시스템-전체-개요.md) · 관련 도메인 4.1
>
> **개발 기준 문서**: 본 로그인 시스템은 [jacking75 — ASP.NET Core API 게임서버 실습 09장](https://github.com/jacking75/programming-books-with-ai/blob/main/ASPNETCore-API_%EA%B2%8C%EC%9E%84%EC%84%9C%EB%B2%84_%EC%8B%A4%EC%8A%B5/09.md)의 구조를 기준으로 개발한다. 아래 명세는 해당 문서의 아키텍처(MySQL + Redis, 계층형 구조, 커스텀 HMAC 토큰)를 본 프로젝트에 맞게 반영한 것이다.

## 목차

- [1. 개요](#1-개요)
- [2. 아키텍처 / 서버 구성](#2-아키텍처--서버-구성)
- [3. 데이터 모델 (ERD)](#3-데이터-모델-erd)
- [4. 토큰 생성 / 검증 방식](#4-토큰-생성--검증-방식)
- [5. API 명세](#5-api-명세)
  - [5.1 회원가입 — `POST /api/auth/signup`](#51-회원가입--post-apiauthsignup)
  - [5.2 로그인 — `POST /api/auth/login`](#52-로그인--post-apiauthlogin)
  - [5.3 로그아웃 — `POST /api/auth/logout`](#53-로그아웃--post-apiauthlogout)
  - [5.4 토큰 검증 (인증 미들웨어)](#54-토큰-검증-인증-미들웨어)
- [6. 에러 코드 (신규 제안)](#6-에러-코드-신규-제안)
- [7. 미결 사항 / TODO](#7-미결-사항--todo)
- [8. 참고](#8-참고)


## 1. 개요

- **목적**: 플레이어의 계정 로그인·인증을 제공하고, 로그인 성공 시 인증 토큰을 발급한다. 이 토큰으로 `GameServer` 접근을 인가한다.
- **대상 서버**: `AccountServer`(인증/토큰 발급), `TaskbarHero.Common`(공유 에러 코드/DTO). 토큰 검증은 `GameServer`의 인증 미들웨어에서도 수행.
- **인증 방식**: 기준 문서에 따라 **이메일 + 비밀번호** 로그인을 사용한다.
- **토큰 방식**: JWT가 아닌 **커스텀 HMAC-SHA256 토큰**을 사용한다. `SecretKey`로 서명하여 위변조를 방지하고, 서명 검증만으로 무결성을 확인할 수 있다.
- **세션 정책**: 사용자당 토큰 1개만 유지한다(단일 세션). **다중 기기 로그인은 허용하지 않는다** — 새 로그인이 감지되면 기존에 로그인되어 있던 세션을 무효화한다. 구현상 로그인 시 `user_auth_token`을 UPSERT하고 Redis 토큰값을 새 토큰으로 덮어쓰면, 이전 기기가 갖고 있던 토큰은 Redis 대조에서 불일치하여 자동으로 무효가 된다(다음 요청부터 401).

## 2. 아키텍처 / 서버 구성

기준 문서의 계층형 아키텍처를 따른다.

```
클라이언트 요청
    ↓
AuthController (API 엔드포인트)
    ↓
AuthService (비즈니스 로직)
    ↓
Repository 계층
    ├─ UserRepository        → MySQL (users)
    └─ AuthTokenRepository   → MySQL (user_auth_token) + Redis (캐시)
    ↓
TokenGenerator (HMAC-SHA256 토큰 생성/검증)
```

- **MySQL**: 사용자 정보·토큰의 영속 저장소.
- **Redis**: 토큰 캐싱으로 인증 조회 성능 향상(요청 시 Redis 우선 조회 → 미스 시 MySQL).
- **AccountServer**가 위 로그인/인증을 담당하고, **GameServer**는 인증 미들웨어에서 요청의 `token`을 Redis(`auth:token:{userId}`) 값과 대조해 요청을 인가한다. (서명 재계산 불필요 → SecretKey 미보유)

### 기술 스택 / 주요 NuGet

| 패키지 | 용도 |
|---|---|
| MySqlConnector | MySQL 연결 |
| SqlKata | 쿼리 빌더 |
| CloudStructures | Redis 클라이언트 |
| BCrypt.Net-Next | 비밀번호 해싱 |

> 참고: 본 프로젝트 서버는 `.NET 10`(기준 문서는 .NET 9 이상)이며, `TaskbarHero.Common`은 Unity 호환을 위해 `netstandard2.0`을 유지한다.

## 3. 데이터 모델 (ERD)

`AccountServer` 전용 MySQL 데이터베이스. 시간 값은 기준 문서와 동일하게 **Unix timestamp(BIGINT, 초 단위)**로 저장한다. 비밀번호는 평문이 아닌 **BCrypt 해시**로 저장한다.

```mermaid
erDiagram
    users ||--|| user_auth_token : has

    users {
        bigint  user_id PK "AUTO_INCREMENT"
        varchar email "UNIQUE, 로그인 ID"
        varchar password "BCrypt 해시"
        varchar nickname
        bigint  created_at "Unix ts"
        bigint  updated_at "Unix ts"
    }

    user_auth_token {
        bigint  user_id PK "users.user_id, 사용자당 1행"
        varchar token "발급 토큰"
        bigint  created_at "Unix ts"
        bigint  expired_at "Unix ts, 만료 시각"
    }
```

- `users.email`에 UNIQUE 인덱스(로그인 ID로 사용), `idx_email` 조회 인덱스.
- `user_auth_token`은 `user_id`가 PK이므로 **사용자당 토큰 1행**만 존재한다. 재로그인 시 UPSERT로 덮어쓴다.
- `user_auth_token`에 `idx_token`, `idx_expired_at` 인덱스(토큰 조회 및 만료 토큰 정리 성능).

### 참고 DDL

```sql
CREATE TABLE users (
    user_id    BIGINT AUTO_INCREMENT PRIMARY KEY,
    email      VARCHAR(100) NOT NULL UNIQUE,
    password   VARCHAR(100) NOT NULL,   -- BCrypt 해시
    nickname   VARCHAR(50)  NOT NULL,
    created_at BIGINT NOT NULL,
    updated_at BIGINT NOT NULL,
    INDEX idx_email (email)
);

CREATE TABLE user_auth_token (
    user_id    BIGINT NOT NULL PRIMARY KEY,
    token      VARCHAR(500) NOT NULL,
    created_at BIGINT NOT NULL,
    expired_at BIGINT NOT NULL,
    INDEX idx_token (token),
    INDEX idx_expired_at (expired_at)
);
```

## 4. 토큰 생성 / 검증 방식

기준 문서의 커스텀 토큰 방식을 따른다. **JWT를 사용하지 않는다.**

### 4.1 생성 (5단계)

1. **Salt 생성**: 16바이트 난수 → Base64
2. **Timestamp 기록**: 현재 Unix timestamp
3. **토큰 데이터 조합**: `tokenData = "{userId}:{timestamp}:{salt}"`
4. **HMAC-SHA256 해싱**: `SecretKey`로 `tokenData` 해싱 → `hash`
5. **최종 인코딩**: `token = Base64("{userId}:{timestamp}:{salt}:{hash}")`

### 4.2 검증

```
Base64 디코딩 → 필드 분리(userId:timestamp:salt:hash)
 → 만료 시각 확인(expired_at) → hash 재계산 → hash 비교
```

위 서명 재계산 검증은 **토큰을 발급하는 `AccountServer`**에서 사용한다(위변조 감지). 반면 **`GameServer`는 SecretKey 없이** 요청의 `token`을 Redis(`auth:token:{userId}`)에 저장된 값과 **대조**하고 존재(만료) 여부만 확인한다. Redis 토큰값이 곧 유효 세션의 단일 기준이므로, 재로그인/로그아웃으로 값이 바뀌거나 삭제되면 이전 토큰은 자동으로 무효가 된다.

### 4.3 설정 / SecretKey 관리 (확정)

기준 문서 08장의 지침("하드코딩 금지, `appsettings.json`에 직접 저장 금지, 환경 변수 또는 User Secrets 사용, 최소 32바이트")에 따라 **SecretKey는 `appsettings.json`에 넣지 않고 환경 변수 / User Secrets로 관리**하는 것으로 확정한다.

- **로컬/개발**: .NET User Secrets에 저장 (`dotnet user-secrets set "Security:SecretKey" "<32바이트 이상 랜덤 값>"`)
- **운영/배포**: 환경 변수로 주입 (`Security__SecretKey=<값>`)
- ASP.NET Core의 `IConfiguration`이 appsettings.json + 환경 변수 + User Secrets를 병합하므로, 코드에서는 출처와 무관하게 `configuration["Security:SecretKey"]`로 읽는다.
- SecretKey는 최소 32바이트(256비트) 이상 랜덤 값을 사용한다.

`appsettings.json`에는 **비밀이 아닌 설정만** 둔다.

```json
// appsettings.json — SecretKey 는 포함하지 않는다
"Security": {
  "TokenExpirationHours": 24
}
```

- 만료 시각: `expired_at = created_at + TokenExpirationHours * 3600` (기본 24시간, 잠정값).

## 5. API 명세

**API 목록**

- [5.1 회원가입 — `POST /api/auth/signup`](#51-회원가입--post-apiauthsignup)
- [5.2 로그인 — `POST /api/auth/login`](#52-로그인--post-apiauthlogin)
- [5.3 로그아웃 — `POST /api/auth/logout`](#53-로그아웃--post-apiauthlogout)
- [5.4 토큰 검증 (인증 미들웨어)](#54-토큰-검증-인증-미들웨어)

Base URL(개발): `http://localhost:5160` (AccountServer)

모든 API는 **POST** 방식이며, 응답에는 **`errorCode`를 포함**한다(`success`, `errorCode`, `userId`/`token` 등, `message`). `errorCode`는 `TaskbarHero.Common`의 `GameErrorCode`(6장) 값이고 `success`는 `errorCode == 0`과 동치이며, `message`는 해당 코드의 설명 문구다. (세이브·마스터 기획서와 동일한 응답 규약)

#### 인증 방식 — HTTP Body(JSON)로 토큰 전달 ⭐

기준 문서 10장(인증 미들웨어)을 따라, 인증이 필요한 API는 **`Authorization` 헤더를 사용하지 않는다.** 대신 로그인 이후 모든 요청은 **요청 body(JSON)에 `userId`와 `token`을 함께 포함**한다. 미들웨어가 `Request.Body`를 읽어 인증 정보를 추출한다.

**인증 요청 공통 형식**
```json
{
  "userId": 1,
  "token": "MToxNzAwMDAwMDAwOmFCM2RFNmZHOWhKMWtM...",
  "data": { /* API별 실제 파라미터 */ }
}
```

> 로그인/회원가입은 아직 토큰이 없으므로 이 형식을 적용하지 않는다(이메일·비밀번호를 직접 body에 담는다).

---

### 5.1 회원가입 — `POST /api/auth/signup`

**Request**
```json
{
  "email": "hero@example.com",
  "password": "password123",
  "nickname": "hero"
}
```

**Response (성공, 200 OK)**
```json
{
  "success": true,
  "errorCode": 0,
  "userId": 1,
  "message": "Signup successful"
}
```

**Response (이메일 중복, 409 Conflict)**
```json
{
  "success": false,
  "errorCode": 1003,
  "userId": 0,
  "message": "Duplicate email"
}
```

- 검증: 이메일 형식, 비밀번호 최소 6자. 저장 시 `BCrypt.HashPassword(password)`.

---

### 5.2 로그인 — `POST /api/auth/login`

**Request**
```json
{
  "email": "hero@example.com",
  "password": "password123"
}
```

**Response (성공, 200 OK)**
```json
{
  "success": true,
  "errorCode": 0,
  "userId": 1,
  "token": "MToxNzAwMDAwMDAwOmFCM2RFNmZHOWhKMWtM...",
  "message": "Login successful"
}
```

**Response (비밀번호 오류, 401 Unauthorized)**
```json
{
  "success": false,
  "errorCode": 1002,
  "userId": 0,
  "token": "",
  "message": "Invalid password"
}
```

**Response (계정 없음, 401 Unauthorized)**
```json
{
  "success": false,
  "errorCode": 1001,
  "userId": 0,
  "token": "",
  "message": "User not found"
}
```

#### 로그인 처리 단계

1. **입력 검증** — 이메일 형식, 비밀번호 최소 6자
2. **사용자 조회** — `UserRepository.GetUserByEmailAsync(email)` (MySQL)
3. **비밀번호 검증** — `BCrypt.Verify(password, storedHash)`
4. **토큰 생성** — `TokenGenerator.GenerateToken(userId)`
5. **MySQL 저장** — `AuthTokenRepository.SaveTokenAsync(...)` (UPSERT, 사용자당 1행 → **기존 세션 무효화**)
6. **Redis 캐싱** — `auth:token:{userId}` = token으로 덮어쓰기, TTL 24시간 (이전 기기의 토큰은 이 시점부터 불일치 → 무효)
7. **응답 반환** — 토큰을 클라이언트에 전달

```
클라이언트            AccountServer          MySQL            Redis
   │ 로그인 요청 ───────▶ 사용자 조회 ───────▶               
   │                    ◀── user_id, hash ──               
   │                    [비밀번호 검증][토큰 생성]          
   │                    토큰 저장(UPSERT) ──▶               
   │                    토큰 캐싱 ───────────────────────▶ 
   │ ◀── success, token ─                                  
```

---

### 5.3 로그아웃 — `POST /api/auth/logout`

인증 필요. `userId`, `token`을 body에 포함한다.

**Request**
```json
{
  "userId": 1,
  "token": "MToxNzAwMDAwMDAwOmFCM2RFNmZHOWhKMWtM...",
  "data": {}
}
```

**Response (200 OK)**
```json
{
  "success": true,
  "errorCode": 0,
  "message": "Logout successful"
}
```

- 처리: 미들웨어가 body의 `userId`·`token`을 검증한 뒤 `user_auth_token` 행 삭제 + Redis `auth:token:{userId}` 삭제.

---

### 5.4 토큰 검증 (인증 미들웨어)

모든 보호 API 요청에서 미들웨어가 토큰을 검증한다. 기준 문서 10장을 따라 **요청 body(JSON)에서 `userId`·`token`을 읽어** 검증한다(헤더 미사용).

```
POST 요청 body(JSON): { userId, token, data }
 → request.EnableBuffering()
 → StreamReader로 Request.Body 읽기 → JsonDocument.Parse
 → root.TryGetProperty("userId"), root.TryGetProperty("token") 로 추출
 → Redis auth:token:{userId} 조회 → 요청 token과 값 일치·존재(만료) 확인
 → 통과 시 userId를 요청 컨텍스트에 주입, 실패 시 401
```

- `Request.Body`는 한 번 읽으면 소비되므로 `EnableBuffering()`으로 되감아 컨트롤러가 다시 읽을 수 있게 한다.
- **인증의 기준은 Redis에 저장된 토큰값**이다. GameServer는 SecretKey 없이 이 값과의 대조만으로 검증하므로 서버 간 키 공유·별도 검증 API 호출이 필요 없다.

> 상세 미들웨어 설계는 기준 문서 10장(인증 미들웨어)에 대응하며, 별도 기획으로 확장할 수 있다.

## 6. 에러 코드 (신규 제안)

`TaskbarHero.Common/ErrorCode.cs`의 `GameErrorCode`에 추가 제안. 기존 값(`Success=0`, `UserNotFound=1001`, `InvalidPassword=1002`)과 중복되지 않게 한다. API 응답의 `message`는 아래 코드에 매핑한다.

| 이름 | 값 | 의미 | 매핑 message 예시 |
|---|---|---|---|
| Success | 0 | 성공 (기존) | "Login successful" |
| UserNotFound | 1001 | 계정 없음 (기존) | "User not found" |
| InvalidPassword | 1002 | 비밀번호 불일치 (기존) | "Invalid password" |
| DuplicateEmail | 1003 | 이메일(로그인 ID) 중복 | "Duplicate email" |
| InvalidToken | 1004 | 토큰 무효(서명/형식 오류) | "Invalid token" |
| ExpiredToken | 1005 | 토큰 만료 또는 폐기됨 | "Expired token" |
| InvalidRequest | 1006 | 요청 파라미터 오류(형식/길이) | "Invalid request" |

## 7. 미결 사항 / TODO

- **비밀번호 정책**: 최소 길이 6자는 기준 문서값. 게임 정책에 맞게 확정.
- **토큰 만료 시간**: 기본 24시간은 잠정값. 방치형 게임 특성(장시간 미접속)을 고려해 확정.
- **만료 토큰 정리**: `DELETE FROM user_auth_token WHERE expired_at < UNIX_TIMESTAMP()` 배치 주기 결정.

> **AccountServer ↔ GameServer 간 SecretKey 공유는 불필요하다.** GameServer는 서명을 재계산하지 않고, 요청으로 받은 `token`을 Redis(`auth:token:{userId}`)에 저장된 값과 **대조**하는 것만으로 인증한다. 따라서 SecretKey는 토큰을 발급하는 AccountServer만 보유하면 된다.

## 8. 참고

본 기획서가 기준으로 삼은 문서 (jacking75 — ASP.NET Core API 게임서버 실습).

| 장 | 내용 | 본 문서 반영 부분 |
|---|---|---|
| [08. 커스텀 토큰 인증 설계](https://github.com/jacking75/programming-books-with-ai/blob/main/ASPNETCore-API_%EA%B2%8C%EC%9E%84%EC%84%9C%EB%B2%84_%EC%8B%A4%EC%8A%B5/08.md) | 토큰 생성/검증 전략, **SecretKey 관리 지침**(하드코딩·appsettings.json 직접 저장 금지, 환경 변수/User Secrets 사용, 최소 32바이트) | 4.1 토큰 생성 · 4.3 SecretKey 관리 |
| [09. 로그인 시스템](https://github.com/jacking75/programming-books-with-ai/blob/main/ASPNETCore-API_%EA%B2%8C%EC%9E%84%EC%84%9C%EB%B2%84_%EC%8B%A4%EC%8A%B5/09.md) | MySQL + Redis 계층형 구조, 커스텀 HMAC-SHA256 토큰, `users`/`user_auth_token` 테이블, 단일 세션(토큰 UPSERT) | 2~5장 (아키텍처·ERD·토큰·로그인 API) |
| [10. 인증 미들웨어](https://github.com/jacking75/programming-books-with-ai/blob/main/ASPNETCore-API_%EA%B2%8C%EC%9E%84%EC%84%9C%EB%B2%84_%EC%8B%A4%EC%8A%B5/10.md) | 인증 정보를 HTTP 헤더가 아닌 **요청 body(JSON)**의 `userId`·`token`으로 전달, `Request.Body`를 파싱해 검증 | 5장 인증 방식 · 5.4 토큰 검증 |
