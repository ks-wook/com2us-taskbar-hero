using GameServer.MasterData;
using GameServer.Repositories;
using GameServer.Repositories.Interfaces;
using TaskbarHero.Common;
using TaskbarHero.Common.Dto;
using ZLogger;

namespace GameServer.Services;

public interface IBossRushService
{
    Task<SaveResult> GetInfoAsync(long userId);
    Task<SaveResult> EnterAsync(long userId);
    Task<SaveResult> ClearAsync(long userId, BossRushClearData request);
    Task<SaveResult> GetRankAsync(long userId, int seasonId, int offset, int limit);
    Task<SaveResult> GetMyRankAsync(long userId, int seasonId);
}

/// <summary>
/// 보스러시 / 랭킹 처리(보스러시 기획서 §5·6). 서버는 <b>도전 원장 관리와 보고된 기록의 형식 검증·등재·
/// 순위 산출</b>을 담당하고, 전투와 시간 측정은 클라이언트 권위다 — 클리어 시간은 클라이언트가 측정해
/// 보고한 값을 그대로 기록하며 진위를 판정하지 않는다.
/// <para>보상은 시즌 정산의 순위 보상뿐이므로 이 서비스는 재화·아이템을 지급하지 않는다.</para>
/// </summary>
public sealed class BossRushService : IBossRushService
{
    /// <summary>KST(UTC+9) — 일일 도전 횟수의 날짜 경계(출석부와 같은 규약).</summary>
    private static readonly TimeSpan KstOffset = TimeSpan.FromHours(9);

    /// <summary>랭킹 목록 기본 페이지 크기(요청이 limit을 생략했을 때).</summary>
    private const int RankDefaultLimit = 50;

    private readonly IBossRushRepository _repository;
    private readonly IBossRushRankCache _rankCache;
    private readonly MasterDataProvider _masterData;
    private readonly ILogger<BossRushService> _logger;

    /// <summary>의존성(보스러시 리포지토리·랭킹 캐시·마스터 데이터·로거)을 주입받는다.</summary>
    public BossRushService(
        IBossRushRepository repository, IBossRushRankCache rankCache,
        MasterDataProvider masterData, ILogger<BossRushService> logger)
    {
        _repository = repository;
        _rankCache = rankCache;
        _masterData = masterData;
        _logger = logger;
    }

    /// <summary>
    /// 보스러시 진입 화면에 필요한 <b>서버만 아는 값</b>을 한 번에 반환한다(§5.1) — 해금 여부·일일 잔여 횟수·
    /// 현 시즌·내 최고 기록·진행 중 런. 라운드 스폰 구성은 담지 않는다(도전 시작이 내려준다).
    /// <para><c>activeRun</c>은 <b>아직 만료되지 않은</b> 런만 담는다 — 제한 시간 + 그레이스가 지난 런은
    /// 만료로 보고 null로 내린다(만료 판정을 읽는 시점에 하는 규약, §6.2).</para>
    /// </summary>
    public async Task<SaveResult> GetInfoAsync(long userId)
    {
        var rule = Rule();
        if (rule is null)
        {
            return new SaveResult(ErrorCode.MasterDataNotLoaded, string.Empty, null);
        }

        var now = DateTimeOffset.UtcNow;
        var nowUnix = now.ToUnixTimeSeconds();
        var (todayStart, tomorrowStart) = KstDayRange(now);

        var season = await CurrentSeasonAsync();
        var snapshot = await _repository.GetInfoSnapshotAsync(
            userId, season?.SeasonId ?? 0, todayStart, tomorrowStart);
        if (snapshot is null)
        {
            return new SaveResult(ErrorCode.SaveNotFound, string.Empty, null);
        }

        BossRushMyRecordDto? myRecord = null;
        if (snapshot.MyRecord is not null && season is not null)
        {
            var rank = await _rankCache.GetRankAsync(season.SeasonId, userId);
            myRecord = new BossRushMyRecordDto
            {
                bestClearMs = snapshot.MyRecord.BestClearMs,
                recordedAt = snapshot.MyRecord.RecordedAt,
                rank = rank ?? 0,
            };
        }

        BossRushActiveRunDto? activeRun = null;
        if (snapshot.ActiveRun is not null)
        {
            var startedAtUnix = snapshot.ActiveRun.StartedAtMs / 1000;
            var expiresAt = startedAtUnix + rule.TimeLimitSec + rule.ExpireGraceSec;
            if (expiresAt > nowUnix)
            {
                activeRun = new BossRushActiveRunDto
                {
                    runId = snapshot.ActiveRun.RunId,
                    startedAt = startedAtUnix,
                    expiresAt = expiresAt,
                };
            }
        }

        var data = new BossRushInfoResultData
        {
            serverTime = nowUnix,
            unlocked = snapshot.MaxStageCleared >= rule.UnlockStageSequence,
            unlockStageSequence = rule.UnlockStageSequence,
            maxStageCleared = snapshot.MaxStageCleared,
            dailyEntryLimit = rule.DailyEntryLimit,
            dailyEntryUsed = snapshot.DailyEntryUsed,
            dailyResetAt = tomorrowStart,
            timeLimitMs = rule.TimeLimitMs,
            season = season is null ? null : ToSeasonDto(season),
            myRecord = myRecord,
            activeRun = activeRun,
        };

        return new SaveResult(ErrorCode.Success, "OK", data);
    }

    /// <summary>
    /// 도전을 개시한다(§5.2). 일일 횟수를 차감하고 런을 만든 뒤 <b>5라운드 전부의 스폰 구성</b>을 내려준다 —
    /// 라운드 전환이 전투 중에 일어나므로 라운드마다 서버를 다시 부르지 않는다.
    /// <para>기존 진행 중 런이 있으면 자동으로 만료 종결한 뒤 새 런을 시작한다(방치형 클라이언트의 강제
    /// 종료를 유저가 스스로 복구할 수 있게 한다). 이미 소모된 일일 횟수는 돌려주지 않는다.</para>
    /// <para>요청에 파라미터가 없다 — 라운드 구성·제한 시간·난이도는 전부 마스터 값이며 클라이언트가 고를 수 없다.</para>
    /// </summary>
    public async Task<SaveResult> EnterAsync(long userId)
    {
        var rule = Rule();
        var rounds = _masterData.BossRushRounds();
        if (rule is null || rounds.Count == 0)
        {
            return new SaveResult(ErrorCode.MasterDataNotLoaded, string.Empty, null);
        }

        var now = DateTimeOffset.UtcNow;
        var (todayStart, tomorrowStart) = KstDayRange(now);

        var outcome = await _repository.ApplyEnterAsync(
            userId, rule.UnlockStageSequence, rule.DailyEntryLimit,
            todayStart, tomorrowStart, now.ToUnixTimeMilliseconds());

        switch (outcome.Status)
        {
            case BossRushEnterStatus.NoPlayer:
                return new SaveResult(ErrorCode.SaveNotFound, string.Empty, null);
            case BossRushEnterStatus.Locked:
                return new SaveResult(ErrorCode.BossRushLocked, string.Empty, null);
            case BossRushEnterStatus.DailyLimit:
                return new SaveResult(ErrorCode.BossRushDailyLimitExceeded, string.Empty, null);
            case BossRushEnterStatus.SeasonClosed:
                return new SaveResult(ErrorCode.BossRushSeasonClosed, string.Empty, null);
        }

        var data = new BossRushEnterResultData
        {
            runId = outcome.RunId,
            seasonId = outcome.SeasonId,
            timeLimitMs = rule.TimeLimitMs,
            rounds = rounds.Select(ToRoundDto).ToList(),
            dailyEntryUsed = outcome.DailyEntryUsed,
            dailyEntryLimit = rule.DailyEntryLimit,
        };

        return new SaveResult(ErrorCode.Success, "BossRushStarted", data);
    }

    /// <summary>
    /// 클리어 보고를 처리한다(§5.3). 형식·자기정합성만 검증한 뒤 <b>클라이언트가 측정한 clearMs를 그대로</b>
    /// 기록하고, 시즌 최고를 갱신했으면 랭킹 캐시에 반영한 다음 그 상태에서 순위를 산출해 응답에 담는다.
    /// <para>검증 순서: 제한 시간 상한(BossRushTimeout) → 라운드 목록 형식·합계 일치(BossRushInvalidProgress).
    /// 기록의 진위는 판정하지 않는다 — 전투를 재현하지 않기 때문이다.</para>
    /// <para>보상 지급은 없다. 이 호출이 바꾸는 것은 런 상태와 시즌 최고 기록뿐이다.</para>
    /// </summary>
    public async Task<SaveResult> ClearAsync(long userId, BossRushClearData request)
    {
        var rule = Rule();
        var rounds = _masterData.BossRushRounds();
        if (rule is null || rounds.Count == 0)
        {
            return new SaveResult(ErrorCode.MasterDataNotLoaded, string.Empty, null);
        }

        if (request.runId <= 0 || request.clearMs <= 0)
        {
            return new SaveResult(ErrorCode.InvalidRequest, string.Empty, null);
        }

        // 제한 시간 상한 — 이 검사는 런을 종결시키지 않는다(그레이스 안에서는 재보고가 통해야 한다).
        if (request.clearMs > rule.TimeLimitMs)
        {
            return new SaveResult(ErrorCode.BossRushTimeout, string.Empty, null);
        }

        var roundTimes = NormalizeRoundTimes(request.rounds, rule.RoundCount, request.clearMs);
        if (roundTimes is null)
        {
            _logger.ZLogWarning($"보스러시 라운드 보고 정합성 실패: userId {userId:@UserId} runId {request.runId:@RunId} clearMs {request.clearMs:@ClearMs} — 클라이언트 보고값이 앞뒤가 맞지 않습니다.");
            return new SaveResult(ErrorCode.BossRushInvalidProgress, string.Empty, null);
        }

        var bossByRound = rounds.ToDictionary(r => r.Round, r => r.BossMonsterCode);
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var outcome = await _repository.ApplyClearAsync(
            userId, request.runId, request.clearMs, roundTimes, bossByRound, rule.RunLifetimeMs, nowMs);

        switch (outcome.Status)
        {
            case BossRushClearStatus.RunNotFound:
                return new SaveResult(ErrorCode.BossRushRunNotFound, string.Empty, null);
            case BossRushClearStatus.AlreadyFinished:
                return new SaveResult(ErrorCode.BossRushRunAlreadyFinished, string.Empty, null);
        }

        // 커밋 이후에만 랭킹 캐시를 갱신한다 — Redis에는 롤백이 없다(§6.2).
        var rank = 0;
        if (outcome.RankEligible)
        {
            if (outcome.IsNewRecord)
            {
                await _rankCache.UpsertAsync(outcome.SeasonId, userId, outcome.BestClearMs, outcome.RecordedAt);
            }

            // 기록을 갱신하지 못했어도 순위는 내려준다 — 다른 유저가 올라와 순위가 밀렸을 수 있다.
            rank = await _rankCache.GetRankAsync(outcome.SeasonId, userId) ?? 0;
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

        return new SaveResult(ErrorCode.Success, "BossRushCleared", data);
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
        var safeLimit = limit <= 0 ? RankDefaultLimit : Math.Min(limit, rule.RankPageLimit);

        var source = BossRushRankSource.RankCache;
        var cached = await _rankCache.GetPageAsync(season.SeasonId, safeOffset, safeLimit);
        var total = await _rankCache.CountAsync(season.SeasonId);

        List<BossRushRankRow> rows;
        if (cached is null || total is null)
        {
            source = BossRushRankSource.Database;
            var closed = season.Status == (int)BossRushSeasonStatus.Closed;
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
            entries = rows.Select(r => ToEntryDto(r, nicknames)).ToList(),
        };

        return new SaveResult(ErrorCode.Success, "OK", data);
    }

    /// <summary>
    /// 요청자 본인의 시즌 순위 1건을 반환한다(§5.5). 랭킹 UI의 고정 영역이 쓰는 값이라 목록 페이지를
    /// 넘기는 동안 다시 호출할 필요가 없다. 기록이 없으면 <c>myRank</c>가 null이다.
    /// <para>시즌 메타(seasonStatus·seasonEndAt)는 담지 않는다 — info·rank가 이미 내려준다.</para>
    /// </summary>
    public async Task<SaveResult> GetMyRankAsync(long userId, int seasonId)
    {
        var season = await ResolveSeasonAsync(seasonId);
        if (season is null)
        {
            return new SaveResult(ErrorCode.BossRushSeasonClosed, string.Empty, null);
        }

        var source = BossRushRankSource.RankCache;
        var cachedEntry = await _rankCache.GetMyEntryAsync(season.SeasonId, userId);
        var total = await _rankCache.CountAsync(season.SeasonId);

        BossRushRankRow? row;
        if (total is null)
        {
            source = BossRushRankSource.Database;
            var closed = season.Status == (int)BossRushSeasonStatus.Closed;
            row = await _repository.GetMyRankAsync(season.SeasonId, userId, closed);
            total = await _repository.CountEntriesAsync(season.SeasonId);
        }
        else if (cachedEntry is null)
        {
            // 캐시는 살아 있는데 내가 없다 = 그 시즌에 기록이 없다(폴백 대상이 아니다).
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
            myRank = row is null ? null : ToEntryDto(row, nicknames),
        };

        return new SaveResult(ErrorCode.Success, "OK", data);
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

    /// <summary>KST 자정 경계로 "오늘"의 [시작, 다음날 시작) 유닉스초 범위를 구한다(일일 횟수 집계 기준).</summary>
    private static (long TodayStart, long TomorrowStart) KstDayRange(DateTimeOffset utcNow)
    {
        var kstNow = utcNow.ToOffset(KstOffset);
        var todayStart = new DateTimeOffset(kstNow.Year, kstNow.Month, kstNow.Day, 0, 0, 0, KstOffset);
        return (todayStart.ToUnixTimeSeconds(), todayStart.AddDays(1).ToUnixTimeSeconds());
    }

    /// <summary>시즌 레코드를 응답 DTO로 변환한다.</summary>
    private static BossRushSeasonDto ToSeasonDto(BossRushSeason season)
        => new()
        {
            seasonId = season.SeasonId,
            startAt = season.StartAt,
            endAt = season.EndAt,
            status = season.Status,
        };

    /// <summary>
    /// 라운드 정의를 응답 DTO로 변환한다 — 서버는 몬스터 코드와 <b>등장 레벨</b>만 내려주고 스탯은 담지 않는다
    /// (클라이언트가 monster_master의 레벨 1 기준값에 레벨 배율을 곱해 산출한다).
    /// </summary>
    private static BossRushRoundDto ToRoundDto(BossRushRoundDef def)
        => new()
        {
            round = def.Round,
            backgroundType = def.BackgroundType,
            monsters = def.Spawns.Select(s => new BossRushSpawnDto
            {
                monsterCode = s.MonsterCode,
                monsterLevel = s.MonsterLevel,
                count = s.Count,
            }).ToList(),
            boss = def.BossMonsterCode == 0
                ? null
                : new BossRushBossDto
                {
                    monsterCode = def.BossMonsterCode,
                    monsterLevel = def.BossMonsterLevel,
                },
        };

    /// <summary>랭킹 1행을 응답 DTO로 변환한다. 닉네임을 찾지 못하면 빈 문자열로 둔다(순위 표시는 유지).</summary>
    private static BossRushRankEntryDto ToEntryDto(
        BossRushRankRow row, IReadOnlyDictionary<long, string> nicknames)
        => new()
        {
            rank = row.Rank,
            userId = row.UserId,
            nickname = nicknames.TryGetValue(row.UserId, out var name) ? name : string.Empty,
            clearMs = row.ClearMs,
            recordedAt = row.RecordedAt,
        };
}
