using CloudStructures;
using GameServer.Auth;
using GameServer.Data;
using GameServer.MasterData;
using GameServer.Repositories;
using GameServer.Services;

var builder = WebApplication.CreateBuilder(args);

// MVC 컨트롤러 + OpenAPI.
// DTO는 TaskbarHero.Common의 [Serializable] + public 필드(Unity JsonUtility 공유용)이므로
// System.Text.Json이 필드도 직렬화하도록 IncludeFields를 켠다.
builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.IncludeFields = true);
builder.Services.AddOpenApi();

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

var app = builder.Build();

// 마스터 데이터 기동 시 적재(실패 시 IsLoaded=false → 관련 요청은 MasterDataNotLoaded).
await app.Services.GetRequiredService<MasterDataProvider>().LoadAsync();

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
