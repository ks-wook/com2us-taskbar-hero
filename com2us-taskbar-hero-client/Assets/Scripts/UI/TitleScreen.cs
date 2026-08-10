using UnityEngine;
using UnityEngine.InputSystem;
using TaskbarHero.Client.Managers;
using TaskbarHero.Common.Dto;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 타이틀 화면 컨트롤러.
    /// 로고와 'Press to start' 안내를 표시하고, 화면 어디든 클릭/터치하면 게임을 시작한다.
    /// 클릭 감지는 EventSystem 없이 Input System의 <see cref="Pointer"/>로 직접 처리한다.
    ///
    /// <para><b>자동 로그인</b> — 씬에 들어오면 저장된 마지막 세션(<see cref="SavedSession"/>)으로
    /// <c>POST /api/auth/validate</c>를 호출한다(계정·로그인 기획서 §5.4).
    /// <list type="bullet">
    /// <item>성공 → 환영 배너(<see cref="WelcomeBanner"/>)를 띄우고, 화면을 누르면
    ///   <b>로그인 UI를 거치지 않고 바로 게임에 진입</b>한다(<see cref="GameEntryFlow"/>).</item>
    /// <item>실패(만료 1005·다른 기기 로그인 1004) → 저장값을 버리고 종전 흐름
    ///   (접속 서버 선택 → 로그인 UI)으로 돌아간다. 사용자에게 모달로 알리지는 않는다 —
    ///   자동 로그인은 편의 기능이라 실패를 알릴 필요 없이 로그인 화면을 보여 주면 된다.</item>
    /// </list></para>
    /// </summary>
    public class TitleScreen : MonoBehaviour
    {
        [Tooltip("로고/안내 문구가 들어있는 타이틀 UI 루트. 시작하면 숨긴다.")]
        [SerializeField] private GameObject titleRoot;

        [Tooltip("'Press to start' 문구를 깜빡이게 할 CanvasGroup (선택).")]
        [SerializeField] private CanvasGroup pressToStartGroup;

        [Tooltip("깜빡임 한 주기의 길이(초).")]
        [SerializeField] private float blinkPeriod = 1.2f;

        private bool _started;
        private bool _quitModalOpen;      // 종료 확인 모달 중복 표시 방지
        private bool _pressHeld;          // 시작 클릭 후보(버튼 눌림 → 놓을 때까지 추적)
        private bool _draggedDuringPress; // 누르고 있는 동안 창 드래그(오버레이 이동)가 발생했는지
        private bool _autoLoginReady;     // 자동 로그인 검증 성공 — 화면을 누르면 바로 게임 진입
        private bool _autoLoginPending;   // 검증 응답 대기 중(그 사이 누르면 종전 로그인 흐름으로 가지 않도록 막는다)

        /// <summary>타이틀 씬 BGM을 시작한다(같은 곡이면 SoundManager가 무시하므로 재진입에도 끊기지 않는다).</summary>
        private void Awake()
        {
            SoundManager.Bgm(SoundId.BgmTitle);
        }

        /// <summary>저장된 마지막 세션이 있으면 자동 로그인을 시도한다.</summary>
        private void Start()
        {
            TryAutoLogin();
        }

        /// <summary>
        /// 저장된 세션으로 <c>/api/auth/validate</c>를 호출해 자동 로그인 여부를 정한다.
        /// 저장값이 없거나 접속 환경이 다르면(<see cref="SavedSession.CanAutoLogin"/>) 조용히 건너뛴다.
        /// </summary>
        private void TryAutoLogin()
        {
            if (!SavedSession.CanAutoLogin || NetworkManager.Instance == null)
            {
                return;
            }

            long userId = SavedSession.UserId;
            string token = SavedSession.Token;
            string email = SavedSession.Email;
            _autoLoginPending = true;

            var request = new ValidateTokenRequest { userId = userId, token = token };
            NetworkManager.Instance.PostToAccount<ValidateTokenResponse>("/api/auth/validate", request,
                _ =>
                {
                    _autoLoginPending = false;
                    _autoLoginReady = true;
                    Session.SetAuth(userId, token);
                    Debug.Log($"[TitleScreen] 자동 로그인 성공. userId={userId}");
                    WelcomeBanner.Show(email);
                },
                error =>
                {
                    // 만료(1005)·다른 기기 로그인으로 밀려남(1004) 등 → 저장값을 버리고 로그인 화면으로.
                    _autoLoginPending = false;
                    _autoLoginReady = false;
                    SavedSession.Clear();
                    Debug.Log($"[TitleScreen] 자동 로그인 실패(code={error.ErrorCode}) → 저장 세션 폐기, 로그인 화면 사용");
                });
        }

        private void Update()
        {
            // ESC → 종료 확인 모달. 시작 전/후(로그인 화면 포함) 모두 동작.
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                ShowQuitModal();
                return;
            }

            if (_started || _quitModalOpen)
            {
                return; // 종료 모달 표시 중에는 화면 클릭으로 게임이 시작되지 않게 한다
            }

            BlinkPressToStart();

            var pointer = Pointer.current;
            if (pointer == null)
            {
                return;
            }

            // 시작은 "눌렀다 뗐을 때". 누르고 있는 동안 창 드래그(투명 오버레이 창 이동)가
            // 발생했다면 시작 클릭이 아니라 창 이동이므로 게임을 시작하지 않는다.
            if (pointer.press.wasPressedThisFrame)
            {
                _pressHeld = true;
                _draggedDuringPress = false;
            }
            if (_pressHeld && TaskbarWindow.DraggingWindow)
            {
                _draggedDuringPress = true;
            }
            if (_pressHeld && pointer.press.wasReleasedThisFrame)
            {
                _pressHeld = false;
                if (!_draggedDuringPress)
                {
                    StartGame();
                }
            }
        }

        /// <summary>'게임을 종료하시겠습니까?' 확인/취소 모달을 띄운다('확인' 시 종료, '취소' 시 복귀).</summary>
        private void ShowQuitModal()
        {
            if (_quitModalOpen)
            {
                return;
            }
            if (ModalManager.Instance == null)
            {
                Debug.LogWarning("[TitleScreen] ModalManager가 없어 확인 없이 종료합니다.");
                QuitGame();
                return;
            }
            _quitModalOpen = true;
            ModalManager.Instance.ShowConfirmCancel(
                "게임 종료",
                "게임을 종료하시겠습니까?",
                onOk: QuitGame,
                onCancel: () => _quitModalOpen = false);
        }

        /// <summary>게임을 종료한다(에디터에서는 플레이 정지).</summary>
        private static void QuitGame()
        {
            Debug.Log("[TitleScreen] 게임 종료");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void BlinkPressToStart()
        {
            if (pressToStartGroup == null)
            {
                return;
            }

            float wave = 0.5f + 0.5f * Mathf.Sin(Time.time * (2f * Mathf.PI / Mathf.Max(0.01f, blinkPeriod)));
            pressToStartGroup.alpha = 0.35f + 0.65f * wave;
        }

        /// <summary>
        /// 화면을 눌렀을 때의 시작 처리.
        /// <para><b>자동 로그인이 성공한 경우</b>에는 접속 서버 선택·로그인 UI를 모두 건너뛰고
        /// 곧바로 게임에 진입한다(이미 인증이 끝났고 접속 서버도 그 검증에 쓴 것으로 확정됐다).</para>
        /// 그 외에는 종전대로 접속 서버 선택 UI → 로그인 UI 순으로 진행한다.
        /// </summary>
        public void StartGame()
        {
            if (_started || _autoLoginPending)
            {
                return; // 검증 응답을 기다리는 사이의 클릭은 무시한다(흐름이 갈리는 지점이라)
            }
            SoundManager.Sfx(SoundId.TitleStart);

            _started = true;

            if (titleRoot != null)
            {
                titleRoot.SetActive(false);
            }

            if (_autoLoginReady)
            {
                EnterGameDirectly();
                return;
            }

            // 로그인 이전에 접속 서버 선택 UI를 노출하고, '확인' 후 로그인 UI를 활성화한다.
            ServerSelectPanelController.Show(ShowLogin);
        }

        /// <summary>자동 로그인 상태에서 로그인 UI 없이 게임 진입 연쇄를 실행한다(세이브 로드 → 오프라인 정산 → 씬 전환).</summary>
        private void EnterGameDirectly()
        {
            Debug.Log("[TitleScreen] 자동 로그인 상태 — 로그인 UI를 건너뛰고 게임에 진입한다");
            SoundManager.Sfx(SoundId.LoginSuccess);
            LoadingOverlay.Instance?.Show(); // 씬 전환/실패까지 스피너 + 입력 차단

            GameEntryFlow.Begin(onFailed: error =>
            {
                // 진입에 실패하면(세이브 로드 오류 등) 자동 로그인을 포기하고 종전 흐름으로 되돌린다.
                LoadingOverlay.Instance?.Hide();
                _autoLoginReady = false;
                _started = false;
                SavedSession.Clear();
                Session.Clear();
                if (titleRoot != null)
                {
                    titleRoot.SetActive(true);
                }
                string message = error != null ? ErrorMessages.ToKorean(error) : "게임 진입에 실패했습니다.";
                ModalManager.Instance?.ShowConfirm("자동 로그인 실패", message);
                Debug.LogWarning($"[TitleScreen] 자동 로그인 진입 실패 → 타이틀 복귀: {error}");
            });
        }

        /// <summary>접속 서버 확정 후 로그인 UI를 활성화한다.</summary>
        private void ShowLogin()
        {
            if (UIManager.Instance != null)
            {
                UIManager.Instance.ShowLogin();
            }
            else
            {
                Debug.LogError("[TitleScreen] UIManager.Instance가 없습니다. Managers 오브젝트를 확인하세요.", this);
            }
        }
    }
}
