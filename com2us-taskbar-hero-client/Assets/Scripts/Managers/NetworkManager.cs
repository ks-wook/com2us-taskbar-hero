using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using TaskbarHero.Common;

namespace TaskbarHero.Client.Managers
{
    /// <summary>
    /// 서버 API 호출을 담당하는 매니저.
    /// UnityWebRequest 기반으로 JSON GET/POST를 수행하고, 서버 공통 응답 봉투
    /// ({ success, errorCode, message, ... })를 해석해 성공/실패 콜백으로 전달한다.
    /// 에러 코드는 서버-클라이언트 공통 계약인 <see cref="ErrorCode"/>(TaskbarHero.Common)를 사용한다.
    /// <b>전송 계층 오류</b>(연결 실패·타임아웃·봉투 없는 HTTP 오류·응답 해석 실패)는 호출측 처리와 별개로
    /// 여기서 공용 모달로 안내한다(<see cref="ReportTransportError"/>) — 화면 반응 없이 로그에만 남는
    /// 상황을 없애기 위함이다. 서버가 봉투로 응답한 논리 오류(errorCode)는 각 화면이 안내한다.
    /// <para><b>로그인 정보 무효</b>(중복 로그인으로 밀려남 1004·만료 1005·봉투 없는 401/403)만은 예외로,
    /// 화면별 안내가 아니라 <see cref="AuthGuard"/>가 세션을 끝내고 타이틀 화면으로 되돌린다 —
    /// 이때 호출측 <c>onError</c>는 호출하지 않는다(각 화면의 실패 문구가 안내를 덮어쓰지 않게).</para>
    /// </summary>
    public class NetworkManager : MonoBehaviour
    {
        public static NetworkManager Instance { get; private set; }

        [Header("서버 주소 (실행 시 ServerEnvironment 빌드 기본 환경 프리셋으로 덮어씀 · 표시용)")]
        [SerializeField] private string accountServerBaseUrl = ServerEnvironment.DevAccountBaseUrl;
        [SerializeField] private string gameServerBaseUrl = ServerEnvironment.DevGameBaseUrl;

        [Header("요청 설정")]
        [Tooltip("요청 타임아웃(초). 0이면 무제한.")]
        [SerializeField] private int timeoutSeconds = 10;

        [Tooltip("전송 계층 오류(연결 실패·타임아웃·HTTP 오류·응답 해석 실패)를 공용 모달로도 안내한다. " +
                 "끄면 로그에만 남는다.")]
        [SerializeField] private bool showNetworkErrorModal = true;

        // 서버가 죽어 여러 요청이 한꺼번에 실패할 때 같은 안내가 반복해 열리지 않도록 하는 간격(초).
        private const float NetworkErrorModalCooldown = 3f;
        private const string NetworkErrorModalTitle = "네트워크 오류";
        private float _lastNetworkErrorModalTime = -999f;

        // 현재 환경(빌드 옵션)의 프리셋 주소(스킴·포트 포함). 접속 호스트를 바꿔도 각 서버의 스킴·포트는 이 값을 유지한다.
        private string _defaultAccountBaseUrl;
        private string _defaultGameBaseUrl;
        private ServerEnvironmentKind _environment;

        // 접속 주소(호스트:포트) override 저장 키. **계정·게임 서버를 각각** 저장하고, 환경마다 별개로 둔다 —
        // 두 서버는 포트가 다르므로(Dev 5160/5247 · QA 443/8443) 호스트 하나로는 표현할 수 없고,
        // Dev에서 저장한 "localhost:5160"이 QA 빌드의 주소를 덮어써 접속이 깨지는 것도 막아야 한다.
        private const string PrefKeyAccountAuthorityPrefix = "th_server_account_";
        private const string PrefKeyGameAuthorityPrefix = "th_server_game_";

        // 포트 없이 호스트만 저장했던 시절의 키(마이그레이션 전용). 새 키가 없을 때만 호스트로 읽고
        // (포트는 환경 프리셋 유지) 다음 확정 시점에 지운다.
        private const string PrefKeyLegacyServerHostPrefix = "th_server_host_";

        // 접속 **환경**은 저장하지 않는다 — 시작 환경은 언제나 빌드에 구워진 값(ServerEnvironment.BuildDefault,
        // 에디터·Dev 빌드는 Dev)이고, 톱니바퀴로 바꾼 환경은 그 실행에만 적용된다. 저장하면 에디터에서 한 번
        // QA를 고른 뒤로 다음 플레이가 조용히 원격 서버에 붙어 버린다. 호스트:포트 override만 환경별로 저장한다.

        /// <summary>로그인 인증 토큰(캐시된 세션에서 조회). 있으면 요청 헤더(Authorization: Bearer)에 자동 첨부된다.</summary>
        public string AuthToken => Session.Token;

        /// <summary>로그인한 유저 ID(캐시된 세션에서 조회).</summary>
        public long UserId => Session.UserId;

        public string AccountServerBaseUrl => accountServerBaseUrl;
        public string GameServerBaseUrl => gameServerBaseUrl;

        /// <summary>현재 접속 환경. 시작값은 <b>언제나 빌드에 구워진 환경</b>(<see cref="ServerEnvironment.BuildDefault"/> —
        /// 에디터·Dev 빌드는 Dev)이며, '접속 서버 변경' 화면에서 바꾼 환경은 <b>그 실행 동안만</b> 유지된다.</summary>
        public ServerEnvironmentKind CurrentEnvironment => _environment;

        /// <summary>현재 계정 서버 접속 주소(<b>호스트:포트</b>). 예: "localhost:5160". 서버 선택 UI의 입력 기본값.</summary>
        public string AccountAuthority => ExtractAuthority(accountServerBaseUrl);

        /// <summary>현재 게임 서버 접속 주소(<b>호스트:포트</b>). 예: "localhost:5247". 서버 선택 UI의 입력 기본값.</summary>
        public string GameAuthority => ExtractAuthority(gameServerBaseUrl);

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            // 인스펙터 값이 아니라 빌드에 구워진 환경을 정본으로 삼는다(에디터·Dev 빌드는 Dev = 로컬 서버).
            // 지난 실행에서 톱니바퀴로 고른 환경은 되살리지 않고, 그 환경에 저장된 접속 호스트 override만 얹는다.
            ApplyEnvironment(ServerEnvironment.BuildDefault);
            ApplySavedAuthorities();

            Debug.Log($"[NET] 접속 환경={ServerEnvironment.DisplayNameOf(_environment)}(빌드 옵션)" +
                      $" account={accountServerBaseUrl} game={gameServerBaseUrl}");
        }

        /// <summary>
        /// 접속 환경과 계정·게임 서버 접속 주소(<b>호스트:포트</b>)를 확정하고 <b>다음 실행을 위해 저장</b>한다.
        /// 접속 서버 선택 UI의 '확인'이 쓰는 단일 확정 경로다 — 환경 프리셋(스킴·포트)을 먼저 적용한 뒤
        /// 입력한 주소를 얹으므로, 포트를 비워 두면 그 환경 프리셋의 포트가 유지된다.
        /// <b>저장되는 것은 호스트:포트 override뿐이다</b> — 고른 <b>환경</b>은 그 실행에만 적용되고, 다음 실행은
        /// 다시 빌드에 구워진 환경(에디터·Dev 빌드는 Dev)으로 시작한다.
        /// </summary>
        public void SetServerEndpoints(ServerEnvironmentKind kind, string accountAuthority, string gameAuthority)
        {
            if (_environment != kind)
            {
                ApplyEnvironment(kind);   // 프리셋으로 되돌린 뒤 아래에서 입력 주소를 얹는다
            }
            accountServerBaseUrl = BuildUrl(_defaultAccountBaseUrl, accountAuthority);
            gameServerBaseUrl = BuildUrl(_defaultGameBaseUrl, gameAuthority);
            SaveAuthorities();
        }

        /// <summary>현재 환경에 저장된 접속 주소(호스트:포트) override가 있으면 적용한다(없으면 환경 프리셋 주소를 그대로 둔다).
        /// 새 키가 하나도 없고 <b>포트 없던 시절의 호스트 키</b>만 있으면 그 호스트를 두 서버에 적용한다(포트는 프리셋 유지).</summary>
        private void ApplySavedAuthorities()
        {
            string account = PlayerPrefs.GetString(AuthorityPrefKey(PrefKeyAccountAuthorityPrefix, _environment), string.Empty);
            string game = PlayerPrefs.GetString(AuthorityPrefKey(PrefKeyGameAuthorityPrefix, _environment), string.Empty);
            if (string.IsNullOrEmpty(account) && string.IsNullOrEmpty(game))
            {
                string legacyHost = PlayerPrefs.GetString(AuthorityPrefKey(PrefKeyLegacyServerHostPrefix, _environment), string.Empty);
                account = legacyHost;
                game = legacyHost;
            }
            if (!string.IsNullOrWhiteSpace(account))
            {
                accountServerBaseUrl = BuildUrl(_defaultAccountBaseUrl, account);
            }
            if (!string.IsNullOrWhiteSpace(game))
            {
                gameServerBaseUrl = BuildUrl(_defaultGameBaseUrl, game);
            }
        }

        /// <summary>환경 프리셋 주소를 현재 주소로 적용한다(스킴·호스트·포트 전부). 호스트 override의 기준값도 이 값이 된다.</summary>
        private void ApplyEnvironment(ServerEnvironmentKind kind)
        {
            _environment = kind;
            _defaultAccountBaseUrl = ServerEnvironment.AccountBaseUrlOf(kind);
            _defaultGameBaseUrl = ServerEnvironment.GameBaseUrlOf(kind);
            accountServerBaseUrl = _defaultAccountBaseUrl;
            gameServerBaseUrl = _defaultGameBaseUrl;
        }

        /// <summary>계정·게임 서버의 접속 주소(호스트:포트)를 환경별로 저장한다. 환경 프리셋과 같으면 저장하지 않고 지운다 —
        /// 프리셋 주소가 나중에 바뀌었을 때 옛 값이 남아 새 주소를 덮어쓰는 것을 막기 위함이다.
        /// 포트 없던 시절의 호스트 키도 함께 지운다(새 키가 정본이므로 남겨 둘 이유가 없다).</summary>
        private void SaveAuthorities()
        {
            SaveAuthority(PrefKeyAccountAuthorityPrefix, accountServerBaseUrl, _defaultAccountBaseUrl);
            SaveAuthority(PrefKeyGameAuthorityPrefix, gameServerBaseUrl, _defaultGameBaseUrl);
            PlayerPrefs.DeleteKey(AuthorityPrefKey(PrefKeyLegacyServerHostPrefix, _environment));
            PlayerPrefs.Save();
        }

        /// <summary>한 서버의 접속 주소를 저장한다(환경 프리셋과 같으면 키를 지운다).</summary>
        private void SaveAuthority(string prefix, string url, string presetUrl)
        {
            string key = AuthorityPrefKey(prefix, _environment);
            string applied = ExtractAuthority(url);
            if (string.Equals(applied, ExtractAuthority(presetUrl), StringComparison.OrdinalIgnoreCase))
            {
                PlayerPrefs.DeleteKey(key);
            }
            else
            {
                PlayerPrefs.SetString(key, applied);
            }
        }

        /// <summary>환경별 접속 주소 override 저장 키.</summary>
        private static string AuthorityPrefKey(string prefix, ServerEnvironmentKind kind)
            => prefix + ServerEnvironment.DisplayNameOf(kind).ToLowerInvariant();

        /// <summary>프리셋 URL의 스킴에 입력 주소(<c>호스트[:포트]</c>)를 얹어 base URL을 만든다.
        /// 포트를 생략하면 프리셋 포트를 유지하고, 스킴·경로가 섞여 들어오면 떼어낸다.
        /// 포트 자리에 숫자가 아닌 값이 오면 경고를 남기고 프리셋 포트를 쓴다. 파싱 실패 시 프리셋을 그대로 반환한다.</summary>
        private static string BuildUrl(string presetUrl, string authority)
        {
            try
            {
                var uri = new Uri(presetUrl);
                string a = (authority ?? string.Empty).Trim();
                int scheme = a.IndexOf("://", StringComparison.Ordinal);
                if (scheme >= 0) a = a.Substring(scheme + 3);
                int slash = a.IndexOf('/');
                if (slash >= 0) a = a.Substring(0, slash);

                string host = a;
                int port = uri.Port;
                int colon = a.LastIndexOf(':');
                if (colon >= 0)
                {
                    host = a.Substring(0, colon);
                    string portText = a.Substring(colon + 1);
                    if (int.TryParse(portText, out int parsed) && parsed > 0 && parsed <= 65535)
                    {
                        port = parsed;
                    }
                    else if (portText.Length > 0)
                    {
                        Debug.LogWarning($"[NET] 포트 값을 해석할 수 없어 프리셋 포트({port})를 쓴다: {authority}");
                    }
                }
                if (string.IsNullOrEmpty(host))
                {
                    return presetUrl;
                }
                // 스킴 기본 포트(http 80·https 443)는 생략해 프리셋 주소와 같은 형태를 유지한다.
                bool defaultPort = (uri.Scheme == Uri.UriSchemeHttps && port == 443)
                                   || (uri.Scheme == Uri.UriSchemeHttp && port == 80);
                return defaultPort ? $"{uri.Scheme}://{host}" : $"{uri.Scheme}://{host}:{port}";
            }
            catch
            {
                return presetUrl;
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        // ---- 서버별 편의 메서드 ----

        public Coroutine PostToAccount<TResponse>(string path, object body, Action<TResponse> onSuccess, Action<NetworkError> onError)
            => Post(CombineUrl(accountServerBaseUrl, path), body, onSuccess, onError);

        public Coroutine PostToGame<TResponse>(string path, object body, Action<TResponse> onSuccess, Action<NetworkError> onError)
            => Post(CombineUrl(gameServerBaseUrl, path), body, onSuccess, onError);

        public Coroutine GetFromAccount<TResponse>(string path, Action<TResponse> onSuccess, Action<NetworkError> onError)
            => Get(CombineUrl(accountServerBaseUrl, path), onSuccess, onError);

        public Coroutine GetFromGame<TResponse>(string path, Action<TResponse> onSuccess, Action<NetworkError> onError)
            => Get(CombineUrl(gameServerBaseUrl, path), onSuccess, onError);

        // ---- 범용 GET/POST ----

        /// <summary>지정 URL로 GET 요청을 보낸다.</summary>
        public Coroutine Get<TResponse>(string url, Action<TResponse> onSuccess, Action<NetworkError> onError)
            => StartCoroutine(SendRequest("GET", url, null, onSuccess, onError));

        /// <summary>지정 URL로 JSON body를 담아 POST 요청을 보낸다.</summary>
        public Coroutine Post<TResponse>(string url, object body, Action<TResponse> onSuccess, Action<NetworkError> onError)
            => StartCoroutine(SendRequest("POST", url, body, onSuccess, onError));

        private IEnumerator SendRequest<TResponse>(
            string method, string url, object body, Action<TResponse> onSuccess, Action<NetworkError> onError)
        {
            using (var request = new UnityWebRequest(url, method))
            {
                request.timeout = timeoutSeconds;
                request.downloadHandler = new DownloadHandlerBuffer();

                if (body != null)
                {
                    byte[] payload = Encoding.UTF8.GetBytes(JsonUtility.ToJson(body));
                    request.uploadHandler = new UploadHandlerRaw(payload);
                    request.SetRequestHeader("Content-Type", "application/json");
                }

                if (!string.IsNullOrEmpty(AuthToken))
                {
                    request.SetRequestHeader("Authorization", "Bearer " + AuthToken);
                }

                Debug.Log($"[NET] → {method} {url}");

                yield return request.SendWebRequest();

                string responseText = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;

                ResponseEnvelope env = null;
                bool hasEnvelope = HasEnvelope(responseText) && TryGetEnvelope(responseText, out env);

                Debug.Log($"[NET] ← {(int)request.responseCode} {method} {url}" +
                    (hasEnvelope ? $" (errorCode={env.errorCode})" : string.Empty));

                // 1) 서버 논리 오류: HTTP 4xx/5xx라도 봉투가 있고 success == false면 논리 오류로 처리한다.
                //    (서버는 로그인 실패 등을 401/409 + { success:false, errorCode } 형태로 반환한다.)
                if (hasEnvelope && !env.success)
                {
                    var logicError = new NetworkError
                    {
                        IsTransportError = false,
                        HttpStatus = request.responseCode,
                        ErrorCode = (ErrorCode)env.errorCode,
                        Message = env.message,
                        RawBody = responseText,
                    };
                    // 로그인 정보 무효(1004·1005 / 401·403)는 화면별 실패 문구가 아니라 세션 종료로 다룬다.
                    if (!AuthGuard.Handle(url, logicError))
                    {
                        onError?.Invoke(logicError);
                    }
                    yield break;
                }

                // 2) 전송/프로토콜 오류: 연결 실패·타임아웃·봉투 없는 HTTP 오류 등
                if (request.result != UnityWebRequest.Result.Success)
                {
                    var transportError = new NetworkError
                    {
                        IsTransportError = true,
                        HttpStatus = request.responseCode,
                        ErrorCode = hasEnvelope ? (ErrorCode)env.errorCode : ErrorCode.InvalidRequest,
                        Message = string.IsNullOrEmpty(request.error) ? "네트워크 오류" : request.error,
                        RawBody = responseText,
                    };
                    // 봉투 없이 돌아온 401·403도 세션 종료로 다룬다(네트워크 오류 안내로 흘리지 않는다).
                    if (AuthGuard.Handle(url, transportError))
                    {
                        yield break;
                    }
                    ReportTransportError(DescribeTransportKind(request.result, request.error), method, url, transportError);
                    onError?.Invoke(transportError);
                    yield break;
                }

                // 3) 성공 → 역직렬화
                if (typeof(TResponse) == typeof(string))
                {
                    onSuccess?.Invoke((TResponse)(object)responseText);
                    yield break;
                }

                TResponse response = default;
                if (!string.IsNullOrEmpty(responseText))
                {
                    try
                    {
                        response = JsonUtility.FromJson<TResponse>(responseText);
                    }
                    catch (Exception e)
                    {
                        var parseError = new NetworkError
                        {
                            IsTransportError = false,
                            HttpStatus = request.responseCode,
                            ErrorCode = ErrorCode.InvalidRequest,
                            Message = "응답 JSON 파싱 실패: " + e.Message,
                            RawBody = responseText,
                        };
                        ReportTransportError("응답 해석 실패", method, url, parseError);
                        onError?.Invoke(parseError);
                        yield break;
                    }
                }

                onSuccess?.Invoke(response);
            }
        }

        /// <summary>
        /// 전송 계층 오류(연결 실패·타임아웃·봉투 없는 HTTP 오류·응답 해석 실패)를 경고 로그로 남기고,
        /// <b>공용 모달로도 안내</b>한다 — 서버가 내려갔거나 주소가 틀렸을 때 화면에는 아무 반응이 없고
        /// 로그에만 흔적이 남는 상황을 없애기 위함이다(어떤 요청이 왜 실패했는지 사용자에게 그대로 노출).
        /// 서버가 봉투로 응답한 <b>논리 오류(errorCode)는 여기서 다루지 않는다</b> — 그쪽은 각 화면이
        /// 사용자 문구(<c>ErrorMessages</c>)로 이미 안내하므로 중복 안내가 된다.
        /// 호출측이 자체 모달을 띄우는 화면(로그인 등)은 이 안내 직후 같은 모달을 덮어써 더 구체적인
        /// 문구를 보여주게 된다(공용 모달 인스턴스가 하나이므로 창이 쌓이지 않는다).
        /// </summary>
        private void ReportTransportError(string kind, string method, string url, NetworkError error)
        {
            Debug.LogWarning($"[NET] {kind}: {method} {url} → {error}");

            if (!showNetworkErrorModal || ModalManager.Instance == null)
            {
                return;
            }
            // 서버 다운 등으로 여러 요청이 동시에 실패할 때 같은 안내가 반복 개폐되지 않도록 간격을 둔다.
            if (Time.unscaledTime - _lastNetworkErrorModalTime < NetworkErrorModalCooldown)
            {
                return;
            }
            _lastNetworkErrorModalTime = Time.unscaledTime;
            ModalManager.Instance.ShowConfirm(NetworkErrorModalTitle, BuildNetworkErrorMessage(kind, method, url, error));
        }

        /// <summary>모달에 표시할 오류 내역을 만든다(오류 종류·요청 대상·HTTP 상태·원인 메시지).
        /// 공용 모달의 본문 영역은 <b>네 줄까지만</b> 보이므로(그 아래는 버튼에 가린다) 줄 수를 그 안에 맞추고,
        /// 원인 문구는 길면 잘라 한 줄을 넘기지 않게 한다.</summary>
        private static string BuildNetworkErrorMessage(string kind, string method, string url, NetworkError error)
        {
            var sb = new StringBuilder();
            sb.Append(kind).Append('\n');
            sb.Append("요청: ").Append(method).Append(' ').Append(ExtractAuthority(url)).Append(ExtractPath(url));
            if (error.HttpStatus > 0)
            {
                sb.Append('\n').Append("HTTP 상태: ").Append(error.HttpStatus);
            }
            if (!string.IsNullOrEmpty(error.Message))
            {
                sb.Append('\n').Append("원인: ").Append(Shorten(error.Message, 46));
            }
            return sb.ToString();
        }

        /// <summary>모달 한 줄을 넘기지 않도록 긴 문구를 잘라낸다(파싱 예외 메시지 등).</summary>
        private static string Shorten(string text, int max)
            => string.IsNullOrEmpty(text) || text.Length <= max ? text : text.Substring(0, max) + "…";

        /// <summary>전송 계층 실패의 종류를 사람이 읽을 수 있는 문구로 분류한다(연결/타임아웃/HTTP/데이터).</summary>
        private static string DescribeTransportKind(UnityWebRequest.Result result, string error)
        {
            switch (result)
            {
                case UnityWebRequest.Result.ConnectionError:
                    return !string.IsNullOrEmpty(error) && error.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0
                        ? "요청 시간이 초과되었습니다."
                        : "서버에 연결할 수 없습니다.";
                case UnityWebRequest.Result.ProtocolError:
                    return "서버가 오류 응답을 반환했습니다.";
                case UnityWebRequest.Result.DataProcessingError:
                    return "응답 데이터를 처리하지 못했습니다.";
                default:
                    return "네트워크 요청에 실패했습니다.";
            }
        }

        /// <summary>URL에서 경로만 추출한다(파싱 실패 시 원본 반환).</summary>
        private static string ExtractPath(string url)
        {
            try { return new Uri(url).AbsolutePath; }
            catch { return url; }
        }

        /// <summary>URL에서 접속 주소(호스트:포트)를 추출한다. 스킴 기본 포트(http 80·https 443)도 <b>생략하지 않고</b>
        /// 붙인다 — 접속처를 포트까지 명시해 보여 주고(서버 선택 UI의 입력 기본값) 저장값 비교에 쓰기 위함이다.
        /// 요청 로그의 축약 표기도 이 값을 쓴다. 파싱 실패 시 원본 반환.</summary>
        private static string ExtractAuthority(string url)
        {
            try { var uri = new Uri(url); return uri.Host + ":" + uri.Port; }
            catch { return url; }
        }

        private static bool HasEnvelope(string text)
            => !string.IsNullOrEmpty(text) && text.IndexOf("\"success\"", StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool TryGetEnvelope(string text, out ResponseEnvelope envelope)
        {
            envelope = null;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            try
            {
                envelope = JsonUtility.FromJson<ResponseEnvelope>(text);
                return envelope != null;
            }
            catch
            {
                return false;
            }
        }

        private static string CombineUrl(string baseUrl, string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return baseUrl;
            }

            return baseUrl.TrimEnd('/') + "/" + path.TrimStart('/');
        }

        /// <summary>서버 공통 응답 봉투. JsonUtility가 camelCase(JSON)와 매칭되도록 필드명을 맞춘다.</summary>
        [Serializable]
        private class ResponseEnvelope
        {
            public bool success;
            public int errorCode;
            public string message;
        }
    }

    /// <summary>네트워크 요청 실패 정보.</summary>
    public class NetworkError
    {
        /// <summary>true면 연결/타임아웃/프로토콜 등 전송 계층 오류, false면 서버가 응답한 논리 오류.</summary>
        public bool IsTransportError;

        /// <summary>HTTP 상태 코드.</summary>
        public long HttpStatus;

        /// <summary>서버 공통 에러 코드(TaskbarHero.Common).</summary>
        public ErrorCode ErrorCode;

        /// <summary>사람이 읽을 수 있는 오류 메시지.</summary>
        public string Message;

        /// <summary>원본 응답 본문(디버깅용).</summary>
        public string RawBody;

        public override string ToString()
            => $"NetworkError(transport={IsTransportError}, http={HttpStatus}, code={ErrorCode}, msg={Message})";
    }
}
