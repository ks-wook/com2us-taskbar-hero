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
    /// </summary>
    public class NetworkManager : MonoBehaviour
    {
        public static NetworkManager Instance { get; private set; }

        [Header("서버 주소 (실행 시 빌드 옵션 = ServerEnvironment 프리셋으로 덮어씀 · 표시용)")]
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

        // 접속 호스트 override 저장 키. 환경마다 별개로 저장한다 — Dev에서 저장한 "localhost"가
        // QA 빌드의 주소를 덮어써 접속이 깨지는 것을 막기 위함이다.
        private const string PrefKeyServerHostPrefix = "th_server_host_";

        /// <summary>로그인 인증 토큰(캐시된 세션에서 조회). 있으면 요청 헤더(Authorization: Bearer)에 자동 첨부된다.</summary>
        public string AuthToken => Session.Token;

        /// <summary>로그인한 유저 ID(캐시된 세션에서 조회).</summary>
        public long UserId => Session.UserId;

        public string AccountServerBaseUrl => accountServerBaseUrl;
        public string GameServerBaseUrl => gameServerBaseUrl;

        /// <summary>현재 접속 환경(빌드 옵션). 기본값은 빌드에 구워진 환경이고, 서버 선택 화면에서 이 실행 동안만 바꿀 수 있다.</summary>
        public ServerEnvironmentKind CurrentEnvironment => _environment;

        /// <summary>현재 접속 서버 호스트(계정 서버 URL에서 추출). 예: "localhost".</summary>
        public string ServerHost => ExtractHost(accountServerBaseUrl);

        /// <summary>현재 환경의 프리셋 호스트(호스트 override 없는 상태의 주소). 서버 선택 UI의 기본값·프리셋 판정에 사용.</summary>
        public string DefaultServerHost => ExtractHost(string.IsNullOrEmpty(_defaultAccountBaseUrl) ? accountServerBaseUrl : _defaultAccountBaseUrl);

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            // 인스펙터 값이 아니라 빌드 옵션(환경) 프리셋을 정본으로 삼고, 그 환경에 저장된 접속 호스트가 있으면 적용한다.
            // 단 접속처가 고정된 빌드(QA)에서는 저장된 호스트 override를 적용하지 않는다 — 그 빌드는 서버 선택
            // 화면도 뜨지 않으므로, 옛 빌드에서 저장된 호스트가 남아 있으면 되돌릴 방법 없이 원격이 아닌 곳으로 붙는다.
            ApplyEnvironment(ServerEnvironment.BuildDefault);
            if (ServerEnvironment.AllowServerSelection)
            {
                string savedHost = PlayerPrefs.GetString(HostPrefKey(_environment), string.Empty);
                if (!string.IsNullOrEmpty(savedHost))
                {
                    ApplyServerHost(savedHost);
                }
            }

            Debug.Log($"[NET] 접속 환경={ServerEnvironment.DisplayNameOf(_environment)}(빌드 옵션)" +
                      $" account={accountServerBaseUrl} game={gameServerBaseUrl}");
        }

        /// <summary>접속 환경을 바꾼다(이 실행에만 적용 — 저장하지 않는다). 계정·게임 서버 주소를 그 환경의 프리셋으로 되돌린다.</summary>
        public void SetEnvironment(ServerEnvironmentKind kind)
        {
            if (_environment == kind)
            {
                return;   // 같은 환경이면 호스트 override를 날리지 않는다.
            }
            ApplyEnvironment(kind);
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

        /// <summary>접속 서버 호스트를 바꾸고(각 서버의 스킴·포트는 환경 프리셋 유지) 다음 실행을 위해 환경별로 저장한다.
        /// 환경 프리셋과 같은 호스트면 저장하지 않고 지운다 — 프리셋 주소가 나중에 바뀌었을 때 옛 호스트가 남아
        /// 새 주소를 덮어쓰는 것을 막기 위함이다.</summary>
        public void SetServerHost(string host)
        {
            if (!ApplyServerHost(host))
            {
                return;
            }

            string key = HostPrefKey(_environment);
            string applied = ExtractHost(accountServerBaseUrl);
            if (string.Equals(applied, ExtractHost(_defaultAccountBaseUrl), StringComparison.OrdinalIgnoreCase))
            {
                PlayerPrefs.DeleteKey(key);
            }
            else
            {
                PlayerPrefs.SetString(key, applied);
            }
            PlayerPrefs.Save();
        }

        /// <summary>환경별 접속 호스트 override 저장 키.</summary>
        private static string HostPrefKey(ServerEnvironmentKind kind)
            => PrefKeyServerHostPrefix + ServerEnvironment.DisplayNameOf(kind).ToLowerInvariant();

        /// <summary>입력 호스트로 계정·게임 서버 base URL을 재구성한다(저장 없이). 유효하면 true.</summary>
        private bool ApplyServerHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return false;
            }
            string accountBase = string.IsNullOrEmpty(_defaultAccountBaseUrl) ? accountServerBaseUrl : _defaultAccountBaseUrl;
            string gameBase = string.IsNullOrEmpty(_defaultGameBaseUrl) ? gameServerBaseUrl : _defaultGameBaseUrl;
            accountServerBaseUrl = ReplaceHost(accountBase, host);
            gameServerBaseUrl = ReplaceHost(gameBase, host);
            return true;
        }

        /// <summary>URL에서 호스트명만 추출한다(파싱 실패 시 원본 반환).</summary>
        private static string ExtractHost(string url)
        {
            try { return new Uri(url).Host; }
            catch { return url; }
        }

        /// <summary>base URL의 호스트만 newHost로 교체한다(스킴·포트 유지). newHost에 스킴/포트가 섞여 있으면 제거한다.</summary>
        private static string ReplaceHost(string baseUrl, string newHost)
        {
            try
            {
                var uri = new Uri(baseUrl);
                string h = newHost.Trim();
                int scheme = h.IndexOf("://", StringComparison.Ordinal);
                if (scheme >= 0) h = h.Substring(scheme + 3);
                h = h.TrimEnd('/');
                int slash = h.IndexOf('/');
                if (slash >= 0) h = h.Substring(0, slash);
                int colon = h.IndexOf(':');
                if (colon >= 0) h = h.Substring(0, colon); // 커스텀 포트는 무시(각 서버 기본 포트 유지)
                if (string.IsNullOrEmpty(h)) return baseUrl;
                // 스킴 기본 포트(http 80·https 443)는 생략해 프리셋 주소와 같은 형태를 유지한다.
                return uri.IsDefaultPort ? $"{uri.Scheme}://{h}" : $"{uri.Scheme}://{h}:{uri.Port}";
            }
            catch
            {
                return baseUrl;
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
                    onError?.Invoke(new NetworkError
                    {
                        IsTransportError = false,
                        HttpStatus = request.responseCode,
                        ErrorCode = (ErrorCode)env.errorCode,
                        Message = env.message,
                        RawBody = responseText,
                    });
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

        /// <summary>URL에서 호스트:포트를 추출한다(파싱 실패 시 원본 반환).</summary>
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
