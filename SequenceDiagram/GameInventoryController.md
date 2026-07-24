# GameInventoryController (`/api/game/inventory`, GameServer)

장비 장착·해제·배치 이동·용량 확장. 모든 요청은 `GameAuthMiddleware` 인증을 거친다. 저장소: MySQL `taskbar_hero_game`(`player_item`·`player_item_equipped`·`game_player`) + `MasterDataProvider`(item·확장 비용).

> **공통 인증**: 첫 단계는 `GameAuthMiddleware`의 Redis 토큰 대조(실패 시 401). 아래는 인증 통과 이후를 표기한다.

## POST /api/game/inventory/equip — 장착(스왑)

```mermaid
sequenceDiagram
    autonumber
    actor C as 클라이언트
    participant MW as GameAuthMiddleware
    participant Ctrl as GameInventoryController
    participant Svc as InventoryService
    participant MD as MasterDataProvider
    participant Repo as InventoryRepository
    participant DB as MySQL(game)

    C->>MW: POST /equip { userId, token, data:{ characterId, itemId } }
    MW-->>Ctrl: 인증 통과(userId)
    Ctrl->>Svc: EquipAsync(userId, characterId, itemId)
    Svc->>MD: 아이템 정의(타입·슬롯·class_req·level_req) 조회
    Note over Svc,Repo: 단일 트랜잭션
    Repo->>DB: 대상 아이템·캐릭터 조회(소유·직업·레벨)
    alt 미보유 / 장비 아님·슬롯·클래스·레벨 부적합 / 이미 장착 중 / 잘못된 캐릭터
        Svc-->>Ctrl: ItemNotFound(4001) / ItemNotEquippable(4003) / ItemEquipped(4007) / InvalidCharacterId(2006)
    else 장착 가능
        Repo->>DB: (기존 슬롯 장비 있으면) player_item_equipped DELETE (스왑)
        Repo->>DB: player_item_equipped INSERT(대상 캐릭터·슬롯)
        Svc-->>Ctrl: Success + { equipped, unequipped }
    end
    Ctrl-->>C: { success, errorCode, message, data }
```

## POST /api/game/inventory/unequip — 장착 해제

```mermaid
sequenceDiagram
    autonumber
    actor C as 클라이언트
    participant MW as GameAuthMiddleware
    participant Ctrl as GameInventoryController
    participant Svc as InventoryService
    participant Repo as InventoryRepository
    participant DB as MySQL(game)

    C->>MW: POST /unequip { userId, token, data:{ characterId, slot } }
    MW-->>Ctrl: 인증 통과(userId)
    Ctrl->>Svc: UnequipAsync(userId, characterId, slot)
    Repo->>DB: SELECT player_item_equipped(캐릭터·슬롯)
    alt 슬롯 비어 있음 / 잘못된 캐릭터
        Svc-->>Ctrl: ItemNotFound(4001) / InvalidCharacterId(2006)
    else 장착 중
        Repo->>DB: DELETE player_item_equipped(해당 장착 행)
        Svc-->>Ctrl: Success + { characterId, slot, itemId }
    end
    Ctrl-->>C: { success, errorCode, message, data }
```

## POST /api/game/inventory/move — 배치 이동/교환

```mermaid
sequenceDiagram
    autonumber
    actor C as 클라이언트
    participant MW as GameAuthMiddleware
    participant Ctrl as GameInventoryController
    participant Svc as InventoryService
    participant Repo as InventoryRepository
    participant DB as MySQL(game)

    C->>MW: POST /move { userId, token, data:{ itemId, toSlot } }
    MW-->>Ctrl: 인증 통과(userId)
    Ctrl->>Svc: MoveAsync(userId, itemId, toSlot)
    Repo->>DB: 대상 아이템·목표 칸 조회(소유·용량 범위)
    alt 대상 없음 / 잘못된 칸
        Svc-->>Ctrl: ItemNotFound(4001) / InvalidInventorySlot(4009)
    else 유효
        Note over Repo,DB: 단일 트랜잭션((user_id, slot) 유니크 보존)
        Repo->>DB: 두 행 slot 갱신(목표 비었으면 이동, 있으면 교환)
        Svc-->>Ctrl: Success + { moved, swapped }
    end
    Ctrl-->>C: { success, errorCode, message, data }
```

## POST /api/game/inventory/expand — 용량 1칸 확장(골드 소모)

```mermaid
sequenceDiagram
    autonumber
    actor C as 클라이언트
    participant MW as GameAuthMiddleware
    participant Ctrl as GameInventoryController
    participant Svc as InventoryService
    participant MD as MasterDataProvider
    participant Repo as InventoryRepository
    participant DB as MySQL(game)

    C->>MW: POST /expand { userId, token }
    MW-->>Ctrl: 인증 통과(userId)
    Ctrl->>Svc: ExpandAsync(userId)
    Repo->>DB: SELECT game_player.inventory_capacity
    Svc->>MD: PlanExpandOne(currentCapacity) → (가능 여부, 비용)
    alt 상한 도달
        Svc-->>Ctrl: InventoryCapacityMax(4008)
    else 확장 가능
        Note over Repo,DB: 단일 트랜잭션
        Repo->>DB: 골드 확인·차감
        alt 골드 부족
            Svc-->>Ctrl: InsufficientCurrency(4005)
        else 충분
            Repo->>DB: UPDATE game_player.inventory_capacity += 1
            Svc-->>Ctrl: Success + { inventoryCapacity, cost, balance }
        end
    end
    Ctrl-->>C: { success, errorCode, message, data }
```
