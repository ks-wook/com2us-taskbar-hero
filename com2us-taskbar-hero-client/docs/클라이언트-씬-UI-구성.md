# Taskbar Hero 모작 — 클라이언트 씬 / 오브젝트 / UI 구성

> 본 문서는 **Unity 클라이언트**가 구현해야 할 씬, 게임 오브젝트, UI 화면의 큰 그림을 정리한다.
> 게임 전반과 서버 책임 범위는 [서버 시스템 전체 개요](../../docs/서버-시스템-전체-개요.md)를, API·에러코드 등 서버 계약은 해당 서버 문서(`../../docs/`)를 **서버 연결이 필요할 때만** 참조한다.
> 각 시스템의 데이터/수치는 서버 세부 기획서(`../../docs/세부/`)가 정본이며, 클라이언트는 표현(presentation)과 입력·보고만 담당한다.

## 1. 클라이언트 설계 원칙

- **서버 권위(server-authoritative)**: 재화·성장·오프라인 보상·전투 결과 등 모든 이득 계산의 최종 확정은 서버다. 클라이언트는 결과를 **표시**하고 사용자 액션을 **요청/보고**할 뿐, 값을 스스로 확정하지 않는다.
- **작업표시줄 기반 소형 화면**: 원작은 Windows 작업표시줄에 붙는 좁고 긴 창에서 돌아가는 방치형 RPG다. 모작 학습 프로젝트에서는 이를 **세로로 긴 소형 해상도(예: 세로형 창)** 기준의 URP 2D 레이아웃으로 재현하는 것을 기본 전제로 한다.
- **렌더/입력 스택**: URP 2D(`Renderer2D`, `Global Light 2D`) + 새 Input System(`Assets/Settings/InputSystem_Actions.inputactions`). 픽셀 아트 스프라이트 기반.
- **UI 우선**: 자동 전투 장르라 실시간 조작보다 **패널·팝업 UI 비중이 절대적**이다. 대부분의 화면은 별도 씬이 아니라 메인 씬 위의 UI 패널로 구성한다.
- **공통 계약 공유**: 서버와 주고받는 DTO·에러코드(`ErrorCode`)는 `TaskbarHero.Common`(netstandard2.0)을 그대로 사용하고 클라이언트에서 중복 정의하지 않는다.

## 2. 씬 구성 (Scene)

씬은 최소로 나눈다. 화면 전환의 대부분은 메인 씬 내 패널 토글로 처리하고, 물리적 씬 분리는 로딩/라우팅 경계에서만 둔다.

| 씬 | 역할 | 주요 진입/이탈 |
|---|---|---|
| **Bootstrap** | 앱 기동. 마스터 데이터 번들 로드, 저장된 인증 토큰 확인, 서버 헬스 체크 → 다음 씬 라우팅 | 앱 시작 → (토큰 유효) Main / (없음·만료) Login |
| **Login** | 계정 생성·로그인, 인증 토큰 발급 (AccountServer) | Bootstrap → Login → CharacterSelect |
| **CharacterSelect** | 캐릭터(직업) 생성·선택. 최초 진입 시 생성 화면 | Login → CharacterSelect → Main |
| **Main** | 핵심 게임 화면. 자동 전투 뷰 + HUD + 모든 기능 패널(인벤토리·성장·상점·메일·출석·스테이지 등) | 게임 플레이 전체 |

> **대안**: Login·CharacterSelect는 화면이 가벼우면 하나의 씬(패널 전환)으로 합칠 수 있다. Bootstrap/Main 분리는 유지 권장(로딩·라우팅 책임 분리).

현재 상태: `Assets/Scenes/SampleScene.unity` 하나만 존재하며 `Main Camera`, `Global Light 2D`만 있는 초기 스캐폴드. 위 씬들은 아직 미생성.

## 3. 씬별 오브젝트 / UI

### 3.1 Bootstrap 씬
- `Main Camera` (URP 2D), `EventSystem`
- `AppBootstrapper` — 초기화 순서 제어(마스터 데이터 로드 → 설정 로드 → 토큰 확인 → 라우팅)
- `LoadingView` — 진행 표시 UI

### 3.2 Login 씬 — *원작: `account-login-로그인화면`*
- `LoginCanvas`
  - 계정 생성 폼(아이디/비밀번호) · 로그인 폼
  - 로그인/회원가입 버튼, 에러 메시지 표시(서버 `ErrorCode` 매핑)
- 서버: AccountServer 인증 (토큰 발급 → 로컬 저장)

### 3.3 CharacterSelect 씬 — *원작: `save-data-캐릭터생성화면`*
- `CharacterCanvas`
  - 직업(클래스) 선택 리스트 (Knight/Ranger/Priest 등, `class_master` 기반)
  - 캐릭터 생성 / 기존 캐릭터 선택, 게임 시작 버튼
- 서버: GameServer 세이브 데이터 생성·로드

### 3.4 Main 씬 (핵심)

#### (A) 전투/월드 표현 레이어 — *원작: `stage-battle-일반몬스터`, `stage-battle-보스`*
자동 전투를 **보여주기만** 하는 연출 레이어. 결과는 서버 검증.
- `BattleStage` (루트)
  - `PartyRoot` — 플레이어 파티 캐릭터 스프라이트(직업별), 애니메이션(공격/피격/이동)
  - `MonsterRoot` — 일반 몬스터 / 보스 스폰
  - `ProjectileRoot`, `VfxRoot` — 투사체·이펙트
  - `BackgroundRoot` — 스테이지 배경(Act/난이도별)
- `Global Light 2D` (기존 유지), 필요 시 스포트/연출용 Light2D 추가
- `BattleCamera` — 전투 뷰 프레이밍

#### (B) 상시 HUD 레이어
- `HudCanvas`
  - 재화 표시(골드 등, `inventory-item-cube-골드`)
  - 캐릭터 레벨/경험치 바, 현재 스테이지·진행도
  - 하단/측면 **메뉴 바** — 각 기능 패널 열기 버튼(인벤토리/성장/상점/메일/출석/스테이지)
  - 알림 배지(신규 메일·수령 가능 출석 등)
  - **우측 상단 적용 중인 버프 아이콘**(`BuffIndicator`) — 활성 획득량 버프가 있을 때만 노출하고, hover 시 버프 종류·증가율·남은 시간·만료 시각을 툴팁으로 보여준다. 상태는 `BuffManager` 캐시(코어 로드 `activeBuffs` · 소모품 사용 응답 · `POST /api/game/consumable/buffs` 재동기화)를 따르며, 남은 시간은 서버 시각 기준으로 계산한다(주기 폴링 없음).

#### (C) 기능 패널 레이어 (모달/슬라이드 UI)
메뉴 바에서 여는 패널들. 각 패널은 서버 도메인과 1:1 대응한다.

| 패널 | 대응 서버 도메인 | 원작 화면(이미지) | 클라이언트 주요 요소 |
|---|---|---|---|
| `InventoryPanel` | 4.4 인벤토리/아이템 | `inventory-item-cube-인벤토리_전체/확장`, `장비아이템`, `재료아이템` | 아이템 그리드, 장착 슬롯, 정렬, 인벤토리 확장 |
| `CubePanel` | 4.4 큐브 | `큐브제작1/2`, `큐브합성`, `큐브판매(연금술)`, `랜덤가챠기능` | 합성/분해/제작 UI, 결과는 서버 확정 후 반영 |
| `GrowthPanel` | 4.5 성장 | `growth-스킬포인트`, `스킬레벨업가능`, `액티브스킬_슬롯`, `룬시스템` | 스킬 트리·레벨업, 액티브 스킬 슬롯, 룬 트리 |
| `StagePanel` (포탈) | 4.6 스테이지/전투 | `stage-battle-포탈메뉴`, `포탈화면` | Act×난이도×스테이지 선택·이동, 진행도 표시 |
| `TradePanel` (교역선) | 4.7 거래소 | `trade-교역선`, `교역선화면` | 판매 등록/구매/취소, 대금은 메일로 수령 |
| `MailPanel` (우편함) | 4.8 메일 | `mail-우편함_메뉴`, `우편함화면` | 메일 목록, 첨부 수령, 일괄 수령 |
| `AttendancePanel` | 4.9 출석 | `attendance-출석보상`, `보상메일수령` | 월 단위 출석 현황, 일 1회 수령(보상은 메일 경유) |
| `OfflineRewardPopup` | 4.3 오프라인 보상 | `offline-reward-오프라인보상` | 재접속 시 경과시간·정산 결과 팝업(서버 계산 값 표시) |
| `WarehousePanel` (창고) | 4.2/4.4 세이브·보관 | `save-data-창고_인벤토리` | 확장 보관함(선택 구현) |

> 상점/가챠 등 원작의 결제·유료 요소는 학습 프로젝트 범위에 따라 축소될 수 있다. 실물 화폐 연동은 없음(거래 대금은 인게임 재화).

## 4. 공통 UI / 런타임 시스템

- `EventSystem` + Input System UI Input Module (모든 UI 씬 필수)
- `UIManager` / `PanelStack` — 패널 열기·닫기, 뒤로가기, 모달 스택, 딤(dim) 배경 관리
- `PopupManager` — 공용 확인/알림/에러 팝업. 서버 `ErrorCode` → 사용자 메시지 매핑 담당
- `ToastManager` — 획득·보상 등 짧은 알림
- `LoadingOverlay` — 네트워크 대기 표시
- `AudioManager`, `AudioListener`(카메라)

## 5. 네트워크 / 데이터 레이어 (비-UI)

씬에 상주하는 매니저(또는 DontDestroyOnLoad 싱글턴)로 구성:
- `ApiClient` — `UnityWebRequest` 기반 HTTP. AccountServer/GameServer 호출, 토큰 헤더 부착
- `AuthService` — 토큰 저장·갱신, 인증 상태
- `PlayerStateStore` — 서버에서 로드한 세이브/재화/인벤토리/성장 상태의 클라이언트 캐시(표시용, 권위는 서버)
- `MasterDataProvider` — 클라이언트 빌드에 **번들된 마스터 데이터** 로드(4.10 — 런타임 다운로드 없음)
- DTO·에러코드는 `TaskbarHero.Common` 공유 타입 사용

## 6. 폴더 / 씬 컨벤션 (첫 스크립트 작성 시 확정)

- `Assets/Scenes/` — Bootstrap / Login / CharacterSelect / Main
- `Assets/Scripts/` — 기능 단위 `.asmdef`로 컴파일 범위 분리(예: `Core`, `Network`, `UI`, `Battle`, `Data`)
- `Assets/UI/` — 패널 프리팹, `Assets/Art/` — 스프라이트/아트, `Assets/Audio/`
- 네임스페이스 및 asmdef 규칙은 첫 코드 도입 PR에서 결정하고 이 문서/`CLAUDE.md`에 반영한다.

## 7. 미결 사항 (결정 필요)

- 창 해상도/종횡비(작업표시줄형 세로 창 재현 범위), 전체화면 지원 여부
- Login·CharacterSelect 씬 분리 vs 패널 통합
- 상점/가챠·창고 확장 등 원작 부가 기능의 구현 범위
- 저장 트리거(주기 저장/이벤트 저장)의 클라이언트측 호출 시점 — 서버 세이브 정책(`save-data-기획서`)과 맞춤
