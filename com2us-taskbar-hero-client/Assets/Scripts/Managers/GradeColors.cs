using UnityEngine;

namespace TaskbarHero.Client.Managers
{
    /// <summary>
    /// 아이템 등급(1~5: 노말·고급·희귀·영웅·전설)별 UI 색을 한곳에서 정의하는 공용 헬퍼.
    /// 인벤토리(<c>TaskbarHero.Client.UI</c>)와 보상 연출(<c>TaskbarHero.Client.Battle</c>)이 모두
    /// <c>Managers</c> 어셈블리를 참조하므로, 등급 색 계약을 이 하위 어셈블리에 두어 두 화면이 공유한다.
    /// </summary>
    public static class GradeColors
    {
        /// <summary>등급별 배경색(인벤토리 격자용). 노말은 배경 없음(투명), 등급이 높을수록 뚜렷한 색.</summary>
        public static Color Background(int grade)
        {
            switch (grade)
            {
                case 5: return new Color(0.95f, 0.80f, 0.15f, 0.55f); // 전설(노랑)
                case 4: return new Color(0.65f, 0.35f, 0.95f, 0.50f); // 영웅(보라)
                case 3: return new Color(0.25f, 0.55f, 0.95f, 0.50f); // 희귀(파랑)
                case 2: return new Color(0.30f, 0.80f, 0.40f, 0.45f); // 고급(초록)
                default: return new Color(0f, 0f, 0f, 0f);            // 노말(배경 없음)
            }
        }

        /// <summary>
        /// 등급별 보상 슬롯 배경색(스테이지 클리어 보상 등 슬롯 스프라이트가 없는 곳).
        /// 인벤토리 배경과 달리 노말도 포함해 항상 불투명한 슬롯으로 보이며, 아이콘/수량 텍스트가 읽히도록 어둡게 유지한다.
        /// </summary>
        public static Color RewardSlotBackground(int grade)
        {
            switch (grade)
            {
                case 5: return new Color(0.34f, 0.26f, 0.08f, 0.96f); // 전설(짙은 금색)
                case 4: return new Color(0.24f, 0.13f, 0.34f, 0.96f); // 영웅(짙은 보라)
                case 3: return new Color(0.11f, 0.20f, 0.36f, 0.96f); // 희귀(짙은 파랑)
                case 2: return new Color(0.12f, 0.28f, 0.16f, 0.96f); // 고급(짙은 초록)
                default: return new Color(0.12f, 0.14f, 0.22f, 0.95f); // 노말(기존 어두운 슬롯)
            }
        }

        /// <summary>등급별 아이콘 폴백 색(실아이콘 없을 때만 사용).</summary>
        public static Color IconFallback(int grade)
        {
            switch (grade)
            {
                case 5: return new Color(0.95f, 0.55f, 0.20f); // 전설(주황)
                case 4: return new Color(0.70f, 0.45f, 0.95f); // 영웅(보라)
                case 3: return new Color(0.30f, 0.55f, 0.95f); // 희귀(파랑)
                case 2: return new Color(0.35f, 0.80f, 0.45f); // 고급(초록)
                default: return new Color(0.75f, 0.78f, 0.82f); // 노말(회색)
            }
        }

        /// <summary>등급별 이름 텍스트 색.</summary>
        public static Color Name(int grade)
        {
            switch (grade)
            {
                case 5: return new Color(1f, 0.85f, 0.25f);    // 전설(노랑)
                case 4: return new Color(0.80f, 0.55f, 1f);    // 영웅(보라)
                case 3: return new Color(0.45f, 0.70f, 1f);    // 희귀(파랑)
                case 2: return new Color(0.45f, 0.90f, 0.55f); // 고급(초록)
                default: return Color.white;                    // 노말(흰색)
            }
        }
    }
}
