using GameServer.Services;

namespace GameServer.Services.Interfaces;

public interface IGachaService
{
    Task<SaveResult> GetBannersAsync(long userId);
    Task<SaveResult> PullAsync(long userId, int gachaCode, int pullType);
    Task<SaveResult> GetHistoryAsync(long userId, int gachaCode, long cursor, int limit);
}
