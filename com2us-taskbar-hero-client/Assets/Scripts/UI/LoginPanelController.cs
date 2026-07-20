using UnityEngine;
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
        }

        private void OnEnable()
        {
            SetError(string.Empty);
        }

        private void OnLoginClicked()
        {
            string email = emailInput != null ? emailInput.text.Trim() : string.Empty;
            string password = passwordInput != null ? passwordInput.text : string.Empty;

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                SetError("이메일과 비밀번호를 입력하세요.");
                return;
            }

            if (NetworkManager.Instance == null)
            {
                SetError("네트워크 매니저를 찾을 수 없습니다.");
                return;
            }

            SetError("로그인 중...");
            SetInteractable(false);

            var request = new LoginRequest { email = email, password = password };
            NetworkManager.Instance.PostToAccount<LoginResponse>("/api/auth/login", request, OnLoginSuccess, OnLoginError);
        }

        private void OnLoginSuccess(LoginResponse response)
        {
            SetInteractable(true);

            NetworkManager.Instance.AuthToken = response.token;
            NetworkManager.Instance.UserId = response.userId;

            SetError(string.Empty);
            Debug.Log($"[LoginPanel] 로그인 성공. userId={response.userId}");
            // TODO: 로그인 성공 후 게임(로비) 씬 전환 등 다음 흐름 연결.
        }

        private void OnLoginError(NetworkError error)
        {
            SetInteractable(true);
            SetError(ErrorMessages.ToKorean(error));
            Debug.LogWarning($"[LoginPanel] 로그인 실패: {error}");
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
