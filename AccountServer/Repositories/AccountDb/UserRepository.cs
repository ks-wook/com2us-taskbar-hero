using AccountServer.Models;
using AccountServer.Repositories.AccountDb.Interfaces;
using SqlKata.Execution;

namespace AccountServer.Repositories.AccountDb;

/// <summary>
/// users 테이블 접근 계층(taskbar_hero_account). SqlKata 쿼리 빌더 + 제네릭 매핑만 사용한다(dynamic 금지).
/// </summary>
public sealed class UserRepository : AccountDbBase, IUserRepository
{
    /// <summary>계정 DB 커넥션 팩토리를 기반 클래스로 전달한다.</summary>
    public UserRepository(AccountDbFactory dbFactory) : base(dbFactory) { }

    /// <summary>
    /// users에서 해당 이메일의 행 수를 세어 가입 여부를 판정한다(회원가입 사전 검사).
    /// 읽기 전용 단건이므로 트랜잭션을 쓰지 않는다. 이 검사와 INSERT 사이의 경합은
    /// email UNIQUE 제약이 최종적으로 막는다.
    /// </summary>
    public async Task<bool> ExistsByEmailAsync(string email)
    {
        using var db = Db();
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
        using var db = Db();
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
        using var db = Db();
        var row = await db.Query("users")
            .Select("user_id", "password")
            .Where("email", email)
            .FirstOrDefaultAsync<UserCredentialRow>();

        return row is null ? null : new UserCredential(row.UserId, row.Password);
    }
}
