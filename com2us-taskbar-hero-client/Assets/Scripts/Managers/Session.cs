using System;
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

        /// <summary>Login→GameScene 전환 시 정산한 오프라인 보상 결과. GameScene 진입 팝업이 소비(표시 후 <see cref="ConsumePendingOfflineReward"/>)한다.
        /// 정산할 오프라인이 없거나(3001) 이미 팝업을 띄운 뒤에는 null이다.</summary>
        public static OfflineRewardResult PendingOfflineReward { get; private set; }

        /// <summary>로그인 상태 여부.</summary>
        public static bool IsLoggedIn => !string.IsNullOrEmpty(Token);

        /// <summary>CreateCharacterScene에 게임 안(파티 편성 '+')에서 진입했는지. true면 '뒤로가기'로 GameScene 복귀 허용.
        /// 회원가입 직후 최초 캐릭터 생성 진입은 false(뒤로가기 없음).</summary>
        public static bool CreateCharacterFromGame { get; set; }

        /// <summary>인벤토리(장착 상태 포함)가 바뀌었을 때 발생. 전투 스탯 재계산 등에서 구독한다.</summary>
        public static event Action InventoryChanged;

        /// <summary>인벤토리 변경을 알린다(장착·해제·재로드 후 호출).</summary>
        public static void RaiseInventoryChanged() => InventoryChanged?.Invoke();

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

        /// <summary>
        /// 오프라인 보상 정산 결과를 캐싱된 세이브에 반영하고, GameScene 진입 팝업이 표시할 수 있게 대기시킨다.
        /// 서버가 이미 지급을 확정한 값이므로(경험치·레벨·골드), 클라이언트 캐시(파티/HUD/인벤토리 표시)를 동일 상태로 맞춘다:
        /// 각 캐릭터의 level·exp를 정산 후 값으로 갱신하고, 계정 골드(재화 타입 1)를 지급분만큼 증가시킨다.
        /// </summary>
        public static void ApplyOfflineReward(OfflineRewardResult result)
        {
            if (result == null)
            {
                return;
            }

            // 정산 후 캐릭터 상태(level·잔여 exp)를 캐시에 반영(characterId로 매칭).
            if (GameData != null && GameData.characters != null && result.characters != null)
            {
                foreach (var after in result.characters)
                {
                    if (after == null)
                    {
                        continue;
                    }
                    foreach (var c in GameData.characters)
                    {
                        if (c != null && c.characterId == after.characterId)
                        {
                            c.level = after.level;
                            c.exp = after.exp;
                            break;
                        }
                    }
                }
            }

            // 지급 골드를 계정 재화(타입 1)에 가산.
            long gold = result.rewards != null ? result.rewards.gold : 0L;
            if (gold > 0 && GameData != null && GameData.currencies != null)
            {
                foreach (var cur in GameData.currencies)
                {
                    if (cur != null && cur.currencyType == 1)
                    {
                        cur.amount += gold;
                        break;
                    }
                }
            }

            PendingOfflineReward = result;
        }

        /// <summary>대기 중인 오프라인 보상 결과를 반환하고 비운다(GameScene 팝업이 1회 소비). 없으면 null.</summary>
        public static OfflineRewardResult ConsumePendingOfflineReward()
        {
            var pending = PendingOfflineReward;
            PendingOfflineReward = null;
            return pending;
        }

        /// <summary>세션을 비운다(로그아웃).</summary>
        public static void Clear()
        {
            UserId = 0;
            Token = null;
            Nickname = null;
            GameData = null;
            PendingOfflineReward = null;
        }
    }
}
