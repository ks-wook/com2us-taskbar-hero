# AuthController (`/api/auth`, AccountServer)

계정/인증. `signup`·`login`은 **무인증**, `logout`은 서버가 토큰을 대조한다. 저장소: MySQL `taskbar_hero_account`(`users`·`user_auth_token`) + Redis(`auth:token:{userId}`).

## POST /api/auth/signup — 회원가입

```mermaid
sequenceDiagram
    autonumber
    actor C as 클라이언트
    participant S as AccountServer
    participant DB as MySQL(account)

    C->>S: POST /signup { email, password, nickname }
    S->>S: 입력 검증(이메일 형식·비번 6자↑·닉네임)
    alt 검증 실패
        S-->>C: 실패 { errorCode: InvalidRequest(1006) }
    else 검증 통과
        S->>DB: 동일 이메일 계정 데이터 확인
        DB-->>S: 존재 여부
        alt 이미 존재
            S-->>C: 실패 { errorCode: DuplicateEmail(1003) }
        else 신규
            S->>S: 비밀번호 해싱(BCrypt)
            S->>DB: 계정 데이터 적재(이메일·해시·닉네임)
            alt 이메일 중복 충돌(동시 가입 경합)
                DB-->>S: 유니크 제약 위반
                S-->>C: 실패 { errorCode: DuplicateEmail(1003) }
            else 성공
                DB-->>S: 발급된 계정 식별자
                S-->>C: 성공 { userId }
            end
        end
    end
```

## POST /api/auth/login — 로그인(토큰 발급)

```mermaid
sequenceDiagram
    autonumber
    actor C as 클라이언트
    participant S as AccountServer
    participant DB as MySQL(account)
    participant Redis as Redis

    C->>S: POST /login { email, password }
    S->>S: 입력 검증
    S->>DB: 계정 자격(식별자·비밀번호 해시) 데이터 확인
    DB-->>S: 자격 or 없음
    alt 사용자 없음
        S-->>C: 실패 { errorCode: UserNotFound(1001) }
    else 존재
        S->>S: 비밀번호 해시 검증(BCrypt)
        alt 비밀번호 불일치
            S-->>C: 실패 { errorCode: InvalidPassword(1002) }
        else 일치
            S->>S: 인증 토큰 발급(만료 시각 산정)
            S->>DB: 토큰 데이터 적재·갱신(계정당 1행 → 기존 세션 무효화)
            S->>Redis: 토큰 캐시 데이터 적재(TTL 24h)
            S-->>C: 성공 { userId, token }
        end
    end
```

## POST /api/auth/logout — 로그아웃(토큰 무효화)

```mermaid
sequenceDiagram
    autonumber
    actor C as 클라이언트
    participant S as AccountServer
    participant DB as MySQL(account)
    participant Redis as Redis

    C->>S: POST /logout { userId, token }
    S->>Redis: 토큰 캐시 데이터 확인
    Redis-->>S: 캐시 토큰 or 없음
    alt 토큰 없음(만료/폐기)
        S-->>C: 실패 { errorCode: ExpiredToken(1005) }
    else 캐시 토큰 ≠ 요청 토큰
        S-->>C: 실패 { errorCode: InvalidToken(1004) }
    else 일치
        S->>DB: 토큰 데이터 삭제
        S->>Redis: 토큰 캐시 데이터 삭제
        S-->>C: 성공 { }
    end
```
