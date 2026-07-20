using UnityEngine;
using UnityEngine.UI;
using TaskbarHero.Client.Managers;
using TaskbarHero.Common.Dto;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 회원가입 패널 컨트롤러. 이메일/비밀번호/닉네임을 검증해 AccountServer(/api/auth/signup)에 가입 요청을 보낸다.
    /// 성공 시 로그인 UI로 돌아가고, 뒤로가기 버튼도 로그인 UI로 전환한다.
    /// </summary>
    public class SignUpPanelController : MonoBehaviour
    {
        [SerializeField] private InputField emailInput;
        [SerializeField] private InputField passwordInput;
        [SerializeField] private InputField nicknameInput;
        [SerializeField] private Button signUpButton;
        [SerializeField] private Button backButton;
        [SerializeField] private Text errorText;

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
        }

        private void OnEnable()
        {
            SetError(string.Empty);
        }

        private void OnSignUpClicked()
        {
            string email = emailInput != null ? emailInput.text.Trim() : string.Empty;
            string password = passwordInput != null ? passwordInput.text : string.Empty;
            string nickname = nicknameInput != null ? nicknameInput.text.Trim() : string.Empty;

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password) || string.IsNullOrEmpty(nickname))
            {
                SetError("이메일, 비밀번호, 닉네임을 모두 입력하세요.");
                return;
            }

            if (NetworkManager.Instance == null)
            {
                SetError("네트워크 매니저를 찾을 수 없습니다.");
                return;
            }

            SetError("가입 중...");
            SetInteractable(false);

            var request = new SignupRequest { email = email, password = password, nickname = nickname };
            NetworkManager.Instance.PostToAccount<SignupResponse>("/api/auth/signup", request, OnSignUpSuccess, OnSignUpError);
        }

        private void OnSignUpSuccess(SignupResponse response)
        {
            SetInteractable(true);
            Debug.Log($"[SignUpPanel] 회원가입 성공. userId={response.userId}");

            // 가입 완료 → 로그인 화면으로 복귀.
            if (UIManager.Instance != null)
            {
                UIManager.Instance.ShowLogin();
            }
        }

        private void OnSignUpError(NetworkError error)
        {
            SetInteractable(true);
            SetError(ErrorMessages.ToKorean(error));
            Debug.LogWarning($"[SignUpPanel] 회원가입 실패: {error}");
        }

        private void OnBackClicked()
        {
            if (UIManager.Instance != null)
            {
                UIManager.Instance.ShowLogin();
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
