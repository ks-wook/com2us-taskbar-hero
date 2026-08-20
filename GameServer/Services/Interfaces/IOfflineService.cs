using GameServer.Services;

namespace GameServer.Services.Interfaces;

public interface IOfflineService
{
    Task<SaveResult> ClaimAsync(long userId);
}
