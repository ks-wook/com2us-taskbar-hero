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
    /// <b>시작 접속처는 언제나 이 빌드에 구워진 환경</b>이다 — 에디터·Dev 빌드는 <b>Dev(로컬)</b>, QA 빌드는 QA.
    /// 지난 실행에서 고른 환경은 다음 실행으로 넘기지 않는다 — 에디터에서 한 번 QA를 골랐다는 이유로
    /// 다음 플레이가 조용히 원격 서버에 붙어 버리는 것을 막기 위함이다.
    /// </para>
    /// <para>
    /// <b>접속 서버 선택 화면은 자동으로 뜨지 않는다</b>(<see cref="ShowServerSelectOnStart"/>가 어느 빌드에서도 false).
    /// 접속처를 바꾸려면 타이틀 화면 우측 하단의 톱니바퀴(<c>ServerSettingsButton</c>)로 직접 연다.
    /// 거기서 확정한 <b>호스트:포트 override</b>는 환경별 키로 PlayerPrefs에 저장돼 다음 실행에도 유지되지만
    /// (Dev/QA 키가 분리돼 서로를 덮어쓰지 않는다), <b>어느 환경으로 붙을지</b>는 저장하지 않는다.
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

        /// <summary>이 빌드에 구워진 접속 환경(정의 심볼 <c>TH_QA</c>가 있으면 QA, 없으면 <b>Dev</b>).
        /// <b>실행할 때마다 이 환경으로 시작</b>한다(지난 실행의 선택은 되살리지 않는다).</summary>
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
        /// 로그인 직전에 '접속 서버 선택' 화면을 <b>자동으로</b> 띄우는가. <b>어느 빌드에서도 false</b> —
        /// 시작 접속처가 <see cref="BuildDefault"/>(에디터·Dev 빌드는 Dev)로 정해져 있으므로 매 실행 물을 이유가 없다.
        /// <para>이 값이 false여도 <b>접속처를 바꿀 수 없다는 뜻은 아니다</b> — 타이틀 화면의 톱니바퀴로
        /// 언제든 '접속 서버 변경'을 열 수 있다(<c>ServerSelectPanelController.ShowManual</c>).</para>
        /// </summary>
        public static bool ShowServerSelectOnStart => false;

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
