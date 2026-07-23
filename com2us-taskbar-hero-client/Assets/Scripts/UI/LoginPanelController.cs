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
            // 로그인 계정 정보를 세션에 캐싱(이후 인증 API 요청에서 재사용).
            Session.SetAuth(response.userId, response.token);

            Debug.Log($"[LoginPanel] 로그인 성공. userId={response.userId} → 게임 데이터 로드");
            SetError("데이터 로드 중...");

            // 로그인 성공 → 세이브 스냅샷 로드(/api/game/load, 인증 필요).
            var request = new AuthRequest { userId = Session.UserId, token = Session.Token };
            NetworkManager.Instance.PostToGame<LoadResponse>("/api/game/load", request, OnLoadSuccess, OnLoadError);
        }

        private void OnLoadSuccess(LoadResponse response)
        {
            SetInteractable(true);
            SetError(string.Empty);

            bool hasCharacter = !response.data.isNew && response.data.characters != null && response.data.characters.Count > 0;
            Debug.Log($"[LoginPanel] 게임 데이터 로드 완료. isNew={response.data.isNew}, 캐릭터수={(response.data.characters != null ? response.data.characters.Count : 0)}");

            // 로그인 시점의 세이브 스냅샷을 세션에 캐싱(닉네임도 함께 갱신).
            Session.SetGameData(response.data);

            if (SceneManager.Instance == null)
            {
                SetError("씬 매니저를 찾을 수 없습니다.");
                return;
            }

            // 신규 계정(또는 캐릭터 없음)이면 캐릭터 생성 씬으로, 아니면 게임 씬으로 전환.
            SceneManager.Instance.LoadScene(hasCharacter ? "GameScene" : "CreateCharacterScene");
        }

        private void OnLoadError(NetworkError error)
        {
            SetInteractable(true);
            SetError(string.Empty);
            ShowModal("데이터 로드 실패", ErrorMessages.ToKorean(error));
            Debug.LogWarning($"[LoginPanel] 게임 데이터 로드 실패: {error}");
        }

        private void OnLoginError(NetworkError error)
        {
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
