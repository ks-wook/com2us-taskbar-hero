using CloudStructures;
using Utf8StringInterpolation;
using ZLogger;
using GameServer.Auth;
using GameServer.Batch;
using GameServer.Data;
using GameServer.MasterData;
using GameServer.Middleware;
using GameServer.Repositories.GameDb;
using GameServer.Repositories.GameDb.Interfaces;
using GameServer.Services;
using GameServer.Repositories.MemoryDb.Interfaces;
using GameServer.Repositories.MemoryDb;

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


// MVC 컨트롤러 + OpenAPI.
// DTO는 TaskbarHero.Common의 [Serializable] + public 필드(Unity JsonUtility 공유용)이므로
// System.Text.Json이 필드도 직렬화하도록 IncludeFields를 켠다.
builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.IncludeFields = true);
builder.Services.AddOpenApi();

// 전역 예외 처리기(미처리 예외 → Error 로깅 + 일반화 500 응답). 로깅 규칙 §6.
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// DB 접근 팩토리(SqlKata + MySqlConnector).
builder.Services.AddSingleton<GameDbFactory>();
builder.Services.AddSingleton<MasterDbFactory>();

// 마스터 데이터 인메모리 캐시(기동 시 1회 적재).
builder.Services.AddSingleton<MasterDataProvider>();

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
    var connectionString = builder.Configuration.GetValue("Redis:ConnectionString", "127.0.0.1:6379")!;
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

// 배치 리더 락(batch:lock:{배치키}) — 주기 배치가 scale-out 환경에서 중복 실행되지 않게 한다.
//   PeriodicBatchService(싱글턴 BackgroundService)가 주입받으므로 싱글턴으로 등록한다.
builder.Services.AddSingleton<IBatchLock, BatchLock>();
builder.Services.AddScoped<IBossRushService, BossRushService>();

// 거래소(교역선) 계층(목록·등록·구매·취소). Redis를 쓰지 않는다 — 목록은 전용 색인을 타는 MySQL 직접 조회,
// 등록·구매·취소·만료의 직렬화는 MySQL 행 잠금이 담당한다(거래소 기획서 7.3·7.4).
builder.Services.AddScoped<ITradeRepository, TradeRepository>();
builder.Services.AddScoped<ITradeService, TradeService>();

// ── 주기 배치(BackgroundService) ───────────────────────────────────────────────────────────────
// 두 배치 모두 공통 골격 PeriodicBatchService를 상속하며, 그 골격이 다음을 보장한다:
//   · 기동 직후 즉시 1회 실행 → 그 뒤 각자의 주기(Interval)로 반복(PeriodicTimer.WaitForNextTickAsync).
//     서버가 내려가 있던 동안 쌓인 대상을 첫 주기까지 기다리지 않고 바로 소화한다.
//   · 이전 주기가 끝난 뒤에야 다음 tick을 기다리므로 재진입(주기 겹침)이 구조적으로 불가능하다.
//     따라서 아래 "주기"는 정확히는 "이전 주기 종료 후 다음 실행까지의 간격"이다.
//   · 주기마다 Redis 리더 락(batch:lock:{배치키}, SET NX)을 먼저 잡고, 잡은 인스턴스만 실행한다
//     (scale-out 시 동시 실행 방지). **주기가 끝나면 소유자 확인 후 즉시 해제**하며, TTL은 락을 잡은 채
//     프로세스가 죽었을 때 자동으로 풀리게 하는 안전망이다(= min(주기, 5분), 1주기 실행 시간의 상한 기준).
//     "주기당 1회"는 락이 아니라 각 인스턴스의 타이머가 페이싱하고, 중복 실행은 작업의 멱등성이 흡수한다.
//   · 1주기 실패는 Error 로그만 남기고 루프를 유지한다(배치 사망으로 대상이 영구 방치되는 것 방지).
// 주기·1회 처리 상한은 appsettings에서 조절하며, 값이 없거나 0 이하이면 각 서비스의 기본값을 쓴다.

// 거래소 만료 배치(등록 3일 경과 → status 정리 + 에스크로 아이템 메일 반송, trade 기획서 7.6).
//   실행 주기: **3600초 = 1시간** — appsettings "TradeExpireBatch:IntervalSeconds"(기본 3600).
//   1회 처리 상한 1000건("BatchSize") — 주기보다 넉넉히 잡아 서버가 내려가 있던 동안 밀린 물량을 소화한다.
//   **만료 판정은 이 배치가 하지 않는다.** 목록·단건 조회·구매·등록 한도 쿼리가 모두 `expires_at > now`를
//   직접 검사하므로(TradeRepository), 만료된 매물은 배치를 기다리지 않고 즉시 목록에서 빠지고 구매는
//   TradeAlreadyClosed로 거부되며 판매자 등록 칸도 곧바로 풀린다.
//   따라서 이 주기는 "판매 기간 3일"의 정확도가 아니라 **에스크로 아이템이 메일로 반송되기까지의 지연 상한**
//   만 결정한다 — 3일을 기다린 판매자를 더 기다리게 하지 않도록 1시간으로 잡았다. 대상 조회가
//   idx_trade_expire를 커버링으로 타고 0건이면 로그도 남기지 않아 빈 주기 비용은 사실상 없다.
builder.Services.AddHostedService<TradeExpireBatchService>();

// 메일 보관 GC 배치(발급 7일 경과 메일 삭제, mail 기획서 6.5).
//   실행 주기: **3600초 = 1시간** — appsettings "MailGcBatch:IntervalSeconds"(기본 3600). 1회 처리 상한 500건("BatchSize").
//   보관 기간(7일)에 비해 삭제가 몇 분~한 시간 늦어도 사용자에게 보이는 차이가 없어 시간 단위로 넉넉히 잡았다.
//   상한을 넘긴 분량은 다음 주기로 이월된다(1시간마다 최대 500건 정리).
builder.Services.AddHostedService<MailGcBatchService>();

// 보스러시 시즌 정산 배치(주간 시즌 종료 → 순위 확정 + 1~3위 골드 보상 메일 발급 → 다음 시즌 개시, 기획서 6.4).
//   실행 주기: **600초 = 10분** — appsettings "BossRushSeasonBatch:IntervalSeconds"(기본 600).
//   1회(페이지) 처리 상한 500건("BatchSize") — 페이지 단위 트랜잭션으로 쪼개 긴 잠금을 만들지 않는다.
//   정산은 final_rank=0 조건부 갱신이라 멱등하며, 중간에 죽어도 다음 주기가 남은 행만 이어서 처리한다.
//   기동 시에는 정산 전에 **랭킹 캐시 워밍업**(리더보드가 비었으면 boss_rush_record에서 재구축 + 시즌 메타 캐시
//   채우기)도 수행한다 — 이미 리더 락이 여기 있어 scale-out 시 중복 재구축을 그대로 막아 준다.
//   **버려진 런을 정리하는 배치는 두지 않는다** — 만료된 런에 반송할 자산이 없어 배치가 할 일이 status 정리
//   뿐이므로, 만료 판정을 읽는 시점(clear·info·enter)에 한다(거래소의 만료 판정 규약과 동일).
builder.Services.AddHostedService<BossRushSeasonBatchService>();

// 리더 락은 주기 종료와 함께 해제되므로, 정상 종료·재기동 후에는 곧바로 다시 실행된다(옛 방식처럼 주기만큼
// 스킵되지 않는다). 프로세스가 락을 잡은 채 강제 종료된 경우에만 TTL(최대 5분)이 지나야 풀린다.

var app = builder.Build();

// 마스터 데이터 기동 시 적재(실패 시 IsLoaded=false → 관련 요청은 MasterDataNotLoaded).
await app.Services.GetRequiredService<MasterDataProvider>().LoadAsync();

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
