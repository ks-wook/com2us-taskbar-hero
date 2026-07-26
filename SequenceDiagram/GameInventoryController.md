# GameInventoryController (`/api/game/inventory`, GameServer)

장비 장착·해제·배치 이동·용량 확장. 저장소: MySQL `taskbar_hero_game`(`player_item`·`player_item_equipped`·`game_player`) + `MasterDataProvider`(item·확장 비용).

> **인증**: `/api/game/*` 요청은 컨트롤러 진입 전에 `GameAuthMiddleware`가 토큰을 검증한다(실패 시 401). 주요 로직이 아니므로 아래 다이어그램에서는 생략하고, 인증된 `userId`가 컨트롤러에 전달된 이후를 표기한다.

## POST /api/game/inventory/equip — 장착(스왑)

```mermaid
sequenceDiagram
    autonumber
    actor C as 클라이언트
    participant Ctrl as GameInventoryController
    participant Svc as InventoryService
    participant MD as MasterDataProvider
    participant Repo as InventoryRepository
    participant DB as MySQL(game)

    C->>Ctrl: POST /equip { userId, token, data:{ characterId, itemId } }
    Ctrl->>Svc: EquipAsync(userId, characterId, itemId)
    Svc->>MD: 아이템 정의(타입·슬롯·class_req·level_req) 조회
    Note over Svc,Repo: 단일 트랜잭션
    Repo->>DB: 대상 아이템·캐릭터 데이터 확인(소유·직업·레벨)
    alt 미보유 / 장비 아님·슬롯·클래스·레벨 부적합 / 이미 장착 중 / 잘못된 캐릭터
        Svc-->>Ctrl: ItemNotFound(4001) / ItemNotEquippable(4003) / ItemEquipped(4007) / InvalidCharacterId(2006)
    else 장착 가능
        Repo->>DB: (기존 슬롯 장비 있으면) 장착 데이터 삭제(스왑)
        Repo->>DB: 장착 데이터 적재(대상 캐릭터·슬롯)
        Svc-->>Ctrl: Success + { equipped, unequipped }
    end
    Ctrl-->>C: { success, errorCode, message, data }
```

## POST /api/game/inventory/unequip — 장착 해제

```mermaid
sequenceDiagram
    autonumber
    actor C as 클라이언트
    participant Ctrl as GameInventoryController
    participant Svc as InventoryService
    participant Repo as InventoryRepository
    participant DB as MySQL(game)

    C->>Ctrl: POST /unequip { userId, token, data:{ characterId, slot } }
    Ctrl->>Svc: UnequipAsync(userId, characterId, slot)
    Repo->>DB: 해당 캐릭터·슬롯의 장착 데이터 확인
    alt 슬롯 비어 있음 / 잘못된 캐릭터
        Svc-->>Ctrl: ItemNotFound(4001) / InvalidCharacterId(2006)
    else 장착 중
        Repo->>DB: 장착 데이터 삭제(아이템은 인벤토리에 잔존)
        Svc-->>Ctrl: Success + { characterId, slot, itemId }
    end
    Ctrl-->>C: { success, errorCode, message, data }
```

## POST /api/game/inventory/move — 배치 이동/교환

```mermaid
sequenceDiagram
    autonumber
    actor C as 클라이언트
    participant Ctrl as GameInventoryController
    participant Svc as InventoryService
    participant Repo as InventoryRepository
    participant DB as MySQL(game)

    C->>Ctrl: POST /move { userId, token, data:{ itemId, toSlot } }
    Ctrl->>Svc: MoveAsync(userId, itemId, toSlot)
    Repo->>DB: 대상 아이템·목표 칸 데이터 확인(소유·용량 범위)
    alt 대상 없음 / 잘못된 칸
        Svc-->>Ctrl: ItemNotFound(4001) / InvalidInventorySlot(4009)
    else 유효
        Note over Repo,DB: 단일 트랜잭션(계정-칸 유니크 제약 보존)
        Repo->>DB: 두 아이템의 칸 데이터 갱신(목표 비었으면 이동, 차 있으면 교환)
        Svc-->>Ctrl: Success + { moved, swapped }
    end
    Ctrl-->>C: { success, errorCode, message, data }
```

## POST /api/game/inventory/expand — 용량 1칸 확장(골드 소모)

```mermaid
sequenceDiagram
    autonumber
    actor C as 클라이언트
    participant Ctrl as GameInventoryController
    participant Svc as InventoryService
    participant MD as MasterDataProvider
    participant Repo as InventoryRepository
    participant DB as MySQL(game)

    C->>Ctrl: POST /expand { userId, token }
    Ctrl->>Svc: ExpandAsync(userId)
    Repo->>DB: 현재 인벤토리 용량 데이터 확인
    Svc->>MD: PlanExpandOne(currentCapacity) → (가능 여부, 비용)
    alt 상한 도달
        Svc-->>Ctrl: InventoryCapacityMax(4008)
    else 확장 가능
        Note over Repo,DB: 단일 트랜잭션
        Repo->>DB: 골드 데이터 확인·차감
        alt 골드 부족
            Svc-->>Ctrl: InsufficientCurrency(4005)
        else 충분
            Repo->>DB: 인벤토리 용량 데이터 갱신(+1칸)
            Svc-->>Ctrl: Success + { inventoryCapacity, cost, balance }
        end
    end
    Ctrl-->>C: { success, errorCode, message, data }
```
