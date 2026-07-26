# GameCubeController (`/api/game/cube`, GameServer)

큐브 — 합성·분해·제작(서버 권위 결과 산출). 저장소: MySQL `taskbar_hero_game`(`player_item`·`player_cube`) + `MasterDataProvider`(cube_master·item·recipe). 연산마다 큐브 경험치가 쌓여 레벨업한다.

> **인증**: `/api/game/*` 요청은 컨트롤러 진입 전에 `GameAuthMiddleware`가 토큰을 검증한다(실패 시 401). 주요 로직이 아니므로 아래 다이어그램에서는 생략하고, 인증된 `userId`가 컨트롤러에 전달된 이후를 표기한다.

## POST /api/game/cube/combine — 합성(동급 3개 → 상위 등급 1개)

```mermaid
sequenceDiagram
    autonumber
    actor C as 클라이언트
    participant Ctrl as GameCubeController
    participant Svc as CubeService
    participant MD as MasterDataProvider
    participant Repo as CubeRepository
    participant DB as MySQL(game)

    C->>Ctrl: POST /cube/combine { userId, token, data:{ itemIds[] } }
    Ctrl->>Svc: 합성 처리 요청(입력 아이템 목록)
    Note over Repo,DB: 단일 트랜잭션(합성 판정·결과 산출은 서비스가 수행)
    Repo->>DB: 입력 아이템 데이터 확인(소유·미장착)
    Svc->>MD: 합성 규칙 조회(필요 개수·등급 상승) + 입력 등급 일치 검증(장착 슬롯·직업 무관)
    Svc->>MD: 상위 등급 결과 아이템 추첨(같은 등급대 무작위, 장착 슬롯·직업 무관)
    alt 미보유 / 장착 중 / 조건 미충족(등급·개수·최대 등급)
        Svc-->>Ctrl: ItemNotFound(4001) / ItemEquipped(4007) / CubeRecipeNotMet(4010)
    else 성공
        Repo->>DB: 입력 아이템 데이터 삭제 + 결과 아이템 데이터 적재(빈 칸)
        Repo->>DB: 큐브 경험치·레벨 데이터 갱신(50 × 입력등급 누적)
        Svc-->>Ctrl: Success + { 소모한 아이템, 결과 아이템, 큐브 상태 }
    end
    Ctrl-->>C: { success, errorCode, message, data }
```

## POST /api/game/cube/dismantle — 분해(아이템 → 골드)

```mermaid
sequenceDiagram
    autonumber
    actor C as 클라이언트
    participant Ctrl as GameCubeController
    participant Svc as CubeService
    participant MD as MasterDataProvider
    participant Repo as CubeRepository
    participant DB as MySQL(game)

    C->>Ctrl: POST /cube/dismantle { userId, token, data:{ items[]{ itemId, count } } }
    Ctrl->>Svc: 분해 처리 요청(대상 아이템·수량 목록)
    Note over Repo,DB: 단일 트랜잭션(보상 산출은 서비스가 수행)
    Repo->>DB: 각 아이템 데이터 확인(소유·미장착·수량)
    Svc->>MD: 큐브 레벨별 분해 골드 계수·아이템 등급 조회
    Svc->>Svc: 골드 = Σ(분해 계수 × 등급 × 개수), 큐브 경험치 = Σ(20 × 등급 × 개수)
    alt 미보유 / 수량 부족 / 장착 중
        Svc-->>Ctrl: ItemNotFound(4001) / InsufficientQuantity(4006) / ItemEquipped(4007)
    else 성공
        Repo->>DB: 아이템 수량 데이터 차감 + 골드 재화 데이터 적립 + 큐브 경험치·레벨 데이터 갱신
        Svc-->>Ctrl: Success + { 획득 골드, 획득 큐브 경험치 }
    end
    Ctrl-->>C: { success, errorCode, message, data }
```

## POST /api/game/cube/craft — 제작(레시피)

```mermaid
sequenceDiagram
    autonumber
    actor C as 클라이언트
    participant Ctrl as GameCubeController
    participant Svc as CubeService
    participant MD as MasterDataProvider
    participant Repo as CubeRepository
    participant DB as MySQL(game)

    C->>Ctrl: POST /cube/craft { userId, token, data:{ recipeCode } }
    Ctrl->>Svc: 제작 처리 요청(대상 레시피)
    Svc->>MD: 레시피 정의 조회(결과 아이템·요구 큐브 레벨·비용·소모 재료)
    alt 레시피 없음
        Svc-->>Ctrl: CubeRecipeNotMet(4010)
    else 존재
        Note over Repo,DB: 단일 트랜잭션
        Repo->>DB: 큐브 레벨·골드·재료 보유 데이터 확인
        alt 큐브 레벨 미달 / 골드 부족 / 재료 부족 / 용량 부족
            Svc-->>Ctrl: CubeLevelInsufficient(4011) / InsufficientCurrency(4005) / CubeRecipeNotMet(4010) / InventoryFull(4002)
        else 충족
            Repo->>DB: 골드·재료 데이터 차감 + 결과 아이템 데이터 적재 + 큐브 경험치 데이터 갱신(+20)
            Svc-->>Ctrl: Success + { 소모한 골드·재료, 획득 아이템, 큐브 상태 }
        end
    end
    Ctrl-->>C: { success, errorCode, message, data }
```
