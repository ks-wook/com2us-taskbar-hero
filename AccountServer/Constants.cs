namespace AccountServer;

/// <summary>
/// 계정 서버 전역 상수 모음. 입력 검증 수치·DB 에러 번호처럼 서비스마다 따로 선언되면 값이 갈라지는 것을
/// 한곳으로 모은다(프로젝트 규칙: 상수는 Constants.cs에만 선언한다).
/// </summary>
public static class Constants
{
    /// <summary>회원가입·로그인 입력 검증 기준값.</summary>
    public static class Auth
    {
        /// <summary>비밀번호 최소 길이(계정/로그인 기획서 5.1).</summary>
        public const int MinPasswordLength = 6;

        /// <summary>
        /// 닉네임 최대 길이. 클라이언트 입력 UI가 이 길이까지만 보내므로 더 긴 값은 조작이거나 클라 버그다.
        /// <c>users.nickname</c>·<c>game_player.nickname</c> 컬럼(VARCHAR(50))보다 짧아 DB에 닿기 전에 걸린다.
        /// 게임 서버의 캐릭터 생성 닉네임 검증(<c>GameServer</c> <c>Constants.Player.NicknameMaxLength</c>)과 같은 값이어야 한다.
        /// </summary>
        public const int MaxNicknameLength = 12;
    }

    /// <summary>MySQL 에러 번호.</summary>
    public static class MySqlError
    {
        /// <summary>중복 키(UNIQUE) 위반.</summary>
        public const int DuplicateEntry = 1062;
    }
}
