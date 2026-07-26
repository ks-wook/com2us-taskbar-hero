# GameSaveController (`/api/game`, GameServer)

세이브 로드·캐릭터 생성·접속 시각 갱신. 저장소: MySQL `taskbar_hero_game`(`game_player`·`player_character`·`player_item`·`player_cube` 등).

> **인증**: `/api/game/*` 요청은 컨트롤러 진입 전에 `GameAuthMiddleware`가 토큰을 검증한다(실패 시 401). 주요 로직이 아니므로 아래 다이어그램에서는 생략하고, 인증된 `userId`가 컨트롤러에 전달된 이후를 표기한다.

## POST /api/game/load — 세이브 로드

```mermaid
sequenceDiagram
    autonumber
    actor C as 클라이언트
    participant Ctrl as GameSaveController
    participant Svc as SaveService
    participant Repo as SaveRepository
    participant DB as MySQL(game)

    C->>Ctrl: POST /load { userId, token }
    Ctrl->>Svc: 세이브 로드 요청
    Svc->>Repo: 플레이어 세이브 조회 요청
    Repo->>DB: 플레이어 세이브 데이터 확인
    DB-->>Repo: 플레이어 정보 or 없음
    alt 신규 계정(세이브 없음)
        Svc-->>Ctrl: Success + { isNew:true }
    else 기존 계정
        Svc->>Repo: 캐릭터·인벤토리·스킬·룬·큐브 조회 요청
        Repo->>DB: 캐릭터·아이템·스킬·룬·큐브 데이터 확인
        DB-->>Repo: 세이브 스냅샷
        Svc->>Svc: 방치 경과 시간 계산(현재 시각 − 마지막 활동 시각)
        Svc-->>Ctrl: Success + 세이브 전체 스냅샷
    end
    Ctrl-->>C: { success, errorCode, message, data }
```

## POST /api/game/create-character — 캐릭터 생성

```mermaid
sequenceDiagram
    autonumber
    actor C as 클라이언트
    participant Ctrl as GameSaveController
    participant Svc as SaveService
    participant MD as MasterDataProvider
    participant Repo as SaveRepository
    participant DB as MySQL(game)

    C->>Ctrl: POST /create-character { userId, token, data:{ nickname, classCode } }
    Ctrl->>Svc: 캐릭터 생성 요청(닉네임·직업)
    Svc->>MD: 마스터 로드 상태·직업 코드 유효성 확인
    alt 마스터 미로드 or 잘못된 직업
        Svc-->>Ctrl: MasterDataNotLoaded(10001) / InvalidClassCode(2005)
    else 유효
        Svc->>Repo: 플레이어 세이브 조회 요청
        Repo->>DB: 플레이어 세이브 데이터 확인
        alt 신규 계정(최초 생성)
            Svc->>Repo: 최초 세이브 생성 요청(플레이어·1번 슬롯 캐릭터·큐브)
            Repo->>DB: 단일 트랜잭션 — 플레이어·1번 슬롯 캐릭터·큐브 데이터 적재 + [테스트용] 초기 골드 적립
            Svc-->>Ctrl: Success (무료, cost 0)
        else 기존 계정(2·3번 슬롯 추가)
            Svc->>Repo: 기존 캐릭터 슬롯 조회 요청
            Repo->>DB: 기존 캐릭터 슬롯 데이터 확인
            Svc->>Svc: 슬롯 여유(≤3)·직업 중복 검사
            Svc->>MD: 해당 슬롯의 캐릭터 생성 비용 조회
            Svc->>Repo: 캐릭터 추가 요청(슬롯·직업·생성 비용)
            Repo->>DB: 단일 트랜잭션 — 골드 데이터 확인·차감 + 캐릭터 데이터 적재
            alt 골드 부족 / 슬롯·직업 경합
                Svc-->>Ctrl: InsufficientCurrency(4005) / InvalidCharacterId(2006)
            else 성공
                Svc-->>Ctrl: Success + 생성 비용·잔액
            end
        end
    end
    Ctrl-->>C: { success, errorCode, message, data }
```

## POST /api/game/update-last-active — 접속 시각 갱신(heartbeat)

```mermaid
sequenceDiagram
    autonumber
    actor C as 클라이언트
    participant Ctrl as GameSaveController
    participant Svc as SaveService
    participant Repo as SaveRepository
    participant DB as MySQL(game)

    C->>Ctrl: POST /update-last-active { userId, token }
    Ctrl->>Svc: 접속 시각 갱신 요청
    Svc->>Repo: 접속 시각을 현재로 갱신 요청
    Repo->>DB: 접속 시각 데이터 갱신
    DB-->>Repo: 반영 행 수
    alt 0행(세이브 없음)
        Svc-->>Ctrl: SaveNotFound(2001)
    else 갱신됨
        Svc-->>Ctrl: Success + { lastActiveAt }
    end
    Ctrl-->>C: { success, errorCode, message, data }
```
