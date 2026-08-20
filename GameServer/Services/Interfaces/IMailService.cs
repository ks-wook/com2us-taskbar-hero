using GameServer.Services;

namespace GameServer.Services.Interfaces;

public interface IMailService
{
    Task<SaveResult> ListAsync(long userId);
    Task<SaveResult> ClaimAsync(long userId, long mailId);
    Task<SaveResult> ClaimAllAsync(long userId);
}
