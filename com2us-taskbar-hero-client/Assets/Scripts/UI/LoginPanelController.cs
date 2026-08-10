using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TaskbarHero.Client.Managers;
using TaskbarHero.Common.Dto;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 로그인 패널 컨트롤러. 입력값 검증 후 AccountServer(/api/auth/login)에 로그인 요청을 보내고,
    /// 결과를 에러 문구로 표시한다. 회원가입 버튼은 회원가입 UI로 전환한다.
    /// </summary>
    public class LoginPanelController : MonoBehaviour
    {
        [SerializeField] private InputField emailInput;
        [SerializeField] private InputField passwordInput;
        [SerializeField] private Button loginButton;
        [SerializeField] private Button signUpButton;
        [SerializeField] private Text errorText;

        private CanvasGroup _panelGroup; // 로딩 중 로그인 UI 전체를 숨기고 입력을 차단하기 위한 그룹
        private string _lastEmail = string.Empty; // 로그인 요청에 쓴 이메일(성공 시 자동 로그인용으로 저장)

        private void Awake()
        {
            if (loginButton != null)
            {
                loginButton.onClick.AddListener(OnLoginClicked);
            }

            if (signUpButton != null)
            {
                signUpButton.onClick.AddListener(OnSignUpClicked);
            }

            // 패널 루트에 CanvasGroup을 확보(로딩 중 로그인 UI만 숨기고 스피너만 노출).
            _panelGroup = transform.root.GetComponent<CanvasGroup>();
            if (_panelGroup == null)
            {
                _panelGroup = transform.root.gameObject.AddComponent<CanvasGroup>();
            }
        }

        /// <summary>로그인 UI 전체를 표시/숨김한다(로딩 중에는 숨겨 로딩 스피너만 보이게 한다).</summary>
        private void SetLoginUiShown(bool shown)
        {
            if (_panelGroup == null)
            {
                return;
            }
            _panelGroup.alpha = shown ? 1f : 0f;
            _panelGroup.interactable = shown;
            _panelGroup.blocksRaycasts = shown;
        }

        private void OnEnable()
        {
            SetError(string.Empty);
            SetLoginUiShown(true); // 패널이 다시 표시될 때 로그인 UI를 확실히 노출(숨김 상태 잔존 방지)
            FocusInput(emailInput);  // 열자마자 바로 타이핑할 수 있게 아이디 칸에 커서를 둔다
        }

        /// <summary>키보드만으로 로그인할 수 있게 한다 — <b>Tab</b>은 아이디↔비밀번호 이동,
        /// <b>Enter</b>는 로그인 요청. 새 Input System에는 Tab 기본 내비게이션 바인딩이 없어 직접 처리한다.
        /// 입력이 막힌 상태(로딩 중)에는 반응하지 않는다.</summary>
        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || _panelGroup == null || !_panelGroup.interactable)
            {
                return;
            }

            if (kb.tabKey.wasPressedThisFrame)
            {
                // 비밀번호 칸에 있으면 아이디로 되돌아가고(Shift+Tab과 동일한 순환), 그 외에는 비밀번호로.
                bool onPassword = passwordInput != null && IsFocused(passwordInput);
                FocusInput(onPassword ? emailInput : passwordInput);
            }
            else if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
            {
                OnLoginClicked();
            }
        }

        /// <summary>해당 입력창을 선택하고 커서를 문자열 끝에 둔다.</summary>
        private static void FocusInput(InputField input)
        {
            if (input == null || !input.gameObject.activeInHierarchy)
            {
                return;
            }
            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(input.gameObject);
            }
            input.ActivateInputField();
            input.caretPosition = input.text.Length;
        }

        /// <summary>현재 EventSystem 선택이 이 입력창인지.</summary>
        private static bool IsFocused(InputField input)
        {
            return EventSystem.current != null
                   && EventSystem.current.currentSelectedGameObject == input.gameObject;
        }

        private void OnLoginClicked()
        {
            string email = emailInput != null ? emailInput.text.Trim() : string.Empty;
            string password = passwordInput != null ? passwordInput.text : string.Empty;

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                ShowModal("로그인", "이메일과 비밀번호를 입력하세요.");
                return;
            }

            if (NetworkManager.Instance == null)
            {
                ShowModal("오류", "네트워크 매니저를 찾을 수 없습니다.");
                return;
            }

            SetError(string.Empty);
            SetInteractable(false);
            SetLoginUiShown(false);           // 로그인 UI 숨김 → 로딩 스피너만 노출
            LoadingOverlay.Instance?.Show();   // 완료(씬 전환/오류)까지 스피너 표시 + 입력 차단

            _lastEmail = email;   // 로그인 성공 시 자동 로그인용으로 저장할 계정 이메일
            var request = new LoginRequest { email = email, password = password };
            NetworkManager.Instance.PostToAccount<LoginResponse>("/api/auth/login", request, OnLoginSuccess, OnLoginError);
        }

        private void OnLoginSuccess(LoginResponse response)
        {
            SoundManager.Sfx(SoundId.LoginSuccess);
            // 로그인 계정 정보를 세션에 캐싱(이후 인증 API 요청에서 재사용).
            Session.SetAuth(response.userId, response.token);
            // 다음 실행의 자동 로그인용으로 마지막 세션을 로컬에 저장한다(타이틀 화면이 /api/auth/validate로 확인).
            SavedSession.Save(response.userId, response.token, _lastEmail);

            Debug.Log($"[LoginPanel] 로그인 성공. userId={response.userId} → 게임 데이터 로드");

            // 이후 진입 연쇄(세이브 로드 → 캐릭터 유무 → 오프라인 정산 → 씬 전환)는 자동 로그인과 공유한다.
            GameEntryFlow.Begin(
                onProgress: SetError,
                onFailed: OnEntryFailed,
                onEnteringScene: () =>
                {
                    SetInteractable(true);
                    SetError(string.Empty);
                });
        }

        /// <summary>게임 진입 실패(세이브 로드 오류·씬 매니저 없음): 로그인 UI를 되살리고 안내한다.</summary>
        private void OnEntryFailed(NetworkError error)
        {
            LoadingOverlay.Instance?.Hide();
            SetLoginUiShown(true);
            SetInteractable(true);
            SetError(string.Empty);
            ShowModal("데이터 로드 실패",
                error != null ? ErrorMessages.ToKorean(error) : "게임 진입에 실패했습니다.");
            Debug.LogWarning($"[LoginPanel] 게임 진입 실패: {error}");
        }

        private void OnLoginError(NetworkError error)
        {
            SoundManager.Sfx(SoundId.LoginFail);
            LoadingOverlay.Instance?.Hide();
            SetLoginUiShown(true);
            SetInteractable(true);
            SetError(string.Empty);
            ShowModal("로그인 실패", ErrorMessages.ToKorean(error));
            Debug.LogWarning($"[LoginPanel] 로그인 실패: {error}");
        }

        /// <summary>공용 모달로 안내한다(매니저가 없으면 인라인 문구로 폴백).</summary>
        private void ShowModal(string title, string message)
        {
            if (ModalManager.Instance != null)
            {
                ModalManager.Instance.ShowConfirm(title, message);
            }
            else
            {
                SetError(message);
            }
        }

        private void OnSignUpClicked()
        {
            if (UIManager.Instance != null)
            {
                UIManager.Instance.ShowSignUp();
            }
        }

        private void SetError(string message)
        {
            if (errorText != null)
            {
                errorText.text = message;
            }
        }

        private void SetInteractable(bool value)
        {
            if (loginButton != null)
            {
                loginButton.interactable = value;
            }

            if (signUpButton != null)
            {
                signUpButton.interactable = value;
            }
        }
    }
}
