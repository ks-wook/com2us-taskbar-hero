using TaskbarHero.Client.Managers;
using TaskbarHero.Client.MasterData;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 레드닷(알림 점) 표시 조건을 한곳에서 정의하는 공용 헬퍼. 새 알림 조건이 생기면 여기에 메서드를 추가하고
    /// <see cref="RedDot.Bind"/>로 연결한다. 현재는 잔여(미사용) 스킬 포인트 여부만 제공한다.
    /// </summary>
    public static class RedDotConditions
    {
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
