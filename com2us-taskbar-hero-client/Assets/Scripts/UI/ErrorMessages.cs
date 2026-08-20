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
                // 파티 편성 도메인(save-data 기획서 §5.5 / party/arrange)
                case ErrorCode.CannotRemoveLastCharacter:
                    return "파티는 최소 1명이어야 합니다.";
                case ErrorCode.CharacterNotFound:
                    return "보유하지 않은 캐릭터입니다.";
                case ErrorCode.PartySlotOccupied:
                    return "파티 자리가 올바르지 않습니다(최대 3명, 자리 중복 불가).";
                case ErrorCode.RunePrereqNotMet:
                    return "선행 룬을 먼저 해금하세요.";
                case ErrorCode.RuneMaxLevel:
                    return "이미 최대 레벨입니다.";
                case ErrorCode.InsufficientCurrency:
                    return "골드가 부족합니다.";
                case ErrorCode.InventoryCapacityMax:
                    return "인벤토리가 이미 최대 용량입니다.";
                // 장비 강화 도메인(inventory-item-cube 기획서 §5.3)
                case ErrorCode.MaxEnhanceReached:
                    return "이미 최대 강화 단계입니다.";
                case ErrorCode.ItemNotEquippable:
                    return "장비가 아니어서 강화할 수 없습니다.";
                case ErrorCode.InventoryFull:
                    return "인벤토리가 가득 차 받을 수 없습니다.";
                // 소모품/버프 도메인(consumable-buff 기획서 §7)
                case ErrorCode.ItemNotConsumable:
                    return "사용할 수 없는 아이템입니다.";
                case ErrorCode.BuffDurationLimitExceeded:
                    return "버프 지속시간이 상한(24시간)을 넘어 더 사용할 수 없습니다.";
                case ErrorCode.InsufficientQuantity:
                    return "아이템 수량이 부족합니다.";
                // 메일(우편함) 도메인(mail 기획서 §7)
                case ErrorCode.MailNotFound:
                    return "메일을 찾을 수 없습니다.";
                case ErrorCode.MailAlreadyClaimed:
                    return "이미 수령한 메일입니다.";
                case ErrorCode.MailExpired:
                    return "만료되어 수령할 수 없는 메일입니다.";
                // 출석부 도메인(attendance 기획서 §7)
                case ErrorCode.AttendanceAlreadyClaimed:
                    return "이미 수령한 출석 보상입니다.";
                // 9002(구 AttendanceAllClaimed)는 결번 — 30일차 이후 1일차로 순환하므로 사다리 소진 실패가 없다.
                // 거래소 도메인(trade 기획서 §8)
                case ErrorCode.TradeListingNotFound:
                    return "이미 사라진 거래 등록입니다.";
                case ErrorCode.TradeNotSellable:
                    return "거래소에 등록할 수 없는 아이템입니다.";
                case ErrorCode.TradeNotOwner:
                    return "본인이 등록한 매물만 취소할 수 있습니다.";
                case ErrorCode.TradeSelfPurchase:
                    return "내가 등록한 매물은 구매할 수 없습니다.";
                case ErrorCode.TradeAlreadyClosed:
                    return "이미 판매되었거나 취소된 매물입니다.";
                case ErrorCode.TradePriceOutOfRange:
                    return "등록 가격이 허용 범위(기준가 ±20%)를 벗어났습니다.";
                case ErrorCode.TradeListingLimitExceeded:
                    return "판매 등록은 최대 10개까지 가능합니다.";
                // 가챠(뽑기) 도메인(gacha 기획서 §7)
                case ErrorCode.GachaNotFound:
                    return "존재하지 않는 뽑기입니다. 클라이언트를 갱신해 주세요.";
                case ErrorCode.GachaNotAvailable:
                    return "지금은 진행 중인 뽑기가 아닙니다.";
                case ErrorCode.GachaPoolEmpty:
                    return "뽑기 데이터에 문제가 있어 취소되었습니다(골드는 차감되지 않습니다).";
                // 보스러시/랭킹 도메인(boss-rush 기획서 §7)
                case ErrorCode.BossRushLocked:
                    return "보스 러시가 아직 열리지 않았습니다. 스테이지를 더 진행해 주세요.";
                case ErrorCode.BossRushDailyLimitExceeded:
                    return "오늘 도전 횟수를 모두 사용했습니다.";
                case ErrorCode.BossRushSeasonClosed:
                    return "시즌 정산 중입니다. 잠시 후 다시 시도해 주세요.";
                case ErrorCode.BossRushRunNotFound:
                case ErrorCode.BossRushRunAlreadyFinished:
                    return "도전 기록을 등록하지 못했습니다.";
                case ErrorCode.BossRushTimeout:
                    return "제한 시간을 넘겨 기록이 등록되지 않았습니다.";
                case ErrorCode.BossRushInvalidProgress:
                    return "도전 기록이 올바르지 않아 등록되지 않았습니다.";
                default:
                    return string.IsNullOrEmpty(fallback) ? ("오류가 발생했습니다. (" + code + ")") : fallback;
            }
        }
    }
}
