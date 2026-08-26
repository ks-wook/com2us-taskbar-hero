using GameServer.MasterData;
using GameServer.Repositories.GameDb;
using GameServer.Repositories.GameDb.Interfaces;
using TaskbarHero.Common;
using TaskbarHero.Common.Dto;
using ZLogger;
using GameServer.Repositories.MasterDb;
using GameServer.Models;
using GameServer.Services.Interfaces;
using GameServer.Logging;

namespace GameServer.Services;

/// <summary>
/// 성장(스킬·룬) 액션 처리(growth 기획서 §5). 스킬 포인트는 저장하지 않고 캐릭터 레벨(level_master.skill_points)에서
/// 파생하며, 모든 검증(직업 소속·최대 레벨·포인트·선행 룬·골드)은 마스터 데이터로 서버가 확정한다(서버 권위).
/// 상태 변경은 모두 리포지토리 트랜잭션으로 원자적으로 반영한다.
/// </summary>
public sealed class GrowthService : IGrowthService
{
    private readonly IGrowthRepository _growthRepository;
    private readonly MasterDbProvider _masterData;
    private readonly ILogger<GrowthService> _logger;
    private readonly IEventLogger _eventLogger;

    /// <summary>의존성(성장 리포지토리·마스터 데이터·운영 로거·이벤트 로거)을 주입받는다.</summary>
    public GrowthService(
        IGrowthRepository growthRepository, MasterDbProvider masterData,
        ILogger<GrowthService> logger, IEventLogger eventLogger)
    {
        _growthRepository = growthRepository;
        _masterData = masterData;
        _logger = logger;
        _eventLogger = eventLogger;
    }

    /// <summary>
    /// 스킬 레벨업을 처리한다. 마스터 로드를 확인하고, 리포지토리 트랜잭션 안에서 직업 소속·최대 레벨·사용 가능
    /// 스킬 포인트(레벨 파생 총량 − 사용량)를 검증한 뒤 스킬 레벨을 1 올린다. 결과 상태를 에러 코드로 매핑한다.
    /// </summary>
    public async Task<SaveResult> SkillLevelUpAsync(long userId, int characterId, int skillCode)
    {
        try
        {
            if (!_masterData.IsLoaded)
            {
                return new SaveResult(ErrorCode.MasterDataNotLoaded, string.Empty, null);
            }

            // 마스터 검증(직업 소속·최대 레벨·포인트)을 트랜잭션 내부 DB 값(직업·레벨·현재/사용 포인트)으로 판정하는 델리게이트.
            var outcome = await _growthRepository.ApplySkillLevelUpAsync(userId, characterId, skillCode,
                (classCode, charLevel, curLevel, spent) =>
                {
                    var skill = _masterData.GetSkill(skillCode);
                    if (skill is null)
                    {
                        return (SkillLevelUpStatus.SkillNotFound, 0);
                    }

                    if (skill.ClassCode != classCode)
                    {
                        return (SkillLevelUpStatus.ClassMismatch, 0);
                    }

                    if (curLevel >= skill.MaxLevel)
                    {
                        return (SkillLevelUpStatus.MaxLevel, 0);
                    }

                    var available = _masterData.SkillPointsForLevel(charLevel) - spent;
                    if (available < Constants.Skill.PointPerLevel)
                    {
                        return (SkillLevelUpStatus.InsufficientPoint, 0);
                    }

                    return (SkillLevelUpStatus.Ok, available - Constants.Skill.PointPerLevel);
                });

            switch (outcome.Status)
            {
                case SkillLevelUpStatus.InvalidCharacter:
                    return new SaveResult(ErrorCode.InvalidCharacterId, string.Empty, null);
                case SkillLevelUpStatus.SkillNotFound:
                    return new SaveResult(ErrorCode.InvalidGrowthTarget, string.Empty, null);
                case SkillLevelUpStatus.ClassMismatch:
                    return new SaveResult(ErrorCode.SkillClassMismatch, string.Empty, null);
                case SkillLevelUpStatus.MaxLevel:
                    return new SaveResult(ErrorCode.SkillMaxLevel, string.Empty, null);
                case SkillLevelUpStatus.InsufficientPoint:
                    return new SaveResult(ErrorCode.InsufficientSkillPoint, string.Empty, null);
            }

            var data = new SkillLevelUpResultData
            {
                characterId = characterId,
                skillCode = skillCode,
                level = outcome.NewLevel,
                cost = new SkillPointCostDto { skillPoint = Constants.Skill.PointPerLevel },
                skillPoint = outcome.AvailablePoints,
            };

            _logger.ZLogInformation($"스킬 레벨업: userId {userId:@UserId}, characterId {characterId:@CharacterId}, skillCode {skillCode:@SkillCode}, level {outcome.NewLevel:@Level}");
            return new SaveResult(ErrorCode.Success, "Skill leveled up", data);
        }
        catch (Exception ex)
        {
            _logger.ZLogError(
                ex, $"SkillLevelUpAsync 처리 중 예외: errorCode {(int)ErrorCode.ServerError:@ErrorCode}({ErrorCode.ServerError:@ErrorName}), userId {userId:@UserId}");
            return new SaveResult(ErrorCode.ServerError, string.Empty, null);
        }
    }

    /// <summary>
    /// 스킬 초기화를 처리한다. 마스터 로드를 확인하고, 리포지토리 트랜잭션으로 대상 캐릭터의 스킬 행을 삭제하지 않고
    /// 레벨 0·미장착으로 되돌려 포인트를 전량 회수한다(무료). 초기화 후 사용 가능 포인트(레벨 비례 총량 전액)를 함께 반환한다.
    /// </summary>
    public async Task<SaveResult> SkillResetAsync(long userId, int characterId)
    {
        try
        {
            if (!_masterData.IsLoaded)
            {
                return new SaveResult(ErrorCode.MasterDataNotLoaded, string.Empty, null);
            }

            var outcome = await _growthRepository.ApplySkillResetAsync(userId, characterId,
                charLevel => _masterData.SkillPointsForLevel(charLevel));

            if (outcome.Status == SkillResetStatus.InvalidCharacter)
            {
                return new SaveResult(ErrorCode.InvalidCharacterId, string.Empty, null);
            }

            var data = new SkillResetResultData
            {
                characterId = characterId,
                resetSkillCount = outcome.ResetCount,
                skillPoint = outcome.AvailablePoints,
            };

            _logger.ZLogInformation($"스킬 초기화: userId {userId:@UserId}, characterId {characterId:@CharacterId}, resetCount {outcome.ResetCount:@ResetCount}");
            return new SaveResult(ErrorCode.Success, "Skills reset", data);
        }
        catch (Exception ex)
        {
            _logger.ZLogError(
                ex, $"SkillResetAsync 처리 중 예외: errorCode {(int)ErrorCode.ServerError:@ErrorCode}({ErrorCode.ServerError:@ErrorName}), userId {userId:@UserId}");
            return new SaveResult(ErrorCode.ServerError, string.Empty, null);
        }
    }

    /// <summary>
    /// 액티브 스킬 장착을 처리한다. 마스터 로드를 확인하고, 리포지토리 트랜잭션 안에서 요청 목록(0~2개)의 각 스킬이
    /// 대상 캐릭터 직업의 액티브 스킬이며 습득(레벨 ≥ 1)되었는지 검증한 뒤 장착 목록을 통째로 교체한다.
    /// </summary>
    public async Task<SaveResult> SkillEquipAsync(long userId, int characterId, IReadOnlyList<int> skillCodes)
    {
        try
        {
            if (!_masterData.IsLoaded)
            {
                return new SaveResult(ErrorCode.MasterDataNotLoaded, string.Empty, null);
            }

            var codes = skillCodes ?? new List<int>();

            var outcome = await _growthRepository.ApplySkillEquipAsync(userId, characterId, codes,
                (classCode, levels) =>
                {
                    if (codes.Count > Constants.Skill.MaxActiveEquipped)
                    {
                        return SkillEquipStatus.LimitExceeded;
                    }

                    foreach (var code in codes)
                    {
                        var skill = _masterData.GetSkill(code);
                        if (skill is null)
                        {
                            return SkillEquipStatus.SkillNotFound;
                        }

                        if (skill.ClassCode != classCode)
                        {
                            return SkillEquipStatus.ClassMismatch;
                        }

                        if (skill.SkillType != Constants.SkillType.Active)
                        {
                            return SkillEquipStatus.NotActive;
                        }

                        if (levels.GetValueOrDefault(code, 0) < 1)
                        {
                            return SkillEquipStatus.NotLearned;
                        }
                    }

                    return SkillEquipStatus.Ok;
                });

            switch (outcome.Status)
            {
                case SkillEquipStatus.InvalidCharacter:
                    return new SaveResult(ErrorCode.InvalidCharacterId, string.Empty, null);
                case SkillEquipStatus.SkillNotFound:
                    return new SaveResult(ErrorCode.InvalidGrowthTarget, string.Empty, null);
                case SkillEquipStatus.ClassMismatch:
                    return new SaveResult(ErrorCode.SkillClassMismatch, string.Empty, null);
                case SkillEquipStatus.NotActive:
                    return new SaveResult(ErrorCode.SkillNotActive, string.Empty, null);
                case SkillEquipStatus.NotLearned:
                    return new SaveResult(ErrorCode.SkillNotLearned, string.Empty, null);
                case SkillEquipStatus.LimitExceeded:
                    return new SaveResult(ErrorCode.ActiveSkillLimitExceeded, string.Empty, null);
            }

            var data = new SkillEquipResultData
            {
                characterId = characterId,
                equipped = outcome.Equipped,
            };

            _logger.ZLogInformation($"액티브 스킬 장착: userId {userId:@UserId}, characterId {characterId:@CharacterId}, count {outcome.Equipped.Count:@Count}");
            return new SaveResult(ErrorCode.Success, "Active skills equipped", data);
        }
        catch (Exception ex)
        {
            _logger.ZLogError(
                ex, $"SkillEquipAsync 처리 중 예외: errorCode {(int)ErrorCode.ServerError:@ErrorCode}({ErrorCode.ServerError:@ErrorName}), userId {userId:@UserId}");
            return new SaveResult(ErrorCode.ServerError, string.Empty, null);
        }
    }

    /// <summary>
    /// 룬 업그레이드를 처리한다. 마스터 로드·룬 존재를 확인하고, 리포지토리 트랜잭션 안에서 최대 레벨·선행 룬 해금을
    /// 검증한 뒤 현재 레벨 비례 골드 비용을 차감하고 룬 레벨을 1 올린다(계정 공용). 결과 상태를 에러 코드로 매핑한다.
    /// </summary>
    public async Task<SaveResult> RuneUpgradeAsync(long userId, int runeCode)
    {
        try
        {
            if (!_masterData.IsLoaded)
            {
                return new SaveResult(ErrorCode.MasterDataNotLoaded, string.Empty, null);
            }

            var rune = _masterData.GetRune(runeCode);
            if (rune is null)
            {
                return new SaveResult(ErrorCode.InvalidGrowthTarget, string.Empty, null);
            }

            var outcome = await _growthRepository.ApplyRuneUpgradeAsync(
                userId, runeCode, rune.PrereqCode, rune.MaxLevel,
                currentLevel => _masterData.RuneUpgradeCost(rune, currentLevel));

            switch (outcome.Status)
            {
                case RuneUpgradeStatus.PrereqNotMet:
                    return new SaveResult(ErrorCode.RunePrereqNotMet, string.Empty, null);
                case RuneUpgradeStatus.MaxLevel:
                    return new SaveResult(ErrorCode.RuneMaxLevel, string.Empty, null);
                case RuneUpgradeStatus.InsufficientCurrency:
                    return new SaveResult(ErrorCode.InsufficientCurrency, string.Empty, null);
            }

            var data = new RuneUpgradeResultData
            {
                runeCode = runeCode,
                level = outcome.NewLevel,
                cost = new CurrencyDto { currencyType = Constants.Currency.GoldType, amount = outcome.Cost },
                balance = new List<CurrencyDto>
                {
                    new CurrencyDto { currencyType = Constants.Currency.GoldType, amount = outcome.GoldBalance },
                },
            };

            _logger.ZLogInformation($"룬 업그레이드: userId {userId:@UserId}, runeCode {runeCode:@RuneCode}, level {outcome.NewLevel:@Level}, cost {outcome.Cost:@Cost}");

            // 재화 원장(6.1). 룬은 도메인 로그 테이블이 없어 이 행이 유일한 기록이다 —
            // 룬별 투자 편중은 ref_id로, 도달 레벨은 룬별 행 수의 누적으로 나온다.
            if (outcome.Cost > 0)
            {
                _eventLogger.CurrencySpent(
                    userId, outcome.Cost, outcome.GoldBalance, CurrencySource.RuneUpgrade, runeCode);
            }

            return new SaveResult(ErrorCode.Success, "Rune upgraded", data);
        }
        catch (Exception ex)
        {
            _logger.ZLogError(
                ex, $"RuneUpgradeAsync 처리 중 예외: errorCode {(int)ErrorCode.ServerError:@ErrorCode}({ErrorCode.ServerError:@ErrorName}), userId {userId:@UserId}");
            return new SaveResult(ErrorCode.ServerError, string.Empty, null);
        }
    }
}
