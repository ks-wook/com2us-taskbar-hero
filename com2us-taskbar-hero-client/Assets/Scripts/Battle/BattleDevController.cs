using System.Collections;
using System.Collections.Generic;
using UnityEngine;
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

            Log($"전투 시작 — 파티 {_members.Count}인 vs {_monsterName} 웨이브(hp {_monsterMaxHp}, 동시 최대 {maxConcurrentEnemies})");
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

        /// <summary>선택 상태 배열을 파티 크기에 맞춰 초기화한다(구현된 직업만 기본 선택).</summary>
        private void InitSelection()
        {
            _selected = new bool[party.Count];
            for (int i = 0; i < party.Count; i++)
                _selected[i] = IsImplemented(party[i]);
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

            SpawnParty(start);

            // 스킬/초상화 UI를 새 파티로 재구성
            var ui = FindAnyObjectByType<SkillCooldownUI>();
            if (ui != null) ui.Rebuild();

            Log($"재시작 — 선택 소환 {_members.Count}인");
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
            Log($"{label} → -{dmg} (HP {Mathf.Max(0, (int)target.Hp)}/{target.MaxHp})");
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

            DrawMonsterHpBars();
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

        /// <summary>살아있는 모든 몬스터 머리 위에 HP 바를 그린다(웨이브 전원 표시).</summary>
        private void DrawMonsterHpBars()
        {
            if (_om == null || _cam == null) return;
            foreach (var go in _om.Active(CatEnemy))
            {
                var m = go.GetComponent<MonsterUnit>();
                if (m == null || !m.Alive || m.MaxHp <= 0) continue;
                Vector3 sp = _cam.WorldToScreenPoint(m.transform.position + Vector3.up * 1.2f); // 몬스터에 더 가깝게(아래로)
                if (sp.z <= 0f) continue;
                const float w = 90f, h = 10f;
                float x = sp.x - w / 2f;
                float y = Screen.height - sp.y;
                float ratio = Mathf.Clamp01((float)m.Hp / m.MaxHp);
                DrawRect(new Rect(x - 1, y - 1, w + 2, h + 2), new Color(0f, 0f, 0f, 0.6f));
                DrawRect(new Rect(x, y, w * ratio, h), Color.red); // 적 체력바 빨강
            }
        }

        private static Texture2D _px;

        private static void DrawRect(Rect r, Color c)
        {
            if (_px == null)
            {
                _px = new Texture2D(1, 1);
                _px.SetPixel(0, 0, Color.white);
                _px.Apply();
            }
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, _px);
            GUI.color = prev;
        }
    }
}
