using UnityEngine;
using TaskbarHero.Common;
using USceneManager = UnityEngine.SceneManagement.SceneManager;

namespace TaskbarHero.Client.Managers
{
    /// <summary>
    /// <b>로그인 정보가 무효화된 상황을 전역에서 한곳에서 처리</b>하는 가드.
    /// 다른 기기에서 같은 계정으로 로그인해 토큰이 밀려났거나(<see cref="ErrorCode.InvalidToken"/>),
    /// 토큰이 만료돼 서버가 폐기한 경우(<see cref="ErrorCode.ExpiredToken"/>) 서버는 인증이 필요한 모든 요청을
    /// <c>401 + { success:false, errorCode:1004|1005 }</c>로 되돌린다(<c>GameAuthMiddleware</c>).
    ///
    /// <para>그대로 두면 각 화면이 "실패했다"는 자기 문구만 보여 주고 <b>무효화된 세션으로 계속 플레이</b>하게 되므로
    /// (전투는 돌지만 서버 저장·정산이 전부 실패), <see cref="NetworkManager"/>가 응답을 해석하는 지점에서
    /// 이 가드가 가로채 <b>안내 모달 + 타이틀 화면 복귀</b>로 세션을 끝낸다.</para>
    ///
    /// <para><b>계정 API(<c>/api/auth/*</c>)는 대상이 아니다</b> — 타이틀의 자동 로그인 검증
    /// (<c>/api/auth/validate</c>)은 토큰 무효를 <b>정상 분기</b>로 다뤄 조용히 로그인 화면을 보여 주고,
    /// 로그인·회원가입 실패도 그 화면이 자기 문구로 안내한다. 여기서 또 끼어들면 중복 안내가 된다.</para>
    /// </summary>
    public static class AuthGuard
    {
        /// <summary>세션이 끊겼을 때 돌아갈 씬.</summary>
        private const string TitleSceneName = "TitleScene";

        private const string ModalTitle = "로그인 오류";
        private const string ModalMessage = "로그인 정보가 유효하지 않습니다.\n타이틀 화면으로 돌아갑니다.";

        /// <summary>인증 실패를 화면이 직접 처리하는 계정 API 경로(자동 로그인 검증·로그인·회원가입).</summary>
        private const string SelfHandledPathPrefix = "/api/auth/";

        // 세션이 끊기면 진행 중이던 요청 여러 건이 한꺼번에 401로 돌아오므로, 안내·전환은 한 번만 한다.
        private static bool _handled;

        /// <summary>
        /// 이 오류가 "로그인 정보 무효"인가 — 서버 봉투의 에러 코드(1004·1005)이거나,
        /// 봉투 없이 돌아온 인증 계열 HTTP 상태(401 Unauthorized·403 Forbidden)인 경우다.
        /// <para>그 밖의 4xx(예: 400 잘못된 요청)는 로그인 정보와 무관하므로 포함하지 않는다 —
        /// 요청 하나가 잘못된 것으로 세션을 끊으면 플레이가 통째로 날아간다.</para>
        /// </summary>
        public static bool IsAuthFailure(NetworkError error)
        {
            if (error == null)
            {
                return false;
            }
            if (error.ErrorCode == ErrorCode.InvalidToken || error.ErrorCode == ErrorCode.ExpiredToken)
            {
                return true;
            }
            return error.HttpStatus == 401 || error.HttpStatus == 403;
        }

        /// <summary>
        /// 인증 실패라면 세션을 끝내고(안내 모달 + 타이틀 복귀) <c>true</c>를 반환한다.
        /// <c>true</c>면 <see cref="NetworkManager"/>는 호출측 <c>onError</c>를 부르지 않는다 —
        /// 각 화면의 실패 문구가 이 안내를 덮어써 원인이 가려지는 것을 막기 위함이다.
        /// </summary>
        /// <param name="url">요청 URL(계정 API 예외 판정에 쓴다).</param>
        /// <param name="error">해석이 끝난 실패 정보.</param>
        public static bool Handle(string url, NetworkError error)
        {
            if (!IsAuthFailure(error) || IsSelfHandledPath(url))
            {
                return false;
            }
            if (_handled)
            {
                return true;   // 같은 세션 끊김으로 뒤따라 실패한 요청들 — 조용히 삼킨다.
            }
            _handled = true;
            Debug.LogWarning($"[AuthGuard] 로그인 정보 무효({error}) → 세션 종료·타이틀 복귀: {url}");
            ReturnToTitle();
            return true;
        }

        /// <summary>다시 로그인해 세션이 살아났을 때 가드를 원상복구한다(<see cref="Session.SetAuth"/>가 호출).</summary>
        public static void Reset() => _handled = false;

        /// <summary>계정 API처럼 화면이 인증 실패를 직접 처리하는 경로인지.</summary>
        private static bool IsSelfHandledPath(string url)
            => !string.IsNullOrEmpty(url)
               && url.IndexOf(SelfHandledPathPrefix, System.StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// 세션 흔적을 지우고 타이틀 화면으로 되돌린 뒤 안내 모달을 띄운다(ESC 메뉴의 '타이틀로 돌아가기'와 같은 순서).
        /// <para>저장된 자동 로그인 세션(<see cref="SavedSession"/>)도 버린다 — 이미 서버가 무효로 판정한 토큰이라
        /// 남겨 두면 타이틀에서 실패할 검증 왕복을 한 번 더 하게 된다.</para>
        /// <para>모달은 씬 전환 <b>뒤</b>에 띄운다 — <see cref="ModalManager"/> 인스턴스는
        /// <c>DontDestroyOnLoad</c>라 전환에도 살아남고, 이 순서면 타이틀 화면 위에 안내가 남는다.</para>
        /// </summary>
        private static void ReturnToTitle()
        {
            Time.timeScale = 1f;              // 전투 슬로우모션 등 배율 복원
            UIManager.Instance?.HideAll();    // 열려 있던 패널 정리
            Session.Clear();
            SavedSession.Clear();
            MailNotifier.Clear();             // 우편함 알림 캐시 폐기(다음 계정에 이전 알림이 새지 않도록)

            if (USceneManager.GetActiveScene().name != TitleSceneName)
            {
                if (SceneManager.Instance != null)
                {
                    SceneManager.Instance.LoadScene(TitleSceneName);
                }
                else
                {
                    USceneManager.LoadScene(TitleSceneName);
                }
            }

            ModalManager.Instance?.ShowConfirm(ModalTitle, ModalMessage);
        }
    }
}
