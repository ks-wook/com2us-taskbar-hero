using GameServer.MasterData;
using GameServer.Repositories.GameDb;
using GameServer.Repositories.GameDb.Interfaces;
using TaskbarHero.Common;
using TaskbarHero.Common.Dto;
using ZLogger;
using GameServer.Repositories.MemoryDb;
using GameServer.Repositories.MemoryDb.Interfaces;
using GameServer.Repositories.MasterDb;
using GameServer.Models;
using GameServer.Services.Interfaces;
using GameServer.Util;
using GameServer.Logging;

namespace GameServer.Services;

/// <summary>
/// 보스러시 / 랭킹 처리(보스러시 기획서 §5·6). 서버는 <b>도전 원장 관리와 보고된 기록의 형식 검증·등재·
/// 순위 산출</b>을 담당하고, 전투와 시간 측정은 클라이언트 권위다 — 클리어 시간은 클라이언트가 측정해
/// 보고한 값을 그대로 기록하며 진위를 판정하지 않는다.
/// <para>보상은 시즌 정산의 순위 보상뿐이므로 이 서비스는 재화·아이템을 지급하지 않는다.</para>
/// </summary>
public sealed class BossRushService : IBossRushService
{
    private readonly IBossRushRepository _repository;
    private readonly IBossRushRankCache _rankCache;
    private readonly IBossRushRankWarmupService _warmupService;
    private readonly MasterDbProvider _masterData;
    private readonly ILogger<BossRushService> _logger;
    private readonly IEventLogger _eventLogger;

    /// <summary>
    /// 의존성(보스러시 리포지토리·랭킹 캐시·마스터 데이터·운영 로거·이벤트 로거)을 주입받는다.
    /// 워밍업 서비스도 받는다 — 조회가 적재되지 않은 리더보드를 만나면 재적재를 걸어야 하기 때문이다.
    /// </summary>
    public BossRushService(
        IBossRushRepository repository, IBossRushRankCache rankCache,
        IBossRushRankWarmupService warmupService,
        MasterDbProvider masterData, ILogger<BossRushService> logger, IEventLogger eventLogger)
    {
        _repository = repository;
        _rankCache = rankCache;
        _warmupService = warmupService;
        _masterData = masterData;
        _logger = logger;
        _eventLogger = eventLogger;
    }

    /// <summary>
    /// 이 시즌 리더보드를 조회에 써도 되는지 판정하고, 적재되지 않았으면 <b>자동 재적재를 건다</b>.
    /// <para><b>리더보드 키의 존재로 판단할 수 없다</b> — 없는 키의 ZCARD·ZRANGE는 예외가 아니라
    /// "0명·빈 목록"을 돌려주므로, 그대로 쓰면 캐시가 비었을 때 <b>빈 랭킹이 정상 응답으로</b> 나간다.
    /// 캐시는 정본의 파생이라 비어 있다는 사실이 "기록이 없다"의 근거가 되어서는 안 된다.</para>
    /// <para>재적재는 <b>진행 중 시즌</b>에만 건다 — 종료된 시즌은 TTL로 캐시가 사라지는 게 정상이라
    /// 되살릴 이유가 없고, 그대로 두면 과거 시즌을 열어 볼 때마다 재적재가 걸린다. Redis 자체를 쓸 수
    /// 없을 때(<c>null</c>)도 걸지 않는다 — 적재할 곳이 없다.</para>
    /// </summary>
    private async Task<bool> IsRankCacheUsableAsync(int seasonId, bool closed)
    {
        var ready = await _rankCache.IsReadyAsync(seasonId);
        if (ready == true)
        {
            return true;
        }

        if (ready == false && !closed)
        {
            _warmupService.RequestRebuild(seasonId);
        }

        return false;
    }

    /// <summary>
    /// 본인 순위 번호를 얻는다 — 리더보드를 믿을 수 있으면 ZRANK로, 아니면 <b>정본에서</b> 읽는다.
    /// 기록이 없으면 0이다.
    /// <para>캐시가 살아 있어도 ZRANK가 비면 정본을 한 번 더 확인한다 — 클리어 보고의 리더보드 반영이
    /// 실패했을 때 그 사람만 캐시에서 빠지는데, 그 경우까지 "기록 없음"으로 응답하지 않기 위해서다.</para>
    /// </summary>
    private async Task<int> ResolveMyRankAsync(int seasonId, bool closed, long userId)
    {
        if (await IsRankCacheUsableAsync(seasonId, closed))
        {
            var cachedRank = await _rankCache.GetRankAsync(seasonId, userId);
            if (cachedRank is not null)
            {
                return cachedRank.Value;
            }
        }

        var row = await _repository.GetMyRankAsync(seasonId, userId, closed);
        return row?.Rank ?? 0;
    }

    /// <summary>
    /// 도전 개시 이벤트 1행을 방출한다(5.10). 성공과 거부가 같은 자리를 쓰며, 거부 라인의
    /// run_id·season_id는 0이다(런이 만들어지지 않았다). 완주율을 셀 때는 <c>error_code = 0</c>만 분모로 삼는다.
    /// </summary>
    private void EmitEnter(long userId, long runId, int seasonId, ErrorCode errorCode)
        => _eventLogger.Action(
            Constants.EventLog.Tags.BossRushEnter, userId,
            new BossRushEnterEvent(runId, seasonId),
            (int)errorCode);

    /// <summary>
    /// 클리어 보고 이벤트 1행을 방출한다(5.10). 라운드 목록을 <b>컬럼 5개로 펼쳐</b> 담는다 —
    /// 보고에 빠진 라운드는 0이 된다(정합성 검증을 통과한 보고라 정상 경로에서는 다섯 칸이 모두 찬다).
    /// </summary>
    private void EmitClear(
        long userId, long runId, int seasonId, int clearMs,
        IReadOnlyList<(int Round, int ElapsedMs)> roundTimes,
        bool isNewRecord, int bestClearMs, int rankAtReport, ErrorCode errorCode)
    {
        var byRound = new int[Constants.BossRush.RoundLogColumns];
        foreach (var (round, elapsedMs) in roundTimes)
        {
            if (round >= 1 && round <= Constants.BossRush.RoundLogColumns)
            {
                byRound[round - 1] = elapsedMs;
            }
        }

        _eventLogger.Action(
            Constants.EventLog.Tags.BossRushClear, userId,
            new BossRushClearEvent(
                runId, seasonId, clearMs,
                byRound[0], byRound[1], byRound[2], byRound[3], byRound[4],
                isNewRecord, bestClearMs, rankAtReport),
            (int)errorCode);
    }

    /// <summary>
    /// 보스러시 진입 화면에 필요한 <b>서버만 아는 값</b>을 한 번에 반환한다(§5.1) — 해금 여부·현 시즌·
    /// 내 최고 기록·진행 중 런. 라운드 스폰 구성은 담지 않는다(도전 시작이 내려준다).
    /// <para>도전 횟수 제한도 제한 시간도 없으므로 잔여 횟수·제한 시간 필드가 없다(§5.1).</para>
    /// <para><c>activeRun</c>은 <b>아직 만료되지 않은</b> 런만 담는다 — 런 수명이 지난 런은 만료로 보고
    /// null로 내린다(만료 판정을 읽는 시점에 하는 규약, §6.2). 이 값은 전투를 끊는 타이머가 아니라
    /// 보고가 아직 받아들여지는 구간을 알린다.</para>
    /// </summary>
    public async Task<SaveResult> GetInfoAsync(long userId)
    {
        try
        {
            var rule = Rule();
            if (rule is null)
            {
                return new SaveResult(ErrorCode.MasterDataNotLoaded, string.Empty, null);
            }

            var now = DateTimeUtil.UtcNow;
            var nowUnix = DateTimeUtil.ToUnixSeconds(now);

            var season = await CurrentSeasonAsync();
            var snapshot = await _repository.GetInfoSnapshotAsync(userId, season?.SeasonId ?? 0);
            if (snapshot is null)
            {
                return new SaveResult(ErrorCode.SaveNotFound, string.Empty, null);
            }

            BossRushMyRecordDto? myRecord = null;
            if (snapshot.MyRecord is not null && season is not null)
            {
                // 기록이 있다는 것은 정본이 확정한 사실이다 — 순위만 캐시에서 얻되, 캐시가 적재되지
                // 않았으면 정본에서 읽는다(그러지 않으면 기록은 있는데 순위가 0으로 나간다).
                var myRank = await ResolveMyRankAsync(
                    season.SeasonId, season.Status == (int)BossRushSeasonStatus.Closed, userId);
                myRecord = snapshot.MyRecord.ToDto(myRank);
            }

            var activeRun = snapshot.ActiveRun is not null
                            && snapshot.ActiveRun.ExpiresAt(rule.RunExpireSec) > nowUnix
                ? snapshot.ActiveRun.ToDto(rule.RunExpireSec)
                : null;

            var data = new BossRushInfoResultData
            {
                serverTime = nowUnix,
                unlocked = snapshot.MaxStageCleared >= rule.UnlockStageSequence,
                unlockStageSequence = rule.UnlockStageSequence,
                maxStageCleared = snapshot.MaxStageCleared,
                season = season?.ToDto(),
                myRecord = myRecord,
                activeRun = activeRun,
            };

            return new SaveResult(ErrorCode.Success, "OK", data);
        }
        catch (Exception ex)
        {
            _logger.ZLogError(
                ex, $"GetInfoAsync 처리 중 예외: errorCode {(int)ErrorCode.ServerError:@ErrorCode}({ErrorCode.ServerError:@ErrorName}), userId {userId:@UserId}");
            return new SaveResult(ErrorCode.ServerError, string.Empty, null);
        }
    }

    /// <summary>
    /// 도전을 개시한다(§5.2). 런을 만든 뒤 <b>5라운드 전부의 스폰 구성</b>을 내려준다 — 라운드 전환이
    /// 전투 중에 일어나므로 라운드마다 서버를 다시 부르지 않는다. <b>차감할 횟수가 없다</b>(도전 무제한).
    /// <para>기존 진행 중 런이 있으면 자동으로 만료 종결한 뒤 새 런을 시작한다(방치형 클라이언트의 강제
    /// 종료를 유저가 스스로 복구할 수 있게 한다). 잃는 것은 그 런의 진행뿐이다.</para>
    /// <para>요청에 파라미터가 없다 — 라운드 구성·난이도는 전부 마스터 값이며 클라이언트가 고를 수 없다.</para>
    /// </summary>
    public async Task<SaveResult> EnterAsync(long userId)
    {
        try
        {
            var rule = Rule();
            var rounds = _masterData.BossRushRounds();
            if (rule is null || rounds.Count == 0)
            {
                return new SaveResult(ErrorCode.MasterDataNotLoaded, string.Empty, null);
            }

            var now = DateTimeUtil.UtcNow;

            var outcome = await _repository.ApplyEnterAsync(
                userId, rule.UnlockStageSequence, DateTimeUtil.ToUnixMilliseconds(now));

            switch (outcome.Status)
            {
                case BossRushEnterStatus.NoPlayer:
                    return new SaveResult(ErrorCode.SaveNotFound, string.Empty, null);
                case BossRushEnterStatus.Locked:
                    // 해금 미달 진입 시도가 반복되면 해금 조건(진행도)이 유저 기대와 어긋난다는 신호다.
                    EmitEnter(userId, 0, 0, ErrorCode.BossRushLocked);
                    return new SaveResult(ErrorCode.BossRushLocked, string.Empty, null);
                case BossRushEnterStatus.SeasonClosed:
                    // 정산 중 진입 시도. 반복되면 정산 창이 길다는 뜻이다(그만큼 콘텐츠가 닫혀 있었다).
                    EmitEnter(userId, 0, 0, ErrorCode.BossRushSeasonClosed);
                    return new SaveResult(ErrorCode.BossRushSeasonClosed, string.Empty, null);
                // 처리하지 않은 상태가 성공 경로로 흘러가지 않게 닫는다.
                case BossRushEnterStatus.Ok:
                    break;
                default:
                    _logger.ZLogError($"보스러시 입장: 처리하지 않은 상태 {outcome.Status:@Status}, userId {userId:@UserId}");
                    return new SaveResult(ErrorCode.ServerError, string.Empty, null);
            }

            var data = new BossRushEnterResultData
            {
                runId = outcome.RunId,
                seasonId = outcome.SeasonId,
                rounds = rounds.Select(r => r.ToDto()).ToList(),
            };

            EmitEnter(userId, outcome.RunId, outcome.SeasonId, ErrorCode.Success);
            return new SaveResult(ErrorCode.Success, "BossRushStarted", data);
        }
        catch (Exception ex)
        {
            _logger.ZLogError(
                ex, $"EnterAsync 처리 중 예외: errorCode {(int)ErrorCode.ServerError:@ErrorCode}({ErrorCode.ServerError:@ErrorName}), userId {userId:@UserId}");
            return new SaveResult(ErrorCode.ServerError, string.Empty, null);
        }
    }

    /// <summary>
    /// 클리어 보고를 처리한다(§5.3). 형식·자기정합성만 검증한 뒤 <b>클라이언트가 측정한 clearMs를 그대로</b>
    /// 기록하고, 시즌 최고를 갱신했으면 랭킹 캐시에 반영한 다음 그 상태에서 순위를 산출해 응답에 담는다.
    /// <para>검증은 형식·자기정합성뿐이며 모두 BossRushInvalidProgress로 묶인다 — clearMs가 런 수명을
    /// 넘거나(런이 열려 있던 시간보다 긴 클리어는 자기모순), 라운드 목록이 빠지거나 겹치거나 합계가
    /// clearMs와 어긋나는 경우다. 기록의 진위는 판정하지 않는다 — 전투를 재현하지 않기 때문이다.</para>
    /// <para>보상 지급은 없다. 이 호출이 바꾸는 것은 런 상태와 시즌 최고 기록뿐이다.</para>
    /// </summary>
    public async Task<SaveResult> ClearAsync(long userId, BossRushClearData request)
    {
        try
        {
            var rule = Rule();
            var rounds = _masterData.BossRushRounds();
            if (rule is null || rounds.Count == 0)
            {
                return new SaveResult(ErrorCode.MasterDataNotLoaded, string.Empty, null);
            }

            // JSON 배열에는 null 요소가 들어올 수 있다(예: rounds:[null]). 아래 정합성 검사가 요소를 그대로
            // 읽으므로, 그 전에 형식 오류로 거른다(읽으면 NullReferenceException → 잘못된 요청이 500으로 나간다).
            if (request.runId <= 0 || request.clearMs <= 0 || (request.rounds is not null && request.rounds.Contains(null)))
            {
                return new SaveResult(ErrorCode.InvalidRequest, string.Empty, null);
            }

            // 자기정합성 상한 — 런이 열려 있던 시간(런 수명)보다 긴 클리어 시간은 앞뒤가 맞지 않는다.
            // 이 검사는 런을 종결시키지 않는다(런 수명 안에서는 재보고가 통해야 한다). 동시에 이 상한이
            // 랭킹 점수 인코딩의 안전 여유를 보장한다(§4.3).
            var roundTimes = request.clearMs > rule.RunLifetimeMs
                ? null
                : NormalizeRoundTimes(request.rounds, rule.RoundCount, request.clearMs);
            if (roundTimes is null)
            {
                _logger.ZLogWarning($"보스러시 라운드 보고 정합성 실패: userId {userId:@UserId} runId {request.runId:@RunId} clearMs {request.clearMs:@ClearMs} — 클라이언트 보고값이 앞뒤가 맞지 않습니다.");
                return new SaveResult(ErrorCode.BossRushInvalidProgress, string.Empty, null);
            }

            var nowMs = DateTimeUtil.NowUnixMilliseconds();

            var outcome = await _repository.ApplyClearAsync(
                userId, request.runId, request.clearMs, roundTimes, rule.RunLifetimeMs, nowMs);

            switch (outcome.Status)
            {
                case BossRushClearStatus.RunNotFound:
                    return new SaveResult(ErrorCode.BossRushRunNotFound, string.Empty, null);
                case BossRushClearStatus.AlreadyFinished:
                    // 이미 닫힌 런에 온 보고(중복 보고이거나 런 수명 초과). 후자가 반복되면 런 수명이
                    // 실제 플레이 시간보다 짧다는 신호라, 보고된 기록을 그대로 담아 남긴다.
                    EmitClear(
                        userId, request.runId, outcome.SeasonId, request.clearMs, roundTimes,
                        isNewRecord: false, bestClearMs: 0, rankAtReport: 0,
                        errorCode: ErrorCode.BossRushRunAlreadyFinished);
                    return new SaveResult(ErrorCode.BossRushRunAlreadyFinished, string.Empty, null);
                // 처리하지 않은 상태가 성공 경로로 흘러가지 않게 닫는다.
                case BossRushClearStatus.Ok:
                    break;
                default:
                    _logger.ZLogError($"보스러시 클리어 보고: 처리하지 않은 상태 {outcome.Status:@Status}, userId {userId:@UserId}");
                    return new SaveResult(ErrorCode.ServerError, string.Empty, null);
            }

            // 커밋 이후에만 랭킹 캐시를 갱신한다 — Redis에는 롤백이 없다(§6.2).
            var rank = 0;
            if (outcome.RankEligible)
            {
                if (outcome.IsNewRecord
                    && !await _rankCache.UpsertAsync(
                        outcome.SeasonId, outcome.SeasonStartAt, userId, outcome.BestClearMs, outcome.RecordedAt))
                {
                    // **반영 실패를 버리지 않는다** — 기록은 정본에 들어갔는데 리더보드에는 이 사람만 빠진
                    // 상태다. 리더보드 자체는 멀쩡해 보이므로 아무도 폴백하지 않고, 다음 기록 갱신 전까지
                    // 이 사람은 랭킹에서 사라진다. 마커를 내려 다음 조회가 정본을 보고 재적재를 걸게 한다.
                    await _rankCache.ClearReadyAsync(outcome.SeasonId);
                    _logger.ZLogWarning(
                        $"보스러시 리더보드 반영 실패 — 랭킹 캐시를 재적재 대상으로 내립니다: seasonId {outcome.SeasonId:@SeasonId} userId {userId:@UserId}");
                }

                // 기록을 갱신하지 못했어도 순위는 내려준다 — 다른 유저가 올라와 순위가 밀렸을 수 있다.
                // 클리어 보고는 진행 중 시즌에만 들어오므로 종료 시즌(final_rank) 경로가 아니다.
                rank = await ResolveMyRankAsync(outcome.SeasonId, false, userId);
            }

            var data = new BossRushClearResultData
            {
                runId = request.runId,
                seasonId = outcome.SeasonId,
                clearMs = request.clearMs,
                isNewRecord = outcome.IsNewRecord,
                bestClearMs = outcome.BestClearMs,
                rank = rank,
            };

            // 랭킹 캐시 갱신까지 끝난 뒤에 방출한다 — rank_at_report가 응답과 같은 값이어야 하기 때문이다.
            EmitClear(
                userId, request.runId, outcome.SeasonId, request.clearMs, roundTimes,
                outcome.IsNewRecord, outcome.BestClearMs, rank, ErrorCode.Success);

            return new SaveResult(ErrorCode.Success, "BossRushCleared", data);
        }
        catch (Exception ex)
        {
            _logger.ZLogError(
                ex, $"ClearAsync 처리 중 예외: errorCode {(int)ErrorCode.ServerError:@ErrorCode}({ErrorCode.ServerError:@ErrorName}), userId {userId:@UserId}");
            return new SaveResult(ErrorCode.ServerError, string.Empty, null);
        }
    }

    /// <summary>
    /// 랭킹 목록 한 페이지를 반환한다(§5.4). <b>뷰어와 무관한 데이터</b>로 내 순위는 담지 않는다.
    /// <para>노출 순위에 상한이 없다 — 1위부터 꼴찌까지 offset으로 넘겨 볼 수 있고, 서버가 clamp하는 것은
    /// 페이지 크기(limit)뿐이다. 페이지 간 스냅샷은 보장하지 않는다(§6.5).</para>
    /// <para>정상 경로는 Redis 단독(ZRANGE + ZCARD + 닉네임 HMGET)이며, 캐시를 쓸 수 없으면 MySQL로
    /// 폴백한다. 종료된 시즌은 정산이 확정한 final_rank를 그대로 읽어 순위를 재계산하지 않는다.</para>
    /// </summary>
    public async Task<SaveResult> GetRankAsync(long userId, int seasonId, int offset, int limit)
    {
        try
        {
            var rule = Rule();
            if (rule is null)
            {
                return new SaveResult(ErrorCode.MasterDataNotLoaded, string.Empty, null);
            }

            var season = await ResolveSeasonAsync(seasonId);
            if (season is null)
            {
                return new SaveResult(ErrorCode.BossRushSeasonClosed, string.Empty, null);
            }

            var safeOffset = Math.Max(offset, 0);
            var safeLimit = limit <= 0 ? Constants.BossRush.RankDefaultLimit : Math.Min(limit, rule.RankPageLimit);

            var closed = season.Status == (int)BossRushSeasonStatus.Closed;

            // 리더보드를 믿을 수 있을 때만 읽는다 — 적재되지 않은 리더보드는 ZCARD가 0을 돌려주므로
            // 그대로 읽으면 "등재 0명"이 정상 응답으로 나간다(캐시 부재가 오답이 되는 자리다).
            var source = BossRushRankSource.RankCache;
            IReadOnlyList<BossRushCachedRank>? cached = null;
            int? total = null;
            if (await IsRankCacheUsableAsync(season.SeasonId, closed))
            {
                cached = await _rankCache.GetPageAsync(season.SeasonId, season.StartAt, safeOffset, safeLimit);
                total = await _rankCache.CountAsync(season.SeasonId);
            }

            List<BossRushRankRow> rows;
            if (cached is null || total is null)
            {
                source = BossRushRankSource.Database;
                rows = (await _repository.GetRankPageAsync(season.SeasonId, safeOffset, safeLimit, closed)).ToList();
                total = await _repository.CountEntriesAsync(season.SeasonId);
            }
            else
            {
                rows = cached.Select(c => new BossRushRankRow(c.Rank, c.UserId, c.ClearMs, c.RecordedAt)).ToList();
            }

            var nicknames = await ResolveNicknamesAsync(rows.Select(r => r.UserId).ToList());

            var data = new BossRushRankResultData
            {
                seasonId = season.SeasonId,
                seasonStatus = season.Status,
                seasonEndAt = season.EndAt,
                totalEntries = total ?? rows.Count,
                offset = safeOffset,
                limit = safeLimit,
                source = (int)source,
                entries = rows.Select(r => r.ToDto(nicknames)).ToList(),
            };

            return new SaveResult(ErrorCode.Success, "OK", data);
        }
        catch (Exception ex)
        {
            _logger.ZLogError(
                ex, $"GetRankAsync 처리 중 예외: errorCode {(int)ErrorCode.ServerError:@ErrorCode}({ErrorCode.ServerError:@ErrorName}), userId {userId:@UserId}");
            return new SaveResult(ErrorCode.ServerError, string.Empty, null);
        }
    }

    /// <summary>
    /// 요청자 본인의 시즌 순위 1건을 반환한다(§5.5). 랭킹 UI의 고정 영역이 쓰는 값이라 목록 페이지를
    /// 넘기는 동안 다시 호출할 필요가 없다. 기록이 없으면 <c>myRank</c>가 null이다.
    /// <para>시즌 메타(seasonStatus·seasonEndAt)는 담지 않는다 — info·rank가 이미 내려준다.</para>
    /// </summary>
    public async Task<SaveResult> GetMyRankAsync(long userId, int seasonId)
    {
        try
        {
            var season = await ResolveSeasonAsync(seasonId);
            if (season is null)
            {
                return new SaveResult(ErrorCode.BossRushSeasonClosed, string.Empty, null);
            }

            var closed = season.Status == (int)BossRushSeasonStatus.Closed;

            // 적재되지 않은 리더보드에서는 "내가 없다"가 곧 "기록이 없다"가 아니다 — 아무도 없는 것이다.
            // 그래서 캐시에 물어보기 전에 믿을 수 있는 리더보드인지부터 가른다.
            var source = BossRushRankSource.RankCache;
            BossRushCachedRank? cachedEntry = null;
            int? total = null;
            if (await IsRankCacheUsableAsync(season.SeasonId, closed))
            {
                cachedEntry = await _rankCache.GetMyEntryAsync(season.SeasonId, season.StartAt, userId);
                total = await _rankCache.CountAsync(season.SeasonId);
            }

            BossRushRankRow? row;
            if (total is null)
            {
                source = BossRushRankSource.Database;
                row = await _repository.GetMyRankAsync(season.SeasonId, userId, closed);
                total = await _repository.CountEntriesAsync(season.SeasonId);
            }
            else if (cachedEntry is null)
            {
                // 전량 적재된 리더보드에 내가 없다 = 그 시즌에 기록이 없다(폴백 대상이 아니다).
                row = null;
            }
            else
            {
                row = new BossRushRankRow(
                    cachedEntry.Rank, cachedEntry.UserId, cachedEntry.ClearMs, cachedEntry.RecordedAt);
            }

            var nicknames = row is null
                ? new Dictionary<long, string>()
                : await ResolveNicknamesAsync(new List<long> { row.UserId });

            var data = new BossRushMyRankResultData
            {
                seasonId = season.SeasonId,
                totalEntries = total ?? 0,
                source = (int)source,
                myRank = row?.ToDto(nicknames),
            };

            return new SaveResult(ErrorCode.Success, "OK", data);
        }
        catch (Exception ex)
        {
            _logger.ZLogError(
                ex, $"GetMyRankAsync 처리 중 예외: errorCode {(int)ErrorCode.ServerError:@ErrorCode}({ErrorCode.ServerError:@ErrorName}), userId {userId:@UserId}");
            return new SaveResult(ErrorCode.ServerError, string.Empty, null);
        }
    }

    /// <summary>마스터에 적재된 보스러시 전역 규칙. 마스터 미로드거나 콘텐츠 미구성이면 null.</summary>
    private BossRushRuleDef? Rule()
        => _masterData.IsLoaded ? _masterData.BossRushRule : null;

    /// <summary>
    /// 현재 시즌을 얻는다 — 캐시(<c>bossrush:season:current</c>)를 먼저 보고, 없으면 MySQL에서 읽어
    /// 캐시를 채운다. 캐시에 담긴 시즌이 이미 진행 중이 아니면 정본을 다시 읽는다(정산 직후 갱신 지연 흡수).
    /// </summary>
    private async Task<BossRushSeason?> CurrentSeasonAsync()
    {
        var cached = await _rankCache.GetCurrentSeasonAsync();
        if (cached is not null && cached.Status == (int)BossRushSeasonStatus.Running)
        {
            return cached;
        }

        var season = await _repository.GetRunningSeasonAsync();
        if (season is not null)
        {
            await _rankCache.SetCurrentSeasonAsync(season);
        }

        return season;
    }

    /// <summary>
    /// 요청의 seasonId를 시즌으로 해석한다. 0 이하면 현재 시즌, 그 외에는 그 시즌을 정본에서 읽는다
    /// (종료 시즌 메타는 뜨거운 경로가 아니라 캐싱하지 않는다, §4.3).
    /// </summary>
    private async Task<BossRushSeason?> ResolveSeasonAsync(int seasonId)
        => seasonId <= 0 ? await CurrentSeasonAsync() : await _repository.GetSeasonAsync(seasonId);

    /// <summary>
    /// 랭킹 표시 이름을 채운다 — 닉네임 캐시(HMGET)를 먼저 보고, 미스된 userId만 <c>game_player</c>에서
    /// 읽어 캐시에 백필한다(lazy 채움). 그래서 정상 상태에서는 MySQL을 건드리지 않는다(§4.3).
    /// </summary>
    private async Task<IReadOnlyDictionary<long, string>> ResolveNicknamesAsync(IReadOnlyList<long> userIds)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<long, string>();
        }

        var distinct = userIds.Distinct().ToList();
        var cached = await _rankCache.GetNicknamesAsync(distinct);

        var missing = distinct.Where(id => !cached.ContainsKey(id)).ToList();
        if (missing.Count == 0)
        {
            return cached;
        }

        var fetched = await _repository.GetNicknamesAsync(missing);
        await _rankCache.SetNicknamesAsync(fetched);

        var merged = new Dictionary<long, string>(cached);
        foreach (var pair in fetched)
        {
            merged[pair.Key] = pair.Value;
        }

        return merged;
    }

    /// <summary>
    /// 클라이언트가 보고한 라운드 목록을 검증·정규화한다(§6.2 2단계). 형식·자기정합성만 본다 —
    /// 1..roundCount가 빠짐없이 한 번씩 있고, 각 소요가 양수이며, <b>합계가 clearMs와 일치</b>해야 한다.
    /// 어긋나면 null을 돌려주고 호출측이 BossRushInvalidProgress로 거부한다(클라이언트 버그).
    /// </summary>
    private static List<(int Round, int ElapsedMs)>? NormalizeRoundTimes(
        List<BossRushRoundTimeDto>? reported, int roundCount, int clearMs)
    {
        if (reported is null || reported.Count != roundCount)
        {
            return null;
        }

        var byRound = new Dictionary<int, int>(roundCount);
        long sum = 0;
        foreach (var entry in reported)
        {
            if (entry.round < 1 || entry.round > roundCount || entry.elapsedMs <= 0)
            {
                return null;
            }

            if (!byRound.TryAdd(entry.round, entry.elapsedMs))
            {
                return null; // 같은 라운드가 두 번
            }

            sum += entry.elapsedMs;
        }

        if (byRound.Count != roundCount || sum != clearMs)
        {
            return null;
        }

        return Enumerable.Range(1, roundCount).Select(r => (r, byRound[r])).ToList();
    }
}
