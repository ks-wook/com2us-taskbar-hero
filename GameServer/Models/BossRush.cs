namespace GameServer.Models;

// BossRushRepository 전용 DB 행 매핑용 POCO(제네릭 매핑 전용, dynamic 금지).
// Dapper.MatchNamesWithUnderscores=true(Program.cs)로 snake_case 컬럼 → PascalCase 프로퍼티 매핑.

/// <summary>boss_rush_season 행 매핑용 POCO.</summary>
class BossRushSeasonRow
{
    public int SeasonId { get; set; }
    public long StartAt { get; set; }
    public long EndAt { get; set; }
    public int Status { get; set; }
}

/// <summary>game_player 진행도 조회용 POCO(보스러시 해금 판정·정보 조회).</summary>
class BossRushPlayerRow
{
    public int MaxStageCleared { get; set; }
}

/// <summary>boss_rush_run 조회용 POCO(진행 중 런).</summary>
class BossRushRunRow
{
    public long RunId { get; set; }
    public int SeasonId { get; set; }
    public long StartedAt { get; set; }
}

/// <summary>boss_rush_record 최고 기록 조회용 POCO.</summary>
class BossRushRecordRow
{
    public int BestClearMs { get; set; }
    public long RecordedAt { get; set; }
}

/// <summary>boss_rush_record 랭킹 조회용 POCO(final_rank 포함).</summary>
class BossRushRankRecordRow
{
    public long UserId { get; set; }
    public int BestClearMs { get; set; }
    public long RecordedAt { get; set; }
    public int FinalRank { get; set; }
}

/// <summary>boss_rush_record 정산·워밍업 스캔용 POCO.</summary>
class BossRushSettleRow
{
    public long UserId { get; set; }
    public int BestClearMs { get; set; }
    public long RecordedAt { get; set; }
}

/// <summary>game_player 닉네임 조회용 POCO(랭킹 표시 이름).</summary>
class BossRushNicknameRow
{
    public long UserId { get; set; }
    public string? Nickname { get; set; }
}

// ── 마스터 데이터 정의(불변). Repositories/MasterDb 가 적재하고 MasterDbProvider 가 조회한다. ──
/// <summary>
/// 보스러시 전역 규칙(boss_rush_master, 단일 행). 라운드 수·해금 조건은 밸런스 값이고
/// 클라이언트가 같은 값으로 판정해야 하므로 마스터에 둔다(보스러시 기획서 4.1).
/// <para><b>제한 시간과 일일 도전 횟수를 두지 않는다</b> — 전자는 실플레이에서 파티가 완주하거나
/// 전멸하거나 둘 중 하나라 판정에 관여한 적이 없고, 후자는 도전 보상이 사라진 뒤로 조일 대상이
/// 없어졌다(도전은 재화·아이템을 만들지 않는다).</para>
/// <para><see cref="RunExpireSec"/>은 게임 룰이 아니라 <b>버려진 런을 정리하는 원장 규칙</b>이며,
/// 동시에 보고 가능한 clearMs의 형식 상한이자 랭킹 점수 인코딩의 안전 여유다(같은 문서 4.3).</para>
/// </summary>
public sealed record BossRushRuleDef(
    int RoundCount, int UnlockStageSequence,
    int SeasonPeriodDays, int RunExpireSec, int RankPageLimit)
{
    /// <summary>런이 만료로 취급되기까지의 수명(ms). clearMs의 형식 상한이기도 하다(6.2 lazy 만료).</summary>
    public long RunLifetimeMs => (long)RunExpireSec * 1000;
}
/// <summary>보스러시 라운드 스폰 1건(boss_rush_spawn, is_boss=0). 레벨은 등장 자리의 속성이다.</summary>
public sealed record BossRushSpawnEntry(int MonsterCode, int MonsterLevel, int Count);
/// <summary>
/// 보스러시 라운드 정의(boss_rush_round + 자식 boss_rush_spawn). 라운드 r은 Act r의 전투이며
/// 배경도 그 Act의 스테이지 배경을 재활용한다. 보스는 자식 테이블의 is_boss=1 행에서 투영한다.
/// </summary>
public sealed record BossRushRoundDef(
    int Round, int BackgroundType, int BossMonsterCode, int BossMonsterLevel,
    IReadOnlyList<BossRushSpawnEntry> Spawns);
/// <summary>
/// 보스러시 시즌 순위 보상 구간(boss_rush_rank_reward). 지급 품목이 골드뿐이라 자식 테이블이 없다.
/// 현재 3행(1위·2위·3위)이며 4위 이하는 행이 없어 보상을 받지 않는다(보스러시 기획서 4.1).
/// </summary>
public sealed record BossRushRankRewardDef(int RankGroup, int RankFrom, int RankTo, long RewardGold)
{
    /// <summary>이 구간이 해당 순위를 포함하는지.</summary>
    public bool Contains(int rank) => rank >= RankFrom && rank <= RankTo;
}

// ── 마스터 적재용 DB 행 매핑 POCO(제네릭 매핑 전용, dynamic 금지). snake_case→PascalCase는 Dapper 규칙으로 매핑.
//    DECIMAL 컬럼은 decimal로 받아 float/double로 캐스팅한다. 적재는 Repositories/MasterDb/MasterDbLoader. ──
/// <summary>boss_rush_master 행 매핑용 POCO(단일 행).</summary>
class BossRushMasterRow
{
    public int RoundCount { get; set; }
    public int UnlockStageSequence { get; set; }
    public int SeasonPeriodDays { get; set; }
    public int RunExpireSec { get; set; }
    public int RankPageLimit { get; set; }
}
/// <summary>boss_rush_round 행 매핑용 POCO.</summary>
class BossRushRoundRow
{
    public int Round { get; set; }
    public int BackgroundType { get; set; }
}
/// <summary>boss_rush_spawn 행 매핑용 POCO(일반 몬스터와 보스를 is_boss로 구분).</summary>
class BossRushSpawnRow
{
    public int Round { get; set; }
    public int MonsterCode { get; set; }
    public int MonsterLevel { get; set; }
    public int SpawnCount { get; set; }
    public int IsBoss { get; set; }
}
/// <summary>boss_rush_rank_reward 행 매핑용 POCO.</summary>
class BossRushRankRewardRow
{
    public int RankGroup { get; set; }
    public int RankFrom { get; set; }
    public int RankTo { get; set; }
    public long RewardGold { get; set; }
}
