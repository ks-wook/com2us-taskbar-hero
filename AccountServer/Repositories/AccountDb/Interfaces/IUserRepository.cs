using AccountServer.Models;

namespace AccountServer.Repositories.AccountDb.Interfaces;

public interface IUserRepository
{
    /// <summary>해당 이메일로 가입된 계정이 이미 있는지 확인.</summary>
    Task<bool> ExistsByEmailAsync(string email);

    /// <summary>계정 1건 저장 후 발급된 user_id 반환. 이메일 UNIQUE 위반 시 예외를 그대로 전파한다.</summary>
    Task<long> InsertUserAsync(string email, string passwordHash, string nickname, long nowUnix);

    /// <summary>이메일로 계정 조회(로그인용). 없으면 null.</summary>
    Task<UserCredential?> GetCredentialByEmailAsync(string email);
}
