# GameSaveController (`/api/game`, GameServer)

세이브 로드·캐릭터 생성·접속 시각 갱신. 모든 요청은 `GameAuthMiddleware` 인증을 거친다. 저장소: MySQL `taskbar_hero_game`(`game_player`·`player_character`·`player_item`·`player_cube` 등).

> **공통 인증**: 아래 모든 다이어그램의 첫 단계는 `GameAuthMiddleware`가 body의 `userId`·`token`을 Redis `auth:token:{userId}`와 대조하는 것이다(실패 시 401, 컨트롤러 미도달). 이후 인증된 `userId`가 컨트롤러로 전달된다.

## POST /api/game/load — 세이브 로드

```mermaid
sequenceDiagram
    autonumber
    actor C as 클라이언트
    participant MW as GameAuthMiddleware
    participant Ctrl as GameSaveController
    participant Svc as SaveService
    participant Repo as SaveRepository
    participant DB as MySQL(game)
    participant Redis as Redis

    C->>MW: POST /load { userId, token }
    MW->>Redis: GET auth:token:{userId}
    Redis-->>MW: 토큰
    alt 인증 실패
        MW-->>C: 401 Unauthorized
    else 인증 성공
        MW->>Ctrl: Load() (userId 주입)
        Ctrl->>Svc: LoadAsync(userId)
        Svc->>Repo: GetPlayerAsync(userId)
        Repo->>DB: SELECT game_player
        DB-->>Repo: player or null
        alt 신규 계정(game_player 없음)
            Svc-->>Ctrl: Success + { isNew:true }
        else 기존 계정
            Svc->>Repo: 캐릭터·인벤토리·스킬·룬·큐브 조회
            Repo->>DB: SELECT player_character / player_item / player_skill / player_rune / player_cube
            DB-->>Repo: 스냅샷
            Svc->>Svc: offlineElapsedSec = now - last_active_at
            Svc-->>Ctrl: Success + LoadDataDto
        end
        Ctrl-->>C: { success, errorCode, message, data }
    end
```

## POST /api/game/create-character — 캐릭터 생성

```mermaid
sequenceDiagram
    autonumber
    actor C as 클라이언트
    participant MW as GameAuthMiddleware
    participant Ctrl as GameSaveController
    participant Svc as SaveService
    participant MD as MasterDataProvider
    participant Repo as SaveRepository
    participant DB as MySQL(game)

    C->>MW: POST /create-character { userId, token, data:{ nickname, classCode } }
    MW-->>Ctrl: 인증 통과(userId)
    Ctrl->>Svc: CreateCharacterAsync(userId, nickname, classCode)
    Svc->>MD: IsLoaded / IsValidClass(classCode)
    alt 마스터 미로드 or 잘못된 직업
        Svc-->>Ctrl: MasterDataNotLoaded(10001) / InvalidClassCode(2005)
    else 유효
        Svc->>Repo: GetPlayerAsync(userId)
        Repo->>DB: SELECT game_player
        alt 신규 계정(최초 생성)
            Svc->>Repo: CreatePlayerWithFirstCharacterAsync(...)
            Repo->>DB: TX: INSERT game_player + player_character(1) + player_cube + [테스트용] 초기 골드
            Svc-->>Ctrl: Success (무료, cost 0)
        else 기존 계정(2·3번 슬롯 추가)
            Svc->>Repo: GetCharacterSlotsAsync(userId)
            Repo->>DB: SELECT player_character
            Svc->>Svc: 슬롯 여유(≤3)·직업 중복 검사
            Svc->>MD: CharacterCreateCost(newSlot)
            Svc->>Repo: AddCharacterAsync(userId, slot, classCode, cost)
            Repo->>DB: TX: 골드 확인·차감 + INSERT player_character
            alt 골드 부족 / 슬롯·직업 경합
                Svc-->>Ctrl: InsufficientCurrency(4005) / InvalidCharacterId(2006)
            else 성공
                Svc-->>Ctrl: Success + cost·balance
            end
        end
        Ctrl-->>C: { success, errorCode, message, data }
    end
```

## POST /api/game/update-last-active — 접속 시각 갱신(heartbeat)

```mermaid
sequenceDiagram
    autonumber
    actor C as 클라이언트
    participant MW as GameAuthMiddleware
    participant Ctrl as GameSaveController
    participant Svc as SaveService
    participant Repo as SaveRepository
    participant DB as MySQL(game)

    C->>MW: POST /update-last-active { userId, token }
    MW-->>Ctrl: 인증 통과(userId)
    Ctrl->>Svc: UpdateLastActiveAsync(userId)
    Svc->>Repo: UpdateLastActiveAsync(userId, now)
    Repo->>DB: UPDATE game_player SET last_active_at = now
    DB-->>Repo: 영향 행 수
    alt 0행(세이브 없음)
        Svc-->>Ctrl: SaveNotFound(2001)
    else 갱신됨
        Svc-->>Ctrl: Success + { lastActiveAt }
    end
    Ctrl-->>C: { success, errorCode, message, data }
```
