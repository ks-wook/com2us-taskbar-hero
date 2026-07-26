# GameStageController (`/api/game/stage`, GameServer)

스테이지 진입·클리어(서버 권위 보상 산출). 저장소: MySQL `taskbar_hero_game` + 인메모리 마스터 데이터(stage/reward/level/item).

> **인증**: `/api/game/*` 요청은 `GameAuthMiddleware`가 토큰을 검증한다(실패 시 401). 주요 로직이 아니므로 아래 다이어그램에서는 생략하고, 인증된 `userId`로 처리가 시작된 이후를 표기한다.

## POST /api/game/stage/enter — 스테이지 진입

```mermaid
sequenceDiagram
    autonumber
    actor C as 클라이언트
    participant S as GameServer
    participant DB as MySQL(game)

    C->>S: POST /stage/enter { userId, token, data:{ act, difficulty, stage } }
    S->>S: 마스터 데이터 확인(인메모리) — 해당 좌표의 스테이지 정의
    alt 마스터 미로드 / 스테이지 없음
        S-->>C: 실패 { errorCode: MasterDataNotLoaded(10001) / StageNotFound(6001) }
    else 스테이지 존재
        S->>DB: 플레이어 진행도 데이터 확인
        alt 세이브 없음
            S-->>C: 실패 { errorCode: SaveNotFound(2001) }
        else 도달 검증(이미 클리어 or 프런티어+1)
            alt 도달 불가(잠김)
                S-->>C: 실패 { errorCode: StageLocked(6002) }
            else 허용
                S->>DB: 현재 진입 스테이지 데이터 갱신
                S-->>C: 성공 { 스폰·보스·배경타입 }
            end
        end
    end
```

## POST /api/game/stage/clear — 스테이지 클리어(보상 지급)

```mermaid
sequenceDiagram
    autonumber
    actor C as 클라이언트
    participant S as GameServer
    participant DB as MySQL(game)

    C->>S: POST /stage/clear { userId, token, data:{ act, difficulty, stage } }
    S->>S: 마스터 데이터 확인(인메모리) — 스테이지 정의·클리어 보상 정의
    alt 스테이지·보상 정의 없음
        S-->>C: 실패 { errorCode: StageNotFound(6001) / MasterDataNotLoaded(10001) }
    else 정의 있음
        S->>S: 전리품 추첨(등급 확률, 서버 RNG) → 전리품 or 미드롭
        Note over S,DB: 단일 트랜잭션
        S->>DB: 진입 스테이지 데이터 확인(재검증)
        S->>DB: 골드 재화 데이터 적립
        S->>DB: 3캐릭터 경험치·레벨 데이터 갱신(레벨별 요구 경험치 기준)
        S->>DB: 전리품 아이템 데이터 적재(스택/용량 규칙)
        S->>DB: 진행도 데이터 갱신(프런티어면 다음 스테이지 전진)
        alt 미진입 / 용량 초과
            S-->>C: 실패 { errorCode: StageNotEntered(6003) / InventoryFull(4002) }
        else 성공
            S-->>C: 성공 { 보상·캐릭터·잔액·진행도 }
        end
    end
```
