using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TaskbarHero.Client.Managers;
using TaskbarHero.Client.MasterData;
using TaskbarHero.Common.MasterData;

namespace TaskbarHero.Client.Battle
{
    /// <summary>
    /// 전투 시스템 개발용 씬(BattleDevScene)의 자동 전투 하네스(끝없는 전진 전투 + 스킬 + 적 웨이브).
    ///
    /// 파티는 <see cref="party"/> 인스펙터 리스트로 구성한다(1~3인 자유 조합). 오브젝트 생성(아군/적)은
    /// <see cref="ObjectManager"/>가 담당한다: 적은 카메라 우측 바깥에서 주기적으로 스폰돼 파티를 향해
    /// 몰려온다(웨이브 연출). 컨트롤러는 전진(선두 기준)·대형·카메라·교전 판정·공용 데미지를 관리하고,
    /// 각 멤버의 전투/스킬/돌진/버프는 <see cref="PlayerCombatant"/>가 스스로 처리한다.
    ///
    /// 흐름:
    ///   1) 파티가 오른쪽으로 전진(MOVE). 카메라는 최전방 캐릭터 추적 + 배경 무한 스크롤.
    ///   2) 최전방 몬스터가 선두 사거리에 들면 교전(Fighting). 각 멤버가 최전방 몬스터를 공격.
    ///   3) 몬스터 처치 시 뒤의 몬스터가 새 최전방이 되고, 부족분은 우측 바깥에서 계속 스폰(무한 웨이브).
    /// 수치는 마스터 데이터로 구동한다: class_master·monster_master·skill_master.
    /// </summary>
    public class BattleDevController : MonoBehaviour
    {
        private enum Phase { Advancing, Fighting }

        [Header("파티 구성 (1~3인 자유 조합)")]
        [Tooltip("전투에 참여할 멤버. index 0 = 선두(대형 기준). 리스트만 바꾸면 조합이 바뀐다.")]
        public List<PartyMemberConfig> party = new List<PartyMemberConfig>();
        [Tooltip("개발 하네스 전용. 0이 아니면 이 classCode 멤버만 기본 선택해 소환한다(한 직업의 연출을 단독으로 개발할 때). "
                 + "OnGUI '소환 캐릭터 선택'에서 다시 켤 수 있고, serverMode에는 영향이 없다.")]
        public int devSoloClassCode = 0;

        [Header("몬스터 / 스폰 위치")]
        public GameObject monsterPrefab;
        public Transform playerSpawn;
        public Transform monsterSpawn;
        public int monsterCode = 0;

        [Header("적 웨이브 (몰려오는 적)")]
        [Tooltip("몬스터 스폰 주기(초)")]
        public float enemySpawnInterval = 1.5f;
        [Tooltip("동시에 살아있을 수 있는 몬스터 최대 수")]
        public int maxConcurrentEnemies = 5;
        [Tooltip("카메라 우측 끝에서 이만큼 더 바깥(보이지 않는 곳)에 스폰")]
        public float enemyOffscreenMargin = 2f;
        [Tooltip("몬스터가 파티를 향해 전진하는 속도(유닛/초)")]
        public float enemyMoveSpeed = 1.2f;
        [Tooltip("몬스터가 파티 선두 앞에서 멈추는 여백. 2마리 이상이면 아군과 달리 이 지점에 완전히 겹쳐 몰린다")]
        public float enemyFrontStopGap = 1.2f;
        [Tooltip("몬스터가 아군을 공격하는 주기(초). 0 이하면 아군을 공격하지 않는다")]
        public float enemyAttackInterval = 1.5f;
        [Tooltip("몬스터 공격력 배수(아군에게 주는 데미지). 마스터 데이터 값 자체는 불변")]
        public float enemyDamageMultiplier = 2f;

        [Header("적 웨이브 — 불규칙 스폰")]
        [Tooltip("한 번에 나올 수 있는 최대 마리 수. 1이면 종전처럼 한 마리씩만 나온다")]
        public int spawnBurstMax = 3;
        [Tooltip("무리로 나올 확률(0~1). 이 확률로 2마리 이상이 뭉쳐 나온다")]
        [Range(0f, 1f)] public float spawnBurstChance = 0.45f;
        [Tooltip("스폰 간격에 곱하는 최소 배수(작을수록 빨리 이어 나온다)")]
        public float spawnIntervalMinFactor = 0.5f;
        [Tooltip("스폰 간격에 곱하는 최대 배수(클수록 뜸해진다)")]
        public float spawnIntervalMaxFactor = 1.7f;
        [Tooltip("무리로 나올 때 개체 사이의 x 간격(유닛). 같은 자리에 겹쳐 나오지 않게 뒤로 물려 세운다")]
        public float spawnBurstSpacing = 0.8f;
        [Tooltip("스폰 x 위치에 더하는 무작위 흔들림(±유닛). 줄 맞춰 나오는 느낌을 없앤다")]
        public float spawnPositionJitter = 0.35f;
        [Tooltip("전선에 몰렸을 때 개체별로 앞뒤로 벌리는 폭(±유닛). 겹쳐서 한 마리로 보이지 않을 만큼만 아주 작게")]
        public float crowdSpreadX = 0.22f;
        [Tooltip("전선에 몰렸을 때 개체별로 위아래로 벌리는 폭(±유닛). 발밑 라인이 무너지지 않게 x보다 훨씬 작게")]
        public float crowdSpreadY = 0.09f;

        [Header("스킬 설정")]
        [Tooltip("개발용 스킬 레벨(계수 조회 기준)")]
        public int devSkillLevel = 1;
        [Tooltip("스킬 쿨타임 폴백(초). 마스터 데이터 cooldown이 0 이하일 때만 사용")]
        public float skillCooldown = 10f;
        [Tooltip("이펙트 재생 y 오프셋")]
        public float effectYOffset = 0.6f;

        [Header("개발용 편의")]
        [Tooltip("데미지 배수. BattleDevScene 하네스에서 전투를 빨리 돌려보기 위한 값이며, 실게임(serverMode)에서는 무시된다")]
        public float devDamageMultiplier = 20f;

        [Header("이동 / 교전 / 카메라")]
        [Tooltip("카메라 중심을 최전방 아군보다 이만큼 오른쪽에 둔다(아군은 화면 왼쪽, 오른쪽에서 다가오는 적이 멀리서부터 보이게)")]
        public float camOffsetX = 2.5f;
        [Tooltip("최후방 캐릭터 뒤쪽 여백(최후방 아군이 화면 왼쪽 밖으로 밀려나지 않게 하는 하한)")]
        public float camRearMargin = 1.2f;
        public float fallbackMoveSpeed = 3f;

        [Header("대형 (사거리 기반 행 배치)")]
        [Tooltip("공격 사거리를 이 크기로 묶어 같은 행(같은 X)에 배치. 비슷한 사거리끼리 한 행이 됨")]
        public float rangeGroupSize = 3f;
        [Tooltip("행 간 X 간격(뒤 행일수록 뒤로)")]
        public float rowSpacingX = 1.6f;
        [Tooltip("같은 행 내 멤버 간 Y 간격(세로로 벌려 겹침·공격 시각 분리)")]
        public float rowYSpacing = 0.8f;

        [Header("전투 설정")]
        [Tooltip("기본 공격: 공격 모션 후 데미지 적용까지 지연(초)")]
        public float basicAttackHitDelay = 0.35f;

        [Header("서버 구동 모드 (GameScene 던전)")]
        [Tooltip("true면 서버 웨이브 플랜(유한)으로 진행하고 개발용 무한 웨이브를 끈다. BeginServerBattle로 시작.")]
        public bool serverMode = false;

        [Header("걷기 먼지 이펙트")]
        [Tooltip("이동(걷기) 중 캐릭터 발밑에 반복 재생할 먼지 프레임(순서대로).")]
        [SerializeField] private Sprite[] walkDustFrames;
        [Tooltip("걷기 먼지 초당 프레임 수.")]
        [SerializeField] private float walkDustFps = 24f;
        [Tooltip("걷기 먼지 월드 폭(유닛). 확대된 캐릭터 스케일과 무관하게 이 크기로 표시.")]
        [SerializeField] private float walkDustWorldWidth = 1.2f;

        [Header("보스 연출")]
        [Tooltip("보스 몬스터 머리 위에 띄울 아이콘(왕관). 던전 배선 빌더가 자동으로 배선한다.")]
        [SerializeField] private Sprite bossIcon;
        [Tooltip("보스 등장 경고 이미지(Assets/Art/UI/System/boss_warning.png). 화면 중앙에서 커졌다 작아지는 펄스로 표시한다. " +
                 "없으면 종전의 붉은 \"Warning!!\" 문구로 대체한다. 던전 배선 빌더가 자동으로 배선한다.")]
        [SerializeField] private Sprite bossWarningImage;
        [Tooltip("보스의 이동속도 배율(일반 몹 대비). 1보다 작으면 더 느리게 전진한다.")]
        public float bossMoveSpeedFactor = 0.6f;

        [Header("몬스터 체력바")]
        [Tooltip("몬스터 머리 위 HP바의 프레임 아트(Assets/Art/Icon/Combat/체력바.png). " +
                 "없으면 단색 반투명 배경으로 대체한다. 던전 배선 빌더가 BattleDevScene 값을 그대로 복사한다.")]
        [SerializeField] private Sprite enemyHpBarFrame;

        // ---- 히트스톱(큰 타격 순간 시간 정지) ----
        // 평타에는 걸지 않는다 — 상시 걸면 전투가 계속 끊겨 보이고, 강한 한 방이라는 대비가 사라진다.
        private const float HitStopScale = 0.05f;      // 정지 중 시간 배율(완전 0은 애니가 굳어 부자연스럽다)
        private const float HitStopSeconds = 0.06f;    // 치명타·스킬 타격
        private const float BossHitStopSeconds = 0.09f;// 보스 피격은 더 길게(무게감)
        private const float HitStopCooldown = 0.3f;    // 이 간격 안에는 다시 걸지 않는다(연타로 전투가 늘어지지 않게)
        // 웨이브 종료 슬로우: 히트스톱보다 얕고 길게 — 멈춤이 아니라 "마무리" 박자다.
        private const float WaveClearSlowScale = 0.3f;
        private const float WaveClearSlowSeconds = 0.15f;
        private bool _hitStopping;
        private float _slowScale = 1f;                 // 지금 내가 걸어 둔 시간 배율(원복 판정용)
        private float _hitStopReadyAt;                 // 다음 히트스톱 허용 시각(unscaled)

        // 보스 페이즈 전환 비네트(화면 가장자리 붉은 플래시)
        private static readonly Color BossPhaseVignetteColor = new Color(0.85f, 0.05f, 0.05f, 0.55f);
        private const float BossPhaseVignetteSeconds = 0.55f;
        private Canvas _fxCanvas;
        private Image _vignette;
        private Coroutine _vignetteAnim;
        // 버프 지속 동안 켜 두는 비네트는 두지 않는다 — 광전사의 힘이 재사용 대기시간이 짧아 화면이 거의
        // 상시 붉어져 시야를 방해했다(2026-08-04 제거). 비네트는 보스 페이즈 전환처럼 <b>순간</b> 연출에만 쓴다.
        private Image _screenFlash;    // 순간 전체 플래시(라이트닝 볼트)
        private Coroutine _flashAnim;

        // ---- 미세 카메라 스웨이 ----
        // 구도가 늘 고정인 것이 지루함의 숨은 원인이다. 아주 작은 상시 흔들림만으로 화면이 "살아 있게" 보인다.
        // 위치만 흔들고 <b>줌은 건드리지 않는다</b> — 줌 고정 정책(_baseOrtho 강제)과 충돌하지 않는다.
        private const float SwayAmplitudeX = 0.05f;
        private const float SwayAmplitudeY = 0.03f;
        private const float SwaySpeedX = 0.37f;   // 초당 사이클(느리게 — 흔들림이 아니라 '숨'처럼)
        private const float SwaySpeedY = 0.23f;

        // ---- 지속 데칼(강타·내려찍기 자리) ----
        private const float DecalSeconds = 4f;         // 남아 있는 시간
        private const float DecalFadeSeconds = 1.2f;   // 마지막 이만큼은 서서히 사라진다
        private static readonly Color DecalColor = new Color(0.12f, 0.09f, 0.07f, 0.5f); // 그을린 눌림 자국
        private const int DecalSortingOrder = -50;     // 캐릭터·이펙트보다 뒤(바닥)
        private readonly List<SpriteRenderer> _decals = new List<SpriteRenderer>();

        // ---- 진영별 정렬 순서(캐릭터 루트의 SortingGroup) ----
        // 아군과 몬스터 프리팹은 둘 다 SortingGroup order 5로 만들어져 있어, 겹쳤을 때 앞뒤가 정해지지 않는다.
        // 특히 덩치가 큰 보스는 아군을 통째로 덮어 버린다. 그래서 스폰할 때 진영별로 순서를 갈라
        // **아군이 항상 몬스터 앞**에 오게 한다(파티원끼리·몬스터끼리의 순서는 종전대로 동일 값).
        private const int AllySortingOrder = 20;
        private const int EnemySortingOrder = 10;

        // 킬 콤보 표시는 넣지 않는다 — 방치형은 처치가 끊이지 않아 카운터가 사실상 상시 표시가 되고,
        // "연속"이라는 정보가 아무 의미를 갖지 못한다(2026-08-04 확인 후 제거).

        // ---- 카메라 셰이크 ----
        // 진폭 3단: 평타 없음 / 스킬 약 / 보스·광역 다수 강. 상주 소형 창이라 진폭을 작게 잡는다
        // (ortho 5 = 화면 높이 10유닛이므로 0.14는 화면 높이의 1.4%다).
        private const float ShakeLight = 0.08f;
        private const float ShakeHeavy = 0.18f;
        private const float ShakeSeconds = 0.18f;
        private const float ShakeFrequency = 24f;      // 초당 흔들림 진동 수
        private const float ShakeVerticalRatio = 0.7f; // 세로 진폭 비율(횡스크롤이라 가로가 주 방향)
        private float _shakeAmp;
        private float _shakeTimer;
        private float _camBaseY;                       // 셰이크 없을 때의 카메라 y(원복 기준)

        // ---- 런타임 상태 ----
        private Camera _cam;
        private ObjectManager _om;
        private readonly List<PlayerCombatant> _members = new List<PlayerCombatant>();
        private float _pathY;
        private float _partyX;      // 파티 대형 기준 x(선두 행 라인)
        private float _partySpeed;  // 선두 이동 속도
        private int[] _rowIndex;    // 멤버별 행(0=선두, 사거리 오름차순)
        private float[] _formY;     // 멤버별 y 오프셋(같은 행 내 세로 분리)
        private float _engageRange; // 선두 행 사거리(교전 진입 거리)
        private float _baseOrtho = 5f; // 카메라 기본 줌(이보다 크게만 줌아웃)

        // 현재 몬스터 종류 스탯(스폰용 + HP바 표기)
        private string _monsterName = "Monster";
        private long _monsterAtk;
        private long _monsterMaxHp;

        // 적 웨이브 스폰 타이머(스폰 시점/위치·스탯은 컨트롤러가 결정, 생성/추적은 ObjectManager가 담당)
        private float _spawnTimer;
        // 이번에 기다릴 스폰 간격(매 스폰마다 다시 뽑는다 — 0이면 다음 프레임에 바로 낸다).
        private float _nextSpawnDelay;

        // ObjectManager 카테고리 키
        private const string CatAlly = "battle_ally";
        private const string CatEnemy = "battle_enemy";

        private Phase _phase = Phase.Advancing;
        private bool _paused;
        private int _killCount;

        // 서버 구동 모드 상태
        private Queue<QueuedMonster> _serverQueue;             // 스폰할 몬스터(코드+등장 레벨, 순서대로)
        private System.Func<int, GameObject> _prefabResolver;  // 코드 → 프리팹
        private System.Action _onAllCleared;                   // 전멸 시 1회 호출
        private System.Action _onDefeat;                       // 아군 전멸(패배) 시 1회 호출
        private int _serverSpawned;
        private int _serverTotal;                              // 이번 스테이지 전체 스폰 예정 수(진행도 분모)
        private int _serverKilled;                             // 이번 스테이지 누적 처치 수(진행도 분자)
        private bool _serverCleared;
        private bool _defeated;                                // 아군 전멸 판정 1회 가드
        private int _bossCode;                                 // 이 스테이지의 보스 몬스터 코드(0=없음)

        /// <summary>스폰 큐 원소 — 몬스터 코드와 <b>그 자리의 등장 레벨</b>.
        /// 레벨은 몬스터가 아니라 등장 자리의 속성이라(마스터 데이터 값 §9.4) 코드와 함께 실어 나른다.</summary>
        private readonly struct QueuedMonster
        {
            public readonly int Code;
            public readonly int Level;

            public QueuedMonster(int code, int level)
            {
                Code = code;
                Level = level;
            }
        }

        // 소환 캐릭터 선택(테스트용). 현재 구현된 직업만 선택 가능.
        private static readonly HashSet<int> ImplementedClasses = new HashSet<int> { 1, 2, 3, 4 }; // 기사1·레인저2·마법사3·슬레이어4
        private const int MaxPartySlots = 3;  // 편성 자리 1~3(slot 0 = 미편성 → 전투 미참가)

        /// <summary>방어력 경감 곡선의 반감점(<see cref="MitigatedDamage"/>). 방어력이 이 값이면 피해가 절반이 된다.
        /// 씬별로 어긋나면 같은 캐릭터가 화면마다 다르게 맞으므로 인스펙터 노출 없이 상수로 고정한다
        /// (개발용 노브인 <see cref="devDamageMultiplier"/>가 씬에 구워져 실게임까지 따라온 전례가 있다).</summary>
        private const long DefenseMitigationK = 100L;
        private bool[] _selected;

        private readonly List<string> _log = new List<string>();

        /// <summary>파티 멤버(UI 등 외부 조회용). index 0 = 선두.</summary>
        public IReadOnlyList<PlayerCombatant> Party => _members;

        /// <summary>서버 스테이지 전투가 진행 중(스폰 계획 수신 후)인지 — 진행도 바 표시 여부 판정용.</summary>
        public bool ServerBattleActive => serverMode && _serverTotal > 0;

        /// <summary>서버 스테이지 진행도(0~1): 처치 수 ÷ 전체 스폰 예정 수. 전멸(클리어) 시 1, 서버 전투가 아니면 0.</summary>
        public float ServerBattleProgress
        {
            get
            {
                if (!ServerBattleActive)
                {
                    return 0f;
                }
                if (_serverCleared)
                {
                    return 1f;
                }
                return Mathf.Clamp01((float)_serverKilled / _serverTotal);
            }
        }

        private void Start()
        {
            _cam = Camera.main;
            if (_cam != null && _cam.orthographic) _baseOrtho = _cam.orthographicSize;
            if (_cam != null) _camBaseY = _cam.transform.position.y; // 셰이크 원복 기준(씬이 잡아 둔 프레이밍 y)
            MasterDataManager.EnsureLoaded();
            ResolveMonsterCode();
            LoadMonsterStats();

            Vector3 start = playerSpawn != null ? playerSpawn.position : new Vector3(-4.5f, -1.6f, 0f);
            _pathY = start.y;
            _partyX = start.x;

            // 씬이 바뀌어도 유지되는 범용 ObjectManager(싱글턴) 사용. 이전 씬 잔여물이 남지 않도록 카테고리 초기화.
            _om = ObjectManager.EnsureInstance();
            _om.Clear(CatAlly);
            _om.Clear(CatEnemy);
            _spawnTimer = enemySpawnInterval; // 시작하자마자 첫 무리 스폰
            _nextSpawnDelay = 0f;             // 첫 스폰은 기다리지 않는다(이후부터 간격을 무작위로 뽑는다)

            InitSelection();
            SpawnParty(start);
            _phase = Phase.Advancing;

            // 장비 장착/해제 시 파티 전투 스탯을 즉시 재계산한다.
            Session.InventoryChanged += RefreshPartyStats;

            Log($"전투 시작 — 파티 {_members.Count}인 vs {_monsterName} 웨이브(hp {_monsterMaxHp}, 동시 최대 {maxConcurrentEnemies})");
        }

        private void OnDestroy()
        {
            Session.InventoryChanged -= RefreshPartyStats;
            CancelTimeEffects();
        }

        /// <summary>
        /// 진행 중인 히트스톱·카메라 셰이크를 즉시 끝내고 시간 배율을 원복한다.
        /// <para>히트스톱은 코루틴이 끝나면서 배율을 되돌리므로, <b>코루틴이 죽는 시점</b>
        /// (씬 전환·<see cref="OnDestroy"/>·<c>StopAllCoroutines</c>로 재시작)마다 반드시 불러야
        /// 0.05배가 그대로 남지 않는다.</para>
        /// </summary>
        private void CancelTimeEffects()
        {
            if (_hitStopping)
            {
                if (Mathf.Approximately(Time.timeScale, _slowScale))
                {
                    Time.timeScale = 1f; // 다른 연출(클리어 슬로우모션)이 잡은 배율은 건드리지 않는다
                }
                _hitStopping = false;
            }
            _shakeTimer = 0f;
            _shakeAmp = 0f;
        }

        /// <summary>
        /// 큰 타격(치명타·스킬·보스 피격) 순간에 시간을 아주 짧게 <see cref="HitStopScale"/>로 떨어뜨린다.
        /// 임팩트가 "멈춰서 보이는" 효과가 넉백보다 체감이 크다.
        /// </summary>
        private void RequestHitStop(float seconds)
        {
            RequestTimeScale(HitStopScale, seconds, respectCooldown: true);
        }

        /// <summary>
        /// 웨이브의 마지막 한 마리를 잡은 순간처럼 <b>얕고 조금 긴</b> 슬로우를 건다(히트스톱보다 덜 멈춘다).
        /// 웨이브당 한 번뿐이라 쿨다운은 보지 않는다(직전 타격의 히트스톱 쿨다운에 삼켜지면 안 된다).
        /// </summary>
        private void RequestSlowMotion(float scale, float seconds)
        {
            RequestTimeScale(scale, seconds, respectCooldown: false);
        }

        /// <summary>
        /// 시간 배율 연출의 공통 창구.
        /// <para>다음 경우에는 걸지 않는다 — ① 이미 이 컨트롤러가 시간을 잡고 있을 때 ② 쿨다운
        /// (<see cref="HitStopCooldown"/>, 히트스톱만) ③ 일시정지 중 ④ <b>다른 연출이 이미 시간 배율을
        /// 잡고 있을 때</b>(클리어·패배 슬로우모션 — 여기에 끼어들면 그 배율을 덮어써 연출이 깨진다).</para>
        /// </summary>
        private void RequestTimeScale(float scale, float seconds, bool respectCooldown)
        {
            if (_hitStopping || _paused) return;
            if (respectCooldown && Time.unscaledTime < _hitStopReadyAt) return;
            if (!Mathf.Approximately(Time.timeScale, 1f)) return;
            StartCoroutine(HitStopRoutine(Mathf.Clamp(scale, 0.01f, 0.95f), seconds));
        }

        /// <summary>
        /// 큰 타격에 카메라를 짧게 흔든다. 진폭은 3단이며 <b>평타에는 걸지 않는다</b>(히트스톱과 같은 지점에서 호출).
        /// <para>이미 흔들리는 중이면 <b>더 센 쪽</b>으로 갈아탄다(합산하면 화면이 튀어 오른다).</para>
        /// </summary>
        private void RequestShake(float amplitude)
        {
            if (_paused || amplitude <= 0f) return;
            _shakeAmp = Mathf.Max(_shakeAmp, amplitude);
            _shakeTimer = ShakeSeconds;
        }

        /// <summary>
        /// 이번 프레임에 카메라에 더할 셰이크 오프셋을 계산하고 타이머를 진행한다.
        /// <para><b>unscaled 시간</b>을 쓴다 — 히트스톱으로 시간이 멈춘 0.06초 동안에도 화면이 흔들려야
        /// "멈춘 채 얻어맞는" 임팩트가 살아난다(스케일 시간이면 정지 중 셰이크도 함께 멈춘다).</para>
        /// <para><b>감쇠 사인</b>을 쓴다. 프레임마다 난수를 뽑으면 지글거리고, 펄린 노이즈는 값이 0.5 근처에
        /// 몰려 있어 <b>지정 진폭의 20%밖에 나오지 않았다</b>(실측 0.06 지정 → 최대 0.013유닛 ≈ 1px로
        /// 사실상 안 보였다). 사인은 짧은 0.18초 버스트에서 주기성이 눈에 띄지 않고 진폭을 정확히 쓴다.
        /// x·y는 서로 다른 주파수·위상을 써서 같은 대각선만 왕복하지 않게 한다.</para>
        /// </summary>
        private Vector2 ConsumeShakeOffset()
        {
            if (_shakeTimer <= 0f)
            {
                return Vector2.zero;
            }
            _shakeTimer -= Time.unscaledDeltaTime;
            if (_shakeTimer <= 0f)
            {
                _shakeAmp = 0f;
                return Vector2.zero;
            }
            float decay = Mathf.Clamp01(_shakeTimer / ShakeSeconds); // 끝으로 갈수록 잦아든다
            float phase = Time.unscaledTime * ShakeFrequency;
            float amp = _shakeAmp * decay;
            return new Vector2(
                Mathf.Sin(phase) * amp,
                Mathf.Sin(phase * 1.63f + 1.1f) * amp * ShakeVerticalRatio);
        }

        /// <summary>
        /// 상시 미세 스웨이 오프셋 — 서로 다른 아주 느린 사인 두 개로 카메라가 조용히 떠 있게 만든다.
        /// <para>진폭이 0.05·0.03유닛(화면 높이의 0.3~0.5%)이라 흔들린다고 느끼지 못하면서도 고정 구도의
        /// 정적인 느낌이 사라진다. <b>줌은 건드리지 않으므로</b> 줌 고정 정책(<see cref="_baseOrtho"/> 강제)과
        /// 충돌하지 않는다 — 예외를 둘 필요가 없었다.</para>
        /// <para>일시정지 중에는 멈춘다(정지 화면이 미묘하게 흐르면 정지처럼 보이지 않는다).</para>
        /// </summary>
        private Vector2 SwayOffset()
        {
            if (_paused)
            {
                return Vector2.zero;
            }
            float t = Time.unscaledTime;
            return new Vector2(
                Mathf.Sin(t * SwaySpeedX * Mathf.PI * 2f) * SwayAmplitudeX,
                Mathf.Sin(t * SwaySpeedY * Mathf.PI * 2f + 1.7f) * SwayAmplitudeY);
        }

        /// <summary>히트스톱·슬로우 본체 — 시간 배율을 떨어뜨리고 <b>실시간</b>으로 기다린 뒤 원복한다.</summary>
        private IEnumerator HitStopRoutine(float scale, float seconds)
        {
            _hitStopping = true;
            _slowScale = scale;
            Time.timeScale = scale;
            yield return new WaitForSecondsRealtime(Mathf.Max(0.01f, seconds));
            // 정지 중에 클리어·패배 슬로우모션이 시작됐다면 그 배율을 존중한다(우리 값일 때만 원복).
            if (Mathf.Approximately(Time.timeScale, scale))
            {
                Time.timeScale = 1f;
            }
            _slowScale = 1f;
            _hitStopping = false;
            _hitStopReadyAt = Time.unscaledTime + HitStopCooldown;
        }

        /// <summary>
        /// 화면 가장자리를 물들이는 <b>비네트 플래시</b>(보스 페이즈 전환용). 빠르게 켜졌다 서서히 사라진다.
        /// <para>가운데는 투명한 절차적 비네트 스프라이트를 쓰므로 캐릭터·숫자를 가리지 않는다
        /// (<see cref="BattleFxTextures.Vignette"/>). 전투 화면 밴드(<see cref="GameAreaRect"/>) 안에만
        /// 그려 좌우 UI 패널 영역까지 붉어지지 않게 한다.</para>
        /// </summary>
        private void FlashVignette(Color color, float seconds)
        {
            EnsureFxCanvas();
            if (_vignette == null) return;
            if (_vignetteAnim != null)
            {
                StopCoroutine(_vignetteAnim);
            }
            _vignetteAnim = StartCoroutine(VignetteRoutine(color, seconds));
        }

        private IEnumerator VignetteRoutine(Color color, float seconds)
        {
            float dur = Mathf.Max(0.05f, seconds);
            float t = 0f;
            var go = _vignette.gameObject;
            go.SetActive(true);
            while (t < dur)
            {
                t += Time.unscaledDeltaTime; // 페이즈 전환 셰이크·히트스톱과 결을 맞춘다
                float k = Mathf.Clamp01(t / dur);
                // 0.15까지 빠르게 차오르고 그 뒤 천천히 빠진다.
                float a = k < 0.15f ? k / 0.15f : 1f - (k - 0.15f) / 0.85f;
                var c = color;
                c.a = color.a * a;
                _vignette.color = c;
                yield return null;
            }
            go.SetActive(false);
            _vignetteAnim = null;
        }

        /// <summary>
        /// 화면 전체를 아주 짧게 물들인다(라이트닝 볼트의 백색 섬광). 스프라이트 없이 단색 <c>Image</c>를 쓴다.
        /// <para>상주 소형 창이므로 <b>알파를 낮게(0.35) 그리고 아주 짧게</b> 쓴다 — 전체 백색 플래시를
        /// 불투명하게 넣으면 장시간 켜 두는 사용 패턴에서 눈이 피곤하다(제약 §5).</para>
        /// </summary>
        public void FlashScreen(Color color, float seconds)
        {
            EnsureFxCanvas();
            if (_screenFlash == null) return;
            if (_flashAnim != null)
            {
                StopCoroutine(_flashAnim);
            }
            _flashAnim = StartCoroutine(ScreenFlashRoutine(color, seconds));
        }

        /// <summary>지연 뒤 화면 플래시(스킬 타격 시점에 맞춘다).</summary>
        public void FlashScreenAfter(float delay, Color color, float seconds)
        {
            StartCoroutine(FlashAfterRoutine(delay, color, seconds));
        }

        private IEnumerator FlashAfterRoutine(float delay, Color color, float seconds)
        {
            if (delay > 0f)
            {
                yield return new WaitForSeconds(delay);
            }
            FlashScreen(color, seconds);
        }

        private IEnumerator ScreenFlashRoutine(Color color, float seconds)
        {
            float dur = Mathf.Max(0.03f, seconds);
            var go = _screenFlash.gameObject;
            go.SetActive(true);
            float t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                var c = color;
                c.a = color.a * (1f - Mathf.Clamp01(t / dur)); // 즉시 최대 → 빠르게 감소
                _screenFlash.color = c;
                yield return null;
            }
            go.SetActive(false);
            _flashAnim = null;
        }

        /// <summary>
        /// 지정 위치 바닥에 <b>지속 데칼</b>을 남긴다(강타·내려찍기 자리). 전투가 지나간 흔적이 보이게 한다.
        /// <para>스프라이트는 절차적 부드러운 타원을 어둡게 깔아 <b>그을린 눌림 자국</b>으로 쓴다 —
        /// 프로젝트에 갈라진 바닥 아트가 없어서다(아트가 생기면 스프라이트만 갈아 끼우면 된다).</para>
        /// <para>데칼은 <b>풀로 재사용</b>한다(스킬마다 생성/파괴하지 않는다).</para>
        /// </summary>
        public void SpawnGroundDecal(Vector3 worldPos, float width)
        {
            var sr = GetFreeDecal();
            sr.transform.position = new Vector3(worldPos.x, _pathY, 0f); // 항상 길(지면) 높이에
            float w = Mathf.Max(0.4f, width);
            sr.transform.localScale = new Vector3(w, w * 0.32f, 1f);     // 납작하게 눌러 바닥에 붙은 느낌
            sr.color = DecalColor;
            sr.gameObject.SetActive(true);
            StartCoroutine(DecalFadeRoutine(sr));
        }

        /// <summary>지연 뒤 데칼을 남긴다(타격 시점에 맞춘다).</summary>
        public void SpawnGroundDecalAfter(float delay, Vector3 worldPos, float width)
        {
            StartCoroutine(DecalAfterRoutine(delay, worldPos, width));
        }

        private IEnumerator DecalAfterRoutine(float delay, Vector3 worldPos, float width)
        {
            if (delay > 0f)
            {
                yield return new WaitForSeconds(delay);
            }
            SpawnGroundDecal(worldPos, width);
        }

        private IEnumerator DecalFadeRoutine(SpriteRenderer sr)
        {
            float t = 0f;
            while (t < DecalSeconds && sr != null)
            {
                t += Time.deltaTime;
                float left = DecalSeconds - t;
                if (left < DecalFadeSeconds)
                {
                    var c = DecalColor;
                    c.a = DecalColor.a * Mathf.Clamp01(left / DecalFadeSeconds);
                    sr.color = c;
                }
                yield return null;
            }
            if (sr != null) sr.gameObject.SetActive(false); // 풀로 반환
        }

        /// <summary>비활성 데칼을 꺼내거나 새로 만든다(풀).</summary>
        private SpriteRenderer GetFreeDecal()
        {
            for (int i = 0; i < _decals.Count; i++)
            {
                if (_decals[i] != null && !_decals[i].gameObject.activeSelf)
                {
                    return _decals[i];
                }
            }
            var go = new GameObject("GroundDecal");
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = BattleFxTextures.SoftEllipse();
            sr.sortingOrder = DecalSortingOrder;
            go.SetActive(false);
            _decals.Add(sr);
            return sr;
        }

        /// <summary>
        /// 지정 중심 반경 안의 살아있는 적을 <b>빙결</b>시킨다(프로스트 노바). 데미지는 건드리지 않는다.
        /// </summary>
        public void FreezeEnemiesNear(float delay, Vector3 center, float radius, float seconds)
        {
            StartCoroutine(FreezeRoutine(delay, center, radius, seconds));
        }

        private IEnumerator FreezeRoutine(float delay, Vector3 center, float radius, float seconds)
        {
            if (delay > 0f)
            {
                yield return new WaitForSeconds(delay);
            }
            if (_om == null) yield break;
            float r2 = radius * radius;
            foreach (var go in _om.Active(CatEnemy))
            {
                var mu = go.GetComponent<MonsterUnit>();
                if (mu == null || !mu.Alive) continue;
                if (((Vector2)mu.transform.position - (Vector2)center).sqrMagnitude <= r2)
                {
                    mu.ApplyFreeze(seconds);
                }
            }
        }

        /// <summary>비네트 등 전면 연출용 Canvas를 최초 1회 만든다(HP바 캔버스보다 위, HUD·패널보다 아래).</summary>
        private void EnsureFxCanvas()
        {
            if (_fxCanvas != null)
            {
                return;
            }
            var go = new GameObject("BattleFxCanvas", typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(transform, false);
            _fxCanvas = go.GetComponent<Canvas>();
            _fxCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _fxCanvas.sortingOrder = EnemyHpBarSortingOrder + 1; // HP바 위, HUD(10)·패널(100) 아래
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;

            // 순간 비네트(보스 페이즈) → 전체 플래시 순서로 겹친다(뒤에 만든 것이 위에 온다).
            _vignette = NewFxOverlay(go.transform, "Vignette", BattleFxTextures.Vignette());
            _screenFlash = NewFxOverlay(go.transform, "ScreenFlash", null); // 단색(스프라이트 없음)
        }

        /// <summary>전면 연출용 전체 화면 오버레이 Image를 만든다(전투 밴드에만 붙고 클릭은 통과).</summary>
        private static Image NewFxOverlay(Transform parent, string name, Sprite sprite)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            GameAreaRect.Attach((RectTransform)go.transform); // 전투 화면 밴드만 덮는다
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;
            img.color = new Color(1f, 1f, 1f, 0f);
            go.SetActive(false);
            return img;
        }

        /// <summary>장비/스킬(장착·레벨) 변경 등으로 모든 파티 멤버의 전투 스탯과 사용 스킬 세트를 재계산한다.</summary>
        public void RefreshPartyStats()
        {
            foreach (var m in _members)
            {
                if (m != null)
                {
                    m.RefreshStats();
                    m.RebuildSkills(); // 장착 스킬(최대 2)·습득 레벨 변경을 전투에 반영
                }
            }
            // 스킬 슬롯 개수(장착 수)가 바뀔 수 있으므로 전투 스킬 HUD도 다시 구성한다.
            var skillUi = Object.FindAnyObjectByType<SkillCooldownUI>(FindObjectsInactive.Include);
            if (skillUi != null)
            {
                skillUi.Rebuild();
            }
            Log("장비/스킬 변경 → 파티 전투 스탯·스킬 세트 재계산");
        }

        private void Update()
        {
            if (_paused || _members.Count == 0 || _om == null)
            {
                return;
            }

            // 적 웨이브 갱신(주기 스폰 + 큐 배치). 큐 라인은 파티 선두 기준.
            TickWave(Time.deltaTime);

            // 최전방 몬스터 기준으로 전진/교전 판정
            UpdatePhase();

            // 매 프레임 각 멤버에게 대형 목표를 전달(멤버가 자기 속도로 이동)
            PushFormationTargets();
        }

        /// <summary>최전방 몬스터와의 거리로 전진/교전을 결정한다(히스테리시스로 경계 떨림 방지).</summary>
        private void UpdatePhase()
        {
            var front = FrontMonsterUnit();
            if (front != null)
            {
                float dist = front.transform.position.x - _partyX;
                if (_phase == Phase.Fighting)
                {
                    if (dist > _engageRange + 0.3f) _phase = Phase.Advancing; // 최전방 처치 후 다음 적으로 재전진
                }
                else if (dist <= _engageRange)
                {
                    EnterFighting(front);
                }
            }
            // 몬스터가 없으면 계속 전진(웨이브 사이).

            if (_phase == Phase.Advancing) _partyX += _partySpeed * Time.deltaTime;
        }

        private void LateUpdate()
        {
            UpdateEnemyHpBars(); // 적 HP바(Canvas)를 매 프레임 몬스터 위치에 맞춰 갱신

            if (_cam == null || _members.Count == 0) return;

            // 팔로우 기준은 <b>연출 오프셋을 뺀</b> 위치(`CameraFollowX`)다 — 근접 평타의 lunge(0.32유닛을
            // 0.18초에 앞뒤로 왕복)를 그대로 따라가면 기본공격마다 카메라가 왕복해 셰이크처럼 보인다.
            float front = float.NegativeInfinity, rear = float.PositiveInfinity;
            foreach (var m in _members)
            {
                if (m == null) continue;
                float x = m.CameraFollowX;
                if (x > front) front = x;
                if (x < rear) rear = x;
            }
            if (float.IsNegativeInfinity(front)) return;

            // 카메라 줌은 고정(_baseOrtho) — 아군 간 거리에 따른 확대/축소를 하지 않는다.
            if (_baseOrtho > 0f && !Mathf.Approximately(_cam.orthographicSize, _baseOrtho))
            {
                _cam.orthographicSize = _baseOrtho;
            }

            // x 팔로우: 카메라 중심을 최전방 아군보다 camOffsetX만큼 오른쪽에 둔다
            // (아군은 화면 왼쪽에 두고, 오른쪽에서 다가오는 적이 멀리서부터 보이게).
            // 단, 최후방 아군이 화면 왼쪽 밖으로 밀려나면 그만큼만 왼쪽으로 당긴다.
            float halfW = _cam.orthographicSize * Mathf.Max(0.01f, _cam.aspect);
            Vector3 c = _cam.transform.position;
            c.x = Mathf.Min(front + camOffsetX, rear - camRearMargin + halfW);

            // 셰이크·스웨이는 팔로우 결과 위에 오프셋으로 얹는다 — 팔로우가 매 프레임 x를 다시 계산하고
            // y는 기준값(_camBaseY)에서 다시 잡으므로 흔들림이 누적되어 카메라가 떠내려가지 않는다.
            Vector2 shake = ConsumeShakeOffset();
            Vector2 sway = SwayOffset();
            c.x += shake.x + sway.x;
            c.y = _camBaseY + shake.y + sway.y;
            _cam.transform.position = c;
        }

        /// <summary>각 멤버에게 대형 목표(행 기반 X + 같은 행 Y 분리)를 전달한다. 이동은 멤버가 스스로 수행.</summary>
        private void PushFormationTargets()
        {
            if (_rowIndex == null) return;
            for (int i = 0; i < _members.Count; i++)
            {
                var m = _members[i];
                if (m == null) continue;
                float tx = _partyX - _rowIndex[i] * rowSpacingX;
                float ty = _pathY + _formY[i];
                m.SetFormationTarget(new Vector2(tx, ty));
            }
        }

        // ---- 마스터 데이터 로드 ----

        /// <summary>
        /// 웨이브에 쓸 몬스터 코드를 정한다.
        /// <para>CharacterDevScene이 검수용으로 넘긴 코드가 있으면 그것을 먼저 쓴다 —
        /// 그 씬은 EditorPrefs(<c>TaskbarHero.Dev.SpawnMonsterCode</c>)에 코드를 써 두고 이 씬으로 넘어온다
        /// (캐릭터 개발씬 기획서 §7.3). 한 번만 적용되도록 읽은 즉시 키를 지우며, 인스펙터 필드는
        /// 키가 없을 때 손으로 지정하는 경로로 남는다.</para>
        /// </summary>
        private void ResolveMonsterCode()
        {
            var db = MasterDataManager.Db;
            if (db == null) return;
#if UNITY_EDITOR
            const string DevSpawnCodeKey = "TaskbarHero.Dev.SpawnMonsterCode";
            if (UnityEditor.EditorPrefs.HasKey(DevSpawnCodeKey))
            {
                int devCode = UnityEditor.EditorPrefs.GetInt(DevSpawnCodeKey, 0);
                UnityEditor.EditorPrefs.DeleteKey(DevSpawnCodeKey);
                if (devCode != 0 && db.Monsters.ContainsKey(devCode))
                {
                    monsterCode = devCode;
                    Debug.Log($"[BattleDev] 검수 요청 몬스터로 고정: {devCode}");
                    return;
                }
            }
#endif
            if (monsterCode != 0 && db.Monsters.ContainsKey(monsterCode)) return;
            foreach (var code in db.Monsters.Keys) { monsterCode = code; return; }
        }

        private void LoadMonsterStats()
        {
            var db = MasterDataManager.Db;
            if (db != null && db.Monsters.TryGetValue(monsterCode, out MonsterMaster mon))
            {
                _monsterName = mon.name;
                // 개발용 무한 웨이브는 등장 자리(stage_spawn)가 없어 레벨도 없다 → 레벨 1 기준값을 그대로 쓴다.
                // 레벨 배율이 붙는 것은 서버 플랜으로 스폰하는 경로(SpawnMonsterByCode)뿐이다.
                MonsterStats.Scale(mon, 1, out _monsterMaxHp, out _monsterAtk);
            }
            else
            {
                _monsterName = "Monster(?" + monsterCode + ")";
                _monsterMaxHp = 100;
                _monsterAtk = 5;
                Log($"[경고] monster_master에 코드 {monsterCode} 없음 → 임시값");
            }
        }

        // ---- 스폰 / 대형 ----

        /// <summary>선택 상태 배열을 초기화한다. 개발 모드는 구현된 직업 전부(단
        /// <see cref="devSoloClassCode"/>가 지정되면 그 직업만), 서버 모드는
        /// <b>파티에 편성된 캐릭터</b>(slot 1~3)의 직업과 일치하는 멤버만 선택한다 —
        /// slot 0(미편성)은 보유만 하고 전투에 나가지 않으므로 스폰하지 않는다(세이브 데이터 기획서 5.5).</summary>
        private void InitSelection()
        {
            _selected = new bool[party.Count];

            HashSet<int> accountClasses = null;
            if (serverMode)
            {
                accountClasses = new HashSet<int>();
                var chars = Session.GameData != null ? Session.GameData.characters : null;
                if (chars != null)
                {
                    foreach (var c in chars)
                    {
                        if (c != null && c.slot >= 1 && c.slot <= MaxPartySlots) accountClasses.Add(c.classCode);
                    }
                }
            }

            for (int i = 0; i < party.Count; i++)
            {
                bool ok = IsImplemented(party[i]);
                if (ok && accountClasses != null)
                {
                    ok = party[i] != null && accountClasses.Contains(party[i].classCode); // 계정 보유 직업만
                }
                else if (ok && devSoloClassCode != 0)
                {
                    ok = party[i].classCode == devSoloClassCode; // 개발 하네스 단독 소환
                }
                _selected[i] = ok;
            }
        }

        /// <summary>구현이 완료돼 선택 가능한 멤버인지(프리팹 지정 + 구현 직업 코드).</summary>
        private static bool IsImplemented(PartyMemberConfig cfg)
        {
            return cfg != null && cfg.prefab != null && ImplementedClasses.Contains(cfg.classCode);
        }

        private void SpawnParty(Vector3 start)
        {
            _members.Clear();

            for (int i = 0; i < party.Count; i++)
            {
                var cfg = party[i];
                if (cfg == null || cfg.prefab == null) continue;
                if (!IsImplemented(cfg)) continue;                       // 미구현 직업 제외
                if (_selected != null && i < _selected.Length && !_selected[i]) continue; // 미선택 제외

                var pc = SpawnAlly(cfg, new Vector3(start.x, _pathY, 0f));
                if (pc == null) continue;
                _members.Add(pc);
                Log($"합류 — {pc.DisplayName}(사거리 {pc.AttackRange:0.#}{(cfg.ranged ? ", 원거리" : "")})");
            }

            _partySpeed = _members.Count > 0 ? Mathf.Max(0.1f, _members[0].MoveSpeed) : fallbackMoveSpeed;

            ComputeFormation();
            for (int i = 0; i < _members.Count; i++)
            {
                if (_members[i] == null) continue;
                _members[i].transform.position = new Vector3(_partyX - _rowIndex[i] * rowSpacingX, _pathY + _formY[i], 0f);
            }
        }

        /// <summary>
        /// 새 스테이지 전투를 시작할 때, 이전 전투에서 전사해 사라진 파티원을 다시 스폰한다(전체 체력으로 부활).
        /// 살아남은 멤버는 그대로 두고(위치·남은 체력 유지) 빠진 자리만 채운 뒤, 파티 순서를 원래 편성 순서로
        /// 되돌려 대형·전투 UI를 다시 구성한다. 파티는 직업 중복이 없으므로 <see cref="PlayerCombatant.ClassCode"/>로
        /// 생존 여부를 판정한다.
        /// </summary>
        private void RestoreFallenMembers()
        {
            if (party == null || _om == null)
            {
                return;
            }
            _members.RemoveAll(m => m == null); // 파괴 대기 중이던 참조 정리

            bool restored = false;
            for (int i = 0; i < party.Count; i++)
            {
                var cfg = party[i];
                if (cfg == null || cfg.prefab == null || !IsImplemented(cfg)) continue;
                if (_selected != null && i < _selected.Length && !_selected[i]) continue; // 편성되지 않은 직업
                if (HasLiveMember(cfg.classCode)) continue;                               // 이미 살아 있음

                // 대형 목표는 아래에서 다시 계산되므로, 우선 현재 파티 라인에 세운다.
                var pc = SpawnAlly(cfg, new Vector3(_partyX, _pathY, 0f));
                if (pc == null) continue;
                _members.Add(pc);
                restored = true;
                Log($"재합류 — {pc.DisplayName}(이전 전투 전사)");
            }

            if (!restored)
            {
                return;
            }

            SortMembersByPartyOrder();
            _partySpeed = _members.Count > 0 ? Mathf.Max(0.1f, _members[0].MoveSpeed) : fallbackMoveSpeed;
            ComputeFormation();

            // 아군 HP바·스킬 슬롯(전투 UI)을 새 파티 구성으로 다시 만든다(전사 시 Rebuild와 짝).
            var ui = FindAnyObjectByType<SkillCooldownUI>();
            if (ui != null) ui.Rebuild();
        }

        /// <summary>파티 전원의 체력을 최대치로 되돌린다(스테이지 시작 시 무조건 회복).
        /// 스테이지를 넘겨도 체력이 누적 소모되어 뒤 스테이지가 부당하게 어려워지는 것을 막는다.</summary>
        private void HealPartyFull()
        {
            foreach (var m in _members)
            {
                if (m != null && m.Alive)
                {
                    m.RestoreFullHp();
                }
            }
        }

        /// <summary>해당 직업의 살아 있는 파티원이 있는지.</summary>
        private bool HasLiveMember(int classCode)
        {
            foreach (var m in _members)
            {
                if (m != null && m.Alive && m.ClassCode == classCode)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>파티 목록을 편성(party 인스펙터) 순서로 정렬한다. 대형 계산이 이 순서를 전제로 한다.</summary>
        private void SortMembersByPartyOrder()
        {
            var ordered = new List<PlayerCombatant>(_members.Count);
            for (int i = 0; i < party.Count; i++)
            {
                var cfg = party[i];
                if (cfg == null) continue;
                foreach (var m in _members)
                {
                    if (m != null && m.ClassCode == cfg.classCode)
                    {
                        ordered.Add(m);
                        break;
                    }
                }
            }
            // 편성 목록에 없는(예외) 멤버는 뒤에 붙여 유실을 막는다.
            foreach (var m in _members)
            {
                if (m != null && !ordered.Contains(m))
                {
                    ordered.Add(m);
                }
            }
            _members.Clear();
            _members.AddRange(ordered);
        }

        /// <summary>
        /// 스폰에 쓸 캐릭터 프리팹을 고른다. 서버 모드에서는 세이브의 성별(1:남 2:여)에 맞는 프리팹을
        /// 공용 <see cref="CharacterPrefabDatabase"/>에서 찾아 쓰고, 개발 하네스(BattleDevScene)나
        /// 등록된 프리팹이 없을 때는 인스펙터에 배선된 프리팹을 그대로 쓴다.
        /// </summary>
        private GameObject PrefabFor(PartyMemberConfig cfg)
        {
            if (cfg == null)
            {
                return null;
            }
            if (!serverMode)
            {
                return cfg.prefab;
            }
            var byGender = CharacterPrefabDatabase.PrefabOf(cfg.classCode, GenderOfClass(cfg.classCode));
            return byGender != null ? byGender : cfg.prefab;
        }

        /// <summary>계정 세이브에서 해당 직업 캐릭터의 성별을 찾는다(없으면 1:남).</summary>
        private static int GenderOfClass(int classCode)
        {
            var chars = Session.GameData != null ? Session.GameData.characters : null;
            if (chars != null)
            {
                foreach (var c in chars)
                {
                    if (c != null && c.classCode == classCode && c.gender > 0)
                    {
                        return c.gender;
                    }
                }
            }
            return CharacterPrefabDatabase.DefaultGender;
        }

        /// <summary>아군 1인을 ObjectManager로 생성하고 SPUM 애니메이터/방향/전투 컴포넌트를 배선해 반환한다.</summary>
        private PlayerCombatant SpawnAlly(PartyMemberConfig cfg, Vector3 pos)
        {
            var go = _om.Spawn(CatAlly, PrefabFor(cfg), pos, Quaternion.identity);
            if (go == null) return null;
            EnsureSpumAnimator(go);
            SetUnitSortingOrder(go, AllySortingOrder); // 몬스터(보스 포함)에 가리지 않게 항상 앞
            SetFacingRight(go, true); // 아군은 오른쪽(적)을 바라봄
            var pc = go.GetComponent<PlayerCombatant>();
            if (pc == null) pc = go.AddComponent<PlayerCombatant>();
            pc.Configure(this, cfg);
            AttachWalkDust(go, () => pc != null && pc.IsMoving); // 걷기 먼지(이동 애니 재생 중에만 노출)
            return pc;
        }

        /// <summary>캐릭터 발밑에 걷기 먼지 이펙트를 붙인다(프레임 미배선이면 생략). isMoving은 이동 애니 재생 여부.</summary>
        private void AttachWalkDust(GameObject owner, System.Func<bool> isMoving)
        {
            if (walkDustFrames == null || walkDustFrames.Length == 0 || owner == null) return;
            var dustGo = new GameObject("WalkDust");
            dustGo.transform.SetParent(owner.transform, false);
            var wd = dustGo.AddComponent<WalkDust>();
            wd.Initialize(walkDustFrames, walkDustFps, isMoving, walkDustWorldWidth);
        }

        // ---- 적 웨이브(스폰 시점/위치·스탯은 컨트롤러가 결정, 생성/추적/정리는 ObjectManager) ----

        // ── 불규칙 스폰 ──
        // 고정 간격으로 한 마리씩 내보내면 "한 줄로 배급되는" 인상이 강해, 몰려오는 웨이브로 읽히지 않는다.
        // 그래서 ① 다음 스폰까지의 대기를 매번 다시 뽑고(간격 지터) ② 가끔 2~3마리를 한 번에 내며(무리)
        // ③ 무리는 x를 뒤로 물려 세워 겹치지 않게 한다. 동시 상한(maxConcurrentEnemies)은 그대로 지킨다.

        /// <summary>이번 스폰까지 기다릴 시간을 뽑는다(기본 주기 × 무작위 배수).</summary>
        private float RollSpawnDelay()
        {
            float baseInterval = Mathf.Max(0.1f, enemySpawnInterval);
            float lo = Mathf.Max(0.05f, spawnIntervalMinFactor);
            float hi = Mathf.Max(lo, spawnIntervalMaxFactor);
            return baseInterval * Random.Range(lo, hi);
        }

        /// <summary>이번에 한 번에 내보낼 마리 수(1 ~ <see cref="spawnBurstMax"/>).
        /// <see cref="spawnBurstChance"/>로 무리 여부를 정하고, 무리면 2마리 이상에서 균등하게 뽑는다.</summary>
        private int RollBurstSize()
        {
            int max = Mathf.Max(1, spawnBurstMax);
            if (max == 1 || Random.value >= spawnBurstChance)
            {
                return 1;
            }
            return Random.Range(2, max + 1);
        }

        /// <summary>무리 중 <paramref name="index"/>번째 개체의 스폰 x 오프셋(뒤로 물린 간격 + 흔들림).</summary>
        private float BurstOffsetX(int index)
        {
            return index * Mathf.Max(0f, spawnBurstSpacing)
                   + Random.Range(-spawnPositionJitter, spawnPositionJitter);
        }

        /// <summary>
        /// 전선에 몰린 몬스터가 한 마리처럼 보이지 않도록 개체별 미세 분산을 준다.
        /// <para>x는 정지 지점을 앞뒤로 밀고(<see cref="MonsterUnit.SetCrowdOffsetX"/>), y는 스폰 높이를 흔든다 —
        /// 이동 로직이 x만 건드리므로 y는 스폰 때 한 번 정하면 그대로 유지된다.</para>
        /// <para>겹칠 때 누가 앞인지 읽히도록 <b>아래쪽(더 앞) 개체를 위에 그린다</b> — 정렬값은 적 기본값
        /// 근처에서만 움직여(±1) 아군(20)보다 뒤라는 관계는 그대로 둔다.</para>
        /// <para>보스는 단독 등장이라 겹칠 일이 없고, 3배 크기라 흔들면 발밑 라인이 눈에 띄게 어긋나므로 제외한다.</para>
        /// </summary>
        private void ApplyCrowdSpread(GameObject go, MonsterUnit mu, bool isBoss)
        {
            if (isBoss || go == null || mu == null)
            {
                return;
            }

            mu.SetCrowdOffsetX(Random.Range(-crowdSpreadX, crowdSpreadX));

            float dy = Random.Range(-crowdSpreadY, crowdSpreadY);
            var p = go.transform.position;
            go.transform.position = new Vector3(p.x, p.y + dy, p.z);
            SetUnitSortingOrder(go, EnemySortingOrder + (dy < 0f ? 1 : -1));
        }

        /// <summary>주기적으로 적을 카메라 우측 바깥에 스폰(동시 상한 이내)하고, 살아있는 적을 파티 앞 라인으로 몰아넣는다.</summary>
        private void TickWave(float dt)
        {
            if (serverMode)
            {
                TickServerWave(dt);
                return;
            }

            if (monsterPrefab != null)
            {
                _spawnTimer += dt;
                int room = Mathf.Max(1, maxConcurrentEnemies) - AliveEnemyCount();
                if (_spawnTimer >= _nextSpawnDelay && room > 0)
                {
                    _spawnTimer = 0f;
                    _nextSpawnDelay = RollSpawnDelay();
                    int count = Mathf.Min(RollBurstSize(), room);
                    for (int i = 0; i < count; i++)
                    {
                        SpawnMonster(BurstOffsetX(i));
                    }
                }
            }
            UpdateQueue();
        }

        /// <summary>서버 웨이브(유한): 큐에서 코드별로 주기 스폰하고, 모두 스폰·처치되면 전멸 콜백을 1회 호출한다.</summary>
        private void TickServerWave(float dt)
        {
            if (_serverQueue != null && _serverQueue.Count > 0)
            {
                _spawnTimer += dt;
                int room = Mathf.Max(1, maxConcurrentEnemies) - AliveEnemyCount();
                if (_spawnTimer >= _nextSpawnDelay && room > 0)
                {
                    _spawnTimer = 0f;
                    _nextSpawnDelay = RollSpawnDelay();
                    int count = Mathf.Min(Mathf.Min(RollBurstSize(), room), _serverQueue.Count);
                    for (int i = 0; i < count; i++)
                    {
                        // 보스는 등장 경고·BGM 전환이 붙는 단독 연출이라 무리에 섞지 않는다.
                        // 무리 중간에 보스가 걸리면 거기서 끊고, 보스는 다음 스폰에서 혼자 나온다.
                        var next = _serverQueue.Peek();
                        if (_bossCode != 0 && next.Code == _bossCode && i > 0)
                        {
                            break;
                        }
                        _serverQueue.Dequeue();
                        SpawnMonsterByCode(next.Code, next.Level, BurstOffsetX(i));
                        _serverSpawned++;
                        if (_bossCode != 0 && next.Code == _bossCode)
                        {
                            break; // 보스를 냈으면 이번 무리는 여기까지
                        }
                    }
                }
            }

            UpdateQueue();

            // 전멸 판정: 예정분 모두 스폰 완료 + 살아있는 적 0 + 최소 1마리 스폰됨.
            bool allSpawned = _serverQueue == null || _serverQueue.Count == 0;
            if (!_serverCleared && _serverSpawned > 0 && allSpawned && AliveEnemyCount() == 0)
            {
                _serverCleared = true;
                Log("모든 몬스터 처치 — 클리어");
                _onAllCleared?.Invoke();
            }
        }

        /// <summary>지정 몬스터 코드의 프리팹(리졸버)과 마스터 스탯으로 적 1기를 스폰한다.
        /// <para><paramref name="level"/>은 <b>이 스테이지에서 이 몬스터가 등장하는 레벨</b>이며,
        /// <c>monster_master</c>의 값은 레벨 1 기준값이므로 <see cref="MonsterStats"/>로 배율을 곱해
        /// 실제 hp·attack을 만든다(마스터 데이터 값 §9.4 · 스테이지/전투 결과 기획서 5.1).
        /// 서버는 레벨만 내려주고 스탯은 내려주지 않는다 — 전투가 클라이언트 권위이기 때문이다.</para>
        /// <paramref name="offsetX"/>는 무리 스폰에서 개체를 뒤로 물리는 간격이다(0 = 종전 위치).</summary>
        private void SpawnMonsterByCode(int code, int level, float offsetX = 0f)
        {
            string mname = "Monster";
            long hp = 100;
            long atk = 5;
            var db = MasterDataManager.Db;
            if (db != null && db.Monsters.TryGetValue(code, out MonsterMaster mon))
            {
                mname = mon.name;
                MonsterStats.Scale(mon, level, out hp, out atk);
            }

            GameObject prefab = _prefabResolver != null ? _prefabResolver(code) : null;
            if (prefab == null) prefab = monsterPrefab; // 폴백
            if (prefab == null)
            {
                Log($"[경고] 몬스터 {code} 프리팹을 찾지 못해 스폰 건너뜀");
                return;
            }

            float rightEdge = _cam != null ? _cam.transform.position.x + _cam.orthographicSize * _cam.aspect : 10f;
            Vector3 pos = new Vector3(rightEdge + enemyOffscreenMargin + offsetX, _pathY, 0f);

            var go = _om.Spawn(CatEnemy, prefab, pos, Quaternion.identity);
            if (go == null) return;
            EnsureSpumAnimator(go);
            SetUnitSortingOrder(go, EnemySortingOrder); // 아군보다 뒤
            SetFacingRight(go, false);
            var mu = go.GetComponent<MonsterUnit>();
            if (mu == null) mu = go.AddComponent<MonsterUnit>();

            bool isBoss = _bossCode != 0 && code == _bossCode;
            float speed = isBoss ? enemyMoveSpeed * Mathf.Max(0.05f, bossMoveSpeedFactor) : enemyMoveSpeed;
            mu.Init(mname, hp, atk, speed, () => _paused, OnMonsterKilled, isBoss, isBoss ? bossIcon : null,
                    OnMonsterAttack, enemyAttackInterval, OnBossPhase);
            ApplyCrowdSpread(go, mu, isBoss); // 전선에서 겹쳐도 여러 마리로 보이게(Init 뒤 — Init이 오프셋을 리셋한다)
            AttachWalkDust(go, () => mu != null && mu.IsMoving); // 걷기 먼지(전진 애니 재생 중에만 노출)
            if (isBoss)
            {
                BossWarningBanner.Show(bossWarningImage); // 보스 등장 경고 연출(중앙 경고 이미지 3회 펄스)
                SoundManager.Sfx(SoundId.BossWarning);
                SoundManager.Bgm(SoundId.BgmBoss, 2f); // 보스전 BGM으로 2초 크로스페이드
                Log($"보스 등장! — {mname}");
            }
        }

        /// <summary>서버 스테이지 진입 데이터로 유한 웨이브 전투를 시작한다.
        /// <para>plan: 등장 순서대로의 <see cref="Spawn"/>(몬스터코드·<b>등장 레벨</b>·마리 수) 목록,
        /// prefabResolver: 코드→프리팹, onAllCleared: 전멸 시 1회,
        /// bossCode: 보스 몬스터 코드(0=없음). 해당 코드 스폰 시 3배 크기·감속·왕관·경고 연출을 적용한다.</para>
        /// <para>같은 몬스터라도 <b>스테이지마다 등장 레벨이 다를 수 있으므로</b> 레벨을 마리 단위로 큐에 실어 두고,
        /// 스폰 시점에 <see cref="MonsterStats"/>로 레벨 1 기준값에 배율을 곱한다.</para></summary>
        public void BeginServerBattle(List<Spawn> plan,
                                      System.Func<int, GameObject> prefabResolver, System.Action onAllCleared,
                                      int bossCode = 0, System.Action onDefeat = null)
        {
            serverMode = true;
            // 이전 스테이지에서 전사해 사라진 파티원을 새 전투 시작 시 다시 세운다.
            // (전사자는 OnAllyKilled에서 목록에서 빠지고 오브젝트도 파괴되므로, 이 복구가 없으면
            //  전멸로 리셋되기 전까지 다음 스테이지들에 계속 나타나지 않는다.)
            RestoreFallenMembers();
            HealPartyFull(); // 스테이지 시작 시 파티 전원 체력 회복(부활한 멤버와 생존 멤버의 체력을 같은 기준으로 맞춘다)
            _prefabResolver = prefabResolver;
            _onAllCleared = onAllCleared;
            _onDefeat = onDefeat;
            _defeated = false;
            _bossCode = bossCode;
            _serverQueue = new Queue<QueuedMonster>();
            if (plan != null)
            {
                foreach (var sp in plan)
                {
                    for (int i = 0; i < sp.count; i++)
                    {
                        _serverQueue.Enqueue(new QueuedMonster(sp.monsterCode, sp.monsterLevel));
                    }
                }
            }
            _serverSpawned = 0;
            _serverTotal = _serverQueue.Count;
            _serverKilled = 0;
            _serverCleared = false;
            _spawnTimer = enemySpawnInterval; // 곧 첫 스폰
            _nextSpawnDelay = 0f;
            Log($"서버 전투 시작 — 총 {_serverQueue.Count}마리 예정");
        }

        /// <summary>카메라 우측 바깥(보이지 않는 지점)에 적 1기를 생성·배선한다.
        /// <paramref name="offsetX"/>는 무리 스폰에서 개체를 뒤로 물리는 간격이다(0 = 종전 위치).</summary>
        private void SpawnMonster(float offsetX = 0f)
        {
            float rightEdge = _cam != null ? _cam.transform.position.x + _cam.orthographicSize * _cam.aspect : 10f;
            Vector3 pos = new Vector3(rightEdge + enemyOffscreenMargin + offsetX, _pathY, 0f);

            var go = _om.Spawn(CatEnemy, monsterPrefab, pos, Quaternion.identity);
            if (go == null) return;
            EnsureSpumAnimator(go);
            SetUnitSortingOrder(go, EnemySortingOrder); // 아군보다 뒤
            SetFacingRight(go, false); // 파티(왼쪽)를 바라봄
            var mu = go.GetComponent<MonsterUnit>();
            if (mu == null) mu = go.AddComponent<MonsterUnit>();
            mu.Init(_monsterName, _monsterMaxHp, _monsterAtk, enemyMoveSpeed,
                    () => _paused, OnMonsterKilled, false, null,
                    OnMonsterAttack, enemyAttackInterval, OnBossPhase);
            ApplyCrowdSpread(go, mu, false); // 전선에서 겹쳐도 여러 마리로 보이게(Init 뒤 — Init이 오프셋을 리셋한다)
            AttachWalkDust(go, () => mu != null && mu.IsMoving); // 걷기 먼지(전진 애니 재생 중에만 노출)
        }

        /// <summary>살아있는 적을 모두 같은 파티 앞 라인으로 보낸다(아군과 달리 서로 완전히 겹쳐도 무방).</summary>
        private void UpdateQueue()
        {
            if (_om == null) return;
            float line = FrontX() + enemyFrontStopGap;
            foreach (var go in _om.Active(CatEnemy))
            {
                var mu = go.GetComponent<MonsterUnit>();
                if (mu != null && mu.Alive) mu.SetTargetX(line);
            }
        }

        /// <summary>파티에 가장 가까운(x 최소) 살아있는 적. 없으면 null.</summary>
        private MonsterUnit FrontMonsterUnit()
        {
            if (_om == null) return null;
            MonsterUnit best = null;
            float bx = float.MaxValue;
            foreach (var go in _om.Active(CatEnemy))
            {
                var mu = go.GetComponent<MonsterUnit>();
                if (mu == null || !mu.Alive) continue;
                float x = go.transform.position.x;
                if (x < bx) { bx = x; best = mu; }
            }
            return best;
        }

        /// <summary>살아있는 적 수.</summary>
        private int AliveEnemyCount()
        {
            if (_om == null) return 0;
            int n = 0;
            foreach (var go in _om.Active(CatEnemy))
            {
                var mu = go.GetComponent<MonsterUnit>();
                if (mu != null && mu.Alive) n++;
            }
            return n;
        }

        /// <summary>SPUM 애니메이터(Assembly-CSharp)를 없으면 부착한다(asmdef 직접 참조 불가 → 타입명으로).</summary>
        private static void EnsureSpumAnimator(GameObject go)
        {
            var t = System.Type.GetType("SpumCharacterAnimator, Assembly-CSharp");
            if (t != null && go.GetComponent(t) == null) go.AddComponent(t);
        }

        /// <summary>
        /// 유닛의 진영별 정렬 순서를 지정한다(<see cref="AllySortingOrder"/> / <see cref="EnemySortingOrder"/>).
        /// SPUM 캐릭터는 파트(머리·몸·무기…)가 여러 스프라이트로 나뉘고 루트의 <c>SortingGroup</c>이 그 묶음을
        /// 한 덩어리로 정렬하므로, <b>그룹 하나의 순서만 바꾸면 캐릭터 전체가 함께 앞뒤로 이동</b>한다.
        /// 프리팹의 구운 값(둘 다 5)을 스폰 시점에 덮어쓰는 방식이라 프리팹 12종을 고칠 필요가 없다.
        /// </summary>
        private static void SetUnitSortingOrder(GameObject go, int order)
        {
            var sg = go.GetComponentInChildren<UnityEngine.Rendering.SortingGroup>(true);
            if (sg != null)
            {
                sg.sortingOrder = order;
            }
        }

        /// <summary>SPUM 기본 스프라이트가 왼쪽을 보므로 오른쪽=localScale.x 음수.</summary>
        private static void SetFacingRight(GameObject go, bool faceRight)
        {
            Vector3 s = go.transform.localScale;
            float mag = Mathf.Abs(s.x);
            if (mag < 0.0001f) mag = 1f;
            s.x = faceRight ? -mag : mag;
            go.transform.localScale = s;
        }

        /// <summary>공격 사거리로 멤버를 행(row)에 묶는다: 비슷한 사거리=같은 행(같은 X), 같은 행 내는 Y로 분리.</summary>
        private void ComputeFormation()
        {
            int n = _members.Count;
            _rowIndex = new int[n];
            _formY = new float[n];
            if (n == 0) { _engageRange = 1.5f; return; }

            var keys = new int[n];
            float minRange = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                float r = _members[i] != null ? _members[i].AttackRange : 1.5f;
                keys[i] = Mathf.FloorToInt(r / Mathf.Max(0.01f, rangeGroupSize));
                if (r < minRange) minRange = r;
            }

            var distinct = new List<int>();
            foreach (var k in keys) if (!distinct.Contains(k)) distinct.Add(k);
            distinct.Sort();

            var rowTotal = new Dictionary<int, int>();
            for (int i = 0; i < n; i++)
            {
                int ri = distinct.IndexOf(keys[i]);
                rowTotal[ri] = rowTotal.TryGetValue(ri, out var c) ? c + 1 : 1;
            }
            var rowSlot = new Dictionary<int, int>();
            for (int i = 0; i < n; i++)
            {
                int ri = distinct.IndexOf(keys[i]);
                _rowIndex[i] = ri;
                int slot = rowSlot.TryGetValue(ri, out var s) ? s : 0;
                rowSlot[ri] = slot + 1;
                int total = rowTotal[ri];
                _formY[i] = (slot - (total - 1) * 0.5f) * rowYSpacing;
            }

            _engageRange = minRange;
        }

        /// <summary>파티 최전방 x(카메라·큐 라인 기준).</summary>
        private float FrontX()
        {
            float f = _partyX;
            foreach (var m in _members)
                if (m != null && m.transform.position.x > f) f = m.transform.position.x;
            return f;
        }

        private void EnterFighting(MonsterUnit front)
        {
            if (_phase == Phase.Fighting) return;
            _phase = Phase.Fighting;
            // 선두 행 라인을 최전방 몬스터의 사거리 지점에 고정(사격선 정렬)
            if (front != null) _partyX = front.transform.position.x - _engageRange;
            Log($"교전 시작 — {_monsterName} 웨이브와 마주침");
        }

        /// <summary>멤버(돌진 등)가 몬스터에 도달했음을 알릴 때 교전으로 전환.</summary>
        public void RequestFighting()
        {
            var front = FrontMonsterUnit();
            if (front != null) EnterFighting(front);
        }

        /// <summary>현재 선택(<see cref="_selected"/>)대로 파티/몬스터를 초기화하고 전투를 재시작한다(테스트용 선택 소환).</summary>
        private void RestartWithSelection()
        {
            ResetBattlefield();
            Log($"재시작 — 선택 소환 {_members.Count}인");
        }

        /// <summary>전투 필드를 처음 상태로 되돌린다: 아군/적을 모두 정리하고 진행 중 코루틴을 취소한 뒤,
        /// 킬 수·페이즈·파티 위치를 스폰 지점으로 리셋하고 파티를 재스폰한다(스킬/초상화 UI도 재구성).</summary>
        private void ResetBattlefield()
        {
            // 기존 아군 제거(ObjectManager 카테고리째 정리)
            if (_om != null) _om.Clear(CatAlly);
            _members.Clear();

            // 진행 중 지연 데미지 코루틴 취소 + 몬스터 전부 정리.
            // 히트스톱 코루틴도 함께 죽으므로 시간 배율을 먼저 원복한다(안 하면 0.05배로 멈춘 채 재시작된다).
            CancelTimeEffects();
            StopAllCoroutines();
            if (_om != null) _om.Clear(CatEnemy);
            _spawnTimer = enemySpawnInterval;
            _nextSpawnDelay = 0f;

            _killCount = 0;
            _phase = Phase.Advancing;
            _defeated = false;
            _paused = false;

            Vector3 start = playerSpawn != null ? playerSpawn.position : new Vector3(-4.5f, -1.6f, 0f);
            _pathY = start.y;
            _partyX = start.x;

            // 카메라를 시작 지점으로 즉시 스냅(다음 프레임 팔로우 전). 이 직후 배경을 다시 구축하면
            // 스크롤 배경 타일이 올바른 위치(시작 지점)에 생성된다(수동 재입장 시 배경 사라짐 방지).
            if (_cam != null)
            {
                Vector3 cc = _cam.transform.position;
                cc.x = start.x;
                _cam.transform.position = cc;
                if (_baseOrtho > 0f) _cam.orthographicSize = _baseOrtho;
            }

            SpawnParty(start);

            // 스킬/초상화 UI를 새 파티로 재구성
            var ui = FindAnyObjectByType<SkillCooldownUI>();
            if (ui != null) ui.Rebuild();
        }

        /// <summary>전투 필드를 처음 상태로 초기화한 뒤 새 플랜으로 서버 전투를 처음부터 다시 시작한다.
        /// 스테이지 UI에서 특정 스테이지를 선택해 "처음부터" 입장할 때 사용한다(진행 중인 전투를 리셋).</summary>
        public void RestartServerBattle(List<Spawn> plan,
                                        System.Func<int, GameObject> prefabResolver, System.Action onAllCleared,
                                        int bossCode = 0, System.Action onDefeat = null)
        {
            ResetBattlefield();
            BeginServerBattle(plan, prefabResolver, onAllCleared, bossCode, onDefeat);
        }

        // ---- 파티 멤버(PlayerCombatant)가 사용하는 공유 훅 ----

        /// <summary>파티에 가장 가까운(최전방) 살아있는 몬스터.</summary>
        public MonsterUnit FrontMonster => FrontMonsterUnit();
        public Transform MonsterTransform { get { var m = FrontMonster; return m != null ? m.transform : null; } }
        public bool MonsterAlive => FrontMonster != null;
        public bool IsFighting => _phase == Phase.Fighting && MonsterAlive;
        public float PathY => _pathY;
        /// <summary>
        /// 실제 전투에 적용할 데미지 배수. <b>실게임(<see cref="serverMode"/>)에서는 항상 1</b>이고,
        /// 개발 하네스(BattleDevScene)에서만 <see cref="devDamageMultiplier"/>를 쓴다.
        /// <para>GameScene 전투는 BattleDevScene을 복제해 만들기 때문에 하네스용 20배가 실게임 씬까지 따라와,
        /// 아군 데미지가 마스터 데이터의 20배로 나가고 있었다. 기획서(master-data-기획서 §7.4)의 데미지 공식은
        /// <c>공격력 × 스킬 계수</c>(치명 시 × 치명피해)이며 개발 배수는 없다 — 실게임은 그 공식을 그대로 따른다.</para>
        /// </summary>
        public float DevDamageMultiplier => serverMode ? 1f : Mathf.Max(1f, devDamageMultiplier);
        public float BasicHitDelay => basicAttackHitDelay;
        public float EffectYOffset => effectYOffset;
        public int DevSkillLevel => devSkillLevel;
        public float SkillCooldownFallback => skillCooldown;
        public bool IsPaused => _paused;

        /// <summary>공격 모션/이펙트가 끝난 뒤 최전방 몬스터에 데미지를 적용한다(호출 시점의 대상을 캡처, 생존 시에만 적용).
        /// <paramref name="crit"/>는 시전 측이 굴린 치명타 판정 결과로, 숫자 연출·로그에만 쓴다(데미지에는 이미 반영돼 있다).
        /// <paramref name="bigHit"/>는 <b>스킬 타격</b>이라는 표시로, 히트스톱·셰이크를 걸지 판단하는 데만 쓴다(평타는 false).
        /// <paramref name="knockback"/>은 대상이 밀려날 거리로, <b>0이면 밀지 않는다</b>(기본공격이 이 경우다).</summary>
        public void DealDamageAfter(float delay, long dmg, bool crit, string label, bool bigHit = false,
                                    float knockback = 0f)
        {
            var target = FrontMonster;
            StartCoroutine(DoDamageAfter(delay, dmg, crit, label, target, bigHit, knockback));
        }

        private IEnumerator DoDamageAfter(float delay, long dmg, bool crit, string label, MonsterUnit target,
                                          bool bigHit, float knockback)
        {
            if (delay > 0f)
            {
                yield return new WaitForSeconds(delay);
            }
            if (target == null || !target.Alive)
            {
                yield break;
            }
            // 피격음은 대상 계열에 따라 살점/금속으로 갈린다(사운드 정의서 §5.1·§8).
            SoundManager.Sfx(BattleSounds.MonsterHitFor(target.MonsterName));
            bool isBoss = target.IsBoss;
            bool killed = target.TakeDamage(dmg);
            // 맞는 반응(붉은 틴트·넉백). 죽은 대상은 사망 모션·사망음이 피드백을 맡는다.
            if (!killed)
            {
                target.PlayHitReaction(heavy: true, knockback: knockback);
            }
            // 히트스톱은 큰 타격(치명타·스킬·보스 피격)에 건다.
            if (crit || bigHit || isBoss)
            {
                RequestHitStop(isBoss ? BossHitStopSeconds : HitStopSeconds);
            }
            // 카메라 셰이크는 <b>스킬 타격</b>과 <b>처치</b>에만 건다. 치명타·보스 피격까지 넣었더니 기본공격에도
            // 치명타가 자주 떠서(그리고 보스전에서는 평타마다) 화면이 거의 상시 흔들렸다(2026-08-04 축소).
            // 처치는 평타로 쓰러뜨려도 흔든다 — 자주 일어나지 않는 '성과'라 상시 흔들림이 되지 않는다.
            if (bigHit || killed)
            {
                RequestShake(isBoss ? ShakeHeavy : ShakeLight);
            }
            // 피격 데미지를 숫자로 표시(치명타는 노란색). 크기는 대상 최대 체력 대비 피해 비중으로 정한다.
            DamageNumberPool.GetOrCreate().Spawn(dmg, target.transform.position + Vector3.up * (effectYOffset + 0.5f),
                crit, DamageSizeMul(dmg, target.MaxHp));
            Log($"{label} → -{dmg}{(crit ? " (치명타)" : string.Empty)} (HP {Mathf.Max(0, (int)target.Hp)}/{target.MaxHp})");
        }

        /// <summary>지연 후 지정 중심 반경 내 모든 살아있는 적에게 데미지를 적용한다(광역 스킬).
        /// <paramref name="bigHit"/>는 스킬 타격 표시(히트스톱 판단용) — 광역 <b>평타</b>도 있으므로 여기서도 구분한다.
        /// <paramref name="knockback"/>은 밀려날 거리(0이면 밀지 않는다).
        /// <paramref name="knockbackAll"/>가 false면 <b>대표(첫) 대상만</b> 밀린다(광역 한 방에 화면이 찢어지는 것을 막는다).
        /// true면 맞은 적 전부가 밀린다 — <b>밀치는 것 자체가 스킬인</b> 기사 방패 돌진이 이 경우다.</summary>
        public void DealAreaDamageAfter(float delay, long dmg, bool crit, string label, Vector3 center, float radius,
                                        bool bigHit = false, float knockback = 0f, bool knockbackAll = false)
        {
            StartCoroutine(DoAreaDamageAfter(delay, dmg, crit, label, center, radius, bigHit, knockback, knockbackAll));
        }

        private IEnumerator DoAreaDamageAfter(float delay, long dmg, bool crit, string label, Vector3 center, float radius,
                                              bool bigHit, float knockback, bool knockbackAll)
        {
            if (delay > 0f)
            {
                yield return new WaitForSeconds(delay);
            }
            if (_om == null)
            {
                yield break;
            }

            // 순회 중 사망/파괴에 대비해 대상 스냅샷을 먼저 모은다.
            var targets = new List<MonsterUnit>();
            foreach (var go in _om.Active(CatEnemy))
            {
                var mu = go.GetComponent<MonsterUnit>();
                if (mu != null && mu.Alive)
                {
                    targets.Add(mu);
                }
            }

            int hit = 0;
            int killedCount = 0; // 처치 셰이크 판단용(광역 한 방에 여러 마리가 함께 죽는다)
            bool killedBoss = false;
            bool hitBoss = false;
            float r2 = radius * radius;
            foreach (var mu in targets)
            {
                if (mu == null || !mu.Alive) continue;
                if (((Vector2)mu.transform.position - (Vector2)center).sqrMagnitude <= r2)
                {
                    hitBoss |= mu.IsBoss;
                    // 광역은 대상 수만큼 루프를 돌지만 피격음은 **첫 대상 한 번만** 울린다 —
                    // 대상마다 재생하면 소리가 찢어진다(사운드 정의서 §9.3).
                    if (hit == 0)
                    {
                        SoundManager.Sfx(BattleSounds.MonsterHitFor(mu.MonsterName));
                    }
                    bool killed = mu.TakeDamage(dmg);
                    if (killed)
                    {
                        killedCount++;
                        killedBoss |= mu.IsBoss;
                    }
                    // 피격음과 같은 기준으로 나눈다 — 대표(첫) 대상만 모션·넉백까지, 나머지는 틴트만.
                    // 전원에게 모션·넉백을 주면 광역 한 방에 화면이 찢어진다(사운드 정의서 §9.3과 같은 이유).
                    // 단 knockbackAll(방패 돌진)은 예외다 — 부딪힌 적이 다 같이 날아가는 것이 곧 그 스킬의 그림이다.
                    if (!killed)
                    {
                        bool heavy = knockbackAll || hit == 0;
                        mu.PlayHitReaction(heavy: heavy, knockback: heavy ? knockback : 0f);
                    }
                    // 광역은 대상마다 숫자가 동시에 떠 한 덩어리로 보이므로 대상 순서대로 시차를 준다.
                    DamageNumberPool.GetOrCreate().Spawn(dmg, mu.transform.position + Vector3.up * (effectYOffset + 0.5f),
                        crit, DamageSizeMul(dmg, mu.MaxHp), hit * AreaNumberStagger);
                    hit++;
                }
            }
            // 히트스톱·셰이크는 광역 한 방에 <b>한 번만</b> 건다(대상마다 걸면 시간·화면이 계단처럼 끊긴다).
            if (hit > 0 && (crit || bigHit || hitBoss))
            {
                RequestHitStop(hitBoss ? BossHitStopSeconds : HitStopSeconds);
            }
            // 셰이크는 스킬 타격과 <b>처치</b>에 건다(광역 <b>평타</b>도 있으므로 타격은 bigHit로 가른다).
            // 보스이거나 여러 대상을 한 번에 쓸었으면 강하게 준다.
            if (hit > 0 && (bigHit || killedCount > 0))
            {
                bool heavy = killedBoss || hitBoss || killedCount >= 3 || (bigHit && hit >= 3);
                RequestShake(heavy ? ShakeHeavy : ShakeLight);
            }
            Log($"{label} (광역 r{radius:0.#}) → {hit}체 -{dmg}{(crit ? " (치명타)" : string.Empty)}");
        }

        // 광역에서 대상별로 숫자를 띄우는 간격(초) — 한 점에 겹쳐 한 덩어리로 보이는 것을 막는다.
        private const float AreaNumberStagger = 0.05f;

        /// <summary>
        /// <b>이펙트 그림이 실제로 덮은 자리</b>에 들어온 적을 닿는 순간 1회씩 때린다(휩쓸기 판정).
        /// <para>앞으로 <b>뻗어 나가는</b> 이펙트(마법사 파이어볼·프로스트 노바·라이트닝 볼트처럼 시전자에서
        /// 적 쪽으로 날아가며 자라는 스프라이트 시퀀스)는 <b>한 점 중심의 원 판정</b>으로는 표현되지 않는다 —
        /// 시전 순간의 그림은 시전자 손끝의 작은 불씨뿐이고, 적을 덮는 것은 그 뒤 프레임들이기 때문이다.
        /// 시전 시점의 렌더 크기를 반경으로 쓰면 반경이 0.2 남짓이라 <b>아무도 맞지 않는다</b>(파이어볼의 증상).</para>
        /// <para>그래서 여기서는 이펙트 인스턴스가 살아 있는 동안 <b>매 프레임 렌더 바운즈를 다시 읽어</b>
        /// 그 사각형 안에 들어온 적을 피격시킨다. 적은 발밑 좌표를 쓰므로 세로는 몸 중심(<see cref="effectYOffset"/>)으로
        /// 올려 비교하고, 얇은 프레임에서 빠지지 않도록 <paramref name="minHalfHeight"/>를 세로 여유로 둔다.</para>
        /// <para><paramref name="freezeSeconds"/>가 0보다 크면 닿은 적을 그만큼 빙결시킨다(프로스트 노바) —
        /// 데미지와 같은 판정을 쓰므로 "맞은 적만 언다".</para>
        /// </summary>
        /// <summary>휩쓸기 판정이 유지되는 최대 시간(초). 이펙트가 스스로 사라지지 않는 경우의 안전 상한이다.</summary>
        private const float SweepMaxSeconds = 5f;

        public void DealSweepDamage(GameObject fx, long dmg, bool crit, string label, float minHalfHeight,
                                    bool bigHit = false, float knockback = 0f, float freezeSeconds = 0f)
        {
            if (fx == null || _om == null)
            {
                return;
            }
            StartCoroutine(DoSweepDamage(fx, dmg, crit, label, minHalfHeight, bigHit, knockback, freezeSeconds));
        }

        private IEnumerator DoSweepDamage(GameObject fx, long dmg, bool crit, string label, float minHalfHeight,
                                          bool bigHit, float knockback, float freezeSeconds)
        {
            var rends = fx.GetComponentsInChildren<Renderer>();
            if (rends == null || rends.Length == 0)
            {
                yield break; // 판정할 그림이 없다(렌더러 없는 이펙트)
            }

            var alreadyHit = new HashSet<MonsterUnit>();
            var targets = new List<MonsterUnit>(); // 프레임마다 재사용(순회 중 사망·풀 반환에 대비한 스냅샷)
            int hitTotal = 0;
            int killedTotal = 0;
            bool impactShown = false; // 히트스톱·셰이크는 첫 접촉에 한 번만(휩쓸기는 여러 프레임에 걸쳐 맞는다)
            float elapsed = 0f;

            // 이펙트가 스스로 사라질 때까지 따라간다(SpriteSequenceEffect의 destroyOnFinish).
            // 반복 재생 이펙트가 잘못 물려 판정이 영원히 남지 않도록 상한을 둔다.
            while (fx != null && elapsed < SweepMaxSeconds)
            {
                if (_paused)
                {
                    yield return null;
                    continue;
                }
                elapsed += Time.deltaTime;

                // 이펙트 바운즈는 프레임이 넘어갈수록 앞으로 자란다 — 매 프레임 다시 읽어야 "닿은 만큼" 맞는다.
                bool hasBounds = false;
                Bounds b = default;
                foreach (var r in rends)
                {
                    if (r == null || !r.enabled)
                    {
                        continue;
                    }
                    if (!hasBounds) { b = r.bounds; hasBounds = true; }
                    else { b.Encapsulate(r.bounds); }
                }
                if (!hasBounds)
                {
                    yield return null;
                    continue;
                }

                float halfW = b.extents.x;
                float halfH = Mathf.Max(b.extents.y, minHalfHeight);
                int batch = 0;
                int batchKilled = 0;
                bool batchKilledBoss = false, batchHitBoss = false;

                targets.Clear();
                foreach (var go in _om.Active(CatEnemy))
                {
                    if (go == null) continue;
                    var mu = go.GetComponent<MonsterUnit>();
                    if (mu != null && mu.Alive && !alreadyHit.Contains(mu))
                    {
                        targets.Add(mu);
                    }
                }

                foreach (var mu in targets)
                {
                    if (mu == null || !mu.Alive) continue;

                    // 적 좌표는 발밑이므로 몸 중심으로 올려 비교한다(이펙트는 몸 높이에서 날아간다).
                    Vector3 body = mu.transform.position + Vector3.up * effectYOffset;
                    if (Mathf.Abs(body.x - b.center.x) > halfW || Mathf.Abs(body.y - b.center.y) > halfH)
                    {
                        continue;
                    }

                    alreadyHit.Add(mu);
                    batchHitBoss |= mu.IsBoss;
                    if (batch == 0)
                    {
                        // 피격음은 프레임당 한 번만(대상마다 재생하면 소리가 찢어진다 — 사운드 정의서 §9.3).
                        SoundManager.Sfx(BattleSounds.MonsterHitFor(mu.MonsterName));
                    }
                    if (freezeSeconds > 0f)
                    {
                        mu.ApplyFreeze(freezeSeconds);
                    }
                    bool killed = mu.TakeDamage(dmg);
                    if (killed)
                    {
                        killedTotal++;
                        batchKilled++;
                        batchKilledBoss |= mu.IsBoss;
                    }
                    else
                    {
                        // 넉백·피격 모션은 대표(첫) 대상만 — 전원을 밀면 한 번의 스킬에 화면이 찢어진다.
                        mu.PlayHitReaction(heavy: batch == 0, knockback: batch == 0 ? knockback : 0f);
                    }
                    DamageNumberPool.GetOrCreate().Spawn(dmg, mu.transform.position + Vector3.up * (effectYOffset + 0.5f),
                        crit, DamageSizeMul(dmg, mu.MaxHp), batch * AreaNumberStagger);
                    batch++;
                    hitTotal++;
                }

                // 히트스톱은 <b>첫 접촉에 한 번만</b> — 관통 중 매 접촉마다 걸면 시간이 계단처럼 끊긴다.
                if (batch > 0 && !impactShown)
                {
                    impactShown = true;
                    if (crit || bigHit || batchHitBoss)
                    {
                        RequestHitStop(batchHitBoss ? BossHitStopSeconds : HitStopSeconds);
                    }
                    RequestShake(batchKilledBoss || batchHitBoss || batch >= 3 ? ShakeHeavy : ShakeLight);
                }
                else if (batchKilled > 0)
                {
                    RequestShake(batchKilledBoss ? ShakeHeavy : ShakeLight); // 휩쓸며 추가로 쓰러뜨린 반응
                }

                yield return null;
            }

            Log($"{label} (관통 광역) → {hitTotal}체 -{dmg}{(crit ? " (치명타)" : string.Empty)}");
        }

        /// <summary>
        /// 데미지 숫자 크기 배수. <b>절대 데미지 값이 아니라 대상 최대 체력 대비 비중</b>으로 정한다 —
        /// 절대값은 성장에 따라 계속 커져(3자리 → 6자리) 어느 값이 "큰 타격"인지 기준이 사라진다.
        /// 체력의 절반을 날린 한 방은 어느 구간에서든 큰 타격이다.
        /// </summary>
        private static float DamageSizeMul(long dmg, long maxHp)
        {
            float weight = maxHp > 0 ? Mathf.Clamp01((float)dmg / maxHp) : 0.5f;
            return Mathf.Lerp(0.9f, 1.35f, weight);
        }

        // 데미지 없이 밀기만 하던 ShoveEnemiesAhead는 제거했다 — 방패 돌진이 광역 타격으로 바뀌면서
        // 부딪힌 적이 데미지와 넉백을 함께 받게 되었고(DealAreaDamageAfter의 knockbackAll), 밀기 전용 경로가
        // 필요 없어졌다. 데미지 없는 밀기가 다시 필요해지면 MonsterUnit.ApplyPush로 되살릴 수 있다.

        /// <summary>MonsterUnit이 죽는 순간 주입된 콜백으로 호출된다 — 누적 킬/로그 갱신 + 웨이브 종료 슬로우.</summary>
        public void OnMonsterKilled(MonsterUnit m)
        {
            _killCount++;
            if (serverMode)
            {
                _serverKilled++; // 스테이지 진행도(처치/전체) 분자
            }
            Log($"{(m != null ? m.MonsterName : _monsterName)} 처치! (누적 {_killCount})");

            // 웨이브의 <b>마지막 한 마리</b>를 잡는 순간만 짧게 슬로우 + 강한 셰이크 — 한 웨이브를 정리했다는
            // 마무리 박자를 준다. <b>슬로우</b>는 전투를 실제로 멈추므로 매 처치에 걸지 않는다(방치형 처치
            // 빈도상 전투가 계속 끊긴다). 개별 처치의 약한 셰이크는 데미지 관문(`DoDamageAfter` 등)이 건다.
            if (AliveEnemyCount() == 0)
            {
                RequestSlowMotion(WaveClearSlowScale, WaveClearSlowSeconds);
                RequestShake(ShakeHeavy);
            }
        }

        /// <summary>
        /// 보스가 체력 70%·30%를 지날 때 <see cref="MonsterUnit"/>이 호출한다 — 붉은 비네트 플래시 + 강한 셰이크.
        /// 포효·공격 주기 단축은 몬스터 쪽에서 처리하고, 여기서는 <b>화면 연출</b>만 담당한다.
        /// </summary>
        public void OnBossPhase(MonsterUnit boss, int phase)
        {
            FlashVignette(BossPhaseVignetteColor, BossPhaseVignetteSeconds);
            RequestShake(ShakeHeavy);
            Log($"{(boss != null ? boss.MonsterName : "보스")} 페이즈 {phase} 진입 — 공격이 빨라진다!");
        }

        /// <summary>몬스터가 공격 주기마다 호출한다. 최전방 생존 아군에게 몬스터 공격력×배수를 방어력으로 경감해 준다.
        /// 대상이 있으면 true(공격 애니 재생), 없으면 false.</summary>
        public bool OnMonsterAttack(MonsterUnit m)
        {
            if (m == null || !m.Alive || _defeated) return false;
            var target = FrontAlly();
            if (target == null) return false;

            long raw = (long)(m.Atk * Mathf.Max(1f, enemyDamageMultiplier));
            long dmg = MitigatedDamage(raw, target.Defense);
            target.TakeDamage(dmg);
            // 아군 피격 데미지를 붉은 숫자로 표시(오브젝트 풀 재사용).
            DamageNumberPool.GetOrCreate().Spawn(dmg, target.transform.position + Vector3.up * (effectYOffset + 0.5f));
            return true;
        }

        /// <summary>
        /// 방어력으로 피해를 경감한다 — <c>raw × K / (K + def)</c>(K = <see cref="DefenseMitigationK"/>).
        /// <para><b>감산식(<c>raw − def</c>)을 쓰지 않는 이유</b>: 방어력은 레벨당 선형으로 계속 오르는데
        /// 저레벨 구간 몬스터의 공격력은 고정이라, 방어력이 공격력을 넘는 순간 피해가 최소값 1로 바닥친다
        /// (1지역이 영구히 1뎀이 되어 사실상 무적). 반대로 고레벨 구간에서는 방어력이 무의미해져 즉사한다.
        /// 비율식은 수확체감으로 0에 수렴하지 않으므로 두 극단이 모두 사라진다 —
        /// 방어력 K면 절반, 3K면 1/4로 경감된다.</para>
        /// </summary>
        public static long MitigatedDamage(long raw, long defense)
        {
            if (raw <= 0)
            {
                return 0;
            }
            long def = System.Math.Max(0L, defense);
            // 정수 나눗셈 전에 double로 계산해 작은 피해가 0으로 잘리지 않게 한다(하한은 1).
            double mitigated = raw * (DefenseMitigationK / (double)(DefenseMitigationK + def));
            return System.Math.Max(1L, (long)System.Math.Round(mitigated));
        }

        /// <summary>파티에서 가장 앞선(x 최대) 생존 아군. 없으면 null(전멸).</summary>
        private PlayerCombatant FrontAlly()
        {
            PlayerCombatant best = null;
            float bx = float.NegativeInfinity;
            foreach (var m in _members)
            {
                if (m == null || !m.Alive) continue;
                if (m.transform.position.x > bx) { bx = m.transform.position.x; best = m; }
            }
            return best;
        }

        /// <summary>아군 1인이 사망하면 호출된다 — 파티 목록에서 제거·대형 재계산·전투 UI 갱신하고,
        /// 전원 사망 시 패배 처리를 1회 발동한다.</summary>
        public void OnAllyKilled(PlayerCombatant a)
        {
            _members.Remove(a);
            Log($"{(a != null ? a.DisplayName : "아군")} 전사 — 남은 파티 {_members.Count}인");

            // 전투 UI는 다시 만들지 않고 <b>그 줄만 죽은 표시</b>로 바꾼다 — 재구성하면 전사자 초상화가
            // 통째로 사라져 누가 죽었는지 알 수 없고, 남은 멤버의 줄 위치까지 밀린다.
            // 초상화는 캐릭터를 실시간 렌더하므로 <b>파괴되기 전인 지금</b> 흑백 스냅숏으로 굳혀야 한다.
            var skillUi = FindAnyObjectByType<SkillCooldownUI>();
            if (skillUi != null) skillUi.MarkMemberDead(a);

            if (_members.Count > 0)
            {
                // 남은 멤버로 대형·선두 속도 재계산.
                _partySpeed = Mathf.Max(0.1f, _members[0].MoveSpeed);
                ComputeFormation();
            }
            else
            {
                TriggerDefeat();
            }
        }

        /// <summary>아군 전멸: 패배 처리를 1회 발동한다. 서버 구동 모드는 주입된 패배 콜백(연출+재시작)을 호출하고,
        /// 개발 하네스는 패배 오버레이 후 전장을 리셋한다.</summary>
        private void TriggerDefeat()
        {
            if (_defeated) return;
            _defeated = true;
            Log("아군 전멸 — 패배");

            if (_onDefeat != null)
            {
                _onDefeat();
            }
            else
            {
                // 개발 하네스: 서버 흐름이 없으므로 패배 오버레이 후 전장 리셋.
                SoundManager.Jingle(SoundId.JingleDefeat);
                BattleDefeatOverlay.Show(ResetBattlefield);
            }
        }

        private void CycleMonster()
        {
            var db = MasterDataManager.Db;
            if (db == null || db.Monsters.Count == 0) return;
            var keys = new List<int>(db.Monsters.Keys);
            keys.Sort();
            int idx = keys.IndexOf(monsterCode);
            monsterCode = keys[(idx + 1) % keys.Count];
            LoadMonsterStats(); // 이후 SpawnMonster가 이 스탯으로 스폰
            Log($"몬스터 교체 → {_monsterName}(hp {_monsterMaxHp}) — 이후 스폰부터 적용");
        }

        private void Log(string msg)
        {
            _log.Add(msg);
            if (_log.Count > 12) _log.RemoveAt(0);
            Debug.Log("[BattleDev] " + msg);
        }

        // ---- 디버그 UI ----

        private void OnGUI()
        {
            // 개발용 하네스 박스(스탯·로그·일시정지·소환 선택 등)는 개발 모드에서만 표시한다.
            if (!serverMode)
            {
                GUILayout.BeginArea(new Rect(10, 10, 420, 560), GUI.skin.box);
                GUILayout.Label("<b>Battle Dev Harness</b> (파티 " + _members.Count + "인 · 마스터데이터 연동)");
                int alive = AliveEnemyCount();
                GUILayout.Label($"적: {_monsterName}  현재 {alive}기 (동시 최대 {maxConcurrentEnemies})");
                GUILayout.Label($"처치 수: {_killCount}   단계: {PhaseLabel()}   partyX={_partyX:0.0}");

                GUILayout.Space(3);
                for (int i = 0; i < _members.Count; i++)
                {
                    var m = _members[i];
                    if (m == null) continue;
                    GUILayout.Label($"<b>[{i}] {m.DisplayName}</b>  사거리 {m.AttackRange:0.#}{(m.IsCharging ? "  (돌진 중)" : "")}");
                    for (int j = 0; j < m.SkillCount; j++)
                    {
                        int code = m.SkillCodeAt(j);
                        if (m.TryGetSkillCooldown(code, out float rem, out float tot))
                        {
                            string state = rem <= 0f ? "READY" : $"{rem:0.0}s";
                            GUILayout.Label($"    · 스킬 {code}  cd {tot:0.#}s — {state}");
                        }
                    }
                }

                GUILayout.Space(3);
                DrawSelectionPanel();

                GUILayout.Space(3);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(_paused ? "재개" : "일시정지")) _paused = !_paused;
                if (GUILayout.Button("다음 몬스터")) CycleMonster();
                GUILayout.EndHorizontal();

                GUILayout.Space(3);
                GUILayout.Label("<b>전투 로그</b>");
                for (int i = _log.Count - 1; i >= 0; i--) GUILayout.Label(_log[i]);
                GUILayout.EndArea();
            }
            // 적 HP바는 IMGUI가 아닌 Canvas(UpdateEnemyHpBars)로 그린다 — UI가 그 위에 오도록.
        }

        /// <summary>소환할 캐릭터를 고르는 토글 + 재시작 버튼(구현된 직업만 선택 가능).</summary>
        private void DrawSelectionPanel()
        {
            GUILayout.Label("<b>소환 캐릭터 선택</b> (구현: 기사·레인저·마법사·슬레이어)");
            if (_selected == null || _selected.Length != party.Count) InitSelection();

            int chosen = 0;
            for (int i = 0; i < party.Count; i++)
            {
                var cfg = party[i];
                if (cfg == null) continue;
                bool impl = IsImplemented(cfg);
                GUI.enabled = impl;
                bool cur = impl && _selected[i];
                bool nv = GUILayout.Toggle(cur, $" {cfg.label}{(impl ? "" : " (미구현)")}");
                _selected[i] = impl && nv;
                if (_selected[i]) chosen++;
                GUI.enabled = true;
            }

            GUI.enabled = chosen > 0;
            if (GUILayout.Button($"선택 적용 / 재시작 ({chosen}인)")) RestartWithSelection();
            GUI.enabled = true;
        }

        private string PhaseLabel()
        {
            if (_paused) return "일시정지";
            return _phase == Phase.Advancing ? "전진 중" : "전투 중";
        }

        // ── 적 HP바(Canvas 기반) ──
        // IMGUI는 항상 모든 UI 위에 그려지므로, HP바를 낮은 sortingOrder의 Canvas로 그려
        // HUD(10)·패널(100) 등 UI가 항상 HP바 위에 오도록 한다.
        private const int EnemyHpBarSortingOrder = 1; // HUD(10)/패널(100)보다 아래

        // ── 바 크기 ──
        // 크기는 픽셀 고정이 아니라 <b>월드 기준(카메라 배율에 비례)</b>으로 매 프레임 계산한다.
        // 고정 픽셀(종전 96×18)로 두면 창 높이에 따라 몬스터 대비 크기가 달라진다 — GameScene은 카메라가
        // 창 높이와 같은 정사각 밴드만 그리므로(GameViewLayout), 작업표시줄에 도킹된 작은 창에서는
        // 월드 1유닛당 픽셀이 줄어 같은 96px 바가 몬스터를 뒤덮을 만큼 커 보였다.
        private const float HpBarWorldWidth = 0.5f;        // 바 폭(월드 단위) — 몬스터 대비 크기를 정하는 값
        private const float HpBarAspect = 144f / 768f;     // 프레임 아트(체력바.png 768×144) 비율
        private const float HpBarMinWidthPx = 32f;         // 창이 아주 작아도 이 폭 아래로는 줄이지 않는다
        private const float HpBarInsetRatio = 1f / 6f;     // 프레임 테두리 몫(높이 대비) — 안쪽에 채움을 둔다

        // 이번 프레임의 바 픽셀 크기(UpdateHpBarMetrics가 카메라 배율로 계산한다).
        private float _hpBarWidthPx;
        private float _hpBarHeightPx;
        private float _hpBarInsetPx;

        // HP바 juice: 방금 깎인 구간을 보여주는 고스트 바 + 피격 시 바 흔들림.
        private const float HpBarShakeFrequency = 26f; // 초당 진동 수
        private static readonly Color HpGhostColor = new Color(1f, 0.93f, 0.88f, 0.9f); // 방금 잃은 양(흰색)

        private Canvas _hpCanvas;
        private RectTransform _hpArea; // 전투 화면 밴드로 잘라 내는 컨테이너(밖으로 나간 HP바를 가린다)
        private readonly List<RectTransform> _hpBarRoots = new List<RectTransform>();
        private readonly List<Image> _hpBarFills = new List<Image>();
        private readonly List<Image> _hpBarGhosts = new List<Image>();

        [Tooltip("일반 몬스터 HP바를 머리(스프라이트 상단)에서 얼마나 위에 둘지(월드 단위).")]
        private const float HpBarAboveMargin = 0.2f;

        /// <summary>살아있는 모든 몬스터 머리 위에 Canvas HP 바를 배치·갱신한다(LateUpdate).
        /// 보스는 왕관을 머리 위로 띄워 두므로, 머리와 왕관 사이(band) 안에 바가 오도록 배치한다.</summary>
        private void UpdateEnemyHpBars()
        {
            if (_om == null || _cam == null)
            {
                return;
            }
            EnsureHpCanvas();
            UpdateHpBarMetrics();

            int used = 0;
            foreach (var go in _om.Active(CatEnemy))
            {
                var m = go.GetComponent<MonsterUnit>();
                // 살아 있는 동안 + <b>사망 직후 juice가 남아 있는 동안</b>(MonsterUnit.ShowHpBar) 그린다.
                // 사망 즉시 건너뛰면 한 방에 죽는 몬스터는 바가 한 번도 안 보인 채 사라진다.
                if (m == null || !m.ShowHpBar || m.MaxHp <= 0)
                {
                    continue;
                }
                // 머리 위 기준점은 몬스터가 스폰 직후 1회 측정해 캐시한 오프셋을 쓴다.
                // 매 프레임 스프라이트 경계를 재면 애니메이션(걷기·공격·피격)이 파트를 움직일 때마다
                // 상단·중앙이 요동쳐 HP바가 떨리므로, 애니메이션과 무관한 transform 위치 + 고정 오프셋으로 따라간다.
                // 사망 후에는 시신이 날아가므로 바 기준 위치가 사망 지점에 고정된다(MonsterUnit.HpBarAnchorPos).
                Vector3 pos = m.HpBarAnchorPos;
                float bodyTop, centerX;
                if (m.TryGetHeadAnchor(out Vector2 anchor))
                {
                    centerX = pos.x + anchor.x;
                    bodyTop = pos.y + anchor.y;
                }
                else
                {
                    MeasureBodyTop(m, out centerX, out bodyTop); // 측정 완료 전(스폰 후 1~2프레임) 폴백
                }
                // 일반: 머리 위. 보스: 머리와 왕관 사이(band 중앙).
                float barY = m.IsBoss
                    ? bodyTop + MonsterUnit.BossHpBarBand * 0.5f
                    : bodyTop + HpBarAboveMargin;
                Vector3 sp = _cam.WorldToScreenPoint(new Vector3(centerX, barY, pos.z));
                if (sp.z <= 0f)
                {
                    continue;
                }
                var bar = GetHpBar(used);
                bar.gameObject.SetActive(true);
                bar.sizeDelta = new Vector2(_hpBarWidthPx, _hpBarHeightPx); // 창 크기가 바뀌면 함께 갱신된다
                // 픽셀 격자에 맞춰(정수 좌표) 배치한다 — 소수 좌표면 프레임 아트가 프레임마다 미세하게 번진다.
                // 좌표는 캔버스가 아니라 밴드 컨테이너(_hpArea)의 좌하단 기준이므로 그 원점만큼 빼 준다.
                Vector2 origin = HpAreaOrigin();
                Vector2 jitter = HpBarJitter(m.HpBarShake01); // 피격 직후 바를 미세하게 흔든다
                bar.anchoredPosition = new Vector2(Mathf.Round(sp.x - origin.x + jitter.x),
                                                   Mathf.Round(sp.y - origin.y + jitter.y));
                float fillWidth = _hpBarWidthPx - _hpBarInsetPx * 2f;
                float ratio = Mathf.Clamp01((float)m.Hp / m.MaxHp);
                ApplyHpBarFill(_hpBarFills[used], fillWidth * ratio); // 너비로 체력 표현
                // 고스트 바는 붉은 채움 뒤에서 조금 늦게 따라 내려와, 방금 깎인 구간을 흰색으로 남긴다.
                float ghost = Mathf.Max(ratio, m.HpGhostRatio);
                ApplyHpBarFill(_hpBarGhosts[used], fillWidth * ghost);
                used++;
            }

            // 남는 바 숨김
            for (int i = used; i < _hpBarRoots.Count; i++)
            {
                if (_hpBarRoots[i] != null) _hpBarRoots[i].gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// 몬스터 몸통(왕관 제외) 스프라이트 경계로 몸통 중앙 x·상단 y를 즉석 측정한다.
        /// <b>스폰 직후 <see cref="MonsterUnit.TryGetHeadAnchor"/>가 아직 준비되지 않은 몇 프레임용 폴백</b>이며,
        /// 평상시에는 쓰지 않는다(매 프레임 측정은 애니메이션 때문에 값이 흔들린다).
        /// </summary>
        private static void MeasureBodyTop(MonsterUnit m, out float centerX, out float bodyTop)
        {
            Vector3 pos = m.HpBarAnchorPos; // 사망 후에는 사망 지점(시신을 따라가지 않는다)
            centerX = pos.x;
            bodyTop = pos.y + 1.2f; // 렌더러가 아직 없을 때의 최종 폴백

            Transform crownT = m.transform.Find("BossCrown");
            var rends = m.GetComponentsInChildren<SpriteRenderer>();
            if (rends == null || rends.Length == 0)
            {
                return;
            }
            Bounds body = default;
            bool has = false;
            foreach (var r in rends)
            {
                if (r == null) continue;
                if (crownT != null && r.transform == crownT) continue; // 왕관은 몸통 경계에서 제외
                if (!has) { body = r.bounds; has = true; } else body.Encapsulate(r.bounds);
            }
            if (has) { bodyTop = body.max.y; centerX = body.center.x; }
        }

        /// <summary>
        /// 적 HP바 전용 Canvas(낮은 sortingOrder, 픽셀 좌표계)를 최초 1회 생성한다.
        /// <para>바는 캔버스 직속이 아니라 <b>전투 화면 밴드로 잘라 내는 컨테이너</b>
        /// (<see cref="GameAreaRect"/> + <see cref="RectMask2D"/>) 밑에 만든다. 카메라는
        /// 가운데 밴드만 그리는데(<see cref="GameViewLayout"/>) 캔버스는 창 전체를 덮으므로,
        /// 그냥 두면 <b>아직 화면에 들어오지 않은 몬스터의 HP바가 좌우 여백에 먼저 떠 있었다</b>.
        /// 마스크로 잘라 내면 몬스터 스프라이트가 카메라 가장자리에서 드러나는 것과 같은 속도로
        /// HP바도 밴드 안으로 들어오면서 보인다.</para>
        /// <para>레이아웃이 적용되지 않는 씬(BattleDevScene)에서는 <see cref="GameAreaRect"/>가
        /// 캔버스 전체 스트레치로 되돌리므로 종전과 동일하게 동작한다.</para>
        /// </summary>
        private void EnsureHpCanvas()
        {
            if (_hpCanvas != null)
            {
                return;
            }
            var go = new GameObject("EnemyHpBarCanvas", typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(transform, false);
            _hpCanvas = go.GetComponent<Canvas>();
            _hpCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _hpCanvas.sortingOrder = EnemyHpBarSortingOrder; // UI보다 아래
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; // WorldToScreenPoint 픽셀과 1:1
            scaler.scaleFactor = 1f;

            var areaGo = new GameObject("GameArea", typeof(RectTransform), typeof(RectMask2D));
            areaGo.transform.SetParent(go.transform, false);
            _hpArea = (RectTransform)areaGo.transform;
            GameAreaRect.Attach(_hpArea);
        }

        /// <summary>
        /// 밴드 컨테이너(<see cref="_hpArea"/>) 좌하단의 스크린 픽셀 좌표.
        /// HP바는 이 컨테이너의 자식이라 <c>WorldToScreenPoint</c>(화면 기준)에서 이 원점을 빼야 자리가 맞는다.
        /// 캔버스가 ConstantPixelSize·배율 1이라 캔버스 좌표 = 스크린 픽셀이다.
        /// </summary>
        private Vector2 HpAreaOrigin()
        {
            if (_hpArea == null)
            {
                return Vector2.zero;
            }
            Vector3 corner = _hpArea.TransformPoint(new Vector3(_hpArea.rect.xMin, _hpArea.rect.yMin, 0f));
            return new Vector2(corner.x, corner.y);
        }

        /// <summary>
        /// 이번 프레임의 HP바 픽셀 크기를 카메라 배율(월드 1유닛당 픽셀)로 계산한다.
        /// <para>바 폭을 <see cref="HpBarWorldWidth"/> 월드 단위로 고정하므로 <b>몬스터 대비 크기가 창 크기와
        /// 무관하게 일정</b>하다 — 카메라 세로 시야는 <c>orthographicSize×2</c> 월드이고 그것이
        /// <c>pixelHeight</c> 픽셀로 그려지므로, 배율은 <c>pixelHeight / (2·orthographicSize)</c>다.
        /// <c>pixelHeight</c>는 카메라 뷰포트(GameScene의 전투 밴드) 기준이라 밴드 제한도 그대로 반영된다.</para>
        /// 픽셀 격자에 맞추려고 정수로 반올림한다(소수 크기는 프레임 아트가 번진다).
        /// </summary>
        private void UpdateHpBarMetrics()
        {
            float pxPerWorld = _cam != null && _cam.orthographicSize > 0f
                ? _cam.pixelHeight / (2f * _cam.orthographicSize)
                : 100f;
            _hpBarWidthPx = Mathf.Max(HpBarMinWidthPx, Mathf.Round(HpBarWorldWidth * pxPerWorld));
            _hpBarHeightPx = Mathf.Max(4f, Mathf.Round(_hpBarWidthPx * HpBarAspect));
            _hpBarInsetPx = Mathf.Max(1f, Mathf.Round(_hpBarHeightPx * HpBarInsetRatio));
        }

        /// <summary>채움(현재 체력 또는 고스트) 사각형을 프레임 테두리 안쪽에 지정 너비로 놓는다.</summary>
        private void ApplyHpBarFill(Image fill, float width)
        {
            var rt = fill.rectTransform;
            rt.anchoredPosition = new Vector2(_hpBarInsetPx, 0f);
            rt.sizeDelta = new Vector2(Mathf.Max(0f, width), -_hpBarInsetPx * 2f);
        }

        /// <summary>
        /// 피격 직후 HP바를 흔들 픽셀 오프셋. 강도(<paramref name="shake01"/>)는 몬스터가 감쇠시켜 넘겨준다.
        /// 서로 다른 주파수의 사인을 써서 x·y가 같은 방향으로만 왕복하지 않게 한다(원운동처럼 보이지 않도록).
        /// 시간은 <b>unscaled</b> — 히트스톱으로 시간이 멈춘 동안에도 바가 흔들려야 카메라 셰이크와 결이 맞는다.
        /// 진폭은 바 크기(테두리 몫 = 높이의 1/6)에 비례한다 — 바가 작을 때 크게 흔들면 읽기 어렵다.
        /// </summary>
        private Vector2 HpBarJitter(float shake01)
        {
            if (shake01 <= 0f)
            {
                return Vector2.zero;
            }
            float phase = Time.unscaledTime * HpBarShakeFrequency;
            float amp = _hpBarInsetPx * shake01;
            return new Vector2(Mathf.Sin(phase) * amp, Mathf.Sin(phase * 1.7f) * amp * 0.6f);
        }

        /// <summary>인덱스에 해당하는 HP바(배경+고스트+채움)를 풀에서 얻거나 새로 만든다.</summary>
        private RectTransform GetHpBar(int index)
        {
            while (_hpBarRoots.Count <= index)
            {
                var bgGo = new GameObject("EnemyHpBar", typeof(RectTransform), typeof(Image));
                bgGo.transform.SetParent(_hpArea, false); // 밴드 밖은 마스크가 잘라 낸다
                var bgRt = (RectTransform)bgGo.transform;
                bgRt.anchorMin = bgRt.anchorMax = new Vector2(0f, 0f); // 좌하단 기준
                bgRt.pivot = new Vector2(0.5f, 0.5f);
                bgRt.sizeDelta = new Vector2(_hpBarWidthPx, _hpBarHeightPx); // 실제 크기는 매 프레임 갱신된다
                var bgImg = bgGo.GetComponent<Image>();
                if (enemyHpBarFrame != null)
                {
                    bgImg.sprite = enemyHpBarFrame; // 체력바 프레임 아트(원본 비율 그대로 축소)
                    bgImg.color = Color.white;
                }
                else
                {
                    bgImg.color = new Color(0f, 0f, 0f, 0.6f); // 프레임 아트 미배선 시 폴백(단색 배경)
                }
                bgImg.raycastTarget = false;

                // 고스트(방금 깎인 양): 붉은 채움보다 <b>먼저</b> 만들어 그 아래에 깔린다(자식 순서 = 그리기 순서).
                // 붉은 채움이 덮지 못하고 남는 오른쪽 구간이 곧 "직전 체력 → 현재 체력" 차이다.
                var ghostImg = NewHpBarFill(bgGo.transform, "Ghost", HpGhostColor);
                ApplyHpBarFill(ghostImg, 0f);

                // 채움: 좌측 정렬 솔리드 사각형(너비로 체력 비율 표현). 프레임 테두리를 피해 안쪽으로 넣는다.
                var fillImg = NewHpBarFill(bgGo.transform, "Fill", new Color(0.85f, 0.16f, 0.16f, 1f));
                ApplyHpBarFill(fillImg, 0f);

                _hpBarRoots.Add(bgRt);
                _hpBarFills.Add(fillImg);
                _hpBarGhosts.Add(ghostImg);
            }
            return _hpBarRoots[index];
        }

        /// <summary>HP바 안쪽 채움용 Image(좌측 정렬·프레임 테두리 안쪽)를 만든다 — 고스트와 현재 체력이 공유한다.
        /// 자리·크기는 <see cref="ApplyHpBarFill"/>이 현재 바 크기에 맞춰 넣는다.</summary>
        private static Image NewHpBarFill(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }
    }
}
