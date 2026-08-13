using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TaskbarHero.Client.Managers;
using TaskbarHero.Client.MasterData;
using TaskbarHero.Common.Dto;
using TaskbarHero.Common.MasterData;

namespace TaskbarHero.Client.Battle
{
    /// <summary>
    /// 파티 멤버 1인의 전투를 담당하는 통일 전투 유닛(기사·레인저·마법사 등 모든 직업 공용).
    /// 몬스터/전진/대형/카메라/공용 데미지는 <see cref="BattleDevController"/>가 관리하고,
    /// 이 컴포넌트는 자기 캐릭터의 기본공격(근접/원거리 화살)·스킬(공격/버프)·돌진형 스킬·
    /// 분노 애니·모션 게이트를 처리한다. 설정은 <see cref="PartyMemberConfig"/>로 주입받는다.
    /// </summary>
    public class PlayerCombatant : MonoBehaviour
    {
        private class Skill
        {
            public int code;
            public string name;
            public int coefType;   // 주 효과 타입. 1=공격 2=버프 3=디버프
            public float coef;
            public float duration;
            public int statType;   // 버프/디버프가 작용할 스탯(룬과 동일 enum). 1=공격력 … 7=재사용 대기시간
            // 한 스킬이 여러 coef_type을 갖는 경우의 부가 효과(광전사의 힘 402: 버프 + 자원 소모 + 흡혈).
            public float hpCostRatio;      // coefType 4(자원 소모): 시전 시 잃는 현재 체력 비율
            public float lifestealRatio;   // coefType 5(흡혈): 가한 피해 중 회복 비율
            public float lifestealDuration;
            public float cooldown;
            public float timer;
            public GameObject effect;
            public Sprite icon;
            public float scale;    // 발동 이펙트 크기 배율(1=기본)
            public Vector2 offset; // 발동 이펙트 위치 보정(월드, x+는 적 방향)
            public float hitTimeRatio;    // 데미지 타격 시점(이펙트 재생 구간 비율, 1=이펙트 종료 시점)
        }

        private BattleDevController _ctrl;
        private PartyMemberConfig _cfg;
        private int _classCode;
        private bool _ranged;
        private float _attackRange;
        private string _attackAnim;
        private bool _castHold;       // 스킬 시전 시 손 든 프레임 정지 홀드(캐스터)
        private GameObject _arrowPrefab;
        private float _arrowSpeed = 9f;

        // 돌진형 스킬
        private int _chargeSkillCode;
        private float _chargeRange;
        private float _chargeSpeed;
        private int _rainSkillCode;   // 공중 화살비형 스킬 코드(0=없음)
        private int _slamSkillCode;   // 도약 내려찍기형 스킬 코드(0=없음)
        private float _slamAirTime;   // 내려찍기 공중 체류 시간(상승+낙하)
        private readonly HashSet<int> _aoeSkillCodes = new HashSet<int>(); // 광역(범위) 판정을 쓰는 스킬 코드
        private bool _basicAttackAoe; // true면 근접 기본공격이 사거리 내 모든 적에게 명중
        private float _selfEffectXOffset;   // 자기 위치 이펙트 X 오프셋(+=오른쪽)
        private GameObject _basicAttackProjectile; // 기본공격 투사체 프리팹(마법사 등, 없으면 근접/기존 방식)
        private float _basicAttackProjectileScale = 1f;
        private bool _allSkillsAoe;    // true면 모든 액티브 공격 스킬이 대상 중심 범위 데미지(마법사)
        private bool _charging;
        private Skill _chargeSkill;
        public bool IsCharging => _charging;

        /// <summary>
        /// 방패에 <b>부딪힌 뒤</b> 자세를 정리하기까지의 시간(초). 짧게 끊어야 "부딪혀서 멈췄다"로 읽힌다 —
        /// 예전처럼 이펙트 길이만큼 자세를 유지하면 이미 충돌했는데도 계속 밀고 나가는 그림이 남는다.
        /// </summary>
        private const float ChargeImpactWrapUpSeconds = 0.12f;

        /// <summary>방패 돌진이 적을 쓸어 담는 범위 = 공격 사거리 + 이 값(유닛). 정면의 대상뿐 아니라
        /// 그 바로 뒤에 몰려 있던 적들까지 방패에 부딪혀 함께 맞고 날아가도록 조금 넓게 잡는다.</summary>
        private const float ChargeShoveExtraRange = 1.8f;

        /// <summary>방패 돌진 전 <b>뒤로 도약하는</b> 거리(유닛). 제자리에서 튀어나가는 것보다 준비 동작이 있어야
        /// 앞으로 나가는 순간이 크게 읽힌다. 뒤 행 아군과의 간격(<c>rowSpacingX</c> 1.6)보다 작게 잡아
        /// 물러나면서 뒷사람과 겹치지 않게 한다.</summary>
        private const float ChargeWindupDistance = 1.2f;

        /// <summary>뒤로 도약하는 데 걸리는 시간(초). 도약이 읽힐 만큼은 주되, 전투 흐름이 끊기지 않게 짧게 잡는다.</summary>
        private const float ChargeWindupSeconds = 0.34f;

        /// <summary>뒤로 도약할 때 솟구치는 정점 높이(유닛). 뒤로 가는 거리와 한 벌로 맞춰야 포물선이 자연스럽다.</summary>
        private const float ChargeWindupJumpHeight = 0.7f;

        private float _chargeWindup;    // 남은 준비 동작(뒤로 물러남) 시간. 0보다 크면 아직 돌진 전이다
        private float _windupApplied;   // 지금 위치에 반영돼 있는 물러남 오프셋
        private float _chargeStartX;    // 물러나기 <b>전</b>의 x — 돌진은 최소한 여기까지는 되돌아온다

        // ---- 근접 lunge(찔러 들어갔다 복귀) ----
        // 제자리 스윙은 "때렸다"가 약하게 읽힌다. 평타마다 0.1초 앞으로 파고들었다 돌아오면 체감이 확 다르다.
        /// <summary>자버프 이펙트를 캐릭터 묶음보다 얼마나 뒤에 둘지(정렬 칸 수).</summary>
        private const int BuffEffectSortingGap = 1;

        /// <summary>
        /// 자버프 이펙트가 내려갈 수 있는 <b>정렬 하한</b>. 던전 배경(<c>ScrollingBackground.sortingOrder</c>
        /// 기본 -100)보다 확실히 앞이어야 아우라가 배경에 가려지지 않는다. 0으로 두면 배경(-100)보다 앞이면서
        /// 아군 묶음(20)·적 묶음(10)보다는 뒤라 "캐릭터 뒤에서 번지는" 그림이 유지된다.
        /// </summary>
        private const int MinBuffEffectOrder = 0;

        private const float LungeDistance = 0.32f;      // 앞으로 파고드는 거리(유닛)
        private const float LungeOutSeconds = 0.06f;    // 나가는 시간(짧고 빠르게)
        private const float LungeBackSeconds = 0.12f;   // 돌아오는 시간(조금 느리게 — 무게감)
        private Coroutine _lungeAnim;
        private float _lungeApplied;                    // 지금 위치에 반영돼 있는 lunge 오프셋

        /// <summary>
        /// 카메라 팔로우가 기준으로 삼을 x — <b>연출용 lunge 오프셋을 뺀 위치</b>다.
        /// <para>카메라 x는 최전방 아군을 그대로 따라가므로, 평타마다 0.32유닛을 앞뒤로 오가는 lunge를
        /// 그대로 따라가면 <b>기본공격마다 카메라가 왕복해 셰이크처럼 보인다</b>(2026-08-05 수정).
        /// 대형 이동·돌진처럼 <b>실제로 이동한 결과</b>는 이 값에 그대로 반영되므로 카메라가 따라간다.</para>
        /// </summary>
        public float CameraFollowX => transform.position.x - _lungeApplied;

        // ---- 스킬별 화면 효과(3순위) ----
        // 마스터 데이터의 스킬 코드는 고정 키라 연출 분기 기준으로 안전하다(이름 문자열로 비교하지 않는다).
        private const int KnightPowerStrikeSkillCode = 103; // 강타 — 바닥 데칼
        private const int FrostNovaSkillCode = 302;         // 프로스트 노바 — 빙결(파란 틴트 + 이동 정지)
        private const int LightningBoltSkillCode = 303;      // 라이트닝 볼트 — 화면 백색 섬광
        private const int SlayerGroundSlamSkillCode = 401;   // 내려찍기 — 바닥 데칼(착지 시점)

        private const float FrostNovaFreezeSeconds = 0.5f;
        private const float FrostNovaFreezeRadius = 3.2f;

        /// <summary>
        /// 전(全)스킬 광역 캐스터(마법사)의 휩쓸기 판정 세로 여유(월드 유닛, 중심 기준 ±).
        /// 이펙트 스프라이트는 얇은 프레임(높이 0.4 남짓)이 많아 세로를 그림 그대로 쓰면 몸 높이가 조금만
        /// 어긋나도 빠진다. 적은 사실상 한 줄로 서므로(스폰 세로 분산 ±0.09) 이 정도 여유면 줄 안의 적을 모두 덮는다.
        /// </summary>
        private const float CasterSweepHalfHeight = 0.9f;

        /// <summary>
        /// 원거리 관통 스킬(정조준 사격)의 휩쓸기 판정 세로 여유(월드 유닛, 중심 기준 ±).
        /// 궤적 그림이 얇아 세로를 그림 그대로 쓰면 몸 높이가 조금만 어긋나도 빠지므로 캐스터와 같은 여유를 둔다.
        /// </summary>
        private const float RangedSweepHalfHeight = 0.9f;

        /// <summary>전스킬 광역 캐스터의 이펙트가 대상보다 짧을 때, 대상 뒤까지 더 뻗게 할 여유(월드 유닛).</summary>
        private const float CasterSweepOvershoot = 1.2f;

        /// <summary>전스킬 광역 캐스터의 이펙트를 앞으로 늘릴 수 있는 최대 배율. 그림이 늘어나 보이지 않도록 상한을 둔다.</summary>
        private const float CasterSweepMaxStretch = 2f;
        private const float LightningFlashSeconds = 0.09f;
        private const float DecalWidthStrike = 1.6f;
        private const float DecalWidthSlam = 2.4f;
        private static readonly Color LightningFlashColor = new Color(1f, 1f, 1f, 0.35f); // 상주 창이라 옅게

        private bool _chargeImpacted;  // 이번 돌진의 타격을 이미 예약했는지(중복 데미지 방지)
        private float _chargeElapsed;  // 돌진(준비 동작 이후) 시작 후 경과 시간
        private float _chargeEndAt;    // 이 경과 시간에 돌진을 끝낸다(충돌 전에는 무한 — 충돌해야 정해진다)

        private string _name = "Ally";
        private long _atk;
        private long _baseAtk;        // 장비 제외 기본 공격(클래스+레벨)
        private long _def;            // 최종 방어력(클래스+레벨+장비 × 패시브 × 룬). 피격 데미지 경감에 사용
        private long _baseDef;        // 장비 제외 기본 방어력(클래스+레벨)
        /// <summary>클래스 마스터에 치명피해가 없을 때 쓰는 기본 배율(150%).</summary>
        private const float DefaultCritDamage = 1.5f;

        private float _critChance;    // 최종 치명확률(0~1). 매 타격마다 굴려 치명타 여부를 정한다
        private float _critDamage;    // 최종 치명피해 배율(1.5 = 150%). 치명타일 때 데미지에 곱한다
        private float _baseCritChance; // 장비 제외 기본 치명확률(클래스+레벨)
        private float _baseCritDamage; // 장비 제외 기본 치명피해(클래스+레벨)
        private long _baseMaxHp;      // 장비 제외 기본 체력
        private int _characterId;     // 연결된 계정 캐릭터 id(serverMode, 0=없음)
        private long _maxHp = 1;
        private long _hp = 1;
        private bool _dead;
        private float _cooldown;
        private float _baseCooldown = 1.2f; // 룬(재사용 단축) 적용 전 기준 쿨다운

        [Tooltip("사망 애니 후 오브젝트가 사라지기까지 지연(초)")]
        private const float DeathLinger = 1.0f;

        // ---- 사망 시 나가떨어지는 연출(몬스터 DeathFlight와 같은 문법, 방향만 반대) ----
        // 아군은 오른쪽에서 맞으므로 <b>왼쪽(-x)</b>으로 날아간다. 몬스터 쪽 값(3.4·3.2·9·220)을 그대로 쓰면
        // 화면 밖까지 크게 튀어 파티 자리가 휑해 보여, 아군은 조금 짧게·낮게 잡았다.
        private const float DeathLaunchX = 2.6f;      // 뒤(왼쪽)로 날아가는 초기 속도(유닛/초)
        private const float DeathLaunchY = 2.9f;      // 위로 솟는 초기 속도
        private const float DeathGravity = 9f;        // 낙하 가속
        private const float DeathSpin = 190f;         // 회전 속도(도/초)
        private const float DeathFadeDelay = 0.35f;   // 이 시간 뒤부터 서서히 투명해진다
        private float _moveSpeed;
        private float _baseMoveSpeed = 3f;  // 패시브 제외 기본 이동속도(클래스)

        // ---- 좌상단 아군 HP바 juice(고스트 바 · 피격 흔들림 · 붉은 점멸) ----
        // 연출 상태를 <b>아군 쪽에</b> 둔다 — 바를 그리는 <see cref="SkillCooldownUI"/>는 파티 재구성 때 통째로
        // 다시 만들어지므로, 상태를 UI에 두면 그 순간 연출이 끊긴다(적 HP바가 상태를 <see cref="MonsterUnit"/>에
        // 두는 것과 같은 이유). 수치는 적 HP바와 같은 값을 써서 아군·적 바가 같은 문법으로 읽히게 한다.
        private const float HpGhostHoldSeconds = 0.18f;   // 깎인 직후 고스트를 이만큼 그대로 둔다(방금 잃은 양 노출)
        private const float HpGhostCatchUpPerSec = 1.4f;  // 그 뒤 비율/초로 현재 체력까지 따라 내려온다
        private const float HpBarShakeSeconds = 0.16f;    // 피격 시 바가 흔들리는 시간
        private const float HpBarFlashSeconds = 0.22f;    // 피격 시 바가 붉게 물들었다 돌아오는 시간

        private float _hpGhostRatio = 1f;
        private float _hpGhostHold;
        private float _hpBarShakeTimer;
        private float _hpBarFlashTimer;

        /// <summary>HP바 고스트(지연) 비율 0~1 — 현재 체력 비율보다 크면 그 차이가 "방금 깎인 양"이다.</summary>
        public float HpGhostRatio => _hpGhostRatio;
        /// <summary>피격 직후 HP바를 흔드는 강도 0~1(시간이 지나며 0으로 감쇠).</summary>
        public float HpBarShake01 => HpBarShakeSeconds > 0f ? Mathf.Clamp01(_hpBarShakeTimer / HpBarShakeSeconds) : 0f;
        /// <summary>피격 직후 HP바를 붉게 물들이는 강도 0~1(시간이 지나며 0으로 감쇠).</summary>
        public float HpBarFlash01 => HpBarFlashSeconds > 0f ? Mathf.Clamp01(_hpBarFlashTimer / HpBarFlashSeconds) : 0f;

        /// <summary>아군 현재 체력.</summary>
        public long Hp => _hp;
        /// <summary>아군 최대 체력.</summary>
        public long MaxHp => _maxHp;
        /// <summary>생존 여부(사망 시 false).</summary>
        public bool Alive => !_dead;
        /// <summary>현재 공격력(클래스+레벨+장비 합산).</summary>
        public long Attack => _atk;
        /// <summary>현재 방어력(클래스+레벨+장비 × 패시브 × 룬). 피격 데미지 경감에 사용.</summary>
        public long Defense => _def;
        /// <summary>현재 치명확률(0~1, 클래스+레벨+장비 × 패시브 × 룬).</summary>
        public float CritChance => _critChance;
        /// <summary>현재 치명피해 배율(1.5 = 150%).</summary>
        public float CritDamage => _critDamage;
        /// <summary>연결된 계정 캐릭터 id(serverMode).</summary>
        public int CharacterId => _characterId;

        private readonly List<Skill> _skills = new List<Skill>();
        private float _attackTimer;
        private float _busyTimer;
        private float _atkBuffMult = 1f;
        private float _cooldownBuffMult = 1f;   // 공격 주기 배율(statType 7 버프. <1 이면 그만큼 빨라짐)
        private float _lifestealRatio;          // 흡혈 비율(0=없음)
        private float _lifestealTimer;          // 흡혈 남은 시간
        private float _buffTimer;
        private GameObject _buffEffect;
        private WeaponAfterimage _weaponTrail;  // 무기 끝 잔상(색·반짝임은 무기 강화 단계 / 버프 중 붉음)
        private const int WeaponEquipSlot = 1;  // equip_slot_master 1 = 무기

        // 개별 이동(대형 목표를 자기 속도로 추격 — 칼같은 정렬이 아닌 동적 이동)
        private Vector2 _formTarget;
        private bool _hasTarget;
        private bool _moving;
        private float _stillTime;   // 목표에 도달해 멈춰 있던 시간(idle 전환 디바운스)
        private Animator _anim;     // 공격 모션 재생 여부 판정용(SPUM Animator)

        /// <summary>컨트롤러가 매 프레임 이 멤버의 대형 목표(월드 좌표)를 갱신한다.</summary>
        public void SetFormationTarget(Vector2 target) { _formTarget = target; _hasTarget = true; }

        public string DisplayName => _name;
        /// <summary>이 멤버의 직업 코드(파티는 직업 중복이 없어 파티원 식별 키로 쓸 수 있다).</summary>
        public int ClassCode => _classCode;
        public float AttackRange => _attackRange;
        public float MoveSpeed => _moveSpeed;
        /// <summary>현재 걷기(이동) 애니메이션이 재생 중인지. 걷기 먼지 이펙트 노출 판정에 사용.</summary>
        public bool IsMoving => _moving && !_dead;

        /// <summary>컨트롤러가 스폰 직후 호출해 설정을 주입한다.</summary>
        public void Configure(BattleDevController ctrl, PartyMemberConfig cfg)
        {
            _ctrl = ctrl;
            _cfg = cfg;
            _classCode = cfg.classCode;
            _ranged = cfg.ranged;
            _attackRange = cfg.attackRange;
            _attackAnim = cfg.attackAnim;
            _castHold = cfg.castHold;
            _arrowPrefab = cfg.arrowPrefab;
            _chargeSkillCode = cfg.chargeSkillCode;
            _chargeRange = cfg.chargeRange;
            _chargeSpeed = cfg.chargeSpeed;
            _rainSkillCode = cfg.rainSkillCode;
            _slamSkillCode = cfg.slamSkillCode;
            _slamAirTime = cfg.slamAirTime > 0f ? cfg.slamAirTime : 0.46f;
            _aoeSkillCodes.Clear();
            if (cfg.aoeSkillCodes != null)
            {
                foreach (int c in cfg.aoeSkillCodes)
                {
                    if (c != 0) _aoeSkillCodes.Add(c);
                }
            }
            _basicAttackAoe = cfg.basicAttackAoe;
            _selfEffectXOffset = cfg.selfEffectXOffset;
            _basicAttackProjectile = cfg.basicAttackProjectile;
            _basicAttackProjectileScale = cfg.basicAttackProjectileScale;
            _allSkillsAoe = cfg.allSkillsAoe;
            _anim = GetComponentInChildren<Animator>();

            // 무기 잔상: 스폰 시 1회 부착. 형태는 무기 종류별, 색·반짝임은 장착 무기의 강화 단계가 정한다
            // (단계 반영은 이어지는 LoadStats → ApplyEquipStats → RefreshWeaponTrailEnhance).
            if (cfg.weaponTrail && _weaponTrail == null)
            {
                _weaponTrail = WeaponAfterimage.Create(
                    transform, () => IsAttackMotionPlaying(), cfg.weaponTrailStyle);
            }

            LoadStats();
            BuildSkills();
        }

        private void LoadStats()
        {
            var db = MasterDataManager.Db;
            long atk, hp, def;
            float critChance, critDamage;
            if (db != null && db.Classes.TryGetValue(_classCode, out ClassMaster cls))
            {
                _name = cls.name;
                atk = cls.baseStats.atk;
                hp = System.Math.Max(1L, cls.baseStats.hp);
                def = cls.baseStats.def;
                critChance = cls.baseStats.critChance;
                // 마스터에 치명피해가 없으면(0) 치명타가 무의미해지므로 기본 배율 1.5를 쓴다.
                critDamage = cls.baseStats.critDamage > 0f ? cls.baseStats.critDamage : DefaultCritDamage;
                _cooldown = cls.baseStats.cooldown > 0f ? cls.baseStats.cooldown : 1.2f;
                _moveSpeed = cls.baseStats.moveSpeed > 0f ? cls.baseStats.moveSpeed : 3f;
            }
            else
            {
                _name = "Ally(?" + _classCode + ")";
                atk = 10; hp = 100; def = 0; _cooldown = 1.2f; _moveSpeed = 3f;
                critChance = 0f; critDamage = DefaultCritDamage;
            }

            // 서버 구동 모드: 계정 캐릭터의 레벨 보너스를 기본 스탯에 반영하고 characterId를 기록(장비 합산용).
            _characterId = 0;
            if (_ctrl != null && _ctrl.serverMode)
            {
                var ch = FindAccountCharacter(_classCode);
                if (ch != null)
                {
                    _characterId = ch.characterId;
                    if (db != null && db.Levels.TryGetValue(ch.level, out LevelMaster lm))
                    {
                        atk += lm.statBonus.atk;
                        hp += lm.statBonus.hp;
                        def += lm.statBonus.def;
                        critChance += lm.statBonus.critChance;
                        critDamage += lm.statBonus.critDamage;
                    }
                }
            }

            _baseAtk = atk;
            _baseDef = def;
            _baseCritChance = critChance;
            _baseCritDamage = critDamage;
            _baseMaxHp = System.Math.Max(1L, hp);
            _baseMoveSpeed = _moveSpeed; // 패시브 적용 전 기준 이동속도
            _baseCooldown = _cooldown;   // 룬 적용 전 기준 쿨다운
            ApplyEquipStats(); // 장비 스탯 + 패시브 배율 합산 → _atk/_maxHp/_moveSpeed 확정
            _hp = _maxHp;
            gameObject.name = "Player_" + _name;
        }

        /// <summary>기본 스탯(_baseAtk/_baseMaxHp/_baseMoveSpeed/_baseCrit*)에 장착 장비 합산 + 학습한 패시브 스킬
        /// 배율을 적용해 _atk/_maxHp/_moveSpeed/_critChance/_critDamage를 확정한다.
        /// 패시브는 장착과 무관하게 습득(레벨 ≥ 1) 시 상시 적용된다.
        /// 계산식은 인벤토리 능력치 패널(<c>InventoryPanelController.RefreshStatPanel</c>)과 동일하게 맞춘다 —
        /// 패널에 보이는 수치가 곧 전투에 쓰이는 수치여야 한다.</summary>
        private void ApplyEquipStats()
        {
            long atk = _baseAtk;
            long hp = _baseMaxHp;
            long def = _baseDef;
            float critChance = _baseCritChance;
            float critDamage = _baseCritDamage;
            var db = MasterDataManager.Db;
            // 장착 장비는 코어 로드(equipped)에만 있다 — 가방 아이템(페이징 조회)과 겹치지 않으므로
            // 가방을 아직 받지 않은 상태(접속 직후)에도 전투 스탯을 온전히 계산할 수 있다.
            var equipped = Session.Equipped;
            if (_characterId != 0 && equipped != null && db != null)
            {
                foreach (var it in equipped)
                {
                    if (it != null && it.equippedCharacterId == _characterId
                        && db.Items.TryGetValue(it.itemCode, out ItemMaster im))
                    {
                        // 강화 단계 배율(enhance_master)을 그 장비의 옵션 스탯에 곱한다 — 서버는 단계만 확정하고
                        // 배율은 응답에 담지 않으므로(기획서 §5.3) 인벤토리 능력치 패널과 같은 규칙으로 계산한다.
                        float em = db.EnhanceMultiplier(it.enhanceLevel);
                        atk += (long)System.Math.Round(im.baseStats.atk * (double)em);
                        hp += (long)System.Math.Round(im.baseStats.hp * (double)em);
                        def += (long)System.Math.Round(im.baseStats.def * (double)em);
                        critChance += im.baseStats.critChance * em;
                        critDamage += im.baseStats.critDamage * em;
                    }
                }
            }

            RefreshWeaponTrailEnhance(); // 무기 잔상 색·반짝임은 장착 무기의 강화 단계를 따른다

            // 패시브 스킬 배율 + 룬(계정 공용) 배율(statType: 1 공격력 · 2 방어력 · 3 체력 · 4 치명확률 ·
            // 5 치명피해 · 6 이동속도 · 7 쿨다운)을 곱한다.
            // 반올림으로 확정한다(버림 시 작은 % 상승분이 사라지는 문제 방지 — 인벤토리 능력치 패널과 동일 규칙).
            _atk = System.Math.Max(1L, (long)System.Math.Round(atk * (double)PassiveMult(1) * RuneMult(1)));
            _def = System.Math.Max(0L, (long)System.Math.Round(def * (double)PassiveMult(2) * RuneMult(2)));
            _maxHp = System.Math.Max(1L, (long)System.Math.Round(hp * (double)PassiveMult(3) * RuneMult(3)));
            // 치명확률은 0~1로 클램프(100%를 넘겨도 항상 치명일 뿐이고, 음수는 판정을 깨뜨린다).
            _critChance = Mathf.Clamp01(critChance * PassiveMult(4) * RuneMult(4));
            // 치명피해는 1 미만이면 치명타가 오히려 손해가 되므로 하한을 1로 둔다.
            _critDamage = Mathf.Max(1f, critDamage * PassiveMult(5) * RuneMult(5));
            _moveSpeed = _baseMoveSpeed * PassiveMult(6) * RuneMult(6);
            _cooldown = Mathf.Max(0.1f, _baseCooldown * RuneMult(7)); // 룬 재사용 단축(감소 방향)
        }

        /// <summary>
        /// 이 캐릭터가 장착한 <b>무기</b>(equip_slot 1)의 강화 단계를 무기 잔상에 반영한다.
        /// 잔상 색(+0 흰빛 → 붉은색 → +8 보랏빛 → +10 암흑)과 반짝임 입자의 세기가 여기서 정해진다.
        /// 장착이 없거나 개발 하네스처럼 계정 캐릭터가 연결되지 않은 경우(_characterId = 0)에는 0단계로 둔다.
        /// 장비 교체·강화 시 <see cref="RefreshStats"/> → <see cref="ApplyEquipStats"/> 경로로 다시 불린다.
        /// </summary>
        private void RefreshWeaponTrailEnhance()
        {
            if (_weaponTrail == null)
            {
                return;
            }

            int level = 0;
            var equipped = Session.Equipped;
            if (_characterId != 0 && equipped != null)
            {
                foreach (var it in equipped)
                {
                    if (it != null && it.equippedCharacterId == _characterId && it.equippedSlot == WeaponEquipSlot)
                    {
                        level = it.enhanceLevel;
                        break;
                    }
                }
            }
            _weaponTrail.SetEnhanceLevel(level);
        }

        /// <summary>계정 공용 룬(레벨 ≥ 1) 중 대상 statType을 올리는 것들의 배율 곱. 룬 stat_value는 레벨당 누적 비율이며
        /// 총 보너스 = stat_value × 레벨. statType 7(재사용 대기시간)은 감소(1 − 보너스), 그 외는 증가(1 + 보너스). 없으면 1.</summary>
        private static float RuneMult(int statType)
        {
            float mult = 1f;
            var db = MasterDataManager.Db;
            var runes = Session.GameData != null ? Session.GameData.runes : null;
            if (db == null || runes == null)
            {
                return mult;
            }
            foreach (var pr in runes)
            {
                if (pr == null || pr.level < 1)
                {
                    continue;
                }
                if (db.Runes.TryGetValue(pr.runeCode, out RuneMaster rm) && rm.statType == statType)
                {
                    float bonus = rm.statValue * pr.level;
                    mult *= statType == 7 ? Mathf.Max(0.05f, 1f - bonus) : (1f + bonus);
                }
            }
            return mult;
        }

        /// <summary>이 캐릭터가 습득(레벨 ≥ 1)한 패시브 스킬 중 대상 statType을 올리는 것들의 레벨별 배율 곱.
        /// 없으면 1(변화 없음). serverMode 계정 캐릭터에만 적용된다(세션 player_skill 기준).</summary>
        private float PassiveMult(int statType)
        {
            float mult = 1f;
            if (_characterId == 0)
            {
                return mult;
            }
            var db = MasterDataManager.Db;
            var skills = Session.GameData != null ? Session.GameData.skills : null;
            if (db == null || skills == null)
            {
                return mult;
            }
            foreach (var ps in skills)
            {
                if (ps == null || ps.characterId != _characterId || ps.level < 1)
                {
                    continue;
                }
                if (db.Skills.TryGetValue(ps.skillCode, out SkillMaster sm)
                    && sm.skillType == 2 && sm.statType == statType && sm.coefs != null)
                {
                    int lv = Mathf.Clamp(ps.level, 1, Mathf.Max(1, sm.maxLevel));
                    foreach (var c in sm.coefs)
                    {
                        if (c.skillLevel == lv)
                        {
                            mult *= c.coef;
                            break;
                        }
                    }
                }
            }
            return mult;
        }

        /// <summary>장비 변경 등으로 스탯을 재계산한다(현재 체력 비율 유지). 전투 중 즉시 반영.</summary>
        public void RefreshStats()
        {
            float ratio = _maxHp > 0 ? (float)_hp / _maxHp : 1f;
            ApplyEquipStats();
            _hp = System.Math.Max(1L, (long)(_maxHp * ratio));
        }

        /// <summary>클래스 코드에 해당하는 계정 캐릭터(없으면 null).</summary>
        private static CharacterDto FindAccountCharacter(int classCode)
        {
            var chars = Session.GameData != null ? Session.GameData.characters : null;
            if (chars != null)
            {
                foreach (var c in chars)
                {
                    if (c != null && c.classCode == classCode)
                    {
                        return c;
                    }
                }
            }
            return null;
        }

        private void BuildSkills()
        {
            _skills.Clear();
            _chargeSkill = null;
            var db = MasterDataManager.Db;
            if (db == null) return;

            var actives = new List<SkillMaster>();
            foreach (var s in db.Skills.Values)
                if (s.classCode == _classCode && s.skillType == 1) actives.Add(s);
            actives.Sort((a, b) => a.skillCode.CompareTo(b.skillCode));

            // serverMode + 연결된 계정 캐릭터: **장착(equipped=1)한 액티브 스킬만** 실제 습득 레벨로 사용한다
            // (캐릭터당 최대 2개, growth 기획서 5.3). 그 외(개발 하네스)에서는 모든 액티브를 DevSkillLevel로 사용.
            bool useEquipped = _ctrl != null && _ctrl.serverMode && _characterId != 0;

            float[] initReadyIn = { 2f, 4f, 6f, 8f };
            int devLevel = Mathf.Max(1, _ctrl != null ? _ctrl.DevSkillLevel : 1);
            int added = 0;
            for (int i = 0; i < actives.Count; i++)
            {
                var s = actives[i];
                int lv;
                if (useEquipped)
                {
                    if (!TryGetEquippedSkillLevel(s.skillCode, out int plv)) continue; // 미장착 → 전투에서 사용 안 함
                    lv = Mathf.Clamp(plv, 1, Mathf.Max(1, s.maxLevel));
                }
                else
                {
                    lv = Mathf.Clamp(devLevel, 1, Mathf.Max(1, s.maxLevel));
                }

                // 한 스킬이 레벨마다 여러 coef_type 행을 가질 수 있다(광전사의 힘 402 = 버프 + 자원 소모 + 흡혈).
                // 첫 행만 읽고 break 하면 부가 효과가 통째로 버려지므로 해당 레벨의 모든 행을 타입별로 분류한다.
                float coef = 0f, dur = 0f; int ct = 1;
                float hpCost = 0f, steal = 0f, stealDur = 0f;
                bool primaryFound = false;
                if (s.coefs != null)
                {
                    foreach (var c in s.coefs)
                    {
                        if (c.skillLevel != lv) continue;
                        switch (c.coefType)
                        {
                            case 4: hpCost = c.coef; break;                          // 자원 소모(현재 체력 비율)
                            case 5: steal = c.coef; stealDur = c.duration; break;    // 흡혈
                            default:                                                  // 1 공격 · 2 버프 · 3 디버프
                                if (!primaryFound)
                                {
                                    coef = c.coef; dur = c.duration; ct = c.coefType;
                                    primaryFound = true;
                                }
                                break;
                        }
                    }
                }

                float cd = s.cooldown > 0f ? s.cooldown : (_ctrl != null ? _ctrl.SkillCooldownFallback : 10f);
                float readyIn = added < initReadyIn.Length ? initReadyIn[added] : 2f;
                var sk = new Skill
                {
                    code = s.skillCode, name = s.name, coefType = ct, coef = coef, duration = dur,
                    statType = s.statType,
                    hpCostRatio = hpCost, lifestealRatio = steal, lifestealDuration = stealDur,
                    cooldown = cd, timer = Mathf.Max(0f, cd - readyIn),
                    effect = _cfg != null ? _cfg.EffectFor(s.skillCode) : null,
                    icon = _cfg != null ? _cfg.IconFor(s.skillCode) : null,
                    scale = _cfg != null ? _cfg.ScaleFor(s.skillCode) : 1f,
                    offset = _cfg != null ? _cfg.OffsetFor(s.skillCode) : Vector2.zero,
                    hitTimeRatio = _cfg != null ? _cfg.HitTimeRatioFor(s.skillCode) : 1f,
                };
                _skills.Add(sk);
                if (sk.code == _chargeSkillCode) _chargeSkill = sk;
                added++;
            }
        }

        /// <summary>serverMode: 이 캐릭터가 장착(equipped=1)한 스킬이면 습득 레벨을 돌려준다(미장착이면 false).</summary>
        private bool TryGetEquippedSkillLevel(int skillCode, out int level)
        {
            level = 0;
            var skills = Session.GameData != null ? Session.GameData.skills : null;
            if (skills == null) return false;
            foreach (var s in skills)
                if (s != null && s.characterId == _characterId && s.skillCode == skillCode && s.equipped == 1)
                {
                    level = s.level;
                    return true;
                }
            return false;
        }

        /// <summary>장착/레벨 변경 후 전투에서 사용할 스킬 세트를 세션 기준으로 다시 구성한다(장비 스탯도 함께 갱신).</summary>
        public void RebuildSkills()
        {
            BuildSkills();
        }

        /// <summary>적에게 데미지를 받는다. 체력이 0 이하가 되면 사망 처리한다(1회).</summary>
        public void TakeDamage(long dmg)
        {
            if (_dead) return;
            if (dmg > 0)
            {
                // HP바 연출 시작 — 고스트를 잠깐 붙들어 방금 잃은 양을 남기고, 바를 흔들며 붉게 물들인다.
                _hpGhostHold = HpGhostHoldSeconds;
                _hpBarShakeTimer = HpBarShakeSeconds;
                _hpBarFlashTimer = HpBarFlashSeconds;
            }
            _hp -= dmg;
            if (_hp <= 0)
            {
                _hp = 0;
                Die();
            }
        }

        /// <summary>
        /// HP바 연출 상태를 진행한다 — 고스트 바가 <see cref="HpGhostHoldSeconds"/> 동안 멈춰 방금 깎인 양을
        /// 보여준 뒤 현재 체력까지 내려오고, 흔들림·붉은 점멸 강도가 시간에 따라 0으로 감쇠한다.
        /// <para><b>사망·일시정지와 무관하게 돌고 unscaled 시간을 쓴다</b> — 정지 중에 붉은 점멸이 굳거나,
        /// 히트스톱(timeScale 감속) 동안 0.2초짜리 연출이 몇 배로 늘어지면 안 된다. 전사한 뒤에도 계속 돌아야
        /// 마지막 한 방으로 바가 쭉 빠지는 것이 보인다.</para>
        /// </summary>
        private void TickHpBarJuice(float dt)
        {
            if (_hpBarShakeTimer > 0f) _hpBarShakeTimer -= dt;
            if (_hpBarFlashTimer > 0f) _hpBarFlashTimer -= dt;

            float current = _maxHp > 0 ? Mathf.Clamp01((float)_hp / _maxHp) : 0f;
            if (_hpGhostHold > 0f)
            {
                _hpGhostHold -= dt;
            }
            else if (_hpGhostRatio > current)
            {
                _hpGhostRatio = Mathf.MoveTowards(_hpGhostRatio, current, HpGhostCatchUpPerSec * dt);
            }
            if (_hpGhostRatio < current)
            {
                _hpGhostRatio = current; // 회복으로 현재가 더 높아지면 즉시 맞춘다(고스트는 감소 전용 연출)
            }
        }

        /// <summary>체력 0: 사망 처리 — 사망 애니 재생, 컨트롤러에 통지, 잠시 뒤 자기 소멸.</summary>
        private void Die()
        {
            if (_dead) return;
            _dead = true;
            _charging = false;
            SoundManager.Sfx(SoundId.AllyDeath); // 아군 사망(사운드 정의서 §5.1)
            SendMessage("PlayDeathOnce", SendMessageOptions.DontRequireReceiver);
            if (_ctrl != null) _ctrl.OnAllyKilled(this);
            StartCoroutine(DeathFlight());
        }

        /// <summary>
        /// 사망 연출 — 몬스터 처치와 같은 문법으로 <b>뒤(왼쪽)로 포물선을 그리며 나가떨어지고</b> 회전하다
        /// 서서히 사라진 뒤 파괴된다. 아군만 그 자리에서 스르륵 사라지면 "쓰러졌다"가 잘 읽히지 않는다.
        /// <para>초상화는 이 시점에 이미 정지·흑백으로 고정되므로(<see cref="SkillCooldownUI"/>)
        /// 시신이 날아가도 좌상단 초상화가 함께 날아가거나 비지 않는다.</para>
        /// </summary>
        private IEnumerator DeathFlight()
        {
            var parts = GetComponentsInChildren<SpriteRenderer>(true);
            var baseColors = new Color[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] != null) baseColors[i] = parts[i].color;
            }

            float vx = -DeathLaunchX;   // 왼쪽으로(적에게 맞아 뒤로 밀려나는 방향)
            float vy = DeathLaunchY;
            float spin = DeathSpin * (Random.value < 0.5f ? -1f : 1f);
            Quaternion baseRot = transform.rotation;
            float t = 0f;

            while (t < DeathLinger)
            {
                float dt = Time.deltaTime;
                t += dt;

                var p = transform.position;
                p.x += vx * dt;
                p.y += vy * dt;
                vy -= DeathGravity * dt;   // 포물선
                transform.position = p;
                transform.rotation = baseRot * Quaternion.Euler(0f, 0f, spin * t);

                if (t >= DeathFadeDelay)
                {
                    float k = 1f - Mathf.Clamp01((t - DeathFadeDelay) / Mathf.Max(0.01f, DeathLinger - DeathFadeDelay));
                    for (int i = 0; i < parts.Length; i++)
                    {
                        var r = parts[i];
                        if (r == null) continue;
                        var c = baseColors[i];
                        r.color = new Color(c.r, c.g, c.b, c.a * k);
                    }
                }
                yield return null;
            }
            Destroy(gameObject);
        }

        private void Update()
        {
            // HP바 연출은 사망·일시정지와 무관하게 먼저 진행한다(마지막 한 방의 연출이 끝까지 보이게).
            TickHpBarJuice(Time.unscaledDeltaTime);

            if (_dead) return;
            if (_ctrl == null || _ctrl.IsPaused) return;

            TickTimers();
            if (_busyTimer > 0f) _busyTimer -= Time.deltaTime;

            // 돌진 진행 중이면 그것만 처리(자기 위치를 스스로 이동)
            if (_charging)
            {
                UpdateCharge();
                return;
            }

            bool attacking = IsAttackMotionPlaying();

            // 돌진형 스킬: 모션 중이 아니고 준비됐고 적이 chargeRange 이내면 발동(전진/교전 무관)
            if (_busyTimer <= 0f && !attacking && TryStartCharge())
            {
                return;
            }

            // 전투: 교전 중이면 스킬/기본공격.
            // 마법사(자기중심 광역 캐스터, _allSkillsAoe)는 몬스터 생존/사거리와 무관하게 준비된 스킬을 시전해
            // **이미 몬스터가 죽었어도 스킬 이펙트가 무조건 나가도록** 한다. 기본공격만 대상 사거리를 요구한다.
            if (_ctrl.IsFighting)
            {
                bool monsterReady = _ctrl.MonsterAlive && _ctrl.MonsterTransform != null;
                if (monsterReady) _attackTimer += Time.deltaTime; // 기본공격 간격 누적(기존 동작 유지)
                float dist = monsterReady ? _ctrl.MonsterTransform.position.x - transform.position.x : float.MaxValue;
                bool monsterInRange = monsterReady && dist > 0f && dist <= _attackRange;

                if (_busyTimer <= 0f && !attacking)
                {
                    if (_allSkillsAoe)
                    {
                        // 마법사: 준비된 스킬은 대상 유무·거리와 무관하게 자기 기준으로 시전(이펙트 보장).
                        if (!TryCastSkill() && monsterInRange && _attackTimer >= AttackCooldown)
                        {
                            _attackTimer = 0f;
                            BasicAttack();
                        }
                    }
                    else if (monsterInRange)
                    {
                        // 근접/원거리: 대상이 사거리 안일 때만 스킬/기본공격.
                        if (!TryCastSkill() && _attackTimer >= AttackCooldown)
                        {
                            _attackTimer = 0f;
                            BasicAttack();
                        }
                    }
                }
            }

            // 이동: 대형 목표로 자기 속도 추격. **모션(스킬/공격) 중엔 정지**해 스킬을 끊지 않는다.
            UpdateMovement();
        }

        /// <summary>대형 목표를 자기 이동속도로 추격한다(뒤처지면 가속). 모션 중엔 멈춰 시전을 완료.</summary>
        private void UpdateMovement()
        {
            if (!_hasTarget) return;
            if (_busyTimer > 0f) return; // 스킬/공격 모션 진행 중 → 위치·애니 유지(끊김 방지)
            if (IsAttackMotionPlaying()) return; // 공격 애니가 끝날 때까지 이동 금지(모션 완료 후 이동)

            Vector3 p = transform.position;
            var cur = new Vector2(p.x, p.y);
            float gap = Vector2.Distance(cur, _formTarget);

            // 이동 임계값은 작게(목표가 조금이라도 움직이면 계속 추격) → 걷기 애니가 매 프레임 끊기지 않음
            if (gap > 0.02f)
            {
                float spd = Mathf.Max(0.5f, _moveSpeed) * (1f + gap * 0.6f); // 뒤처질수록 가속
                var next = Vector2.MoveTowards(cur, _formTarget, spd * Time.deltaTime);
                transform.position = new Vector3(next.x, next.y, 0f);
                _stillTime = 0f;
                SetMoving(true);
            }
            else
            {
                // 목표에 도달해도 잠깐의 정지로는 idle로 바꾸지 않음(전진 중 미세 정지에 의한 애니 튐 방지).
                // 일정 시간(0.15s) 이상 멈춰 있을 때만 idle로 전환.
                _stillTime += Time.deltaTime;
                if (_stillTime >= 0.15f) SetMoving(false);
            }
        }

        private void SetMoving(bool moving)
        {
            if (moving == _moving) return;
            _moving = moving;
            SendMessage(moving ? "PlayMove" : "PlayIdle", SendMessageOptions.DontRequireReceiver);
        }

        /// <summary>
        /// 공격 애니(SPUM "…Attack…" 클립)가 아직 재생 중인지. 트랜지션 중이거나, 현재 클립명이 "attack"을
        /// 포함하고 정규화 진행도가 1 미만이면 true. 이걸로 공격 모션이 완전히 끝난 뒤에만 이동한다.
        /// (캐스트홀드/화살비 정지도 attack 클립을 nt<1로 고정하므로 이동이 막힌다.)
        /// </summary>
        private bool IsAttackMotionPlaying()
        {
            if (_anim == null) return false;
            var ci = _anim.GetCurrentAnimatorClipInfo(0);
            if (ci == null || ci.Length == 0 || ci[0].clip == null) return false;
            if (ci[0].clip.name.ToLower().IndexOf("attack") < 0) return false;
            // 공격 클립이 아직 1회 재생을 끝내지 않았으면(정규화<1) 모션 진행 중.
            // (시작 순간의 트랜지션 구간은 _busyTimer가 커버하므로 여기선 현재 클립만 본다.)
            return _anim.GetCurrentAnimatorStateInfo(0).normalizedTime < 1f;
        }

        private void TickTimers()
        {
            foreach (var sk in _skills)
                if (sk.timer < sk.cooldown) sk.timer += Time.deltaTime;

            if (_buffTimer > 0f)
            {
                _buffTimer -= Time.deltaTime;
                if (_buffTimer <= 0f)
                {
                    _atkBuffMult = 1f;
                    _cooldownBuffMult = 1f;
                    if (_buffEffect != null) { Destroy(_buffEffect); _buffEffect = null; }
                }
            }

            // 흡혈은 버프와 별도 지속시간을 가질 수 있어 따로 센다(광전사의 힘은 둘 다 6초).
            if (_lifestealTimer > 0f)
            {
                _lifestealTimer -= Time.deltaTime;
                if (_lifestealTimer <= 0f)
                {
                    _lifestealRatio = 0f;
                }
            }
        }

        // ---- 스킬 시전(돌진 제외) ----

        private bool TryCastSkill()
        {
            foreach (var sk in _skills)
            {
                if (sk.code == _chargeSkillCode) continue; // 돌진은 별도 경로
                if (sk.timer >= sk.cooldown)
                {
                    CastSkill(sk);
                    sk.timer = 0f;
                    return true;
                }
            }
            return false;
        }

        private void PlayAttackAnim()
        {
            _moving = false; // 공격 애니가 idle로 복귀하므로 다음 이동 시 PlayMove 재전송되게
            if (string.IsNullOrEmpty(_attackAnim))
                SendMessage("PlayAttackOnce", SendMessageOptions.DontRequireReceiver);
            else
                SendMessage("PlayAttackByName", _attackAnim, SendMessageOptions.DontRequireReceiver);
        }

        /// <summary>
        /// 기본 공격 주기(초). 마스터 데이터의 cooldown에 statType 7 버프 배율을 곱한다 —
        /// 광전사의 힘은 배율이 1보다 작아 평타가 그만큼 빨라진다.
        /// </summary>
        private float AttackCooldown => Mathf.Max(0.05f, _cooldown * _cooldownBuffMult);

        /// <summary>
        /// 자버프 이펙트를 <b>캐릭터 스프라이트 뒤, 던전 배경 앞</b>에 놓는다(기사의 분노·광전사의 힘).
        /// 이펙트가 캐릭터를 덮으면 무슨 캐릭터가 무엇을 하는지 안 보이므로 아우라는 뒤에서 번지게 하되,
        /// 배경보다는 앞이어야 한다.
        /// <para><b>기준은 캐릭터 묶음의 <see cref="SortingGroup"/> 순서</b>다. SPUM 캐릭터는 그룹이
        /// 루트가 아니라 자식(<c>UnitRoot</c>)에 있고 자버프 이펙트는 캐릭터 <b>루트</b>의 자식이라,
        /// 이펙트는 그 그룹 <b>밖</b>에 있고 자기 sortingOrder가 <b>전역 값</b>으로 해석된다.
        /// 그래서 캐릭터 파트의 정렬값(그룹 안에서만 의미 있는 상대값)을 기준으로 삼으면 안 된다 —
        /// 실제로 파트 스캔 결과가 -100까지 내려가 이펙트가 -101이 되었고, 던전 배경(-100)보다 뒤라
        /// <b>아우라가 배경에 가려 보이지 않았다</b>(2026-08-13 실측·수정).</para>
        /// <para>그룹을 못 찾는 경우에만 예전처럼 파트를 훑고, 어느 경로든 <see cref="MinBuffEffectOrder"/>로
        /// 하한을 걸어 배경 뒤로 내려가지 않게 한다.</para>
        /// </summary>
        private void SendBuffEffectBehind(GameObject effect)
        {
            if (effect == null)
            {
                return;
            }

            int reference;
            var charGroup = GetComponentInChildren<UnityEngine.Rendering.SortingGroup>(true);
            if (charGroup != null)
            {
                reference = charGroup.sortingOrder; // 캐릭터 묶음 전체의 전역 순서(아군 20 / 적 10)
            }
            else
            {
                reference = int.MaxValue;
                foreach (var sr in GetComponentsInChildren<SpriteRenderer>(true))
                {
                    // 이펙트 자신은 기준에서 뺀다(방금 붙었으므로 이 시점엔 아직 옛 값이다).
                    if (sr.transform.IsChildOf(effect.transform))
                    {
                        continue;
                    }
                    if (sr.sortingOrder < reference)
                    {
                        reference = sr.sortingOrder;
                    }
                }
                if (reference == int.MaxValue)
                {
                    reference = 0;
                }
            }

            int target = Mathf.Max(reference - BuffEffectSortingGap, MinBuffEffectOrder);

            var group = effect.GetComponentInChildren<UnityEngine.Rendering.SortingGroup>(true);
            if (group != null)
            {
                group.sortingOrder = target;
                return;
            }

            foreach (var r in effect.GetComponentsInChildren<Renderer>(true))
            {
                r.sortingOrder = target;
            }
        }

        /// <summary>
        /// 자기 버프의 대상 스탯을 <c>skill_master.stat_type</c>으로 정해 적용한다(하드코딩 제거).
        /// 1=공격력 배율(기사의 분노), 7=공격 주기 배율(광전사의 힘 — 값이 1보다 작아 주기 단축).
        /// 그 외 스탯(방어·치명 등)은 이 개발 하네스의 전투 계산에 반영 대상이 없어 적용하지 않는다.
        /// </summary>
        private void ApplyStatBuff(Skill sk)
        {
            switch (sk.statType)
            {
                case 1:
                    _atkBuffMult = Mathf.Max(1f, sk.coef);
                    break;
                case 7:
                    _cooldownBuffMult = Mathf.Clamp(sk.coef, 0.1f, 1f);
                    break;
                default:
                    // 계산 대상이 없는 스탯이면 지속시간·연출만 유지하고 수치는 건드리지 않는다.
                    break;
            }
        }

        /// <summary>
        /// 자원 소모(coefType 4). 시전 순간 <b>현재</b> 체력의 <paramref name="sk"/>.hpCostRatio 만큼을 잃는다.
        /// 고정값이 아니라 현재 체력 비율이므로 체력이 낮을수록 손실도 줄고, 이 소모로 죽지는 않는다(최소 1 유지).
        /// </summary>
        private void PayHpCost(Skill sk)
        {
            if (sk.hpCostRatio <= 0f || _dead)
            {
                return;
            }
            long cost = (long)(_hp * sk.hpCostRatio);
            if (cost <= 0)
            {
                return;
            }
            _hp = System.Math.Max(1L, _hp - cost);
        }

        /// <summary>흡혈(coefType 5)을 지속시간만큼 켠다. 지속시간이 없으면 버프 지속시간을 따른다.</summary>
        private void StartLifesteal(Skill sk)
        {
            if (sk.lifestealRatio <= 0f)
            {
                return;
            }
            _lifestealRatio = sk.lifestealRatio;
            _lifestealTimer = sk.lifestealDuration > 0f ? sk.lifestealDuration : Mathf.Max(0.1f, sk.duration);
        }

        /// <summary>체력을 최대치로 되돌린다(스테이지 시작 시 파티 전원 회복). 사망 상태에서는 무시한다 —
        /// 전사자는 컨트롤러가 다시 스폰하므로 여기서 되살리지 않는다.</summary>
        public void RestoreFullHp()
        {
            if (_dead)
            {
                return;
            }
            _hp = _maxHp;
        }

        /// <summary>체력을 회복한다(최대 체력 초과 없음, 사망 후에는 무시).</summary>
        public void Heal(long amount)
        {
            if (_dead || amount <= 0)
            {
                return;
            }
            _hp = System.Math.Min(_maxHp, _hp + amount);
        }

        /// <summary>
        /// 단일 대상 데미지를 컨트롤러에 넘기면서 흡혈을 함께 처리한다.
        /// 모든 데미지 경로가 이 창구를 지나므로 기본공격·스킬·투사체·돌진 어디서든 흡혈이 동작한다.
        /// <paramref name="bigHit"/>는 <b>스킬 타격</b> 표시로, 컨트롤러가 히트스톱·셰이크를 걸지 판단하는 데만 쓴다.
        /// <paramref name="knockback"/>은 대상이 밀려날 거리다 — <b>기본공격은 0</b>(밀지 않음),
        /// 스킬은 <see cref="MonsterUnit.SkillKnockback"/>, 방패 돌진은 <see cref="MonsterUnit.ChargeKnockback"/>.
        /// </summary>
        private void DealDamage(float delay, long dmg, bool crit, string label, bool bigHit = false,
                                float knockback = 0f)
        {
            _ctrl.DealDamageAfter(delay, dmg, crit, label, bigHit, knockback);
            ScheduleLifesteal(delay, dmg);
        }

        /// <summary>광역 데미지 + 흡혈. 회복량은 대상 1기분 피해 기준이다(적중 수만큼 배로 늘리지 않는다).
        /// <paramref name="knockbackAll"/>가 true면 맞은 적 <b>전부</b>가 밀려난다(방패 돌진 — 밀치는 것이 곧 스킬).</summary>
        private void DealAreaDamage(float delay, long dmg, bool crit, string label, Vector3 center, float radius,
                                    bool bigHit = false, float knockback = 0f, bool knockbackAll = false)
        {
            _ctrl.DealAreaDamageAfter(delay, dmg, crit, label, center, radius, bigHit, knockback, knockbackAll);
            ScheduleLifesteal(delay, dmg);
        }

        /// <summary>흡혈이 켜져 있으면 데미지가 들어가는 시점에 맞춰 회복시킨다.</summary>
        private void ScheduleLifesteal(float delay, long dmg)
        {
            if (_lifestealTimer <= 0f || _lifestealRatio <= 0f || dmg <= 0)
            {
                return;
            }
            long heal = System.Math.Max(1L, (long)(dmg * _lifestealRatio));
            if (delay <= 0f)
            {
                Heal(heal);
            }
            else
            {
                StartCoroutine(HealAfter(delay, heal));
            }
        }

        private IEnumerator HealAfter(float delay, long heal)
        {
            yield return new WaitForSeconds(delay);
            Heal(heal);
        }

        /// <summary>
        /// 근접 기본공격용 lunge — 앞(적 방향 = +x)으로 짧게 파고들었다 제자리로 돌아온다.
        /// <para><b>절대 좌표가 아니라 "이번 프레임에 더할 차이"만 적용한다</b>(<see cref="_lungeApplied"/>).
        /// 대형 추격(<see cref="UpdateMovement"/>)·돌진이 같은 transform을 움직이므로, 절대 위치로 되돌리면
        /// 그 이동을 취소해 캐릭터가 뒤로 끌린다.</para>
        /// </summary>
        private void StartLunge()
        {
            if (_lungeAnim != null)
            {
                StopCoroutine(_lungeAnim);
                ApplyLungeOffset(0f); // 진행 중이던 lunge를 정리하고 다시 시작
            }
            _lungeAnim = StartCoroutine(LungeRoutine());
        }

        private IEnumerator LungeRoutine()
        {
            float t = 0f;
            while (t < LungeOutSeconds)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / LungeOutSeconds);
                ApplyLungeOffset(Mathf.Sin(k * Mathf.PI * 0.5f) * LungeDistance); // ease-out으로 튀어나감
                yield return null;
            }
            t = 0f;
            while (t < LungeBackSeconds)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / LungeBackSeconds);
                ApplyLungeOffset(Mathf.Lerp(LungeDistance, 0f, k));
                yield return null;
            }
            ApplyLungeOffset(0f);
            _lungeAnim = null;
        }

        /// <summary>lunge 오프셋을 목표값으로 맞춘다(차이만 위치에 더한다).</summary>
        private void ApplyLungeOffset(float offset)
        {
            float delta = offset - _lungeApplied;
            if (Mathf.Approximately(delta, 0f)) return;
            var p = transform.position;
            p.x += delta;
            transform.position = p;
            _lungeApplied = offset;
        }

        /// <summary>
        /// 스킬 코드별 화면 효과를 타격 시점에 맞춰 예약한다 — <b>스킬마다 다른 기억</b>을 남기는 장치다.
        /// <list type="bullet">
        /// <item>프로스트 노바(302): 범위 내 적 <b>파란 틴트 + 이동 정지</b>(빙결 시각화)</item>
        /// <item>라이트닝 볼트(303): <b>화면 전체 백색 섬광</b> 한 번</item>
        /// <item>강타(103)·내려찍기(401): 바닥에 <b>지속 데칼</b>(전투가 지나간 흔적)</item>
        /// </list>
        /// <para>스킬 코드로 분기하는 이유: 이 셋은 마스터 데이터의 수치(계수·범위)로는 구분되지 않는
        /// <b>연출 고유 특성</b>이다. 코드는 마스터 데이터의 고정 키라 안전한 분기 기준이다.</para>
        /// <para>데미지·상태에는 손대지 않는다(빙결은 이동만 멈추고 공격 주기는 그대로 — 연출이 난이도를
        /// 바꾸지 않게 한다).</para>
        /// </summary>
        private void ApplySkillScreenEffect(Skill sk, float hitDelay)
        {
            switch (sk.code)
            {
                case FrostNovaSkillCode:
                {
                    if (_allSkillsAoe)
                    {
                        // 전스킬 광역 캐스터(마법사)는 휩쓸기 판정이 데미지와 빙결을 함께 처리한다
                        // (얼음이 닿은 적만 언다) — 여기서 별도 원 판정으로 얼리면 그림과 어긋난다.
                        break;
                    }
                    Vector3 center = TargetOrForwardPos();
                    _ctrl.FreezeEnemiesNear(hitDelay, center, FrostNovaFreezeRadius, FrostNovaFreezeSeconds);
                    break;
                }
                case LightningBoltSkillCode:
                    _ctrl.FlashScreenAfter(hitDelay, LightningFlashColor, LightningFlashSeconds);
                    break;
                case KnightPowerStrikeSkillCode:
                    _ctrl.SpawnGroundDecalAfter(hitDelay, TargetOrForwardPos(), DecalWidthStrike);
                    break;
                // 내려찍기는 착지 시점에 데칼을 남긴다(SlamImpactAfter에서 직접 호출).
            }
        }

        /// <summary>대상(최전방 몬스터) 위치, 없으면 자기 앞쪽 사거리 절반 지점(연출 기준점).</summary>
        private Vector3 TargetOrForwardPos()
        {
            var t = _ctrl.MonsterTransform;
            return t != null
                ? t.position
                : transform.position + Vector3.right * Mathf.Max(0.5f, _attackRange * 0.5f);
        }

        /// <summary>지연 뒤 효과음을 1회 재생한다(타격 시점과 소리를 맞춰야 하는 임팩트음용 — 사운드 정의서 §9.3).</summary>
        private IEnumerator PlaySfxAfter(float delay, SoundId id)
        {
            if (delay > 0f)
            {
                yield return new WaitForSeconds(delay);
            }
            SoundManager.Sfx(id);
        }

        private void CastSkill(Skill sk)
        {
            _moving = false; // 스킬 모션 후 idle 복귀 → 이후 이동 시 PlayMove 재전송
            // 스킬 시전음(사운드 정의서 §5.2~§5.5). 타격이 시전과 떨어진 스킬(내려찍기·화살비)의 임팩트음은
            // 그 도달 콜백에서 따로 울린다(§9.3 동기화 주의 지점).
            SoundManager.Sfx(BattleSounds.SkillFor(sk.code));

            if (sk.coefType == 2) // 버프(자기 강화)
            {
                SendMessage("PlayRage", SendMessageOptions.DontRequireReceiver); // 분노/기합 자세
                ApplyStatBuff(sk);
                _buffTimer = sk.duration > 0f ? sk.duration : 5f;
                PayHpCost(sk);        // 자원 소모(coefType 4) — 광전사의 힘: 현재 체력의 일정 비율
                StartLifesteal(sk);   // 흡혈(coefType 5)
                if (_buffEffect != null) { Destroy(_buffEffect); _buffEffect = null; }
                if (sk.effect != null)
                {
                    _buffEffect = Instantiate(sk.effect, transform, false);
                    _buffEffect.transform.localPosition =
                        new Vector3(sk.offset.x, _ctrl.EffectYOffset + sk.offset.y, 0f);
                    SendBuffEffectBehind(_buffEffect);
                }

                // 자버프(기사의 분노·광전사의 힘)는 무기 잔상을 건드리지 않는다 — 잔상 색·반짝임은
                // 오직 장착 무기의 강화 단계만 따른다(2026-08-06). 버프 표현은 위 발동 이펙트가 맡는다.
                // 화면 가장자리를 붉게 물들이는 비네트도 넣지 않는다 — 광전사의 힘은 재사용 대기시간이
                // 짧아 화면이 거의 상시 붉어져 시야를 방해했다(2026-08-04 제거).

                _busyTimer = _ctrl.BasicHitDelay;
            }
            else // 공격 스킬
            {
                long dmg = Damage(sk.coef, out bool crit);
                float motion = EffectDuration(sk.effect);
                // 데미지 타격 시점 — 기본은 이펙트 종료 시점(비율 1)이고, 이펙트가 빠르게 터지는 스킬은
                // 멤버 설정의 hitTimeRatio로 앞당긴다(모션·쿨타임은 motion 그대로 유지).
                float hitDelay = motion * (sk.hitTimeRatio > 0f ? sk.hitTimeRatio : 1f);
                string label = $"[{_name}] 스킬 {sk.name} ×{sk.coef:0.##}";
                ApplySkillScreenEffect(sk, hitDelay); // 스킬별 화면 효과(빙결 틴트·섬광·지속 데칼)

                if (_rainSkillCode != 0 && sk.code == _rainSkillCode)
                {
                    // 화살비: 점프→공중에서 활 하늘로 든 채 정지→대상 위치에 이펙트.
                    // 몬스터가 이미 죽었/없어도 시전 시 무조건 연출되도록 대상 위치를 캡처해 무조건 스폰.
                    _moving = false;
                    SendMessage("PlayArrowRain", motion, SendMessageOptions.DontRequireReceiver);
                    Vector3 rainPos = ArrowRainTargetPos(); // 한 번만 구해 이펙트와 판정이 같은 지점을 쓰게 한다
                    var rainFx = SpawnEffectAt(sk.effect, rainPos, sk.scale);
                    // 화살비 착탄음은 데미지가 들어가는 시점에 맞춘다(시전음은 활 소리, §8).
                    StartCoroutine(PlaySfxAfter(hitDelay, SoundId.ArcherArrowImpact));
                    // 넓게 쏟아지는 비 그림대로 <b>범위 안의 적 전부</b>가 맞는다(단일 대상 아님).
                    // 판정 크기는 다른 광역기와 같은 기준 — 스폰된 이펙트의 렌더 크기 그대로다.
                    // 다만 중심은 그림 중앙(공중)이 아니라 <b>대상의 발밑 라인</b>으로 내린다 — 비는 지면에
                    // 떨어지는데 중심을 EffectYOffset만큼 띄우면 원 판정의 가로 도달이 그만큼 줄어
                    // (1.85 → 1.75) 그림 가장자리에 선 적이 빠진다. 적은 발밑 y로 판정되므로 이렇게 맞춘다.
                    Vector3 rainCenter = new Vector3(EffectCenter(rainFx, rainPos).x,
                                                     rainPos.y - _ctrl.EffectYOffset, 0f);
                    DealAreaDamage(hitDelay, dmg, crit, label, rainCenter, EffectRadius(rainFx),
                                   bigHit: true, knockback: MonsterUnit.SkillKnockback);
                    _busyTimer = motion + 0.4f; // 점프+홀드+착지 동안 대기
                }
                else if (_slamSkillCode != 0 && sk.code == _slamSkillCode)
                {
                    // 내려찍기: 공격 애니를 재생한 채 솟구쳐 올랐다 빠르게 낙하 →
                    // **착지한 뒤에** 지면 이펙트와 데미지가 나간다(공중에서 터지지 않게).
                    SendMessage("PlayGroundSlam", _slamAirTime, SendMessageOptions.DontRequireReceiver);
                    StartCoroutine(SlamImpactAfter(_slamAirTime, sk, dmg, crit, label));
                    _busyTimer = _slamAirTime + motion; // 상승·낙하·이펙트 동안 이동/다음 행동 금지
                }
                else if (_allSkillsAoe && sk.effect != null)
                {
                    // 캐스터 전(全)스킬 광역(마법사): 이펙트를 **자기 위치 기준**으로 쏘고(대상 위치가 아님),
                    // 그 이펙트가 앞으로 뻗어 나가며 <b>닿은 적을 모두</b> 때린다(휩쓸기 판정).
                    // 몬스터가 이미 죽었어도 이펙트는 무조건 나간다.
                    //
                    // 시전 시점의 이펙트 크기를 반경으로 쓰는 원 판정(DealAreaDamage)은 쓸 수 없다 —
                    // 마법사 이펙트는 시전자 손끝의 작은 불씨(반경 0.2 남짓)로 시작해 프레임이 지나며 앞으로
                    // 자라는 그림이라, 시전 순간 크기로 재면 판정이 시전자 발밑에만 생겨 **아무도 맞지 않는다**
                    // (파이어볼이 허공을 치는 것처럼 보이던 원인).
                    if (_castHold)
                        SendMessage("PlayCastHold", motion, SendMessageOptions.DontRequireReceiver);
                    else
                        PlayAttackAnim();
                    Vector3 center = SelfEffectPos(sk.offset);
                    var fx = SpawnEffectAt(sk.effect, center, sk.scale);
                    StretchCasterEffectToTarget(fx, center);
                    // 프로스트 노바는 같은 판정으로 빙결까지 처리한다(얼음이 닿은 적만 언다).
                    float freeze = sk.code == FrostNovaSkillCode ? FrostNovaFreezeSeconds : 0f;
                    _ctrl.DealSweepDamage(fx, dmg, crit, label, CasterSweepHalfHeight, bigHit: true,
                        knockback: MonsterUnit.SkillKnockback, freezeSeconds: freeze);
                    ScheduleLifesteal(hitDelay, dmg);
                    _busyTimer = motion;
                }
                else if (_ranged && sk.effect != null && _ctrl.MonsterTransform != null)
                {
                    // 원거리(레인저): 스킬 이펙트를 투사체로 발사(자기 위치 → 대상).
                    PlayAttackAnim();
                    Vector3 origin = transform.position + Vector3.up * _ctrl.EffectYOffset;
                    var fx = Instantiate(sk.effect, origin, Quaternion.identity);
                    if (sk.scale > 0f && sk.scale != 1f) fx.transform.localScale *= sk.scale; // 이펙트 크기 배율(정조준·다중 사격 2배 등)
                    var proj = fx.GetComponent<ProjectileEffect>();
                    if (proj == null) proj = fx.AddComponent<ProjectileEffect>();

                    if (_aoeSkillCodes.Contains(sk.code))
                    {
                        // 광역(관통) 지정 스킬: 대상 한 기만 맞히는 대신, <b>날아가는 이펙트가 훑고 지나간 적을
                        // 모두</b> 1회씩 맞힌다(정조준 사격). 판정을 그려진 궤적에 맡기므로 관통 사거리·굵기를
                        // 따로 정하지 않아도 그림과 어긋나지 않는다.
                        _ctrl.DealSweepDamage(fx, dmg, crit, label, RangedSweepHalfHeight, bigHit: true,
                            knockback: MonsterUnit.SkillKnockback);
                        ScheduleLifesteal(hitDelay, dmg);
                        proj.Launch(_ctrl.MonsterTransform, _arrowSpeed, _ctrl.EffectYOffset, null);
                    }
                    else
                    {
                        // 단일 대상: 종전대로 도달 시점에 대상에게만 데미지.
                        proj.Launch(_ctrl.MonsterTransform, _arrowSpeed, _ctrl.EffectYOffset,
                                    () => DealDamage(0f, dmg, crit, label, bigHit: true, knockback: MonsterUnit.SkillKnockback));
                    }
                    _busyTimer = motion;
                }
                else
                {
                    // 근접/캐스터: 자기 위치에서 이펙트 발생 + 이펙트 종료 후 데미지
                    if (_castHold)
                        SendMessage("PlayCastHold", motion, SendMessageOptions.DontRequireReceiver);
                    else
                        PlayAttackAnim();
                    var fx = SpawnEffectAtSelf(sk.effect, sk.offset);
                    if (fx != null && sk.scale > 0f && sk.scale != 1f) fx.transform.localScale *= sk.scale;
                    if (_aoeSkillCodes.Contains(sk.code))
                    {
                        // 광역(강타·강한일격 등): 이펙트 범위 내 모든 적에게 데미지.
                        DealAreaDamage(hitDelay, dmg, crit, label,
                            EffectCenter(fx, SelfEffectPos(sk.offset)), EffectRadius(fx), bigHit: true,
                            knockback: MonsterUnit.SkillKnockback);
                    }
                    else
                    {
                        DealDamage(hitDelay, dmg, crit, label, bigHit: true, knockback: MonsterUnit.SkillKnockback);
                    }
                    _busyTimer = motion;
                }
            }
        }

        private void BasicAttack()
        {
            PlayAttackAnim();
            SoundManager.Sfx(BattleSounds.BasicAttackFor(_classCode)); // 직업별 기본 공격음(§5.2~§5.5)
            long dmg = Damage(1f, out bool crit);

            if (_basicAttackProjectile != null && _ctrl.MonsterTransform != null)
            {
                // 캐스터(마법사 등): 기본공격을 투사체로 발사(자기 위치 → 대상). 도달 시 데미지.
                Vector3 origin = transform.position + Vector3.up * _ctrl.EffectYOffset;
                var fx = Instantiate(_basicAttackProjectile, origin, Quaternion.identity);
                if (_basicAttackProjectileScale > 0f && _basicAttackProjectileScale != 1f)
                {
                    fx.transform.localScale *= _basicAttackProjectileScale;
                }
                var proj = fx.GetComponent<ProjectileEffect>();
                if (proj == null) proj = fx.AddComponent<ProjectileEffect>();
                string label = $"[{_name}] → 몬스터";
                // 기본공격은 <b>명중하는 순간 투사체가 사라진다</b>(화살과 같은 규칙) — 남은 프레임을 이어
                // 재생하면 맞은 자리에서 불길이 더 번지는 것처럼 보여 한 대 때린 시점이 흐려진다.
                proj.Launch(_ctrl.MonsterTransform, _arrowSpeed, _ctrl.EffectYOffset,
                            () => DealDamage(0f, dmg, crit, label), stopOnArrive: true);
                _busyTimer = _ctrl.BasicHitDelay;
            }
            else if (_ranged && _arrowPrefab != null)
            {
                var monster = _ctrl.MonsterTransform;
                Vector3 origin = transform.position + Vector3.up * _ctrl.EffectYOffset;
                var arrowGo = Instantiate(_arrowPrefab, origin, Quaternion.identity);
                var arrow = arrowGo.GetComponent<ArrowProjectile>();
                string label = $"[{_name}] 화살 → 몬스터";
                if (arrow != null)
                    arrow.Launch(monster, _arrowSpeed, _ctrl.EffectYOffset, () => DealDamage(0f, dmg, crit, label));
                else
                    DealDamage(_ctrl.BasicHitDelay, dmg, crit, label);
                _busyTimer = _ctrl.BasicHitDelay;
            }
            else // 근접
            {
                StartLunge(); // 제자리 스윙 대신 살짝 찔러 들어갔다 복귀
                if (_basicAttackAoe)
                {
                    // 광역 기본공격: 대상(최전방 몬스터) 위치를 중심으로 사거리 내 모든 적에게 명중.
                    var target = _ctrl.MonsterTransform;
                    Vector3 center = target != null
                        ? target.position
                        : transform.position + Vector3.right * Mathf.Max(1f, _attackRange * 0.5f) + Vector3.up * _ctrl.EffectYOffset;
                    DealAreaDamage(_ctrl.BasicHitDelay, dmg, crit, $"[{_name}] 광역 → 적", center, _attackRange);
                }
                else
                {
                    DealDamage(_ctrl.BasicHitDelay, dmg, crit, $"[{_name}] → 몬스터");
                }
                _busyTimer = _ctrl.BasicHitDelay;
            }
        }

        // ---- 돌진형 스킬 ----

        private bool TryStartCharge()
        {
            if (_chargeSkill == null || _chargeSkill.timer < _chargeSkill.cooldown) return false;
            if (!_ctrl.MonsterAlive || _ctrl.MonsterTransform == null) return false;

            float dist = _ctrl.MonsterTransform.position.x - transform.position.x;
            if (dist <= 0f || dist > _chargeRange) return false;

            _charging = true;
            _moving = false;
            _chargeSkill.timer = 0f;
            _chargeImpacted = false;
            _chargeElapsed = 0f;
            // 충돌하기 전까지는 시간 제한을 두지 않는다 — 자세가 보이는 시간은 준비 동작(뒤로 도약)이 보장한다.
            _chargeEndAt = float.MaxValue;

            // 준비 동작부터 시작한다 — 제자리에서 바로 튀어나가지 않고 <b>살짝 뒤로 물러섰다가</b> 돌진한다.
            // 이펙트와 개시음은 물러남이 끝나고 앞으로 튀어나가는 순간에 터뜨린다(그때가 이 스킬의 시작점이다).
            _chargeWindup = ChargeWindupSeconds;
            _windupApplied = 0f;
            _chargeStartX = transform.position.x;
            SendMessage("PlayChargeDash", SendMessageOptions.DontRequireReceiver); // 물러나는 동안 자세를 잡는다
            // 물러남을 <b>뒤로 도약</b>으로 보여 준다 — x는 여기서, y 포물선은 애니메이터가 같은 시간 동안 그린다.
            // 시간·높이를 함께 넘겨 도약 연출 수치가 이 스킬 상수들과 한자리에 모이게 한다(Vector2 = 시간, 높이).
            SendMessage("PlayBackJump", new Vector2(ChargeWindupSeconds, ChargeWindupJumpHeight),
                SendMessageOptions.DontRequireReceiver);
            return true;
        }

        /// <summary>
        /// 물러남이 끝나고 <b>앞으로 튀어나가는 순간</b> — 돌진 개시음을 낸다.
        /// <para>스킬 이펙트는 여기서 두르지 않는다. 방패 돌진의 이펙트는 <b>부딪히는 순간 터지는 폭발</b>이라
        /// 충돌 지점에서 1회 재생한다(<see cref="SpawnChargeImpactFx"/>).</para>
        /// </summary>
        private void BeginChargeDash()
        {
            SoundManager.Sfx(BattleSounds.SkillFor(_chargeSkill.code)); // 돌진 개시음(§5.2)
            _chargeElapsed = 0f; // 자세 유지 시간은 물러남이 아니라 돌진부터 센다
        }

        /// <summary>
        /// 충돌 지점에 방패 돌진 폭발 이펙트를 1회 터뜨린다 — 자기 자식이 아니라 <b>월드에</b> 놓아
        /// 돌진 자세가 끝나도(캐릭터가 움직여도) 그 자리에서 흩어지게 한다. 재생이 끝나면 스스로 사라진다.
        /// 대상 위치를 쓰되, 대상이 없으면 방패가 닿는 자기 앞쪽으로 폴백한다.
        /// </summary>
        private void SpawnChargeImpactFx()
        {
            if (_chargeSkill.effect == null)
            {
                return;
            }
            var target = _ctrl.MonsterTransform;
            Vector3 pos = target != null
                ? target.position
                : transform.position + Vector3.right * _attackRange;
            SpawnEffectAt(_chargeSkill.effect, pos + Vector3.up * _ctrl.EffectYOffset, _chargeSkill.scale);
        }

        /// <summary>
        /// 준비 동작: 적 반대쪽(뒤)으로 <see cref="ChargeWindupDistance"/>만큼 물러난다.
        /// <para><b>절대 좌표가 아니라 이번 프레임에 더할 차이만 적용한다</b>(<see cref="_windupApplied"/>) —
        /// 근접 lunge와 같은 이유로, 절대 위치로 되돌리면 다른 이동 처리를 취소해 캐릭터가 끌린다.</para>
        /// <para>뒤로 빼는 양은 감속(ease-out)해 "주춤했다가 튀어나간다"로 읽히게 한다.</para>
        /// </summary>
        private void UpdateChargeWindup()
        {
            _chargeWindup -= Time.deltaTime;
            float k = 1f - Mathf.Clamp01(_chargeWindup / ChargeWindupSeconds);
            float target = -ChargeWindupDistance * Mathf.Sin(k * Mathf.PI * 0.5f); // ease-out으로 뒤로 뺀다
            float delta = target - _windupApplied;
            if (!Mathf.Approximately(delta, 0f))
            {
                var p = transform.position;
                p.x += delta;
                transform.position = p;
                _windupApplied = target;
            }
            if (_chargeWindup <= 0f)
            {
                _chargeWindup = 0f;
                BeginChargeDash();
            }
        }

        private float ChargeSpeedEffective() => Mathf.Max(_chargeSpeed, _moveSpeed * 1.5f);

        /// <summary>돌진 진행: <b>준비 동작(뒤로 도약) → 전진 → 충돌 → 짧은 마무리</b> 순으로 돈다.
        /// 충돌하기 전에는 시간 제한 없이 전진하고, 부딪히는 순간 데미지·이펙트 정리를 끝낸 뒤
        /// <see cref="ChargeImpactWrapUpSeconds"/>만큼만 자세를 남기고 종료한다.</summary>
        private void UpdateCharge()
        {
            // 준비 동작(뒤로 물러나기)이 남아 있으면 그것부터 — 돌진은 물러남이 끝난 뒤 시작한다.
            if (_chargeWindup > 0f)
            {
                UpdateChargeWindup();
                return;
            }

            _chargeElapsed += Time.deltaTime;

            if (!_chargeImpacted)
            {
                // 타격 전에 대상이 사라졌으면 유지할 이유가 없으므로 즉시 종료.
                if (!_ctrl.MonsterAlive || _ctrl.MonsterTransform == null)
                {
                    EndCharge();
                    return;
                }

                // 돌진 목표 x — 사거리에 닿는 지점이되, <b>최소한 물러나기 전 자리까지는 되돌아온다</b>.
                // 이 하한이 없으면 물러난 뒤 적이 이미 사거리 안이라 전진 거리가 0이 되어,
                // 뒤로 빠지기만 하고 돌진이 보이지 않는다(실측: 3회 중 2회가 전진 0).
                float targetX = Mathf.Max(_ctrl.MonsterTransform.position.x - _attackRange, _chargeStartX);
                if (transform.position.x < targetX - 0.01f)
                {
                    Vector3 p = transform.position;
                    p.x = Mathf.Min(targetX, p.x + ChargeSpeedEffective() * Time.deltaTime);
                    p.y = _ctrl.PathY;
                    transform.position = p;
                    return;
                }

                // 부딪혔다. <b>부딪힌 순간이 곧 타격</b>이다 — 데미지·충돌음을 지연 없이 넣고, 방패 이펙트도
                // 그 자리에서 정리한 뒤 짧게(<see cref="ChargeImpactWrapUpSeconds"/>) 마무리하고 끝낸다.
                // 예전에는 이펙트 길이만큼(_chargeMotion) 자세를 더 유지해, <b>이미 부딪혔는데도 계속 앞으로
                // 밀고 나가는 자세</b>가 남아 부자연스러웠다.
                _chargeImpacted = true;
                _chargeEndAt = _chargeElapsed + ChargeImpactWrapUpSeconds;
                long dmg = Damage(_chargeSkill.coef, out bool crit);
                SoundManager.Sfx(SoundId.KnightShieldChargeImpact); // 방패가 터지는 파열음(§5.2)
                SpawnChargeImpactFx();                             // 부딪힌 자리에 폭발 이펙트
                // 방패 돌진은 '밀치는 것'이 스킬의 정체성이라 <b>방패에 부딪힌 전방의 적 전부</b>가 대상이다 —
                // 예전에는 데미지가 단일 대상뿐이고 나머지는 밀리기만 했지만, 이제 맞은 적 모두가
                // 데미지와 넉백을 함께 받는다(knockbackAll). 판정 범위는 종전에 밀어내던 범위 그대로다.
                float hitRange = _attackRange + ChargeShoveExtraRange;
                Vector3 hitCenter = transform.position + Vector3.right * (hitRange * 0.5f);
                DealAreaDamage(0f, dmg, crit,
                    $"[{_name}] 돌진 {_chargeSkill.name} ×{_chargeSkill.coef:0.##}",
                    hitCenter, hitRange * 0.5f, bigHit: true,
                    knockback: MonsterUnit.ChargeKnockback, knockbackAll: true);
            }

            // 충돌 마무리 시간이 지나면 종료(이 동안 _charging이 다른 행동을 막으므로 별도 _busyTimer는 불필요).
            if (_chargeElapsed >= _chargeEndAt)
            {
                EndCharge();
            }
        }

        private void EndCharge()
        {
            _charging = false;
            SendMessage("StopChargeDash", SendMessageOptions.DontRequireReceiver);
            _ctrl.RequestFighting(); // 돌진 도달 → 파티 교전 진입
        }

        // ---- 공용 계산 ----

        /// <summary>
        /// 한 번의 타격 데미지를 계산하고 치명타 여부를 함께 돌려준다.
        /// 기획서(master-data-기획서 §7.4)의 공식 <c>공격력 × 스킬 계수</c>에 버프 배율을 곱한 값이며,
        /// <see cref="_critChance"/>로 굴린 판정이 성공하면 <see cref="_critDamage"/>를 추가로 곱한다
        /// (치명 판정은 <b>타격 단위</b> — 광역 스킬은 그 타격에 맞은 모든 적이 같은 판정 결과를 공유한다).
        /// <see cref="BattleDevController.DevDamageMultiplier"/>는 개발 하네스에서만 1이 아니다(실게임은 1).
        /// </summary>
        private long Damage(float coef, out bool crit)
        {
            crit = _critChance > 0f && UnityEngine.Random.value < _critChance;
            double dmg = _atk * _ctrl.DevDamageMultiplier * coef * _atkBuffMult;
            if (crit)
            {
                dmg *= _critDamage;
            }
            return System.Math.Max(1L, (long)dmg);
        }

        /// <summary>
        /// 내려찍기 착지 순간에 지면 이펙트를 띄우고 데미지를 적용한다(공중 체류 <paramref name="airTime"/> 뒤).
        /// 착지 후에 스폰하므로 이펙트가 도약 전 발밑이 아니라 실제로 내리찍은 지점에 생긴다.
        /// </summary>
        private System.Collections.IEnumerator SlamImpactAfter(float airTime, Skill sk, long dmg, bool crit, string label)
        {
            yield return new WaitForSeconds(Mathf.Max(0.05f, airTime));

            // 착지 임팩트음 — 시전음(도약 whoosh)과 나눠 울린다(사운드 정의서 §5.5·§9.3).
            SoundManager.Sfx(SoundId.SlayerGroundSlam);

            var fx = SpawnEffectAtSelf(sk.effect, sk.offset);
            if (fx != null && sk.scale > 0f && sk.scale != 1f) fx.transform.localScale *= sk.scale;
            // 내려찍은 자리에 지속 데칼(전투가 지나간 흔적) — 착지 시점이라 여기서 남긴다.
            if (sk.code == SlayerGroundSlamSkillCode)
            {
                _ctrl.SpawnGroundDecal(SelfEffectPos(sk.offset), DecalWidthSlam);
            }
            // 내리찍은 순간이 곧 타격. 광역 지정이면 먼지 이펙트 범위 안의 적 전부를 때린다.
            if (_aoeSkillCodes.Contains(sk.code))
            {
                DealAreaDamage(0f, dmg, crit, label,
                    EffectCenter(fx, SelfEffectPos(sk.offset)), EffectRadius(fx), bigHit: true,
                            knockback: MonsterUnit.SkillKnockback);
            }
            else
            {
                DealDamage(0f, dmg, crit, label, bigHit: true, knockback: MonsterUnit.SkillKnockback);
            }
        }

        /// <summary>
        /// 자기 위치 기준 스킬 이펙트 발생 좌표. 멤버 공통 보정(<see cref="_selfEffectXOffset"/> — 기사 강타 등)에
        /// 스킬별 보정(<paramref name="extra"/> — 지면에서 터지는 내려찍기 등)을 더한다.
        /// </summary>
        private Vector3 SelfEffectPos(Vector2 extra)
        {
            return transform.position
                + Vector3.up * (_ctrl.EffectYOffset + extra.y)
                + Vector3.right * (_selfEffectXOffset + extra.x);
        }

        /// <summary>스킬 이펙트를 자기 위치에서 발생시키고 생성된 인스턴스를 반환한다(없으면 null).</summary>
        private GameObject SpawnEffectAtSelf(GameObject effect, Vector2 extraOffset)
        {
            if (effect == null) return null;
            return Instantiate(effect, SelfEffectPos(extraOffset), Quaternion.identity);
        }

        /// <summary>광역 스킬 이펙트의 판정 반경(월드). **이펙트의 실제 렌더 크기에 맞춘다** — 이전에는
        /// 사거리(_attackRange)를 최소값으로 강제해 이펙트보다 넓은 범위의 적이 피격되던 문제가 있었다.
        /// 렌더러가 없어 크기를 구하지 못한 경우에만 사거리로 폴백한다.</summary>
        private float EffectRadius(GameObject fx)
        {
            float r = 0f;
            if (fx != null)
            {
                var rends = fx.GetComponentsInChildren<Renderer>();
                if (rends != null && rends.Length > 0)
                {
                    var b = rends[0].bounds;
                    for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                    r = Mathf.Max(b.extents.x, b.extents.y);
                }
            }
            // 이펙트 크기를 그대로 판정 반경으로 사용(과도한 광역 피격 방지). 크기 미상일 때만 사거리 폴백.
            return r > 0f ? r : _attackRange;
        }

        /// <summary>
        /// 광역 판정의 중심. 이펙트의 <b>렌더 바운즈 중심</b>을 쓴다 — 스프라이트 피벗이 중앙이 아닌 이펙트
        /// (강한일격 검기는 좌측 피벗)는 transform.position이 그림의 왼쪽 끝이라, 그대로 쓰면 판정이
        /// 검기가 뻗는 방향과 어긋난다. 렌더러가 없으면 <paramref name="fallback"/>을 쓴다.
        /// </summary>
        private static Vector3 EffectCenter(GameObject fx, Vector3 fallback)
        {
            if (fx != null)
            {
                var rends = fx.GetComponentsInChildren<Renderer>();
                if (rends != null && rends.Length > 0)
                {
                    var b = rends[0].bounds;
                    for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                    return b.center;
                }
            }
            return fallback;
        }

        /// <summary>
        /// 전(全)스킬 광역 캐스터(마법사)의 이펙트가 <b>대상까지 닿지 못할 때</b> 앞으로만(가로) 늘려 준다.
        /// <para>판정이 그려진 그림을 그대로 따르므로(<see cref="BattleDevController.DealSweepDamage"/>),
        /// 그림이 대상에 못 미치면 그만큼 맞지 않는다. 파티에 근접 직업이 없어 교전 거리가 멀어지면
        /// (교전 거리 = 파티 최소 사거리) 마법사가 대상보다 훨씬 뒤에 서게 되는데, 그때도 마법이 적에게
        /// 닿아 보이도록 부족한 만큼만 늘린다. 세로는 건드리지 않아 두께는 그대로다.</para>
        /// <para>근접 직업이 있는 보통 편성에서는 이펙트가 이미 대상을 넘으므로 아무 일도 하지 않는다.</para>
        /// </summary>
        private void StretchCasterEffectToTarget(GameObject fx, Vector3 origin)
        {
            if (fx == null)
            {
                return;
            }
            var target = _ctrl.MonsterTransform;
            if (target == null)
            {
                return;
            }

            float reach = EffectForwardReach(fx);
            float need = target.position.x - origin.x + CasterSweepOvershoot;
            if (reach <= 0.01f || need <= reach)
            {
                return;
            }

            float stretch = Mathf.Min(need / reach, CasterSweepMaxStretch);
            var s = fx.transform.localScale;
            s.x *= stretch;
            fx.transform.localScale = s;
        }

        /// <summary>
        /// 이펙트가 <b>앞(적 방향)으로 뻗는 최대 거리</b>(월드). 스프라이트 시퀀스는 프레임마다 크기가 다르므로
        /// 모든 프레임 중 가장 멀리 뻗는 값을 쓴다. 스프라이트 바운즈는 피벗 기준이라 왼쪽 피벗 이펙트(마법사)는
        /// 곧 그림의 길이가 된다. 프레임 정보를 못 구하면 0(늘리지 않음).
        /// </summary>
        private static float EffectForwardReach(GameObject fx)
        {
            var seq = fx.GetComponent<SpriteSequenceEffect>();
            if (seq == null || seq.frames == null || seq.frames.Length == 0)
            {
                return 0f;
            }
            float reach = 0f;
            foreach (var f in seq.frames)
            {
                if (f == null) continue;
                float r = f.bounds.max.x;
                if (r > reach) reach = r;
            }
            return reach * Mathf.Abs(fx.transform.localScale.x);
        }

        /// <summary>지정 위치에 스킬 이펙트를 무조건 발생시키고 인스턴스를 반환한다(몬스터 생존 여부와 무관 —
        /// 화살비·마법사 광역 등 대상 기준 연출 보장). scale로 이펙트 크기 배율을 적용한다.</summary>
        private GameObject SpawnEffectAt(GameObject effect, Vector3 pos, float scale = 1f)
        {
            if (effect == null) return null;
            var fx = Instantiate(effect, pos, Quaternion.identity);
            if (scale > 0f && scale != 1f) fx.transform.localScale *= scale;
            return fx;
        }

        /// <summary>화살비 낙하 위치. 최전방 몬스터가 있으면 그 위치, 없으면(이미 죽음) 레인저 앞쪽으로 폴백.</summary>
        private Vector3 ArrowRainTargetPos()
        {
            var mt = _ctrl.MonsterTransform;
            if (mt != null) return mt.position + Vector3.up * _ctrl.EffectYOffset;
            // 몬스터 부재 시: 사거리 지점(레인저는 오른쪽=적 방향)으로 폴백
            return transform.position + Vector3.right * Mathf.Max(1f, _attackRange * 0.5f) + Vector3.up * _ctrl.EffectYOffset;
        }

        private float EffectDuration(GameObject effect)
        {
            if (effect == null) return _ctrl.BasicHitDelay;
            var e = effect.GetComponent<SpriteSequenceEffect>();
            if (e == null || e.frames == null || e.frames.Length == 0 || e.fps <= 0f) return _ctrl.BasicHitDelay;
            return e.frames.Length / e.fps;
        }

        // ---- UI용 ----

        public int SkillCount => _skills.Count;
        public int SkillCodeAt(int i) => (i >= 0 && i < _skills.Count) ? _skills[i].code : 0;
        public Sprite SkillIconAt(int i) => (i >= 0 && i < _skills.Count) ? _skills[i].icon : null;

        public bool TryGetSkillCooldown(int skillCode, out float remaining, out float total)
        {
            foreach (var sk in _skills)
                if (sk.code == skillCode)
                {
                    total = sk.cooldown;
                    remaining = Mathf.Max(0f, sk.cooldown - sk.timer);
                    return true;
                }
            remaining = 0f; total = 0f;
            return false;
        }

        /// <summary>슬롯 hover 툴팁용: 스킬 코드의 유효(장착 레벨 반영) 정보를 돌려준다.</summary>
        public bool TryGetSkillInfo(int skillCode, out string name, out int coefType, out float coef, out float duration, out float cooldown)
        {
            foreach (var sk in _skills)
                if (sk.code == skillCode)
                {
                    name = sk.name;
                    coefType = sk.coefType;
                    coef = sk.coef;
                    duration = sk.duration;
                    cooldown = sk.cooldown;
                    return true;
                }
            name = null; coefType = 0; coef = 0f; duration = 0f; cooldown = 0f;
            return false;
        }
    }
}
