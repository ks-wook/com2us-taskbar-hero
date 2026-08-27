using CloudStructures;
using GameServer;
using GameServer.Batch;
using GameServer.Logging;
using GameServer.MasterData;
using GameServer.Repositories.GameDb;
using GameServer.Repositories.GameDb.Interfaces;
using GameServer.Repositories.MasterDb;
using GameServer.Repositories.MasterDb.Interfaces;
using GameServer.Repositories.MemoryDb;
using GameServer.Repositories.MemoryDb.Interfaces;
using Utf8StringInterpolation;
using ZLogger;
using ZLogger.Providers;

// 배치 전담 워커. HTTP를 받지 않으므로 컨트롤러·인증 미들웨어·OpenAPI가 없고,
// 데이터 접근·마스터 데이터·이벤트 로깅은 GameServer 프로젝트를 참조해 그대로 쓴다.
//
// **이 프로세스는 1대만 뜬다**(compose는 container_name 고정). 게임 API를 N대로 늘려도
// 배치는 늘지 않는 것이 분리의 목적이다. 그래서 **분산 락을 쓰지 않는다** — "N대 중 하나만"을 매 발화마다
// 맞출 이유가 없다. 배치는 모두 **정해진 시각**(매시 00분·KST 05시·시즌 end_at)에 실행되므로, 프로세스를
// 언제 띄웠는지와 무관하게 늘 같은 시각에 돈다.

// DB 조회는 SqlKata 제네릭 매핑(.GetAsync<T>/.FirstOrDefaultAsync<T>)으로 POCO에 매핑한다(dynamic 금지, CLAUDE.md 규칙).
// snake_case 컬럼 → PascalCase 프로퍼티 자동 매핑을 위해 Dapper 규칙을 켠다(SqlKata.Execution이 Dapper로 실행).
Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;

var builder = Host.CreateApplicationBuilder(args);

// 로깅: ZLogger 콘솔 프로바이더로 교체한다(로깅 규칙 §1). GameServer와 같은 형식을 쓴다 —
// 두 프로세스의 운영 로그를 같은 눈으로 읽어야 하기 때문이다.
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
//   **GameServer와 같은 디렉터리에 쓴다** — fluentd가 그 폴더 하나만 in_tail 하므로, 어느 프로세스가
//   냈든 같은 파이프라인으로 흘러가야 한다(compose가 두 서비스에 같은 호스트 경로를 마운트한다).
//   파일명에 프로세스 구분을 넣지 않아도 되는 이유: 라인 자체가 tag로 구분되고, 수집기는 tag만 본다.
var eventLogDirectory = builder.Configuration.GetValue("EventLog:Directory", "logs/event")!;
Directory.CreateDirectory(eventLogDirectory);
builder.Logging.AddZLoggerRollingFile(options =>
{
    // GameServer와 같은 폴더를 쓰므로 파일명 접두사를 달리해 **두 프로세스가 같은 파일을 두고 다투지 않게** 한다.
    // fluentd의 in_tail 패턴은 이 폴더의 *.json을 모두 걷는다.
    options.FilePathSelector = (timestamp, sequence) =>
        Path.Combine(eventLogDirectory, $"event-batch-{timestamp.ToLocalTime():yyyyMMdd}_{sequence:000}.json");
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

// 이벤트 로그 방출기. 요청이라는 개념이 없는 프로세스라 req_id는 언제나 null이다 —
// 배치가 내는 라인은 원래 req_id가 없으므로(로그 이벤트 정의 4.1) 축소가 아니라 정확한 표현이다.
builder.Services.AddSingleton<IRequestIdAccessor, NullRequestIdAccessor>();
builder.Services.AddSingleton<IEventLogger, EventLogger>();

// DB 접근 팩토리(SqlKata + MySqlConnector).
builder.Services.AddSingleton<GameDbFactory>();

// 마스터 데이터 인메모리 캐시(기동 시 1회 적재).
//   배치도 마스터를 읽는다 — 보스러시 시즌 규칙·순위 보상 구간·메일 템플릿·아이템 속성.
builder.Services.AddSingleton<IMasterDbLoader, MasterDbLoader>();
builder.Services.AddSingleton<MasterDbProvider>();

// 마스터에서 파생되는 조회기. 메일·거래 리포지토리가 적재 규칙(StackMax 등)을 여기서 읽는다.
//   무상태이고 마스터가 싱글턴이라 싱글턴으로 둔다. 레벨·큐브 계산기는 배치가 쓰지 않아 등록하지 않는다.
builder.Services.AddSingleton<IItemLookup, ItemLookup>();

// Redis(CloudStructures) — 보스러시 랭킹 캐시(리더보드 TTL·다음 시즌 메타)에 쓴다.
//   인증 토큰은 읽지 않는다(요청을 받지 않으므로 AuthTokenReader를 등록하지 않는다).
//   배치 리더 락은 없앴다 — 이 프로세스가 1대이므로 잠글 상대가 없다.
builder.Services.AddSingleton(_ =>
{
    var connectionString = builder.Configuration.GetValue("Redis:ConnectionString", "127.0.0.1:36379")!;
    return new RedisConnection(new RedisConfig("batch", connectionString));
});

// 배치가 쓰는 데이터 접근만 등록한다 — 게임 API 전용 리포지토리(세이브·스테이지·인벤토리·성장·큐브·
// 소모품·출석·가챠·오프라인)는 이 프로세스에 필요 없다.
builder.Services.AddScoped<IBossRushRepository, BossRushRepository>();
builder.Services.AddScoped<IBossRushRankCache, BossRushRankCache>();
builder.Services.AddScoped<IHistoryRepository, HistoryRepository>();
builder.Services.AddScoped<IMailRepository, MailRepository>();
builder.Services.AddScoped<ITradeRepository, TradeRepository>();

// ── 주기 배치(BackgroundService) ───────────────────────────────────────────────────────────────
// 여섯 배치 모두 공통 골격 PeriodicBatchScheduler를 상속하며, 그 골격이 다음을 보장한다:
//   · **실행 시각이 고정이다.** 프로세스를 언제 띄웠든 정해진 시각에 돈다 — 재기동으로 집계 시각이 밀리지 않는다.
//   · 밀린 실행은 한 번으로 접는다. 기동 직후 지나간 실행을 따라잡을지는 배치마다 다르다.
//   · 순차 루프라 이전 작업이 끝나야 다음 실행 시각을 계산한다(재진입 불가).
//   · **분산 락이 없다.** 이 프로세스가 1대인 것을 배포가 보장하므로 잠글 상대가 없다.
//   · 1회 실패는 Error 로그만 남기고 루프를 유지한다(배치 사망으로 대상이 영구 방치되는 것 방지).
// 실행 시각·주기·1회 처리 상한은 **BatchSettingConstants.cs**에 모여 있다(파일 상단에 여섯 배치의
// 실행 시각 요약표). appsettings가 기본값을 덮어쓰며, 값이 없거나 0 이하이면 기본값을 쓴다.

// 거래소 만료 배치(등록 3일 경과 → status 정리 + 에스크로 아이템 메일 반송, trade 기획서 7.6).
//   실행 시각: **매시 00분** — 값은 BatchSettingConstants.TradeExpire.
//   1회 처리 상한은 주기보다 넉넉히 잡아 프로세스가 내려가 있던 동안 밀린 물량을 소화한다.
//   **만료 판정은 이 배치가 하지 않는다.** 목록·단건 조회·구매·등록 한도 쿼리가 모두 `expires_at > now`를
//   직접 검사하므로(TradeRepository), 만료된 매물은 배치를 기다리지 않고 즉시 목록에서 빠진다.
//   따라서 이 주기는 **에스크로 아이템이 메일로 반송되기까지의 지연 상한**만 결정한다.
builder.Services.AddHostedService<TradeExpireBatchScheduler>();

// 메일 보관 GC 배치(발급 7일 경과 메일 삭제, mail 기획서 6.5).
//   실행 시각: **매시 00분** — 값은 BatchSettingConstants.MailGc.
//   보관 기간(Constants.Mail.RetentionSeconds)에 비해 삭제가 몇 분~한 시간 늦어도 사용자에게 보이는 차이가
//   없어 시간 단위로 넉넉히 잡았다.
builder.Services.AddHostedService<MailGcBatchScheduler>();

// 보스러시 시즌 정산 배치(주간 시즌 종료 → 순위 확정 + 1~3위 골드 보상 메일 발급 → 다음 시즌 개시, 기획서 6.4).
//   **폴링하지 않는다** — 정산이 필요한 순간은 진행 중 시즌의 end_at 하나뿐이라 **그 시각을 그대로 발화
//   시각으로 삼는다**. 며칠 뒤여도 그때까지 통째로 자고 정확히 그 시각에 깨어난다. 진행 중 시즌이 없으면
//   (첫 시즌 미등록·마스터 미적재) 기본 간격으로 되돌아가 다시 살핀다.
//   1회(페이지) 처리 상한(BatchSettingConstants.BossRushSeason.DefaultBatchSize) — 페이지 단위 트랜잭션으로 쪼개
//   긴 잠금을 만들지 않는다.
//   정산은 final_rank=0 조건부 갱신이라 멱등하며, 중간에 죽어도 다음 발화가 남은 행만 이어서 처리한다.
//   **이어받기 재시도는 5회까지**(BatchSettingConstants.BossRushSeason.MaxRecoveryAttempts) — 한 건도 확정하지
//   못한 시도가 그만큼 연속되면 재시도로 풀리지 않는 원인이므로, 발화마다 DB를 다시 두드리지 않고 멈춘다.
//   **랭킹 캐시 워밍업은 이 배치가 하지 않는다** — 부트스트랩 스크립트가 GameServer의 관리 API로 지시한다.
builder.Services.AddHostedService<BossRushSeasonBatchScheduler>();

// 히스토리(주기 스냅샷) 배치 3종 — 로그 이벤트 정의 7장.
//   액션 로그가 **변화**를 담는 데 반해 이쪽은 **총량과 현재 상태**를 담는다. 게임 DB를 세어 이벤트 로그
//   1줄을 내보내는 것이 전부다(이 프로세스도 logdb에 접속하지 않는다).
//   주기별로 셋으로 나눈 기준은 "같은 시점의 스냅샷이어야 서로 나눠 볼 수 있는가"다.
//   ① 5분 — 동시 접속(history.online_user). 하트비트를 액션 로그로 남기면 그것 하나가 전체 볼륨을
//      넘으므로, 같은 주기에 접속자 수만 세어 1행으로 대신한다. **기동 시 따라잡지 않는다** — 발화 1회가
//      곧 그래프의 점 하나라, 재기동할 때마다 같은 구간에 점이 하나 더 찍히면 추이가 부풀려진다.
//   ② 1시간 — 재화 유통 총량 + 거래소 호가. 둘은 함께 읽어야 뜻이 생기는 짝이라(총량↑·호가↑=인플레이션,
//      총량 유지·호가↑=품귀) 한 배치에서 같은 시각 기준으로 낸다. ①과 같은 이유로 기동 시 따라잡지 않는다.
//   ③ 1일 — 상태 스냅샷 6종(아이템 유통량·진행도·착용 장비·파티 조합·스킬 조합·스킬 투자). player_item·
//      player_character·player_skill 전체를 GROUP BY 하는 무거운 집계라 **트래픽이 낮은 시간대(KST 05시)**에
//      몰아 돌린다. **이 무거운 집계가 게임 API의 커넥션·스레드와 경합하지 않는 것**이 프로세스를 나눈
//      실질적인 이득이다. 적재 테이블이 (log_date, …) 자연 키 PK라 같은 날 다시 돌면 덮어쓰므로,
//      예정 시각을 지나 기동하면 그날분을 **따라잡는다**(그날의 스냅샷이 비지 않는다).
builder.Services.AddHostedService<OnlineUserHistoryBatchScheduler>();
builder.Services.AddHostedService<HourlyHistoryBatchScheduler>();
builder.Services.AddHostedService<DailyHistoryBatchScheduler>();

var host = builder.Build();

// 마스터 데이터 기동 시 적재. **배치가 돌기 전에 끝나야 한다** — 보스러시 정산이 시즌 규칙과 보상
// 구간을 여기서 읽기 때문이다(미적재면 그 배치는 아무 일도 하지 않고 넘어간다).
await host.Services.GetRequiredService<MasterDbProvider>().LoadAsync();

// server.lifecycle 이벤트는 **내지 않는다** — 그 이벤트에는 어느 프로세스인지 구분하는 필드가 없어
// (ServerLifecycleEvent = phase + version), 여기서도 내면 대시보드의 배포 주석이 두 줄로 겹친다.
// 배포 시점의 기준선은 GameServer 하나가 대표한다.

host.Run();
