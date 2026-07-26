# GameSaveController (`/api/game`, GameServer)

세이브 로드·캐릭터 생성·접속 시각 갱신. 저장소: MySQL `taskbar_hero_game`(`game_player`·`player_character`·`player_item`·`player_cube` 등).

> **인증**: `/api/game/*` 요청은 `GameAuthMiddleware`가 토큰을 검증한다(실패 시 401). 주요 로직이 아니므로 아래 다이어그램에서는 생략하고, 인증된 `userId`로 처리가 시작된 이후를 표기한다.

## POST /api/game/load — 세이브 로드

```mermaid
sequenceDiagram
    autonumber
    actor C as 클라이언트
    participant S as GameServer
    participant DB as MySQL(game)

    C->>S: POST /load { userId, token }
    S->>DB: 플레이어 세이브 데이터 확인
    DB-->>S: 플레이어 정보 or 없음
    alt 신규 계정(세이브 없음)
        S-->>C: 성공 { isNew: true }
    else 기존 계정
        S->>DB: 캐릭터·아이템·스킬·룬·큐브 데이터 확인
        DB-->>S: 세이브 스냅샷
        S->>S: 방치 경과 시간 계산(현재 시각 − 마지막 활동 시각)
        S-->>C: 성공 { 세이브 전체 스냅샷 }
    end
```

## POST /api/game/create-character — 캐릭터 생성

```mermaid
sequenceDiagram
    autonumber
    actor C as 클라이언트
    participant S as GameServer
    participant DB as MySQL(game)

    C->>S: POST /create-character { userId, token, data:{ nickname, classCode } }
    S->>S: 마스터 데이터 확인(인메모리) — 로드 상태·직업 코드 유효성
    alt 마스터 미로드 or 잘못된 직업
        S-->>C: 실패 { errorCode: MasterDataNotLoaded(10001) / InvalidClassCode(2005) }
    else 유효
        S->>DB: 플레이어 세이브 데이터 확인
        alt 신규 계정(최초 생성)
            S->>DB: 단일 트랜잭션 — 플레이어·1번 슬롯 캐릭터·큐브 데이터 적재 + [테스트용] 초기 골드 적립
            S-->>C: 성공 { 무료, cost 0 }
        else 기존 계정(2·3번 슬롯 추가)
            S->>DB: 기존 캐릭터 슬롯 데이터 확인
            S->>S: 슬롯 여유(≤3)·직업 중복 검사 + 마스터에서 해당 슬롯의 생성 비용 조회
            S->>DB: 단일 트랜잭션 — 골드 데이터 확인·차감 + 캐릭터 데이터 적재
            alt 골드 부족 / 슬롯·직업 경합
                S-->>C: 실패 { errorCode: InsufficientCurrency(4005) / InvalidCharacterId(2006) }
            else 성공
                S-->>C: 성공 { 생성 비용, 잔액 }
            end
        end
    end
```

## POST /api/game/update-last-active — 접속 시각 갱신(heartbeat)

```mermaid
sequenceDiagram
    autonumber
    actor C as 클라이언트
    participant S as GameServer
    participant DB as MySQL(game)

    C->>S: POST /update-last-active { userId, token }
    S->>DB: 접속 시각 데이터 갱신(현재 시각으로)
    DB-->>S: 반영 행 수
    alt 0행(세이브 없음)
        S-->>C: 실패 { errorCode: SaveNotFound(2001) }
    else 갱신됨
        S-->>C: 성공 { lastActiveAt }
    end
```
