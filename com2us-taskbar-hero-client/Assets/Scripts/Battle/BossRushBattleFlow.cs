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
    /// <para><b>라운드 전환은 포탈 이동</b>이다 — 보스를 처치하면 길 앞에 포탈(<see cref="PortalEffect"/>)이 생기고,
    /// 파티가 걸어 들어가면 화면이 잠깐 덮이는 사이 <b>배경이 다음 라운드의 지역으로 바뀌고</b>(서버가 내려준
    /// <c>backgroundType</c>) 파티가 그 지역 시작 지점에 나타난다. 체력·쿨다운은 그대로 이어진다.</para>
    /// <para><b>시간 측정은 클라이언트 권위</b>다. <c>Time.unscaledDeltaTime</c>을 누적하며(히트스톱·슬로우모션에
    /// 기록이 줄어들지 않게), 포탈 이동·결과/실패 연출·<c>clear</c> 응답 대기 구간에서는 멈춘다. 라운드별 소요는
    /// 같은 타이머에서 라운드 경계마다 스냅샷하므로 <b>합이 항상 보고 값(clearMs)과 일치</b>한다
    /// (불일치는 서버에서 <c>BossRushInvalidProgress(13006)</c>가 된다).</para>
    /// <para>실패(전멸·시간 초과)는 <b>서버에 아무것도 보내지 않는다</b> — 그 런은 제한 시간이 지나 만료된다.</para>
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

        // 포탈 진입·전환 타이밍(전부 unscaled).
        private const float AbsorbEnterGap = 0.35f;   // 포탈 x에 이만큼 다가오면 그 멤버가 빨려 들어가기 시작
        private const float AbsorbSeconds = 0.40f;    // 한 멤버가 사라지는 데 걸리는 시간
        private const float PortalWalkTimeout = 8f;   // 안전장치: 이 시간이 지나면 걸어 들어간 것으로 본다
        private const float FadeOutSeconds = 0.35f;
        private const float FadeHoldSeconds = 0.15f;
        private const float FadeInSeconds = 0.45f;

        private const int DefaultTimeLimitMs = 600000;   // 마스터 미로드 시 폴백(10분)

        private Phase _phase = Phase.Idle;
        private long _runId;
        private int _timeLimitMs = DefaultTimeLimitMs;
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
        private readonly List<AbsorbState> _absorbing = new List<AbsorbState>();

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
            _timeLimitMs = data.timeLimitMs > 0 ? data.timeLimitMs : DefaultTimeLimitMs;
            _rounds = SortedRounds(data.rounds);
            _roundIndex = 0;
            _elapsedSec = 0f;
            _accountedMs = 0;
            _roundTimes.Clear();
            _absorbing.Clear();

            if (dungeon != null)
            {
                _returnAct = dungeon.CurrentAct;
                _returnDifficulty = dungeon.CurrentDifficulty;
                _returnStage = dungeon.CurrentStage;
            }

            _hud = BossRushHud.Show();
            Time.timeScale = 1f;   // 직전 전투의 슬로우모션이 남아 있을 수 있다
            Debug.Log($"[BossRush] 도전 시작 run={_runId} 라운드 {_rounds.Count}개, 제한 {_timeLimitMs}ms");
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

        /// <summary>라운드 배너를 띄운다(스테이지 입장 배너 재사용, 문구만 <c>ROUND n / 5</c>).</summary>
        private void ShowRoundBanner(int index)
        {
            string top = $"ROUND {RoundNumber(index)} / {_rounds.Count}";
            const string bottom = "BOSS RUSH";

            var prefab = dungeon != null ? dungeon.StageEnterBannerPrefab : null;
            if (prefab != null)
            {
                var go = Instantiate(prefab);
                GameViewLayout.ApplyCurrentScalers(go);
                var banner = go.GetComponent<StageEnterBanner>();
                if (banner != null)
                {
                    banner.PlayText(top, bottom);
                    return;
                }
                Destroy(go); // StageEnterBanner가 없는 잘못된 프리팹 → 폴백
            }

            var fallback = new GameObject("BossRushRoundBanner").AddComponent<StageEnterBanner>();
            fallback.PlayText(top, bottom);
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

            if (_timerRunning && ElapsedMs() >= _timeLimitMs)
            {
                FailRun("시간 초과", $"제한 시간 {BossRushFormat.Remaining(_timeLimitMs)}을 넘겨 도전이 끝났습니다");
                return;
            }

            if (_phase == Phase.PortalWalk)
            {
                TickPortalWalk();
            }
        }

        /// <summary>HUD에 현재 라운드·경과·잔여를 넘긴다.</summary>
        private void UpdateHud()
        {
            if (_hud == null)
            {
                return;
            }
            int elapsed = ElapsedMs();
            _hud.SetState(RoundNumber(_roundIndex), _rounds.Count, elapsed,
                          Mathf.Max(0, _timeLimitMs - elapsed), _timeLimitMs);
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

        /// <summary>보스를 처치한 자리 앞에 포탈을 세우고, 파티가 걸어 들어가기를 기다린다.</summary>
        private void OpenPortal()
        {
            _phase = Phase.PortalWalk;
            _portalWalkTimer = 0f;
            _absorbing.Clear();

            _portalX = battle.PartyFrontX + Mathf.Max(1f, portalAheadDistance);
            _portal = PortalEffect.Spawn(portalFrames, new Vector3(_portalX, battle.PathY, 0f),
                                         portalWorldHeight, portalFps);
            if (_portal == null)
            {
                // 프레임이 배선되지 않았으면 연출 없이 곧바로 전환한다(진행이 막히지 않게).
                BeginTransition();
            }
        }

        /// <summary>포탈에 닿은 파티원부터 빨려 들어가게 하고, 전원이 들어가면 화면 전환을 시작한다.</summary>
        private void TickPortalWalk()
        {
            _portalWalkTimer += Time.unscaledDeltaTime;

            var party = battle.Party;
            bool allIn = true;
            for (int i = 0; i < party.Count; i++)
            {
                var m = party[i];
                if (m == null || !m.Alive)
                {
                    continue;
                }
                var st = FindAbsorb(m);
                if (st == null)
                {
                    if (m.transform.position.x >= _portalX - AbsorbEnterGap)
                    {
                        st = new AbsorbState(m);
                        _absorbing.Add(st);
                    }
                    else
                    {
                        allIn = false;
                        continue;
                    }
                }
                if (!st.Tick(Time.unscaledDeltaTime))
                {
                    allIn = false;
                }
            }

            if (allIn || _portalWalkTimer >= PortalWalkTimeout)
            {
                BeginTransition();
            }
        }

        /// <summary>이 멤버의 진입 상태를 찾는다(없으면 null).</summary>
        private AbsorbState FindAbsorb(PlayerCombatant m)
        {
            foreach (var st in _absorbing)
            {
                if (st.Owner == m)
                {
                    return st;
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
            foreach (var st in _absorbing)
            {
                st.Restore();
            }
            _absorbing.Clear();

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

        /// <summary>아군 전멸 — 실패 처리(서버에는 아무것도 보내지 않는다).</summary>
        private void OnDefeat()
        {
            if (_phase == Phase.Idle || _phase == Phase.Finished)
            {
                return;
            }
            FailRun("보스 러시 도전 실패", $"ROUND {RoundNumber(_roundIndex)}에서 쓰러졌습니다");
        }

        /// <summary>도전 실패(전멸·시간 초과) — 전투를 끊고 패배 연출을 띄운 뒤 던전으로 돌아간다.
        /// <b>서버에 보고하지 않는다</b> — 그 런은 제한 시간이 지나 만료된다(서버 기획서 6.2).</summary>
        private void FailRun(string headline, string hint)
        {
            _timerRunning = false;
            _phase = Phase.Finished;

            if (_portal != null)
            {
                Destroy(_portal.gameObject);
                _portal = null;
            }
            foreach (var st in _absorbing)
            {
                st.Restore();
            }
            _absorbing.Clear();

            battle.AbortServerBattle();   // 남은 웨이브·적을 정리(시간 초과 시 전투 즉시 중단)
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
            _absorbing.Clear();
            Time.timeScale = 1f;

            if (returnToDungeon && dungeon != null)
            {
                Debug.Log($"[BossRush] 던전 복귀 {_returnAct}-{_returnDifficulty}-{_returnStage}");
                dungeon.EnterSelectedStage(_returnAct, _returnDifficulty, _returnStage);
            }
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
        /// 포탈로 빨려 들어가는 파티원 1명의 연출 상태 — 원래 크기·색을 기억해 두고 줄어들며 사라지게 한 뒤,
        /// 다음 지역에 다시 나타날 때 <see cref="Restore"/>로 되돌린다(오브젝트를 파괴하지 않으므로
        /// 체력·쿨다운·스킬 상태가 그대로 이어진다).
        /// </summary>
        private class AbsorbState
        {
            public readonly PlayerCombatant Owner;

            private readonly Transform _tr;
            private readonly Vector3 _baseScale;
            private readonly SpriteRenderer[] _renderers;
            private readonly Color[] _colors;
            private float _t;

            public AbsorbState(PlayerCombatant owner)
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
                float k = Mathf.Clamp01(_t / AbsorbSeconds);
                if (_tr != null)
                {
                    _tr.localScale = _baseScale * (1f - k);   // 좌우 반전 부호가 유지되도록 원래 스케일에 비례
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
