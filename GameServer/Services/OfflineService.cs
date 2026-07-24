using GameServer.MasterData;
using GameServer.Repositories;
using TaskbarHero.Common;
using TaskbarHero.Common.Dto;

namespace GameServer.Services;

public interface IOfflineService
{
    Task<SaveResult> ClaimAsync(long userId);
}

/// <summary>
/// 오프라인(방치) 보상 정산(offline-reward 기획서 §5·§6). last_active_at 기준 경과 시간을 서버 권위로 계산해
/// 골드·경험치를 산출·지급하고(아이템 미지급), last_active_at을 현재로 리셋해 중복 정산을 막는다.
/// 경과가 최소 기준(10분) 미만이면 NoOfflineReward, 동시 중복 요청이 먼저 정산했으면 OfflineRewardAlreadyClaimed.
/// </summary>
public sealed class OfflineService : IOfflineService
{
    private const long OfflineCapSec = 43200;         // 최대 누적 12시간 (기획서 확정)
    private const long MinRewardSec = 600;            // 최소 정산 10분 (기획서 확정)
    // 파밍 스테이지의 "클리어 보상"을 이 주기(초)마다 얻는다고 가정해 시간당 산출율을 파생한다.
    // 기획서 §9 미결이던 stageGoldRate/stageExpRate를 학습용 단순식으로 확정한 것으로, 값만 바꾸면 조정된다.
    private const long AssumedClearIntervalSec = 60;  // 스테이지 1클리어 ≈ 60초 가정
    private const long OfflineEfficiencyDivisor = 2;  // 온라인 대비 50% = ÷2 (기획서 확정)

    private readonly IOfflineRepository _offlineRepository;
    private readonly MasterDataProvider _masterData;
    private readonly ILogger<OfflineService> _logger;

    /// <summary>의존성(오프라인 리포지토리·마스터 데이터·로거)을 주입받는다.</summary>
    public OfflineService(IOfflineRepository offlineRepository, MasterDataProvider masterData, ILogger<OfflineService> logger)
    {
        _offlineRepository = offlineRepository;
        _masterData = masterData;
        _logger = logger;
    }

    /// <summary>
    /// 오프라인 보상을 정산한다. 마스터 로드·세이브 존재를 확인하고, 서버 시각 기준 경과가 최소 기준 이상일 때만
    /// 파밍 스테이지(현재 진입 스테이지)의 클리어 보상에서 파생한 시간당 산출율로 골드·경험치를 계산해
    /// 리포지토리 트랜잭션(중복 정산 방지 CAS 포함)으로 지급·리셋하고 결과를 반환한다.
    /// </summary>
    public async Task<SaveResult> ClaimAsync(long userId)
    {
        if (!_masterData.IsLoaded)
        {
            return new SaveResult(ErrorCode.MasterDataNotLoaded, string.Empty, null);
        }

        var ctx = await _offlineRepository.GetContextAsync(userId);
        if (ctx is null)
        {
            // 계정 세이브(game_player) 미생성 — 캐릭터 생성 전.
            return new SaveResult(ErrorCode.SaveNotFound, string.Empty, null);
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var elapsed = Math.Max(0, now - ctx.LastActiveAt);
        if (elapsed < MinRewardSec)
        {
            // 정산할 오프라인 경과가 최소 기준 미만(기획서 5.1: 200 OK + errorCode 3001).
            return new SaveResult(ErrorCode.NoOfflineReward, string.Empty, null);
        }

        // 파밍 기준: 현재 진입 스테이지의 클리어 보상(골드·경험치)에서 시간당 산출율을 파생한다.
        var (rewardGold, rewardExp) = StageRewardRates(ctx.Act, ctx.Difficulty, ctx.Stage);

        var outcome = await _offlineRepository.ClaimAsync(
            userId, now, MinRewardSec,
            lockedElapsed => ComputeReward(lockedElapsed, rewardGold, rewardExp),
            ApplyExp);

        switch (outcome.Status)
        {
            case OfflineClaimStatus.NoPlayer:
                return new SaveResult(ErrorCode.SaveNotFound, string.Empty, null);
            case OfflineClaimStatus.AlreadyClaimed:
                return new SaveResult(ErrorCode.OfflineRewardAlreadyClaimed, string.Empty, null);
        }

        var data = new OfflineRewardResult
        {
            offlineElapsedSec = elapsed,
            effectiveSec = outcome.EffectiveSec,
            capped = outcome.Capped,
            rewards = new OfflineRewardAmount { gold = outcome.Gold, exp = outcome.Exp },
            characters = outcome.Characters,
            lastActiveAt = outcome.LastActiveAt,
        };

        _logger.LogInformation(
            "오프라인 보상 정산: userId {UserId}, elapsed {Elapsed}s, effective {Effective}s, gold {Gold}, exp {Exp}",
            userId, elapsed, outcome.EffectiveSec, outcome.Gold, outcome.Exp);
        return new SaveResult(ErrorCode.Success, "Offline reward claimed", data);
    }

    /// <summary>파밍 기준 스테이지(현재 진입 스테이지)의 클리어 보상(골드·경험치)을 조회한다. 스테이지/보상 정의가 없으면 (0, 0).</summary>
    private (long rewardGold, long rewardExp) StageRewardRates(int act, int difficulty, int stage)
    {
        var stageDef = _masterData.GetStage(act, difficulty, stage);
        if (stageDef is null)
        {
            return (0, 0);
        }

        var reward = _masterData.GetStageReward(stageDef.StageId);
        if (reward is null)
        {
            return (0, 0);
        }

        return (reward.Gold, reward.Exp);
    }

    /// <summary>잠금 상태 경과 시간으로 보상을 산출한다: 12시간 상한 적용 후, 파밍 스테이지 클리어 보상을
    /// 가정 클리어 주기로 나눈 시간당 산출율 × 경과 × 오프라인 효율(50%). 정수 내림으로 확정한다.</summary>
    private static (long effectiveSec, bool capped, long gold, long exp) ComputeReward(long elapsed, long rewardGold, long rewardExp)
    {
        var capped = elapsed > OfflineCapSec;
        var effective = capped ? OfflineCapSec : elapsed;

        // 시간당 산출 = 클리어 보상 / 가정 클리어 주기. 여기에 경과·오프라인 효율(÷2)을 곱/나눠 정수 내림.
        long divisor = AssumedClearIntervalSec * OfflineEfficiencyDivisor;
        long gold = effective * rewardGold / divisor;
        long exp = effective * rewardExp / divisor;
        return (effective, capped, gold, exp);
    }

    /// <summary>레벨당 요구 경험치(level_master)로 지급 경험치를 반영하고 레벨을 재계산한다.
    /// exp는 "현재 레벨 내 누적치"로 다루며, 요구치를 넘으면 차감하며 레벨업한다(최대 레벨에서 정지, 초과분 이월).</summary>
    private (int newLevel, long newExp) ApplyExp(int level, long curExp, long addExp)
    {
        var newLevel = level;
        var newExp = curExp + addExp;

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

        return (newLevel, newExp);
    }
}
