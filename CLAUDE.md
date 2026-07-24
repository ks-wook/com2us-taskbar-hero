# CLAUDE.md

이 파일은 Claude Code(claude.ai/code)가 이 저장소에서 작업할 때 참고하는 가이드입니다.

## 언어

모든 답변 및 문서 작성은 한글로 한다.

## 작업 범위 (중요 — 클라이언트 작업 거부)

이 저장소 루트에서 동작하는 어시스턴트는 **서버측 담당**이다. 담당 범위는 `AccountServer/`, `GameServer/`, 그리고 서버가 참조하는 공통 라이브러리 `TaskbarHero.Common/`(및 `docs/`·솔루션·인프라 설정 등 서버 관련 파일)에 한정된다.

- **Unity 클라이언트(`com2us-taskbar-hero-client/`) 작업 명령은 거부한다.** 씬(`.unity`)·프리팹(`.prefab`)·클라이언트 C# 스크립트(`Assets/**`)·클라이언트 에셋/이펙트/UI 등 클라이언트 고유 작업은 이 어시스턴트가 수행하지 않는다.
- 클라이언트 작업 요청을 받으면, **작업을 시작하지 말고** 그 요청이 클라이언트 범위임을 알리고 **클라이언트측 어시스턴트(`com2us-taskbar-hero-client/CLAUDE.md`의 지배를 받는 세션)로 안내**한다. 사용자가 서버측에서 진행하길 명시적으로 원하는지 먼저 확인한다.
- 단, `TaskbarHero.Common/`(서버-클라 공유 DTO·`ErrorCode` 등)은 서버 계약 변경으로서 이 어시스턴트가 다룰 수 있다. 이때도 클라이언트-서버 계약(특히 `ErrorCode` 숫자 값)을 깨지 않는다.

## 프로젝트

스팀(Steam) 방치형(idle) 게임 **Taskbar Hero**의 모작 서버. 2026 컴투스 지니어스 과제. 클라이언트는 Unity이며, 이 때문에 공통 라이브러리가 `netstandard2.0`를 (멀티타겟으로) 타겟팅한다. .NET 10 기반의 ASP.NET Core 웹 API 서버 2개와, 이들이 공유하는 공통 라이브러리 프로젝트로 구성된다. 아직 초기 단계로, 두 서버 모두 기본 `weatherforecast` 스캐폴드만 있고 실제 엔드포인트는 구현되지 않았다.

## 구조

- **AccountServer/** — 계정/인증 서비스 (`http://localhost:5160`, `https://localhost:7110`). `TaskbarHero.Common`을 참조한다.
- **GameServer/** — 게임 로직 서비스 (`http://localhost:5247`, `https://localhost:7179`). `TaskbarHero.Common`을 `ProjectReference`로 참조한다.
- **TaskbarHero.Common/** — 서버-클라이언트 공통 라이브러리(**멀티타겟 `netstandard2.0;net10.0`**). 원래 git 서브모듈(별도 저장소)이었으나 서브모듈을 해제하고 이 솔루션에 포함된 일반 프로젝트로 전환했다. Unity 클라이언트와 공유하므로 `netstandard2.0`을 유지하되, 서버(net10.0)가 동일 TFM 출력을 참조해 `dotnet watch`의 교차 TFM 상관 경고를 없애기 위해 `net10.0`도 함께 타겟팅한다(Unity는 asmdef로 소스를 직접 컴파일하므로 TFM 목록과 무관). `TaskbarHero.Common/ErrorCode.cs`의 `ErrorCode`는 클라이언트와 공유하는 에러 코드이므로, 그 숫자 값은 변경하면 안 되는 계약(contract)으로 취급한다(정본 목록: `docs/공통/error-code-정의.md`).
  - **소비 방식(이중)**: 서버 두 곳은 `ProjectReference`로 참조한다. **Unity 클라이언트는 이 폴더를 로컬 UPM 패키지로 소비한다** — `com2us-taskbar-hero-client/Packages/manifest.json`에 `"com.com2us.taskbarhero.common": "file:../../TaskbarHero.Common"`로 등록되어 있고, 폴더 안의 `package.json` + `TaskbarHero.Common.asmdef`로 Unity가 소스를 직접 컴파일한다(별도 빌드/DLL 복사 없음).
  - **빌드 산출물 격리**: Unity가 이 폴더의 `.cs`를 직접 컴파일하므로, `Directory.Build.props`가 MSBuild의 `obj/bin`을 폴더 밖 `artifacts/`로 재배치한다. **이 폴더 안에 `obj/`·`bin/`을 만들지 말 것**(Unity가 생성 `.cs`를 중복 컴파일해 깨진다).

`com2us-taskbar-hero.slnx`는 솔루션 파일(XML 형식의 `.slnx`)이며 AccountServer·GameServer와 공통 프로젝트 `TaskbarHero.Common`을 포함한다. 서버 프로젝트는 `TaskbarHero.Common`을 `ProjectReference`로 참조한다.

## 명령어

- 전체 빌드: `dotnet build com2us-taskbar-hero.slnx`
- 서버 실행: `dotnet run --project GameServer` 또는 `dotnet run --project AccountServer` (HTTPS 프로필은 `--launch-profile https` 추가)
- OpenAPI 문서는 Development 환경에서만 `/openapi/v1.json`에 매핑된다.
- Swagger UI는 Development 환경에서만 `/swagger`에서 제공된다(`Swashbuckle.AspNetCore.SwaggerUI`가 위 OpenAPI 문서를 렌더링). 예: AccountServer `http://localhost:5160/swagger`, GameServer `http://localhost:5247/swagger`.

아직 테스트 프로젝트는 없다. `.http` 파일(`GameServer/GameServer.http`, `AccountServer/AccountServer.http`)로 엔드포인트를 수동 호출할 수 있다.

## 규칙

- 두 서버 프로젝트는 `net10.0`을 사용하며 `Nullable`과 `ImplicitUsings`가 활성화되어 있다.
- `TaskbarHero.Common`은 Unity/게임 클라이언트와 공유하기 위해 `netstandard2.0`을 타겟팅하며(서버 참조·watch 경고 해소용으로 `net10.0`도 멀티타겟), implicit usings와 nullable을 *비활성화*한다. 프레임워크 중립적으로 유지하고(netstandard2.0에서 컴파일되는 코드만), 서버 전용 의존성을 추가하지 않는다.
- MySQL DB 연동은 **SqlKata**를 사용해 개발한다. 쿼리는 SqlKata의 쿼리 빌더로 작성하고, 원시(raw) SQL 문자열을 직접 조립하지 않는다.
- **DB 조회 결과는 `dynamic`으로 다루지 않는다.** 반드시 **제네릭 매핑**(`.GetAsync<T>()`·`.FirstOrDefaultAsync<T>()`, 단일 컬럼은 `.GetAsync<int>()`/`<long?>` 등 스칼라)으로 **POCO/스칼라 타입에 매핑**한다. 컬럼 접근을 `row.column`(dynamic) + `Convert.ToXxx(...)`로 하지 않는다(컴파일 타임 타입 검사 상실 + dynamic 전염으로 인한 튜플/변환 런타임 오류 방지). 행 매핑용 POCO는 리포지토리 파일에 `file sealed class`로 두고, `snake_case` 컬럼은 `Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true`(각 서버 `Program.cs`에서 1회 설정)로 `PascalCase` 프로퍼티에 자동 매핑한다. `DECIMAL` 컬럼은 POCO에서 `decimal`로 받아 `float`/`double`로 캐스팅한다.
- Redis와의 통신은 **CloudStructures**를 사용한다. Redis 접근은 CloudStructures가 제공하는 타입 구조체를 통해 처리한다.
- **Redis 인스턴스**: 로컬 개발용 Redis는 저장소 내 `Redis-8.8.0-Windows-x64-cygwin-with-Service/`의 Redis를 사용한다(`redis.conf` 기준 `127.0.0.1:6379`). `start.bat` 또는 `redis-server.exe redis.conf`로 실행하며, docker-compose에는 Redis를 두지 않는다(MySQL만 컨테이너로 관리).
- **작업 완료 시 README 현황판 갱신**: 기능/작업이 완료되면 `README.md`의 「개발 현황」 체크리스트에서 해당 항목의 상태 기호를 갱신한다(☐ 미착수 → ◐ 진행 중 → ☑ 완료). 서버 구현·클라 실연동은 각각 별도로 표시한다.
- **컨트롤러는 HTTP 응답 메서드만 포함**: 컨트롤러 클래스에는 엔드포인트 액션 메서드(`[HttpGet]`/`[HttpPost]` 등이 붙은 HTTP 요청/응답 처리 메서드)만 둔다. 그 외 로직은 **private 헬퍼라도 예외 없이** 컨트롤러 밖으로 분리한다:
  - **컨트롤러 공통 보조 메서드**(인증 userId 추출, 공통 응답 변환, ErrorCode ↔ HTTP 상태/메시지 매핑 등)는 **베이스 컨트롤러 클래스**(`ControllerBase`를 상속한 추상 클래스, 예: `GameApiControllerBase`·`AccountApiControllerBase`)에 `protected`/`private static`으로 구현하고, 각 컨트롤러가 이를 **상속**해 사용한다.
  - 컨트롤러 작업 시 이 규칙을 매번 확인한다.
- **기능 구현 시 빌드 및 테스트 진행**: 기능을 구현하면 반드시 빌드(`dotnet build`)로 컴파일을 확인하고, 실제 동작을 테스트로 검증한다. 빌드 성공과 테스트 통과를 확인하기 전에는 작업을 완료로 간주하지 않는다.
- **서비스 클래스 메서드 주석 필수**: 서비스 클래스(`*Service`, 예: `AuthService`·`SaveService`·`StageService`)에 속한 **모든 메서드**(public·private 헬퍼·생성자 포함)에는 그 메서드가 **어떤 로직을 수행하는지** 설명하는 `/// <summary>` 주석을 반드시 작성한다. 새 서비스 메서드를 추가하거나 기존 메서드를 수정할 때 이 규칙을 매번 확인한다.
- **로깅 규칙 준수**: 서버에 로그를 추가·수정할 때는 [`docs/공통/로깅-규칙.md`](docs/공통/로깅-규칙.md)를 따른다. 기본 `ILogger<T>`(생성자 주입)·메시지 템플릿(문자열 보간 금지)·레벨 기준(사용자 실수는 로깅 안 함, 경합은 Warning, 서버 결함은 Error), 로깅은 서비스/미들웨어에서(컨트롤러 금지), 비밀번호·토큰·PII 미노출을 매번 확인한다.
- **기능 구현 시 시퀀스 다이어그램 작성/갱신**: 새 컨트롤러·엔드포인트를 구현하거나 기존 처리 흐름을 변경하면, 루트 **`SequenceDiagram/`** 폴더에 해당 컨트롤러의 Mermaid 시퀀스 다이어그램(`<컨트롤러명>.md`, 엔드포인트별 `sequenceDiagram` 블록)을 생성/갱신하고, **`SequenceDiagram/README.md`의 기능별 컨트롤러 매핑 표**에도 링크를 추가/갱신한다. 흐름은 `클라이언트 → (GameServer는 GameAuthMiddleware) → Controller → Service → MasterDataProvider/Repository → MySQL/Redis` 계층으로 표기하고, 주요 분기·에러 코드(`alt`)·트랜잭션 경계(`Note over`)를 포함한다. 기능 구현 시 이 규칙을 매번 확인한다.
