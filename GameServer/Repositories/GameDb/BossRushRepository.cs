using GameServer.Models;
using GameServer.Repositories.GameDb.Interfaces;
using MySqlConnector;
using SqlKata.Execution;
using TaskbarHero.Common;

namespace GameServer.Repositories.GameDb;

/// <summary>진행 중인 도전 런(만료 판정 전 원본). 만료 여부는 호출측이 started_at 나이로 판단한다.</summary>
public sealed record BossRushActiveRun(long RunId, int SeasonId, long StartedAtMs);

/// <summary>시즌 개인 최고 기록(boss_rush_record). 기록이 없으면 null로 다룬다.</summary>
public sealed record BossRushRecord(int BestClearMs, long RecordedAt);

/// <summary>보스러시 정보 조회용 스냅샷 — 진행도·진행 중 런·내 최고 기록을 한 번에 읽는다.</summary>
public sealed record BossRushInfoSnapshot(
    int MaxStageCleared, BossRushActiveRun? ActiveRun, BossRushRecord? MyRecord);

/// <summary>도전 시작 트랜잭션 결과. 성공 시 RunId·SeasonId를 담는다(차감할 횟수가 없다).</summary>
public sealed record BossRushEnterOutcome(
    BossRushEnterStatus Status, long RunId, int SeasonId)
{
    public static BossRushEnterOutcome Fail(BossRushEnterStatus status) => new(status, 0, 0);
}

/// <summary>
/// 클리어 보고 트랜잭션 결과. IsNewRecord = 이번 보고가 시즌 최고를 갱신했는지,
/// BestClearMs = 갱신 후 최고 기록, RecordedAt = 그 기록의 달성 시각(랭킹 tie-break 축).
/// <para>RankEligible이 false면 런의 시즌이 더 이상 진행 중이 아니어서 기록 등재를 생략한 경우다(6.5).</para>
/// <para>SeasonStartAt은 랭킹 점수 인코딩의 기준점이다 — 점수의 tie-break 자리가 시즌 시작 기준
/// 상대 초라(4.3) 커밋 이후 ZADD에 시즌 시작 시각이 함께 필요하다.</para>
/// </summary>
public sealed record BossRushClearOutcome(
    BossRushClearStatus Status, int SeasonId, long SeasonStartAt,
    bool IsNewRecord, int BestClearMs, long RecordedAt, bool RankEligible)
{
    public static BossRushClearOutcome Fail(BossRushClearStatus status) => new(status, 0, 0, false, 0, 0, false);
}

/// <summary>랭킹 목록 1행(MySQL 폴백·종료 시즌 조회 결과). 닉네임은 별도 조회로 채운다.</summary>
public sealed record BossRushRankRow(int Rank, long UserId, int ClearMs, long RecordedAt);

/// <summary>
/// 순위 확정 1건의 결과. <paramref name="Applied"/>가 false면 이미 확정된 행이라 아무것도 하지 않았다는 뜻이고,
/// <paramref name="RewardMailId"/>는 이때 발급한 순위 보상 메일 id(보상 구간 밖이면 0)다 —
/// 커밋 이후 발급 이벤트 로그(<c>mail.issue</c>)가 쓴다(5.8).
/// </summary>
public sealed record SettleRecordOutcome(bool Applied, long RewardMailId);

/// <summary>시즌 정산 대상 1건(순위 미확정 기록).</summary>
public sealed record BossRushSettleTarget(long UserId, int BestClearMs, long RecordedAt);

/// <summary>
/// 보스러시 세이브 접근 계층(taskbar_hero_game). SqlKata 쿼리 빌더 + 제네릭 매핑만 사용한다(dynamic 금지).
/// 랭킹 캐시(Redis)는 <see cref="GameServer.Repositories.MemoryDb.Interfaces.IBossRushRankCache"/>가 담당하고 이 클래스는 MySQL 정본만 다룬다.
/// </summary>
public sealed class BossRushRepository : GameDbBase, IBossRushRepository
{
    /// <summary>세이브 DB 커넥션 팩토리를 기반 클래스로 전달한다.</summary>
    public BossRushRepository(GameDbFactory dbFactory) : base(dbFactory) { }

    /// <summary>진행 중(status=1) 시즌 1행을 읽는다. 정산 중이면 없는 것으로 다룬다.</summary>
    public async Task<BossRushSeason?> GetRunningSeasonAsync()
    {
        using var db = Db();
        var row = await db.Query("boss_rush_season")
            .Select("season_id", "start_at", "end_at", "status")
            .Where("status", (int)BossRushSeasonStatus.Running)
            .OrderByDesc("season_id")
            .FirstOrDefaultAsync<BossRushSeasonRow>();

        return row is null ? null : new BossRushSeason(row.SeasonId, row.StartAt, row.EndAt, row.Status);
    }

    /// <summary>지정 시즌 1행을 읽는다(종료 시즌 조회 포함). 없으면 null.</summary>
    public async Task<BossRushSeason?> GetSeasonAsync(int seasonId)
    {
        using var db = Db();
        var row = await db.Query("boss_rush_season")
            .Select("season_id", "start_at", "end_at", "status")
            .Where("season_id", seasonId)
            .FirstOrDefaultAsync<BossRushSeasonRow>();

        return row is null ? null : new BossRushSeason(row.SeasonId, row.StartAt, row.EndAt, row.Status);
    }

    /// <summary>
    /// 정보 조회에 필요한 값을 한 커넥션에서 읽는다 — 진행도(max_stage_cleared), 진행 중 런,
    /// 현 시즌 개인 최고 기록. 세이브 행이 없으면 null(계정 미생성).
    /// </summary>
    public async Task<BossRushInfoSnapshot?> GetInfoSnapshotAsync(long userId, int seasonId)
    {
        using var db = Db();

        var player = await db.Query("game_player")
            .Select("max_stage_cleared")
            .Where("user_id", userId)
            .FirstOrDefaultAsync<BossRushPlayerRow>();
        if (player is null)
        {
            return null;
        }

        var activeRow = await db.Query("boss_rush_run")
            .Select("run_id", "season_id", "started_at")
            .Where("user_id", userId)
            .Where("status", (int)BossRushRunStatus.Running)
            .OrderByDesc("run_id")
            .FirstOrDefaultAsync<BossRushRunRow>();

        var recordRow = seasonId <= 0
            ? null
            : await db.Query("boss_rush_record")
                .Select("best_clear_ms", "recorded_at")
                .Where("season_id", seasonId)
                .Where("user_id", userId)
                .FirstOrDefaultAsync<BossRushRecordRow>();

        return new BossRushInfoSnapshot(
            player.MaxStageCleared,
            activeRow is null ? null : new BossRushActiveRun(activeRow.RunId, activeRow.SeasonId, activeRow.StartedAt),
            recordRow is null ? null : new BossRushRecord(recordRow.BestClearMs, recordRow.RecordedAt));
    }

    /// <summary>
    /// 도전 시작을 단일 트랜잭션으로 적용한다. 검증 실패는 즉시 롤백 후 Fail로 반환하고, 예외는 롤백 후 전파한다.
    /// </summary>
    /// <remarks>
    /// 한 트랜잭션으로 묶는 작업:
    /// <para>1) game_player SELECT ... FOR UPDATE — 진행도 관측 + user_id 잠금(동시 요청 직렬화)</para>
    /// <para>2) 해금 검증 — max_stage_cleared &gt;= unlock_stage_sequence</para>
    /// <para>3) 진행 중 시즌 확인 — 없으면(정산 중) SeasonClosed</para>
    /// <para>4) 진행 중 런 자동 만료 종결 — 나이와 무관하게 정리(보상 없음)</para>
    /// <para>5) boss_rush_run INSERT — started_at은 서버 시각(ms)</para>
    /// <para>도전 횟수 제한이 없어 세거나 차감하는 단계가 없다. 그래도 user_id 잠금은 유지한다 —
    /// 4)와 5)가 같은 잠금 안에 있어야 동시 enter 두 건이 진행 중 런을 두 개 만들지 않는다.</para>
    /// </remarks>
    public async Task<BossRushEnterOutcome> ApplyEnterAsync(
        long userId, int unlockStageSequence, long nowMs)
        => await TransactionAsync<BossRushEnterOutcome>(async (db, transaction) =>
        {
            // 1) 진행도 관측 + user_id 행 잠금.
            var maxStageCleared = await LockPlayerMaxStageAsync(transaction, userId);
            if (maxStageCleared is null)
            {
                return TxResult<BossRushEnterOutcome>.Rollback(BossRushEnterOutcome.Fail(BossRushEnterStatus.NoPlayer));
            }

            // 2) 해금 검증.
            if (maxStageCleared.Value < unlockStageSequence)
            {
                return TxResult<BossRushEnterOutcome>.Rollback(BossRushEnterOutcome.Fail(BossRushEnterStatus.Locked));
            }

            // 3) 진행 중 시즌. enter·clear는 캐시가 아니라 정본을 직접 읽는다(4.3).
            var season = await db.Query("boss_rush_season")
                .Select("season_id", "start_at", "end_at", "status")
                .Where("status", (int)BossRushSeasonStatus.Running)
                .OrderByDesc("season_id")
                .FirstOrDefaultAsync<BossRushSeasonRow>(transaction);
            if (season is null)
            {
                return TxResult<BossRushEnterOutcome>.Rollback(BossRushEnterOutcome.Fail(BossRushEnterStatus.SeasonClosed));
            }

            // 4) 남아 있는 진행 중 런을 정리한다(나이와 무관). 방치형 클라이언트의 강제 종료를
            //    유저가 스스로 복구할 수 있게 하는 장치다.
            await db.Query("boss_rush_run")
                .Where("user_id", userId)
                .Where("status", (int)BossRushRunStatus.Running)
                .UpdateAsync(
                    new { status = (int)BossRushRunStatus.Expired, finished_at = nowMs },
                    transaction);

            // 5) 새 런 개시.
            var runId = await db.Query("boss_rush_run").InsertGetIdAsync<long>(new
            {
                user_id = userId,
                season_id = season.SeasonId,
                started_at = nowMs,
                finished_at = 0L,
                status = (int)BossRushRunStatus.Running,
                clear_ms = 0,
            }, transaction);

            return TxResult<BossRushEnterOutcome>.Commit(new BossRushEnterOutcome(BossRushEnterStatus.Ok, runId, season.SeasonId));
        });

    /// <summary>
    /// 클리어 보고를 단일 트랜잭션으로 적용한다. 만료 판정은 이 경로가 하며(lazy 만료), 만료된 런은
    /// 그 자리에서 status=3으로 종결하고 AlreadyFinished로 거부한다.
    /// </summary>
    /// <remarks>
    /// <para>1) boss_rush_run SELECT ... FOR UPDATE — 소유자·상태 확인</para>
    /// <para>2) 만료 판정 — started_at + runLifetimeMs &lt; now면 status=3으로 종결 후 거부</para>
    /// <para>3) 조건부 종결 — status=1일 때만 전이(0행이면 동시 요청의 패자)</para>
    /// <para>4) boss_rush_run_round INSERT — 라운드별 소요(클라 측정)</para>
    /// <para>5) boss_rush_record 조건부 UPSERT — 기록이 개선된 경우만. 런의 시즌이 진행 중이 아니면 생략</para>
    /// </remarks>
    public async Task<BossRushClearOutcome> ApplyClearAsync(
        long userId, long runId, int clearMs, IReadOnlyList<(int Round, int ElapsedMs)> roundTimes,
        long runLifetimeMs, long nowMs)
        => await TransactionAsync<BossRushClearOutcome>(async (db, transaction) =>
        {
            // 1) 런 행 잠금 + 소유자·상태 확인.
            var locked = await LockRunAsync(transaction, runId);
            if (locked is null || locked.Value.UserId != userId)
            {
                return TxResult<BossRushClearOutcome>.Rollback(BossRushClearOutcome.Fail(BossRushClearStatus.RunNotFound));
            }

            var run = locked.Value;
            if (run.Status != (int)BossRushRunStatus.Running)
            {
                // 거부지만 시즌 id는 담아 돌려준다 — 이 보고가 어느 시즌 것이었는지가 로그의 축이다(5.10).
                return TxResult<BossRushClearOutcome>.Rollback(new BossRushClearOutcome(
                    BossRushClearStatus.AlreadyFinished, run.SeasonId, 0, false, 0, 0, false));
            }

            // 2) 만료 판정(읽는 시점). 정리 배치를 두지 않고 이 경로가 status를 확정한다(6.2).
            if (run.StartedAt + runLifetimeMs < nowMs)
            {
                await db.Query("boss_rush_run")
                    .Where("run_id", runId)
                    .Where("status", (int)BossRushRunStatus.Running)
                    .UpdateAsync(
                        new { status = (int)BossRushRunStatus.Expired, finished_at = nowMs },
                        transaction);
                return TxResult<BossRushClearOutcome>.Commit(new BossRushClearOutcome(
                    BossRushClearStatus.AlreadyFinished, run.SeasonId, 0, false, 0, 0, false));
            }

            // 3) 조건부 종결 — 동시 중복 보고는 여기서 0행이 되어 갈린다.
            var closed = await db.Query("boss_rush_run")
                .Where("run_id", runId)
                .Where("status", (int)BossRushRunStatus.Running)
                .UpdateAsync(
                    new
                    {
                        status = (int)BossRushRunStatus.Cleared,
                        finished_at = nowMs,
                        clear_ms = clearMs,
                    },
                    transaction);
            if (closed == 0)
            {
                return TxResult<BossRushClearOutcome>.Rollback(BossRushClearOutcome.Fail(BossRushClearStatus.AlreadyFinished));
            }

            // 4) 라운드별 소요 기록(클라 측정). 몬스터 구성은 복사하지 않는다 — round로 마스터를 찾으면 된다.
            foreach (var (round, elapsedMs) in roundTimes)
            {
                await db.Query("boss_rush_run_round").InsertAsync(new
                {
                    run_id = runId,
                    round,
                    elapsed_ms = elapsedMs,
                }, transaction);
            }

            // 5) 시즌 최고 기록 갱신. 런의 시즌이 더 이상 진행 중이 아니면 등재를 생략한다(6.5).
            //    start_at도 함께 읽는다 — 랭킹 점수의 tie-break 자리가 시즌 상대 초라(4.3)
            //    커밋 이후 ZADD가 이 값을 필요로 한다.
            var seasonRow = await db.Query("boss_rush_season")
                .Select("season_id", "start_at", "end_at", "status")
                .Where("season_id", run.SeasonId)
                .FirstOrDefaultAsync<BossRushSeasonRow>(transaction);

            var rankEligible = seasonRow?.Status == (int)BossRushSeasonStatus.Running;
            var isNewRecord = false;
            var bestClearMs = clearMs;
            var recordedAt = nowMs / 1000;

            if (rankEligible)
            {
                var existing = await db.Query("boss_rush_record")
                    .Select("best_clear_ms", "recorded_at")
                    .Where("season_id", run.SeasonId)
                    .Where("user_id", userId)
                    .FirstOrDefaultAsync<BossRushRecordRow>(transaction);

                if (existing is null)
                {
                    await db.Query("boss_rush_record").InsertAsync(new
                    {
                        season_id = run.SeasonId,
                        user_id = userId,
                        best_clear_ms = clearMs,
                        best_run_id = runId,
                        recorded_at = recordedAt,
                        final_rank = 0,
                        rank_reward_mail_id = 0L,
                    }, transaction);
                    isNewRecord = true;
                }
                else if (clearMs < existing.BestClearMs)
                {
                    // 조건부 갱신 — 관측 이후 더 좋은 기록이 들어왔으면 0행이 되어 덮어쓰지 않는다.
                    var updated = await db.Query("boss_rush_record")
                        .Where("season_id", run.SeasonId)
                        .Where("user_id", userId)
                        .Where("best_clear_ms", ">", clearMs)
                        .UpdateAsync(
                            new { best_clear_ms = clearMs, best_run_id = runId, recorded_at = recordedAt },
                            transaction);
                    isNewRecord = updated > 0;
                    if (!isNewRecord)
                    {
                        bestClearMs = existing.BestClearMs;
                        recordedAt = existing.RecordedAt;
                    }
                }
                else
                {
                    bestClearMs = existing.BestClearMs;
                    recordedAt = existing.RecordedAt;
                }
            }

            return TxResult<BossRushClearOutcome>.Commit(new BossRushClearOutcome(
                BossRushClearStatus.Ok, run.SeasonId, seasonRow?.StartAt ?? 0,
                isNewRecord, bestClearMs, recordedAt, rankEligible));
        });

    /// <summary>시즌 등재 인원을 센다(랭킹 캐시를 쓸 수 없을 때의 totalEntries).</summary>
    public async Task<int> CountEntriesAsync(int seasonId)
    {
        using var db = Db();
        return await db.Query("boss_rush_record").Where("season_id", seasonId).CountAsync<int>();
    }

    /// <summary>
    /// 랭킹 목록 한 페이지를 읽는다. 종료 시즌은 정산이 확정한 final_rank를 그대로 쓰므로 순위를 재계산하지
    /// 않고, 진행 중 시즌은 (best_clear_ms, recorded_at) 정렬 위치로 순위를 매긴다(offset + 인덱스 + 1).
    /// </summary>
    public async Task<IReadOnlyList<BossRushRankRow>> GetRankPageAsync(
        int seasonId, int offset, int limit, bool useFinalRank)
    {
        using var db = Db();

        var query = db.Query("boss_rush_record")
            .Select("user_id", "best_clear_ms", "recorded_at", "final_rank")
            .Where("season_id", seasonId);

        query = useFinalRank
            ? query.Where("final_rank", ">", 0).OrderBy("final_rank")
            : query.OrderBy("best_clear_ms", "recorded_at");

        var rows = await query.Offset(offset).Limit(limit).GetAsync<BossRushRankRecordRow>();

        var result = new List<BossRushRankRow>();
        var index = 0;
        foreach (var row in rows)
        {
            var rank = useFinalRank ? row.FinalRank : offset + index + 1;
            result.Add(new BossRushRankRow(rank, row.UserId, row.BestClearMs, row.RecordedAt));
            index++;
        }

        return result;
    }

    /// <summary>
    /// 본인 순위를 MySQL에서 계산한다(폴백). 종료 시즌은 final_rank를 그대로 쓰고, 진행 중 시즌은
    /// "나보다 앞선 기록 수 + 1"로 센다 — 동점은 recorded_at이 작은 쪽이 앞이라는 규칙을 그대로 반영한다.
    /// </summary>
    public async Task<BossRushRankRow?> GetMyRankAsync(int seasonId, long userId, bool useFinalRank)
    {
        using var db = Db();

        var mine = await db.Query("boss_rush_record")
            .Select("user_id", "best_clear_ms", "recorded_at", "final_rank")
            .Where("season_id", seasonId)
            .Where("user_id", userId)
            .FirstOrDefaultAsync<BossRushRankRecordRow>();
        if (mine is null)
        {
            return null;
        }

        if (useFinalRank && mine.FinalRank > 0)
        {
            return new BossRushRankRow(mine.FinalRank, mine.UserId, mine.BestClearMs, mine.RecordedAt);
        }

        // (best_clear_ms, recorded_at) 사전식 비교 — 더 빠르거나, 같은 기록을 더 먼저 세운 행이 앞선다.
        var ahead = await db.Query("boss_rush_record")
            .Where("season_id", seasonId)
            .Where(q => q
                .Where("best_clear_ms", "<", mine.BestClearMs)
                .OrWhere(inner => inner
                    .Where("best_clear_ms", mine.BestClearMs)
                    .Where("recorded_at", "<", mine.RecordedAt)))
            .CountAsync<int>();

        return new BossRushRankRow(ahead + 1, mine.UserId, mine.BestClearMs, mine.RecordedAt);
    }

    /// <summary>지정 userId들의 닉네임을 한 번에 읽는다(랭킹 표시 이름 백필용).</summary>
    public async Task<IReadOnlyDictionary<long, string>> GetNicknamesAsync(IReadOnlyCollection<long> userIds)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<long, string>();
        }

        using var db = Db();
        var rows = await db.Query("game_player")
            .Select("user_id", "nickname")
            .WhereIn("user_id", userIds)
            .GetAsync<BossRushNicknameRow>();

        var map = new Dictionary<long, string>();
        foreach (var row in rows)
        {
            map[row.UserId] = row.Nickname ?? string.Empty;
        }

        return map;
    }

    /// <summary>시즌 기록을 정렬 순서로 페이지 단위 스캔한다(랭킹 캐시 워밍업용).</summary>
    public async Task<IReadOnlyList<BossRushSettleTarget>> ScanRecordsAsync(int seasonId, int offset, int limit)
    {
        using var db = Db();
        var rows = await db.Query("boss_rush_record")
            .Select("user_id", "best_clear_ms", "recorded_at")
            .Where("season_id", seasonId)
            .OrderBy("best_clear_ms", "recorded_at")
            .Offset(offset)
            .Limit(limit)
            .GetAsync<BossRushSettleRow>();

        return rows.Select(r => new BossRushSettleTarget(r.UserId, r.BestClearMs, r.RecordedAt)).ToList();
    }

    /// <summary>
    /// 정산 대상 시즌을 조건부 갱신으로 선점한다(status 1 → 2, end_at 경과분만). 0행이면 다른 인스턴스가
    /// 이미 선점했거나 정산할 시즌이 없다.
    /// </summary>
    public async Task<BossRushSeason?> ClaimSeasonForSettlementAsync(long nowUnix)
    {
        using var db = Db();

        var target = await db.Query("boss_rush_season")
            .Select("season_id", "start_at", "end_at", "status")
            .Where("status", (int)BossRushSeasonStatus.Running)
            .Where("end_at", "<=", nowUnix)
            .OrderBy("season_id")
            .FirstOrDefaultAsync<BossRushSeasonRow>();
        if (target is null)
        {
            return null;
        }

        var claimed = await db.Query("boss_rush_season")
            .Where("season_id", target.SeasonId)
            .Where("status", (int)BossRushSeasonStatus.Running)
            .UpdateAsync(new { status = (int)BossRushSeasonStatus.Settling });

        return claimed == 0
            ? null
            : new BossRushSeason(target.SeasonId, target.StartAt, target.EndAt, (int)BossRushSeasonStatus.Settling);
    }

    /// <summary>순위 미확정(final_rank=0) 기록을 정렬 순서로 상한까지 읽는다.</summary>
    public async Task<IReadOnlyList<BossRushSettleTarget>> GetUnsettledRecordsAsync(int seasonId, int limit)
    {
        using var db = Db();
        var rows = await db.Query("boss_rush_record")
            .Select("user_id", "best_clear_ms", "recorded_at")
            .Where("season_id", seasonId)
            .Where("final_rank", 0)
            .OrderBy("best_clear_ms", "recorded_at")
            .Limit(limit)
            .GetAsync<BossRushSettleRow>();

        return rows.Select(r => new BossRushSettleTarget(r.UserId, r.BestClearMs, r.RecordedAt)).ToList();
    }

    /// <summary>이미 순위가 확정된 기록 수(정산 재진입 시 다음 페이지의 시작 순위를 잇는 데 쓴다).</summary>
    public async Task<int> CountSettledAsync(int seasonId)
    {
        using var db = Db();
        return await db.Query("boss_rush_record")
            .Where("season_id", seasonId)
            .Where("final_rank", ">", 0)
            .CountAsync<int>();
    }

    /// <summary>
    /// 기록 1건의 순위를 확정하고(final_rank) 보상 메일을 같은 트랜잭션에서 발급한다. final_rank=0 조건부
    /// 갱신이라 재진입 시 이미 처리한 행은 0행이 되어 스킵된다 — "보상은 갔는데 순위 기록이 없음"이 생기지 않는다.
    /// </summary>
    public async Task<SettleRecordOutcome> SettleRecordAsync(
        int seasonId, long userId, int finalRank, MailDraft? rewardMail, long nowUnix)
        => await TransactionAsync<SettleRecordOutcome>(async (db, transaction) =>
        {
            var mailId = rewardMail is null
                ? 0L
                : await MailRepository.InsertMailAsync(db, transaction, userId, rewardMail, nowUnix);

            var updated = await db.Query("boss_rush_record")
                .Where("season_id", seasonId)
                .Where("user_id", userId)
                .Where("final_rank", 0)
                .UpdateAsync(new { final_rank = finalRank, rank_reward_mail_id = mailId }, transaction);

            if (updated == 0)
            {
                // 이미 정산된 행 — 방금 만든 메일까지 함께 되돌린다(중복 발급 방지).
                return TxResult<SettleRecordOutcome>.Rollback(new SettleRecordOutcome(false, 0));
            }

            return TxResult<SettleRecordOutcome>.Commit(new SettleRecordOutcome(true, mailId));
        });

    /// <summary>시즌을 종료 처리한다(status → 3, settled_at 기록).</summary>
    public async Task CloseSeasonAsync(int seasonId, long nowUnix)
    {
        using var db = Db();
        await db.Query("boss_rush_season")
            .Where("season_id", seasonId)
            .UpdateAsync(new { status = (int)BossRushSeasonStatus.Closed, settled_at = nowUnix });
    }

    /// <summary>
    /// 다음 시즌을 개시한다. start_at 유니크 제약이 중복 삽입을 막으므로, 이미 있으면 그 행을 읽어 돌려준다
    /// (배치가 여러 인스턴스에서 돌아도 시즌이 두 개 생기지 않는다).
    /// </summary>
    public async Task<BossRushSeason> StartNextSeasonAsync(long startAt, long endAt)
    {
        using var db = Db();

        var existing = await db.Query("boss_rush_season")
            .Select("season_id", "start_at", "end_at", "status")
            .Where("start_at", startAt)
            .FirstOrDefaultAsync<BossRushSeasonRow>();
        if (existing is not null)
        {
            return new BossRushSeason(existing.SeasonId, existing.StartAt, existing.EndAt, existing.Status);
        }

        try
        {
            var seasonId = await db.Query("boss_rush_season").InsertGetIdAsync<int>(new
            {
                start_at = startAt,
                end_at = endAt,
                status = (int)BossRushSeasonStatus.Running,
                settled_at = 0L,
            });

            return new BossRushSeason(seasonId, startAt, endAt, (int)BossRushSeasonStatus.Running);
        }
        catch (MySqlException ex) when (ex.Number == Constants.MySqlError.DuplicateEntry)
        {
            // 다른 인스턴스가 그 사이에 개시했다 — 그 시즌을 읽어 돌려준다.
            var raced = await db.Query("boss_rush_season")
                .Select("season_id", "start_at", "end_at", "status")
                .Where("start_at", startAt)
                .FirstOrDefaultAsync<BossRushSeasonRow>();
            return raced is null
                ? new BossRushSeason(0, startAt, endAt, (int)BossRushSeasonStatus.Running)
                : new BossRushSeason(raced.SeasonId, raced.StartAt, raced.EndAt, raced.Status);
        }
    }

    /// <summary>
    /// game_player 행을 잠그고 진행도(max_stage_cleared)를 읽는다(SELECT ... FOR UPDATE). 행이 없으면 null.
    /// 잠금이 필요해 SqlKata가 아닌 원시 커맨드를 쓰지만, 반환은 스칼라라 dynamic 매핑이 없다.
    /// </summary>
    private static async Task<int?> LockPlayerMaxStageAsync(MySqlTransaction transaction, long userId)
    {
        await using var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT max_stage_cleared FROM game_player WHERE user_id = @userId FOR UPDATE";
        command.Parameters.AddWithValue("@userId", userId);

        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? reader.GetInt32(0) : null;
    }

    /// <summary>boss_rush_run 행을 잠그고 읽는다(SELECT ... FOR UPDATE). 행이 없으면 null.</summary>
    private static async Task<(long UserId, int SeasonId, long StartedAt, int Status)?> LockRunAsync(
        MySqlTransaction transaction, long runId)
    {
        await using var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT user_id, season_id, started_at, status FROM boss_rush_run WHERE run_id = @runId FOR UPDATE";
        command.Parameters.AddWithValue("@runId", runId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return (reader.GetInt64(0), reader.GetInt32(1), reader.GetInt64(2), reader.GetInt32(3));
    }
}
