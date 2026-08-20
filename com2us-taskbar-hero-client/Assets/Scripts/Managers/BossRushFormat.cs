using UnityEngine;

namespace TaskbarHero.Client.Managers
{
    /// <summary>보스 러시 화면의 시간 표기 헬퍼. 기록은 <c>m:ss.mmm</c>, 시즌 남은 시간은 <c>N일 HH:MM</c>이다.
    /// <para>진입 화면(<c>BossRushPanelController</c>)과 전투 중 HUD·결과 연출(<c>TaskbarHero.Client.Battle</c>)이
    /// 같은 표기를 써야 하므로 두 어셈블리가 함께 참조하는 Managers에 둔다.</para></summary>
    public static class BossRushFormat
    {
        /// <summary>클리어 기록(ms) → <c>3:34.388</c>. ms를 그대로 노출하지 않는다.</summary>
        public static string Record(int totalMs)
        {
            if (totalMs <= 0)
            {
                return "-";
            }
            int minutes = totalMs / 60000;
            int seconds = totalMs / 1000 % 60;
            int millis = totalMs % 1000;
            return $"{minutes}:{seconds:00}.{millis:000}";
        }

        /// <summary>시즌 남은 시간(초) → <c>3일 04:12</c> / <c>04:12:33</c>(하루 미만).</summary>
        public static string Countdown(float seconds)
        {
            if (seconds <= 0f)
            {
                return "종료";
            }
            int total = Mathf.FloorToInt(seconds);
            int days = total / 86400;
            int hours = total / 3600 % 24;
            int minutes = total / 60 % 60;
            int secs = total % 60;
            return days > 0 ? $"{days}일 {hours:00}:{minutes:00}" : $"{hours:00}:{minutes:00}:{secs:00}";
        }

    }
}
