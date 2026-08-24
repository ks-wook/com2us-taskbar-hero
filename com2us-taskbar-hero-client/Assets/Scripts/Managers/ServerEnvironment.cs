namespace TaskbarHero.Client.Managers
{
    /// <summary>빌드 옵션(접속 환경) 종류.</summary>
    public enum ServerEnvironmentKind
    {
        /// <summary>개발 빌드 — 로컬에서 직접 띄운 서버(http://localhost).</summary>
        Dev = 0,

        /// <summary>QA 빌드 — Tailscale로 노출한 원격 서버(https).</summary>
        Qa = 1,
    }

    /// <summary>
    /// <b>빌드 옵션별 접속 서버 주소</b>를 정의하는 정적 테이블.
    /// 환경마다 계정/게임 서버의 <b>스킴·호스트·포트가 모두 다르므로</b>(Dev는 http + 5160/5247,
    /// QA는 https + 443/8443) 호스트만 바꾸는 방식으로는 표현할 수 없어 환경 단위 프리셋으로 둔다.
    /// <para>
    /// 기본 환경은 스크립팅 정의 심볼 <see cref="QaDefineSymbol"/>(<c>TH_QA</c>) 유무로 <b>빌드 시점에 결정</b>된다
    /// (에디터 메뉴 <c>TaskbarHero/Build/…</c>의 QA 빌드가 <c>extraScriptingDefines</c>로 넣는다).
    /// </para>
    /// <para>
    /// <b>접속처는 어느 빌드에서든 바꿀 수 있고, 마지막 선택이 다음 실행까지 유지된다</b> —
    /// 타이틀 화면 우측 하단의 톱니바퀴(<c>ServerSettingsButton</c>)로 '접속 서버 변경' 화면을 열며,
    /// 확정한 환경·호스트는 <see cref="NetworkManager"/>가 PlayerPrefs에 저장해 다음 실행에 되살린다.
    /// 저장 키는 <b>빌드에 구워진 기본 환경별로 분리</b>돼 있다 — PlayerPrefs는 Dev/QA 빌드가 같은
    /// product 이름으로 공유하므로, 키가 하나면 Dev 빌드에서 고른 값이 QA 빌드의 시작 접속처를 덮어써
    /// "QA 빌드인데 로컬로 붙는" 사고가 난다.
    /// </para>
    /// <para>
    /// <b>QA 빌드는 로그인 전 서버 선택 화면을 자동으로 띄우지 않는다</b>(<see cref="ShowServerSelectOnStart"/>) —
    /// 접속처가 원격으로 정해진 배포본이라 매 실행 선택을 물을 이유가 없다. 바꿔야 할 때는 톱니바퀴로 연다.
    /// <b>예외로, 이 기기에서 접속처를 한 번도 확정하지 않았으면</b>(<see cref="NetworkManager.HasServerSelection"/>가 false)
    /// 빌드 종류와 무관하게 선택 화면을 무조건 띄운다(<c>ServerSelectPanelController.NeedsInitialSelection</c>).
    /// </para>
    /// 실제 적용은 <see cref="NetworkManager"/>가 담당한다.
    /// </summary>
    public static class ServerEnvironment
    {
        /// <summary>QA 빌드를 표시하는 스크립팅 정의 심볼. 에디터 빌더가 QA 빌드에만 넣는다.</summary>
        public const string QaDefineSymbol = "TH_QA";

        /// <summary>Dev(로컬) 계정 서버 주소.</summary>
        public const string DevAccountBaseUrl = "http://localhost:5160";

        /// <summary>Dev(로컬) 게임 서버 주소.</summary>
        public const string DevGameBaseUrl = "http://localhost:5247";

        /// <summary>QA 계정 서버 주소(Tailscale, https 기본 포트 443).</summary>
        public const string QaAccountBaseUrl = "https://ksu864-1.tail961c4e.ts.net";

        /// <summary>QA 게임 서버 주소(Tailscale, https 8443).</summary>
        public const string QaGameBaseUrl = "https://ksu864-1.tail961c4e.ts.net:8443";

        /// <summary>이 빌드에 구워진 기본 환경(정의 심볼 <c>TH_QA</c>가 있으면 QA).
        /// 저장된 선택이 없을 때의 시작 환경이자, 접속처 저장 키를 가르는 기준이다.</summary>
        public static ServerEnvironmentKind BuildDefault
        {
            get
            {
#if TH_QA
                return ServerEnvironmentKind.Qa;
#else
                return ServerEnvironmentKind.Dev;
#endif
            }
        }

        /// <summary>
        /// 로그인 직전에 '접속 서버 선택' 화면을 <b>자동으로</b> 띄우는가. <b>QA 빌드는 false</b> —
        /// 접속처가 정해진 배포본이라 매 실행 선택을 묻지 않는다.
        /// <para>이 값이 false여도 <b>접속처를 바꿀 수 없다는 뜻은 아니다</b> — 타이틀 화면의 톱니바퀴로
        /// 언제든 '접속 서버 변경'을 열 수 있다(<c>ServerSelectPanelController.ShowManual</c>).</para>
        /// <para>이 값은 <b>빌드 옵션만</b> 본다. <b>저장된 접속처가 없는 기기</b>에서는 이 값이 false여도
        /// 화면을 띄운다 — 그 판정은 <c>ServerSelectPanelController.NeedsInitialSelection</c>이 한다.</para>
        /// </summary>
        public static bool ShowServerSelectOnStart
        {
            get
            {
#if TH_QA
                return false;
#else
                return true;
#endif
            }
        }

        /// <summary>환경별 계정 서버 base URL.</summary>
        public static string AccountBaseUrlOf(ServerEnvironmentKind kind)
            => kind == ServerEnvironmentKind.Qa ? QaAccountBaseUrl : DevAccountBaseUrl;

        /// <summary>환경별 게임 서버 base URL.</summary>
        public static string GameBaseUrlOf(ServerEnvironmentKind kind)
            => kind == ServerEnvironmentKind.Qa ? QaGameBaseUrl : DevGameBaseUrl;

        /// <summary>UI·로그에 표시할 환경 이름.</summary>
        public static string DisplayNameOf(ServerEnvironmentKind kind)
            => kind == ServerEnvironmentKind.Qa ? "QA" : "Dev";
    }
}
