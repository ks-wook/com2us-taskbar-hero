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
- **`TaskbarHero.Common`은 로컬 UPM 패키지로 참조한다.** `Packages/manifest.json`에 `"com.com2us.taskbarhero.common": "file:../../TaskbarHero.Common"`로 등록되어 있으며, 상위 저장소의 `TaskbarHero.Common/` 폴더 소스를 Unity가 직접 컴파일한다(어셈블리명 `TaskbarHero.Common`, `autoReferenced`라 별도 asmdef 참조 없이 사용 가능). 이 라이브러리는 서버와 공유하는 `netstandard2.0` 코드이므로 여기에 Unity 전용 의존성을 넣지 말 것.
- 새 C# 코드를 추가할 때는 기능 단위로 `.asmdef`(Assembly Definition)를 만들어 컴파일 범위를 나눈다. 첫 스크립트 작성 시 폴더 구조와 asmdef 컨벤션을 먼저 정한다.
- HTTP 통신은 Unity의 `UnityWebRequest`(manifest에 포함됨)를 사용한다.
