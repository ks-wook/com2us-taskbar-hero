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

        private CanvasGroup _panelGroup; // 로딩 중 로그인 UI 전체를 숨기고 입력을 차단하기 위한 그룹

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
            bool hasCharacter = !response.data.isNew && response.data.characters != null && response.data.characters.Count > 0;
            Debug.Log($"[LoginPanel] 게임 데이터 로드 완료. isNew={response.data.isNew}, 캐릭터수={(response.data.characters != null ? response.data.characters.Count : 0)}");

            // 로그인 시점의 세이브 스냅샷을 세션에 캐싱(닉네임도 함께 갱신).
            Session.SetGameData(response.data);

            if (SceneManager.Instance == null)
            {
                LoadingOverlay.Instance?.Hide();
                SetLoginUiShown(true);
                SetInteractable(true);
                SetError(string.Empty);
                ShowModal("오류", "씬 매니저를 찾을 수 없습니다.");
                return;
            }

            // 회원가입 직후 최초 캐릭터 생성 진입은 '뒤로가기' 미노출(게임 안 진입 아님).
            Session.CreateCharacterFromGame = false;

            // 캐릭터가 없으면(신규 계정) 오프라인 정산 대상이 아니므로 캐릭터 생성 씬으로 전환.
            if (!hasCharacter)
            {
                SetInteractable(true);
                SetError(string.Empty);
                SceneManager.Instance.LoadScene("CreateCharacterScene");
                return;
            }

            // Login → GameScene: 진입 직전에 오프라인 보상을 정산(/api/game/offline/claim).
            // heartbeat 시작 전에 정산해야 경과가 소실되지 않는다(기획서 §6.2). 성공 시 결과를 세션에 대기시켜
            // GameScene 진입 팝업이 표시하고, 정산할 오프라인이 없거나(3001) 실패해도 게임 진입은 계속한다.
            SetError("오프라인 보상 정산 중...");
            var claimRequest = new AuthRequest { userId = Session.UserId, token = Session.Token };
            NetworkManager.Instance.PostToGame<OfflineClaimResponse>(
                "/api/game/offline/claim", claimRequest, OnOfflineClaimed, OnOfflineClaimError);
        }

        /// <summary>오프라인 보상 정산 성공: 결과를 세션에 반영·대기시키고 GameScene으로 진입한다.</summary>
        private void OnOfflineClaimed(OfflineClaimResponse response)
        {
            if (response != null && response.data != null)
            {
                Session.ApplyOfflineReward(response.data);
                Debug.Log($"[LoginPanel] 오프라인 보상 정산 완료: gold=+{response.data.rewards.gold}, " +
                          $"exp=+{response.data.rewards.exp}, effectiveSec={response.data.effectiveSec}, capped={response.data.capped}");
            }
            EnterGameScene();
        }

        /// <summary>오프라인 보상 정산 실패/생략(정산할 오프라인 없음 3001·이미 정산 3002 등): 팝업 없이 GameScene으로 진입한다.</summary>
        private void OnOfflineClaimError(NetworkError error)
        {
            Debug.Log($"[LoginPanel] 오프라인 보상 없음/생략(code={error.ErrorCode}) → 팝업 없이 GameScene 진입");
            EnterGameScene();
        }

        /// <summary>입력 상태를 복구하고 GameScene으로 전환한다(정산 성공/실패 공통).</summary>
        private void EnterGameScene()
        {
            SetInteractable(true);
            SetError(string.Empty);
            SceneManager.Instance.LoadScene("GameScene");
        }

        private void OnLoadError(NetworkError error)
        {
            LoadingOverlay.Instance?.Hide();
            SetLoginUiShown(true);
            SetInteractable(true);
            SetError(string.Empty);
            ShowModal("데이터 로드 실패", ErrorMessages.ToKorean(error));
            Debug.LogWarning($"[LoginPanel] 게임 데이터 로드 실패: {error}");
        }

        private void OnLoginError(NetworkError error)
        {
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
