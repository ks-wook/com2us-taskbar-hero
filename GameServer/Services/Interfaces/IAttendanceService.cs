using GameServer.Services;

namespace GameServer.Services.Interfaces;

public interface IAttendanceService
{
    Task<SaveResult> StatusAsync(long userId);
    Task<SaveResult> ClaimAsync(long userId);
}
