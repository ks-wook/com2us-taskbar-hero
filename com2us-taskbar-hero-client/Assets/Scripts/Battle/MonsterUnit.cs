using System;
using System.Collections;
using UnityEngine;
using TaskbarHero.Client.Managers;

namespace TaskbarHero.Client.Battle
{
    /// <summary>
    /// 몰려오는 적(몬스터) 1기. HP/공격력/이름을 가지며, 파티를 향해 왼쪽으로 전진하다
    /// 지정된 목표 x(<see cref="SetTargetX"/>)에서 멈춘다. 데미지를 받아 죽으면 사망 애니 후 스스로 소멸한다.
    /// 생성/추적/정리는 범용 <c>ObjectManager</c>가, 무엇을·언제 스폰할지와 처치/일시정지 판정은
    /// 전투 로직(개발용 <c>BattleDevController</c>)이 콜백으로 주입한다(특정 컨트롤러 타입에 직접 의존하지 않음).
    /// </summary>
    public class MonsterUnit : MonoBehaviour
    {
        private string _name = "Monster";
        private long _maxHp = 1;
        private long _hp = 1;
        private long _atk;
        private float _moveSpeed;
        private float _targetX;
        private bool _alive;
        private bool _moving;

        private Func<bool> _isPaused;          // 전투 일시정지 여부(주입)
        private Action<MonsterUnit> _onDeath;  // 사망 통지(주입) — 누적 킬/로그 등
        private Func<MonsterUnit, bool> _onAttack; // 아군 공격 시도(주입) — 대상이 있으면 데미지 적용 후 true
        private Action<MonsterUnit, int> _onBossPhase; // 보스 페이즈 진입 통지(주입) — 화면 연출용(1=70%, 2=30%)
        private float _attackInterval;         // 공격 주기(초, 0 이하면 공격 안 함)
        private float _attackTimer;
        private bool _isBoss;

        // ---- 보스 텔레그래프(공격 예고) ----
        // 예고 없이 때리면 일반 몹과 리듬이 같아 보스가 "센 몹"으로만 보인다. 예고 → 폭발이 관전 재미의 핵심이다.
        private const float TelegraphSeconds = 0.6f;      // 예고 길이(바닥 마커 + 뒤로 웅크리는 준비 동작)
        // 준비 동작(앤티시페이션): 예고 동안 뒤로 살짝 물러나며 커진다 → 예고가 끝나는 순간 스윙.
        // <b>애니메이터를 정지시키지 않는다</b> — SPUM 공격 클립을 중간 프레임에서 멈추면(과거 PlayCastHold 방식)
        // "때리려다 마는" 동작이 되고, 재개 시점과 타격 시점이 어긋난다(실측: nt 0.14에서 0.6초 정지).
        private const float TelegraphWindupBack = 0.16f;   // 뒤(+x)로 물러나는 거리(유닛)
        private const float TelegraphWindupScale = 1.05f;  // 예고 끝에서의 크기 배율
        private const float TelegraphMarkerWidth = 2.6f;  // 바닥 예고 마커의 월드 폭
        private const float TelegraphMarkerHeight = 0.8f;
        private static readonly Color TelegraphColor = new Color(1f, 0.15f, 0.1f, 0.55f);
        private const int TelegraphSortingOffset = -20;   // 몬스터 스프라이트 뒤(바닥)에 깔린다

        private float _telegraphTimer;      // >0이면 예고 진행 중(0이 되는 순간 타격)
        private SpriteRenderer _telegraph;  // 바닥 마커(보스마다 1회 생성해 켜고 끈다)
        private float _windupApplied;       // 위치에 반영돼 있는 준비 동작 오프셋
        private Vector3 _windupBaseScale;   // 준비 동작 전 크기(스폰 시 캡처)

        // ---- 보스 페이즈 전환 ----
        // 남은 체력 70%·30%를 지나는 순간 포효 + 화면 비네트 + 공격 주기 단축.
        private static readonly float[] BossPhaseThresholds = { 0.7f, 0.3f };
        private const float BossPhaseIntervalFactor = 0.78f; // 페이즈마다 공격 주기를 이 비율로 줄인다
        private int _bossPhase;                              // 진입한 페이즈 수(0=아직)

        // ---- 처치 연출(사망 시 날아가며 사라짐) ----
        private const float DeathLaunchX = 3.4f;      // 맞은 방향(뒤)으로 날아가는 초기 속도(유닛/초)
        private const float DeathLaunchY = 3.2f;      // 위로 솟는 초기 속도
        private const float DeathGravity = 9f;        // 낙하 가속
        private const float DeathSpin = 220f;         // 회전 속도(도/초)
        private const float DeathFadeDelay = 0.15f;   // 이 시간 뒤부터 서서히 투명해진다

        [Tooltip("사망 애니 후 오브젝트가 사라지기까지 지연(초)")]
        private const float DeathLinger = 0.8f;

        // 보스 연출 상수: 일반 몹 대비 3배 크기, 머리 위 왕관 아이콘의 월드 폭.
        private const float BossScale = 3f;
        private const float CrownWorldWidth = 1.4f;
        private const int CrownSortingOffset = 50;

        /// <summary>보스의 머리(스프라이트 상단)와 왕관 사이에 HP바가 들어갈 수 있도록,
        /// 왕관 하단을 머리에서 띄우는 여백(월드 단위). HP바 배치(<c>BattleDevController</c>)와 공유한다.</summary>
        public const float BossHpBarBand = 0.5f;

        // HP바를 붙일 '머리 위' 기준점(몬스터 transform 기준 월드 오프셋). 스폰 직후 1회만 측정한다.
        private Vector2 _headAnchorOffset;
        private bool _hasHeadAnchor;

        // ---- 피격 반응(연출) ----
        // 틴트 색은 '흰색'이 아니라 붉게 간다. SpriteRenderer.color는 텍스처에 <b>곱</b>해지므로 흰색을 넣으면
        // 아무 변화가 없고(밝게 만들 수 없다 — 정점 색이 Color32로 클램프되어 1을 넘길 수 없다),
        // 흰 실루엣을 만들려면 전용 셰이더 + Always Included 등록 + SPUM 파트마다 머티리얼 교체가 필요하다.
        // 붉은 틴트는 곱 연산만으로 즉시 "맞았다"로 읽히고 파트가 20개씩 있는 SPUM에서도 비용이 없다.
        private static readonly Color HitTint = new Color(1f, 0.42f, 0.38f);
        private const float HitTintSeconds = 0.08f;      // 약 2~3프레임 유지 후 원색 복귀
        // 빙결(프로스트 노바): 파랗게 물들이고 이동을 멈춘다. 곱 연산이라 채도를 낮추며 시원한 색으로 간다.
        private static readonly Color FreezeTint = new Color(0.55f, 0.76f, 1f);
        private float _freezeTimer;
        // 넉백은 몸 폭(약 0.9유닛) 만큼 뒤(+x = 파티 반대 방향)로 밀어 눈에 확실히 보이게 한다.
        // 순간이동이 아니라 KnockbackSpeed로 <b>미끄러지듯</b> 밀려나야 "맞아서 밀렸다"로 읽힌다.
        /// <summary>스킬 타격의 기본 넉백 거리(몸 폭 ≈ 0.9유닛). <b>기본공격은 넉백하지 않는다</b> —
        /// 초당 여러 번 들어오는 평타마다 밀리면 몬스터가 전선에 붙지 못하고 계속 떠 있는 것처럼 보인다.</summary>
        public const float SkillKnockback = 0.9f;
        /// <summary>밀치는 것이 곧 컨셉인 스킬(기사 방패 돌진)의 강한 넉백 거리 — 화면 폭(≈17.8)의 1/5쯤 날아간다.</summary>
        public const float ChargeKnockback = 3.2f;

        private const float BossKnockbackFactor = 0.35f; // 보스는 덜 밀린다(무게감)
        private const float KnockbackSpeed = 9f;          // 기본 밀림 속도(유닛/초) — 0.9를 0.1초에 이동
        private const float KnockbackMaxSeconds = 0.28f;  // 먼 거리는 속도를 올려 이 시간 안에 밀어낸다
        private const float KnockbackLimit = 3.5f;        // 목표 라인에서 이만큼 이상 밀려나지 않는다(기본)
        private const float AttackMotionSeconds = 0.5f;  // 공격 모션 추정 길이(무기 스윙 이펙트 노출 구간)

        // ---- HP바 juice(고스트 바·바 흔들림) ----
        // 상태를 <b>몬스터에</b> 두는 이유: HP바 오브젝트는 인덱스로 재사용되는 풀이라, 같은 몬스터가
        // 매 프레임 같은 바를 쓴다는 보장이 없다(죽거나 스폰하면 인덱스가 밀린다).
        private const float GhostHoldSeconds = 0.18f;   // 깎인 직후 고스트를 이만큼 그대로 둔다(방금 잃은 양 노출)
        private const float GhostCatchUpPerSec = 1.4f;  // 그 뒤 비율/초로 현재 체력까지 따라 내려온다
        private const float BarShakeSeconds = 0.16f;    // 피격 시 바가 흔들리는 시간
        // 사망 후에도 HP바를 잠깐 남겨 <b>마지막 한 방의 juice를 끝까지</b> 보여 준다 — 이게 없으면 바를 그리는
        // 쪽이 죽은 몬스터를 건너뛰어, <b>한 방에 죽는 몬스터는 바가 아예 안 보인다</b>(연출 없이 즉사).
        private const float HpBarDeathLinger = 0.55f;      // 유지 시간 상한(고스트가 다 빠지면 그 즉시 치운다)
        private const float GhostCatchUpDeadPerSec = 3f;   // 사망 후 고스트 하강 속도(0.18 유지 + 0.33 하강 = 0.51초)

        private float _ghostRatio = 1f;
        private float _ghostHold;
        private float _barShakeTimer;
        private float _hpBarLinger;
        private Vector3 _hpBarDeathPos;

        /// <summary>HP바 고스트(지연) 비율 0~1 — 현재 체력 비율보다 크면 그 차이가 "방금 깎인 양"이다.</summary>
        public float HpGhostRatio => _ghostRatio;
        /// <summary>피격 직후 HP바를 흔드는 강도 0~1(시간이 지나며 0으로 감쇠).</summary>
        public float HpBarShake01 => BarShakeSeconds > 0f ? Mathf.Clamp01(_barShakeTimer / BarShakeSeconds) : 0f;
        /// <summary>HP바를 그려야 하는지 — 살아 있거나, <b>사망 직후 마지막 juice가 남아 있는 동안</b> true.
        /// 한 방에 죽어도 바가 나타나 고스트가 끝까지 빠지는 것이 보이게 하는 조건이다.</summary>
        public bool ShowHpBar => _alive || _hpBarLinger > 0f;
        /// <summary>HP바가 따라갈 기준 위치. 사망 후에는 시신이 날아가므로(<c>DeathFlight</c>)
        /// <b>사망 지점에 고정</b>해 바가 시신과 함께 회전·비행하지 않게 한다.</summary>
        public Vector3 HpBarAnchorPos => _alive ? transform.position : _hpBarDeathPos;

        private SpriteRenderer[] _tintParts;   // 틴트 대상 SPUM 파트(스폰 후 1회 캐시)
        private Color[] _tintOriginals;        // 파트별 원래 색(SPUM은 파트마다 색이 다르다)
        private float _tintTimer;
        private float _crowdOffsetX;           // 전선에서 겹치지 않게 이 개체만 밀어 두는 x 오프셋
        // 무기를 실제로 휘두르는 구간(공격 모션 재생 중) — 이 사이에만 무기 궤적·불티가 나온다.
        // 보스의 예고(웅크리는 준비 동작) 구간은 포함하지 않는다 — 예고 중에는 무기가 움직이지 않는다.
        private float _swingTimer;
        private MonsterWeaponSwingFx _swingFx;   // 무기 끝 궤적·불티(무기가 없는 몬스터는 null)
        private float _knockbackRemaining;     // 아직 밀려나야 하는 거리(유닛)
        private float _knockbackSpeed;         // 이번 밀림의 속도(거리가 멀면 더 빠르게)
        private bool _engaged;                 // 전선(목표 x)에 한 번이라도 닿았는지 — 공격 주기 누적 시작 조건
        private int _lastReactionFrame = -1;   // 프레임당 1회 제한(상주 소형 창이므로 과한 점멸 금지)

        public bool Alive => _alive;
        public bool IsBoss => _isBoss;
        /// <summary>현재 전진(이동) 애니메이션이 재생 중인지. 걷기 먼지 이펙트 노출 판정에 사용.</summary>
        public bool IsMoving => _moving && _alive;
        /// <summary>지금 무기를 휘두르는 중인지(공격 모션 재생 구간). 무기 스윙 이펙트의 방출 조건이다.
        /// 보스 예고 구간은 포함하지 않는다 — 그때는 무기가 움직이지 않는다.</summary>
        public bool IsSwinging => _swingTimer > 0f && _alive;
        public long Hp => _hp;
        public long MaxHp => _maxHp;
        public long Atk => _atk;
        public string MonsterName => _name;

        /// <summary>스폰 직후 호출해 스탯/이동속도/콜백을 주입하고 idle로 초기화한다.
        /// isBoss=true면 3배 크기로 키우고(이동 방향 부호 유지) 머리 위에 왕관 아이콘을 붙인다.
        /// onAttack/attackInterval을 주면 목표 지점에 멈춘 뒤 주기적으로 아군을 공격한다.
        /// onBossPhase를 주면 보스가 체력 70%·30%를 지날 때 화면 연출용으로 통지한다.</summary>
        public void Init(string monsterName, long hp, long atk, float moveSpeed,
                         Func<bool> isPaused, Action<MonsterUnit> onDeath,
                         bool isBoss = false, Sprite bossIcon = null,
                         Func<MonsterUnit, bool> onAttack = null, float attackInterval = 0f,
                         Action<MonsterUnit, int> onBossPhase = null)
        {
            _name = monsterName;
            _maxHp = Math.Max(1L, hp);
            _hp = _maxHp;
            _atk = atk;
            _moveSpeed = Mathf.Max(0f, moveSpeed);
            _isPaused = isPaused;
            _onDeath = onDeath;
            _onAttack = onAttack;
            _onBossPhase = onBossPhase;
            _attackInterval = attackInterval;
            _attackTimer = 0f;
            _telegraphTimer = 0f;
            _bossPhase = 0;
            _freezeTimer = 0f;
            _windupApplied = 0f;
            _windupBaseScale = transform.localScale; // 보스면 아래 확대 후 다시 잡는다
            if (_telegraph != null) _telegraph.enabled = false;
            _alive = true;
            _isBoss = isBoss;
            _targetX = transform.position.x;
            _hasHeadAnchor = false; // 풀에서 재사용될 수 있으므로 머리 기준점을 다시 측정한다
            _tintParts = null;      // 같은 이유로 틴트 대상 파트도 다시 캐시한다
            _tintTimer = 0f;
            _crowdOffsetX = 0f;   // 풀에서 재사용될 수 있으므로 초기화(스폰 직후 컨트롤러가 다시 지정한다)
            _swingTimer = 0f;
            _knockbackRemaining = 0f;
            _knockbackSpeed = KnockbackSpeed;
            _engaged = false;
            _ghostRatio = 1f;
            _ghostHold = 0f;
            _barShakeTimer = 0f;
            _hpBarLinger = 0f;
            gameObject.name = (isBoss ? "Boss_" : "Monster_") + _name;

            if (isBoss)
            {
                transform.localScale *= BossScale; // 부호(좌우 방향) 유지한 채 3배 확대
                _windupBaseScale = transform.localScale; // 준비 동작의 기준 크기(확대 반영 후)
                // 보스 등장 포효 — Warning!! 배너·경보음과 같은 시점이다(사운드 정의서 §5.6).
                SoundManager.Sfx(SoundId.BossRoar);
                if (bossIcon != null)
                {
                    StartCoroutine(AttachCrown(bossIcon));
                }
            }

            // 무기를 휘두를 때만 보이는 궤적·불티. 몬스터 자식으로 한 번만 붙이고(풀에서 재사용되면
            // 그대로 다시 쓴다), 무기 렌더러가 없는 몬스터에는 붙지 않는다(Create가 null).
            if (_swingFx == null)
            {
                // 색은 프리팹에 담긴 팔레트가 정본이다(보스는 지역별 색). 없으면 기본 불티색.
                var palette = GetComponent<MonsterSwingFxPalette>();
                _swingFx = MonsterWeaponSwingFx.Create(
                    transform, () => IsSwinging,
                    palette != null ? palette.BaseColor : (Color?)null);
            }
            else
            {
                _swingFx.ResetEmission(); // 이전 등장에서 남은 궤적이 새 스폰 지점까지 이어지지 않게
            }

            SendMessage("PlayIdle", SendMessageOptions.DontRequireReceiver);
            StartCoroutine(MeasureHeadAnchor());
        }

        /// <summary>
        /// HP바를 붙일 '머리 위' 기준점을 돌려준다(몬스터 <c>transform</c> 기준 월드 오프셋 — x는 몸통 중앙,
        /// y는 몸통 상단). 아직 측정되지 않았으면 false.
        /// </summary>
        public bool TryGetHeadAnchor(out Vector2 offset)
        {
            offset = _headAnchorOffset;
            return _hasHeadAnchor;
        }

        /// <summary>
        /// 머리 위 기준점을 <b>스폰 직후 idle 자세에서 한 번만</b> 측정해 캐시한다.
        /// <para>매 프레임 스프라이트 경계를 재면 걷기·공격·피격 애니메이션이 파트를 움직일 때마다 상단·중앙이
        /// 요동쳐 HP바가 심하게 떨린다. 반면 몬스터의 <c>transform</c>은 이동(x)만 갱신되므로,
        /// 여기서 잰 오프셋을 그 위치에 더하면 <b>흔들림 없이 따라다니기만</b> 한다.</para>
        /// <para>왕관(<c>BossCrown</c>)은 머리 위에 따로 띄우는 장식이라 몸통 경계에서 제외한다.
        /// 같은 프레임에 붙을 수 있어(AttachCrown도 2프레임 대기) 이름으로 걸러 낸다.</para>
        /// </summary>
        private IEnumerator MeasureHeadAnchor()
        {
            // SPUM이 스프라이트 파트를 붙이고 idle 첫 프레임이 적용될 시간을 준다(1~2프레임).
            yield return null;
            yield return null;
            if (this == null || !_alive)
            {
                yield break;
            }

            var rends = GetComponentsInChildren<SpriteRenderer>(true);
            Bounds body = default;
            bool has = false;
            foreach (var r in rends)
            {
                if (r == null || r.gameObject.name == "BossCrown")
                {
                    continue;
                }
                if (!has) { body = r.bounds; has = true; } else { body.Encapsulate(r.bounds); }
            }
            if (!has)
            {
                yield break; // 렌더러가 아직 없으면 다음 스폰에서 다시 측정된다(그때까지 폴백 사용)
            }

            Vector3 pos = transform.position;
            _headAnchorOffset = new Vector2(body.center.x - pos.x, body.max.y - pos.y);
            _hasHeadAnchor = true;
        }

        /// <summary>보스 머리 위에 왕관 아이콘을 붙인다. SPUM 파트가 구성될 때까지 잠깐 기다린 뒤
        /// 몬스터 렌더러 경계 상단에 배치하고, 확대된 부모 스케일을 보정해 일정한 월드 크기로 표시한다.</summary>
        private IEnumerator AttachCrown(Sprite icon)
        {
            // SPUM이 스프라이트 파트를 붙일 시간을 준다(1~2프레임).
            yield return null;
            yield return null;
            if (this == null || !_alive)
            {
                yield break;
            }

            var rends = GetComponentsInChildren<SpriteRenderer>(true);
            var bounds = new Bounds(transform.position, Vector3.zero);
            bool hasBounds = false;
            int maxOrder = int.MinValue;
            int sortingLayerId = 0;
            foreach (var r in rends)
            {
                if (r == null)
                {
                    continue;
                }
                if (!hasBounds) { bounds = r.bounds; hasBounds = true; }
                else { bounds.Encapsulate(r.bounds); }
                if (r.sortingOrder > maxOrder) { maxOrder = r.sortingOrder; sortingLayerId = r.sortingLayerID; }
            }

            var crownGo = new GameObject("BossCrown");
            var sr = crownGo.AddComponent<SpriteRenderer>();
            sr.sprite = icon;
            if (maxOrder != int.MinValue)
            {
                sr.sortingLayerID = sortingLayerId;
                sr.sortingOrder = maxOrder + CrownSortingOffset; // 항상 몬스터 스프라이트 앞
            }

            crownGo.transform.SetParent(transform, true); // 부모(보스)를 따라 이동하도록 자식으로

            // 확대된 부모 lossyScale을 보정해 왕관을 일정한 월드 폭으로 맞춘다.
            float spriteW = icon.bounds.size.x;
            float worldScale = spriteW > 0.001f ? CrownWorldWidth / spriteW : 1f;
            Vector3 lossy = transform.lossyScale;
            crownGo.transform.localScale = new Vector3(
                worldScale / Mathf.Max(0.0001f, Mathf.Abs(lossy.x)),
                worldScale / Mathf.Max(0.0001f, Mathf.Abs(lossy.y)),
                1f);

            // 왕관을 머리 위로 띄워 그 아래(머리와 왕관 사이)에 HP바 공간을 확보한다.
            // 왕관 하단이 머리 상단보다 BossHpBarBand만큼 위에 오도록 중심 Y를 계산한다.
            float topY = hasBounds ? bounds.max.y : transform.position.y + 2f;
            float centerX = hasBounds ? bounds.center.x : transform.position.x;
            float crownWorldHeight = CrownWorldWidth * (icon.bounds.size.y / Mathf.Max(0.0001f, icon.bounds.size.x));
            crownGo.transform.position = new Vector3(centerX, topY + BossHpBarBand + crownWorldHeight * 0.5f, 0f);
        }

        /// <summary>이 몬스터가 멈출 목표 x(파티 앞 라인). 왼쪽으로만 이동하며 이 지점에서 정지한다.
        /// <para>실제 정지 지점은 여기에 <see cref="SetCrowdOffsetX"/>의 개체별 오프셋을 더한 값이다 —
        /// 전선에 몰린 몬스터들이 한 점에 완전히 겹쳐 <b>한 마리처럼 보이는</b> 것을 막는다.</para></summary>
        public void SetTargetX(float x) { _targetX = x + _crowdOffsetX; }

        /// <summary>
        /// 전선에서 다른 몬스터와 겹치지 않도록 이 개체만 앞뒤로 밀어 두는 오프셋(유닛).
        /// 스폰 직후 컨트롤러가 아주 작은 무작위 값으로 1회 지정한다(세로 흩뜨림은 스폰 y가 담당).
        /// </summary>
        public void SetCrowdOffsetX(float offset) { _crowdOffsetX = offset; }

        /// <summary>데미지를 적용하고, 이번 타격으로 죽었으면 true를 1회 반환한다.
        /// HP바 연출(고스트 바 유지·바 흔들림)도 여기서 시작한다 — <b>모든</b> 피해가 이 창구를 지난다.</summary>
        public bool TakeDamage(long dmg)
        {
            if (!_alive) return false;
            if (dmg > 0)
            {
                // 고스트는 현 위치에 잠시 멈춰 방금 깎인 구간을 흰색으로 남긴다(값은 내리지 않는다).
                _ghostHold = GhostHoldSeconds;
                _barShakeTimer = BarShakeSeconds;
            }
            _hp -= dmg;
            if (_hp <= 0)
            {
                _hp = 0;
                Die();
                return true;
            }
            TickBossPhase();
            return false;
        }

        /// <summary>
        /// 보스가 체력 70%·30% 선을 지나는 순간 <b>페이즈 전환</b>을 한 번씩 발동한다 —
        /// 포효(등장과 같은 공용음) + 공격 주기 단축 + 화면 연출 통지(<see cref="_onBossPhase"/>).
        /// 보스가 아니거나 이미 그 페이즈를 지났으면 아무것도 하지 않는다.
        /// </summary>
        private void TickBossPhase()
        {
            if (!_isBoss || _maxHp <= 0 || _bossPhase >= BossPhaseThresholds.Length) return;
            float ratio = (float)_hp / _maxHp;
            if (ratio > BossPhaseThresholds[_bossPhase]) return;

            _bossPhase++;
            SoundManager.Sfx(SoundId.BossRoar);              // 페이즈 진입 포효(§5.6 공용음)
            if (_attackInterval > 0f)
            {
                _attackInterval *= BossPhaseIntervalFactor;  // 더 자주 때린다(체감 난이도 상승)
            }
            _onBossPhase?.Invoke(this, _bossPhase);          // 붉은 비네트 플래시·셰이크는 컨트롤러가 그린다
        }

        /// <summary>
        /// 피격 반응 연출을 재생한다 — ① 붉은 틴트 ② 넉백(요청한 거리만큼).
        /// 데미지 단일 관문(<c>BattleDevController.DoDamageAfter</c>·<c>DoAreaDamageAfter</c>)에서 호출하므로
        /// 기본공격·스킬·투사체·돌진 전 경로에 같은 반응이 걸린다.
        /// <para><b>SPUM 피격(DAMAGED) 모션은 재생하지 않는다</b> — 초당 여러 번 들어오는 타격마다 움찔거려
        /// 몬스터가 전진·공격 자세를 유지하지 못했다. 맞았다는 신호는 틴트·넉백·데미지 숫자·HP바가 맡는다.</para>
        /// <para><paramref name="heavy"/>가 false면 <b>틴트만</b> 준다 — 광역기는 대상 수만큼 루프를 돌기 때문에
        /// 전원을 밀어내면 화면이 찢어진다(피격음이 이미 첫 대상만 울리는 것과 같은 이유).</para>
        /// <para><paramref name="knockback"/>은 밀려날 거리이며 <b>0이면 밀리지 않는다</b> — 기본공격이 이 경우다
        /// (<see cref="SkillKnockback"/>·<see cref="ChargeKnockback"/> 참고).</para>
        /// <para>죽은 대상에는 아무것도 하지 않는다 — 마지막 타격의 피드백은 사망 모션·사망음이 맡는다.</para>
        /// </summary>
        public void PlayHitReaction(bool heavy, float knockback = 0f)
        {
            if (!_alive) return;
            // 같은 프레임에 여러 타격이 들어와도 연출은 1회만(상주 소형 창 — 과한 점멸 금지).
            if (_lastReactionFrame == Time.frameCount) return;
            _lastReactionFrame = Time.frameCount;

            ApplyHitTint();
            if (!heavy) return;

            ApplyKnockback(knockback);
        }

        /// <summary>
        /// 데미지 없이 밀어내기만 한다(기사 방패 돌진처럼 <b>밀치는 것 자체가 효과</b>인 스킬의 부수 대상용).
        /// 틴트·데미지 숫자는 붙이지 않는다 — 맞은 것이 아니라 부딪혀 밀린 것이다.
        /// </summary>
        public void ApplyPush(float distance)
        {
            if (!_alive) return;
            ApplyKnockback(distance);
        }

        /// <summary>SPUM 파트 전체를 붉게 물들이고 복귀 타이머를 건다(파트별 원래 색은 1회 캐시).</summary>
        private void ApplyHitTint()
        {
            ApplyTint(HitTint);
            _tintTimer = HitTintSeconds;
        }

        /// <summary>SPUM 파트 전체를 지정 색으로 물들인다(피격 붉은 틴트·빙결 파란 틴트 공용).</summary>
        private void ApplyTint(Color tint)
        {
            if (_tintParts == null)
            {
                CacheTintParts();
            }
            for (int i = 0; i < _tintParts.Length; i++)
            {
                var r = _tintParts[i];
                if (r == null) continue;
                // 원색의 알파는 유지한다(반투명 파트가 피격 순간 불투명해지지 않도록).
                var c = tint;
                c.a = _tintOriginals[i].a;
                r.color = c;
            }
        }

        /// <summary>
        /// <b>빙결</b>(프로스트 노바) — 지정 시간 동안 파랗게 물들이고 <b>전진을 멈춘다</b>.
        /// 이미 빙결 중이면 남은 시간을 더 긴 쪽으로 갱신한다.
        /// <para>공격은 막지 않는다 — 빙결 대상은 대개 전선에 멈춰 있어 이동만 막아도 시각적으로 충분하고,
        /// 공격까지 막으면 <b>연출이 전투 난이도를 바꾼다</b>(넉백에서와 같은 기준).</para>
        /// </summary>
        public void ApplyFreeze(float seconds)
        {
            if (!_alive || seconds <= 0f) return;
            _freezeTimer = Mathf.Max(_freezeTimer, seconds);
            ApplyTint(FreezeTint);
        }

        /// <summary>빙결 중인지(이동 정지 판정·디버그용).</summary>
        public bool IsFrozen => _freezeTimer > 0f;

        /// <summary>틴트 대상 SPUM 파트와 그 원래 색을 캐시한다(왕관은 몬스터 몸이 아니라 장식이므로 제외).</summary>
        private void CacheTintParts()
        {
            var rends = GetComponentsInChildren<SpriteRenderer>(true);
            int n = 0;
            for (int i = 0; i < rends.Length; i++)
            {
                if (rends[i] != null && rends[i].gameObject.name != "BossCrown") n++;
            }
            _tintParts = new SpriteRenderer[n];
            _tintOriginals = new Color[n];
            int k = 0;
            for (int i = 0; i < rends.Length; i++)
            {
                var r = rends[i];
                if (r == null || r.gameObject.name == "BossCrown") continue;
                _tintParts[k] = r;
                _tintOriginals[k] = r.color;
                k++;
            }
        }

        /// <summary>
        /// 캐시된 원래 색으로 되돌린다(틴트 종료·사망 시).
        /// <b>빙결이 남아 있으면 원색이 아니라 파란 틴트로 복귀</b>한다 — 빙결 중에 한 대 맞으면 붉은 틴트가
        /// 잠깐 덮는데, 그때 원색으로 풀어 버리면 아직 얼어 있는 몬스터가 멀쩡한 색으로 돌아간다.
        /// </summary>
        private void RestoreTint()
        {
            if (_tintParts == null) return;
            if (_freezeTimer > 0f && _alive)
            {
                ApplyTint(FreezeTint);
                return;
            }
            for (int i = 0; i < _tintParts.Length; i++)
            {
                if (_tintParts[i] != null) _tintParts[i].color = _tintOriginals[i];
            }
        }

        /// <summary>
        /// 맞은 방향(파티 반대쪽 = +x)으로 <paramref name="distance"/>만큼 밀려날 거리를 예약한다.
        /// 실제 이동은 <see cref="TickKnockback"/>이 처리하므로 <b>미끄러지듯</b> 밀려난다(순간이동은 눈에 안 보인다).
        /// <para>먼 거리는 속도를 올려 <see cref="KnockbackMaxSeconds"/> 안에 밀어낸다 — 기본 속도로 3유닛을
        /// 밀면 0.36초나 걸려 "날아갔다"가 아니라 "끌려갔다"로 보인다.</para>
        /// <para>연타로 무한히 밀려나지 않도록 목표 라인에서 일정 거리까지만 밀린다(기본
        /// <see cref="KnockbackLimit"/>, 그보다 큰 밀치기를 요청하면 그 거리까지 허용). 이미 한계 밖에 있으면
        /// 위치를 건드리지 않는다. 되돌아오는 이동은 <see cref="Update"/>가 <b>평소 걷는 속도</b>로 처리한다
        /// — 밀려난 뒤 순간이동처럼 따라붙으면 넉백이 맞은 것으로 읽히지 않는다.</para>
        /// </summary>
        private void ApplyKnockback(float distance)
        {
            if (distance <= 0f) return; // 기본공격 등 — 밀지 않는다
            float dist = distance * (_isBoss ? BossKnockbackFactor : 1f);
            float limit = Mathf.Max(KnockbackLimit, dist + 0.6f); // 강한 밀치기는 그만큼 뒤까지 허용
            float offset = transform.position.x - _targetX + _knockbackRemaining; // 예약분까지 포함한 밀림
            float room = limit - offset;
            if (room <= 0f) return;
            _knockbackRemaining += Mathf.Min(dist, room);
            _knockbackSpeed = Mathf.Max(KnockbackSpeed, _knockbackRemaining / KnockbackMaxSeconds);
        }

        /// <summary>
        /// 예약된 넉백 거리를 이번 프레임 몫만큼 밀어낸다. 밀리는 중이면 true를 반환해
        /// <see cref="Update"/>의 전진을 막는다(같은 프레임에 밀기와 걷기가 맞서면 제자리에서 떠는 것처럼 보인다).
        /// </summary>
        private bool TickKnockback()
        {
            if (_knockbackRemaining <= 0f) return false;
            float speed = _knockbackSpeed > 0f ? _knockbackSpeed : KnockbackSpeed;
            float step = Mathf.Min(_knockbackRemaining, speed * Time.deltaTime);
            _knockbackRemaining -= step;
            var p = transform.position;
            p.x += step;
            transform.position = p;
            return true;
        }

        /// <summary>
        /// 피격 연출 타이머를 진행한다 — 붉은 틴트 복귀, 무기 스윙 구간·빙결 지속 시간 감소.
        /// <para>일시정지·사망과 무관하게 돌고 <b>unscaled 시간</b>을 쓴다 — 정지 중에 붉은 틴트가 굳거나,
        /// 나중에 히트스톱(<c>timeScale</c> 감속)을 넣었을 때 2~3프레임 점멸이 20배로 늘어지면 안 된다.</para>
        /// </summary>
        private void TickHitReaction()
        {
            float dt = Time.unscaledDeltaTime;

            if (_tintTimer > 0f)
            {
                _tintTimer -= dt;
                if (_tintTimer <= 0f) RestoreTint();
            }

            if (_swingTimer > 0f)
            {
                _swingTimer -= dt;
            }

            if (_freezeTimer > 0f)
            {
                _freezeTimer -= dt;
                if (_freezeTimer <= 0f)
                {
                    RestoreTint(); // 빙결 해제 → 원색 복귀
                }
            }

            TickHpBarJuice(dt);
        }

        /// <summary>
        /// HP바 연출 상태를 진행한다 — 고스트 바가 <see cref="GhostHoldSeconds"/> 동안 멈춰 방금 깎인 양을
        /// 보여준 뒤 현재 체력까지 내려오고, 피격 흔들림 강도는 시간에 따라 0으로 감쇠한다.
        /// <para><b>사망 후에도 계속 돈다</b>(<see cref="TickHitReaction"/>에서 호출되므로 사망·일시정지와 무관).
        /// 죽은 뒤에는 고스트를 <see cref="GhostCatchUpDeadPerSec"/>로 더 빠르게 0까지 내리고, 다 빠지면
        /// 유지 시간을 끝내 바를 치운다 — 한 방에 죽어도 "바가 쭉 빠지는" 연출이 보이게 하는 부분이다.</para>
        /// </summary>
        private void TickHpBarJuice(float dt)
        {
            if (_barShakeTimer > 0f)
            {
                _barShakeTimer -= dt;
            }

            float current = _maxHp > 0 ? Mathf.Clamp01((float)_hp / _maxHp) : 0f;
            if (_ghostHold > 0f)
            {
                _ghostHold -= dt;
            }
            else if (_ghostRatio > current)
            {
                _ghostRatio = Mathf.MoveTowards(_ghostRatio, current,
                    (_alive ? GhostCatchUpPerSec : GhostCatchUpDeadPerSec) * dt);
            }
            if (_ghostRatio < current)
            {
                _ghostRatio = current; // 회복(디버그 등)으로 현재가 더 높아지면 즉시 맞춘다
            }

            if (!_alive && _hpBarLinger > 0f)
            {
                _hpBarLinger -= dt;
                if (_ghostHold <= 0f && _ghostRatio <= 0.001f)
                {
                    _hpBarLinger = 0f; // 다 빠졌으면 빈 바를 남기지 않고 즉시 숨긴다
                }
            }
        }

        /// <summary>
        /// 매 프레임 파티를 향해 왼쪽으로만 전진하고, 목표 x에 닿으면 멈춘다(전진/정지 애니 전환).
        /// 넉백으로 밀려나는 동안에는 전진하지 않고, 밀림이 끝나면 <b>평소 걷는 속도</b>로 전선까지 걸어서
        /// 되돌아온다(복귀 가속 없음 — 밀려난 것이 눈에 보이게). <b>넉백이 전투 효율을 바꾸지 않는</b> 것은
        /// 위치와 무관하게 차는 공격 주기가 담당한다(<see cref="TickAttack"/> 참고).
        /// </summary>
        private void Update()
        {
            TickHitReaction(); // 연출 복귀는 사망·일시정지와 무관하게 진행한다(붉은 틴트가 굳지 않도록)
            if (!_alive || (_isPaused != null && _isPaused())) return;

            if (_engaged)
            {
                // 전선에 한 번 닿은 뒤로는 밀려나 있어도 공격 주기가 계속 찬다(TickAttack 참고).
                // 접근 중(_engaged=false)에는 누적하지 않는다 — 그러면 도착하는 순간 바로 때린다.
                _attackTimer += Time.deltaTime;
            }
            if (TickKnockback())
            {
                return; // 밀려나는 중 — 이번 프레임에는 전진하지 않는다
            }

            float x = transform.position.x;
            if (_freezeTimer > 0f)
            {
                // 빙결: 전진만 멈춘다(공격 주기는 계속 돈다 — 연출이 난이도를 바꾸지 않게).
                SetMoving(false);
                TickAttack();
                return;
            }
            if (_telegraphTimer > 0f)
            {
                // 예고(준비 동작) 중에는 <b>이동 판정을 하지 않는다</b>. 준비 동작이 뒤로 밀어내는 것과
                // "목표 라인으로 되돌아가기"가 매 프레임 맞서면 걷기/대기 애니가 프레임마다 토글돼
                // 캐릭터가 떠는 것처럼 보인다(실측: 0_move ↔ 0_idle 반복).
                SetMoving(false);
                TickAttack();
                return;
            }
            if (x - _targetX > 0.02f)
            {
                // 밀려난 뒤 복귀도 <b>평소 걷는 속도</b>로만 한다 — 넉백 직후 빠르게 따라붙으면
                // 밀린 것이 눈에 남지 않는다(공격 주기는 위치와 무관하게 차므로 난이도는 그대로다).
                float nx = Mathf.MoveTowards(x, _targetX, _moveSpeed * Time.deltaTime);
                var p = transform.position; p.x = nx; transform.position = p;
                SetMoving(true);
            }
            else
            {
                _engaged = true; // 이 시점부터 공격 주기를 누적한다
                SetMoving(false);
                TickAttack(); // 목표 지점(파티 앞)에 멈춘 동안 주기적으로 아군 공격
            }
        }

        /// <summary>
        /// 목표 지점에 멈춰 있는 동안 공격 주기마다 아군 공격을 시도하고, 대상이 있으면 공격 애니를 재생한다.
        /// <para>주기 누적(<c>_attackTimer</c>)은 <see cref="Update"/>가 <b>위치와 무관하게</b> 진행시킨다 —
        /// 넉백으로 밀려나 있는 동안 주기가 멈추면 맞을수록 몬스터가 덜 때리게 되어 넉백이 전투 효율을
        /// 바꿔 버린다. 밀려나 있는 사이에도 주기가 차므로 복귀 직후 바로 때린다(때리는 위치는 그대로 전선).</para>
        /// </summary>
        private void TickAttack()
        {
            if (_onAttack == null || _attackInterval <= 0f) return;

            // 보스: 예고(텔레그래프) 진행 중이면 끝나는 순간 타격한다.
            if (_telegraphTimer > 0f)
            {
                _telegraphTimer -= Time.deltaTime;
                PulseTelegraph();
                // 준비 동작: 예고가 진행될수록 뒤로 물러나며 커진다(예고 끝에서 최대).
                float progress = 1f - Mathf.Clamp01(_telegraphTimer / TelegraphSeconds);
                ApplyWindup(progress);
                if (_telegraphTimer <= 0f)
                {
                    ShowTelegraph(false);
                    ApplyWindup(0f);  // 원래 자리·크기로 돌아오며
                    StrikeNow();      // 그 순간 스윙 + 타격(동작과 타격이 일치한다)
                }
                return;
            }

            if (_attackTimer >= _attackInterval)
            {
                _attackTimer = 0f;
                if (_isBoss)
                {
                    BeginTelegraph(); // 보스는 0.6초 예고 후에 때린다
                }
                else
                {
                    StrikeNow();
                }
            }
        }

        /// <summary>
        /// 보스 공격 예고를 시작한다 — 바닥에 붉은 마커를 켜고, 뒤로 웅크리는 준비 동작에 들어간다.
        /// <b>공격 애니메이션은 예고가 끝나는 순간에 재생</b>하므로(<see cref="StrikeNow"/>) 스윙과 타격이 일치한다.
        /// <para>과거에는 <c>PlayCastHold</c>로 공격 애니를 중간 프레임에서 멈춰 "치켜든 자세"를 만들었는데,
        /// 그 방식은 ① 클립마다 그 프레임이 다르고(몬스터는 nt 0.14가 거의 시작점이라 <b>때리려다 마는</b> 동작이
        /// 된다) ② <c>Animator.speed = 0</c>이 애니메이터 전체를 멈춰 피격·이동 모션까지 굳고 ③ 재개 시점이
        /// 타격 시점보다 늦어 동작과 타격이 어긋났다. 그래서 애니메이터를 건드리지 않는 방식으로 바꿨다.</para>
        /// </summary>
        private void BeginTelegraph()
        {
            _telegraphTimer = TelegraphSeconds;
            ShowTelegraph(true);
        }

        /// <summary>실제 타격 — 아군에게 피해를 넘기고 공격음·공격 모션을 재생한다.</summary>
        private void StrikeNow()
        {
            if (_onAttack == null || !_onAttack(this))
            {
                return;
            }
            // 몬스터·보스 공격은 전 계열 공용음 하나를 쓴다(사운드 정의서 §5.6·§8).
            SoundManager.Sfx(SoundId.MonAttack);
            SendMessage("PlayAttackOnce", SendMessageOptions.DontRequireReceiver);
            _swingTimer = AttackMotionSeconds; // 이 사이에만 무기 궤적·불티가 나온다
        }

        /// <summary>
        /// 준비 동작을 진행도(0~1)에 맞춰 반영한다 — 뒤(+x)로 물러나며 살짝 커진다.
        /// <para>위치는 <b>절대 좌표가 아니라 차이만</b> 더한다(<see cref="_windupApplied"/>) — 예고 중에도
        /// 넉백·전진이 같은 transform을 움직이므로 절대 좌표로 되돌리면 그 이동을 취소해 버린다.</para>
        /// </summary>
        private void ApplyWindup(float progress01)
        {
            float k = Mathf.Sin(Mathf.Clamp01(progress01) * Mathf.PI * 0.5f); // 초반에 빠르게 물러난다
            float offset = TelegraphWindupBack * k;
            float delta = offset - _windupApplied;
            if (!Mathf.Approximately(delta, 0f))
            {
                var p = transform.position;
                p.x += delta;
                transform.position = p;
                _windupApplied = offset;
            }
            if (_windupBaseScale != Vector3.zero)
            {
                transform.localScale = _windupBaseScale * Mathf.Lerp(1f, TelegraphWindupScale, k);
            }
        }

        /// <summary>
        /// 바닥 예고 마커를 켜고 끈다. 마커는 보스마다 1회 만들어 재사용하며(매 공격 생성 금지),
        /// 절차적 스프라이트(<see cref="BattleFxTextures.SoftEllipse"/>)를 납작하게 눌러 그린다.
        /// <para>보스는 3배로 확대돼 있어 자식으로 붙이면 마커도 3배가 되므로, 왕관과 같은 방식으로
        /// 부모 스케일을 나눠 보정한다.</para>
        /// </summary>
        private void ShowTelegraph(bool on)
        {
            if (!on)
            {
                if (_telegraph != null) _telegraph.enabled = false;
                return;
            }
            if (_telegraph == null)
            {
                var go = new GameObject("BossTelegraph");
                go.transform.SetParent(transform, false);
                _telegraph = go.AddComponent<SpriteRenderer>();
                _telegraph.sprite = BattleFxTextures.SoftEllipse();
                _telegraph.color = TelegraphColor;

                int order = 0;
                foreach (var r in GetComponentsInChildren<SpriteRenderer>(true))
                {
                    if (r != null && r != _telegraph && r.sortingOrder > order) order = r.sortingOrder;
                }
                _telegraph.sortingOrder = order + TelegraphSortingOffset;

                Vector3 lossy = transform.lossyScale;
                float sx = TelegraphMarkerWidth / Mathf.Max(0.0001f, Mathf.Abs(lossy.x));
                float sy = TelegraphMarkerHeight / Mathf.Max(0.0001f, Mathf.Abs(lossy.y));
                go.transform.localScale = new Vector3(sx, sy, 1f);
                go.transform.localPosition = Vector3.zero; // 발밑(몬스터 기준점 = 지면)
            }
            _telegraph.enabled = true;
        }

        /// <summary>예고 마커를 깜빡이며 <b>점점 진해지게</b> 한다 — 남은 시간이 줄수록 밝아져 임팩트 시점을 읽게 한다.</summary>
        private void PulseTelegraph()
        {
            if (_telegraph == null) return;
            float progress = 1f - Mathf.Clamp01(_telegraphTimer / TelegraphSeconds); // 0 → 1
            float blink = 0.75f + 0.25f * Mathf.Sin(progress * Mathf.PI * 6f);
            var c = TelegraphColor;
            c.a = TelegraphColor.a * Mathf.Lerp(0.55f, 1f, progress) * blink;
            _telegraph.color = c;
        }

        /// <summary>이동/정지 상태가 바뀔 때만 SPUM 전진/대기 애니를 전송한다(클립 재시작 방지).</summary>
        private void SetMoving(bool moving)
        {
            if (moving == _moving) return;
            _moving = moving;
            SendMessage(moving ? "PlayMove" : "PlayIdle", SendMessageOptions.DontRequireReceiver);
        }

        /// <summary>HP 0: 사망 처리 — 사망 애니 재생, 처치 콜백 호출, 잠시 뒤 자기 소멸.</summary>
        private void Die()
        {
            _alive = false;
            // HP바를 잠깐 더 남겨 마지막 타격의 juice(고스트 하강 + 흔들림)를 끝까지 보여 준다.
            // 위치는 지금 자리로 고정한다 — 시신은 DeathFlight로 날아가므로 바가 따라가면 안 된다.
            _hpBarLinger = HpBarDeathLinger;
            _hpBarDeathPos = transform.position;
            // 피격 연출을 즉시 걷어낸다 — 붉은 틴트가 사망 모션 내내 남지 않게 한다.
            _tintTimer = 0f;
            RestoreTint();
            _knockbackRemaining = 0f;
            ShowTelegraph(false); // 예고 중에 죽으면 마커가 남는다
            _telegraphTimer = 0f;
            ApplyWindup(0f);      // 준비 동작 중에 죽으면 크기·위치가 어긋난 채로 날아간다
            // 몬스터·보스 사망 공용음(§5.1·§8). 웨이브 전멸처럼 여러 마리가 동시에 죽어도
            // SoundManager의 같은 클립 쿨다운(0.05초)이 소리가 찢어지는 것을 막는다(§9.3).
            SoundManager.Sfx(SoundId.MonsterDeath);
            SendMessage("PlayDeathOnce", SendMessageOptions.DontRequireReceiver);
            _onDeath?.Invoke(this);
            StartCoroutine(DeathFlight());
        }

        /// <summary>
        /// 처치 연출 — 붉은 플래시 뒤 <b>맞은 방향(뒤+위)으로 날아가며 회전·페이드</b>하고 사라진다.
        /// 그냥 제자리에서 사망 애니만 재생하고 없어지면 처치가 밋밋하고, 방치형은 처치 빈도가 높아
        /// 여기 들이는 투자 효율이 가장 좋다.
        /// <para><b>새 오브젝트를 만들지 않는다</b> — 죽은 몬스터 자신을 날려 보내므로 웨이브 전멸(동시 최대
        /// 12마리)에서도 생성/파괴가 늘지 않는다(제약 §5의 풀링 요구를 이 방식으로 충족한다).</para>
        /// <para>사망 시점의 SPUM 파트 색을 그대로 쓰기 위해 <see cref="_tintParts"/> 캐시를 재사용해 알파만 낮춘다.</para>
        /// </summary>
        private IEnumerator DeathFlight()
        {
            ApplyHitTint();          // 마지막 타격의 붉은 플래시(사망 순간을 확실히 읽히게)
            if (_tintParts == null)
            {
                CacheTintParts();
            }

            float vx = DeathLaunchX * (_isBoss ? 0.45f : 1f); // 보스는 덜 날아간다(무게감)
            float vy = DeathLaunchY * (_isBoss ? 0.6f : 1f);
            // 파일에 using System이 있어 Random은 모호하다 — Unity 쪽을 명시한다.
            float spin = DeathSpin * (_isBoss ? 0.4f : 1f) * (UnityEngine.Random.value < 0.5f ? -1f : 1f);
            Quaternion baseRot = transform.rotation;
            float t = 0f;

            while (t < DeathLinger)
            {
                float dt = Time.deltaTime;
                t += dt;

                var p = transform.position;
                p.x += vx * dt;
                p.y += vy * dt;
                vy -= DeathGravity * dt;          // 포물선
                transform.position = p;
                transform.rotation = baseRot * Quaternion.Euler(0f, 0f, spin * t);

                // 틴트가 끝난 뒤부터 알파를 내린다(플래시 → 페이드 순서).
                if (t >= DeathFadeDelay)
                {
                    float k = 1f - Mathf.Clamp01((t - DeathFadeDelay) / Mathf.Max(0.01f, DeathLinger - DeathFadeDelay));
                    SetPartsAlpha(k);
                }
                yield return null;
            }
            Destroy(gameObject);
        }

        /// <summary>SPUM 파트 전체의 알파에 배수를 적용한다(원래 색·원래 알파 기준). 처치 페이드아웃용.</summary>
        private void SetPartsAlpha(float multiplier)
        {
            if (_tintParts == null) return;
            for (int i = 0; i < _tintParts.Length; i++)
            {
                var r = _tintParts[i];
                if (r == null) continue;
                var c = r.color;
                c.a = _tintOriginals[i].a * Mathf.Clamp01(multiplier);
                r.color = c;
            }
        }
    }
}
