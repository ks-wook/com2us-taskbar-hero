# AuthController (`/api/auth`, AccountServer)

계정/인증. `signup`·`login`은 **무인증**, `logout`은 서비스에서 토큰을 대조한다. 저장소: MySQL `taskbar_hero_account`(`users`·`user_auth_token`) + Redis(`auth:token:{userId}`).

## POST /api/auth/signup — 회원가입

```mermaid
sequenceDiagram
    autonumber
    actor C as 클라이언트
    participant Ctrl as AuthController
    participant Svc as AuthService
    participant Repo as UserRepository
    participant DB as MySQL(account)

    C->>Ctrl: POST /signup { email, password, nickname }
    Ctrl->>Svc: 회원가입 처리 요청
    Svc->>Svc: 입력 검증(이메일 형식·비번 6자↑·닉네임)
    alt 검증 실패
        Svc-->>Ctrl: InvalidRequest(1006)
    else 검증 통과
        Svc->>Repo: 이메일 중복 확인 요청
        Repo->>DB: 동일 이메일 계정 데이터 확인
        DB-->>Repo: 존재 여부
        alt 이미 존재
            Svc-->>Ctrl: DuplicateEmail(1003)
        else 신규
            Svc->>Svc: 비밀번호 해싱(BCrypt)
            Svc->>Repo: 계정 저장 요청(이메일·해시·닉네임)
            Repo->>DB: 계정 데이터 적재
            alt 이메일 중복 충돌(동시 가입 경합)
                DB-->>Svc: 유니크 제약 위반
                Svc-->>Ctrl: DuplicateEmail(1003)
            else 성공
                DB-->>Repo: 발급된 계정 식별자
                Svc-->>Ctrl: Success(0) + userId
            end
        end
    end
    Ctrl-->>C: { success, errorCode, userId, message }
```

## POST /api/auth/login — 로그인(토큰 발급)

```mermaid
sequenceDiagram
    autonumber
    actor C as 클라이언트
    participant Ctrl as AuthController
    participant Svc as AuthService
    participant URepo as UserRepository
    participant TRepo as AuthTokenRepository
    participant Cache as AuthTokenCache
    participant DB as MySQL(account)
    participant Redis as Redis

    C->>Ctrl: POST /login { email, password }
    Ctrl->>Svc: 로그인 처리 요청
    Svc->>Svc: 입력 검증
    Svc->>URepo: 이메일로 계정 자격 조회 요청
    URepo->>DB: 계정 자격(식별자·비밀번호 해시) 데이터 확인
    DB-->>URepo: 자격 or 없음
    alt 사용자 없음
        Svc-->>Ctrl: UserNotFound(1001)
    else 존재
        Svc->>Svc: 비밀번호 해시 검증(BCrypt)
        alt 비밀번호 불일치
            Svc-->>Ctrl: InvalidPassword(1002)
        else 일치
            Svc->>Svc: 인증 토큰 발급
            Svc->>TRepo: 토큰 저장 요청(만료 시각 포함)
            TRepo->>DB: 토큰 데이터 적재·갱신(계정당 1행 → 기존 세션 무효화)
            Svc->>Cache: 토큰 캐시 저장 요청(만료 시간 지정)
            Cache->>Redis: 토큰 캐시 데이터 적재(TTL 24h)
            Svc-->>Ctrl: Success(0) + userId + token
        end
    end
    Ctrl-->>C: { success, errorCode, userId, token, message }
```

## POST /api/auth/logout — 로그아웃(토큰 무효화)

```mermaid
sequenceDiagram
    autonumber
    actor C as 클라이언트
    participant Ctrl as AuthController
    participant Svc as AuthService
    participant TRepo as AuthTokenRepository
    participant Cache as AuthTokenCache
    participant DB as MySQL(account)
    participant Redis as Redis

    C->>Ctrl: POST /logout { userId, token }
    Ctrl->>Svc: 로그아웃 처리 요청
    Svc->>Cache: 캐시된 토큰 조회 요청
    Cache->>Redis: 토큰 캐시 데이터 확인
    Redis-->>Cache: 캐시 토큰 or 없음
    alt 토큰 없음(만료/폐기)
        Svc-->>Ctrl: ExpiredToken(1005)
    else 캐시 토큰 ≠ 요청 토큰
        Svc-->>Ctrl: InvalidToken(1004)
    else 일치
        Svc->>TRepo: 토큰 삭제 요청
        TRepo->>DB: 토큰 데이터 삭제
        Svc->>Cache: 토큰 캐시 삭제 요청
        Cache->>Redis: 토큰 캐시 데이터 삭제
        Svc-->>Ctrl: Success(0)
    end
    Ctrl-->>C: { success, errorCode, message }
```
