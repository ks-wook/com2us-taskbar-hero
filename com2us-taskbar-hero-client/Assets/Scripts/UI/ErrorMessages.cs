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
                // 성장(스킬/룬) 도메인(growth 기획서 §7)
                case ErrorCode.InvalidGrowthTarget:
                    return "존재하지 않는 스킬/룬입니다.";
                case ErrorCode.SkillMaxLevel:
                    return "이미 최대 레벨입니다.";
                case ErrorCode.InsufficientSkillPoint:
                    return "스킬 포인트가 부족합니다.";
                case ErrorCode.SkillClassMismatch:
                    return "이 캐릭터의 직업 스킬이 아닙니다.";
                case ErrorCode.SkillNotActive:
                    return "액티브 스킬만 장착할 수 있습니다.";
                case ErrorCode.SkillNotLearned:
                    return "먼저 스킬을 습득하세요.";
                case ErrorCode.ActiveSkillLimitExceeded:
                    return "액티브 스킬은 최대 2개까지 장착할 수 있습니다.";
                case ErrorCode.InvalidCharacterId:
                    return "잘못된 캐릭터입니다.";
                case ErrorCode.RunePrereqNotMet:
                    return "선행 룬을 먼저 해금하세요.";
                case ErrorCode.RuneMaxLevel:
                    return "이미 최대 레벨입니다.";
                case ErrorCode.InsufficientCurrency:
                    return "골드가 부족합니다.";
                case ErrorCode.InventoryCapacityMax:
                    return "인벤토리가 이미 최대 용량입니다.";
                case ErrorCode.InventoryFull:
                    return "인벤토리가 가득 차 받을 수 없습니다.";
                // 메일(우편함) 도메인(mail 기획서 §7)
                case ErrorCode.MailNotFound:
                    return "메일을 찾을 수 없습니다.";
                case ErrorCode.MailAlreadyClaimed:
                    return "이미 수령한 메일입니다.";
                case ErrorCode.MailExpired:
                    return "만료되어 수령할 수 없는 메일입니다.";
                default:
                    return string.IsNullOrEmpty(fallback) ? ("오류가 발생했습니다. (" + code + ")") : fallback;
            }
        }
    }
}
