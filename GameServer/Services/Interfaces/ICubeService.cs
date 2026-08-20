using GameServer.Services;
using TaskbarHero.Common.Dto;

namespace GameServer.Services.Interfaces;

public interface ICubeService
{
    Task<SaveResult> CombineAsync(long userId, IReadOnlyList<long> itemIds);
    Task<SaveResult> DismantleAsync(long userId, IReadOnlyList<CubeDismantleItemDto> items);
    Task<SaveResult> CraftAsync(long userId, int recipeCode);
}
