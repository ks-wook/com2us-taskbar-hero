namespace GameServer.Repositories;

/// <summary>
/// 진행 중 시즌 스냅샷(<c>boss_rush_season</c>). <b>GameDb와 MemoryDb가 함께 쓰는 도메인 모델</b>이라
/// 어느 한쪽 계층에 두지 않고 저장소 루트 네임스페이스에 둔다 — 정본은 MySQL(<c>boss_rush_season</c>)이고
/// 캐시(<c>bossrush:season:current</c>)는 같은 모양을 Redis Hash에 복사해 둔 것이다.
/// </summary>
public sealed record BossRushSeason(int SeasonId, long StartAt, long EndAt, int Status);
