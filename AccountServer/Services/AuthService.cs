using System.Net.Mail;
using AccountServer.Auth;
using AccountServer.Models;
using AccountServer.Repositories.AccountDb.Interfaces;
using AccountServer.Repositories.MemoryDb.Interfaces;
using AccountServer.Services.Interfaces;
using MySqlConnector;
using TaskbarHero.Common;
using TaskbarHero.Common.Dto;
using ZLogger;

namespace AccountServer.Services;

/// <summary>
/// 계정/인증 유스케이스(회원가입·로그인·로그아웃·자동 로그인 검증)를 처리하는 서비스.
/// 세션의 정본은 Redis 토큰이고 MySQL user_auth_token은 영속 백업이다(계정/로그인 기획서 4.2).
/// </summary>
public sealed class AuthService : IAuthService
{
    // 계정/로그인 기획서 5.1: 비밀번호 최소 6자.
    private const int MinPasswordLength = 6;
    private const int MaxNicknameLength = 50;

    // MySQL 중복 키(UNIQUE) 위반 에러 번호.
    private const int MySqlDuplicateEntry = 1062;

    private readonly IUserRepository _userRepository;
    private readonly IAuthTokenRepository _authTokenRepository;
    private readonly IAuthTokenCache _authTokenCache;
    private readonly TokenGenerator _tokenGenerator;
    private readonly int _tokenExpirationHours;
    private readonly ILogger<AuthService> _logger;

    /// <summary>의존성(사용자·토큰 리포지토리, 토큰 캐시, 토큰 생성기, 설정, 로거)을 주입받고 토큰 만료 시간을 읽는다.</summary>
    public AuthService(
        IUserRepository userRepository,
        IAuthTokenRepository authTokenRepository,
        IAuthTokenCache authTokenCache,
        TokenGenerator tokenGenerator,
        IConfiguration configuration,
        ILogger<AuthService> logger)
    {
        _userRepository = userRepository;
        _authTokenRepository = authTokenRepository;
        _authTokenCache = authTokenCache;
        _tokenGenerator = tokenGenerator;
        _tokenExpirationHours = configuration.GetValue("Security:TokenExpirationHours", 24);
        _logger = logger;
    }

    /// <summary>
    /// 회원가입을 처리한다. 입력(이메일 형식·비밀번호 최소 길이·닉네임)을 검증하고, 이메일 중복을 선검사한 뒤
    /// 비밀번호를 BCrypt로 해시해 사용자 행을 삽입한다. 동시 삽입 경합은 UNIQUE 위반을 잡아 DuplicateEmail로 변환한다.
    /// </summary>
    public async Task<SignupResult> SignupAsync(SignupRequest request)
    {
        // 1. 입력 검증 — 이메일 형식, 비밀번호 최소 6자, 닉네임 필수.
        if (!IsValidEmail(request.email)
            || string.IsNullOrEmpty(request.password) || request.password.Length < MinPasswordLength
            || string.IsNullOrWhiteSpace(request.nickname) || request.nickname.Length > MaxNicknameLength)
        {
            return new SignupResult(ErrorCode.InvalidRequest, 0);
        }

        var email = request.email!.Trim();
        var nickname = request.nickname!.Trim();

        // 2. 이메일 중복 선검사(빠른 실패). 최종 보장은 DB UNIQUE 인덱스가 한다.
        if (await _userRepository.ExistsByEmailAsync(email))
        {
            return new SignupResult(ErrorCode.DuplicateEmail, 0);
        }

        // 3. BCrypt 해시 후 저장.
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.password);
        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        try
        {
            var userId = await _userRepository.InsertUserAsync(email, passwordHash, nickname, nowUnix);
            _logger.ZLogInformation($"회원가입 성공: userId {userId:@UserId}");
            return new SignupResult(ErrorCode.Success, userId);
        }
        catch (MySqlException ex) when (ex.Number == MySqlDuplicateEntry)
        {
            // 선검사 통과 후 동시 요청이 먼저 삽입한 경합(race). UNIQUE 인덱스가 막아준다.
            _logger.ZLogWarning($"회원가입 이메일 중복 경합 감지: {email:@Email}");
            return new SignupResult(ErrorCode.DuplicateEmail, 0);
        }
    }

    /// <summary>
    /// 로그인을 처리한다. 입력 검증 → 이메일로 사용자 조회 → BCrypt 비밀번호 검증 후 인증 토큰을 발급하고,
    /// MySQL에 UPSERT(기존 세션 무효화)하고 Redis에 캐싱한다(단일 세션). 성공 시 user_id와 토큰을 반환한다.
    /// </summary>
    public async Task<LoginResult> LoginAsync(LoginRequest request)
    {
        // 1. 입력 검증.
        if (!IsValidEmail(request.email)
            || string.IsNullOrEmpty(request.password) || request.password.Length < MinPasswordLength)
        {
            return new LoginResult(ErrorCode.InvalidRequest, 0, string.Empty);
        }

        // 2. 사용자 조회.
        var user = await _userRepository.GetCredentialByEmailAsync(request.email!.Trim());
        if (user is null)
        {
            return new LoginResult(ErrorCode.UserNotFound, 0, string.Empty);
        }

        // 3. 비밀번호 검증(BCrypt).
        if (!BCrypt.Net.BCrypt.Verify(request.password, user.PasswordHash))
        {
            return new LoginResult(ErrorCode.InvalidPassword, 0, string.Empty);
        }

        // 4. 토큰 생성.
        var token = _tokenGenerator.GenerateToken(user.UserId);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var expiredAt = now + _tokenExpirationHours * 3600L;

        // 5. MySQL 저장(UPSERT → 기존 세션 무효화) + 6. Redis 캐싱(단일 세션 기준값 덮어쓰기).
        await _authTokenRepository.UpsertAsync(user.UserId, token, now, expiredAt);
        await _authTokenCache.SetAsync(user.UserId, token, TimeSpan.FromHours(_tokenExpirationHours));

        _logger.ZLogInformation($"로그인 성공: userId {user.UserId:@UserId}");
        return new LoginResult(ErrorCode.Success, user.UserId, token);
    }

    /// <summary>
    /// 로그아웃을 처리한다. Redis 캐시 토큰과 대조해(없으면 만료, 불일치면 무효 토큰) 유효할 때만
    /// MySQL 토큰 행과 Redis 키를 삭제해 세션을 무효화한다.
    /// </summary>
    public async Task<ErrorCode> LogoutAsync(long userId, string token)
    {
        var verified = await VerifyTokenAsync(userId, token);
        if (verified != ErrorCode.Success)
        {
            return verified;
        }

        // MySQL 행 삭제 + Redis 키 삭제.
        await _authTokenRepository.DeleteAsync(userId);
        await _authTokenCache.DeleteAsync(userId);

        _logger.ZLogInformation($"로그아웃 성공: userId {userId:@UserId}");
        return ErrorCode.Success;
    }

    /// <summary>
    /// 자동 로그인 검증을 처리한다. 클라이언트가 저장해 둔 직전 세션(userId·token)이 아직 유효한지
    /// Redis 토큰과 대조만 하고, 토큰을 재발급하거나 TTL을 연장하지 않는다(계정/로그인 기획서 5.4).
    /// 유효하면 클라이언트가 가진 토큰을 그대로 계속 쓰고, 아니면 타이틀 화면이 로그인 입력을 요구한다.
    /// 대조 자체는 로그아웃과 공유하는 <see cref="VerifyTokenAsync"/>가 맡고, 이 메서드는 그 앞뒤에
    /// **자동 로그인 유스케이스에만 필요한 처리**(진입 로그 등)를 얹는 자리다.
    /// </summary>
    public Task<ErrorCode> ValidateTokenAsync(long userId, string token)
    {
        // 앱 실행마다 호출되는 경로라 Debug로 남긴다(운영에서는 꺼지고, 접근 로그와도 중복되지 않는다).
        // 토큰 값은 남기지 않는다(로깅 규칙 §7).
        _logger.ZLogDebug($"자동 로그인 토큰 유효성 검증 요청: userId {userId:@UserId}");

        return VerifyTokenAsync(userId, token);
    }

    /// <summary>
    /// 요청의 userId·token이 현재 유효한 세션인지 Redis 토큰과 대조한다(단일 세션의 유효 기준).
    /// 값이 없으면 만료/폐기(ExpiredToken), 있으나 다르면 다른 기기의 로그인으로 밀려난 토큰(InvalidToken)이다.
    /// 로그아웃·자동 로그인 검증이 공유하는 판정이라 한 곳에 둔다.
    /// </summary>
    private async Task<ErrorCode> VerifyTokenAsync(long userId, string token)
    {
        if (userId <= 0 || string.IsNullOrEmpty(token))
        {
            return ErrorCode.InvalidRequest;
        }

        var cachedToken = await _authTokenCache.GetAsync(userId);
        if (cachedToken is null)
        {
            return ErrorCode.ExpiredToken;
        }

        if (!string.Equals(cachedToken, token, StringComparison.Ordinal))
        {
            return ErrorCode.InvalidToken;
        }

        return ErrorCode.Success;
    }

    /// <summary>이메일이 비어 있지 않고 파싱 가능한 형식인지 검사한다(MailAddress 기준).</summary>
    private static bool IsValidEmail(string? email)
        => !string.IsNullOrWhiteSpace(email) && MailAddress.TryCreate(email, out _);
}
