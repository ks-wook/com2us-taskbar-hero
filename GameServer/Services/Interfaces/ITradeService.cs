using GameServer.Services;

namespace GameServer.Services.Interfaces;

public interface ITradeService
{
    Task<SaveResult> ListAsync(long userId, int itemCode, bool mine, int page, int pageSize);
    Task<SaveResult> RegisterAsync(long userId, long itemId, long price);
    Task<SaveResult> BuyAsync(long userId, long listingId);
    Task<SaveResult> CancelAsync(long userId, long listingId);
}
