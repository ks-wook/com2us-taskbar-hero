using GameServer.Repositories;

namespace GameServer.Repositories.Interfaces;

public interface IBossRushRepository
{
    /// <summary>진행 중(status=1) 시즌을 조회한다. 정산 중이거나 시즌이 없으면 null.</summary>
    Task<BossRushSeason?> GetRunningSeasonAsync();

    /// <summary>시즌 1건을 조회한다(종료 시즌 메타 조회용). 없으면 null.</summary>
    Task<BossRushSeason?> GetSeasonAsync(int seasonId);

    /// <summary>정보 조회용 스냅샷(진행도·오늘 사용 횟수·진행 중 런·내 최고 기록). 세이브가 없으면 null.</summary>
    Task<BossRushInfoSnapshot?> GetInfoSnapshotAsync(long userId, int seasonId, long todayStartUnix, long tomorrowStartUnix);

    /// <summary>
    /// 도전 시작을 한 트랜잭션으로 적용한다(기획서 6.1): 진행도 확인 → 해금 검증 → 진행 중 시즌 확인 →
    /// 오늘 사용 횟수 검증 → 진행 중 런 자동 만료 종결 → 새 런 INSERT.
    /// <b>일일 횟수 차감의 정본은 런 INSERT 그 자체</b>이며, 카운트와 INSERT가 같은 트랜잭션·같은 user_id
    /// 잠금 안에 있어 동시 요청이 한도를 넘기지 못한다.
    /// </summary>
    Task<BossRushEnterOutcome> ApplyEnterAsync(
        long userId, int unlockStageSequence, int dailyEntryLimit,
        long todayStartUnix, long tomorrowStartUnix, long nowMs);

    /// <summary>
    /// 클리어 보고를 한 트랜잭션으로 적용한다(기획서 6.2): 런 행 잠금 → 만료 판정(lazy) → 조건부 종결 →
    /// 라운드 기록 INSERT → 시즌 최고 기록 조건부 UPSERT. 보상 지급은 없다.
    /// </summary>
    /// <param name="roundTimes">라운드 번호 → 소요(ms). 형식 검증은 서비스가 이미 마쳤다.</param>
    Task<BossRushClearOutcome> ApplyClearAsync(
        long userId, long runId, int clearMs, IReadOnlyList<(int Round, int ElapsedMs)> roundTimes,
        long runLifetimeMs, long nowMs);

    /// <summary>시즌 등재 인원(ZCARD 폴백).</summary>
    Task<int> CountEntriesAsync(int seasonId);

    /// <summary>
    /// 랭킹 목록 한 페이지를 MySQL에서 읽는다. 종료 시즌(<paramref name="useFinalRank"/> true)은 정산이
    /// 확정한 final_rank로 그대로 읽고, 진행 중 시즌은 (best_clear_ms, recorded_at) 정렬로 순위를 매긴다.
    /// </summary>
    Task<IReadOnlyList<BossRushRankRow>> GetRankPageAsync(int seasonId, int offset, int limit, bool useFinalRank);

    /// <summary>본인 순위 1건을 MySQL에서 계산한다(폴백). 기록이 없으면 null.</summary>
    Task<BossRushRankRow?> GetMyRankAsync(int seasonId, long userId, bool useFinalRank);

    /// <summary>지정 userId들의 닉네임을 조회한다(랭킹 표시 이름 백필용).</summary>
    Task<IReadOnlyDictionary<long, string>> GetNicknamesAsync(IReadOnlyCollection<long> userIds);

    /// <summary>랭킹 캐시 워밍업용 — 시즌 기록을 정렬 순서로 페이지 단위 스캔한다.</summary>
    Task<IReadOnlyList<BossRushSettleTarget>> ScanRecordsAsync(int seasonId, int offset, int limit);

    // ── 시즌 정산 배치(6.4) ──

    /// <summary>정산 대상 시즌을 조건부 갱신으로 선점한다(status 1 → 2). 선점하지 못하면 null.</summary>
    Task<BossRushSeason?> ClaimSeasonForSettlementAsync(long nowUnix);

    /// <summary>순위가 아직 확정되지 않은 기록을 정렬 순서로 상한까지 읽는다(정산 대상).</summary>
    Task<IReadOnlyList<BossRushSettleTarget>> GetUnsettledRecordsAsync(int seasonId, int limit);

    /// <summary>해당 시즌에서 이미 순위가 확정된 기록 수(다음 페이지의 시작 순위를 잇는 데 쓴다).</summary>
    Task<int> CountSettledAsync(int seasonId);

    /// <summary>
    /// 기록 1건의 순위를 확정하고 보상 메일을 발급한다(같은 트랜잭션, 멱등). final_rank가 0일 때만
    /// 전이하므로 재진입 시 이미 처리한 행은 0행이 되어 스킵된다.
    /// </summary>
    /// <param name="rewardMail">발급할 순위 보상 메일 초안. null이면 보상 없이 순위만 확정한다.</param>
    Task<bool> SettleRecordAsync(
        int seasonId, long userId, int finalRank, MailDraft? rewardMail, long nowUnix);

    /// <summary>시즌을 종료 처리한다(status → 3, settled_at 기록).</summary>
    Task CloseSeasonAsync(int seasonId, long nowUnix);

    /// <summary>다음 시즌을 개시한다(start_at 유니크로 중복 삽입 방지). 이미 있으면 기존 시즌을 돌려준다.</summary>
    Task<BossRushSeason> StartNextSeasonAsync(long startAt, long endAt);
}
