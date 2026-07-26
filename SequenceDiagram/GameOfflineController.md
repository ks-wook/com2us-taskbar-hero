# GameOfflineController (`/api/game/offline`, GameServer)

방치형 오프라인 보상 정산. 저장소: MySQL `taskbar_hero_game`(`game_player`·`player_character`·`player_item`) + 인메모리 마스터 데이터(stage_reward·level). 중복 정산은 트랜잭션 + `last_active_at` CAS로 방지한다.

> **인증**: `/api/game/*` 요청은 `GameAuthMiddleware`가 토큰을 검증한다(실패 시 401). 주요 로직이 아니므로 아래 다이어그램에서는 생략하고, 인증된 `userId`로 처리가 시작된 이후를 표기한다.

## POST /api/game/offline/claim — 오프라인 보상 정산

```mermaid
sequenceDiagram
    autonumber
    actor C as 클라이언트
    participant S as GameServer
    participant DB as MySQL(game)

    C->>S: POST /offline/claim { userId, token }
    S->>S: 마스터 데이터 확인(인메모리) — 로드 상태
    S->>DB: 기준 시각·현재 파밍 스테이지 데이터 확인
    alt 세이브 없음
        S-->>C: 실패 { errorCode: SaveNotFound(2001) }
    else 존재
        S->>S: 방치 경과 시간 계산(현재 시각 − 마지막 활동 시각)
        alt 경과 < 10분(최소 기준)
            S-->>C: 미지급 { errorCode: NoOfflineReward(3001) } — 200 OK
        else 정산 대상
            S->>S: 마스터 데이터 확인(인메모리) — 현재 파밍 스테이지의 클리어 보상 → 시간당 산출율 환산
            Note over S,DB: 단일 트랜잭션
            S->>DB: 기준 시각 데이터 재확인(트랜잭션 내부)
            S->>DB: 기준 시각 데이터 조건부 갱신(관측값과 같을 때만 현재로 리셋해 정산권 선점)
            alt 선점 실패(동시 요청이 먼저 정산)
                S-->>C: 실패 { errorCode: OfflineRewardAlreadyClaimed(3002) } — 409
            else 정산권 선점
                S->>S: 방치 12시간 상한 적용 후 골드·경험치 산출(산출율 × 유효 시간 × 50%)
                S->>DB: 골드 재화 데이터 적립
                S->>DB: 전 캐릭터 경험치·레벨 데이터 갱신(레벨별 요구 경험치 기준)
                S-->>C: 성공 { 경과·유효 시간·상한 여부·보상·캐릭터·기준 시각 }
            end
        end
    end
```
