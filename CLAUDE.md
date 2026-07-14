# CLAUDE.md

이 파일은 Claude Code(claude.ai/code)가 이 저장소에서 작업할 때 참고하는 가이드입니다.

## 언어

모든 답변 및 문서 작성은 한글로 한다.

## 프로젝트

스팀(Steam) 방치형(idle) 게임 **Taskbar Hero**의 모작 서버. 2026 컴투스 지니어스 과제. 클라이언트는 Unity이며, 이 때문에 공통 라이브러리가 `netstandard2.0`를 타겟팅한다. .NET 10 기반의 ASP.NET Core 웹 API 서버 2개와, 이들이 공유하는 공통 라이브러리 프로젝트로 구성된다. 아직 초기 단계로, 두 서버 모두 기본 `weatherforecast` 스캐폴드만 있고 실제 엔드포인트는 구현되지 않았다.

## 구조

- **AccountServer/** — 계정/인증 서비스 (`http://localhost:5160`, `https://localhost:7110`). `TaskbarHero.Common`을 참조한다.
- **GameServer/** — 게임 로직 서비스 (`http://localhost:5247`, `https://localhost:7179`). 아직 `TaskbarHero.Common`을 참조하지 않음. 공통 코드가 필요해지면 `ProjectReference`를 추가한다.
- **TaskbarHero.Common/** — 서버-클라이언트 공통 라이브러리(`netstandard2.0`). 원래 git 서브모듈(별도 저장소)이었으나 서브모듈을 해제하고 이 솔루션에 포함된 일반 프로젝트로 전환했다. Unity 클라이언트와 공유하므로 `netstandard2.0`을 유지한다. `TaskbarHero.Common/ErrorCode.cs`의 `GameErrorCode`는 클라이언트와 공유하는 에러 코드이므로, 그 숫자 값은 변경하면 안 되는 계약(contract)으로 취급한다.

`com2us-taskbar-hero.slnx`는 솔루션 파일(XML 형식의 `.slnx`)이며 AccountServer·GameServer와 공통 프로젝트 `TaskbarHero.Common`을 포함한다. 서버 프로젝트는 `TaskbarHero.Common`을 `ProjectReference`로 참조한다.

## 명령어

- 전체 빌드: `dotnet build com2us-taskbar-hero.slnx`
- 서버 실행: `dotnet run --project GameServer` 또는 `dotnet run --project AccountServer` (HTTPS 프로필은 `--launch-profile https` 추가)
- OpenAPI 문서는 Development 환경에서만 `/openapi`에 매핑된다.

아직 테스트 프로젝트는 없다. `.http` 파일(`GameServer/GameServer.http`, `AccountServer/AccountServer.http`)로 엔드포인트를 수동 호출할 수 있다.

## 규칙

- 두 서버 프로젝트는 `net10.0`을 사용하며 `Nullable`과 `ImplicitUsings`가 활성화되어 있다.
- `TaskbarHero.Common`은 Unity/게임 클라이언트와 공유하기 위해 의도적으로 `netstandard2.0`을 타겟팅하고 implicit usings와 nullable을 *비활성화*한다. 프레임워크 중립적으로 유지하고, 서버 전용 의존성을 추가하지 않는다.
