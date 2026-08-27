using CloudStructures;
using CloudStructures.Structures;
using GameServer.Repositories.MemoryDb.Interfaces;
using StackExchange.Redis;
using TaskbarHero.Common;
using GameServer.Models;

namespace GameServer.Repositories.MemoryDb;

/// <summary>랭킹 캐시에서 읽은 1행. 순위는 1부터이며 점수에서 기록·달성 시각을 복원한 값이다.</summary>
public sealed record BossRushCachedRank(int Rank, long UserId, int ClearMs, long RecordedAt);

/// <summary>
/// 보스러시 랭킹 조회용 Redis 계층(기획서 4.3). 키 5종을 다룬다 —
/// 리더보드 <c>rank:bossrush:{seasonId}</c>(Sorted Set), 그 리더보드의 적재 완료 마커
/// <c>rank:bossrush:{seasonId}:ready</c>와 재적재 락 <c>rank:bossrush:{seasonId}:rebuilding</c>(String),
/// 닉네임 캐시 <c>player:nickname</c>(Hash), 현재 시즌 메타 <c>bossrush:season:current</c>(Hash).
/// <para><b>리더보드 키의 존재는 신뢰의 근거가 되지 못한다</b> — 클리어 보고의 ZADD가 키를 새로 만들 수
/// 있어, Redis를 재기동한 뒤 한 명이 기록을 갱신하면 <b>그 한 명만 든 리더보드</b>가 생긴다. 비어 있는
/// 것보다 나쁜 상태다(틀린 순위표가 정상으로 보인다). 그래서 신뢰 판정은 <b>적재 완료 마커</b>가 맡고,
/// 마커는 워밍업이 시즌 기록을 전량 넣었을 때만 세워진다(<see cref="IsReadyAsync"/>).</para>
/// <para><b>정본이 아니다.</b> 리더보드는 MySQL <c>boss_rush_record</c>에서 파생된 조회 인덱스이고,
/// 닉네임·시즌 메타도 각각 <c>game_player.nickname</c>·<c>boss_rush_season</c>의 캐시다. 그래서 모든 접근을
/// <see cref="MemoryDbBase.SafeAsync{T}"/>로 감싸 실패 시 null·기본값을 돌려주고 호출측이 MySQL로 폴백한다
/// (축소 운전) — 예외를 밖으로 던지지 않는다.</para>
/// <para><b>점수 인코딩</b>: <c>score = clearMs × 10^7 + (recordedAt − season.start_at)(초)</c>.
/// 오름차순이 곧 순위이며 동점은 먼저 달성한 쪽이 상위다. tie-break 축을 유닉스초가 아니라
/// <b>시즌 시작 기준 상대 초</b>로 두는 이유는 리더보드 키가 시즌마다 분리돼 있어 한 키 안의 비교가
/// 전부 같은 시즌이기 때문이다 — 시즌 길이(7일 = 604,800초)가 10^7보다 한참 작아 하위 자리를 넘치지
/// 않는다. 그래서 배수가 10^7로 내려가 clearMs는 약 9.0 × 10^8 ms까지 안전하며(2^53 ≈ 9.007e15),
/// <b>이 인코딩은 더 이상 클리어 시간 상한에 기대지 않는다</b>(제한 시간을 없앤 근거, 기획서 4.3).</para>
/// </summary>
public sealed class BossRushRankCache : MemoryDbBase, IBossRushRankCache
{
    /// <summary>Redis 연결과 로거를 주입받는다.</summary>
    public BossRushRankCache(RedisConnection connection, ILogger<BossRushRankCache> logger)
        : base(connection, logger, "보스러시 랭킹 캐시")
    {
    }

    /// <summary>개인 최고 기록을 리더보드에 반영한다(ZADD). 실패는 Warning만 남기고 false를 돌려준다.</summary>
    public Task<bool> UpsertAsync(int seasonId, long seasonStartAt, long userId, int bestClearMs, long recordedAt)
        // 기록·보상은 이미 MySQL에 확정되어 있다 — 캐시 반영 실패는 순위 표시만 미룬다.
        => SafeAsync(async () =>
        {
            await Board(seasonId).AddAsync(Member(userId), Encode(bestClearMs, recordedAt, seasonStartAt));
            return true;
        }, false, "리더보드 갱신");

    /// <summary>ZRANK로 본인 순위를 얻는다(0-based → 1-based). 미등재·실패는 null.</summary>
    public Task<int?> GetRankAsync(int seasonId, long userId)
        => SafeAsync(async () =>
        {
            var rank = await Board(seasonId).RankAsync(Member(userId));
            return rank.HasValue ? (int)(rank.Value + 1) : (int?)null;
        }, null, "내 순위 조회");

    /// <summary>ZRANK + ZSCORE로 본인 순위 1행을 만든다. 미등재·실패는 null.</summary>
    public Task<BossRushCachedRank?> GetMyEntryAsync(int seasonId, long seasonStartAt, long userId)
        => SafeAsync(async () =>
        {
            var board = Board(seasonId);
            var member = Member(userId);
            var rank = await board.RankAsync(member);
            if (!rank.HasValue)
            {
                return null;
            }

            var score = await board.ScoreAsync(member);
            if (!score.HasValue)
            {
                return null;
            }

            var (clearMs, recordedAt) = Decode(score.Value, seasonStartAt);
            return new BossRushCachedRank((int)(rank.Value + 1), userId, clearMs, recordedAt);
        }, null, "내 순위 조회");

    /// <summary>ZCARD로 등재 인원을 센다. 실패는 null.</summary>
    public Task<int?> CountAsync(int seasonId)
        => SafeAsync(async () => (int?)(int)await Board(seasonId).LengthAsync(), null, "등재 인원 조회");

    /// <summary>
    /// ZRANGE로 한 페이지를 읽는다. Sorted Set은 skiplist의 span으로 시작 지점을 O(log N)에 찾으므로
    /// 오프셋이 깊어져도 비용이 반환 크기(M)에만 비례한다 — 그래서 순위 범위에 상한을 두지 않는다(4.3).
    /// </summary>
    public Task<IReadOnlyList<BossRushCachedRank>?> GetPageAsync(int seasonId, long seasonStartAt, int offset, int limit)
        => SafeAsync(async () =>
        {
            var entries = await Board(seasonId).RangeByRankWithScoresAsync(offset, offset + limit - 1);
            var result = new List<BossRushCachedRank>(entries.Length);
            for (var i = 0; i < entries.Length; i++)
            {
                var (clearMs, recordedAt) = Decode(entries[i].Score, seasonStartAt);
                result.Add(new BossRushCachedRank(
                    offset + i + 1, UserIdOf(entries[i].Value), clearMs, recordedAt));
            }

            return (IReadOnlyList<BossRushCachedRank>?)result;
        }, null, "랭킹 목록 조회");

    /// <summary>적재 완료 마커의 존재 여부. 없으면 false, Redis를 쓸 수 없으면 null.</summary>
    public Task<bool?> IsReadyAsync(int seasonId)
        => SafeAsync(async () => (bool?)await ReadyMarker(seasonId).ExistsAsync(), null, "리더보드 적재 상태 확인");

    /// <summary>적재 완료 마커를 세운다. TTL은 걸지 않는다 — 시즌 종료 시 리더보드와 함께 만료된다.</summary>
    public Task MarkReadyAsync(int seasonId)
        => SafeAsync(
            () => ReadyMarker(seasonId).SetAsync(Constants.BossRush.RankReadyMarkerValue),
            "리더보드 적재 완료 표시");

    /// <summary>적재 완료 마커를 지운다(재적재 시작·리더보드 반영 실패). 이후 조회는 정본으로 폴백한다.</summary>
    public Task ClearReadyAsync(int seasonId)
        => SafeAsync(() => ReadyMarker(seasonId).DeleteAsync(), "리더보드 적재 상태 무효화");

    /// <summary>리더보드를 통째로 비운다(재적재 직전). 정본에서 사라진 멤버를 남기지 않기 위해서다.</summary>
    public Task ClearBoardAsync(int seasonId)
        => SafeAsync(() => Board(seasonId).DeleteAsync(), "리더보드 비우기");

    /// <summary>
    /// SET NX로 자동 재적재 락을 잡는다. <b>실패를 false로 흡수한다</b> — Redis를 쓸 수 없으면 재적재도
    /// 못 하므로, 잡지 못한 것과 같게 다뤄 호출측이 조용히 넘어가게 한다.
    /// </summary>
    public Task<bool> TryAcquireRebuildLockAsync(int seasonId, TimeSpan ttl)
        => SafeAsync(
            () => RebuildLock(seasonId).SetAsync(Constants.BossRush.RankReadyMarkerValue, ttl, When.NotExists),
            false, "리더보드 재적재 락 획득");

    /// <summary>자동 재적재 락을 푼다. 실패해도 TTL로 만료되므로 Warning만 남긴다.</summary>
    public Task ReleaseRebuildLockAsync(int seasonId)
        => SafeAsync(() => RebuildLock(seasonId).DeleteAsync(), "리더보드 재적재 락 해제");

    /// <summary>
    /// 종료 시즌 리더보드에 TTL을 건다. 실패는 Warning만 남긴다(과거 키가 남아도 조회에 문제없다).
    /// <b>적재 완료 마커에도 같은 TTL을 건다</b> — 리더보드만 사라지고 마커가 남으면 그 시즌 조회가
    /// 빈 랭킹을 정상값으로 믿게 된다.
    /// </summary>
    public Task ExpireAsync(int seasonId, TimeSpan ttl)
        => SafeAsync(
            async () =>
            {
                await Board(seasonId).ExpireAsync(ttl);
                await ReadyMarker(seasonId).ExpireAsync(ttl);
            },
            "리더보드 TTL 설정");

    /// <summary>HMGET으로 닉네임을 읽는다. 미스는 결과에서 빠지고, 호출측이 MySQL에서 백필한다.</summary>
    public Task<IReadOnlyDictionary<long, string>> GetNicknamesAsync(IReadOnlyCollection<long> userIds)
    {
        if (userIds.Count == 0)
        {
            return Task.FromResult<IReadOnlyDictionary<long, string>>(new Dictionary<long, string>());
        }

        return SafeAsync(async () =>
        {
            var map = new Dictionary<long, string>();
            var fields = userIds.Select(id => id.ToString()).ToArray();
            var values = await Nicknames().GetAsync(fields);
            foreach (var pair in values)
            {
                if (long.TryParse(pair.Key, out var userId))
                {
                    map[userId] = pair.Value;
                }
            }

            return (IReadOnlyDictionary<long, string>)map;
        }, new Dictionary<long, string>(), "닉네임 캐시 조회");
    }

    /// <summary>HSET으로 닉네임 캐시를 채운다(lazy 백필). 실패해도 응답에는 영향이 없다.</summary>
    public Task SetNicknamesAsync(IReadOnlyDictionary<long, string> nicknames)
    {
        if (nicknames.Count == 0)
        {
            return Task.CompletedTask;
        }

        return SafeAsync(() => Nicknames().SetAsync(
            nicknames.ToDictionary(p => p.Key.ToString(), p => p.Value)), "닉네임 캐시 갱신");
    }

    /// <summary>현재 시즌 메타를 캐시에서 읽는다. 값이 없거나 실패면 null(호출측이 MySQL 폴백).</summary>
    public Task<BossRushSeason?> GetCurrentSeasonAsync()
        => SafeAsync(async () =>
        {
            var values = await CurrentSeason().GetAllAsync();
            if (values.Count == 0)
            {
                return null;
            }

            if (!values.TryGetValue("seasonId", out var seasonIdRaw) || !int.TryParse(seasonIdRaw, out var seasonId))
            {
                return null;
            }

            values.TryGetValue("startAt", out var startRaw);
            values.TryGetValue("endAt", out var endRaw);
            values.TryGetValue("status", out var statusRaw);

            return new BossRushSeason(
                seasonId,
                long.TryParse(startRaw, out var startAt) ? startAt : 0,
                long.TryParse(endRaw, out var endAt) ? endAt : 0,
                int.TryParse(statusRaw, out var status) ? status : (int)BossRushSeasonStatus.Running);
        }, null, "현재 시즌 캐시 조회");

    /// <summary>현재 시즌 메타 캐시를 갱신한다(정산 배치·기동 워밍업). 실패는 Warning만 남긴다.</summary>
    public Task SetCurrentSeasonAsync(BossRushSeason season)
        => SafeAsync(() => CurrentSeason().SetAsync(new Dictionary<string, string>
        {
            ["seasonId"] = season.SeasonId.ToString(),
            ["startAt"] = season.StartAt.ToString(),
            ["endAt"] = season.EndAt.ToString(),
            ["status"] = season.Status.ToString(),
        }), "현재 시즌 캐시 갱신");

    /// <summary>시즌 리더보드 구조체(member = userId).</summary>
    /// <summary>
    /// 리더보드 멤버 문자열. user_id를 <see cref="Constants.BossRush.RankMemberDigits"/>자리로 0을 채워 만든다 —
    /// 동점일 때 Redis가 멤버를 사전순으로 비교하므로, 자릿수를 맞춰야 그 순서가 user_id 오름차순이 되어
    /// MySQL의 동점 기준과 일치한다.
    /// </summary>
    private static string Member(long userId)
        => userId.ToString(new string('0', Constants.BossRush.RankMemberDigits));

    /// <summary>리더보드 멤버 문자열을 user_id로 되돌린다(선행 0은 그대로 파싱된다).</summary>
    private static long UserIdOf(string member)
        => long.TryParse(member, out var userId) ? userId : 0;

    private RedisSortedSet<string> Board(int seasonId)
        => new(Connection, string.Format(Constants.RedisKey.BossRushLeaderboardFormat, seasonId), null);

    /// <summary>적재 완료 마커 구조체 — 이 키가 있어야 리더보드를 전량 적재된 것으로 본다.</summary>
    private RedisString<string> ReadyMarker(int seasonId)
        => new(Connection, string.Format(Constants.RedisKey.BossRushLeaderboardReadyFormat, seasonId), null);

    /// <summary>자동 재적재 락 구조체(SET NX + TTL).</summary>
    private RedisString<string> RebuildLock(int seasonId)
        => new(Connection, string.Format(Constants.RedisKey.BossRushLeaderboardRebuildLockFormat, seasonId), null);

    /// <summary>닉네임 캐시 구조체(field = userId 문자열).</summary>
    private RedisDictionary<string, string> Nicknames()
        => new(Connection, Constants.RedisKey.PlayerNickname, null);

    /// <summary>현재 시즌 메타 캐시 구조체.</summary>
    private RedisDictionary<string, string> CurrentSeason()
        => new(Connection, Constants.RedisKey.BossRushCurrentSeason, null);

    /// <summary>
    /// 기록·달성 시각을 정렬 가능한 단일 점수로 인코딩한다(4.3). tie-break 자리는 시즌 시작 기준
    /// 상대 초이며, 시즌 시작보다 이른 시각이 들어와도 음수가 되지 않도록 0으로 clamp한다.
    /// </summary>
    private static double Encode(int clearMs, long recordedAt, long seasonStartAt)
        => (double)((long)clearMs * Constants.BossRush.ScoreScale + SeasonOffset(recordedAt, seasonStartAt));

    /// <summary>점수에서 기록(ms)과 달성 시각(초)을 복원한다 — 별도 조회 없이 표시값을 만든다.</summary>
    private static (int ClearMs, long RecordedAt) Decode(double score, long seasonStartAt)
    {
        var raw = (long)score;
        return ((int)(raw / Constants.BossRush.ScoreScale), seasonStartAt + raw % Constants.BossRush.ScoreScale);
    }

    /// <summary>달성 시각을 시즌 시작 기준 상대 초로 바꾼다(0 이상, 배수 미만으로 clamp).</summary>
    private static long SeasonOffset(long recordedAt, long seasonStartAt)
    {
        var offset = recordedAt - seasonStartAt;
        return offset < 0 ? 0 : offset >= Constants.BossRush.ScoreScale ? Constants.BossRush.ScoreScale - 1 : offset;
    }
}
