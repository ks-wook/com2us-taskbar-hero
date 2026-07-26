# 시퀀스 다이어그램 (컨트롤러별)

각 API 컨트롤러의 요청 처리 흐름을 [Mermaid](https://mermaid.js.org/syntax/sequenceDiagram.html) `sequenceDiagram`으로 정리한다. GitHub·VS Code(Mermaid 지원)에서 렌더링된다.

## 기능별 컨트롤러 매핑

어떤 기능이 어떤 컨트롤러에서 처리되는지 정리한다. 컨트롤러 이름을 클릭하면 해당 시퀀스 다이어그램 문서로 이동한다.

| 기능 | 처리 컨트롤러 | 서버 | 주요 엔드포인트 |
|---|---|---|---|
| **로그인 / 인증** (회원가입·로그인·로그아웃) | [AuthController](AuthController.md) | Account | `POST /api/auth/signup` · `login` · `logout` |
| **세이브 로드 / 캐릭터 생성 / 접속 시각(heartbeat)** | [GameSaveController](GameSaveController.md) | Game | `POST /api/game/load` · `create-character` · `update-last-active` |
| **던전 입장 및 클리어 보상** (스테이지) | [GameStageController](GameStageController.md) | Game | `POST /api/game/stage/enter` · `clear` |
| **장비 장착·해제 / 인벤토리 배치·용량 확장** | [GameInventoryController](GameInventoryController.md) | Game | `POST /api/game/inventory/equip` · `unequip` · `move` · `expand` |
| **성장** (스킬 레벨업·초기화·장착 / 룬 업그레이드) | [GameGrowthController](GameGrowthController.md) | Game | `POST /api/game/growth/skill/levelup` · `skill/reset` · `skill/equip` · `rune/upgrade` |
| **큐브** (합성 / 분해=연금술 / 제작) | [GameCubeController](GameCubeController.md) | Game | `POST /api/game/cube/combine` · `dismantle` · `craft` |
| **방치형 오프라인 보상 정산** | [GameOfflineController](GameOfflineController.md) | Game | `POST /api/game/offline/claim` |

## 공통 아키텍처

- **계층**: `Controller`(HTTP 액션) → `Service`(검증·규칙·RNG) → `Repository`(SqlKata + MySqlConnector) → **MySQL**. 정적 수치는 `MasterDataProvider`(기동 시 마스터 DB에서 인메모리 적재)로 조회한다.
  - 단, 시퀀스 다이어그램은 이 내부 계층을 **하나의 서버 참여자로 요약**한다(아래 「참여자 표기」 참고).
- **응답 형식**: 모든 API가 `{ success, errorCode, message, data }`. `success == (errorCode == 0)`. 에러 코드는 `TaskbarHero.Common.ErrorCode`(서버-클라 공유 계약).
- **DB(3종)**: `taskbar_hero_account`(계정), `taskbar_hero_game`(세이브/진행), `taskbar_hero_master`(마스터, 인메모리 적재 원본). **Redis**: 인증 토큰(`auth:token:{userId}`).
- **인증(GameServer 전용)**: `/api/game/*` 요청은 `GameAuthMiddleware`가 body의 `userId`·`token`을 읽어 Redis 토큰과 대조한다. 실패 시 401. 성공 시 `userId`를 컨트롤러로 전달한다. AccountServer의 `signup`·`login`은 무인증, `logout`은 서버가 토큰을 대조한다.
  - 이 미들웨어 검증은 각 기능의 주요 로직이 아니므로 **모든 시퀀스 다이어그램에서 생략**하고, 인증된 `userId`로 처리가 시작된 이후부터 표기한다.
- **트랜잭션**: 재화·상태 변경은 `user_id` 단위 단일 트랜잭션으로 원자적으로 반영한다(중도 실패 시 롤백).

## 참여자 표기

다이어그램은 **프로세스 단위**로 참여자를 둔다. 서버 내부의 `Controller`·`Service`·`Repository`·`Cache`는 하나의 서버 참여자로 합쳐, 기능 흐름(무엇을 검증하고 무엇을 저장하는가)이 계층 왕복에 묻히지 않게 한다.

| 참여자 | 의미 |
|---|---|
| `클라이언트` | Unity 클라이언트(actor) |
| `AccountServer` | 계정/인증 서버 프로세스(컨트롤러·서비스·리포지토리·토큰 캐시 포함) |
| `GameServer` | 게임 로직 서버 프로세스(컨트롤러·서비스·리포지토리 포함) |
| `MySQL(account)` / `MySQL(game)` | 각 서버가 쓰는 MySQL 스키마 |
| `Redis` | 인증 토큰 캐시(`auth:token:{userId}`) |

- **마스터 데이터**는 서버 프로세스 안의 인메모리 구조(`MasterDataProvider`)이므로 별도 참여자로 두지 않고 **서버 자기호출**(`S->>S: 마스터 데이터 확인(인메모리) — ...`)로 표기한다. 검증·계산 등 서버 내부 판정도 같은 방식이다.
- 서버 계층 구조의 정본은 코드와 위 「공통 아키텍처」이며, 어느 클래스가 무엇을 하는지는 다이어그램이 아니라 코드/XML 주석에서 확인한다.

## 범례

- `alt`/`opt` 블록은 분기(에러/조건)를 나타낸다. 각 다이어그램은 대표 경로 중심이며, 세부 에러 코드는 해당 기획서를 정본으로 한다.
- **응답 표기**: 서버 → 클라이언트 응답은 `성공 { ... }` / `실패 { errorCode: ... }`로 줄여 쓴다. 실제 전문은 항상 공통 응답 형식 `{ success, errorCode, message, data }`이며, 중괄호 안은 그중 핵심 필드만 나타낸다. `errorCode`(예: `StageNotFound(6001)`)는 클라이언트와 공유하는 계약이므로 코드명을 그대로 쓴다.
- **저장소 접근 표기**: DB·Redis로 향하는 화살표는 SQL/명령문(`SELECT`·`INSERT`·`UPDATE`·`DELETE` 등)을 쓰지 않고 「~ 데이터 확인 / 적재 / 갱신 / 차감 / 삭제」처럼 수행하는 일로 표기한다. 실제 쿼리는 각 리포지토리 코드와 그 XML 주석을 정본으로 한다.
- **호출 표기**: 화살표 라벨에 메서드명(`EnterAsync`·`GetStage` 등)을 쓰지 않고 「스테이지 진입 처리」처럼 그 호출이 무엇을 하는지로 표기한다.
- **트랜잭션 경계**는 `Note over S,DB: 단일 트랜잭션`으로 표기한다.
