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

        [Header("스킬 설정")]
        [Tooltip("개발용 스킬 레벨(계수 조회 기준)")]
        public int devSkillLevel = 1;
        [Tooltip("스킬 쿨타임 폴백(초). 마스터 데이터 cooldown이 0 이하일 때만 사용")]
        public float skillCooldown = 10f;
        [Tooltip("이펙트 재생 y 오프셋")]
        public float effectYOffset = 0.6f;

        [Header("개발용 편의")]
        [Tooltip("데미지 배수(개발용). 마스터 데이터 값 자체는 불변")]
        public float devDamageMultiplier = 20f;

        [Header("이동 / 교전 / 카메라")]
        [Tooltip("최전방 캐릭터 앞쪽 여백(카메라)")]
        public float camOffsetX = 2.5f;
        [Tooltip("최후방 캐릭터 뒤쪽 여백(카메라 — 후방 전원 노출)")]
        public float camRearMargin = 1.2f;
        [Tooltip("카메라 줌(orthographicSize) 보간 속도")]
        public float camZoomLerp = 4f;
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

        // ObjectManager 카테고리 키
        private const string CatAlly = "battle_ally";
        private const string CatEnemy = "battle_enemy";

        private Phase _phase = Phase.Advancing;
        private bool _paused;
        private int _killCount;

        // 서버 구동 모드 상태
        private Queue<int> _serverQueue;                       // 스폰할 몬스터 코드(순서대로)
        private System.Func<int, GameObject> _prefabResolver;  // 코드 → 프리팹
        private System.Action _onAllCleared;                   // 전멸 시 1회 호출
        private int _serverSpawned;
        private bool _serverCleared;

        // 소환 캐릭터 선택(테스트용). 현재 구현된 직업만 선택 가능.
        private static readonly HashSet<int> ImplementedClasses = new HashSet<int> { 1, 2, 3 }; // 기사1·레인저2·마법사3
        private bool[] _selected;

        private readonly List<string> _log = new List<string>();

        /// <summary>파티 멤버(UI 등 외부 조회용). index 0 = 선두.</summary>
        public IReadOnlyList<PlayerCombatant> Party => _members;

        private void Start()
        {
            _cam = Camera.main;
            if (_cam != null && _cam.orthographic) _baseOrtho = _cam.orthographicSize;
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
            _spawnTimer = enemySpawnInterval; // 시작하자마자 1기 스폰

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

            float front = float.NegativeInfinity, rear = float.PositiveInfinity;
            foreach (var m in _members)
            {
                if (m == null) continue;
                float x = m.transform.position.x;
                if (x > front) front = x;
                if (x < rear) rear = x;
            }
            if (float.IsNegativeInfinity(front)) return;

            float left = rear - camRearMargin;
            float right = front + camOffsetX;
            float mid = (left + right) * 0.5f;
            float needHalfW = (right - left) * 0.5f;
            float targetOrtho = Mathf.Max(_baseOrtho, needHalfW / Mathf.Max(0.01f, _cam.aspect));

            _cam.orthographicSize = Mathf.Lerp(_cam.orthographicSize, targetOrtho, Time.unscaledDeltaTime * camZoomLerp);
            Vector3 c = _cam.transform.position;
            c.x = mid;
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

        private void ResolveMonsterCode()
        {
            var db = MasterDataManager.Db;
            if (db == null) return;
            if (monsterCode != 0 && db.Monsters.ContainsKey(monsterCode)) return;
            foreach (var code in db.Monsters.Keys) { monsterCode = code; return; }
        }

        private void LoadMonsterStats()
        {
            var db = MasterDataManager.Db;
            if (db != null && db.Monsters.TryGetValue(monsterCode, out MonsterMaster mon))
            {
                _monsterName = mon.name;
                _monsterMaxHp = mon.hp;
                _monsterAtk = mon.attack;
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

        /// <summary>선택 상태 배열을 초기화한다. 개발 모드는 구현된 직업 전부, 서버 모드는
        /// 계정에 실제로 있는 캐릭터(classCode)와 일치하는 멤버만 선택한다.</summary>
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
                        if (c != null) accountClasses.Add(c.classCode);
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

        /// <summary>아군 1인을 ObjectManager로 생성하고 SPUM 애니메이터/방향/전투 컴포넌트를 배선해 반환한다.</summary>
        private PlayerCombatant SpawnAlly(PartyMemberConfig cfg, Vector3 pos)
        {
            var go = _om.Spawn(CatAlly, cfg.prefab, pos, Quaternion.identity);
            if (go == null) return null;
            EnsureSpumAnimator(go);
            SetFacingRight(go, true); // 아군은 오른쪽(적)을 바라봄
            var pc = go.GetComponent<PlayerCombatant>();
            if (pc == null) pc = go.AddComponent<PlayerCombatant>();
            pc.Configure(this, cfg);
            return pc;
        }

        // ---- 적 웨이브(스폰 시점/위치·스탯은 컨트롤러가 결정, 생성/추적/정리는 ObjectManager) ----

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
                if (_spawnTimer >= Mathf.Max(0.1f, enemySpawnInterval) && AliveEnemyCount() < Mathf.Max(1, maxConcurrentEnemies))
                {
                    _spawnTimer = 0f;
                    SpawnMonster();
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
                if (_spawnTimer >= Mathf.Max(0.1f, enemySpawnInterval) && AliveEnemyCount() < Mathf.Max(1, maxConcurrentEnemies))
                {
                    _spawnTimer = 0f;
                    SpawnMonsterByCode(_serverQueue.Dequeue());
                    _serverSpawned++;
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

        /// <summary>지정 몬스터 코드의 프리팹(리졸버)과 마스터 스탯으로 적 1기를 스폰한다.</summary>
        private void SpawnMonsterByCode(int code)
        {
            string mname = "Monster";
            long hp = 100;
            long atk = 5;
            var db = MasterDataManager.Db;
            if (db != null && db.Monsters.TryGetValue(code, out MonsterMaster mon))
            {
                mname = mon.name;
                hp = mon.hp;
                atk = mon.attack;
            }

            GameObject prefab = _prefabResolver != null ? _prefabResolver(code) : null;
            if (prefab == null) prefab = monsterPrefab; // 폴백
            if (prefab == null)
            {
                Log($"[경고] 몬스터 {code} 프리팹을 찾지 못해 스폰 건너뜀");
                return;
            }

            float rightEdge = _cam != null ? _cam.transform.position.x + _cam.orthographicSize * _cam.aspect : 10f;
            Vector3 pos = new Vector3(rightEdge + enemyOffscreenMargin, _pathY, 0f);

            var go = _om.Spawn(CatEnemy, prefab, pos, Quaternion.identity);
            if (go == null) return;
            EnsureSpumAnimator(go);
            SetFacingRight(go, false);
            var mu = go.GetComponent<MonsterUnit>();
            if (mu == null) mu = go.AddComponent<MonsterUnit>();
            mu.Init(mname, hp, atk, enemyMoveSpeed, () => _paused, OnMonsterKilled);
        }

        /// <summary>서버 스테이지 진입 데이터로 유한 웨이브 전투를 시작한다.
        /// plan: (몬스터코드→마리수) 순서 목록, prefabResolver: 코드→프리팹, onAllCleared: 전멸 시 1회.</summary>
        public void BeginServerBattle(List<KeyValuePair<int, int>> plan,
                                      System.Func<int, GameObject> prefabResolver, System.Action onAllCleared)
        {
            serverMode = true;
            _prefabResolver = prefabResolver;
            _onAllCleared = onAllCleared;
            _serverQueue = new Queue<int>();
            if (plan != null)
            {
                foreach (var kv in plan)
                {
                    for (int i = 0; i < kv.Value; i++)
                    {
                        _serverQueue.Enqueue(kv.Key);
                    }
                }
            }
            _serverSpawned = 0;
            _serverCleared = false;
            _spawnTimer = enemySpawnInterval; // 곧 첫 스폰
            Log($"서버 전투 시작 — 총 {_serverQueue.Count}마리 예정");
        }

        /// <summary>카메라 우측 바깥(보이지 않는 지점)에 적 1기를 생성·배선한다.</summary>
        private void SpawnMonster()
        {
            float rightEdge = _cam != null ? _cam.transform.position.x + _cam.orthographicSize * _cam.aspect : 10f;
            Vector3 pos = new Vector3(rightEdge + enemyOffscreenMargin, _pathY, 0f);

            var go = _om.Spawn(CatEnemy, monsterPrefab, pos, Quaternion.identity);
            if (go == null) return;
            EnsureSpumAnimator(go);
            SetFacingRight(go, false); // 파티(왼쪽)를 바라봄
            var mu = go.GetComponent<MonsterUnit>();
            if (mu == null) mu = go.AddComponent<MonsterUnit>();
            mu.Init(_monsterName, _monsterMaxHp, _monsterAtk, enemyMoveSpeed,
                    () => _paused, OnMonsterKilled);
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

            // 진행 중 지연 데미지 코루틴 취소 + 몬스터 전부 정리
            StopAllCoroutines();
            if (_om != null) _om.Clear(CatEnemy);
            _spawnTimer = enemySpawnInterval;

            _killCount = 0;
            _phase = Phase.Advancing;

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
        public void RestartServerBattle(List<KeyValuePair<int, int>> plan,
                                        System.Func<int, GameObject> prefabResolver, System.Action onAllCleared)
        {
            ResetBattlefield();
            BeginServerBattle(plan, prefabResolver, onAllCleared);
        }

        // ---- 파티 멤버(PlayerCombatant)가 사용하는 공유 훅 ----

        /// <summary>파티에 가장 가까운(최전방) 살아있는 몬스터.</summary>
        public MonsterUnit FrontMonster => FrontMonsterUnit();
        public Transform MonsterTransform { get { var m = FrontMonster; return m != null ? m.transform : null; } }
        public bool MonsterAlive => FrontMonster != null;
        public bool IsFighting => _phase == Phase.Fighting && MonsterAlive;
        public float PathY => _pathY;
        public float DevDamageMultiplier => devDamageMultiplier;
        public float BasicHitDelay => basicAttackHitDelay;
        public float EffectYOffset => effectYOffset;
        public int DevSkillLevel => devSkillLevel;
        public float SkillCooldownFallback => skillCooldown;
        public bool IsPaused => _paused;

        /// <summary>공격 모션/이펙트가 끝난 뒤 최전방 몬스터에 데미지를 적용한다(호출 시점의 대상을 캡처, 생존 시에만 적용).</summary>
        public void DealDamageAfter(float delay, long dmg, string label)
        {
            var target = FrontMonster;
            StartCoroutine(DoDamageAfter(delay, dmg, label, target));
        }

        private IEnumerator DoDamageAfter(float delay, long dmg, string label, MonsterUnit target)
        {
            if (delay > 0f)
            {
                yield return new WaitForSeconds(delay);
            }
            if (target == null || !target.Alive)
            {
                yield break;
            }
            target.TakeDamage(dmg);
            // 피격 데미지를 붉은 숫자로 표시(오브젝트 풀 재사용).
            DamageNumberPool.GetOrCreate().Spawn(dmg, target.transform.position + Vector3.up * (effectYOffset + 0.5f));
            Log($"{label} → -{dmg} (HP {Mathf.Max(0, (int)target.Hp)}/{target.MaxHp})");
        }

        /// <summary>지연 후 지정 중심 반경 내 모든 살아있는 적에게 데미지를 적용한다(광역 스킬).</summary>
        public void DealAreaDamageAfter(float delay, long dmg, string label, Vector3 center, float radius)
        {
            StartCoroutine(DoAreaDamageAfter(delay, dmg, label, center, radius));
        }

        private IEnumerator DoAreaDamageAfter(float delay, long dmg, string label, Vector3 center, float radius)
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
            float r2 = radius * radius;
            foreach (var mu in targets)
            {
                if (mu == null || !mu.Alive) continue;
                if (((Vector2)mu.transform.position - (Vector2)center).sqrMagnitude <= r2)
                {
                    mu.TakeDamage(dmg);
                    DamageNumberPool.GetOrCreate().Spawn(dmg, mu.transform.position + Vector3.up * (effectYOffset + 0.5f));
                    hit++;
                }
            }
            Log($"{label} (광역 r{radius:0.#}) → {hit}체 -{dmg}");
        }

        /// <summary>MonsterUnit이 죽는 순간 주입된 콜백으로 호출된다 — 누적 킬/로그 갱신.</summary>
        public void OnMonsterKilled(MonsterUnit m)
        {
            _killCount++;
            Log($"{(m != null ? m.MonsterName : _monsterName)} 처치! (누적 {_killCount})");
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
            GUILayout.Label("<b>소환 캐릭터 선택</b> (구현: 기사·레인저·마법사)");
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
        private const float HpBarWidth = 90f;
        private const float HpBarHeight = 10f;

        private Canvas _hpCanvas;
        private readonly List<RectTransform> _hpBarRoots = new List<RectTransform>();
        private readonly List<Image> _hpBarFills = new List<Image>();

        /// <summary>살아있는 모든 몬스터 머리 위에 Canvas HP 바를 배치·갱신한다(LateUpdate).</summary>
        private void UpdateEnemyHpBars()
        {
            if (_om == null || _cam == null)
            {
                return;
            }
            EnsureHpCanvas();

            int used = 0;
            foreach (var go in _om.Active(CatEnemy))
            {
                var m = go.GetComponent<MonsterUnit>();
                if (m == null || !m.Alive || m.MaxHp <= 0)
                {
                    continue;
                }
                Vector3 sp = _cam.WorldToScreenPoint(m.transform.position + Vector3.up * 1.2f);
                if (sp.z <= 0f)
                {
                    continue;
                }
                var bar = GetHpBar(used);
                bar.gameObject.SetActive(true);
                bar.anchoredPosition = new Vector2(sp.x, sp.y); // ConstantPixelSize 캔버스(좌하단 기준 픽셀)
                float ratio = Mathf.Clamp01((float)m.Hp / m.MaxHp);
                _hpBarFills[used].rectTransform.sizeDelta = new Vector2(HpBarWidth * ratio, -2f); // 너비로 체력 표현
                used++;
            }

            // 남는 바 숨김
            for (int i = used; i < _hpBarRoots.Count; i++)
            {
                if (_hpBarRoots[i] != null) _hpBarRoots[i].gameObject.SetActive(false);
            }
        }

        /// <summary>적 HP바 전용 Canvas(낮은 sortingOrder, 픽셀 좌표계)를 최초 1회 생성한다.</summary>
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
        }

        /// <summary>인덱스에 해당하는 HP바(배경+채움)를 풀에서 얻거나 새로 만든다.</summary>
        private RectTransform GetHpBar(int index)
        {
            while (_hpBarRoots.Count <= index)
            {
                var bgGo = new GameObject("EnemyHpBar", typeof(RectTransform), typeof(Image));
                bgGo.transform.SetParent(_hpCanvas.transform, false);
                var bgRt = (RectTransform)bgGo.transform;
                bgRt.anchorMin = bgRt.anchorMax = new Vector2(0f, 0f); // 좌하단 기준
                bgRt.pivot = new Vector2(0.5f, 0.5f);
                bgRt.sizeDelta = new Vector2(HpBarWidth + 2f, HpBarHeight + 2f);
                var bgImg = bgGo.GetComponent<Image>();
                bgImg.color = new Color(0f, 0f, 0f, 0.6f);
                bgImg.raycastTarget = false;

                // 채움: 좌측 정렬 솔리드 사각형(너비로 체력 비율 표현). 세로는 부모에 맞춰 1px 인셋.
                var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
                fillGo.transform.SetParent(bgGo.transform, false);
                var fillRt = (RectTransform)fillGo.transform;
                fillRt.anchorMin = new Vector2(0f, 0f);
                fillRt.anchorMax = new Vector2(0f, 1f);
                fillRt.pivot = new Vector2(0f, 0.5f);
                fillRt.anchoredPosition = new Vector2(1f, 0f);
                fillRt.sizeDelta = new Vector2(HpBarWidth, -2f);
                var fillImg = fillGo.GetComponent<Image>();
                fillImg.color = Color.red;
                fillImg.raycastTarget = false;

                _hpBarRoots.Add(bgRt);
                _hpBarFills.Add(fillImg);
            }
            return _hpBarRoots[index];
        }
    }
}
