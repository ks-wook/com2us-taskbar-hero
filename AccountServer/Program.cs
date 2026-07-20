using AccountServer.Auth;
using AccountServer.Data;
using AccountServer.Repositories;
using AccountServer.Services;
using CloudStructures;

var builder = WebApplication.CreateBuilder(args);

// MVC 컨트롤러 + OpenAPI.
// DTO는 TaskbarHero.Common의 [Serializable] + public 필드(Unity JsonUtility 공유용)이므로
// System.Text.Json이 필드도 직렬화하도록 IncludeFields를 켠다.
builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.IncludeFields = true);
builder.Services.AddOpenApi();

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
