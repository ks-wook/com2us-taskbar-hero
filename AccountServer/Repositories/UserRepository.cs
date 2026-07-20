using AccountServer.Data;
using SqlKata.Execution;

namespace AccountServer.Repositories;

/// <summary>로그인 검증에 필요한 최소 계정 정보(user_id + BCrypt 해시).</summary>
public sealed record UserCredential(long UserId, string PasswordHash);

public interface IUserRepository
{
    /// <summary>해당 이메일로 가입된 계정이 이미 있는지 확인.</summary>
    Task<bool> ExistsByEmailAsync(string email);

    /// <summary>계정 1건 저장 후 발급된 user_id 반환. 이메일 UNIQUE 위반 시 예외를 그대로 전파한다.</summary>
    Task<long> InsertUserAsync(string email, string passwordHash, string nickname, long nowUnix);

    /// <summary>이메일로 계정 조회(로그인용). 없으면 null.</summary>
    Task<UserCredential?> GetCredentialByEmailAsync(string email);
}

/// <summary>users 테이블 접근 계층. SqlKata 쿼리 빌더만 사용한다.</summary>
public sealed class UserRepository : IUserRepository
{
    private readonly AccountDbFactory _dbFactory;

    public UserRepository(AccountDbFactory dbFactory) => _dbFactory = dbFactory;

    public async Task<bool> ExistsByEmailAsync(string email)
    {
        using var db = _dbFactory.Create();
        var count = await db.Query("users").Where("email", email).CountAsync<int>();
        return count > 0;
    }

    public async Task<long> InsertUserAsync(string email, string passwordHash, string nickname, long nowUnix)
    {
        using var db = _dbFactory.Create();
        return await db.Query("users").InsertGetIdAsync<long>(new
        {
            email,
            password = passwordHash,
            nickname,
            created_at = nowUnix,
            updated_at = nowUnix,
        });
    }

    public async Task<UserCredential?> GetCredentialByEmailAsync(string email)
    {
        using var db = _dbFactory.Create();
        var row = await db.Query("users")
            .Select("user_id", "password")
            .Where("email", email)
            .FirstOrDefaultAsync();

        if (row is null)
        {
            return null;
        }

        return new UserCredential(Convert.ToInt64(row.user_id), (string)row.password);
    }
}
