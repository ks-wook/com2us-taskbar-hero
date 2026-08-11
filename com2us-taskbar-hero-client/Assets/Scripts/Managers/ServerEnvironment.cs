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
    /// <b>QA 빌드는 접속처를 고정</b>한다 — 서버 선택 화면을 띄우지 않고 항상 QA(원격) 프리셋으로 접속한다
    /// (<see cref="AllowServerSelection"/>). Dev 빌드·에디터 플레이만 선택 화면에서 환경·호스트를 바꿀 수 있고,
    /// 그 선택은 <b>해당 실행에만</b> 적용된다(환경은 저장하지 않는다) — PlayerPrefs는 Dev/QA 빌드가 같은
    /// product 이름으로 공유하므로, 저장하면 Dev에서 고른 값이 QA 빌드의 기본 접속처를 덮어써
    /// "QA 빌드인데 로컬로 붙는" 사고가 난다.
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

        /// <summary>이 빌드에 구워진 기본 환경(정의 심볼 <c>TH_QA</c>가 있으면 QA). 실행마다 이 값에서 시작한다.</summary>
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
        /// 실행 중 접속처(환경·호스트) 변경을 허용하는가. <b>QA 빌드는 false</b> — 서버 선택 화면을 띄우지 않고
        /// 항상 QA(원격) 프리셋으로 접속한다(저장된 호스트 override도 무시한다). Dev 빌드·에디터 플레이는 true다.
        /// </summary>
        public static bool AllowServerSelection
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
