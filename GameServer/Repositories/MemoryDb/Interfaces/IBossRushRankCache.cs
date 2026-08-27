using GameServer.Repositories.MemoryDb;
using GameServer.Models;

namespace GameServer.Repositories.MemoryDb.Interfaces;

public interface IBossRushRankCache
{
    /// <summary>
    /// 리더보드에 개인 최고 기록을 반영한다(ZADD). 반드시 <b>MySQL 커밋 이후</b>에 호출한다 —
    /// 트랜잭션 안에서 넣으면 롤백된 기록이 랭킹에 남고 Redis에는 롤백이 없다(기획서 6.2·6.3).
    /// <para>점수의 tie-break 자리가 <b>시즌 시작 기준 상대 초</b>라 seasonStartAt이 함께 필요하다(4.3).</para>
    /// </summary>
    Task<bool> UpsertAsync(int seasonId, long seasonStartAt, long userId, int bestClearMs, long recordedAt);

    /// <summary>본인 순위(ZRANK + 1)를 조회한다. 등재되지 않았거나 캐시를 쓸 수 없으면 null.</summary>
    Task<int?> GetRankAsync(int seasonId, long userId);

    /// <summary>본인 순위 1행(순위·기록·달성 시각)을 조회한다. 등재되지 않았거나 캐시를 쓸 수 없으면 null.
    /// 달성 시각 복원에 시즌 시작 시각이 필요하다(점수 인코딩, 4.3).</summary>
    Task<BossRushCachedRank?> GetMyEntryAsync(int seasonId, long seasonStartAt, long userId);

    /// <summary>시즌 등재 인원(ZCARD). 캐시를 쓸 수 없으면 null.</summary>
    Task<int?> CountAsync(int seasonId);

    /// <summary>랭킹 목록 한 페이지(ZRANGE). 캐시를 쓸 수 없으면 null(호출측이 MySQL 폴백).
    /// 달성 시각 복원에 시즌 시작 시각이 필요하다(점수 인코딩, 4.3).</summary>
    Task<IReadOnlyList<BossRushCachedRank>?> GetPageAsync(int seasonId, long seasonStartAt, int offset, int limit);

    /// <summary>
    /// 리더보드를 <b>믿고 써도 되는지</b>(적재 완료 마커 존재 여부). 캐시를 쓸 수 없으면 null.
    /// <para><b>리더보드 키의 존재로 판단하지 않는다</b> — 클리어 보고의 ZADD가 키를 새로 만들 수 있어,
    /// Redis를 재기동한 뒤 누군가 기록을 갱신하면 그 한 명만 든 리더보드가 생기기 때문이다. 마커는
    /// 워밍업이 시즌 기록을 전량 적재했을 때만 세워진다.</para>
    /// <para>돌려주는 값의 뜻이 셋이라 <c>bool?</c>다 — <c>true</c>=캐시 신뢰 가능,
    /// <c>false</c>=적재되지 않음(정본 폴백 + 재적재 필요), <c>null</c>=Redis를 쓸 수 없음(정본 폴백).</para>
    /// </summary>
    Task<bool?> IsReadyAsync(int seasonId);

    /// <summary>전량 적재를 마친 리더보드에 완료 마커를 세운다(워밍업만 호출). 이 시점부터 조회가 캐시를 쓴다.</summary>
    Task MarkReadyAsync(int seasonId);

    /// <summary>
    /// 리더보드를 통째로 비운다(재적재 직전에만 호출).
    /// <para><b>덮어쓰기만으로는 정본과 같아지지 않는다</b> — ZADD는 멤버를 갱신할 뿐 정본에서 사라진 멤버를
    /// 지우지 않으므로, 그대로 다시 적재하면 <b>정본에 없는 유령 멤버가 남아</b> 순위를 밀어낸다. 비우고
    /// 채워야 리더보드가 정본의 복제가 된다.</para>
    /// <para>비운 뒤 적재가 실패해도 안전하다 — 적재 완료 마커를 먼저 내리므로 그동안의 조회는 정본으로
    /// 폴백하고, 다음 재적재가 다시 시도한다.</para>
    /// </summary>
    Task ClearBoardAsync(int seasonId);

    /// <summary>
    /// 적재 완료 마커를 지워 리더보드를 <b>믿을 수 없는 것으로</b> 되돌린다. 재적재를 시작할 때와,
    /// 클리어 보고의 리더보드 반영이 실패해 캐시에 빠진 사람이 생겼을 때 호출한다 — 다음 조회가
    /// 정본으로 폴백하고 재적재를 걸어 스스로 복구한다.
    /// </summary>
    Task ClearReadyAsync(int seasonId);

    /// <summary>
    /// 자동 재적재 락을 잡는다(SET NX). 잡았으면 true, 이미 다른 인스턴스가 재적재 중이거나 Redis를
    /// 쓸 수 없으면 false — <b>확실히 잡았을 때만 true</b>라 호출측은 true에서만 재적재하면 된다.
    /// </summary>
    Task<bool> TryAcquireRebuildLockAsync(int seasonId, TimeSpan ttl);

    /// <summary>자동 재적재 락을 푼다. 풀지 못해도 TTL로 만료되므로 실패는 흡수한다.</summary>
    Task ReleaseRebuildLockAsync(int seasonId);

    /// <summary>종료된 시즌 리더보드에 TTL을 걸어 과거 키가 무한히 쌓이지 않게 한다(기획서 4.3).
    /// 적재 완료 마커도 같은 TTL로 함께 만료시킨다 — 리더보드가 사라졌는데 마커만 남으면
    /// 빈 랭킹을 정상으로 믿게 된다.</summary>
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
