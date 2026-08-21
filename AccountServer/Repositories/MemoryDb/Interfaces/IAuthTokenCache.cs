namespace AccountServer.Repositories.MemoryDb.Interfaces;

public interface IAuthTokenCache
{
    /// <summary>토큰을 TTL과 함께 기록한다(재로그인 시 덮어써 이전 세션을 밀어낸다).</summary>
    Task SetAsync(long userId, string token, TimeSpan ttl);

    /// <summary>현재 유효한 토큰 값을 읽는다. 키가 없으면 null(만료·로그아웃).</summary>
    Task<string?> GetAsync(long userId);

    /// <summary>토큰 키를 삭제해 세션을 무효화한다(로그아웃).</summary>
    Task DeleteAsync(long userId);
}
