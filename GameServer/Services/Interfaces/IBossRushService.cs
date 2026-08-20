using GameServer.Services;
using TaskbarHero.Common.Dto;

namespace GameServer.Services.Interfaces;

public interface IBossRushService
{
    Task<SaveResult> GetInfoAsync(long userId);
    Task<SaveResult> EnterAsync(long userId);
    Task<SaveResult> ClearAsync(long userId, BossRushClearData request);
    Task<SaveResult> GetRankAsync(long userId, int seasonId, int offset, int limit);
    Task<SaveResult> GetMyRankAsync(long userId, int seasonId);
}
