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
- **어시스턴트는 서버를 포어그라운드로만 띄운다**: 서버(`GameServer`·`AccountServer`)를 어시스턴트가 띄울 수 있으나, **반드시 사용자가 볼 수 있는 별도 콘솔 창(포어그라운드)** 으로만 띄운다. **숨김·백그라운드 기동은 금지**한다 — `Start-Process -WindowStyle Hidden`·`Start-Job`·harness `run_in_background`·`&`·`nohup` 등. 숨겨진 서버는 세션이 끝나도 남아 `bin/` 파일을 잠그고(MSB3021/MSB3027), 다음 테스트가 **옛 바이너리를 검증**하게 만든다.
  - 기동 명령(창이 뜨고 로그가 사용자에게 그대로 보인다):
    ```powershell
    Start-Process powershell -ArgumentList '-NoExit','-Command','dotnet run --project GameServer'
    Start-Process powershell -ArgumentList '-NoExit','-Command','dotnet run --project AccountServer'
    ```
  - 기동 후에는 헬스 체크(예: `http://localhost:5247/openapi/v1.json`)로 **준비 완료를 확인한 뒤** 시나리오를 시작한다.
  - **테스트가 끝나면 어시스턴트가 띄운 서버는 반드시 강제 종료**하고 잔여 프로세스 0을 확인한다(아래 「테스트 전후 기존 서버 강제 종료」). 세션에 서버를 남기지 않는다.
  - 테스트 스크립트는 **강제 종료 → 빌드 → 포어그라운드 기동 → 헬스 체크 → 시나리오 → 강제 종료·잔여 0 확인** 순서로 구성한다.
- **테스트 전후 기존 서버 강제 종료**: 테스트를 시작하기 전에 **이미 떠 있는 서버가 있으면 확인 없이 강제 종료한다.** 끝난 뒤에도 잔여 프로세스가 0인지 반드시 확인한다.
  - 종료는 **앱 프로세스와 `dotnet run` 래퍼를 모두** 잡는다. 래퍼가 살아 있으면 자식(`GameServer.exe`·`AccountServer.exe`)을 다시 띄워 "0건" 확인 직후에 서버가 되살아난다.
    ```powershell
    Get-Process -Name GameServer,AccountServer -ErrorAction SilentlyContinue | Stop-Process -Force
    Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" |
      Where-Object { $_.CommandLine -match 'GameServer|AccountServer' } |
      ForEach-Object { try { Stop-Process -Id $_.ProcessId -Force } catch {} }
    ```
  - 잔여 확인도 **두 가지를 모두** 센다(앱 프로세스만 세면 래퍼를 놓친다).
  - 배치(`PeriodicBatchService` 파생)의 Redis 리더 락(`batch:lock:{배치키}`)은 주기 종료 시 해제되므로 보통은 신경 쓸 필요가 없다. 다만 **직전 서버를 강제 종료(Stop-Process)해 락이 남은 경우** TTL(최대 5분)이 지나기 전에는 그 배치가 스킵되니, 배치 동작을 즉시 확인해야 하면 해당 락 키를 지우고 기동한다.
- **서비스 클래스 메서드 주석 필수**: 서비스 클래스(`*Service`, 예: `AuthService`·`SaveService`·`StageService`)에 속한 **모든 메서드**(public·private 헬퍼·생성자 포함)에는 그 메서드가 **어떤 로직을 수행하는지** 설명하는 `/// <summary>` 주석을 반드시 작성한다. 새 서비스 메서드를 추가하거나 기존 메서드를 수정할 때 이 규칙을 매번 확인한다.
- **로깅 규칙 준수**: 서버에 로그를 추가·수정할 때는 [`docs/공통/로깅-규칙.md`](docs/공통/로깅-규칙.md)를 따른다. 로거는 `ILogger<T>` 생성자 주입, 기록은 **ZLogger 확장 메서드**(`logger.ZLogInformation($"... {userId:@UserId}")` — 값마다 `:@PascalCase`로 필드 이름 지정, 표준 `Log*` 직접 호출·문자열 `+` 연결 금지), 레벨 기준(사용자 실수는 로깅 안 함, 경합은 Warning, 서버 결함은 Error), 로깅은 서비스/미들웨어에서(컨트롤러 금지), 비밀번호·토큰·PII 미노출을 매번 확인한다.
- **기능 설계·구현 시 통합 문서 동기화 및 정합성 체크**: 기능을 설계(기획서 작성)하거나 구현·변경하면, 해당 **세부 기획서**(`docs/세부/<기능>-기획서.md`)와 함께 아래 **공통 통합 문서를 같은 작업에서 갱신**한다. 정본은 세부 기획서이고 이 셋은 기획서를 가로질러 모은 **집약 참조본**이므로, 어느 하나만 바뀌면 곧 불일치가 된다.

  | 무엇이 바뀌면 | 갱신할 문서 | 갱신 대상 |
  |---|---|---|
  | 엔드포인트 추가·삭제·**경로/요청·응답 필드 변경** | [`docs/공통/api-통합.md`](docs/공통/api-통합.md) | 목차의 도메인 절 · 그 절의 엔드포인트 표(경로·기능·요청 `data`·응답 주요·주요 에러) · 하단 설명 bullet · 「출처 문서」 |
  | DB 테이블·컬럼, 마스터 테이블 추가·변경 | [`docs/공통/db-erd-통합.md`](docs/공통/db-erd-통합.md) | 목차의 테이블 항목 · `erDiagram`(관계 + 엔티티 블록) · 「PK / 유니크」 표 · 「테이블별 역할·저장 데이터」 절 · 마스터 요약 표 · 1장 「저장소 구성」(Redis 키·캐시가 늘면) · 「출처 문서」 |
  | 에러 코드 추가·폐기 | [`docs/공통/error-code-정의.md`](docs/공통/error-code-정의.md) | 도메인 블록 표 · 코드 목록 · `ErrorCode.cs` 반영안 · 「출처」 (+ 실제 `TaskbarHero.Common/ErrorCode.cs`) |

  **정합성 체크(작업 끝에 매번 확인한다)**
  - **양방향으로 본다**: 기획서 → 통합 문서(빠진 항목 추가)와 통합 문서 → 기획서(폐기된 엔드포인트·테이블·필드가 남아 있지 않은지)를 모두 확인한다. 경로를 바꿨으면 **옛 경로 문자열이 문서 어디에도 남지 않아야 한다**(`grep`으로 확인).
  - **목차와 본문이 일치해야 한다**: 두 통합 문서는 목차에 항목을 나열하므로, 절/테이블을 추가하면 목차에도 넣는다(목차에만 있거나 본문에만 있는 항목이 없어야 한다).
  - **도메인 간 파급을 확인한다**: 새 기능이 가방·재화·메일·캐시를 건드리면 그 도메인 문서의 목록(예: `inventoryDelta`를 담는 엔드포인트 목록, 가방 캐시 갱신 주체, 메일 발급 주체)에도 추가한다.
- **기능 구현 시 시퀀스 다이어그램 작성/갱신**: 새 컨트롤러·엔드포인트를 구현하거나 기존 처리 흐름을 변경하면, **`SequenceDiagram/README.md` 단일 문서**를 갱신한다. 이 문서는 컨트롤러별 파일로 나누지 않고 **기능별 섹션**(로그인/인증, 세이브 데이터/캐릭터 생성, 스테이지, 방치형 오프라인 보상, 인벤토리/아이템, 큐브, 성장 등)으로 정리하며, 각 섹션에 엔드포인트별 `sequenceDiagram` 블록을 둔다. 새 기능이면 섹션과 상단 **「기능 목차」 표**(기능 → 앵커 링크·처리 컨트롤러·주요 엔드포인트)에 함께 추가한다. 참여자는 **프로세스 단위로 요약**한다 — `클라이언트`(actor) → `AccountServer`/`GameServer`(컨트롤러·서비스·리포지토리·캐시를 하나로 합침) → `MySQL(account)`/`MySQL(game)`·`Redis`. 인메모리 마스터 데이터 조회와 서버 내부 검증·계산은 서버 자기호출(`S->>S: ...`)로 표기하고, 인증 미들웨어는 생략한다. 주요 분기·에러 코드(`alt`)·트랜잭션 경계(`Note over S,DB`)를 포함하며, 응답은 `성공 { ... }`/`실패 { errorCode: ... }`로 줄여 쓴다(상세 규약: `SequenceDiagram/README.md`의 「참여자 표기」·「범례」). 기능 구현 시 이 규칙을 매번 확인한다.
- **커밋은 사용자만 한다 (예외 없음)**: 어시스턴트는 `git commit`·`git push`를 **어떤 상황에서도 스스로 실행하지 않는다.** 사용자가 커밋 시점·단위·메시지를 직접 통제한다. 작업이 끝나면 **변경 사항 요약만 전달하고 멈춘다** — "커밋할까요?"는 물론 **"커밋하지 않았습니다"·"별도 커밋으로 나누는 게 좋겠습니다" 같은 상태 보고·단위 제안도 붙이지 않는다**(커밋은 화제 자체가 사용자 영역이다). 사용자가 **명시적으로 커밋을 요청했을 때만** 커밋한다. 스테이징(`git add`)은 작업의 일부로 필요할 때만 하고, 했으면 그 사실만 사실로 적는다.
