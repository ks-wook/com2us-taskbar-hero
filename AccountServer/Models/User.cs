namespace AccountServer.Models;

/// <summary>
/// 로그인 검증에 필요한 최소 계정 정보(user_id + BCrypt 해시).
/// 리포지토리가 돌려주는 계층 간 전달 모델이며, 해시 비교는 서비스가 수행한다.
/// </summary>
public sealed record UserCredential(long UserId, string PasswordHash);

// ── UserRepository 전용 DB 행 매핑용 POCO(제네릭 매핑 전용, dynamic 금지).
//    Dapper.MatchNamesWithUnderscores=true(Program.cs)로 snake_case 컬럼 → PascalCase 프로퍼티 매핑. ──

/// <summary>users 조회 행 매핑용 POCO(user_id·password 컬럼).</summary>
class UserCredentialRow
{
    public long UserId { get; set; }
    public string Password { get; set; } = string.Empty;
}
