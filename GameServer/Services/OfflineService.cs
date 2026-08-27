using GameServer.MasterData;
using GameServer.Repositories.GameDb;
using GameServer.Repositories.GameDb.Interfaces;
using TaskbarHero.Common;
using TaskbarHero.Common.Dto;
using ZLogger;
using GameServer.Repositories.MasterDb;
using GameServer.Models;
using GameServer.Services.Interfaces;
using GameServer.Util;
using GameServer.Logging;

namespace GameServer.Services;

/// <summary>
/// 오프라인(방치) 보상 정산(offline-reward 기획서 §5·§6). last_active_at 기준 경과 시간을 서버 권위로 계산해
/// 골드·경험치를 산출·지급하고(아이템 미지급), last_active_at을 현재로 리셋해 중복 정산을 막는다.
/// 경과가 최소 기준(10분) 미만이면 NoOfflineReward, 동시 중복 요청이 먼저 정산했으면 OfflineRewardAlreadyClaimed.
/// </summary>
public sealed class OfflineService : IOfflineService
{
    private readonly IOfflineRepository _offlineRepository;
    private readonly MasterDbProvider _masterData;
    private readonly ILogger<OfflineService> _logger;
    private readonly IEventLogger _eventLogger;

    /// <summary>의존성(오프라인 리포지토리·마스터 데이터·운영 로거·이벤트 로거)을 주입받는다.</summary>
    public OfflineService(
        IOfflineRepository offlineRepository, MasterDbProvider masterData,
        ILogger<OfflineService> logger, IEventLogger eventLogger)
    {
        _offlineRepository = offlineRepository;
        _masterData = masterData;
        _logger = logger;
        _eventLogger = eventLogger;
    }

    /// <summary>
    /// 오프라인 보상을 정산한다. 마스터 로드·세이브 존재를 확인하고, 서버 시각 기준 경과가 최소 기준 이상일 때만
    /// 파밍 스테이지(현재 진입 스테이지)의 클리어 보상에서 파생한 시간당 산출율로 골드·경험치를 계산해
    /// 리포지토리 트랜잭션(중복 정산 방지 CAS 포함)으로 지급·리셋하고 결과를 반환한다.
    /// </summary>
    public async Task<SaveResult> ClaimAsync(long userId)
    {
        try
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

            var now = DateTimeUtil.NowUnixSeconds();
            var elapsed = DateTimeUtil.ElapsedSeconds(ctx.LastActiveAt, now);
            if (elapsed < Constants.Offline.MinRewardSec)
            {
                // 정산할 오프라인 경과가 최소 기준 미만(기획서 5.1: 200 OK + errorCode 3001).
                return new SaveResult(ErrorCode.NoOfflineReward, string.Empty, null);
            }

            // 파밍 기준: 현재 진입 스테이지의 클리어 보상(골드·경험치)에서 시간당 산출율을 파생한다.
            var (rewardGold, rewardExp) = StageRewardRates(ctx.Act, ctx.Difficulty, ctx.Stage);

            var outcome = await _offlineRepository.ClaimAsync(
                userId, now, Constants.Offline.MinRewardSec,
                lockedElapsed => ComputeReward(lockedElapsed, rewardGold, rewardExp));

            switch (outcome.Status)
            {
                case OfflineClaimStatus.NoPlayer:
                    return new SaveResult(ErrorCode.SaveNotFound, string.Empty, null);
                case OfflineClaimStatus.AlreadyClaimed:
                    return new SaveResult(ErrorCode.OfflineRewardAlreadyClaimed, string.Empty, null);
                // 처리하지 않은 상태가 성공 경로로 흘러가지 않게 닫는다.
                case OfflineClaimStatus.Ok:
                    break;
                default:
                    _logger.ZLogError($"오프라인 보상 수령: 처리하지 않은 상태 {outcome.Status:@Status}, userId {userId:@UserId}");
                    return new SaveResult(ErrorCode.ServerError, string.Empty, null);
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

            _logger.ZLogInformation($"오프라인 보상 정산: userId {userId:@UserId}, elapsed {elapsed:@Elapsed}s, effective {outcome.EffectiveSec:@Effective}s, gold {outcome.Gold:@Gold}, exp {outcome.Exp:@Exp}");

            // 지급 트랜잭션이 커밋된 뒤에 방출한다(4.2). elapsed는 응답에 담은 값과 같은 값이라
            // 로그와 유저가 본 화면이 어긋나지 않는다.
            _eventLogger.Action(
                Constants.EventLog.Tags.OfflineClaim, userId,
                new OfflineClaimEvent(elapsed, outcome.EffectiveSec, outcome.Capped, outcome.Gold, outcome.Exp));

            // 레벨업은 스테이지와 같은 테이블에 source만 다르게 쌓인다 — 두 경로의 성장 기여를 갈라 본다(5.3).
            // 재화 원장(6.1). 상세(상한 여부)는 offline_claim_logs가 담으므로 ref_id는 0이다.
            if (outcome.Gold > 0)
            {
                _eventLogger.CurrencyGained(
                    userId, outcome.Gold, outcome.GoldBalance, CurrencySource.OfflineClaim, 0);
            }

            _eventLogger.CharacterLevelUps(userId, outcome.LevelUps, LevelUpSource.Offline);

            return new SaveResult(ErrorCode.Success, "Offline reward claimed", data);
        }
        catch (Exception ex)
        {
            _logger.ZLogError(
                ex, $"ClaimAsync 처리 중 예외: errorCode {(int)ErrorCode.ServerError:@ErrorCode}({ErrorCode.ServerError:@ErrorName}), userId {userId:@UserId}");
            return new SaveResult(ErrorCode.ServerError, string.Empty, null);
        }
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
        var capped = elapsed > Constants.Offline.CapSec;
        var effective = capped ? Constants.Offline.CapSec : elapsed;

        // 시간당 산출 = 클리어 보상 / 가정 클리어 주기. 여기에 경과·오프라인 효율(÷2)을 곱/나눠 정수 내림.
        long divisor = Constants.Offline.AssumedClearIntervalSec * Constants.Offline.EfficiencyDivisor;
        long gold = effective * rewardGold / divisor;
        long exp = effective * rewardExp / divisor;
        return (effective, capped, gold, exp);
    }

}
