using System;
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

        /// <summary>제한 시간 잔여(ms) → <c>06:12</c>(분:초). 음수는 <c>00:00</c>으로 바닥을 친다.</summary>
        public static string Remaining(int totalMs)
        {
            int total = Mathf.Max(0, totalMs) / 1000;
            return $"{total / 60:00}:{total % 60:00}";
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

        /// <summary>
        /// 서버가 준 유닉스 시각(초)을 <b>기기 로컬 시각</b> <c>HH:mm</c>으로 바꾼다(일일 도전 횟수 초기화 안내용).
        /// <para>날짜 경계는 서버 KST 자정이지만 표시는 플레이어가 보는 시계를 따른다 — 서버가 시각 자체를
        /// 내려주므로(<c>dailyResetAt</c>) 클라이언트가 시간대를 계산하지 않는다.</para>
        /// </summary>
        public static string LocalClock(long unixSeconds)
        {
            if (unixSeconds <= 0L)
            {
                return "-";
            }
            return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime().ToString("HH:mm");
        }
    }
}
