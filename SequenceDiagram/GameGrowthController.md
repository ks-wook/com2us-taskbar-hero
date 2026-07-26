# GameGrowthController (`/api/game/growth`, GameServer)

성장 — 스킬 레벨업·초기화·액티브 장착·룬 업그레이드. 저장소: MySQL `taskbar_hero_game`(`player_skill`·`player_rune`·`player_character`·`player_item`) + `MasterDataProvider`(skill·rune·level·rune_cost).

> **인증**: `/api/game/*` 요청은 컨트롤러 진입 전에 `GameAuthMiddleware`가 토큰을 검증한다(실패 시 401). 주요 로직이 아니므로 아래 다이어그램에서는 생략하고, 인증된 `userId`가 컨트롤러에 전달된 이후를 표기한다.
>
> 스킬 포인트는 저장값이 아니라 `level_master.skill_points`에서 파생(레벨 기준 총량 − 사용량)한다.

## POST /api/game/growth/skill/levelup — 스킬 레벨업

```mermaid
sequenceDiagram
    autonumber
    actor C as 클라이언트
    participant Ctrl as GameGrowthController
    participant Svc as GrowthService
    participant MD as MasterDataProvider
    participant Repo as GrowthRepository
    participant DB as MySQL(game)

    C->>Ctrl: POST /skill/levelup { userId, token, data:{ characterId, skillCode } }
    Ctrl->>Svc: SkillLevelUpAsync(userId, characterId, skillCode)
    Svc->>MD: 스킬 정의(직업 소속·최대 레벨) 조회
    alt 잘못된 대상 / 직업 불일치 / 최대 레벨
        Svc-->>Ctrl: InvalidGrowthTarget(5001) / SkillClassMismatch(5004) / SkillMaxLevel(5002)
    else 유효
        Note over Repo,DB: 단일 트랜잭션
        Repo->>DB: 캐릭터 레벨·현재 스킬 레벨·사용 포인트 데이터 확인
        Svc->>Svc: 가용 스킬 포인트 계산(레벨 파생 총량 − 사용량)
        alt 포인트 부족
            Svc-->>Ctrl: InsufficientSkillPoint(5003)
        else 충분
            Repo->>DB: 스킬 레벨 데이터 갱신(+1, 첫 습득이면 신규 적재)
            Svc-->>Ctrl: Success + { level, cost, skillPoint }
        end
    end
    Ctrl-->>C: { success, errorCode, message, data }
```

## POST /api/game/growth/skill/reset — 스킬 초기화

```mermaid
sequenceDiagram
    autonumber
    actor C as 클라이언트
    participant Ctrl as GameGrowthController
    participant Svc as GrowthService
    participant Repo as GrowthRepository
    participant DB as MySQL(game)

    C->>Ctrl: POST /skill/reset { userId, token, data:{ characterId } }
    Ctrl->>Svc: SkillResetAsync(userId, characterId)
    alt 잘못된 캐릭터
        Svc-->>Ctrl: InvalidCharacterId(2006)
    else 유효
        Note over Repo,DB: 단일 트랜잭션
        Repo->>DB: 해당 캐릭터 스킬 데이터 삭제(포인트 전액 회수·장착 해제)
        Svc-->>Ctrl: Success + { resetSkillCount, skillPoint(전액 복구) }
    end
    Ctrl-->>C: { success, errorCode, message, data }
```

## POST /api/game/growth/skill/equip — 액티브 스킬 장착(0~2개)

```mermaid
sequenceDiagram
    autonumber
    actor C as 클라이언트
    participant Ctrl as GameGrowthController
    participant Svc as GrowthService
    participant MD as MasterDataProvider
    participant Repo as GrowthRepository
    participant DB as MySQL(game)

    C->>Ctrl: POST /skill/equip { userId, token, data:{ characterId, skillCodes[] } }
    Ctrl->>Svc: SkillEquipAsync(userId, characterId, skillCodes)
    Svc->>MD: 스킬 정의(액티브 여부·직업) 조회
    Svc->>Svc: 검증(액티브만·최대 2개·습득 레벨 ≥ 1)
    alt 한도 초과 / 미습득 / 패시브 / 직업 불일치
        Svc-->>Ctrl: ActiveSkillLimitExceeded(5007) / SkillNotLearned(5006) / SkillNotActive(5005) / SkillClassMismatch(5004)
    else 유효
        Note over Repo,DB: 단일 트랜잭션
        Repo->>DB: 스킬 장착 데이터 재설정(전량 해제 후 요청 목록만 장착)
        Svc-->>Ctrl: Success + { equipped[] }
    end
    Ctrl-->>C: { success, errorCode, message, data }
```

## POST /api/game/growth/rune/upgrade — 룬 업그레이드(골드 소모)

```mermaid
sequenceDiagram
    autonumber
    actor C as 클라이언트
    participant Ctrl as GameGrowthController
    participant Svc as GrowthService
    participant MD as MasterDataProvider
    participant Repo as GrowthRepository
    participant DB as MySQL(game)

    C->>Ctrl: POST /rune/upgrade { userId, token, data:{ runeCode } }
    Ctrl->>Svc: RuneUpgradeAsync(userId, runeCode)
    Svc->>MD: 룬 정의(선행 룬·최대 레벨) + RuneUpgradeCost(현재 레벨) 조회
    Note over Repo,DB: 단일 트랜잭션
    Repo->>DB: 현재 룬 레벨·선행 룬 레벨·골드 데이터 확인
    alt 선행 미충족 / 최대 레벨 / 골드 부족
        Svc-->>Ctrl: RunePrereqNotMet(5010) / RuneMaxLevel(5011) / InsufficientCurrency(4005)
    else 유효
        Repo->>DB: 골드 데이터 차감 + 룬 레벨 데이터 갱신(+1, 첫 해금이면 신규 적재)
        Svc-->>Ctrl: Success + { level, cost, balance }
    end
    Ctrl-->>C: { success, errorCode, message, data }
```
