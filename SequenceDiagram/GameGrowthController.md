# GameGrowthController (`/api/game/growth`, GameServer)

성장 — 스킬 레벨업·초기화·액티브 장착·룬 업그레이드. 저장소: MySQL `taskbar_hero_game`(`player_skill`·`player_rune`·`player_character`·`player_item`) + 인메모리 마스터 데이터(skill·rune·level·rune_cost).

> **인증**: `/api/game/*` 요청은 `GameAuthMiddleware`가 토큰을 검증한다(실패 시 401). 주요 로직이 아니므로 아래 다이어그램에서는 생략하고, 인증된 `userId`로 처리가 시작된 이후를 표기한다.
>
> 스킬 포인트는 저장값이 아니라 레벨별 스킬 포인트 정의에서 파생(레벨 기준 총량 − 사용량)한다.

## POST /api/game/growth/skill/levelup — 스킬 레벨업

```mermaid
sequenceDiagram
    autonumber
    actor C as 클라이언트
    participant S as GameServer
    participant DB as MySQL(game)

    C->>S: POST /skill/levelup { userId, token, data:{ characterId, skillCode } }
    S->>S: 마스터 데이터 확인(인메모리) — 스킬 정의(직업 소속·최대 레벨)
    alt 잘못된 대상 / 직업 불일치 / 최대 레벨
        S-->>C: 실패 { errorCode: InvalidGrowthTarget(5001) / SkillClassMismatch(5004) / SkillMaxLevel(5002) }
    else 유효
        Note over S,DB: 단일 트랜잭션
        S->>DB: 캐릭터 레벨·현재 스킬 레벨·사용 포인트 데이터 확인
        S->>S: 가용 스킬 포인트 계산(레벨 파생 총량 − 사용량)
        alt 포인트 부족
            S-->>C: 실패 { errorCode: InsufficientSkillPoint(5003) }
        else 충분
            S->>DB: 스킬 레벨 데이터 갱신(+1, 첫 습득이면 신규 적재)
            S-->>C: 성공 { 올린 레벨, 소모 포인트, 잔여 포인트 }
        end
    end
```

## POST /api/game/growth/skill/reset — 스킬 초기화

```mermaid
sequenceDiagram
    autonumber
    actor C as 클라이언트
    participant S as GameServer
    participant DB as MySQL(game)

    C->>S: POST /skill/reset { userId, token, data:{ characterId } }
    alt 잘못된 캐릭터
        S-->>C: 실패 { errorCode: InvalidCharacterId(2006) }
    else 유효
        Note over S,DB: 단일 트랜잭션(회수 후 총 포인트 산출은 서버가 수행)
        S->>DB: 해당 캐릭터 스킬 데이터 삭제(포인트 전액 회수·장착 해제)
        S-->>C: 성공 { 초기화된 스킬 수, 잔여 포인트(전액 복구) }
    end
```

## POST /api/game/growth/skill/equip — 액티브 스킬 장착(0~2개)

```mermaid
sequenceDiagram
    autonumber
    actor C as 클라이언트
    participant S as GameServer
    participant DB as MySQL(game)

    C->>S: POST /skill/equip { userId, token, data:{ characterId, skillCodes[] } }
    S->>S: 마스터 데이터 확인(인메모리) — 스킬 정의(액티브 여부·직업)
    S->>S: 검증(액티브만·최대 2개·습득 레벨 ≥ 1)
    alt 한도 초과 / 미습득 / 패시브 / 직업 불일치
        S-->>C: 실패 { errorCode: ActiveSkillLimitExceeded(5007) / SkillNotLearned(5006) / SkillNotActive(5005) / SkillClassMismatch(5004) }
    else 유효
        Note over S,DB: 단일 트랜잭션
        S->>DB: 스킬 장착 데이터 재설정(전량 해제 후 요청 목록만 장착)
        S-->>C: 성공 { 장착된 스킬 목록 }
    end
```

## POST /api/game/growth/rune/upgrade — 룬 업그레이드(골드 소모)

```mermaid
sequenceDiagram
    autonumber
    actor C as 클라이언트
    participant S as GameServer
    participant DB as MySQL(game)

    C->>S: POST /rune/upgrade { userId, token, data:{ runeCode } }
    S->>S: 마스터 데이터 확인(인메모리) — 룬 정의(선행 룬·최대 레벨) + 현재 레벨의 업그레이드 비용 산출
    Note over S,DB: 단일 트랜잭션
    S->>DB: 현재 룬 레벨·선행 룬 레벨·골드 데이터 확인
    alt 선행 미충족 / 최대 레벨 / 골드 부족
        S-->>C: 실패 { errorCode: RunePrereqNotMet(5010) / RuneMaxLevel(5011) / InsufficientCurrency(4005) }
    else 유효
        S->>DB: 골드 데이터 차감 + 룬 레벨 데이터 갱신(+1, 첫 해금이면 신규 적재)
        S-->>C: 성공 { 올린 레벨, 소모 골드, 잔액 }
    end
```
