# GameStageController (`/api/game/stage`, GameServer)

스테이지 진입·클리어(서버 권위 보상 산출). 저장소: MySQL `taskbar_hero_game` + `MasterDataProvider`(stage/reward/level/item).

> **인증**: `/api/game/*` 요청은 컨트롤러 진입 전에 `GameAuthMiddleware`가 토큰을 검증한다(실패 시 401). 주요 로직이 아니므로 아래 다이어그램에서는 생략하고, 인증된 `userId`가 컨트롤러에 전달된 이후를 표기한다.

## POST /api/game/stage/enter — 스테이지 진입

```mermaid
sequenceDiagram
    autonumber
    actor C as 클라이언트
    participant Ctrl as GameStageController
    participant Svc as StageService
    participant MD as MasterDataProvider
    participant Repo as StageRepository
    participant DB as MySQL(game)

    C->>Ctrl: POST /stage/enter { userId, token, data:{ act, difficulty, stage } }
    Ctrl->>Svc: 스테이지 진입 처리 요청(대상 좌표)
    Svc->>MD: 해당 좌표의 스테이지 정의 조회
    alt 마스터 미로드 / 스테이지 없음
        Svc-->>Ctrl: MasterDataNotLoaded(10001) / StageNotFound(6001)
    else 스테이지 존재
        Svc->>Repo: 플레이어 진행도 조회 요청
        Repo->>DB: 플레이어 진행도 데이터 확인
        alt 세이브 없음
            Svc-->>Ctrl: SaveNotFound(2001)
        else 도달 검증(이미 클리어 or 프런티어+1)
            alt 도달 불가(잠김)
                Svc-->>Ctrl: StageLocked(6002)
            else 허용
                Svc->>Repo: 현재 진입 스테이지 기록 요청
                Repo->>DB: 현재 진입 스테이지 데이터 갱신
                Svc-->>Ctrl: Success + { 스폰·보스·배경타입 }
            end
        end
    end
    Ctrl-->>C: { success, errorCode, message, data }
```

## POST /api/game/stage/clear — 스테이지 클리어(보상 지급)

```mermaid
sequenceDiagram
    autonumber
    actor C as 클라이언트
    participant Ctrl as GameStageController
    participant Svc as StageService
    participant MD as MasterDataProvider
    participant Repo as StageRepository
    participant DB as MySQL(game)

    C->>Ctrl: POST /stage/clear { userId, token, data:{ act, difficulty, stage } }
    Ctrl->>Svc: 스테이지 클리어 처리 요청(대상 좌표)
    Svc->>MD: 스테이지 정의·클리어 보상 정의 조회
    alt 스테이지·보상 정의 없음
        Svc-->>Ctrl: StageNotFound(6001) / MasterDataNotLoaded(10001)
    else 정의 있음
        Svc->>MD: 전리품 추첨(등급 확률, 서버 RNG)
        MD-->>Svc: 전리품 or 미드롭
        Svc->>Repo: 클리어 반영 요청(좌표·지급 골드·전리품 + 레벨 재계산 규칙 전달)
        Note over Repo,DB: 단일 트랜잭션
        Repo->>DB: 진입 스테이지 데이터 확인(재검증)
        Repo->>DB: 골드 재화 데이터 적립
        Repo->>DB: 3캐릭터 경험치·레벨 데이터 갱신(레벨별 요구 경험치 기준)
        Repo->>DB: 전리품 아이템 데이터 적재(스택/용량 규칙)
        Repo->>DB: 진행도 데이터 갱신(프런티어면 다음 스테이지 전진)
        alt 미진입 / 용량 초과
            Svc-->>Ctrl: StageNotEntered(6003) / InventoryFull(4002)
        else 성공
            Svc-->>Ctrl: Success + { 보상·캐릭터·잔액·진행도 }
        end
    end
    Ctrl-->>C: { success, errorCode, message, data }
```
