using TaskbarHero.Client.Managers;
using TaskbarHero.Common;

namespace TaskbarHero.Client.UI
{
    /// <summary>서버 에러 코드/네트워크 오류를 사용자에게 보여줄 한글 메시지로 변환한다.</summary>
    public static class ErrorMessages
    {
        public static string ToKorean(NetworkError error)
        {
            if (error == null)
            {
                return "알 수 없는 오류가 발생했습니다.";
            }

            if (error.IsTransportError)
            {
                return "서버에 연결할 수 없습니다. 잠시 후 다시 시도하세요.";
            }

            return ForCode(error.ErrorCode, error.Message);
        }

        public static string ForCode(ErrorCode code, string fallback)
        {
            switch (code)
            {
                case ErrorCode.UserNotFound:
                    return "존재하지 않는 계정입니다.";
                case ErrorCode.InvalidPassword:
                    return "비밀번호가 올바르지 않습니다.";
                case ErrorCode.DuplicateEmail:
                    return "이미 사용 중인 이메일입니다.";
                case ErrorCode.InvalidRequest:
                    return "입력 값을 확인하세요.";
                case ErrorCode.InvalidToken:
                case ErrorCode.ExpiredToken:
                    return "인증이 만료되었습니다. 다시 로그인하세요.";
                default:
                    return string.IsNullOrEmpty(fallback) ? ("오류가 발생했습니다. (" + code + ")") : fallback;
            }
        }
    }
}
