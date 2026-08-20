using GameServer.Services;

namespace GameServer.Services.Interfaces;

public interface IGrowthService
{
    Task<SaveResult> SkillLevelUpAsync(long userId, int characterId, int skillCode);
    Task<SaveResult> SkillResetAsync(long userId, int characterId);
    Task<SaveResult> SkillEquipAsync(long userId, int characterId, IReadOnlyList<int> skillCodes);
    Task<SaveResult> RuneUpgradeAsync(long userId, int runeCode);
}
