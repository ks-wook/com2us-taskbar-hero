using System;
using System.Collections.Generic;
using UnityEngine;
using TaskbarHero.Client.Managers;
using TaskbarHero.Common.Dto;
using TaskbarHero.Common.MasterData;   // Spawn(몬스터코드·등장 레벨·마리 수)

namespace TaskbarHero.Client.Battle
{
    /// <summary>
    /// 보스러시 도전 진행(보스러시 UI 기획서 4·5장). <b>별도 씬을 만들지 않고 GameScene의 전투 화면에서</b>
    /// 던전과 같은 <see cref="BattleDevController"/>로 5라운드를 이어서 진행한다.
    /// <para><b>라운드 사이에 회복·부활이 없다</b>(서버 기획서 2장) — 그래서 라운드 전환에는
    /// <see cref="BattleDevController.BeginServerBattle"/>(진입 때마다 전원 회복·부활)가 아니라
    /// 스폰 큐만 갈아 끼우는 <see cref="BattleDevController.QueueServerWave"/>를 쓴다.
    /// 첫 라운드만 <see cref="BattleDevController.RestartServerBattle"/>로 필드를 리셋하고 시작한다.</para>
    /// <para><b>라운드 전환은 포탈 이동</b>이다 — 보스를 처치하면 길 앞에 포탈(<see cref="PortalEffect"/>)이 열리고
    /// <b>파티가 그쪽으로 걸어간다</b>. 포탈에 <b>닿은 파티원부터 빨려 들어가듯 사라지고</b>, 전원이 들어가면
    /// 화면이 잠깐 덮이며, 그 사이
    /// <b>배경이 다음 라운드의 지역으로 바뀌고</b>(서버가 내려준 <c>backgroundType</c>) 파티가 그 지역 시작 지점에
    /// 나타난다. 체력·쿨다운은 그대로 이어진다.</para>
    /// <para><b>시간 측정은 클라이언트 권위</b>다. <c>Time.unscaledDeltaTime</c>을 누적하며(히트스톱·슬로우모션에
    /// 기록이 줄어들지 않게), 포탈 이동·결과/실패 연출·<c>clear</c> 응답 대기 구간에서는 멈춘다. 라운드별 소요는
    /// 같은 타이머에서 라운드 경계마다 스냅샷하므로 <b>합이 항상 보고 값(clearMs)과 일치</b>한다
    /// (불일치는 서버에서 <c>BossRushInvalidProgress(13006)</c>가 된다).</para>
    /// <para><b>제한 시간은 없다</b> — 타이머는 기록을 <b>재기만 하고 전투를 끊지 않는다</b>. 도전은
    /// 5라운드를 다 깨거나(완주) 파티가 전멸할 때 끝난다. 실패는 <b>서버에 아무것도 보내지 않는다</b> —
    /// 그 런은 서버가 보는 런 수명(<c>runExpireSec</c>)이 지나면 만료된다.</para>
    /// </summary>
    public class BossRushBattleFlow : MonoBehaviour
    {
        /// <summary>진행 단계.</summary>
        private enum Phase
        {
            Idle,        // 도전 중이 아님
            Fighting,    // 라운드 전투 중(타이머 작동)
            PortalWalk,  // 라운드 클리어 → 포탈로 걸어 들어가는 중(타이머 정지)
            Transition,  // 화면이 덮인 채 배경 교체·재배치 중
            Reporting,   // clear 응답 대기
            Finished,    // 결과/실패 연출 중
        }

        [SerializeField] private BattleDevController battle;
        [Tooltip("던전 전투 흐름. 몬스터 프리팹 표·배경·배너 프리팹을 공유하고, 도전이 끝나면 이 스테이지로 돌아간다.")]
        [SerializeField] private DungeonBattleFlow dungeon;

        [Header("포탈(라운드 전환)")]
        [Tooltip("포탈 애니메이션 프레임(Assets/Art/Effect/Object/Portal/TravelPortal, 에디터 빌더가 배선).")]
        [SerializeField] private Sprite[] portalFrames = new Sprite[0];
        [Tooltip("포탈 초당 프레임 수.")]
        [SerializeField] private float portalFps = 20f;
        [Tooltip("포탈 높이(월드 단위). 캐릭터보다 크게 잡아 '문'으로 읽히게 한다.")]
        [SerializeField] private float portalWorldHeight = 3.2f;
        [Tooltip("보스 처치 지점에서 포탈을 세울 거리(월드 단위, 파티 최전방 기준 오른쪽).")]
        [SerializeField] private float portalAheadDistance = 4f;

        [Header("라운드 배너 아트(에디터 빌더가 배선)")]
        [Tooltip("'ROUND' 명판(Assets/Art/UI/BossRush/boss_rush_round.png).")]
        [SerializeField] private Sprite roundWordSprite;
        [Tooltip("라운드 숫자 아트(Assets/Art/UI/BossRush/digit_N.png). 인덱스 0 = 1라운드.")]
        [SerializeField] private Sprite[] roundDigitSprites = new Sprite[0];
        [Tooltip("HUD 타이머용 픽셀 폰트(비우면 기본 UI 폰트).")]
        [SerializeField] private Font timerFont;

        // 포탈 진입·전환 타이밍(전부 unscaled).
        private const float PortalTouchGap = 0.3f;    // 파티원이 포탈 x에 이만큼 다가오면 "닿았다"로 본다
        private const float VanishSeconds = 0.22f;    // 닿은 파티원이 포탈로 빨려 들어가 사라지는 데 걸리는 시간
        private const float PortalWalkTimeout = 8f;   // 안전장치: 이 시간이 지나면 전원이 들어간 것으로 처리한다
        // 사라지는 연출 뒤 곧바로 넘어가므로 페이드는 짧게 둔다 — 걸어가서 사라지는 구간이 이동의 본 연출이다.
        private const float FadeOutSeconds = 0.22f;
        private const float FadeHoldSeconds = 0.08f;
        private const float FadeInSeconds = 0.35f;


        private Phase _phase = Phase.Idle;
        private long _runId;
        private List<BossRushRoundDto> _rounds = new List<BossRushRoundDto>();
        private int _roundIndex;                       // 0-based
        private float _elapsedSec;                     // 순수 전투 시간 누적(unscaled)
        private bool _timerRunning;
        private int _accountedMs;                      // 라운드 경계까지 이미 기록한 시간(라운드별 소요의 합)
        private readonly List<BossRushRoundTimeDto> _roundTimes = new List<BossRushRoundTimeDto>();

        private BossRushHud _hud;
        private PortalEffect _portal;
        private float _portalX;
        private float _portalWalkTimer;
        private readonly List<PortalVanish> _vanishing = new List<PortalVanish>();   // 포탈에 닿아 사라지는 중인 파티원

        // 도전 시작 시점의 던전 스테이지(끝나면 이 자리로 돌아간다).
        private int _returnAct = 1;
        private int _returnDifficulty = 1;
        private int _returnStage = 1;

        // 네트워크 오류 → 한글 문구 변환(UI 어셈블리가 소유한 매핑을 도전 시작 시 주입받는다).
        private Func<NetworkError, string> _errorText;

        /// <summary>도전이 진행 중인지 — 진입 화면의 [도전 시작] 비활성·스테이지 이동 차단 판정에 쓴다.</summary>
        public bool IsRunning => _phase != Phase.Idle;

        /// <summary>현재 라운드 번호(1~5). 도전 중이 아니면 0.</summary>
        public int CurrentRound => _phase == Phase.Idle ? 0 : RoundNumber(_roundIndex);

        /// <summary>씬에 배선된 보스러시 흐름을 찾는다(진입 화면·스테이지 UI가 상태를 물을 때 쓴다). 없으면 null.</summary>
        public static BossRushBattleFlow Find()
        {
            return FindAnyObjectByType<BossRushBattleFlow>();
        }

        /// <summary>
        /// <c>enter</c> 응답으로 도전을 시작한다 — 현재 던전 좌표를 기억해 두고 라운드 1을 필드 리셋 후 시작한다.
        /// <paramref name="errorText"/>는 <c>clear</c> 실패를 안내할 때 쓸 문구 변환(UI 어셈블리의 <c>ErrorMessages</c>)이다.
        /// </summary>
        public void StartRun(BossRushEnterResultData data, Func<NetworkError, string> errorText = null)
        {
            if (battle == null)
            {
                Debug.LogError("[BossRush] 전투 컨트롤러가 배선되지 않아 도전을 시작할 수 없습니다.");
                return;
            }
            if (data == null || data.rounds == null || data.rounds.Count == 0)
            {
                Debug.LogError("[BossRush] 도전 시작 응답에 라운드 구성이 없어 시작할 수 없습니다.");
                return;
            }
            if (IsRunning)
            {
                Debug.LogWarning("[BossRush] 이미 도전이 진행 중입니다.");
                return;
            }

            _errorText = errorText;
            _runId = data.runId;
            _rounds = SortedRounds(data.rounds);
            _roundIndex = 0;
            _elapsedSec = 0f;
            _accountedMs = 0;
            _roundTimes.Clear();
            _vanishing.Clear();

            if (dungeon != null)
            {
                _returnAct = dungeon.CurrentAct;
                _returnDifficulty = dungeon.CurrentDifficulty;
                _returnStage = dungeon.CurrentStage;
            }

            _hud = BossRushHud.Show(timerFont);
            Time.timeScale = 1f;   // 직전 전투의 슬로우모션이 남아 있을 수 있다
            Debug.Log($"[BossRush] 도전 시작 run={_runId} 라운드 {_rounds.Count}개");
            StartRound(0, restartField: true, applyBackground: true);
        }

        /// <summary>라운드 목록을 라운드 번호 오름차순으로 정렬해 복사한다(서버 응답 순서에 의존하지 않는다).</summary>
        private static List<BossRushRoundDto> SortedRounds(List<BossRushRoundDto> src)
        {
            var list = new List<BossRushRoundDto>();
            foreach (var r in src)
            {
                if (r != null)
                {
                    list.Add(r);
                }
            }
            list.Sort((a, b) => a.round.CompareTo(b.round));
            return list;
        }

        /// <summary>인덱스(0-based)에 해당하는 라운드 번호. 응답에 번호가 없으면 인덱스+1로 본다.</summary>
        private int RoundNumber(int index)
        {
            if (index < 0 || index >= _rounds.Count)
            {
                return index + 1;
            }
            return _rounds[index].round > 0 ? _rounds[index].round : index + 1;
        }

        /// <summary>
        /// 라운드를 시작한다 — 스폰 계획을 만들어 전투에 넘기고, 배너·진행도 바를 띄우고 타이머를 다시 돌린다.
        /// <paramref name="restartField"/>가 true면 전투 필드를 리셋하고(첫 라운드), false면 <b>회복 없이</b>
        /// 스폰 큐만 교체한다. <paramref name="applyBackground"/>가 false면 배경을 여기서 바꾸지 않는다
        /// (포탈 전환은 화면이 덮인 순간에 이미 바꿔 두기 때문).
        /// </summary>
        private void StartRound(int index, bool restartField, bool applyBackground)
        {
            if (index < 0 || index >= _rounds.Count)
            {
                return;
            }
            var r = _rounds[index];
            _roundIndex = index;

            var plan = new List<Spawn>();
            if (r.monsters != null)
            {
                foreach (var m in r.monsters)
                {
                    if (m == null || m.count <= 0)
                    {
                        continue;
                    }
                    plan.Add(new Spawn { monsterCode = m.monsterCode, monsterLevel = m.monsterLevel, count = m.count });
                }
            }
            int bossCode = r.boss != null ? r.boss.monsterCode : 0;
            if (bossCode != 0)
            {
                plan.Add(new Spawn { monsterCode = bossCode, monsterLevel = r.boss.monsterLevel, count = 1 });
            }

            if (restartField)
            {
                battle.RestartServerBattle(plan, ResolvePrefab, OnRoundCleared, bossCode, OnDefeat);
            }
            else
            {
                battle.QueueServerWave(plan, ResolvePrefab, OnRoundCleared, bossCode, OnDefeat);
            }

            StageProgressBar.Attach(battle,
                                    dungeon != null ? dungeon.StageProgressArrow : null,
                                    dungeon != null ? dungeon.StageProgressBoss : null);

            if (applyBackground)
            {
                ApplyRoundBackground(index);
            }
            ShowRoundBanner(index);

            _phase = Phase.Fighting;
            _timerRunning = true;
            Debug.Log($"[BossRush] ROUND {RoundNumber(index)} 시작 — 스폰 {plan.Count}종, 보스 {bossCode}");
        }

        /// <summary>그 라운드의 배경(서버가 내려준 <c>backgroundType</c>)으로 교체한다 — BGM도 그 지역 것으로 바뀐다.</summary>
        private void ApplyRoundBackground(int index)
        {
            if (dungeon == null || index < 0 || index >= _rounds.Count)
            {
                return;
            }
            dungeon.ApplyBackground(_rounds[index].backgroundType);
        }

        /// <summary>라운드 배너를 띄운다(스테이지 입장 배너 재사용). 문구가 아니라 <b>아트 조합</b>이다 —
        /// 'ROUND' 명판(<c>boss_rush_round</c>) 중앙 하단에 라운드 숫자 아트(<c>digit_N</c>)를 걸친다.
        /// 아트가 없으면 예전 문구 표기(<c>ROUND n / 5</c>)로 폴백한다.</summary>
        private void ShowRoundBanner(int index)
        {
            int round = RoundNumber(index);
            string fallbackTop = $"ROUND {round} / {_rounds.Count}";
            var digit = DigitSprite(round);
            // 첫 라운드 배너는 콘텐츠 진입 신호이므로 보스러시 시작음, 이후 라운드는 스테이지 입장음을 쓴다.
            SoundId sfx = index == 0 ? SoundId.BossRushStart : SoundId.StageEnter;

            var prefab = dungeon != null ? dungeon.StageEnterBannerPrefab : null;
            if (prefab != null)
            {
                var go = Instantiate(prefab);
                GameViewLayout.ApplyCurrentScalers(go);
                var banner = go.GetComponent<StageEnterBanner>();
                if (banner != null)
                {
                    banner.PlayRoundArt(roundWordSprite, digit, fallbackTop, sfx);
                    return;
                }
                Destroy(go); // StageEnterBanner가 없는 잘못된 프리팹 → 폴백
            }

            var fallback = new GameObject("BossRushRoundBanner").AddComponent<StageEnterBanner>();
            fallback.PlayRoundArt(roundWordSprite, digit, fallbackTop, sfx);
        }

        /// <summary>라운드 번호(1-based)에 해당하는 숫자 아트. 배선되지 않았거나 범위를 벗어나면 null(문구 폴백).</summary>
        private Sprite DigitSprite(int round)
        {
            int i = round - 1;
            return i >= 0 && i < roundDigitSprites.Length ? roundDigitSprites[i] : null;
        }

        /// <summary>몬스터 코드 → 프리팹(던전 전투가 들고 있는 표를 그대로 쓴다).</summary>
        private GameObject ResolvePrefab(int code)
        {
            return dungeon != null ? dungeon.ResolvePrefab(code) : null;
        }

        // ── 진행 ──

        private void Update()
        {
            if (_phase == Phase.Idle)
            {
                return;
            }

            if (_timerRunning)
            {
                _elapsedSec += Time.unscaledDeltaTime;
            }
            UpdateHud();


            if (_phase == Phase.PortalWalk)
            {
                TickPortalWalk();
            }
        }

        /// <summary>HUD에 현재 라운드·경과를 넘긴다.</summary>
        private void UpdateHud()
        {
            if (_hud == null)
            {
                return;
            }
            _hud.SetState(RoundNumber(_roundIndex), _rounds.Count, ElapsedMs());
        }

        /// <summary>지금까지의 순수 전투 시간(ms).</summary>
        private int ElapsedMs()
        {
            return Mathf.RoundToInt(_elapsedSec * 1000f);
        }

        /// <summary>라운드 전멸 콜백 — 타이머를 멈추고 라운드 소요를 확정한 뒤, 마지막이면 보고하고 아니면 포탈을 연다.</summary>
        private void OnRoundCleared()
        {
            if (_phase != Phase.Fighting)
            {
                return;
            }
            _timerRunning = false;

            // 라운드 소요 = (지금까지 경과) − (앞 라운드까지 기록한 합). 이렇게 스냅샷하면 합이 언제나 총 기록과 같다.
            int now = ElapsedMs();
            int roundMs = now - _accountedMs;
            _roundTimes.Add(new BossRushRoundTimeDto { round = RoundNumber(_roundIndex), elapsedMs = roundMs });
            _accountedMs = now;
            Debug.Log($"[BossRush] ROUND {RoundNumber(_roundIndex)} 클리어 — 소요 {roundMs}ms, 누적 {now}ms");

            if (_roundIndex >= _rounds.Count - 1)
            {
                ReportClear();
                return;
            }
            OpenPortal();
        }

        /// <summary>
        /// 보스를 처치한 자리 앞에 포탈을 세우고 <b>파티가 그쪽으로 걸어가게</b> 한다.
        /// <para>마지막 한 마리를 잡은 직후에는 전투 컨트롤러가 교전 상태 그대로여서 파티가 제자리에 서 있으므로,
        /// <see cref="BattleDevController.ResumeAdvance"/>로 전진을 재개해야 포탈까지 걸어간다.</para>
        /// </summary>
        private void OpenPortal()
        {
            _phase = Phase.PortalWalk;
            _portalWalkTimer = 0f;
            _vanishing.Clear();

            _portalX = battle.PartyFrontX + Mathf.Max(1f, portalAheadDistance);
            _portal = PortalEffect.Spawn(portalFrames, new Vector3(_portalX, battle.PathY, 0f),
                                         portalWorldHeight, portalFps);
            if (_portal != null)
            {
                SoundManager.Sfx(SoundId.PortalOpen);   // 포탈이 열리는 순간(라운드 클리어 신호도 겸한다)
            }
            battle.ResumeAdvance();   // 적이 없어도 계속 걷게 한다(포탈까지 이동하는 연출)
            if (_portal == null)
            {
                // 프레임이 배선되지 않았으면 연출 없이 곧바로 전환한다(진행이 막히지 않게).
                BeginTransition();
            }
        }

        /// <summary>
        /// 파티가 포탈까지 걸어가는 구간 — <b>포탈에 닿은 파티원부터 그 자리에서 빨려 들어가듯 사라지고</b>
        /// (<see cref="VanishSeconds"/>), <b>전원이 들어가면 곧바로</b> 다음 라운드로 넘어간다.
        /// <para>사라지는 것은 <b>보이기만 감추는</b> 처리다 — 오브젝트를 파괴하지 않고 크기·색만 눌러 두었다가
        /// 다음 지역에서 되돌리므로 <b>체력·쿨다운·스킬 상태가 그대로 이어진다</b>.</para>
        /// <para>안전장치: <see cref="PortalWalkTimeout"/>이 지나도록 닿지 못하면 그대로 넘어간다.</para>
        /// </summary>
        private void TickPortalWalk()
        {
            _portalWalkTimer += Time.unscaledDeltaTime;
            float dt = Time.unscaledDeltaTime;

            bool allGone = true;
            var party = battle.Party;
            for (int i = 0; i < party.Count; i++)
            {
                var m = party[i];
                if (m == null || !m.Alive)
                {
                    continue;
                }
                var v = FindVanish(m);
                if (v == null)
                {
                    if (m.transform.position.x < _portalX - PortalTouchGap)
                    {
                        allGone = false;   // 아직 걸어오는 중
                        continue;
                    }
                    if (_vanishing.Count == 0)
                    {
                        // 흡입음은 첫 파티원이 닿을 때 한 번만 — 4명마다 울리면 같은 소리가 겹쳐 뭉갠다.
                        SoundManager.Sfx(SoundId.PortalTravel);
                    }
                    v = new PortalVanish(m);   // 포탈에 닿았다 — 지금부터 사라진다
                    _vanishing.Add(v);
                }
                if (!v.Tick(dt))
                {
                    allGone = false;   // 사라지는 중
                }
            }

            if (allGone || _portalWalkTimer >= PortalWalkTimeout)
            {
                BeginTransition();
            }
        }

        /// <summary>그 파티원이 이미 사라지는 중인지 찾는다(아니면 null).</summary>
        private PortalVanish FindVanish(PlayerCombatant m)
        {
            for (int i = 0; i < _vanishing.Count; i++)
            {
                if (_vanishing[i].Owner == m)
                {
                    return _vanishing[i];
                }
            }
            return null;
        }

        /// <summary>화면을 덮었다 여는 전환을 시작한다 — 덮인 순간에 배경 교체·파티 재배치를, 다 열린 뒤 다음 라운드를 시작한다.</summary>
        private void BeginTransition()
        {
            if (_phase == Phase.Transition)
            {
                return;
            }
            _phase = Phase.Transition;
            BattleScreenFade.Play(FadeOutSeconds, FadeHoldSeconds, FadeInSeconds,
                                  onCovered: EnterNextRegion,
                                  onFinished: () => StartRound(_roundIndex + 1, restartField: false, applyBackground: false));
        }

        /// <summary>화면이 덮인 순간: 포탈을 치우고 파티를 되돌린 뒤 다음 라운드 지역으로 배경을 바꾼다.
        /// <b>체력·쿨다운은 건드리지 않는다</b> — 보스러시는 라운드 사이에 회복하지 않는다.</summary>
        private void EnterNextRegion()
        {
            if (_portal != null)
            {
                Destroy(_portal.gameObject);
                _portal = null;
            }
            RestoreVanished();   // 포탈로 사라진 파티원을 다음 지역에서 다시 보이게 한다(상태는 그대로)
            battle.RelocateParty(battle.PartySpawnPoint);   // 다음 지역 시작 지점(카메라도 함께 스냅)
            ApplyRoundBackground(_roundIndex + 1);          // 배경 타일은 재배치된 카메라 기준으로 다시 만들어진다
        }

        // ── 종결 ──

        /// <summary>5라운드 완주 — 클리어 시간을 보고하고 결과 연출을 띄운다.</summary>
        private void ReportClear()
        {
            _phase = Phase.Reporting;
            _timerRunning = false;

            // 마지막 보스 처치 순간의 슬로우모션(응답 대기 동안 극적 정지감). 오버레이가 닫힐 때 1로 복원된다.
            float slow = dungeon != null ? dungeon.ClearSlowMotionScale : 0.25f;
            Time.timeScale = Mathf.Clamp(slow, 0.01f, 1f);

            int clearMs = _accountedMs;
            Debug.Log($"[BossRush] 완주 — run={_runId} clearMs={clearMs} 라운드 {_roundTimes.Count}개");

            if (NetworkManager.Instance == null || !Session.IsLoggedIn)
            {
                ShowResult(new BossRushClearResultData { runId = _runId, clearMs = clearMs, rank = 0 });
                return;
            }

            var request = new BossRushClearRequest
            {
                userId = Session.UserId,
                token = Session.Token,
                data = new BossRushClearData
                {
                    runId = _runId,
                    clearMs = clearMs,
                    rounds = new List<BossRushRoundTimeDto>(_roundTimes),
                },
            };
            NetworkManager.Instance.PostToGame<BossRushClearResponse>("/api/game/boss-rush/clear", request,
                resp =>
                {
                    if (this == null) return;
                    ShowResult(resp != null ? resp.data : null);
                },
                error =>
                {
                    if (this == null) return;
                    Debug.LogError($"[BossRush] 클리어 보고 실패: {error} (clearMs={clearMs}, rounds={_roundTimes.Count})");
                    ShowModal("보스 러시", _errorText != null ? _errorText(error) : "도전 기록을 등록하지 못했습니다.");
                    // 기록이 등재되지 않아도 도전 자체는 끝난 것이므로 결과는 보여 주고 던전으로 돌아간다.
                    ShowResult(new BossRushClearResultData { runId = _runId, clearMs = clearMs, rank = 0 });
                });
        }

        /// <summary>결과 오버레이를 띄우고, 닫히면 도전을 마치고 던전으로 돌아간다.</summary>
        private void ShowResult(BossRushClearResultData data)
        {
            _phase = Phase.Finished;
            var shown = data ?? new BossRushClearResultData { runId = _runId, clearMs = _accountedMs };
            BossRushResultOverlay.Show(shown, new List<BossRushRoundTimeDto>(_roundTimes), () => EndRun(returnToDungeon: true));
        }

        /// <summary>아군 전멸 — 실패 처리(유일한 실패 경로다. 서버에는 아무것도 보내지 않는다).</summary>
        private void OnDefeat()
        {
            if (_phase == Phase.Idle || _phase == Phase.Finished)
            {
                return;
            }
            FailRun("보스 러시 도전 실패", $"ROUND {RoundNumber(_roundIndex)}에서 쓰러졌습니다");
        }

        /// <summary>도전 실패(파티 전멸) — 전투를 끊고 패배 연출을 띄운 뒤 던전으로 돌아간다.
        /// <b>서버에 보고하지 않는다</b> — 그 런은 런 수명이 지나면 서버가 만료로 처리한다(서버 기획서 6.2).</summary>
        private void FailRun(string headline, string hint)
        {
            _timerRunning = false;
            _phase = Phase.Finished;

            if (_portal != null)
            {
                Destroy(_portal.gameObject);
                _portal = null;
            }
            RestoreVanished();            // 포탈 연출 중 실패해도 파티가 보이지 않는 채로 남지 않게 한다
            battle.AbortServerBattle();   // 남은 웨이브·적을 정리한다
            float slow = dungeon != null ? dungeon.ClearSlowMotionScale : 0.25f;
            Time.timeScale = Mathf.Clamp(slow, 0.01f, 1f);

            Debug.Log($"[BossRush] 도전 실패({headline}) — run={_runId}, ROUND {RoundNumber(_roundIndex)}, 경과 {ElapsedMs()}ms");
            SoundManager.Jingle(SoundId.JingleDefeat);
            BattleDefeatOverlay.Show(headline, hint, () => EndRun(returnToDungeon: true));
        }

        /// <summary>도전을 마친다 — HUD·포탈을 정리하고, 기억해 둔 던전 스테이지로 복귀해 방치 전투를 이어 간다.</summary>
        private void EndRun(bool returnToDungeon)
        {
            _phase = Phase.Idle;
            _timerRunning = false;
            if (_hud != null)
            {
                _hud.Close();
                _hud = null;
            }
            if (_portal != null)
            {
                Destroy(_portal.gameObject);
                _portal = null;
            }
            RestoreVanished();
            Time.timeScale = 1f;

            if (returnToDungeon && dungeon != null)
            {
                Debug.Log($"[BossRush] 던전 복귀 {_returnAct}-{_returnDifficulty}-{_returnStage}");
                dungeon.EnterSelectedStage(_returnAct, _returnDifficulty, _returnStage);
            }
        }

        /// <summary>포탈로 사라진 파티원들의 크기·색을 원래대로 되돌린다(다음 지역에 다시 나타나는 시점).</summary>
        private void RestoreVanished()
        {
            for (int i = 0; i < _vanishing.Count; i++)
            {
                _vanishing[i].Restore();
            }
            _vanishing.Clear();
        }

        /// <summary>안내·오류는 공용 모달로 띄운다(패널과 같은 규칙).</summary>
        private static void ShowModal(string title, string message)
        {
            if (ModalManager.Instance != null)
            {
                ModalManager.Instance.ShowConfirm(title, message);
            }
            else
            {
                Debug.Log($"[BossRush] {title}: {message}");
            }
        }

        /// <summary>
        /// 포탈에 닿아 <b>빨려 들어가듯 사라지는</b> 파티원 1명의 연출 상태.
        /// <para>발밑을 축으로 <b>줄어들면서 함께 옅어진다</b> — 캐릭터의 원점이 발이라 스케일을 줄이면
        /// 포탈 바닥으로 빨려 드는 것처럼 보인다. 좌우 반전(바라보는 방향)이 유지되도록 원래 스케일에 비례해 줄인다.</para>
        /// <para><b>오브젝트를 파괴하지 않는다</b> — 파괴하면 그 파티원이 전사한 것으로 처리되어 다음 라운드에
        /// 돌아오지 못한다. 원래 크기·색을 기억해 두었다가 <see cref="Restore"/>로 되돌린다.</para>
        /// </summary>
        private class PortalVanish
        {
            public readonly PlayerCombatant Owner;

            private readonly Transform _tr;
            private readonly Vector3 _baseScale;
            private readonly SpriteRenderer[] _renderers;
            private readonly Color[] _colors;
            private float _t;

            public PortalVanish(PlayerCombatant owner)
            {
                Owner = owner;
                _tr = owner.transform;
                _baseScale = _tr.localScale;
                _renderers = owner.GetComponentsInChildren<SpriteRenderer>(true);
                _colors = new Color[_renderers.Length];
                for (int i = 0; i < _renderers.Length; i++)
                {
                    _colors[i] = _renderers[i] != null ? _renderers[i].color : Color.white;
                }
            }

            /// <summary>연출을 진행한다. 완전히 사라졌으면 true.</summary>
            public bool Tick(float dt)
            {
                _t += dt;
                float k = Mathf.Clamp01(_t / VanishSeconds);
                if (_tr != null)
                {
                    _tr.localScale = _baseScale * (1f - k);
                }
                for (int i = 0; i < _renderers.Length; i++)
                {
                    var sr = _renderers[i];
                    if (sr == null) continue;
                    var c = _colors[i];
                    sr.color = new Color(c.r, c.g, c.b, c.a * (1f - k));
                }
                return k >= 1f;
            }

            /// <summary>크기·색을 원래대로 되돌린다.</summary>
            public void Restore()
            {
                if (_tr != null)
                {
                    _tr.localScale = _baseScale;
                }
                for (int i = 0; i < _renderers.Length; i++)
                {
                    if (_renderers[i] != null)
                    {
                        _renderers[i].color = _colors[i];
                    }
                }
            }
        }
    }
}
