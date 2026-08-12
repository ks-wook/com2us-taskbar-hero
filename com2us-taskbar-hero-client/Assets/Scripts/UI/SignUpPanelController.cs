using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TaskbarHero.Client.Managers;
using TaskbarHero.Common.Dto;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 회원가입 패널 컨트롤러. 이메일/비밀번호/닉네임을 검증해 AccountServer(/api/auth/signup)에 가입 요청을 보내고,
    /// 결과는 공용 모달(ModalManager)로 안내한다. 성공 시 로그인 UI로 돌아가고, 뒤로가기 버튼도 로그인 UI로 전환한다.
    /// </summary>
    public class SignUpPanelController : MonoBehaviour
    {
        [SerializeField] private InputField emailInput;
        [SerializeField] private InputField passwordInput;
        [SerializeField] private InputField nicknameInput;
        [SerializeField] private Button signUpButton;
        [SerializeField] private Button backButton;

        /// <summary>비밀번호 최소 길이. 서버(<c>AuthService.MinPasswordLength</c> = 6)와 같은 값이며,
        /// 미달이면 요청을 보내지 않고 여기서 막는다(왕복 없이 즉시 안내 + 서버가 다시 검증하는 이중 방어).</summary>
        private const int MinPasswordLength = 6;

        private CanvasGroup _panelGroup; // 로딩 중 회원가입 UI 전체를 숨기고 입력을 차단하기 위한 그룹

        private void Awake()
        {
            if (signUpButton != null)
            {
                signUpButton.onClick.AddListener(OnSignUpClicked);
            }

            if (backButton != null)
            {
                backButton.onClick.AddListener(OnBackClicked);
            }

            // 패널 루트에 CanvasGroup을 확보(로딩 중 회원가입 UI만 숨기고 스피너만 노출).
            _panelGroup = transform.root.GetComponent<CanvasGroup>();
            if (_panelGroup == null)
            {
                _panelGroup = transform.root.gameObject.AddComponent<CanvasGroup>();
            }
        }

        private void OnEnable()
        {
            SetSignUpUiShown(true); // 패널이 다시 표시될 때 회원가입 UI를 확실히 노출
            FocusInput(emailInput); // 열자마자 바로 타이핑할 수 있게 이메일 칸에 커서를 둔다
        }

        /// <summary>키보드만으로 가입할 수 있게 한다 — <b>Tab</b>은 이메일→비밀번호→닉네임 순서로 이동
        /// (<b>Shift+Tab</b>은 역순), <b>Enter</b>는 회원가입 요청. 새 Input System에는 Tab 기본 내비게이션
        /// 바인딩이 없어 직접 처리한다. 입력이 막힌 상태(로딩 중)나 모달이 떠 있는 동안에는 반응하지 않는다.</summary>
        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || _panelGroup == null || !_panelGroup.interactable)
            {
                return;
            }

            // 안내 모달(가입 완료·실패)이 떠 있는 동안에는 뒤쪽 패널이 키를 먹지 않게 한다.
            if (ModalManager.Instance != null && ModalManager.Instance.IsShowing)
            {
                return;
            }

            if (kb.tabKey.wasPressedThisFrame)
            {
                bool backward = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
                FocusInput(NextInput(backward));
            }
            else if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
            {
                OnSignUpClicked();
            }
        }

        /// <summary>현재 포커스를 기준으로 Tab 이동 대상 입력창을 고른다(순환, 어디에도 없으면 첫 칸).</summary>
        private InputField NextInput(bool backward)
        {
            var order = new[] { emailInput, passwordInput, nicknameInput };

            int current = -1;
            for (int i = 0; i < order.Length; i++)
            {
                if (order[i] != null && IsFocused(order[i]))
                {
                    current = i;
                    break;
                }
            }

            if (current < 0)
            {
                return order[0];
            }

            int step = backward ? order.Length - 1 : 1;
            for (int i = 1; i <= order.Length; i++)
            {
                var candidate = order[(current + step * i) % order.Length];
                if (candidate != null)
                {
                    return candidate;
                }
            }
            return null;
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

        /// <summary>회원가입 UI 전체를 표시/숨김한다(로딩 중에는 숨겨 로딩 스피너만 보이게 한다).</summary>
        private void SetSignUpUiShown(bool shown)
        {
            if (_panelGroup == null)
            {
                return;
            }
            _panelGroup.alpha = shown ? 1f : 0f;
            _panelGroup.interactable = shown;
            _panelGroup.blocksRaycasts = shown;
        }

        private void OnSignUpClicked()
        {
            string email = emailInput != null ? emailInput.text.Trim() : string.Empty;
            string password = passwordInput != null ? passwordInput.text : string.Empty;
            string nickname = nicknameInput != null ? nicknameInput.text.Trim() : string.Empty;

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password) || string.IsNullOrEmpty(nickname))
            {
                ShowModal("회원가입", "이메일, 비밀번호, 닉네임을 모두 입력하세요.");
                return;
            }

            // 서버도 같은 규칙으로 거르지만(ErrorCode.InvalidRequest = 1006), 실패가 뻔한 요청은 보내지 않고 여기서 안내한다.
            if (password.Length < MinPasswordLength)
            {
                ShowModal("회원가입", $"비밀번호는 {MinPasswordLength}자 이상이어야 합니다.");
                return;
            }

            if (NetworkManager.Instance == null)
            {
                ShowModal("오류", "네트워크 매니저를 찾을 수 없습니다.");
                return;
            }

            SetInteractable(false);
            SetSignUpUiShown(false);          // 회원가입 UI 숨김 → 로딩 스피너만 노출
            LoadingOverlay.Instance?.Show();   // 완료(성공/오류)까지 스피너 표시 + 입력 차단

            // 입력한 닉네임을 세션에 캐싱(로그인 후 캐릭터 생성 시 계정 닉네임으로 사용).
            Session.Nickname = nickname;

            var request = new SignupRequest { email = email, password = password, nickname = nickname };
            NetworkManager.Instance.PostToAccount<SignupResponse>("/api/auth/signup", request, OnSignUpSuccess, OnSignUpError);
        }

        private void OnSignUpSuccess(SignupResponse response)
        {
            SoundManager.Sfx(SoundId.SignupSuccess);
            LoadingOverlay.Instance?.Hide();
            SetInteractable(true);
            Debug.Log($"[SignUpPanel] 회원가입 성공. userId={response.userId}");

            // 가입 완료 → 공용 모달로 안내 후 확인 시 로그인 화면으로 복귀.
            if (ModalManager.Instance != null)
            {
                ModalManager.Instance.ShowConfirm("회원가입 완료", "회원가입이 완료되었습니다.\n로그인해 주세요.", GoToLogin);
            }
            else
            {
                GoToLogin();
            }
        }

        private void OnSignUpError(NetworkError error)
        {
            LoadingOverlay.Instance?.Hide();
            SetSignUpUiShown(true);
            SetInteractable(true);
            ShowModal("회원가입 실패", ErrorMessages.ToKorean(error));
            Debug.LogWarning($"[SignUpPanel] 회원가입 실패: {error}");
        }

        /// <summary>공용 모달로 안내한다(매니저 배선 누락 시에는 표시할 수단이 없으므로 로그로 남긴다).</summary>
        private void ShowModal(string title, string message)
        {
            if (ModalManager.Instance != null)
            {
                ModalManager.Instance.ShowConfirm(title, message);
                return;
            }
            Debug.LogError($"[SignUpPanel] ModalManager가 없어 안내를 표시하지 못했습니다: {title} - {message}", this);
        }

        /// <summary>로그인 화면으로 전환한다(모달 확인 콜백 포함).</summary>
        private void GoToLogin()
        {
            if (UIManager.Instance != null)
            {
                UIManager.Instance.ShowLogin();
            }
        }

        private void OnBackClicked()
        {
            if (UIManager.Instance != null)
            {
                UIManager.Instance.ShowLogin();
            }
        }

        private void SetInteractable(bool value)
        {
            if (signUpButton != null)
            {
                signUpButton.interactable = value;
            }

            if (backButton != null)
            {
                backButton.interactable = value;
            }
        }
    }
}
