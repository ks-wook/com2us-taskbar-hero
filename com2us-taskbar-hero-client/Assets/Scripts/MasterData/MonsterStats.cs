using System;
using TaskbarHero.Common.MasterData;

namespace TaskbarHero.Client.MasterData
{
    /// <summary>
    /// <b>몬스터 레벨 → 전투 스탯</b> 산출. <c>monster_master</c>의 <c>hp</c>·<c>attack</c>은
    /// <b>레벨 1 기준값</b>이고, 실제로 스폰되는 몬스터의 스탯은 여기에 레벨 배율을 곱해 얻는다.
    ///
    /// <para><b>레벨은 몬스터가 아니라 등장 자리의 속성이다</b> — <c>monster_master</c>에는 레벨 컬럼이 없고
    /// <c>stage_spawn.monster_level</c>이 "이 스테이지에서 이 몬스터가 몇 레벨로 나오는가"를 정한다.
    /// 그래서 같은 코드라도 배치된 스테이지에 따라 다른 스탯으로 등장한다.</para>
    ///
    /// <para><b>계산 주체가 클라이언트인 이유</b> — 전투가 클라이언트 권위이므로 서버는 몬스터 스탯을 로드하지 않고
    /// 스테이지 진입 응답으로 <b>몬스터 코드와 레벨·마리 수만</b> 내려준다(스테이지/전투 결과 기획서 5.1).
    /// 번들 <c>monster_master.json</c>의 기준값에 이 배율을 곱하는 것은 전적으로 클라이언트 몫이다.</para>
    ///
    /// <para><b>배율 상수의 정본은 기획 문서</b>(<c>docs/세부/master-data/master-data-값.md</c> §9.4)이며,
    /// 밸런스 시뮬레이터(<c>tools/balance_sim.py</c>의 <c>MONSTER_HP_GROWTH</c>·<c>MONSTER_ATK_GROWTH</c>)와
    /// <b>같은 값을 써야 한다</b> — 어긋나면 시뮬레이터 측정이 게임 안 체감과 달라진다.</para>
    /// </summary>
    public static class MonsterStats
    {
        /// <summary>레벨당 HP 성장률(+25%). 정본: master-data-값.md §9.4.</summary>
        public const double HpGrowth = 1.25;

        /// <summary>레벨당 공격력 성장률(+18%). HP보다 완만하다 — 몬스터 공격력이 아군 체력이 따라오는
        /// 속도보다 빨라지면 즉사 구간이 생기기 때문이다(같은 문서 §9.3의 긴장도 축).</summary>
        public const double AttackGrowth = 1.18;

        /// <summary>
        /// 레벨 1 기준값에 레벨 배율을 곱해 실제 전투 스탯을 산출한다.
        /// <para>산식 — <c>hp = max(1, round(base.hp × 1.25^(level-1)))</c>,
        /// <c>attack = max(1, round(base.attack × 1.18^(level-1)))</c>.
        /// 반올림은 사사오입이며 <b>최소 1을 보장</b>한다(기준값이 작고 레벨이 낮을 때 0이 되어
        /// 죽지 않는 몬스터가 생기는 것을 막는다).</para>
        /// </summary>
        /// <param name="baseHp">레벨 1 기준 HP(<c>monster_master.hp</c>).</param>
        /// <param name="baseAttack">레벨 1 기준 공격력(<c>monster_master.attack</c>).</param>
        /// <param name="level">등장 레벨(1 이상). 1이면 배율 1.0이라 기준값이 그대로 쓰인다.
        /// <b>0·음수는 1로 취급</b>한다 — 레벨을 싣지 않은 옛 서버 응답·번들에서도 종전과 같은 값이 나오게 한다.</param>
        public static void Scale(long baseHp, long baseAttack, int level, out long hp, out long attack)
        {
            hp = ScaleOne(baseHp, level, HpGrowth);
            attack = ScaleOne(baseAttack, level, AttackGrowth);
        }

        /// <summary>마스터 행의 기준값에 레벨 배율을 적용한다(<see cref="Scale(long,long,int,out long,out long)"/> 편의 오버로드).
        /// 마스터가 null이면 둘 다 0을 돌려주므로 호출측이 폴백 값을 쓰면 된다.</summary>
        public static void Scale(MonsterMaster master, int level, out long hp, out long attack)
        {
            if (master == null)
            {
                hp = 0L;
                attack = 0L;
                return;
            }
            Scale(master.hp, master.attack, level, out hp, out attack);
        }

        /// <summary>기준값 하나에 <paramref name="growth"/>^(level-1)을 곱하고 사사오입한다(최소 1).</summary>
        private static long ScaleOne(long baseValue, int level, double growth)
        {
            if (baseValue <= 0L)
            {
                return 1L;   // 기준값이 없거나 잘못된 행 — 죽지 않는 몬스터가 되지 않도록 1로 올린다
            }
            if (level <= 1)
            {
                return baseValue;   // 레벨 1(또는 레벨 미지정) = 배율 1.0
            }
            double scaled = baseValue * Math.Pow(growth, level - 1);
            return (long)Math.Max(1d, Math.Round(scaled, MidpointRounding.AwayFromZero));
        }

        /// <summary>이 레벨에서의 HP 배율(로그·툴팁 표시용). 레벨 1이면 1.0.</summary>
        public static double HpMultiplierOf(int level) => level <= 1 ? 1d : Math.Pow(HpGrowth, level - 1);

        /// <summary>이 레벨에서의 공격력 배율(로그·툴팁 표시용). 레벨 1이면 1.0.</summary>
        public static double AttackMultiplierOf(int level) => level <= 1 ? 1d : Math.Pow(AttackGrowth, level - 1);
    }
}
