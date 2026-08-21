using AccountServer.Models;
using TaskbarHero.Common;
using TaskbarHero.Common.Dto;

namespace AccountServer.Services.Interfaces;

public interface IAuthService
{
    /// <summary>회원가입(입력 검증 → 이메일 중복 검사 → BCrypt 해시 저장). 성공 시 발급된 user_id를 함께 돌려준다.</summary>
    Task<SignupResult> SignupAsync(SignupRequest request);

    /// <summary>로그인(입력 검증 → 계정 조회 → 비밀번호 검증 → 토큰 발급·저장). 성공 시 user_id와 토큰을 돌려준다.</summary>
    Task<LoginResult> LoginAsync(LoginRequest request);

    /// <summary>로그아웃(토큰 대조 후 MySQL 행·Redis 키 삭제로 세션 무효화).</summary>
    Task<ErrorCode> LogoutAsync(long userId, string token);

    /// <summary>자동 로그인 검증(저장된 세션이 아직 유효한지 대조만 하고 재발급·TTL 연장은 하지 않는다).</summary>
    Task<ErrorCode> ValidateTokenAsync(long userId, string token);
}
