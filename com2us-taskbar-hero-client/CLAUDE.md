# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 언어

모든 답변 및 문서 작성은 한글로 한다.

## 프로젝트

스팀(Steam) 방치형(idle) 게임 **Taskbar Hero**의 모작 **Unity 클라이언트**. 2026 컴투스 지니어스 과제. 이 디렉터리(`com2us-taskbar-hero-client`)는 상위 `com2us-taskbar-hero` git 저장소의 일부이며, 같은 저장소에 .NET 서버 3종이 함께 있다(상위 `CLAUDE.md` 참조):

- **AccountServer** — 계정/인증 (`http://localhost:5160`, `https://localhost:7110`)
- **GameServer** — 게임 로직 (`http://localhost:5247`, `https://localhost:7179`)
- **TaskbarHero.Common** — 서버-클라이언트 공통 라이브러리. **`netstandard2.0` 타겟이며 이 Unity 클라이언트와 공유하는 것이 그 이유다.** `ErrorCode.cs`의 `ErrorCode` 숫자 값은 클라이언트-서버 간 계약(contract)이므로 변경 금지.

현재 상태: **초기 스캐폴드**. C# 스크립트, Assembly Definition(`.asmdef`), 테스트가 아직 하나도 없고 `Assets`에는 `SampleScene`과 URP 2D 설정만 있다.

## 문서

- **클라이언트 문서는 이 프로젝트의 `docs/` 폴더에 작성·관리한다.** 씬 구성, UI 흐름, 클라이언트 아키텍처, 에셋 컨벤션 등 클라이언트 고유 내용은 여기서 다룬다. 새 문서는 `docs/README.md` 목록에 링크한다.
- **서버 프로젝트 문서(`../docs/`, 즉 `com2us-taskbar-hero/docs`)는 서버 연결이 필요할 때만 참조한다.** API 스펙, 에러 코드, 기획서 등은 클라이언트로 복제하지 말고 서버 문서 원본을 참조한다.

## 환경

- **Unity 6000.5.3f1** (Unity 6). 에디터에서 프로젝트를 열어 작업하는 것이 기본. CLI 빌드/테스트 파이프라인은 아직 구성되지 않았다.
- 렌더 파이프라인: **URP 2D** (`Assets/Settings/`의 `Renderer2D.asset`, `UniversalRP.asset`). 신규 씬은 `Lit2DSceneTemplate` 기반.
- 입력: **새 Input System** (`com.unity.inputsystem`). 액션 정의는 `Assets/Settings/InputSystem_Actions.inputactions`. 레거시 `Input` API 대신 이 액션 에셋을 사용한다.
- `com2us-taskbar-hero-client.slnx`는 Unity가 자동 생성/갱신하는 빈 솔루션 파일이므로 수동 편집하지 않는다. C# 프로젝트(`.csproj`)도 Unity가 `.asmdef`에서 자동 생성한다.

## 규칙

- **서버 통신 코드나 DTO는 `TaskbarHero.Common`의 타입을 그대로 사용한다.** 클라이언트에서 중복 정의하지 말 것. 특히 에러 코드는 `ErrorCode`(namespace `TaskbarHero.Common`)를 참조한다.
- **API 요청/응답 DTO는 `TaskbarHero.Common/Dto`의 공유 DTO(namespace `TaskbarHero.Common.Dto`)를 그대로 사용한다.** 예: 로그인/회원가입은 `LoginRequest`·`LoginResponse`·`SignupRequest`·`SignupResponse`. 클라이언트에 별도 DTO 클래스(예: `LoginRequestDto`)를 만들지 말 것. 필요한 DTO가 없으면 클라이언트가 아니라 `TaskbarHero.Common/Dto`에 추가해 서버와 공유한다. 이 DTO들은 `[Serializable]` + public camelCase 필드라 Unity `JsonUtility`와 서버 `System.Text.Json`(IncludeFields) 양쪽에서 동일 JSON으로 직렬화된다.
- **`TaskbarHero.Common`은 로컬 UPM 패키지로 참조한다.** `Packages/manifest.json`에 `"com.com2us.taskbarhero.common": "file:../../TaskbarHero.Common"`로 등록되어 있으며, 상위 저장소의 `TaskbarHero.Common/` 폴더 소스를 Unity가 직접 컴파일한다(어셈블리명 `TaskbarHero.Common`, `autoReferenced`라 별도 asmdef 참조 없이 사용 가능). 이 라이브러리는 서버와 공유하는 `netstandard2.0` 코드이므로 여기에 Unity 전용 의존성을 넣지 말 것.
- 새 C# 코드를 추가할 때는 기능 단위로 `.asmdef`(Assembly Definition)를 만들어 컴파일 범위를 나눈다. 첫 스크립트 작성 시 폴더 구조와 asmdef 컨벤션을 먼저 정한다.
- **매니저 클래스는 `Assets/Scripts/Managers`에 둔다.** 싱글턴 성격의 전역 관리자(예: `UIManager`, `SceneManager`)는 이 폴더에 정리하며, 어셈블리는 `TaskbarHero.Client.Managers`(asmdef)로 묶고 네임스페이스도 `TaskbarHero.Client.Managers`를 사용한다.
- **프리팹은 `Assets/Prefabs/` 아래 카테고리 폴더로 정리한다.** 모든 프리팹의 저장 경로는 `Assets/Prefabs/<카테고리>/`이며(`Resources/` 아래에 두지 않는다), UI 프리팹은 **`Assets/Prefabs/UI/`** 에 둔다(예: `LoginPanel`·`SignUpPanel`·`InventoryPanel`). 프리팹은 `Resources.Load` 대신 **참조하는 매니저/컴포넌트의 `[SerializeField]`에 직접 배선**해 소비한다. 특히 `UIManager`처럼 `DontDestroyOnLoad`로 씬 간 유지되는 싱글턴이 소비하는 패널 프리팹은, 그 인스턴스가 **생성되는 씬(예: `TitleScene`)과 사용되는 씬(예: `GameScene`) 양쪽 UIManager에 모두 참조를 지정**해 씬 전환 후에도 참조가 유지되게 한다. (Resources는 항상 빌드에 포함되고 부분 언로드가 불가하므로 UI 프리팹 보관 위치로 쓰지 않는다.)
- **GameScene 전투는 `BattleDevScene`을 정본으로 따라간다.** 전투 시스템은 `BattleDevScene`에서 개발하고, GameScene의 던전 전투 구성은 에디터 메뉴 **`TaskbarHero/UI/던전 전투 배선`**(`Assets/Editor/DungeonBattleBuilder.cs`)을 실행해 BattleDevScene에서 **복제**해 재생성한다. 복제 대상: 전투 컨트롤러(`BattleDevController`)·파티·스킬·이펙트, `Global Light 2D` 조명, **카메라 프레이밍(위치 y·orthographicSize — BattleDevScene과 일치시켜 길·캐릭터 위치가 맞도록)**, 고정 배경 지오메트리(`worldHeight`/`centerY`, autoFit 아님), 스폰 앵커, 스크롤 배경, 그리고 **전투 UI(초상화·아군 스킬 슬롯·아군 HP바 = `SkillUICanvas`/`SkillCooldownUI`, 적 HP바 = `BattleDevController.UpdateEnemyHpBars`의 `EnemyHpBarCanvas` — 프레임 아트 `Assets/Art/Icon/Combat/체력바.png`를 쓰고, 위치는 몬스터가 스폰 직후 1회 측정한 머리 오프셋(`MonsterUnit.TryGetHeadAnchor`)을 써서 애니메이션에 흔들리지 않게 따라간다)**. **BattleDevScene에서 전투를 추가·수정하면 이 빌더를 다시 실행해 GameScene에 반영**한다(GameScene을 직접 손대지 말 것). GameScene에서 숨기는 것은 **개발 전용 하네스 IMGUI(스탯·로그·일시정지·소환 선택 박스)뿐**이며, 이는 `serverMode=true`에서 `OnGUI`가 자동으로 생략한다(적 HP바는 serverMode에서도 표시).
- **캐릭터 프리팹 애니메이션은 `AnimDevScene`에서 확인한다.** 씬은 에디터 메뉴 **`TaskbarHero/UI/애니메이션 개발 씬(AnimDevScene) 생성`**(`Assets/Editor/AnimDevSceneBuilder.cs`)으로 만들며, 구성은 카메라 · EventSystem · `Knight_Male` 프리팹 1개 · `AnimDevController`뿐이다(배경·조명 없음). 왼쪽 목록에서 애니메이션을 고르고 **캐릭터를 클릭하면 재생**된다 — 목록은 `SPUM_Prefabs`의 상태별 클립 리스트(IDLE·MOVE·ATTACK·DAMAGED·DEBUFF·DEATH·OTHER)와 `SpumCharacterAnimator`의 특수 모션(분노·돌진·캐스트 홀드·화살비·내려찍기)을 런타임에 읽어 만든다. `AnimDevController`는 `SPUM_Prefabs`가 있는 **Assembly-CSharp(`Assets/Scripts/Spum/`)** 에 둬야 클립 목록을 직접 열거할 수 있다(asmdef 어셈블리에서는 SendMessage만 가능). 빌드에는 포함하지 않는다.
  - **새 애니메이션(`.anim`)을 테스트에 넣으려면 파일을 `Assets/Animations/`에 두고 씬 생성 메뉴를 다시 실행한다.** 빌더가 그 폴더의 클립을 `AnimDevController`의 **추가 클립** 슬롯(`extraClips`)에 배선하고, 재생 상태를 이름으로 추정한다(idle/move·walk·run/damage·hit/debuff·stun/die·death → 해당 상태, 그 외 ATTACK). 추정이 틀리면 씬 인스펙터에서 항목의 `playAs`만 바꾼다. 재생은 지정 상태 리스트 **끝에** 클립을 런타임 추가한 뒤 그 인덱스로 하므로(프리팹 에셋·기존 인덱스 불변) 전투가 쓰는 `ATTACK_List[0]` 등이 영향받지 않는다. 이미 특수 모션으로 노출된 클립(`KnightChargeDash`·`KnightRage`)은 중복 배선에서 제외된다.
- **새 씬을 추가하면 에디터 상단 메뉴에서 이동할 수 있게 배선한다.** 씬(`Assets/Scenes/*.unity`)을 새로 만들면 반드시 `Assets/Editor/SceneSwitcher.cs`에 항목을 추가해 **`TaskbarHero/씬/<씬 이름>`** 메뉴(+ `Alt+숫자` 단축키)로 열 수 있게 한다. `[MenuItem]`은 컴파일 타임 속성이라 동적 생성이 불가하므로, 씬마다 다음 4가지를 함께 늘린다 — ① 표시 이름 상수 ② 씬 경로 상수 ③ `[MenuItem(항목 + " &N", false, 우선순위)]` 열기 메서드 ④ `[MenuItem(항목 + " &N", true)]` 검증 메서드(`Validate`로 현재 씬 체크 표시·플레이 중 비활성화). 우선순위는 게임 플로우 순서(Title 0 → CreateCharacter 1 → Game 2 → …)로 두고, 빌드에 포함되지 않는 개발용 하네스 씬(`BattleDevScene`·`AnimDevScene`)은 구분선 뒤(20+)에 둔다. **빌드에 포함되는 씬이면 Build Settings 등록(씬 생성 빌더가 담당)과 `Assets/Editor/FullGameBuilder.cs`의 빌드 씬 목록에도 함께 추가**한다(빠지면 런타임 `LoadScene`이 실패한다).
- **계층을 프리팹·씬에 구워 두는 UI는 버튼 핸들러를 런타임에 다시 연결한다.** `onClick.AddListener`는 **비영구(non-persistent) 리스너라 프리팹·씬에 직렬화되지 않는다.** 따라서 에디터 빌더가 `EditorConstruct()`로 계층을 구워 두는 컨트롤러는, 구성 시점에 붙인 리스너가 실행 시 사라져 **버튼이 눌리지 않는다**(2026-07-30 편성 씬 '뒤로'·'캐릭터 추가'에서 실제 발생). `Awake`에서 호출하는 `WireRuntime()`을 두고 구워진 고정 버튼의 `onClick`을 `RemoveAllListeners()` → `AddListener(...)`로 매 실행마다 다시 연결한다(`PartyPanelController`가 쓰던 방식). 런타임에 생성하는 목록·카드 버튼은 생성 시 붙이면 된다.
- **에디터 빌더는 공용 아트의 임포트 설정을 바꾸지 않는다.** `Assets/Art/**`의 텍스처 임포트 설정(`spriteImportMode`·`spriteBorder`·`textureType`)을 에디터 도구에서 변경(`SaveAndReimport`)하지 말 것. 이 프로젝트의 UI·아이콘 텍스처는 **대부분 `Multiple`로 임포트**돼 있고, 이를 `Single`로 바꾸면 **서브 스프라이트가 삭제되어 그것을 참조하던 모든 프리팹·씬의 스프라이트 참조가 한꺼번에 끊긴다**(증상: 기존 UI의 버튼·배경이 사라짐. 2026-07-30 `modal_bg`·`pixel_rpg_button`에서 실제 발생). 빌더는 이미 임포트된 스프라이트를 **읽어서 배선만** 한다 — `AssetDatabase.LoadAssetAtPath<Sprite>` 실패 시 `LoadAllAssetsAtPath`로 서브 스프라이트를 집는 폴백을 쓴다. 9-slice가 필요하면 임포트를 고치는 대신 `Image.type = Sliced` + `pixelsPerUnitMultiplier`로 맞추고, 테두리 값이 실제로 없다면 사용자에게 알리고 확인받는다.
- **텍스트가 흐리다는 보고를 받으면 Game 뷰의 `Low Resolution Aspect Ratios`를 가장 먼저 확인한다.** 이 토글이 켜져 있으면 게임을 **논리 해상도로 렌더한 뒤 창 크기로 확대**하므로, 고DPI 화면(배율 150% → `pixelsPerPoint 1.5`)에서 화면 전체가 1.5배 업스케일돼 텍스트·픽셀아트가 통째로 흐려진다(실측: 렌더 1081×608 → 1622×913, 텍스트 래스터 17~25px → 25~38px). **글리프에 들어가는 픽셀 수 자체가 다른 문제라 아래 선명화 셰이더로는 보정할 수 없다.** 이 값은 프로젝트 설정이 아니라 Game 뷰 창(에디터 레이아웃) 상태라 레이아웃 초기화·에디터 업그레이드로 조용히 다시 켜지므로, `Assets/Editor/GameViewResolutionGuard.cs`가 에디터 로드 시마다 꺼 준다(수동: 메뉴 `TaskbarHero/UI/Game 뷰 저해상도 렌더링 끄기`). 경위·실측값: [`docs/ui/텍스트-선명도.md`](docs/ui/텍스트-선명도.md).
- **UI 폰트는 빌트인 동적 폰트(`LegacyRuntime.ttf`)이며, 선명화는 전역 자동 적용이다 — 개별 배선하지 말 것.** 이 폰트의 그레이스케일 AA 때문에 텍스트가 픽셀아트 옆에서 흐려 보이는 문제를, 글리프 알파를 언샤프 마스킹 + 배율로 다듬는 셰이더(`Assets/Shaders/UITextSharpen.shader` = `TaskbarHero/UI Text Sharpen`)로 완화했다. **`Assets/Scripts/Managers/UiTextStyle.cs`** 가 플레이 시작·씬 로드·0.4초 주기로 모든 `Text`에 이 머티리얼을 붙이므로 **새 텍스트를 만들 때 따로 할 일은 없다**(즉시 적용은 `UiTextStyle.Apply(text)`). 주의사항:
  - 선명도 파라미터는 `UiTextStyle`의 상수 4개다 — `SharpenAmount`(기본 1.6, 이웃 4탭 평균과 비교하는 언샤프 강도 = 선명도의 주 레버) · `SharpenRadius`(0.7, 이웃 샘플 거리, 화면 픽셀) · `InkFloor`(0.35, 획 쪽 픽셀의 알파 하한 비율) · `AlphaBoost`(1.35, 알파 배율). `SharpenAmount = 0` + `AlphaBoost = 1`이면 원본과 동일해진다.
  - **알파 값만 보고 자르거나 미는 방식(특히 알파 하한 컷 = 그 값 미만을 투명 처리)은 절대 도입하지 말 것** — halo와 작은 글자의 얇은 획은 알파 범위가 겹쳐(둘 다 0.2~0.5) 하한 컷이 획을 그대로 지운다(래스터 9~15px 한글, 2026-08-03 실제 발생·되돌림). 선명도가 부족하면 **주변 문맥을 보는** `SharpenAmount`를 올린다(2.2까지 9px 한글 무손상 확인).
  - `InkFloor`는 **획 쪽 픽셀에만** 걸린다(이웃 대비 밝기에 비례). 모든 픽셀에 일괄로 걸면 halo까지 보호해 선명해지지 않는다(측정 확인).
  - 텍스트에 **커스텀 머티리얼을 지정하면 선명화 대상에서 제외**된다(의도 존중).
  - 셰이더는 코드에서만 참조하므로 **Always Included Shaders 등록이 필요**하며, 이 등록이 에디터 설정 저장 중 유실된 적이 있어 `Assets/Editor/UiTextShaderRegistrar.cs`가 에디터 로드 시마다 자동 보정한다(수동: 메뉴 `TaskbarHero/UI/텍스트 셰이더 Always Included 등록 확인`).
  - **텍스트 rect 높이를 글자 크기에 딱 맞춰 잡지 말 것(여유 2~3px).** `Text`는 `verticalOverflow = Truncate`에서 줄 높이가 rect를 넘으면 그 줄을 **통째로 렌더링하지 않아 글자가 사라진다**(부분 잘림이 아니다. 로그인 패널 `InputField`가 fontSize 16 / rect 18로 빡빡해 실제로 사라진 적이 있다). `UiTextStyle`이 한 줄조차 안 들어가면 자동으로 `Overflow`로 바꿔 구제한다.
  - **텍스트 렌더링을 건드릴 때는 가장 작은 래스터 크기(`fontSize × canvas.scaleFactor` 기준 9~15px)와 한글로 먼저 검증한다** — 캔버스 배율이 0.563(ref 1080×1920 패널)·1.0·1.689(ref 360×640 씬 캔버스)로 섞여 있어 `fontSize`만 보면 작은 구간을 놓친다.
  - 픽셀(비트맵) 폰트는 검증 후 **도입하지 않기로 했다**(기본 폰트 유지). 재검토 시 필요한 준비 사항은 [`docs/ui/텍스트-선명도.md`](docs/ui/텍스트-선명도.md)에 정리해 뒀다. 원인 분석·측정값·미적용 선택지(픽셀 폰트·TMP, 월드 `TextMesh`)는 [`docs/ui/텍스트-선명도.md`](docs/ui/텍스트-선명도.md)에 정리했다.
- **모달에서 골드 수치는 노란색·볼드로 강조한다.** 모달(`ModalManager`/`ModalController`)의 메시지에 골드 금액을 표시할 때는 반드시 `GoldFormat.Highlight(long)`(namespace `TaskbarHero.Client.Managers`)로 감싸 **노란색 볼드 리치텍스트**로 출력한다(예: `$"소모 골드: {GoldFormat.Highlight(cost)}"`). 문자열에 직접 `{cost:N0}`만 넣지 말 것. Unity UI `Text`는 리치텍스트가 기본 활성이므로 태그가 그대로 렌더링된다.
- HTTP 통신은 Unity의 `UnityWebRequest`(manifest에 포함됨)를 사용한다.
- **네트워크 테스트는 명시적으로 요청받지 않는 한 하지 않는다.** 클라이언트 기능 구현 시 실제 서버(AccountServer·GameServer)로의 API 호출 검증은 사용자가 명시적으로 요청한 경우에만 수행한다. 그 외에는 컴파일·씬 구성·UI 흐름·씬 전환 등 서버 없이 확인 가능한 부분만 검증하고, 네트워크 연동 코드는 작성·배선까지만 하고 라이브 호출 검증은 생략한다.
- **서버는 어시스턴트가 절대 띄우지 않는다 (예외 없음).** 서버(`GameServer`·`AccountServer`) 기동은 **사용자만** 한다. 어시스턴트는 `dotnet run`·`Start-Process`(숨김 포함)·`Start-Job`·harness `run_in_background` 등 **어떤 방법으로도 서버를 주도적으로 띄우려 시도하지 않는다.** 테스트 스크립트도 서버를 스스로 기동하지 않는다.
  - **실테스트는 "사용자가 서버를 올린 뒤 테스트를 진행하라고 지시했을 때만" 수행한다.** 서버가 내려가 있으면 **직접 띄우지도, 대신 띄워달라고 조르지도 말고** 그 사실만 알리고 멈춘다(사용자가 `! dotnet run --project GameServer` 형태로 직접 실행한다).
  - 이미 떠 있는 서버는 어시스턴트가 임의로 종료하지 않는다(사용자가 테스트용으로 올려 둔 프로세스일 수 있다).
- **클라이언트 기능 완료 시 README 현황판 갱신.** 클라이언트 측 기능 구현이 완료되면 상위 저장소 `README.md`「개발 현황」 체크리스트에서 해당 기능의 **`클라 실연동`** 칸 상태 기호를 갱신한다(☐ 미착수 → ◐ 진행 중 → ☑ 완료). 서버 실연동까지 검증된 기능만 ☑로 표시하고, 코드 배선만 된 경우는 ◐로 둔다.
