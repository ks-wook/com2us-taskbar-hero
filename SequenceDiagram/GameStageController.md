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
    Ctrl->>Svc: EnterAsync(userId, act, difficulty, stage)
    Svc->>MD: GetStage(act, difficulty, stage)
    alt 마스터 미로드 / 스테이지 없음
        Svc-->>Ctrl: MasterDataNotLoaded(10001) / StageNotFound(6001)
    else 스테이지 존재
        Svc->>Repo: GetProgressAsync(userId)
        Repo->>DB: 플레이어 진행도 데이터 확인
        alt 세이브 없음
            Svc-->>Ctrl: SaveNotFound(2001)
        else 도달 검증(이미 클리어 or 프런티어+1)
            alt 도달 불가(잠김)
                Svc-->>Ctrl: StageLocked(6002)
            else 허용
                Svc->>Repo: SetCurrentStageAsync(userId, act, difficulty, stage)
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
    Ctrl->>Svc: ClearAsync(userId, act, difficulty, stage)
    Svc->>MD: GetStage(...) / GetStageReward(stageId)
    alt 스테이지·보상 정의 없음
        Svc-->>Ctrl: StageNotFound(6001) / MasterDataNotLoaded(10001)
    else 정의 있음
        Svc->>MD: RollDrop(reward) (등급 확률 추첨, 서버 RNG)
        MD-->>Svc: 전리품 or 미드롭
        Svc->>Repo: ApplyClearAsync(userId, 좌표, gold, dropped, levelUp)
        Note over Repo,DB: 단일 트랜잭션
        Repo->>DB: 진입 스테이지 데이터 확인(재검증)
        Repo->>DB: 골드 재화 데이터 적립
        Repo->>DB: 3캐릭터 경험치·레벨 데이터 갱신(level_master 기준)
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
