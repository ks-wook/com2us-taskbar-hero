using System.Globalization;
using UnityEngine;

namespace TaskbarHero.Client.Managers
{
    /// <summary>
    /// <b>마지막 로그인 세션</b>을 로컬(<see cref="PlayerPrefs"/>)에 보관해 다음 실행에서 자동 로그인에 쓰는 저장소.
    /// 로그인 성공 시 저장하고(<see cref="Save"/>), 타이틀 화면이 이 값으로
    /// <c>POST /api/auth/validate</c>(계정·로그인 기획서 §5.4)를 호출해 유효성을 확인한다.
    ///
    /// <para><b>이메일도 함께 저장한다</b> — 검증 응답에는 이메일이 없고(§5.4는 토큰을 재발급하지 않는 읽기 전용 API),
    /// 자동 로그인 환영 문구에 계정 이메일을 보여 줘야 하기 때문이다. 로그인 시 사용자가 입력한 값을 그대로 둔다.</para>
    ///
    /// <para><b>접속 환경도 함께 저장한다</b> — 토큰은 그 서버(Dev/QA)에서만 유효하다. PlayerPrefs는 Dev·QA 빌드가
    /// 같은 product 이름으로 <b>공유</b>하므로(빌드 옵션 문서), 환경이 다르면 자동 로그인을 아예 시도하지 않는다.
    /// 그러지 않으면 Dev에서 저장한 토큰으로 QA에 검증 요청을 보내 늘 실패하는 왕복이 생긴다.</para>
    ///
    /// <para>토큰은 평문으로 저장된다. 노출 시 피해 범위는 서버의 토큰 만료(기본 24시간)가 제한한다는 전제이며,
    /// 그 전제는 기획서 §5.4·§7의 만료 정책과 함께 본다.</para>
    /// </summary>
    public static class SavedSession
    {
        private const string KeyUserId = "Auth_LastUserId";
        private const string KeyToken = "Auth_LastToken";
        private const string KeyEmail = "Auth_LastEmail";
        private const string KeyEnv = "Auth_LastServerEnv";

        /// <summary>
        /// 저장된 유저 ID(없으면 0).
        /// <para><b>문자열로 보관한다</b> — <see cref="PlayerPrefs"/>에는 <c>SetLong</c>이 없어 <c>SetInt</c>로 담으면
        /// <c>users.user_id</c>(BIGINT)가 <see cref="int"/> 범위를 넘는 순간 조용히 잘려 <b>다른 계정의 ID</b>가 된다.
        /// 잘린 ID로는 검증이 실패하므로 자동 로그인이 영구히 안 되는 상태로 굳는다.</para>
        /// </summary>
        public static long UserId =>
            long.TryParse(PlayerPrefs.GetString(KeyUserId, string.Empty),
                          NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
                ? id
                : 0L;

        /// <summary>저장된 인증 토큰(없으면 빈 문자열).</summary>
        public static string Token => PlayerPrefs.GetString(KeyToken, string.Empty);

        /// <summary>저장된 계정 이메일(환영 문구 표시용, 없으면 빈 문자열).</summary>
        public static string Email => PlayerPrefs.GetString(KeyEmail, string.Empty);

        /// <summary>저장 당시의 접속 환경.</summary>
        public static ServerEnvironmentKind Environment =>
            (ServerEnvironmentKind)PlayerPrefs.GetInt(KeyEnv, (int)ServerEnvironmentKind.Dev);

        /// <summary>
        /// 자동 로그인을 시도할 수 있는 상태인가 — 저장된 세션이 있고, <b>지금 접속할 환경과 같은 환경</b>에서
        /// 저장된 것인가. 다른 환경의 토큰은 검증이 반드시 실패하므로 시도하지 않는다.
        /// </summary>
        public static bool CanAutoLogin
        {
            get
            {
                if (UserId <= 0 || string.IsNullOrEmpty(Token))
                {
                    return false;
                }
                var current = NetworkManager.Instance != null
                    ? NetworkManager.Instance.CurrentEnvironment
                    : ServerEnvironment.BuildDefault;
                return Environment == current;
            }
        }

        /// <summary>로그인 성공 시 세션을 저장한다(다음 실행의 자동 로그인 대상).</summary>
        /// <param name="userId">발급받은 유저 ID.</param>
        /// <param name="token">발급받은 인증 토큰.</param>
        /// <param name="email">로그인에 쓴 계정 이메일(환영 문구에 표시).</param>
        public static void Save(long userId, string token, string email)
        {
            if (userId <= 0 || string.IsNullOrEmpty(token))
            {
                return;
            }
            var env = NetworkManager.Instance != null
                ? NetworkManager.Instance.CurrentEnvironment
                : ServerEnvironment.BuildDefault;

            PlayerPrefs.SetString(KeyUserId, userId.ToString(CultureInfo.InvariantCulture));
            PlayerPrefs.SetString(KeyToken, token);
            PlayerPrefs.SetString(KeyEmail, email ?? string.Empty);
            PlayerPrefs.SetInt(KeyEnv, (int)env);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// 저장된 세션을 지운다. 토큰이 무효로 판정됐을 때(만료·다른 기기 로그인)와
        /// <b>사용자가 명시적으로 타이틀로 돌아갔을 때</b>(로그아웃) 호출한다 —
        /// 남겨 두면 다른 계정으로 로그인할 방법이 없어진다.
        /// </summary>
        public static void Clear()
        {
            PlayerPrefs.DeleteKey(KeyUserId);
            PlayerPrefs.DeleteKey(KeyToken);
            PlayerPrefs.DeleteKey(KeyEmail);
            PlayerPrefs.DeleteKey(KeyEnv);
            PlayerPrefs.Save();
        }
    }
}
