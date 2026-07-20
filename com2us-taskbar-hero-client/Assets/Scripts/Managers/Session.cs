using TaskbarHero.Common.Dto;

namespace TaskbarHero.Client.Managers
{
    /// <summary>
    /// 로그인한 계정 정보와 게임 세이브 스냅샷을 앱 전역에서 캐싱하는 세션.
    /// 로그인 성공 시 채워지며, 이후 인증이 필요한 API 요청(GameServer 등)에서 재사용한다.
    /// 정적 클래스이므로 씬 전환에도 유지된다(도메인 리로드 시 초기화).
    /// </summary>
    public static class Session
    {
        /// <summary>로그인한 유저 ID.</summary>
        public static long UserId { get; private set; }

        /// <summary>발급받은 인증 토큰.</summary>
        public static string Token { get; private set; }

        /// <summary>계정 닉네임(회원가입 시 입력값 또는 세이브의 player.nickname).</summary>
        public static string Nickname { get; set; }

        /// <summary>/api/game/load로 가져온 게임 세이브 스냅샷(플레이어·캐릭터·재화 등).</summary>
        public static LoadDataDto GameData { get; private set; }

        /// <summary>로그인 상태 여부.</summary>
        public static bool IsLoggedIn => !string.IsNullOrEmpty(Token);

        /// <summary>로그인 인증 정보를 저장한다.</summary>
        public static void SetAuth(long userId, string token)
        {
            UserId = userId;
            Token = token;
        }

        /// <summary>load 응답으로 받은 세이브 스냅샷을 캐싱한다(닉네임도 있으면 갱신).</summary>
        public static void SetGameData(LoadDataDto data)
        {
            GameData = data;
            if (data != null && data.player != null && !string.IsNullOrEmpty(data.player.nickname))
            {
                Nickname = data.player.nickname;
            }
        }

        /// <summary>세션을 비운다(로그아웃).</summary>
        public static void Clear()
        {
            UserId = 0;
            Token = null;
            Nickname = null;
            GameData = null;
        }
    }
}
