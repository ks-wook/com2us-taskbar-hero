using System.Collections.Generic;
using UnityEngine;
using TaskbarHero.Client.Managers;
using TaskbarHero.Common.Dto;

namespace TaskbarHero.Client.Battle
{
    /// <summary>
    /// GameScene 던전 전투 흐름: 마지막 스테이지(없으면 1-1)로 <c>stage/enter</c> 요청 → 응답의 몬스터 구성으로
    /// <see cref="BattleDevController"/>에 유한 웨이브 전투를 시작 → 모든 몬스터 처치 시 <c>stage/clear</c> 요청.
    /// 몬스터 프리팹은 코드→프리팹 맵(에디터 빌더가 monster_{code}.prefab로 채움)에서 조회한다.
    /// </summary>
    public class DungeonBattleFlow : MonoBehaviour
    {
        /// <summary>몬스터 코드 ↔ 프리팹 매핑(에디터 빌더가 채운다).</summary>
        [System.Serializable]
        public class MonsterPrefabEntry
        {
            public int code;
            public GameObject prefab;
        }

        [SerializeField] private BattleDevController battle;
        [SerializeField] private List<MonsterPrefabEntry> monsterPrefabs = new List<MonsterPrefabEntry>();
        [SerializeField] private ScrollingBackground background;
        [Tooltip("스테이지 backgroundType(1~5)별 배경. index 0=타입1 … 4=타입5 (dungeon_bg_1~5).")]
        [SerializeField] private Sprite[] backgrounds = new Sprite[0];

        [Header("클리어 연출")]
        [Tooltip("클리어 순간 게임 속도(슬로우모션). 0~1, 예: 0.25")]
        [SerializeField] private float clearSlowMotionScale = 0.25f;

        private readonly Dictionary<int, GameObject> _prefabByCode = new Dictionary<int, GameObject>();
        private int _act = 1;
        private int _difficulty = 1;
        private int _stage = 1;
        private bool _entered;
        private bool _cleared;

        /// <summary>현재 진행 중인 스테이지 좌표(외부 관찰/디버그용).</summary>
        public int CurrentAct => _act;
        public int CurrentDifficulty => _difficulty;
        public int CurrentStage => _stage;

        private void Awake()
        {
            foreach (var e in monsterPrefabs)
            {
                if (e != null && e.prefab != null)
                {
                    _prefabByCode[e.code] = e.prefab;
                }
            }
        }

        private void Start()
        {
            RequestEnter();
        }

        /// <summary>마지막 스테이지(없으면 1-1)로 입장 요청을 보낸다(최초 1회).</summary>
        private void RequestEnter()
        {
            if (_entered)
            {
                return;
            }

            int act = 1, difficulty = 1, stage = 1;
            var player = Session.GameData != null ? Session.GameData.player : null;
            if (player != null && player.act >= 1 && player.stage >= 1)
            {
                act = player.act;
                difficulty = player.difficulty >= 1 ? player.difficulty : 1;
                stage = player.stage;
            }
            _entered = true;
            EnterStage(act, difficulty, stage);
        }

        /// <summary>지정한 좌표의 스테이지로 입장 요청을 보낸다(최초 입장·클리어 후 다음 스테이지 공용).</summary>
        private void EnterStage(int act, int difficulty, int stage)
        {
            if (NetworkManager.Instance == null)
            {
                Debug.LogWarning("[Dungeon] NetworkManager를 찾을 수 없어 입장 요청 생략.");
                return;
            }
            if (!Session.IsLoggedIn)
            {
                Debug.LogWarning("[Dungeon] 로그인 세션이 없어 입장 요청 생략.");
                return;
            }

            _act = act;
            _difficulty = difficulty;
            _stage = stage;
            _cleared = false; // 새 스테이지 진입 시 클리어 가드 해제

            var request = new StageActionRequest
            {
                userId = Session.UserId,
                token = Session.Token,
                data = new StageActionData { act = act, difficulty = difficulty, stage = stage },
            };
            Debug.Log($"[Dungeon] 입장 요청 {act}-{difficulty}-{stage}");
            NetworkManager.Instance.PostToGame<StageEnterResponse>("/api/game/stage/enter", request, OnEnter, OnEnterError);
        }

        /// <summary>입장 성공: 몬스터 구성으로 전투 시작.</summary>
        private void OnEnter(StageEnterResponse response)
        {
            var d = response != null ? response.data : null;
            if (d == null || battle == null)
            {
                Debug.LogWarning("[Dungeon] 진입 데이터 또는 전투 컨트롤러가 없어 전투를 시작하지 못했습니다.");
                return;
            }
            _act = d.act;
            _difficulty = d.difficulty;
            _stage = d.stage;

            var plan = new List<KeyValuePair<int, int>>();
            if (d.monsters != null)
            {
                foreach (var m in d.monsters)
                {
                    plan.Add(new KeyValuePair<int, int>(m.monsterCode, m.count));
                }
            }
            if (d.boss != null && d.boss.monsterCode != 0)
            {
                plan.Add(new KeyValuePair<int, int>(d.boss.monsterCode, 1));
            }

            int total = 0;
            foreach (var kv in plan)
            {
                total += kv.Value;
            }
            ApplyBackground(d.backgroundType);
            Debug.Log($"[Dungeon] 진입 완료 {d.act}-{d.difficulty}-{d.stage}, 배경타입 {d.backgroundType} → 스폰 예정 {total}마리");
            battle.BeginServerBattle(plan, ResolvePrefab, OnAllCleared);
        }

        private void OnEnterError(NetworkError error)
        {
            Debug.LogWarning($"[Dungeon] 입장 실패: {error}");
        }

        /// <summary>backgroundType(1~5)에 맞는 전투 배경으로 교체한다.</summary>
        private void ApplyBackground(int backgroundType)
        {
            if (background == null || backgrounds == null || backgrounds.Length == 0)
            {
                return;
            }
            int idx = Mathf.Clamp(backgroundType - 1, 0, backgrounds.Length - 1);
            if (backgrounds[idx] != null)
            {
                background.SetSprite(backgrounds[idx]);
            }
        }

        /// <summary>코드로 몬스터 프리팹을 조회(없으면 null → 컨트롤러가 폴백/건너뜀).</summary>
        private GameObject ResolvePrefab(int code)
        {
            return _prefabByCode.TryGetValue(code, out var p) ? p : null;
        }

        /// <summary>모든 몬스터 처치 시: 슬로우모션 연출과 함께 클리어 요청.</summary>
        private void OnAllCleared()
        {
            if (_cleared)
            {
                return;
            }
            _cleared = true;

            // 슬로우모션 연출(응답 대기 동안 극적 정지감). 오버레이가 닫힐 때 1로 복원된다.
            Time.timeScale = Mathf.Clamp(clearSlowMotionScale, 0.01f, 1f);

            if (NetworkManager.Instance == null)
            {
                return;
            }
            Debug.Log($"[Dungeon] 전멸 → 클리어 요청 {_act}-{_difficulty}-{_stage}");
            var request = new StageActionRequest
            {
                userId = Session.UserId,
                token = Session.Token,
                data = new StageActionData { act = _act, difficulty = _difficulty, stage = _stage },
            };
            NetworkManager.Instance.PostToGame<StageClearResponse>("/api/game/stage/clear", request, OnClear, OnClearError);
        }

        private void OnClear(StageClearResponse response)
        {
            var d = response != null ? response.data : null;
            StageProgressDto next = null;
            if (d != null && d.rewards != null && d.progress != null)
            {
                Debug.Log($"[Dungeon] 클리어 성공! 골드+{d.rewards.gold} 경험치+{d.rewards.exp}, " +
                          $"진행도 {d.progress.act}-{d.progress.difficulty}-{d.progress.stage} (maxCleared={d.progress.maxStageCleared})");
                next = d.progress; // 서버가 전진시킨 진행도 = 다음 도전 스테이지

                // 세션에 캐싱된 세이브 진행도를 갱신(스테이지 UI 등이 최신 클리어 상황을 반영하도록).
                var player = Session.GameData != null ? Session.GameData.player : null;
                if (player != null)
                {
                    player.act = d.progress.act;
                    player.difficulty = d.progress.difficulty;
                    player.stage = d.progress.stage;
                    player.maxStageCleared = d.progress.maxStageCleared;
                }
            }
            else
            {
                Debug.Log("[Dungeon] 클리어 성공(응답 데이터 없음).");
            }

            // 클리어 연출 이펙트 + 보상 노출(클릭 또는 5초 후 닫힘 → timeScale 복원 → 다음 스테이지 자동 입장).
            StageClearOverlay.Show(d, () => OnClearOverlayClosed(next));
        }

        /// <summary>클리어 연출이 닫히면 서버 진행도의 다음 스테이지로 자동 입장한다.</summary>
        private void OnClearOverlayClosed(StageProgressDto next)
        {
            if (next == null)
            {
                return;
            }
            Debug.Log($"[Dungeon] 다음 스테이지 자동 입장 {next.act}-{next.difficulty}-{next.stage}");
            EnterStage(next.act, next.difficulty, next.stage);
        }

        private void OnClearError(NetworkError error)
        {
            Debug.LogWarning($"[Dungeon] 클리어 실패: {error}");
            // 연출을 못 띄우더라도 슬로우모션은 반드시 복원한다.
            Time.timeScale = 1f;
        }
    }
}
