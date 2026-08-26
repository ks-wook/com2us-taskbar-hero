using CloudStructures;
using GameServer;
using Utf8StringInterpolation;
using ZLogger;
using GameServer.Auth;
using GameServer.MasterData;
using GameServer.Middleware;
using GameServer.Repositories.GameDb;
using GameServer.Repositories.GameDb.Interfaces;
using GameServer.Services;
using GameServer.Services.Interfaces;
using GameServer.Repositories.MemoryDb.Interfaces;
using GameServer.Repositories.MemoryDb;
using GameServer.Repositories.MasterDb.Interfaces;
using GameServer.Repositories.MasterDb;
using GameServer.Models;
using System.Reflection;
using GameServer.Logging;
using ZLogger.Providers;

// DB 조회는 SqlKata 제네릭 매핑(.GetAsync<T>/.FirstOrDefaultAsync<T>)으로 POCO에 매핑한다(dynamic 금지, CLAUDE.md 규칙).
// snake_case 컬럼 → PascalCase 프로퍼티 자동 매핑을 위해 Dapper 규칙을 켠다(SqlKata.Execution이 Dapper로 실행).
Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;

var builder = WebApplication.CreateBuilder(args);

// 로깅: ZLogger 콘솔 프로바이더로 교체한다(로깅 규칙 §1).
// 기본 Console 프로바이더를 제거하고 ZLogger만 남겨 출력 형식을 하나로 통일한다.
// 레벨 필터는 appsettings의 Logging:LogLevel을 그대로 쓴다(코드에서 하드코딩하지 않는다).
builder.Logging.ClearProviders();
builder.Logging.AddZLoggerConsole(options =>
{
    // 사람이 읽는 한 줄 형식: [시각] [레벨] [카테고리] 메시지
    options.UsePlainTextFormatter(formatter =>
    {
        formatter.SetPrefixFormatter(
            $"[{0:yyyy-MM-dd HH:mm:ss.fff}] [{1:short}] [{2}] ",
            (in MessageTemplate template, in LogInfo info) =>
                template.Format(info.Timestamp.Local.DateTime, info.LogLevel, info.Category));

        // 예외는 본문과 줄을 나눠 타입·메시지·스택을 붙인다(로깅 규칙 §6).
        formatter.SetExceptionFormatter((writer, ex) => Utf8String.Format(
            writer,
            $"{Environment.NewLine}{ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace ?? string.Empty}"));
    });
});

// 이벤트 로그(집계용): 사람이 읽는 운영 로그와 **완전히 다른 경로**다(로그 이벤트 정의 3장).
//   운영 로그 = 한글 평문 → 콘솔 / 이벤트 로그 = 평탄 JSON 1줄 → 전용 파일 → fluentd in_tail → logdb.
//   같은 stdout에 섞으면 수집기가 평문과 JSON을 갈라 파싱해야 하므로, 카테고리(TaskbarHero.EventLog)로
//   나누고 sink 자체를 분리한다. 라인은 EventLogger가 통째로 만들어 넘기므로 여기서는 **접두사 없이
//   메시지만** 쓴다 — 포매터가 필드를 덧붙이면 out_sql의 컬럼 매핑이 어긋난다(4.1).
var eventLogDirectory = builder.Configuration.GetValue("EventLog:Directory", "logs/event")!;
Directory.CreateDirectory(eventLogDirectory);
builder.Logging.AddZLoggerRollingFile(options =>
{
    options.FilePathSelector = (timestamp, sequence) =>
        Path.Combine(eventLogDirectory, $"event-{timestamp.ToLocalTime():yyyyMMdd}_{sequence:000}.json");
    options.RollingInterval = RollingInterval.Day;
    options.RollingSizeKB = 1024 * 100; // 100MB마다 파일을 끊는다(tail 대상이 무한히 커지지 않게).
    options.UsePlainTextFormatter(formatter =>
    {
        formatter.SetPrefixFormatter($"", (in MessageTemplate _, in LogInfo _) => { });
        formatter.SetSuffixFormatter($"", (in MessageTemplate _, in LogInfo _) => { });
    });
});

// sink 라우팅: 이벤트 로그는 파일에만, 운영 로그는 콘솔에만 간다.
builder.Logging.AddFilter<ZLoggerConsoleLoggerProvider>(Constants.EventLog.Category, LogLevel.None);
builder.Logging.AddFilter<ZLoggerRollingFileLoggerProvider>(null, LogLevel.None);
builder.Logging.AddFilter<ZLoggerRollingFileLoggerProvider>(Constants.EventLog.Category, LogLevel.Information);


// MVC 컨트롤러 + OpenAPI.
// DTO는 TaskbarHero.Common의 [Serializable] + public 필드(Unity JsonUtility 공유용)이므로
// System.Text.Json이 필드도 직렬화하도록 IncludeFields를 켠다.
builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.IncludeFields = true);
builder.Services.AddOpenApi();

// 이벤트 로그 방출기. req_id를 HttpContext.TraceIdentifier에서 얻으므로 접근자를 함께 등록한다.
builder.Services.AddHttpContextAccessor();

// 이벤트 로거는 GameServer.Core에 있어 웹 스택을 모른다(BatchServer와 공유하기 때문).
//   req_id를 어디서 얻는지만 호스트가 정해 주입한다 — 여기서는 HttpContext.TraceIdentifier.
builder.Services.AddSingleton<IRequestIdAccessor, HttpRequestIdAccessor>();
builder.Services.AddSingleton<IEventLogger, EventLogger>();

// 전역 예외 처리기(미처리 예외 → Error 로깅 + 일반화 500 응답). 로깅 규칙 §6.
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// DB 접근 팩토리(SqlKata + MySqlConnector).
builder.Services.AddSingleton<GameDbFactory>();

// 마스터 데이터 인메모리 캐시(기동 시 1회 적재).
builder.Services.AddSingleton<IMasterDbLoader, MasterDbLoader>();
builder.Services.AddSingleton<MasterDbProvider>();

// 마스터 데이터에서 파생되는 계산기. 리포지토리에 생성자 주입되어 **트랜잭션 안에서** 호출되므로
// 계산 함수를 델리게이트 인자로 넘기지 않고도 트랜잭션 경계(잠금 읽기 → 결정 → 쓰기)가 유지된다.
//   · ILevelUpCalculator  — 경험치→레벨 재계산(level_master). 스테이지 클리어·오프라인 정산이 같은 계산을
//     쓰므로 정본을 하나로 둔다(두 서비스가 각자 들고 있던 ApplyExp 사본을 제거했다).
//   · ICubeLevelCalculator — 큐브 경험치→레벨 재계산(cube_master). 합성·분해·제작 세 연산이 공유한다.
//   · IItemLookup         — 적재 규칙·거래 검증에 필요한 item_master 속성. 메일·가챠·거래가 한 곳을 쓴다
//     (세 서비스의 조회 헬퍼가 StackMax 보정에서 서로 달랐던 문제를 없앤다).
// 셋 다 무상태이고 마스터가 싱글턴이라 싱글턴으로 둔다.
builder.Services.AddSingleton<ILevelUpCalculator, LevelUpCalculator>();
builder.Services.AddSingleton<IItemLookup, ItemLookup>();
builder.Services.AddSingleton<ICubeLevelCalculator, CubeLevelCalculator>();

// Redis 토큰 조회기(CloudStructures) — 인증 미들웨어가 사용.
builder.Services.AddSingleton(_ =>
{
    var connectionString = builder.Configuration.GetValue("Redis:ConnectionString", "127.0.0.1:36379")!;
    return new RedisConnection(new RedisConfig("game", connectionString));
});
builder.Services.AddSingleton<IAuthTokenReader, AuthTokenReader>();

// 세이브 계층: Controller → Service → Repository.
builder.Services.AddScoped<ISaveRepository, SaveRepository>();
builder.Services.AddScoped<ISaveService, SaveService>();

// 스테이지 진입·클리어 계층.
builder.Services.AddScoped<IStageRepository, StageRepository>();
builder.Services.AddScoped<IStageService, StageService>();

// 인벤토리/아이템 액션 계층(장착·해제·배치 이동).
// 가방 조회(/inventory/list)에는 캐시를 두지 않는다 — slot 커서 keyset 질의가 (user_id, slot) 유니크
// 인덱스를 그대로 타서 조인·정렬 없이 필요한 구간만 읽는다(기획서 6.5).
builder.Services.AddScoped<IInventoryRepository, InventoryRepository>();
builder.Services.AddScoped<IInventoryService, InventoryService>();

// 성장(스킬·룬) 액션 계층(레벨업·초기화·장착·룬 업그레이드).
builder.Services.AddScoped<IGrowthRepository, GrowthRepository>();
builder.Services.AddScoped<IGrowthService, GrowthService>();

// 큐브 액션 계층(합성·분해·제작).
builder.Services.AddScoped<ICubeRepository, CubeRepository>();
builder.Services.AddScoped<ICubeService, CubeService>();

// 소모품 사용 계층(아이템 1개 차감 → 계정 획득량 버프 부여·연장). 활성 버프 조회는 코어 로드가 담당.
builder.Services.AddScoped<IConsumableRepository, ConsumableRepository>();
builder.Services.AddScoped<IConsumableService, ConsumableService>();

// 오프라인(방치) 보상 정산 계층.
builder.Services.AddScoped<IOfflineRepository, OfflineRepository>();
builder.Services.AddScoped<IOfflineService, OfflineService>();

// 메일(우편함) 계층(목록·수령·일괄 수령).
builder.Services.AddScoped<IMailRepository, MailRepository>();
builder.Services.AddScoped<IMailService, MailService>();

// 출석부 보상 계층(현황 조회·오늘자 획득 → 보상 메일 발급).
builder.Services.AddScoped<IAttendanceRepository, AttendanceRepository>();
builder.Services.AddScoped<IAttendanceService, AttendanceService>();

// 가챠(뽑기) 계층(배너 조회·1연·10연·기록 조회). 추첨·천장·보장은 서버 권위(가챠 기획서 §5·6).
builder.Services.AddScoped<IGachaRepository, GachaRepository>();
builder.Services.AddScoped<IGachaService, GachaService>();

// 보스러시 / 랭킹 계층(정보 조회·도전 시작·클리어 보고·랭킹 목록·내 순위).
// 전투와 시간 측정은 클라이언트 권위이므로 서버는 도전 원장과 보고된 기록의 형식 검증·등재·순위 산출만 한다.
// 랭킹 조회의 정상 경로는 Redis 단독이다 — 순위·기록은 리더보드 ZSET(점수에 clearMs·recordedAt 인코딩),
// 표시 이름은 player:nickname 해시(HMGET, 미스만 game_player에서 부분 백필), 시즌 메타는
// bossrush:season:current 해시에서 나온다. MySQL은 캐시 미스·폴백·종료 시즌 조회에서만 개입한다(기획서 4.3·6.3).
builder.Services.AddScoped<IBossRushRepository, BossRushRepository>();
builder.Services.AddScoped<IBossRushRankCache, BossRushRankCache>();

builder.Services.AddScoped<IBossRushService, BossRushService>();

// 랭킹 캐시 최초 적재(워밍업) — **서버가 스스로 하지 않는다.** 부트스트랩 스크립트(server_up_with_docker.py)가
//   컨테이너·서버 기동을 확인한 뒤 관리 API(POST /api/admin/boss-rush/rank/warmup)로 한 번 지시한다.
//   예전에는 시즌 정산 배치가 리더 락을 쥔 채 매 주기 앞단에서 이 일을 했는데, 적재 시점이 배치 주기에
//   묶여 눈에 보이지 않았다. 호출자가 스크립트 하나로 정해지면서 중복 재구축을 막을 분산 락도 필요 없어졌다.
//   정본이 MySQL이라 몇 번을 돌려도 안전하다(ZADD는 userId 단위 덮어쓰기, 점수는 기록에서 결정론적 계산).
builder.Services.AddScoped<IBossRushRankWarmupService, BossRushRankWarmupService>();

// 거래소(교역선) 계층(목록·등록·구매·취소). Redis를 쓰지 않는다 — 목록은 전용 색인을 타는 MySQL 직접 조회,
// 등록·구매·취소·만료의 직렬화는 MySQL 행 잠금이 담당한다(거래소 기획서 7.3·7.4).
builder.Services.AddScoped<ITradeRepository, TradeRepository>();
builder.Services.AddScoped<ITradeService, TradeService>();

// ── 주기 배치는 이 프로세스에 없다 ────────────────────────────────────────────────────────────
// 거래소 만료·메일 GC·보스러시 시즌 정산·히스토리 3종은 **BatchServer**(별도 워커 프로세스)가 돌린다.
//   왜 떼어냈나:
//     · 게임 API는 scale-out으로 N대까지 늘어나는데, 배치는 그중 1대만 돌아야 한다. 같은 프로세스에
//       두면 "N대 중 하나만"을 분산 락으로 매번 맞춰야 하지만, 프로세스를 나누면 배포가 그것을 정한다.
//     · 일 단위 히스토리는 player_item·player_character·player_skill 전체를 GROUP BY 하는 무거운
//       집계다. 같은 프로세스에 있으면 그 부하가 게임 API의 커넥션 풀·스레드풀과 직접 경합한다.
//     · 배치가 죽어도 게임 API는 살아 있고, 배치만 따로 재시작할 수 있다.
//   공유하는 것: 데이터 접근·모델·마스터 데이터·이벤트 로깅·상수 = GameServer.Core(두 프로젝트가 참조).
//   여기 남는 것: 컨트롤러·서비스·미들웨어·인증 — 요청/응답 고유 계층.
//   배치가 쓰는 리포지토리(History·Mail·Trade·BossRush)와 리더 락은 BatchServer/Program.cs에서 등록한다.

var app = builder.Build();

// 서버 기동·종료 이벤트(로그 이벤트 정의 5.11). 대시보드에서 **배포 시점 주석**으로 쓴다 —
// 지표가 꺾인 시점과 배포를 겹쳐 보기 위한 기준선이라, 요청이 아니라 프로세스 생애를 남긴다.
// 계정이 없는 시스템 이벤트라 uid도 req_id도 붙지 않는다.
var lifecycleLogger = app.Services.GetRequiredService<IEventLogger>();
var serverVersion = typeof(Program).Assembly
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
    ?? typeof(Program).Assembly.GetName().Version?.ToString()
    ?? "unknown";
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStarted.Register(() => lifecycleLogger.Action(
    Constants.EventLog.Tags.ServerLifecycle, null,
    new ServerLifecycleEvent(ServerLifecyclePhase.Start, serverVersion)));
lifetime.ApplicationStopping.Register(() => lifecycleLogger.Action(
    Constants.EventLog.Tags.ServerLifecycle, null,
    new ServerLifecycleEvent(ServerLifecyclePhase.Stop, serverVersion)));

// 마스터 데이터 기동 시 적재(실패 시 IsLoaded=false → 관련 요청은 MasterDataNotLoaded).
await app.Services.GetRequiredService<MasterDbProvider>().LoadAsync();

// 파이프라인 최외곽: 접근 로그(요청 1줄) → 전역 예외 처리 순(로깅 규칙 §4·§6).
// 접근 로그를 바깥에 두어야 예외 처리기가 500으로 확정한 상태코드까지 기록된다.
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseExceptionHandler();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    // Swagger UI(/swagger)에서 위 OpenAPI 문서(/openapi/v1.json)를 렌더링한다.
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "GameServer v1");
        options.RoutePrefix = "swagger";
    });
}

app.UseHttpsRedirection();

// 게임 API 인증(요청 body의 userId·token을 Redis와 대조).
app.UseMiddleware<GameAuthMiddleware>();

app.MapControllers();

app.Run();
