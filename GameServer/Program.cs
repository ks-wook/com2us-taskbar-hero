using CloudStructures;
using GameServer.Auth;
using GameServer.Data;
using GameServer.MasterData;
using GameServer.Middleware;
using GameServer.Repositories;
using GameServer.Services;

// DB 조회는 SqlKata 제네릭 매핑(.GetAsync<T>/.FirstOrDefaultAsync<T>)으로 POCO에 매핑한다(dynamic 금지, CLAUDE.md 규칙).
// snake_case 컬럼 → PascalCase 프로퍼티 자동 매핑을 위해 Dapper 규칙을 켠다(SqlKata.Execution이 Dapper로 실행).
Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;

var builder = WebApplication.CreateBuilder(args);

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

// Redis 토큰 조회기(CloudStructures) — 인증 미들웨어가 사용.
builder.Services.AddSingleton(_ =>
{
    var connectionString = builder.Configuration.GetValue("Redis:ConnectionString", "127.0.0.1:6379")!;
    return new RedisConnection(new RedisConfig("game", connectionString));
});
builder.Services.AddSingleton<IAuthTokenReader, RedisAuthTokenReader>();

// 세이브 계층: Controller → Service → Repository.
builder.Services.AddScoped<ISaveRepository, SaveRepository>();
builder.Services.AddScoped<ISaveService, SaveService>();

// 스테이지 진입·클리어 계층.
builder.Services.AddScoped<IStageRepository, StageRepository>();
builder.Services.AddScoped<IStageService, StageService>();

// 인벤토리/아이템 액션 계층(장착·해제·배치 이동).
builder.Services.AddScoped<IInventoryRepository, InventoryRepository>();
builder.Services.AddScoped<IInventoryService, InventoryService>();

// 성장(스킬·룬) 액션 계층(레벨업·초기화·장착·룬 업그레이드).
builder.Services.AddScoped<IGrowthRepository, GrowthRepository>();
builder.Services.AddScoped<IGrowthService, GrowthService>();

// 큐브 액션 계층(합성·분해·제작).
builder.Services.AddScoped<ICubeRepository, CubeRepository>();
builder.Services.AddScoped<ICubeService, CubeService>();

// 오프라인(방치) 보상 정산 계층.
builder.Services.AddScoped<IOfflineRepository, OfflineRepository>();
builder.Services.AddScoped<IOfflineService, OfflineService>();

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
