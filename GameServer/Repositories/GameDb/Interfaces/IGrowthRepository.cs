using GameServer.Repositories.GameDb;

namespace GameServer.Repositories.GameDb.Interfaces;

public interface IGrowthRepository
{
    /// <summary>
    /// 스킬 레벨업을 한 트랜잭션으로 적용한다: 캐릭터 확인 → 대상 스킬 현재 레벨·사용 포인트 집계 →
    /// decide(마스터 검증: 직업·최대 레벨·포인트)로 판정 → player_skill 레벨 +1(없으면 INSERT).
    /// decide는 (classCode, charLevel, curLevel, spentPoints)→(status, availableAfter).
    /// </summary>
    Task<SkillLevelUpOutcome> ApplySkillLevelUpAsync(
        long userId, int characterId, int skillCode,
        Func<int, int, int, int, (SkillLevelUpStatus status, int availableAfter)> decide);

    /// <summary>
    /// 대상 캐릭터의 player_skill 행을 삭제하지 않고 level=0·equipped=0으로 되돌려 스킬을 초기화한다(무료).
    /// totalPoints=(charLevel)→레벨 비례 총량.
    /// </summary>
    Task<SkillResetOutcome> ApplySkillResetAsync(long userId, int characterId, Func<int, int> totalPoints);

    /// <summary>
    /// 액티브 스킬 장착 목록을 설정한다(통째 교체): 캐릭터·보유 스킬 조회 → validate(마스터 검증)로 판정 →
    /// player_skill.equipped를 요청 목록에 맞춰 갱신. validate는 (classCode, 보유 스킬 code→level)→status.
    /// </summary>
    Task<SkillEquipOutcome> ApplySkillEquipAsync(
        long userId, int characterId, IReadOnlyList<int> skillCodes,
        Func<int, IReadOnlyDictionary<int, int>, SkillEquipStatus> validate);

    /// <summary>
    /// 룬 업그레이드를 한 트랜잭션으로 적용한다: 현재 레벨·최대 레벨·선행 룬(prereqCode) 확인 →
    /// costOf(현재 레벨)로 골드 비용 산출 → 골드 확인·차감 → player_rune 레벨 +1(없으면 INSERT).
    /// 룬 존재 여부는 호출 전(서비스, 마스터)에서 검증한다.
    /// </summary>
    Task<RuneUpgradeOutcome> ApplyRuneUpgradeAsync(
        long userId, int runeCode, int prereqCode, int maxLevel, Func<int, long> costOf);
}
