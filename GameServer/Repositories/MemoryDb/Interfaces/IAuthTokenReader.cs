namespace GameServer.Repositories.MemoryDb.Interfaces;

public interface IAuthTokenReader
{
    /// <summary>Redis auth:token:{userId} 값 조회. 없으면 null(만료/폐기).</summary>
    Task<string?> GetAsync(long userId);
}
