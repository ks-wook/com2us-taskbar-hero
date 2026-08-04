namespace TaskbarHero.Client.Managers
{
    /// <summary>
    /// 전투 사운드 조회표(사운드 리소스 정의서 §5). 직업·스킬 코드·몬스터를 <see cref="SoundId"/>로 바꿔 주며,
    /// 재생은 호출측(<c>PlayerCombatant</c>·<c>MonsterUnit</c>·<c>BattleDevController</c>)이 한다.
    /// <para>이펙트 배선이 <c>PartyMemberConfig.EffectFor(skillCode)</c>로 스킬 코드에서 파생되는 것과 대칭이 되도록
    /// 사운드도 <b>코드 → ID 한 곳</b>에서 정한다(정의서 §9.2). 새 직업·스킬을 넣을 때 이 표만 늘리면 된다.</para>
    /// </summary>
    public static class BattleSounds
    {
        // 클래스 코드(class_master) — 1:기사 2:레인저 3:마법사 4:슬레이어.
        private const int ClassKnight = 1;
        private const int ClassArcher = 2;
        private const int ClassMage = 3;
        private const int ClassSlayer = 4;

        /// <summary>
        /// 직업별 기본 공격음. 기사=검격, 레인저=활, 마법사=파이어볼(시전=명중 공용), 슬레이어=도끼.
        /// 정의되지 않은 직업은 <see cref="SoundId.None"/>(무음)이다.
        /// </summary>
        public static SoundId BasicAttackFor(int classCode)
        {
            switch (classCode)
            {
                case ClassKnight:
                    return SoundId.KnightBasic;
                case ClassArcher:
                    return SoundId.ArcherBowShot;
                case ClassMage:
                    return SoundId.MageFireball;
                case ClassSlayer:
                    return SoundId.SlayerBasic;
                default:
                    return SoundId.None;
            }
        }

        /// <summary>
        /// 액티브 스킬 <b>시전</b>음(skill_master 코드 기준). 패시브(x10~x12)는 전투 중 발동 연출이 없어 무음이다.
        /// <para>타격이 시전과 떨어져 있는 스킬은 여기서 시전음만 돌려주고, 임팩트음은 호출측이 도달 콜백에 따로 건다 —
        /// 방패 돌진(101 → <see cref="SoundId.KnightPowerStrike"/>), 내려찍기(401 → <see cref="SoundId.SlayerGroundSlam"/>),
        /// 화살비(203 → <see cref="SoundId.ArcherArrowImpact"/>). 정의서 §9.3의 동기화 주의 지점과 같은 이유다.</para>
        /// </summary>
        public static SoundId SkillFor(int skillCode)
        {
            switch (skillCode)
            {
                // 기사 — 101 방패 돌진(개시) · 102 기사의 분노(버프) · 103 강타
                case 101:
                    return SoundId.KnightShieldChargeStart;
                case 102:
                    return SoundId.KnightRage;
                case 103:
                    return SoundId.KnightPowerStrike;

                // 레인저 — 201 정조준 사격 · 202 다중 사격 · 203 화살비(시전은 활 소리)
                case 201:
                case 203:
                    return SoundId.ArcherBowShot;
                case 202:
                    return SoundId.ArcherMultiShot;

                // 마법사 — 301 파이어볼 · 302 프로스트 노바 · 303 라이트닝 볼트
                case 301:
                    return SoundId.MageFireball;
                case 302:
                    return SoundId.MageFrostNova;
                case 303:
                    return SoundId.MageLightningBolt;

                // 슬레이어 — 401 내려찍기(도약) · 402 광전사의 힘 · 403 강한일격
                case 401:
                    return SoundId.SlayerLeap;
                case 402:
                    return SoundId.SlayerBerserk;
                case 403:
                    return SoundId.SlayerCleave;

                default:
                    return SoundId.None;
            }
        }

        /// <summary>
        /// 몬스터 피격음(§8 — 계열별로 살점/금속을 가른다). <c>monster_master</c>에는 계열 컬럼이 없으므로
        /// <b>이름 키워드</b>로 판정한다 — 스켈레톤·기사·병사처럼 뼈·갑옷을 두른 대상은 금속음
        /// (<see cref="SoundId.HitArmor"/>), 그 외(데몬·추적자 등 살점)는 <see cref="SoundId.HitFlesh"/>다.
        /// 마스터에 계열 컬럼이 생기면 이 판정을 그 값으로 바꾼다.
        /// </summary>
        public static SoundId MonsterHitFor(string monsterName)
        {
            if (string.IsNullOrEmpty(monsterName))
            {
                return SoundId.HitFlesh;
            }
            if (monsterName.Contains("스켈레톤") || monsterName.Contains("기사") ||
                monsterName.Contains("병사") || monsterName.Contains("골렘") || monsterName.Contains("군주"))
            {
                return SoundId.HitArmor;
            }
            return SoundId.HitFlesh;
        }
    }
}
