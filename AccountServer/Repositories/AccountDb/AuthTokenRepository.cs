using AccountServer.Repositories.AccountDb.Interfaces;
using SqlKata.Execution;

namespace AccountServer.Repositories.AccountDb;

/// <summary>
/// user_auth_token 테이블(영속 백업) 접근 계층. SqlKata 쿼리 빌더만 사용.
/// user_id가 PK라 UPSERT는 update→0행이면 insert 순서로 처리한다(원시 SQL 미사용).
/// </summary>
public sealed class AuthTokenRepository : AccountDbBase, IAuthTokenRepository
{
    /// <summary>계정 DB 커넥션 팩토리를 기반 클래스로 전달한다.</summary>
    public AuthTokenRepository(AccountDbFactory dbFactory) : base(dbFactory) { }

    /// <summary>
    /// user_auth_token에 토큰 1행을 UPSERT한다(user_id가 PK이므로 계정당 1행 = 단일 세션).
    /// 원시 SQL을 쓰지 않으려고 UPDATE를 먼저 시도하고 0행일 때만 INSERT하는 순서로 처리한다.
    /// ⚠️ 두 문장을 트랜잭션으로 묶지 않으므로 동시 로그인 시 두 INSERT가 겹치면 PK 중복 예외가 날 수 있다
    ///    (정본은 Redis이고 이 테이블은 영속 백업이라 현재는 허용 범위).
    /// </summary>
    public async Task UpsertAsync(long userId, string token, long createdAt, long expiredAt)
    {
        using var db = Db();

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

    /// <summary>
    /// 로그아웃 시 해당 계정의 토큰 행을 삭제한다(단일 DELETE라 트랜잭션 불필요).
    /// 행이 없어도 예외 없이 통과한다(멱등).
    /// </summary>
    public async Task DeleteAsync(long userId)
    {
        using var db = Db();
        await db.Query("user_auth_token").Where("user_id", userId).DeleteAsync();
    }
}
