using AccountServer.Data;
using SqlKata.Execution;

namespace AccountServer.Repositories;

public interface IAuthTokenRepository
{
    /// <summary>user_auth_token UPSERT(사용자당 1행 → 단일 세션). 기존 행이 있으면 덮어쓴다.</summary>
    Task UpsertAsync(long userId, string token, long createdAt, long expiredAt);

    /// <summary>토큰 행 삭제(로그아웃).</summary>
    Task DeleteAsync(long userId);
}

/// <summary>
/// user_auth_token 테이블(영속 백업) 접근 계층. SqlKata 쿼리 빌더만 사용.
/// user_id가 PK라 UPSERT는 update→0행이면 insert 순서로 처리한다(원시 SQL 미사용).
/// </summary>
public sealed class AuthTokenRepository : IAuthTokenRepository
{
    private readonly AccountDbFactory _dbFactory;

    public AuthTokenRepository(AccountDbFactory dbFactory) => _dbFactory = dbFactory;

    public async Task UpsertAsync(long userId, string token, long createdAt, long expiredAt)
    {
        using var db = _dbFactory.Create();

        var affected = await db.Query("user_auth_token")
            .Where("user_id", userId)
            .UpdateAsync(new { token, created_at = createdAt, expired_at = expiredAt });

        if (affected == 0)
        {
            await db.Query("user_auth_token").InsertAsync(new
            {
                user_id = userId,
                token,
                created_at = createdAt,
                expired_at = expiredAt,
            });
        }
    }

    public async Task DeleteAsync(long userId)
    {
        using var db = _dbFactory.Create();
        await db.Query("user_auth_token").Where("user_id", userId).DeleteAsync();
    }
}
