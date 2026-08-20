using GameServer.Services;

namespace GameServer.Services.Interfaces;

public interface IInventoryService
{
    Task<SaveResult> GetPageAsync(long userId, int cursor, int limit);
    Task<SaveResult> EquipAsync(long userId, int characterId, long itemId);
    Task<SaveResult> UnequipAsync(long userId, int characterId, int slot);
    Task<SaveResult> MoveAsync(long userId, long itemId, int toSlot);
    Task<SaveResult> EnhanceAsync(long userId, long itemId);
    Task<SaveResult> ExpandAsync(long userId);
}
