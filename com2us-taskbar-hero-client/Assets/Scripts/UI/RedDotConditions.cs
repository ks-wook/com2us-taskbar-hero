using TaskbarHero.Client.Managers;
using TaskbarHero.Client.MasterData;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 레드닷(알림 점) 표시 조건을 한곳에서 정의하는 공용 헬퍼. 새 알림 조건이 생기면 여기에 메서드를 추가하고
    /// <see cref="RedDot.Bind"/>로 연결한다. 현재는 잔여(미사용) 스킬 포인트 여부와 미수령 보상 메일 여부를 제공한다.
    /// </summary>
    public static class RedDotConditions
    {
        /// <summary>HUD 기능 버튼(메일·가방 등)의 알림 중 하나라도 켜져 있으면 true.
        /// 햄버거 메뉴가 접혀 있으면 개별 버튼의 레드닷이 보이지 않으므로, 이 조건을 햄버거에 붙여
        /// 알림을 놓치지 않게 한다. 새 기능 버튼에 레드닷을 추가하면 여기에도 함께 넣는다.</summary>
        public static bool HasAnyMenuNotification()
        {
            return HasUnclaimedMailReward() || HasUnspentSkillPoints();
        }

        /// <summary>수령하지 않은 보상(첨부)이 남은 메일이 하나라도 있으면 true — "만료 전에 받아 가라"는 알림이다.
        /// 열람 여부와는 무관하며(조회 ≠ 수령), 판정은 <see cref="MailNotifier.HasUnclaimedReward"/>가 캐싱된
        /// 우편함 스냅샷으로 수행한다(첨부 있음 + 미수령 + 미만료).</summary>
        public static bool HasUnclaimedMailReward() => MailNotifier.HasUnclaimedReward;

        /// <summary>파티 캐릭터 중 하나라도 미사용(잔여) 스킬 포인트가 있으면 true.
        /// 스킬 포인트 = 캐릭터 레벨 파생 총량(<c>level_master.skillPoints</c>) − 이미 투자한 스킬 레벨 합(1레벨=1포인트).</summary>
        public static bool HasUnspentSkillPoints()
        {
            var gd = Session.GameData;
            if (gd == null || gd.characters == null)
            {
                return false;
            }
            MasterDataManager.EnsureLoaded();
            var db = MasterDataManager.Db;
            if (db == null)
            {
                return false;
            }
            foreach (var c in gd.characters)
            {
                if (c == null)
                {
                    continue;
                }
                int total = db.Levels.TryGetValue(c.level, out var lm) ? lm.skillPoints : 0;
                int spent = 0;
                if (gd.skills != null)
                {
                    foreach (var s in gd.skills)
                    {
                        if (s != null && s.characterId == c.characterId)
                        {
                            spent += s.level;
                        }
                    }
                }
                if (total - spent > 0)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
