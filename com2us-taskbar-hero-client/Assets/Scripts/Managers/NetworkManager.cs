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
    /// </summary>
    public class NetworkManager : MonoBehaviour
    {
        public static NetworkManager Instance { get; private set; }

        [Header("서버 주소")]
        [SerializeField] private string accountServerBaseUrl = "http://localhost:5160";
        [SerializeField] private string gameServerBaseUrl = "http://localhost:5247";

        [Header("요청 설정")]
        [Tooltip("요청 타임아웃(초). 0이면 무제한.")]
        [SerializeField] private int timeoutSeconds = 10;

        // 최초 직렬화 기본값(포트·스킴). 접속 호스트를 바꿔도 각 서버의 포트는 이 기본값을 유지한다.
        private string _defaultAccountBaseUrl;
        private string _defaultGameBaseUrl;
        private const string PrefKeyServerHost = "th_server_host";

        /// <summary>로그인 인증 토큰(캐시된 세션에서 조회). 있으면 요청 헤더(Authorization: Bearer)에 자동 첨부된다.</summary>
        public string AuthToken => Session.Token;

        /// <summary>로그인한 유저 ID(캐시된 세션에서 조회).</summary>
        public long UserId => Session.UserId;

        public string AccountServerBaseUrl => accountServerBaseUrl;
        public string GameServerBaseUrl => gameServerBaseUrl;

        /// <summary>현재 접속 서버 호스트(계정 서버 URL에서 추출). 예: "localhost".</summary>
        public string ServerHost => ExtractHost(accountServerBaseUrl);

        /// <summary>최초 기본(로컬) 접속 호스트. 서버 선택 UI의 기본값·프리셋 판정에 사용.</summary>
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

            // 직렬화된 기본 URL(포트 포함)을 보존한 뒤, 저장된 접속 호스트가 있으면 적용한다.
            _defaultAccountBaseUrl = accountServerBaseUrl;
            _defaultGameBaseUrl = gameServerBaseUrl;
            string savedHost = PlayerPrefs.GetString(PrefKeyServerHost, string.Empty);
            if (!string.IsNullOrEmpty(savedHost))
            {
                ApplyServerHost(savedHost);
            }
        }

        /// <summary>접속 서버 호스트를 바꾸고(각 서버의 스킴·포트는 기본값 유지) 다음 실행을 위해 저장한다.</summary>
        public void SetServerHost(string host)
        {
            if (ApplyServerHost(host))
            {
                PlayerPrefs.SetString(PrefKeyServerHost, ExtractHost(accountServerBaseUrl));
                PlayerPrefs.Save();
            }
        }

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
                return $"{uri.Scheme}://{h}:{uri.Port}";
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
                    onError?.Invoke(new NetworkError
                    {
                        IsTransportError = true,
                        HttpStatus = request.responseCode,
                        ErrorCode = hasEnvelope ? (ErrorCode)env.errorCode : ErrorCode.InvalidRequest,
                        Message = string.IsNullOrEmpty(request.error) ? "네트워크 오류" : request.error,
                        RawBody = responseText,
                    });
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
                        onError?.Invoke(new NetworkError
                        {
                            IsTransportError = false,
                            HttpStatus = request.responseCode,
                            ErrorCode = ErrorCode.InvalidRequest,
                            Message = "응답 JSON 파싱 실패: " + e.Message,
                            RawBody = responseText,
                        });
                        yield break;
                    }
                }

                onSuccess?.Invoke(response);
            }
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
