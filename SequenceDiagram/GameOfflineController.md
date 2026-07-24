# GameOfflineController (`/api/game/offline`, GameServer)

방치형 오프라인 보상 정산. 요청은 `GameAuthMiddleware` 인증을 거친다. 저장소: MySQL `taskbar_hero_game`(`game_player`·`player_character`·`player_item`) + `MasterDataProvider`(stage_reward·level). 중복 정산은 트랜잭션 + `last_active_at` CAS로 방지한다.

> **공통 인증**: 첫 단계는 `GameAuthMiddleware`의 Redis 토큰 대조(실패 시 401). 아래는 인증 통과 이후를 표기한다.

## POST /api/game/offline/claim — 오프라인 보상 정산

```mermaid
sequenceDiagram
    autonumber
    actor C as 클라이언트
    participant MW as GameAuthMiddleware
    participant Ctrl as GameOfflineController
    participant Svc as OfflineService
    participant MD as MasterDataProvider
    participant Repo as OfflineRepository
    participant DB as MySQL(game)

    C->>MW: POST /offline/claim { userId, token }
    MW-->>Ctrl: 인증 통과(userId)
    Ctrl->>Svc: ClaimAsync(userId)
    Svc->>MD: IsLoaded 확인
    Svc->>Repo: GetContextAsync(userId)
    Repo->>DB: SELECT game_player(last_active_at·현재 스테이지)
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
            Repo->>DB: last_active_at 재확인
            Repo->>DB: CAS UPDATE last_active_at = now WHERE last_active_at = 관측값
            alt CAS 0행(동시 요청이 먼저 정산)
                Svc-->>Ctrl: OfflineRewardAlreadyClaimed(3002) — 409
            else 정산권 선점
                Svc->>Svc: 12h 상한 적용 + 골드/경험치 = 산출율 × effective × 50%
                Repo->>DB: 골드 적립(재화 행)
                Repo->>DB: 전 캐릭터 경험치 지급 + 레벨 재계산(level_master)
                Svc-->>Ctrl: Success + { offlineElapsedSec, effectiveSec, capped, rewards, characters, lastActiveAt }
            end
        end
    end
    Ctrl-->>C: { success, errorCode, message, data }
```
