# GameOfflineController (`/api/game/offline`, GameServer)

방치형 오프라인 보상 정산. 저장소: MySQL `taskbar_hero_game`(`game_player`·`player_character`·`player_item`) + `MasterDataProvider`(stage_reward·level). 중복 정산은 트랜잭션 + `last_active_at` CAS로 방지한다.

> **인증**: `/api/game/*` 요청은 컨트롤러 진입 전에 `GameAuthMiddleware`가 토큰을 검증한다(실패 시 401). 주요 로직이 아니므로 아래 다이어그램에서는 생략하고, 인증된 `userId`가 컨트롤러에 전달된 이후를 표기한다.

## POST /api/game/offline/claim — 오프라인 보상 정산

```mermaid
sequenceDiagram
    autonumber
    actor C as 클라이언트
    participant Ctrl as GameOfflineController
    participant Svc as OfflineService
    participant MD as MasterDataProvider
    participant Repo as OfflineRepository
    participant DB as MySQL(game)

    C->>Ctrl: POST /offline/claim { userId, token }
    Ctrl->>Svc: ClaimAsync(userId)
    Svc->>MD: IsLoaded 확인
    Svc->>Repo: GetContextAsync(userId)
    Repo->>DB: 기준 시각·현재 스테이지 데이터 확인
    alt 세이브 없음
        Svc-->>Ctrl: SaveNotFound(2001)
    else 존재
        Svc->>Svc: elapsed = now - last_active_at
        alt elapsed < 10분(최소 기준)
            Svc-->>Ctrl: NoOfflineReward(3001) — 200 OK, 미지급
        else 정산 대상
            Svc->>MD: 현재 스테이지 stage_reward(gold/exp) → 시간당 산출율
            Svc->>Repo: ClaimAsync(now, computeReward, applyExp)
            Note over Repo,DB: 단일 트랜잭션
            Repo->>DB: 기준 시각 데이터 재확인(트랜잭션 내부)
            Repo->>DB: 기준 시각 데이터 조건부 갱신(CAS — 관측값과 같을 때만 now로 리셋해 정산권 선점)
            alt 선점 실패(동시 요청이 먼저 정산)
                Svc-->>Ctrl: OfflineRewardAlreadyClaimed(3002) — 409
            else 정산권 선점
                Svc->>Svc: 12h 상한 적용 + 골드/경험치 = 산출율 × effective × 50%
                Repo->>DB: 골드 재화 데이터 적립
                Repo->>DB: 전 캐릭터 경험치·레벨 데이터 갱신(level_master 기준)
                Svc-->>Ctrl: Success + { offlineElapsedSec, effectiveSec, capped, rewards, characters, lastActiveAt }
            end
        end
    end
    Ctrl-->>C: { success, errorCode, message, data }
```
