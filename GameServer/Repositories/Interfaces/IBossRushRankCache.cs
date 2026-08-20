using GameServer.Repositories;

namespace GameServer.Repositories.Interfaces;

public interface IBossRushRankCache
{
    /// <summary>
    /// 리더보드에 개인 최고 기록을 반영한다(ZADD). 반드시 <b>MySQL 커밋 이후</b>에 호출한다 —
    /// 트랜잭션 안에서 넣으면 롤백된 기록이 랭킹에 남고 Redis에는 롤백이 없다(기획서 6.2·6.3).
    /// </summary>
    Task<bool> UpsertAsync(int seasonId, long userId, int bestClearMs, long recordedAt);

    /// <summary>본인 순위(ZRANK + 1)를 조회한다. 등재되지 않았거나 캐시를 쓸 수 없으면 null.</summary>
    Task<int?> GetRankAsync(int seasonId, long userId);

    /// <summary>본인 순위 1행(순위·기록·달성 시각)을 조회한다. 등재되지 않았거나 캐시를 쓸 수 없으면 null.</summary>
    Task<BossRushCachedRank?> GetMyEntryAsync(int seasonId, long userId);

    /// <summary>시즌 등재 인원(ZCARD). 캐시를 쓸 수 없으면 null.</summary>
    Task<int?> CountAsync(int seasonId);

    /// <summary>랭킹 목록 한 페이지(ZRANGE). 캐시를 쓸 수 없으면 null(호출측이 MySQL 폴백).</summary>
    Task<IReadOnlyList<BossRushCachedRank>?> GetPageAsync(int seasonId, int offset, int limit);

    /// <summary>시즌 리더보드 키가 존재하는지(워밍업 필요 판단). 캐시를 쓸 수 없으면 null.</summary>
    Task<bool?> ExistsAsync(int seasonId);

    /// <summary>종료된 시즌 리더보드에 TTL을 걸어 과거 키가 무한히 쌓이지 않게 한다(기획서 4.3).</summary>
    Task ExpireAsync(int seasonId, TimeSpan ttl);

    /// <summary>지정 userId들의 닉네임을 캐시에서 읽는다. 미스(캐시에 없는 userId)는 결과에서 빠진다.</summary>
    Task<IReadOnlyDictionary<long, string>> GetNicknamesAsync(IReadOnlyCollection<long> userIds);

    /// <summary>닉네임 캐시를 채운다(lazy 채움의 백필 단계). 실패해도 조회 결과에는 영향이 없다.</summary>
    Task SetNicknamesAsync(IReadOnlyDictionary<long, string> nicknames);

    /// <summary>현재 시즌 메타를 캐시에서 읽는다. 없거나 캐시를 쓸 수 없으면 null.</summary>
    Task<BossRushSeason?> GetCurrentSeasonAsync();

    /// <summary>현재 시즌 메타 캐시를 갱신한다(시즌 정산 배치·기동 워밍업이 호출).</summary>
    Task SetCurrentSeasonAsync(BossRushSeason season);
}
