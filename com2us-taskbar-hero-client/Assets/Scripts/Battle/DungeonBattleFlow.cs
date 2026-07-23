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

        [Header("레벨업 이펙트")]
        [Tooltip("레벨업 시 캐릭터에 재생할 글로우 프레임(에디터 빌더가 LevelUpGlow_01~30 순서로 배선).")]
        [SerializeField] private Sprite[] levelUpFrames = new Sprite[0];
        [Tooltip("레벨업 글로우 초당 프레임 수.")]
        [SerializeField] private float levelUpFps = 24f;
        [Tooltip("레벨업 글로우 월드 높이(유닛). 스프라이트를 이 높이에 맞춰 스케일한다.")]
        [SerializeField] private float levelUpHeight = 3f;
        [Tooltip("레벨업 글로우를 캐릭터 기준 위로 올리는 y 오프셋(몸통 중심 정렬).")]
        [SerializeField] private float levelUpYOffset = 0.6f;
        [Tooltip("레벨업 글로우 정렬 순서(캐릭터 스프라이트 대비 가산 — 앞에 표시).")]
        [SerializeField] private int levelUpSortingOffset = 30;

        private readonly Dictionary<int, GameObject> _prefabByCode = new Dictionary<int, GameObject>();
        private int _act = 1;
        private int _difficulty = 1;
        private int _stage = 1;
        private bool _entered;
        private bool _cleared;
        private bool _defeated;        // 패배 처리 1회 가드(재입장 시 해제)
        private bool _restartOnEnter; // true면 다음 입장 응답에서 전투 필드를 리셋하고 처음부터 시작
        private bool _repeatSameStage; // true면 클리어 후 같은 스테이지를 반복(이미 클리어한 스테이지 수동 입장), false면 다음 스테이지로 전진
        private readonly List<int> _pendingLevelUps = new List<int>(); // 이번 클리어에서 레벨업한 캐릭터 id(오버레이 종료 후 글로우 재생)

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

        /// <summary>스테이지 UI에서 선택한 스테이지로 처음부터 입장한다(전투 필드를 리셋하고 그 스테이지를 새로 시작).
        /// 자동 진입 가드(_entered)도 세워, Start의 최초 입장과 중복되지 않게 한다.</summary>
        public void EnterSelectedStage(int act, int difficulty, int stage)
        {
            // 선택 스테이지가 현재 진행도(프론티어 = 다음 도전 스테이지)와 같으면 '첫 클리어' 대상 → 클리어 시 다음으로 전진.
            // 그보다 앞선(이미 클리어한) 스테이지면 → 클리어해도 전진하지 않고 그 스테이지를 계속 반복한다.
            var player = Session.GameData != null ? Session.GameData.player : null;
            bool isFrontier = player != null && player.act == act && player.difficulty == difficulty && player.stage == stage;
            _repeatSameStage = !isFrontier;

            _entered = true;
            EnterStage(act, difficulty, stage, restartFromStart: true);
        }

        /// <summary>지정한 좌표의 스테이지로 입장 요청을 보낸다(최초 입장·클리어 후 다음 스테이지 공용).
        /// restartFromStart=true면 응답 수신 시 전투 필드를 리셋하고 처음부터 시작한다.</summary>
        private void EnterStage(int act, int difficulty, int stage, bool restartFromStart = false)
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
            _defeated = false; // 패배 가드도 해제
            _restartOnEnter = restartFromStart;
            Time.timeScale = 1f; // 클리어 슬로우모션 중 수동 입장에 대비해 시간 배율 복원

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
            int bossCode = d.boss != null ? d.boss.monsterCode : 0;
            if (bossCode != 0)
            {
                plan.Add(new KeyValuePair<int, int>(bossCode, 1));
            }

            int total = 0;
            foreach (var kv in plan)
            {
                total += kv.Value;
            }
            StageEnterBanner.Show(d.act, d.difficulty, d.stage); // 상단 중앙 입장 배너(페이드 인/아웃)
            Debug.Log($"[Dungeon] 진입 완료 {d.act}-{d.difficulty}-{d.stage}, 배경타입 {d.backgroundType} → 스폰 예정 {total}마리");
            if (_restartOnEnter)
            {
                _restartOnEnter = false;
                battle.RestartServerBattle(plan, ResolvePrefab, OnAllCleared, bossCode, OnDefeat); // 처음부터: 진행 중 전투 필드 리셋 후 시작(카메라도 시작 지점으로 스냅)
            }
            else
            {
                battle.BeginServerBattle(plan, ResolvePrefab, OnAllCleared, bossCode, OnDefeat);
            }
            // 전투 필드 리셋(카메라 위치 확정) 후 배경을 구축해야 스크롤 배경 타일이 올바른 위치에 생성된다.
            ApplyBackground(d.backgroundType);
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

        /// <summary>아군 전멸(패배) 시: 패배 연출을 띄우고, 닫히면 현재 스테이지를 처음부터 다시 시작한다.</summary>
        private void OnDefeat()
        {
            if (_defeated)
            {
                return;
            }
            _defeated = true;

            // 패배 순간 슬로우모션(응답 대기감). 오버레이가 닫힐 때 1로 복원된다.
            Time.timeScale = Mathf.Clamp(clearSlowMotionScale, 0.01f, 1f);
            Debug.Log($"[Dungeon] 아군 전멸 → 패배, 현재 스테이지 재시작 {_act}-{_difficulty}-{_stage}");
            BattleDefeatOverlay.Show(() => EnterStage(_act, _difficulty, _stage, restartFromStart: true));
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

            // 레벨업한 캐릭터 수집(오버레이가 닫혀 전장이 보일 때 글로우 재생) + 세션 캐릭터 레벨/경험치 동기화.
            CollectLevelUps(d);

            // 클리어 연출 이펙트 + 보상 노출(클릭 또는 5초 후 닫힘 → timeScale 복원 → 다음 스테이지 자동 입장).
            StageClearOverlay.Show(d, () => OnClearOverlayClosed(next));
        }

        /// <summary>클리어 응답의 캐릭터 진행(레벨/경험치)을 세션에 반영하고, 레벨업한 캐릭터 id를 모아둔다.</summary>
        private void CollectLevelUps(StageClearData d)
        {
            _pendingLevelUps.Clear();
            if (d == null || d.characters == null)
            {
                return;
            }
            var chars = Session.GameData != null ? Session.GameData.characters : null;
            foreach (var cp in d.characters)
            {
                if (cp == null)
                {
                    continue;
                }
                // 세션 캐시의 해당 캐릭터 레벨/경험치를 최신화(인벤토리 경험치 바 등 반영).
                if (chars != null)
                {
                    foreach (var c in chars)
                    {
                        if (c != null && c.characterId == cp.characterId)
                        {
                            c.level = cp.level;
                            c.exp = cp.exp;
                            break;
                        }
                    }
                }
                if (cp.isLevelUp)
                {
                    _pendingLevelUps.Add(cp.characterId);
                }
            }
        }

        /// <summary>클리어 연출이 닫히면 레벨업 글로우를 재생하고, 서버 진행도의 다음 스테이지로 자동 입장한다.</summary>
        private void OnClearOverlayClosed(StageProgressDto next)
        {
            PlayPendingLevelUps(); // 전장이 다시 보이는 시점에 재생(오버레이에 가려지지 않도록)

            // 이미 클리어한 스테이지를 수동 입장한 경우: 전진하지 않고 같은 스테이지를 반복(리셋 후 재시작).
            if (_repeatSameStage)
            {
                Debug.Log($"[Dungeon] 이미 클리어한 스테이지 반복 재입장 {_act}-{_difficulty}-{_stage}");
                EnterStage(_act, _difficulty, _stage, restartFromStart: true);
                return;
            }

            if (next == null)
            {
                return;
            }
            Debug.Log($"[Dungeon] 다음 스테이지 자동 입장 {next.act}-{next.difficulty}-{next.stage}");
            EnterStage(next.act, next.difficulty, next.stage);
        }

        /// <summary>이번 클리어에서 레벨업한 캐릭터들에게 글로우 이펙트를 재생한다.</summary>
        private void PlayPendingLevelUps()
        {
            if (_pendingLevelUps.Count == 0)
            {
                return;
            }
            foreach (var cid in _pendingLevelUps)
            {
                PlayLevelUpEffect(cid);
            }
            _pendingLevelUps.Clear();
        }

        /// <summary>지정 캐릭터(characterId)에 해당하는 전투 유닛 위치에 레벨업 글로우(스프라이트 시퀀스)를 1회 재생한다.
        /// 좌하단 피벗·큰 스프라이트를 몸통 중심에 맞춰 스케일·정렬하고, 재생이 끝나면 자동 파괴된다.</summary>
        private void PlayLevelUpEffect(int characterId)
        {
            if (levelUpFrames == null || levelUpFrames.Length == 0 || battle == null)
            {
                return;
            }

            PlayerCombatant target = null;
            foreach (var m in battle.Party)
            {
                if (m != null && m.CharacterId == characterId)
                {
                    target = m;
                    break;
                }
            }
            if (target == null)
            {
                return; // 파티에 없는 직업(예: 미구현)일 수 있음
            }

            var sp0 = levelUpFrames[0];
            if (sp0 == null)
            {
                return;
            }
            float spriteH = sp0.bounds.size.y;
            float scale = spriteH > 0.001f ? levelUpHeight / spriteH : 1f;

            var go = new GameObject("LevelUpGlow");
            go.SetActive(false); // 프레임 배정 후 활성화(OnEnable에서 Play 호출됨)
            go.transform.SetParent(target.transform, false);
            go.transform.localScale = Vector3.one * scale;
            // 좌하단 피벗 → 몸통 중심 정렬(스프라이트 로컬 중심만큼 역보정, 스케일 반영).
            Vector3 c = sp0.bounds.center;
            go.transform.localPosition = new Vector3(-c.x * scale, levelUpYOffset - c.y * scale, 0f);

            var sr = go.AddComponent<SpriteRenderer>();
            // 캐릭터는 SortingGroup + 다수 파트(정렬 범위 큼)이므로, 파트 최대 order보다 위에 두어 항상 앞에 표시.
            var charRenderers = target.GetComponentsInChildren<SpriteRenderer>(true);
            if (charRenderers != null && charRenderers.Length > 0)
            {
                int maxOrder = int.MinValue;
                foreach (var s in charRenderers)
                {
                    if (s == sr) { continue; }
                    if (s.sortingOrder > maxOrder) { maxOrder = s.sortingOrder; }
                }
                sr.sortingLayerID = charRenderers[0].sortingLayerID;
                sr.sortingOrder = (maxOrder == int.MinValue ? 0 : maxOrder) + levelUpSortingOffset;
            }

            var eff = go.AddComponent<SpriteSequenceEffect>();
            eff.frames = levelUpFrames;
            eff.fps = levelUpFps;
            eff.loop = false;
            eff.destroyOnFinish = true;
            go.SetActive(true);

            Debug.Log($"[Dungeon] 레벨업 글로우 재생 char={characterId}");
        }

        private void OnClearError(NetworkError error)
        {
            Debug.LogWarning($"[Dungeon] 클리어 실패: {error}");
            // 연출을 못 띄우더라도 슬로우모션은 반드시 복원한다.
            Time.timeScale = 1f;
        }
    }
}
