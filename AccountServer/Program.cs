using AccountServer.Auth;
using AccountServer.Data;
using AccountServer.Middleware;
using AccountServer.Repositories;
using AccountServer.Services;
using CloudStructures;

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

// 계정/인증 계층 DI: Controller → Service → Repository → AccountDbFactory(MySQL).
builder.Services.AddSingleton<AccountDbFactory>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IAuthTokenRepository, AuthTokenRepository>();

// 토큰 발급기(HMAC) + Redis 토큰 캐시(CloudStructures).
builder.Services.AddSingleton<TokenGenerator>();
builder.Services.AddSingleton(sp =>
{
    var connectionString = builder.Configuration.GetValue("Redis:ConnectionString", "127.0.0.1:6379")!;
    return new RedisConnection(new RedisConfig("account", connectionString));
});
builder.Services.AddSingleton<IAuthTokenCache, RedisAuthTokenCache>();

builder.Services.AddScoped<IAuthService, AuthService>();

var app = builder.Build();

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
        options.SwaggerEndpoint("/openapi/v1.json", "AccountServer v1");
        options.RoutePrefix = "swagger";
    });
}

app.UseHttpsRedirection();

app.MapControllers();

app.Run();
