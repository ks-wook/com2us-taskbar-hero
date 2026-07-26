# GameCubeController (`/api/game/cube`, GameServer)

큐브 — 합성·분해·제작(서버 권위 결과 산출). 저장소: MySQL `taskbar_hero_game`(`player_item`·`player_cube`) + 인메모리 마스터 데이터(cube_master·item·recipe). 연산마다 큐브 경험치가 쌓여 레벨업한다.

> **인증**: `/api/game/*` 요청은 `GameAuthMiddleware`가 토큰을 검증한다(실패 시 401). 주요 로직이 아니므로 아래 다이어그램에서는 생략하고, 인증된 `userId`로 처리가 시작된 이후를 표기한다.

## POST /api/game/cube/combine — 합성(동급 3개 → 상위 등급 1개)

```mermaid
sequenceDiagram
    autonumber
    actor C as 클라이언트
    participant S as GameServer
    participant DB as MySQL(game)

    C->>S: POST /cube/combine { userId, token, data:{ itemIds[] } }
    Note over S,DB: 단일 트랜잭션
    S->>DB: 입력 아이템 데이터 확인(소유·미장착)
    S->>S: 마스터 데이터 확인(인메모리) — 합성 규칙(필요 개수·등급 상승) + 입력 등급 일치 검증(장착 슬롯·직업 무관)
    S->>S: 상위 등급 결과 아이템 추첨(같은 등급대 무작위, 서버 RNG)
    alt 미보유 / 장착 중 / 조건 미충족(등급·개수·최대 등급)
        S-->>C: 실패 { errorCode: ItemNotFound(4001) / ItemEquipped(4007) / CubeRecipeNotMet(4010) }
    else 성공
        S->>DB: 입력 아이템 데이터 삭제 + 결과 아이템 데이터 적재(빈 칸)
        S->>DB: 큐브 경험치·레벨 데이터 갱신(50 × 입력등급 누적)
        S-->>C: 성공 { 소모한 아이템, 결과 아이템, 큐브 상태 }
    end
```

## POST /api/game/cube/dismantle — 분해(아이템 → 골드)

```mermaid
sequenceDiagram
    autonumber
    actor C as 클라이언트
    participant S as GameServer
    participant DB as MySQL(game)

    C->>S: POST /cube/dismantle { userId, token, data:{ items[]{ itemId, count } } }
    Note over S,DB: 단일 트랜잭션
    S->>DB: 각 아이템 데이터 확인(소유·미장착·수량)
    S->>S: 마스터 데이터 확인(인메모리) — 큐브 레벨별 분해 골드 계수·아이템 등급
    S->>S: 골드 = Σ(분해 계수 × 등급 × 개수), 큐브 경험치 = Σ(20 × 등급 × 개수)
    alt 미보유 / 수량 부족 / 장착 중
        S-->>C: 실패 { errorCode: ItemNotFound(4001) / InsufficientQuantity(4006) / ItemEquipped(4007) }
    else 성공
        S->>DB: 아이템 수량 데이터 차감 + 골드 재화 데이터 적립 + 큐브 경험치·레벨 데이터 갱신
        S-->>C: 성공 { 획득 골드, 획득 큐브 경험치 }
    end
```

## POST /api/game/cube/craft — 제작(레시피)

```mermaid
sequenceDiagram
    autonumber
    actor C as 클라이언트
    participant S as GameServer
    participant DB as MySQL(game)

    C->>S: POST /cube/craft { userId, token, data:{ recipeCode } }
    S->>S: 마스터 데이터 확인(인메모리) — 레시피 정의(결과 아이템·요구 큐브 레벨·비용·소모 재료)
    alt 레시피 없음
        S-->>C: 실패 { errorCode: CubeRecipeNotMet(4010) }
    else 존재
        Note over S,DB: 단일 트랜잭션
        S->>DB: 큐브 레벨·골드·재료 보유 데이터 확인
        alt 큐브 레벨 미달 / 골드 부족 / 재료 부족 / 용량 부족
            S-->>C: 실패 { errorCode: CubeLevelInsufficient(4011) / InsufficientCurrency(4005) / CubeRecipeNotMet(4010) / InventoryFull(4002) }
        else 충족
            S->>DB: 골드·재료 데이터 차감 + 결과 아이템 데이터 적재 + 큐브 경험치 데이터 갱신(+20)
            S-->>C: 성공 { 소모한 골드·재료, 획득 아이템, 큐브 상태 }
        end
    end
```
