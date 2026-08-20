using CloudStructures;
using CloudStructures.Structures;
using GameServer.Repositories.Interfaces;
using TaskbarHero.Common;
using ZLogger;

namespace GameServer.Repositories;

/// <summary>랭킹 캐시에서 읽은 1행. 순위는 1부터이며 점수에서 기록·달성 시각을 복원한 값이다.</summary>
public sealed record BossRushCachedRank(int Rank, long UserId, int ClearMs, long RecordedAt);

/// <summary>
/// 보스러시 랭킹 조회용 Redis 계층(기획서 4.3). 키 3종을 다룬다 —
/// 리더보드 <c>rank:bossrush:{seasonId}</c>(Sorted Set), 닉네임 캐시 <c>player:nickname</c>(Hash),
/// 현재 시즌 메타 <c>bossrush:season:current</c>(Hash).
/// <para><b>정본이 아니다.</b> 리더보드는 MySQL <c>boss_rush_record</c>에서 파생된 조회 인덱스이고,
/// 닉네임·시즌 메타도 각각 <c>game_player.nickname</c>·<c>boss_rush_season</c>의 캐시다. 그래서 모든
/// 읽기는 실패 시 null을 돌려주고 호출측이 MySQL로 폴백한다(축소 운전) — 예외를 밖으로 던지지 않는다.</para>
/// <para><b>점수 인코딩</b>: <c>score = clearMs × 10^10 + recordedAt(초)</c>. 오름차순이 곧 순위이며
/// 동점은 먼저 달성한 쪽이 상위다. 제한 시간 10분(600,000ms)이 상한이라 6 × 10^15 &lt; 2^53으로
/// double 정밀도 안에 들어간다.</para>
/// </summary>
public sealed class BossRushRankCache : IBossRushRankCache
{
    /// <summary>점수 인코딩 배수 — 하위 10자리를 recordedAt(초, 서기 2286년까지 10^10 미만)에 내준다.</summary>
    private const long ScoreScale = 10_000_000_000L;

    /// <summary>닉네임 캐시 키(시즌·콘텐츠 무관 전역 Hash, TTL 없음).</summary>
    private const string NicknameKey = "player:nickname";

    /// <summary>현재 시즌 메타 캐시 키(Hash).</summary>
    private const string CurrentSeasonKey = "bossrush:season:current";

    private readonly RedisConnection _connection;
    private readonly ILogger<BossRushRankCache> _logger;

    /// <summary>Redis 연결과 로거를 주입받는다.</summary>
    public BossRushRankCache(RedisConnection connection, ILogger<BossRushRankCache> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    /// <summary>개인 최고 기록을 리더보드에 반영한다(ZADD). 실패는 Warning만 남기고 false를 돌려준다.</summary>
    public async Task<bool> UpsertAsync(int seasonId, long userId, int bestClearMs, long recordedAt)
    {
        try
        {
            var board = Board(seasonId);
            await board.AddAsync(userId, Encode(bestClearMs, recordedAt));
            return true;
        }
        catch (Exception ex)
        {
            // 기록·보상은 이미 MySQL에 확정되어 있다 — 캐시 반영 실패는 순위 표시만 미룬다.
            Warn(ex, "리더보드 갱신");
            return false;
        }
    }

    /// <summary>ZRANK로 본인 순위를 얻는다(0-based → 1-based). 미등재·실패는 null.</summary>
    public async Task<int?> GetRankAsync(int seasonId, long userId)
    {
        try
        {
            var rank = await Board(seasonId).RankAsync(userId);
            return rank.HasValue ? (int)(rank.Value + 1) : null;
        }
        catch (Exception ex)
        {
            Warn(ex, "내 순위 조회");
            return null;
        }
    }

    /// <summary>ZRANK + ZSCORE로 본인 순위 1행을 만든다. 미등재·실패는 null.</summary>
    public async Task<BossRushCachedRank?> GetMyEntryAsync(int seasonId, long userId)
    {
        try
        {
            var board = Board(seasonId);
            var rank = await board.RankAsync(userId);
            if (!rank.HasValue)
            {
                return null;
            }

            var score = await board.ScoreAsync(userId);
            if (!score.HasValue)
            {
                return null;
            }

            var (clearMs, recordedAt) = Decode(score.Value);
            return new BossRushCachedRank((int)(rank.Value + 1), userId, clearMs, recordedAt);
        }
        catch (Exception ex)
        {
            Warn(ex, "내 순위 조회");
            return null;
        }
    }

    /// <summary>ZCARD로 등재 인원을 센다. 실패는 null.</summary>
    public async Task<int?> CountAsync(int seasonId)
    {
        try
        {
            return (int)await Board(seasonId).LengthAsync();
        }
        catch (Exception ex)
        {
            Warn(ex, "등재 인원 조회");
            return null;
        }
    }

    /// <summary>
    /// ZRANGE로 한 페이지를 읽는다. Sorted Set은 skiplist의 span으로 시작 지점을 O(log N)에 찾으므로
    /// 오프셋이 깊어져도 비용이 반환 크기(M)에만 비례한다 — 그래서 순위 범위에 상한을 두지 않는다(4.3).
    /// </summary>
    public async Task<IReadOnlyList<BossRushCachedRank>?> GetPageAsync(int seasonId, int offset, int limit)
    {
        try
        {
            var entries = await Board(seasonId).RangeByRankWithScoresAsync(offset, offset + limit - 1);
            var result = new List<BossRushCachedRank>(entries.Length);
            for (var i = 0; i < entries.Length; i++)
            {
                var (clearMs, recordedAt) = Decode(entries[i].Score);
                result.Add(new BossRushCachedRank(offset + i + 1, entries[i].Value, clearMs, recordedAt));
            }

            return result;
        }
        catch (Exception ex)
        {
            Warn(ex, "랭킹 목록 조회");
            return null;
        }
    }

    /// <summary>리더보드 키 존재 여부(워밍업 필요 판단). 실패는 null.</summary>
    public async Task<bool?> ExistsAsync(int seasonId)
    {
        try
        {
            return await Board(seasonId).ExistsAsync();
        }
        catch (Exception ex)
        {
            Warn(ex, "리더보드 존재 확인");
            return null;
        }
    }

    /// <summary>종료 시즌 리더보드에 TTL을 건다. 실패는 Warning만 남긴다(과거 키가 남아도 조회에 문제없다).</summary>
    public async Task ExpireAsync(int seasonId, TimeSpan ttl)
    {
        try
        {
            await Board(seasonId).ExpireAsync(ttl);
        }
        catch (Exception ex)
        {
            Warn(ex, "리더보드 TTL 설정");
        }
    }

    /// <summary>HMGET으로 닉네임을 읽는다. 미스는 결과에서 빠지고, 호출측이 MySQL에서 백필한다.</summary>
    public async Task<IReadOnlyDictionary<long, string>> GetNicknamesAsync(IReadOnlyCollection<long> userIds)
    {
        var map = new Dictionary<long, string>();
        if (userIds.Count == 0)
        {
            return map;
        }

        try
        {
            var hash = Nicknames();
            var fields = userIds.Select(id => id.ToString()).ToArray();
            var values = await hash.GetAsync(fields);
            foreach (var pair in values)
            {
                if (long.TryParse(pair.Key, out var userId))
                {
                    map[userId] = pair.Value;
                }
            }
        }
        catch (Exception ex)
        {
            Warn(ex, "닉네임 캐시 조회");
        }

        return map;
    }

    /// <summary>HSET으로 닉네임 캐시를 채운다(lazy 백필). 실패해도 응답에는 영향이 없다.</summary>
    public async Task SetNicknamesAsync(IReadOnlyDictionary<long, string> nicknames)
    {
        if (nicknames.Count == 0)
        {
            return;
        }

        try
        {
            var hash = Nicknames();
            var entries = nicknames.ToDictionary(p => p.Key.ToString(), p => p.Value);
            await hash.SetAsync(entries);
        }
        catch (Exception ex)
        {
            Warn(ex, "닉네임 캐시 갱신");
        }
    }

    /// <summary>현재 시즌 메타를 캐시에서 읽는다. 값이 없거나 실패면 null(호출측이 MySQL 폴백).</summary>
    public async Task<BossRushSeason?> GetCurrentSeasonAsync()
    {
        try
        {
            var hash = CurrentSeason();
            var values = await hash.GetAllAsync();
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
        }
        catch (Exception ex)
        {
            Warn(ex, "현재 시즌 캐시 조회");
            return null;
        }
    }

    /// <summary>현재 시즌 메타 캐시를 갱신한다(정산 배치·기동 워밍업). 실패는 Warning만 남긴다.</summary>
    public async Task SetCurrentSeasonAsync(BossRushSeason season)
    {
        try
        {
            var hash = CurrentSeason();
            await hash.SetAsync(new Dictionary<string, string>
            {
                ["seasonId"] = season.SeasonId.ToString(),
                ["startAt"] = season.StartAt.ToString(),
                ["endAt"] = season.EndAt.ToString(),
                ["status"] = season.Status.ToString(),
            });
        }
        catch (Exception ex)
        {
            Warn(ex, "현재 시즌 캐시 갱신");
        }
    }

    /// <summary>시즌 리더보드 구조체(member = userId).</summary>
    private RedisSortedSet<long> Board(int seasonId)
        => new(_connection, $"rank:bossrush:{seasonId}", null);

    /// <summary>닉네임 캐시 구조체(field = userId 문자열).</summary>
    private RedisDictionary<string, string> Nicknames()
        => new(_connection, NicknameKey, null);

    /// <summary>현재 시즌 메타 캐시 구조체.</summary>
    private RedisDictionary<string, string> CurrentSeason()
        => new(_connection, CurrentSeasonKey, null);

    /// <summary>기록·달성 시각을 정렬 가능한 단일 점수로 인코딩한다(4.3).</summary>
    private static double Encode(int clearMs, long recordedAt)
        => (double)((long)clearMs * ScoreScale + recordedAt);

    /// <summary>점수에서 기록(ms)과 달성 시각(초)을 복원한다 — 별도 조회 없이 표시값을 만든다.</summary>
    private static (int ClearMs, long RecordedAt) Decode(double score)
    {
        var raw = (long)score;
        return ((int)(raw / ScoreScale), raw % ScoreScale);
    }

    /// <summary>캐시 실패는 축소 운전으로 흡수한다 — 사용자 실수가 아니라 인프라 경합이므로 Warning.</summary>
    private void Warn(Exception ex, string operation)
        => _logger.ZLogWarning(ex, $"보스러시 랭킹 캐시 {operation:@Operation} 실패 — MySQL 폴백으로 진행합니다.");
}
