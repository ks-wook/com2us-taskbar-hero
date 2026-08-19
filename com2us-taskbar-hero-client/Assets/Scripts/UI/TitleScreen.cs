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
    ///
    /// <para><b>접속 서버 변경</b> — 화면 <b>우측 하단의 톱니바퀴</b>(<see cref="ServerSettingsButton"/>)로
    /// '접속 서버 변경' 화면을 연다. 로그인 직전의 자동 노출과 달리 <b>QA 빌드에서도 열린다</b> —
    /// 그 빌드는 서버 선택 화면이 뜨지 않아 접속처를 되돌릴 통로가 없기 때문이다.
    /// 게임이 시작되면 톱니바퀴는 사라진다(로그인 이후에는 접속처를 바꿀 수 없다).</para>
    /// </summary>
    public class TitleScreen : MonoBehaviour
    {
        [Tooltip("로고/안내 문구가 들어있는 타이틀 UI 루트. 시작하면 숨긴다.")]
        [SerializeField] private GameObject titleRoot;

        [Tooltip("'Press to start' 문구를 깜빡이게 할 CanvasGroup (선택).")]
        [SerializeField] private CanvasGroup pressToStartGroup;

        [Tooltip("깜빡임 한 주기의 길이(초).")]
        [SerializeField] private float blinkPeriod = 1.2f;

        [Tooltip("우측 하단 '접속 서버 변경' 톱니바퀴 아이콘(Assets/Art/Icon/환경설정.png). " +
                 "에디터 빌더(TaskbarHero/UI/환경설정 패널·씬 배선)가 배선한다. 없으면 글자 버튼으로 대체된다.")]
        [SerializeField] private Sprite settingsIcon;

        // 우측 하단 톱니바퀴(런타임 생성). 게임을 시작하면 파괴한다.
        private ServerSettingsButton _serverSettingsButton;

        private bool _started;
        private bool _quitModalOpen;      // 종료 확인 모달 중복 표시 방지
        private bool _pressHeld;          // 시작 클릭 후보(버튼 눌림 → 놓을 때까지 추적)
        private bool _draggedDuringPress; // 누르고 있는 동안 창 드래그(오버레이 이동)가 발생했는지
        private bool _autoLoginReady;     // 자동 로그인 검증 성공 — 화면을 누르면 바로 게임 진입
        private bool _autoLoginPending;   // 검증 응답 대기 중(그 사이 누르면 종전 로그인 흐름으로 가지 않도록 막는다)
        private bool _startQueued;        // 대기 중에 눌린 시작 입력 — 응답이 오면 그 결과대로 이어서 시작한다

        /// <summary>타이틀 씬 BGM을 시작한다(같은 곡이면 SoundManager가 무시하므로 재진입에도 끊기지 않는다).</summary>
        private void Awake()
        {
            SoundManager.Bgm(SoundId.BgmTitle);
        }

        /// <summary>우측 하단 톱니바퀴를 만들고, 저장된 마지막 세션이 있으면 자동 로그인을 시도한다.</summary>
        private void Start()
        {
            CreateServerSettingsButton();
            TryAutoLogin();
        }

        /// <summary>우측 하단에 '접속 서버 변경' 톱니바퀴 버튼을 만든다(이미 있으면 그대로 쓴다).</summary>
        private void CreateServerSettingsButton()
        {
            _serverSettingsButton = ServerSettingsButton.Create(settingsIcon, OpenServerChange);
        }

        /// <summary>
        /// 톱니바퀴를 눌렀을 때: '접속 서버 변경' 화면을 연다.
        /// <para>접속처가 <b>실제로 바뀌면</b> 자동 로그인 상태를 버리고 새 서버 기준으로 다시 검증한다 —
        /// 앞선 검증은 <b>이전 서버</b>가 내준 결과라 그대로 두면 바꾼 서버에 이전 계정으로 진입하게 된다.
        /// 저장 세션 자체는 지우지 않는다(<see cref="SavedSession"/>이 환경까지 함께 보관하므로,
        /// 원래 서버로 되돌리면 자동 로그인이 다시 살아난다).</para>
        /// </summary>
        private void OpenServerChange()
        {
            ServerSelectPanelController.ShowManual(changed =>
            {
                if (!changed)
                {
                    return;
                }
                Session.Clear();
                _autoLoginReady = false;
                _autoLoginPending = false;
                _startQueued = false;
                TryAutoLogin();
            });
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
                    ResumeQueuedStart();
                },
                error =>
                {
                    // 만료(1005)·다른 기기 로그인으로 밀려남(1004) 등 → 저장값을 버리고 로그인 화면으로.
                    //
                    // <b>전송 계층 실패는 예외</b>(연결 불가·타임아웃) — 토큰이 무효라는 근거가 아니라
                    // 그 주소에 서버가 없다는 뜻일 뿐이다. 톱니바퀴로 접속처를 잘못 바꿔 보기만 해도
                    // 원래 서버의 <b>멀쩡한 토큰</b>이 지워져 재로그인을 강요당하므로, 이때는 남긴다.
                    _autoLoginPending = false;
                    _autoLoginReady = false;
                    if (error != null && error.IsTransportError)
                    {
                        Debug.Log($"[TitleScreen] 자동 로그인 검증 실패(서버 연결 불가) → 저장 세션은 남기고 로그인 화면 사용");
                    }
                    else
                    {
                        SavedSession.Clear();
                        Debug.Log($"[TitleScreen] 자동 로그인 실패(code={error?.ErrorCode}) → 저장 세션 폐기, 로그인 화면 사용");
                    }
                    ResumeQueuedStart();
                });
        }

        /// <summary>
        /// 검증 응답을 기다리는 동안 눌린 시작 입력을 이어서 처리한다.
        /// 그 클릭을 그냥 버리면 <b>아무 반응 없이 무시된 것처럼 보이고</b>(효과음도 나지 않는다)
        /// 사용자가 다시 눌러야 하므로, 응답이 확정된 지금 그 결과대로 시작한다.
        /// </summary>
        private void ResumeQueuedStart()
        {
            if (!_startQueued)
            {
                return;
            }
            _startQueued = false;
            StartGame();
        }

        private void Update()
        {
            // '접속 서버 변경' 창이 떠 있는 동안에는 타이틀 입력(ESC 종료·화면 클릭 시작)을 받지 않는다.
            // 이 화면의 클릭 감지는 EventSystem이 아니라 Pointer 직접 읽기라, 창의 딤 이미지가
            // 클릭을 막아 주지 못한다(창을 닫는 클릭이 그대로 게임 시작으로 이어진다).
            if (ServerSelectPanelController.IsOpen)
            {
                _pressHeld = false;
                return;
            }

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
                // 톱니바퀴 위에서 시작한 누름은 게임 시작이 아니다(버튼만 눌리고 타이틀은 그대로).
                // 포커스 없는 오버레이 창에서도 동작하도록 EventSystem 상태가 아닌 수동 레이캐스트로 판정한다.
                _pressHeld = !ServerSettingsButton.PointerOverButton;
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
            if (_started)
            {
                return;
            }
            if (_autoLoginPending)
            {
                // 검증 응답을 기다리는 사이의 클릭은 흐름이 갈리는 지점이라 지금 처리할 수 없다.
                // 버리지 말고 적어 뒀다가 응답이 오면 이어서 시작한다(ResumeQueuedStart).
                _startQueued = true;
                return;
            }
            SoundManager.Sfx(SoundId.TitleStart);

            _started = true;

            if (titleRoot != null)
            {
                titleRoot.SetActive(false);
            }

            // 로그인 이후에는 접속처를 바꿀 수 없으므로 톱니바퀴를 없앤다.
            // (버튼이 만들어 둔 EventSystem도 함께 사라져 이후 패널들이 자기 것을 만드는 데 방해되지 않는다.)
            if (_serverSettingsButton != null)
            {
                Destroy(_serverSettingsButton.gameObject);
                _serverSettingsButton = null;
            }

            if (_autoLoginReady)
            {
                EnterGameDirectly();
                return;
            }

            // 로그인 이전에 접속 서버 선택 UI를 노출하고, '확인' 후 로그인 UI를 활성화한다.
            // QA 빌드는 접속처가 원격으로 고정이라 이 화면이 뜨지 않고 곧바로 로그인 UI로 넘어간다.
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
                //
                // 저장 세션은 <b>지우지 않는다</b> — 여기까지 왔다는 것은 /api/auth/validate가 이미 성공해
                // 토큰이 유효함이 확인된 상태이고, 실패한 것은 그 뒤의 GameServer 호출이다.
                // 일시적인 GameServer 장애로 <b>아직 유효한 토큰</b>을 버리면 다음 실행에서도 자동 로그인이
                // 사라져 재로그인을 강요하게 된다. 이번 실행만 로그인 UI로 되돌리고 저장값은 남긴다.
                LoadingOverlay.Instance?.Hide();
                _autoLoginReady = false;
                _started = false;
                Session.Clear();
                if (titleRoot != null)
                {
                    titleRoot.SetActive(true);
                }
                CreateServerSettingsButton();   // 시작할 때 없앴으므로 다시 만든다
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
