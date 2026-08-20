using GameServer.Services;

namespace GameServer.Services.Interfaces;

public interface IConsumableService
{
    Task<SaveResult> UseAsync(long userId, long itemId);
    Task<SaveResult> GetActiveBuffsAsync(long userId);
}
