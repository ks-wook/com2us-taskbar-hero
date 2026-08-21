namespace AccountServer.Repositories.AccountDb.Interfaces;

public interface IAuthTokenRepository
{
    /// <summary>user_auth_token UPSERT(사용자당 1행 → 단일 세션). 기존 행이 있으면 덮어쓴다.</summary>
    Task UpsertAsync(long userId, string token, long createdAt, long expiredAt);

    /// <summary>토큰 행 삭제(로그아웃).</summary>
    Task DeleteAsync(long userId);
}
