using System.Collections.Generic;
using UnityEngine;
using TaskbarHero.Client.Managers;
using TaskbarHero.Common.Dto;
using TaskbarHero.Common.MasterData;   // Spawn(몬스터코드·등장 레벨·마리 수) — 스폰 플랜에 그대로 쓴다

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

        [Header("입장 연출")]
        [Tooltip("스테이지 입장 시 띄우는 배너 프리팹(Assets/Prefabs/UI/StageEnterBanner, 에디터 빌더가 배선). " +
                 "미배선 시 코드로 생성해 폴백한다.")]
        [SerializeField] private GameObject stageEnterBannerPrefab;

        [Header("클리어 연출")]
        [Tooltip("클리어 순간 게임 속도(슬로우모션). 0~1, 예: 0.25")]
        [SerializeField] private float clearSlowMotionScale = 0.25f;

        [Header("스테이지 진행도 바")]
        [Tooltip("현재 진행 위치를 가리키는 화살표(Assets/Art/UI/straight_up.png, 에디터 빌더가 배선).")]
        [SerializeField] private Sprite stageProgressArrow;
        [Tooltip("바 우측 끝의 목표(보스) 아이콘(Assets/Art/UI/boss.png, 에디터 빌더가 배선).")]
        [SerializeField] private Sprite stageProgressBoss;

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
        [Tooltip("레벨업 시 캐릭터 위에 띄우는 'LEVEL UP!' 배너 이미지(Assets/Art/UI/System/레벨업.png, 에디터 빌더가 배선).")]
        [SerializeField] private Sprite levelUpBanner;
        [Tooltip("레벨업 배너 월드 너비(유닛). 이미지를 이 너비에 맞춰 스케일한다.")]
        [SerializeField] private float levelUpBannerWidth = 2.6f;
        [Tooltip("레벨업 배너를 캐릭터 기준 위로 올리는 y 오프셋(이미지 중심 기준).")]
        [SerializeField] private float levelUpBannerYOffset = 2.4f;

        private readonly Dictionary<int, GameObject> _prefabByCode = new Dictionary<int, GameObject>();
        private int _act = 1;
        private int _difficulty = 1;
        private int _stage = 1;
        private bool _entered;
        private bool _cleared;
        private bool _defeated;        // 패배 처리 1회 가드(재입장 시 해제)
        private float _stageStartUnscaledTime; // 이 스테이지 전투 시작 시각(패배 보고 elapsedMs 계산용, 슬로우모션 무관)
        private bool _restartOnEnter; // true면 다음 입장 응답에서 전투 필드를 리셋하고 처음부터 시작
        private bool _repeatSameStage; // true면 클리어 후 같은 스테이지를 반복(이미 클리어한 스테이지 수동 입장), false면 다음 스테이지로 전진
        private readonly List<int> _pendingLevelUps = new List<int>(); // 이번 클리어에서 레벨업한 캐릭터 id(오버레이 종료 후 글로우 재생)

        // 지역당 스테이지 수(난이도1 기준 클리어 시퀀스 계산용 — 스테이지 UI/서버 규칙과 동일).
        // 서버 GameServer/MasterData/StageCoords.StagesPerAct(=10)와 같은 값이어야 프론티어 판정이 맞는다.
        private const int StagesPerRegion = 10;

        /// <summary>현재 진행 중인 스테이지 좌표(외부 관찰/디버그용).</summary>
        public int CurrentAct => _act;
        public int CurrentDifficulty => _difficulty;
        public int CurrentStage => _stage;

        // ── 보스러시가 공유하는 참조(같은 아트·프리팹을 씬에 두 벌 배선하지 않는다) ──

        /// <summary>스테이지 입장 배너 프리팹(보스러시 라운드 배너도 이것을 쓴다).</summary>
        public GameObject StageEnterBannerPrefab => stageEnterBannerPrefab;
        /// <summary>진행도 바의 현재 위치 화살표 스프라이트.</summary>
        public Sprite StageProgressArrow => stageProgressArrow;
        /// <summary>진행도 바 우측 끝의 목표(보스) 아이콘 스프라이트.</summary>
        public Sprite StageProgressBoss => stageProgressBoss;
        /// <summary>클리어 순간 슬로우모션 배율(보스러시 완주 연출도 같은 값을 쓴다).</summary>
        public float ClearSlowMotionScale => clearSlowMotionScale;

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
            // '첫 클리어' 판정은 스테이지 UI·서버와 동일하게 클리어 시퀀스로 한다(수동 입장이어도 일관되게 전진 판단).
            // 난이도1 기준 시퀀스 = (지역-1)×StagesPerRegion + 스테이지.
            //  · seq ≤ maxStageCleared      → 이미 클리어한 스테이지 → 클리어해도 전진하지 않고 반복.
            //  · seq == maxStageCleared + 1  → 최초로 깨는(프론티어) 스테이지 → 수동 입장이어도 클리어 시 다음 스테이지로 전진.
            var player = Session.GameData != null ? Session.GameData.player : null;
            int maxCleared = player != null ? player.maxStageCleared : 0;
            int seq = (act - 1) * StagesPerRegion + stage;
            _repeatSameStage = seq <= maxCleared; // 이미 클리어한 스테이지만 반복. 첫 클리어는 다음 스테이지로 입장 요청.

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

            // 스폰 플랜에는 <b>등장 레벨</b>을 함께 싣는다 — monster_master의 hp·attack은 레벨 1 기준값이고,
            // 실제 전투 스탯은 클라이언트가 레벨 배율을 곱해 만든다(마스터 데이터 값 §9.4).
            // 서버는 코드·레벨·마리 수만 내려준다(스테이지/전투 결과 기획서 5.1).
            var plan = new List<Spawn>();
            if (d.monsters != null)
            {
                foreach (var m in d.monsters)
                {
                    plan.Add(new Spawn { monsterCode = m.monsterCode, monsterLevel = m.monsterLevel, count = m.count });
                }
            }
            int bossCode = d.boss != null ? d.boss.monsterCode : 0;
            if (bossCode != 0)
            {
                plan.Add(new Spawn { monsterCode = bossCode, monsterLevel = d.boss.monsterLevel, count = 1 });
            }

            int total = 0;
            foreach (var sp in plan)
            {
                total += sp.count;
            }
            _stageStartUnscaledTime = Time.unscaledTime; // 패배 보고의 elapsedMs 기준점(진입 응답으로 전투를 시작하는 시점)
            ShowEnterBanner(d.act, d.difficulty, d.stage); // 상단 중앙 입장 배너(페이드 인/아웃)
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
            // 우측 하단 스테이지 진행도 바(0% → 처치마다 상승 → 클리어 100%) + 진행 위치 화살표·보스 목표 아이콘.
            StageProgressBar.Attach(battle, stageProgressArrow, stageProgressBoss);
            // 전투 필드 리셋(카메라 위치 확정) 후 배경을 구축해야 스크롤 배경 타일이 올바른 위치에 생성된다.
            ApplyBackground(d.backgroundType);
        }

        /// <summary>스테이지 입장 배너를 띄운다. 프리팹이 배선돼 있으면 Instantiate해 재생하고,
        /// 없으면 코드로 생성하는 폴백(<see cref="StageEnterBanner.Show"/>)을 사용한다.</summary>
        private void ShowEnterBanner(int act, int difficulty, int stage)
        {
            if (stageEnterBannerPrefab != null)
            {
                var go = Instantiate(stageEnterBannerPrefab);
                // 프리팹에 구워진 기본 규격 캔버스를 현재 씬 규격으로 즉시 맞춘다(첫 표시 때 크게 그려지는 것 방지).
                GameViewLayout.ApplyCurrentScalers(go);
                var banner = go.GetComponent<StageEnterBanner>();
                if (banner != null)
                {
                    banner.Play(act, difficulty, stage);
                    return;
                }
                Destroy(go); // StageEnterBanner가 없는 잘못된 프리팹 → 폴백
            }
            StageEnterBanner.Show(act, difficulty, stage);
        }

        private void OnEnterError(NetworkError error)
        {
            Debug.LogWarning($"[Dungeon] 입장 실패: {error}");
        }

        /// <summary>backgroundType(1~5)에 맞는 전투 배경으로 교체한다(BGM도 그 Act 것으로 바꾼다).
        /// 보스러시 라운드 전환(<see cref="BossRushBattleFlow"/>)도 같은 배경 교체를 쓴다.</summary>
        public void ApplyBackground(int backgroundType)
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
            PlayActBgm(backgroundType);
        }

        /// <summary>배경 타입(1~5 = Act)에 대응하는 전투 BGM으로 바꾼다. 같은 곡이면 SoundManager가 무시하므로
        /// 같은 Act 안에서 스테이지를 넘겨도 음악이 끊기지 않는다.</summary>
        public static void PlayActBgm(int backgroundType)
        {
            SoundId id = backgroundType switch
            {
                1 => SoundId.BgmBattleAct1,
                2 => SoundId.BgmBattleAct2,
                3 => SoundId.BgmBattleAct3,
                4 => SoundId.BgmBattleAct4,
                5 => SoundId.BgmBattleAct5,
                _ => SoundId.BgmBattleAct1,
            };
            SoundManager.Bgm(id);
        }

        /// <summary>코드로 몬스터 프리팹을 조회(없으면 null → 컨트롤러가 폴백/건너뜀).
        /// 보스러시도 <b>같은 표</b>를 쓰므로(씬에 프리팹 목록을 두 벌 두지 않는다) 공개한다.</summary>
        public GameObject ResolvePrefab(int code)
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
            SoundManager.Jingle(SoundId.JingleDefeat);
            ReportFail();
            BattleDefeatOverlay.Show(() => EnterStage(_act, _difficulty, _stage, restartFromStart: true));
        }

        /// <summary>패배(파티 전멸)를 서버에 보고한다(<c>stage/fail</c>). 진행도·보상은 바뀌지 않고 기록만 남으므로
        /// 응답을 기다리지 않고 패배 연출을 그대로 진행한다 — 재입장 요청보다 먼저 보내 순서를 지킨다.
        /// <para>어떻게 졌는지(경과 시간·남은 적 수·보스 도달)를 함께 실어 난이도 집계에 쓰이게 한다.</para></summary>
        private void ReportFail()
        {
            if (NetworkManager.Instance == null || !Session.IsLoggedIn)
            {
                return;
            }

            int elapsedMs = Mathf.Max(0, Mathf.RoundToInt((Time.unscaledTime - _stageStartUnscaledTime) * 1000f));
            int remaining = battle != null ? battle.ServerRemainingMonsterCount : 0;
            bool reachedBoss = battle != null && battle.ServerBossReached;

            var request = new StageFailRequest
            {
                userId = Session.UserId,
                token = Session.Token,
                data = new StageFailData
                {
                    act = _act,
                    difficulty = _difficulty,
                    stage = _stage,
                    elapsedMs = elapsedMs,
                    remainingMonsterCount = remaining,
                    reachedBoss = reachedBoss,
                },
            };
            Debug.Log($"[Dungeon] 실패 보고 {_act}-{_difficulty}-{_stage} — {elapsedMs}ms, 남은 적 {remaining}, 보스도달 {reachedBoss}");
            NetworkManager.Instance.PostToGame<StageFailResponse>("/api/game/stage/fail", request, OnFailReported, OnFailError);
        }

        /// <summary>실패 보고 성공: 기록만 남는 요청이라 게임 상태는 건드리지 않고 접수 결과만 로그로 남긴다.</summary>
        private void OnFailReported(StageFailResponse response)
        {
            var d = response != null ? response.data : null;
            if (d != null)
            {
                Debug.Log($"[Dungeon] 실패 보고 접수 {d.act}-{d.difficulty}-{d.stage} (stageId={d.stageId}, failedAt={d.failedAt})");
            }
        }

        /// <summary>실패 보고 실패: 보고는 집계용이라 실패해도 패배 연출·재시작 흐름을 막지 않는다(경고만).</summary>
        private void OnFailError(NetworkError error)
        {
            Debug.LogWarning($"[Dungeon] 실패 보고 실패: {error}");
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

            // 전리품 적재·골드 증가는 응답이 그대로 알려주므로 캐시에 반영해 둔다(§5.0 규약) —
            // 가방을 이미 받아 둔 상태면 창고를 열기 전에 최신이 되고, 아직 안 받았으면 열 때 조회된다.
            if (d != null)
            {
                Session.ApplyInventoryDelta(d.inventoryDelta);
                Session.ApplyBalance(d.balance);
            }

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

        /// <summary>지정 캐릭터(characterId)에 해당하는 전투 유닛 위치에 레벨업 글로우(스프라이트 시퀀스)와
        /// "LEVEL UP!" 배너 이미지를 1회 재생한다. 좌하단 피벗·큰 스프라이트를 몸통 중심에 맞춰 스케일·정렬하고,
        /// 재생이 끝나면 각각 자동 파괴된다.</summary>
        private void PlayLevelUpEffect(int characterId)
        {
            // 레벨업음은 글로우/배너 배선과 무관하게 울린다(사운드 정의서 §5.1).
            SoundManager.Sfx(SoundId.LevelUp);

            if (battle == null)
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

            // 캐릭터는 SortingGroup + 다수 파트(정렬 범위 큼)이므로, 파트 최대 order보다 위에 두어 항상 앞에 표시.
            int baseOrder = levelUpSortingOffset;
            int sortingLayerId = 0;
            var charRenderers = target.GetComponentsInChildren<SpriteRenderer>(true);
            if (charRenderers != null && charRenderers.Length > 0)
            {
                int maxOrder = int.MinValue;
                foreach (var s in charRenderers)
                {
                    if (s.sortingOrder > maxOrder) { maxOrder = s.sortingOrder; }
                }
                sortingLayerId = charRenderers[0].sortingLayerID;
                baseOrder = (maxOrder == int.MinValue ? 0 : maxOrder) + levelUpSortingOffset;
            }

            bool glowPlayed = PlayLevelUpGlow(target, sortingLayerId, baseOrder);

            // 글로우와 함께 캐릭터 위에 "LEVEL UP!" 배너 이미지를 띄운다(글로우보다 위·앞에 표시).
            LevelUpBanner.Spawn(target.transform, levelUpBanner, levelUpBannerYOffset, levelUpBannerWidth,
                                sortingLayerId, baseOrder + 1);

            Debug.Log($"[Dungeon] 레벨업 연출 재생 char={characterId} 글로우={glowPlayed} 배너={levelUpBanner != null}");
        }

        /// <summary>레벨업 글로우(스프라이트 시퀀스)를 캐릭터 몸통 중심에 정렬해 1회 재생한다(재생 후 자동 파괴).
        /// 프레임이 배선되지 않았으면 아무것도 하지 않고 false를 반환한다.</summary>
        private bool PlayLevelUpGlow(PlayerCombatant target, int sortingLayerId, int sortingOrder)
        {
            if (levelUpFrames == null || levelUpFrames.Length == 0 || levelUpFrames[0] == null)
            {
                return false;
            }

            var sp0 = levelUpFrames[0];
            float spriteH = sp0.bounds.size.y;
            float scale = spriteH > 0.001f ? levelUpHeight / spriteH : 1f;

            var go = new GameObject("LevelUpGlow");
            go.SetActive(false); // 프레임 배정 후 활성화(OnEnable에서 Play 호출됨)
            go.transform.SetParent(target.transform, false);

            // 캐릭터는 바라보는 방향을 localScale.x 부호로 표현하므로(오른쪽=음수), 자식으로 붙이면 이펙트도 좌우 반전된다.
            // 부모의 x 부호를 상쇄해 항상 정방향으로 그린다.
            float faceSign = target.transform.lossyScale.x < 0f ? -1f : 1f;
            go.transform.localScale = new Vector3(scale * faceSign, scale, scale);
            // 좌하단 피벗 → 몸통 중심 정렬(스프라이트 로컬 중심만큼 역보정, 스케일·반전 반영).
            Vector3 c = sp0.bounds.center;
            go.transform.localPosition = new Vector3(-c.x * scale * faceSign, levelUpYOffset - c.y * scale, 0f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingLayerID = sortingLayerId;
            sr.sortingOrder = sortingOrder;

            var eff = go.AddComponent<SpriteSequenceEffect>();
            eff.frames = levelUpFrames;
            eff.fps = levelUpFps;
            eff.loop = false;
            eff.destroyOnFinish = true;
            go.SetActive(true);
            return true;
        }

        private void OnClearError(NetworkError error)
        {
            Debug.LogWarning($"[Dungeon] 클리어 실패: {error}");
            // 연출을 못 띄우더라도 슬로우모션은 반드시 복원한다.
            Time.timeScale = 1f;
        }
    }
}
