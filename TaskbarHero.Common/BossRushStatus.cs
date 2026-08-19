namespace TaskbarHero.Common
{
    /// <summary>
    /// 보스러시 도전 런의 상태(boss_rush_run.status). 서버-클라이언트 공유 enum이므로 숫자 값을 바꾸지 않는다.
    /// <para>실패 보고 엔드포인트가 없어 "실패" 상태를 두지 않는다 — 클리어 보고가 오면 <see cref="Cleared"/>,
    /// 오지 않은 채 제한 시간 + 그레이스를 넘기면 만료 판정에 걸려 <see cref="Expired"/>가 된다
    /// (보스러시 기획서 4.2·6.2).</para>
    /// </summary>
    public enum BossRushRunStatus
    {
        /// <summary>진행 중(도전 개시 직후).</summary>
        Running = 1,

        /// <summary>클리어 보고로 종결.</summary>
        Cleared = 2,

        /// <summary>만료 판정으로 종결(보상·기록 없음). 런을 읽는 경로가 lazy하게 기록한다.</summary>
        Expired = 3,
    }

    /// <summary>
    /// 보스러시 시즌 상태(boss_rush_season.status). 서버-클라이언트 공유 enum이므로 숫자 값을 바꾸지 않는다.
    /// <para><see cref="Settling"/> 구간에는 새 런을 받지 않는다 — 순위를 확정한 뒤 더 좋은 기록이 등재되는
    /// 경합을 막는다(보스러시 기획서 6.4).</para>
    /// </summary>
    public enum BossRushSeasonStatus
    {
        /// <summary>진행 중(기록 등재·도전 가능).</summary>
        Running = 1,

        /// <summary>정산 중(순위 확정·보상 메일 발급). 새 런을 받지 않는다.</summary>
        Settling = 2,

        /// <summary>정산 완료(종료). 랭킹은 final_rank로 계속 조회된다.</summary>
        Closed = 3,
    }

    /// <summary>
    /// 랭킹 응답의 <c>source</c> 값 — <b>순위를 어디서 산출했는지</b>를 가리킨다.
    /// <para>닉네임 캐시 미스나 종료 시즌 메타 조회로 MySQL을 거쳐도 <see cref="RankCache"/>다. 이 값은
    /// "MySQL을 접근했는가"가 아니라 순위 산출의 출처를 뜻한다(보스러시 기획서 5.4).</para>
    /// </summary>
    public enum BossRushRankSource
    {
        /// <summary>랭킹 캐시(Redis ZRANGE·ZRANK)에서 산출.</summary>
        RankCache = 1,

        /// <summary>MySQL 폴백(ORDER BY / COUNT(*))에서 산출. 캐시 장애 시의 축소 운전.</summary>
        Database = 2,
    }
}
