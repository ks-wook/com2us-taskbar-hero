using GameServer.Models;
using TaskbarHero.Common.MasterData;

namespace GameServer.Repositories.MasterDb;

/// <summary>
/// 마스터 적재 결과 한 벌(<see cref="Interfaces.IMasterDbLoader"/> → <see cref="MasterDbProvider"/>).
/// 적재 계층과 조회 계층 사이의 내부 계약이며, 파생값(직업별 기본 무기·기본 스킬, 등급별 드롭 풀)도
/// <b>적재 시점에 한 번</b> 계산해 담는다 — 조회 때마다 다시 만들 이유가 없다.
/// </summary>
public sealed record MasterDbSnapshot
{
    /// <summary>class_master</summary>
    public required IReadOnlyDictionary<int, ClassMaster> Classes { get; init; }

    /// <summary>stage_master + stage_spawn</summary>
    public required IReadOnlyDictionary<int, StageDef> StagesById { get; init; }

    /// <summary>stage_reward + stage_reward_drop</summary>
    public required IReadOnlyDictionary<int, StageRewardDef> RewardsByStageId { get; init; }

    /// <summary>level_master.required_exp</summary>
    public required IReadOnlyDictionary<int, long> LevelRequiredExp { get; init; }

    /// <summary>level_master 의 최대 레벨</summary>
    public required int MaxLevel { get; init; }

    /// <summary>level_master.skill_points</summary>
    public required IReadOnlyDictionary<int, int> LevelSkillPoints { get; init; }

    /// <summary>등급별 드롭 후보 풀(item_master 파생)</summary>
    public required IReadOnlyDictionary<int, List<int>> ItemsByGrade { get; init; }

    /// <summary>item_master</summary>
    public required IReadOnlyDictionary<int, ItemDef> ItemsByCode { get; init; }

    /// <summary>직업별 기본 무기(item_master 파생)</summary>
    public required IReadOnlyDictionary<int, ItemDef> StartingWeaponByClass { get; init; }

    /// <summary>consumable_master</summary>
    public required IReadOnlyDictionary<int, ConsumableDef> ConsumablesByCode { get; init; }

    /// <summary>enhance_master</summary>
    public required IReadOnlyDictionary<int, EnhanceRule> EnhanceByLevel { get; init; }

    /// <summary>skill_master</summary>
    public required IReadOnlyDictionary<int, SkillDef> SkillsByCode { get; init; }

    /// <summary>직업별 기본 스킬(skill_master 파생)</summary>
    public required IReadOnlyDictionary<int, SkillDef> StartingSkillByClass { get; init; }

    /// <summary>rune_master</summary>
    public required IReadOnlyDictionary<int, RuneDef> RunesByCode { get; init; }

    /// <summary>rune_cost</summary>
    public required IReadOnlyDictionary<(int rune, int level), long> RuneCosts { get; init; }

    /// <summary>character_create_cost</summary>
    public required IReadOnlyDictionary<int, long> CharacterCreateCosts { get; init; }

    /// <summary>cube_master</summary>
    public required IReadOnlyDictionary<int, CubeRule> CubeRules { get; init; }

    /// <summary>cube_recipe + cube_recipe_ingredient</summary>
    public required IReadOnlyDictionary<int, RecipeDef> RecipesByCode { get; init; }

    /// <summary>attendance_master</summary>
    public required IReadOnlyDictionary<int, AttendanceRewardDef> AttendanceByDay { get; init; }

    /// <summary>mail_master</summary>
    public required IReadOnlyDictionary<int, MailTemplateDef> MailTemplates { get; init; }

    /// <summary>newbie_reward_master</summary>
    public required IReadOnlyList<NewbieRewardDef> NewbieRewards { get; init; }

    /// <summary>gacha_master + 자식 3종</summary>
    public required IReadOnlyDictionary<int, GachaBannerDef> GachaByCode { get; init; }

    /// <summary>boss_rush_master(콘텐츠 1행)</summary>
    public required BossRushRuleDef? BossRushRule { get; init; }

    /// <summary>boss_rush_round + boss_rush_spawn</summary>
    public required IReadOnlyDictionary<int, BossRushRoundDef> BossRushRounds { get; init; }

    /// <summary>boss_rush_rank_reward</summary>
    public required IReadOnlyList<BossRushRankRewardDef> BossRushRankRewards { get; init; }

    /// <summary>inventory_expand_master</summary>
    public required IReadOnlyList<long> ExpandCosts { get; init; }

}
