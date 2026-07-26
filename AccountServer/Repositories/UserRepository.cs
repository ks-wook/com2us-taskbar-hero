using AccountServer.Data;
using SqlKata.Execution;

namespace AccountServer.Repositories;

/// <summary>로그인 검증에 필요한 최소 계정 정보(user_id + BCrypt 해시).</summary>
public sealed record UserCredential(long UserId, string PasswordHash);

/// <summary>users 조회 행 매핑용 POCO(제네릭 매핑 전용, dynamic 금지). snake_case→PascalCase는 Dapper 규칙으로 매핑.</summary>
file sealed class UserCredentialRow
{
    public long UserId { get; set; }
    public string Password { get; set; } = string.Empty;
}

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

    /// <summary>계정 DB 커넥션 팩토리를 주입받는다.</summary>
    public UserRepository(AccountDbFactory dbFactory) => _dbFactory = dbFactory;

    /// <summary>
    /// users에서 해당 이메일의 행 수를 세어 가입 여부를 판정한다(회원가입 사전 검사).
    /// 읽기 전용 단건이므로 트랜잭션을 쓰지 않는다. 이 검사와 INSERT 사이의 경합은
    /// email UNIQUE 제약이 최종적으로 막는다.
    /// </summary>
    public async Task<bool> ExistsByEmailAsync(string email)
    {
        using var db = _dbFactory.Create();
        var count = await db.Query("users").Where("email", email).CountAsync<int>();
        return count > 0;
    }

    /// <summary>
    /// users에 계정 1건(이메일·BCrypt 해시·닉네임·생성/갱신 시각)을 INSERT하고 발급된 user_id를 반환한다.
    /// 쓰기가 한 문장이라 트랜잭션을 열지 않으며, 이메일 UNIQUE 위반 예외는 호출측(서비스)이 중복 가입으로
    /// 판정하도록 그대로 전파한다.
    /// </summary>
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

    /// <summary>
    /// 로그인 검증용으로 이메일에 해당하는 user_id와 저장된 비밀번호 해시만 조회한다(해시 비교는 서비스가 수행).
    /// 읽기 전용 단건이므로 트랜잭션을 쓰지 않으며, 계정이 없으면 null.
    /// </summary>
    public async Task<UserCredential?> GetCredentialByEmailAsync(string email)
    {
        using var db = _dbFactory.Create();
        var row = await db.Query("users")
            .Select("user_id", "password")
            .Where("email", email)
            .FirstOrDefaultAsync<UserCredentialRow>();

        if (row is null)
        {
            return null;
        }

        return new UserCredential(row.UserId, row.Password);
    }
}
