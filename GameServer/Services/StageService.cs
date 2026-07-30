using GameServer.MasterData;
using GameServer.Repositories;
using TaskbarHero.Common;
using TaskbarHero.Common.Dto;
using ZLogger;

namespace GameServer.Services;

public interface IStageService
{
    Task<SaveResult> EnterAsync(long userId, int act, int difficulty, int stage);
    Task<SaveResult> ClearAsync(long userId, int act, int difficulty, int stage);
}

/// <summary>
/// 스테이지 진입·클리어 처리(stage-battle 기획서 §5·§6).
/// 진입: 도달 가능 여부 검증 후 현재 진입 스테이지 설정(보상 없음).
/// 클리어: 서버 권위로 보상(골드·경험치·드롭) 산출 후 트랜잭션으로 지급·진행도 갱신.
/// </summary>
public sealed class StageService : IStageService
{
    private const int GoldCurrencyType = 1;

    private readonly IStageRepository _stageRepository;
    private readonly MasterDataProvider _masterData;
    private readonly ILogger<StageService> _logger;

    /// <summary>의존성(스테이지 리포지토리·마스터 데이터·로거)을 주입받는다.</summary>
    public StageService(IStageRepository stageRepository, MasterDataProvider masterData, ILogger<StageService> logger)
    {
        _stageRepository = stageRepository;
        _masterData = masterData;
        _logger = logger;
    }

    /// <summary>
    /// 스테이지 진입을 처리한다. 마스터 로드·스테이지 존재·세이브 존재를 확인하고,
    /// 대상이 도달 가능한 범위(이미 클리어했거나 프런티어+1)인지 검증한 뒤 현재 진입 스테이지로 설정한다.
    /// 성공 시 스폰·보스·배경 정보를 담은 진입 응답을 반환한다(보상 없음).
    /// </summary>
    public async Task<SaveResult> EnterAsync(long userId, int act, int difficulty, int stage)
    {
        if (!_masterData.IsLoaded)
        {
            return new SaveResult(ErrorCode.MasterDataNotLoaded, string.Empty, null);
        }

        var stageDef = _masterData.GetStage(act, difficulty, stage);
        if (stageDef is null)
        {
            return new SaveResult(ErrorCode.StageNotFound, string.Empty, null);
        }

        var progress = await _stageRepository.GetProgressAsync(userId);
        if (progress is null)
        {
            // 세이브(game_player) 미생성 — 캐릭터 생성 전.
            return new SaveResult(ErrorCode.SaveNotFound, string.Empty, null);
        }

        // 도달 검증: 이미 클리어한 스테이지 재파밍이거나 프런티어(+1)만 허용.
        var seq = StageCoords.Sequence(act, difficulty, stage);
        if (seq > progress.MaxStageCleared + 1)
        {
            return new SaveResult(ErrorCode.StageLocked, string.Empty, null);
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await _stageRepository.SetCurrentStageAsync(userId, act, difficulty, stage, now);

        var data = new StageEnterData
        {
            act = act,
            difficulty = difficulty,
            stage = stage,
            stageId = stageDef.StageId,
            monsters = new List<StageSpawnDto>(stageDef.Spawns),
            boss = stageDef.BossMonsterCode == 0 ? null : new StageBossDto { monsterCode = stageDef.BossMonsterCode },
            backgroundType = stageDef.BackgroundType,
            enteredAt = now,
        };

        return new SaveResult(ErrorCode.Success, "Stage entered", data);
    }

    /// <summary>
    /// 스테이지 클리어를 처리한다. 마스터 로드·스테이지·보상 정의를 확인하고, 서버 권위로 보상을
    /// 산출한다(골드·경험치는 고정, 드롭은 등급 확률로 추첨). 이어서 리포지토리 트랜잭션으로
    /// 진입 스테이지 재검증 → 골드·경험치 지급 → 전리품 적재 → 진행도 갱신을 원자적으로 수행하고,
    /// 결과 상태를 에러 코드로 매핑해(미진입·용량초과 등) 클리어 응답을 반환한다.
    /// </summary>
    public async Task<SaveResult> ClearAsync(long userId, int act, int difficulty, int stage)
    {
        if (!_masterData.IsLoaded)
        {
            return new SaveResult(ErrorCode.MasterDataNotLoaded, string.Empty, null);
        }

        var stageDef = _masterData.GetStage(act, difficulty, stage);
        if (stageDef is null)
        {
            return new SaveResult(ErrorCode.StageNotFound, string.Empty, null);
        }

        var reward = _masterData.GetStageReward(stageDef.StageId);
        if (reward is null)
        {
            _logger.ZLogError($"stage_reward 누락: stageId {stageDef.StageId:@StageId}");
            return new SaveResult(ErrorCode.MasterDataNotLoaded, string.Empty, null);
        }

        // 보상 산출(서버 권위): 골드·경험치는 마스터 고정값이 기본이고, 드롭은 등급 확률로 추첨한다.
        // 활성 획득량 버프(경험치·골드 부스터) 배율은 지급 트랜잭션 안에서 곱한다(소모품/버프 기획서 6.2).
        var dropped = _masterData.RollDrop(reward);

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var outcome = await _stageRepository.ApplyClearAsync(
            userId, act, difficulty, stage, reward.Gold, reward.Exp, dropped,
            ApplyExp,
            now);

        switch (outcome.Status)
        {
            case ClearStatus.NoPlayer:
                return new SaveResult(ErrorCode.SaveNotFound, string.Empty, null);
            case ClearStatus.NotEntered:
                return new SaveResult(ErrorCode.StageNotEntered, string.Empty, null);
            case ClearStatus.InventoryFull:
                return new SaveResult(ErrorCode.InventoryFull, string.Empty, null);
        }

        var items = new List<RewardItemDto>();
        if (dropped is not null)
        {
            items.Add(new RewardItemDto { itemCode = dropped.ItemCode, quantity = dropped.Quantity });
        }

        var data = new StageClearData
        {
            cleared = new ClearedStageDto { act = act, difficulty = difficulty, stage = stage },
            // 버프 배율이 적용된 최종 지급액을 응답에 담는다(클라이언트가 표시하는 값 = 실제 반영된 값).
            rewards = new StageRewardsDto { gold = outcome.GrantedGold, exp = outcome.GrantedExp, items = items },
            characters = outcome.Characters,
            balance = new List<CurrencyDto>
            {
                new CurrencyDto { currencyType = GoldCurrencyType, amount = outcome.GoldBalance },
            },
            progress = new StageProgressDto
            {
                act = outcome.Act,
                difficulty = outcome.Difficulty,
                stage = outcome.Stage,
                maxStageCleared = outcome.MaxStageCleared,
            },
        };

        _logger.ZLogInformation($"스테이지 클리어: userId {userId:@UserId}, act {act:@Act}, difficulty {difficulty:@Difficulty}, stage {stage:@Stage}, gold {outcome.GrantedGold:@Gold}, exp {outcome.GrantedExp:@Exp}, goldMul {outcome.GoldMultiplier:@GoldMultiplier}, expMul {outcome.ExpMultiplier:@ExpMultiplier}");
        return new SaveResult(ErrorCode.Success, "Stage cleared", data);
    }

    /// <summary>레벨당 요구 경험치(level_master)로 경험치 지급 후 레벨을 재계산한다.
    /// exp는 "현재 레벨 내 누적치"로 다루며, 요구치를 넘으면 차감하며 레벨업한다(최대 레벨에서 정지).</summary>
    private (int newLevel, long newExp, bool leveledUp) ApplyExp(int level, long exp, long rewardExp)
    {
        var newLevel = level;
        var newExp = exp + rewardExp;

        while (newLevel < _masterData.MaxLevel)
        {
            var required = _masterData.LevelRequiredExp(newLevel);
            if (required <= 0 || newExp < required)
            {
                break;
            }

            newExp -= required;
            newLevel++;
        }

        return (newLevel, newExp, newLevel > level);
    }
}
