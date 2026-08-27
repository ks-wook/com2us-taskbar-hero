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
/// 스테이지 진입·클리어 처리(stage-battle 기획서 §5·§6).
/// 진입: 도달 가능 여부 검증 후 현재 진입 스테이지 설정(보상 없음).
/// 클리어: 서버 권위로 보상(골드·경험치·드롭) 산출 후 트랜잭션으로 지급·진행도 갱신.
/// </summary>
public sealed class StageService : IStageService
{
    private readonly IStageRepository _stageRepository;
    private readonly MasterDbProvider _masterData;
    private readonly ILogger<StageService> _logger;
    private readonly IEventLogger _eventLogger;

    /// <summary>의존성(스테이지 리포지토리·마스터 데이터·운영 로거·이벤트 로거)을 주입받는다.</summary>
    public StageService(
        IStageRepository stageRepository, MasterDbProvider masterData,
        ILogger<StageService> logger, IEventLogger eventLogger)
    {
        _stageRepository = stageRepository;
        _masterData = masterData;
        _logger = logger;
        _eventLogger = eventLogger;
    }

    /// <summary>
    /// 스테이지 진입을 처리한다. 마스터 로드·스테이지 존재·세이브 존재를 확인하고,
    /// 대상이 도달 가능한 범위(이미 클리어했거나 프런티어+1)인지 검증한 뒤 현재 진입 스테이지로 설정한다.
    /// 성공 시 스폰·보스·배경 정보를 담은 진입 응답을 반환한다(보상 없음).
    /// </summary>
    public async Task<SaveResult> EnterAsync(long userId, int act, int difficulty, int stage)
    {
        try
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
                // 진입 거부도 남긴다 — 도달 못 한 스테이지로의 시도가 반복되면 잠금 UI나 진행 곡선의 문제다(5.3).
                // 나머지 거부(스테이지 없음·세이브 없음·마스터 미적재)는 클라 결함/서버 결함이라 남기지 않는다.
                EmitStageEnter(userId, stageDef.StageId, act, difficulty, stage, ErrorCode.StageLocked);
                return new SaveResult(ErrorCode.StageLocked, string.Empty, null);
            }

            var now = DateTimeUtil.NowUnixSeconds();
            await _stageRepository.SetCurrentStageAsync(userId, act, difficulty, stage, now);

            EmitStageEnter(userId, stageDef.StageId, act, difficulty, stage, ErrorCode.Success);

            var data = new StageEnterData
            {
                act = act,
                difficulty = difficulty,
                stage = stage,
                stageId = stageDef.StageId,
                monsters = new List<StageSpawnDto>(stageDef.Spawns),
                boss = stageDef.BossMonsterCode == 0
                    ? null
                    : new StageBossDto { monsterCode = stageDef.BossMonsterCode, monsterLevel = stageDef.BossMonsterLevel },
                backgroundType = stageDef.BackgroundType,
                enteredAt = now,
            };

            return new SaveResult(ErrorCode.Success, "Stage entered", data);
        }
        catch (Exception ex)
        {
            _logger.ZLogError(
                ex, $"EnterAsync 처리 중 예외: errorCode {(int)ErrorCode.ServerError:@ErrorCode}({ErrorCode.ServerError:@ErrorName}), userId {userId:@UserId}");
            return new SaveResult(ErrorCode.ServerError, string.Empty, null);
        }
    }

    /// <summary>
    /// 스테이지 클리어를 처리한다. 마스터 로드·스테이지·보상 정의를 확인하고, 서버 권위로 보상을
    /// 산출한다(골드·경험치는 고정, 드롭은 등급 확률로 추첨). 이어서 리포지토리 트랜잭션으로
    /// 진입 스테이지 재검증 → 골드·경험치 지급 → 전리품 적재 → 진행도 갱신을 원자적으로 수행하고,
    /// 결과 상태를 에러 코드로 매핑해(미진입 등) 클리어 응답을 반환한다.
    /// 인벤토리가 가득 차 전리품을 적재할 수 없으면 실패로 처리하지 않고 전리품만 폐기해
    /// 골드·경험치만 지급한다(응답 rewards.items 비움).
    /// </summary>
    public async Task<SaveResult> ClearAsync(long userId, int act, int difficulty, int stage)
    {
        try
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

            var now = DateTimeUtil.NowUnixSeconds();
            var outcome = await _stageRepository.ApplyClearAsync(
                userId, act, difficulty, stage, reward.Gold, reward.Exp, dropped,
                now);

            switch (outcome.Status)
            {
                case ClearStatus.NoPlayer:
                    return new SaveResult(ErrorCode.SaveNotFound, string.Empty, null);
                case ClearStatus.NotEntered:
                    return new SaveResult(ErrorCode.StageNotEntered, string.Empty, null);
                // 처리하지 않은 상태가 성공 경로로 흘러가지 않게 닫는다.
                case ClearStatus.Ok:
                    break;
                default:
                    _logger.ZLogError($"스테이지 클리어: 처리하지 않은 상태 {outcome.Status:@Status}, userId {userId:@UserId}");
                    return new SaveResult(ErrorCode.ServerError, string.Empty, null);
            }

            // 전리품은 실제로 적재된 경우에만 보상 목록에 담는다. 인벤토리가 가득 차 폐기됐으면
            // 클리어를 거부하지 않고(기획서 6.3) 골드·경험치만 지급하므로 items는 비운다.
            var items = new List<RewardItemDto>();
            if (dropped is not null && outcome.LootStored)
            {
                items.Add(new RewardItemDto { itemCode = dropped.ItemCode, quantity = dropped.Quantity });
            }

            if (dropped is not null && !outcome.LootStored)
            {
                _logger.ZLogDebug($"전리품 폐기(인벤토리 가득): userId {userId:@UserId}, itemCode {dropped.ItemCode:@ItemCode}, quantity {dropped.Quantity:@Quantity}");
            }

            var data = new StageClearData
            {
                cleared = new ClearedStageDto { act = act, difficulty = difficulty, stage = stage },
                // 버프 배율이 적용된 최종 지급액을 응답에 담는다(클라이언트가 표시하는 값 = 실제 반영된 값).
                rewards = new StageRewardsDto { gold = outcome.GrantedGold, exp = outcome.GrantedExp, items = items },
                characters = outcome.Characters,
                balance = new List<CurrencyDto>
                {
                    new CurrencyDto { currencyType = Constants.Currency.GoldType, amount = outcome.GoldBalance },
                },
                progress = new StageProgressDto
                {
                    act = outcome.Act,
                    difficulty = outcome.Difficulty,
                    stage = outcome.Stage,
                    maxStageCleared = outcome.MaxStageCleared,
                },
                inventoryDelta = outcome.Delta,
            };

            _logger.ZLogInformation($"스테이지 클리어: userId {userId:@UserId}, act {act:@Act}, difficulty {difficulty:@Difficulty}, stage {stage:@Stage}, gold {outcome.GrantedGold:@Gold}, exp {outcome.GrantedExp:@Exp}, goldMul {outcome.GoldMultiplier:@GoldMultiplier}, expMul {outcome.ExpMultiplier:@ExpMultiplier}");

            // 지급 트랜잭션이 커밋된 뒤에 방출한다(롤백된 사실을 로그에 남기지 않는다, 4.2).
            // 담는 값은 마스터 기본값이 아니라 **실제 지급액**이다 — 버프 배율이 곱해진 뒤의 유입량이어야
            // 재화 유입 집계가 경제 실측이 된다.
            _eventLogger.Action(
                Constants.EventLog.Tags.StageClear, userId,
                new StageClearEvent(
                    stageDef.StageId, act, difficulty, stage,
                    outcome.GrantedGold, outcome.GrantedExp, outcome.IsFirstClear, outcome.MaxStageCleared));

            // 재화 원장(6.1). 스테이지 클리어가 이 경제의 주 유입원이다.
            if (outcome.GrantedGold > 0)
            {
                _eventLogger.CurrencyGained(
                    userId, outcome.GrantedGold, outcome.GoldBalance, CurrencySource.StageClear, stageDef.StageId);
            }

            // 아이템 원장(6.2). **실제로 적재된 전리품만** 남긴다 — 가방이 가득 차 폐기된 드랍은
            // 계정 보유량을 바꾸지 않았으므로 원장에 넣으면 유통량이 부풀려진다.
            // 이 행들의 reason='stage_drop'이 곧 드랍률 실측의 근거다(5.3 — 클리어 로그는 드랍 배열을 갖지 않는다).
            if (dropped is not null && outcome.LootStored)
            {
                _eventLogger.ItemGained(
                    userId, dropped.ItemCode, _masterData.GetItem(dropped.ItemCode), dropped.Quantity,
                    new GrantedItemIds(outcome.Delta), ItemFlowReason.StageDrop, stageDef.StageId);
            }

            _eventLogger.CharacterLevelUps(userId, outcome.LevelUps, LevelUpSource.Stage);

            return new SaveResult(ErrorCode.Success, "Stage cleared", data);
        }
        catch (Exception ex)
        {
            _logger.ZLogError(
                ex, $"ClearAsync 처리 중 예외: errorCode {(int)ErrorCode.ServerError:@ErrorCode}({ErrorCode.ServerError:@ErrorName}), userId {userId:@UserId}");
            return new SaveResult(ErrorCode.ServerError, string.Empty, null);
        }
    }

    /// <summary>
    /// 스테이지 실패(파티 전멸)를 접수한다. 전투가 클라이언트 권위라 서버는 전멸을 스스로 알 수 없으므로,
    /// 클라이언트 보고를 **기록 전용**으로 받는다(기획서 5.3). 진행도·보상·재화를 일절 바꾸지 않으며,
    /// 현재 진입 스테이지도 유지해 같은 스테이지를 곧바로 재시도할 수 있게 둔다.
    /// 스테이지 존재·현재 진입 스테이지 일치·보고 수치의 형식만 검증한 뒤 실패 사실을 로그로 남긴다.
    /// </summary>
    public async Task<SaveResult> FailAsync(
        long userId, int act, int difficulty, int stage,
        int elapsedMs, int remainingMonsterCount, bool reachedBoss)
    {
        try
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

            // 보고 수치 검증: 음수는 클라이언트 결함이다. 집계를 오염시키느니 접수를 거부한다.
            if (elapsedMs < 0 || remainingMonsterCount < 0)
            {
                return new SaveResult(ErrorCode.InvalidRequest, string.Empty, null);
            }

            var progress = await _stageRepository.GetProgressAsync(userId);
            if (progress is null)
            {
                return new SaveResult(ErrorCode.SaveNotFound, string.Empty, null);
            }

            // 진입하지 않은 스테이지의 실패 보고는 받지 않는다(클리어와 같은 기준) —
            // enter 없이 들어온 fail은 난이도 집계의 분모(enter)와 짝이 맞지 않는다.
            if (progress.Act != act || progress.Difficulty != difficulty || progress.Stage != stage)
            {
                return new SaveResult(ErrorCode.StageNotEntered, string.Empty, null);
            }

            var now = DateTimeUtil.NowUnixSeconds();

            _logger.ZLogInformation($"스테이지 실패: userId {userId:@UserId}, stageId {stageDef.StageId:@StageId}, act {act:@Act}, difficulty {difficulty:@Difficulty}, stage {stage:@Stage}, elapsedMs {elapsedMs:@ElapsedMs}, remainingMonsterCount {remainingMonsterCount:@RemainingMonsterCount}, reachedBoss {reachedBoss:@ReachedBoss}");

            // 이 엔드포인트의 존재 이유가 곧 이 이벤트다 — 상태를 바꾸지 않고 이 한 행만 남긴다(5.3).
            _eventLogger.Action(
                Constants.EventLog.Tags.StageFail, userId,
                new StageFailEvent(
                    stageDef.StageId, act, difficulty, stage, elapsedMs, remainingMonsterCount, reachedBoss));

            var data = new StageFailResultData
            {
                act = act,
                difficulty = difficulty,
                stage = stage,
                stageId = stageDef.StageId,
                failedAt = now,
            };

            return new SaveResult(ErrorCode.Success, "Stage failure recorded", data);
        }
        catch (Exception ex)
        {
            _logger.ZLogError(
                ex, $"FailAsync 처리 중 예외: errorCode {(int)ErrorCode.ServerError:@ErrorCode}({ErrorCode.ServerError:@ErrorName}), userId {userId:@UserId}");
            return new SaveResult(ErrorCode.ServerError, string.Empty, null);
        }
    }

    /// <summary>
    /// 진입 이벤트 1행을 방출한다(5.3). 성공과 거부가 필드까지 같고 <c>error_code</c>만 다르므로
    /// 두 경로가 같은 자리를 쓴다 — 거부만 따로 적는 자리를 만들면 같은 사실이 두 곳에 생긴다(5장).
    /// </summary>
    private void EmitStageEnter(long userId, int stageId, int act, int difficulty, int stage, ErrorCode errorCode)
        => _eventLogger.Action(
            Constants.EventLog.Tags.StageEnter, userId,
            new StageEnterEvent(stageId, act, difficulty, stage),
            (int)errorCode);
}
