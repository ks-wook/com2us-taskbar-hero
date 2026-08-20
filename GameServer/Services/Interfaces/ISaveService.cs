using GameServer.Services;
using TaskbarHero.Common.Dto;

namespace GameServer.Services.Interfaces;

public interface ISaveService
{
    Task<SaveResult> LoadAsync(long userId);
    Task<SaveResult> CreateCharacterAsync(long userId, string? nickname, int classCode, int gender);
    Task<SaveResult> ArrangePartyAsync(long userId, IReadOnlyList<PartyMemberDto>? members);
    Task<SaveResult> UpdateLastActiveAsync(long userId);
}
