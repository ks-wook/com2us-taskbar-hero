using UnityEngine;

namespace TaskbarHero.Client.Battle
{
    /// <summary>
    /// 이 몬스터의 무기 스윙 이펙트(<see cref="MonsterWeaponSwingFx"/>) 색을 프리팹에 담아 두는 표식 컴포넌트.
    /// <para>붙어 있지 않으면 기본색(잿빛~주황 불티)이 쓰인다. <b>보스는 지역(Act)별 색</b>을 갖도록
    /// 이 컴포넌트를 붙여 두며, 값은 프리팹을 만드는 쪽(CharacterDevScene·몬스터 프리팹 빌더)이 채운다 —
    /// 런타임 코드가 몬스터 코드를 뒤져 색을 고르지 않는다(코드 대역 규칙이 바뀌어도 프리팹이 정본).</para>
    /// <para>색은 <b>기준색 하나만</b> 둔다. 궤적의 밝은 쪽·어두운 쪽과 불티 두 색은 여기서 파생하므로
    /// (<see cref="MonsterWeaponSwingFx"/>), 지정하는 쪽은 색 하나만 고르면 된다.</para>
    /// </summary>
    public class MonsterSwingFxPalette : MonoBehaviour
    {
        [Tooltip("무기 스윙 궤적·불티의 기준색. 밝은 쪽/어두운 쪽은 이 색에서 파생한다.")]
        [SerializeField] private Color baseColor = DefaultColor;

        /// <summary>기본 스윙 색(잿빛~주황 불티) — 팔레트를 지정하지 않은 몬스터가 쓰는 색.</summary>
        public static readonly Color DefaultColor = new Color(1f, 0.80f, 0.45f);

        /// <summary>이 몬스터의 스윙 이펙트 기준색.</summary>
        public Color BaseColor => baseColor;

        /// <summary>기준색을 지정한다(에디터 도구·CharacterDevScene 저장 경로에서 호출).</summary>
        public void SetBaseColor(Color color)
        {
            baseColor = color;
        }

        // ── 지역(Act)별 컬러링 ──
        // 각 지역 배경 아트의 지배색에 맞춰 잡았다. 보스의 스윙 이펙트가 그 지역의 색으로 보이게 하는 것이
        // 목적이라, 배경에 묻히지 않도록 배경보다 밝고 채도가 높은 쪽으로 골랐다.
        private static readonly Color Act1Green = new Color(0.30f, 0.88f, 0.48f);  // 1지역: 녹색
        private static readonly Color Act2White = new Color(0.94f, 0.96f, 1f);     // 2지역: 흰색
        private static readonly Color Act3Flame = new Color(1f, 0.42f, 0.18f);     // 3지역: 불타는 황무지 → 화염 주황
        private static readonly Color Act4Sand = new Color(1f, 0.82f, 0.34f);      // 4지역: 사막 → 금빛
        private static readonly Color Act5Void = new Color(0.68f, 0.38f, 1f);      // 5지역: 보랏빛 묘지 → 공허의 보라

        /// <summary>지역(Act 1~5) 컬러링. 범위 밖이면 기본색을 돌려준다.</summary>
        public static Color ActColor(int act)
        {
            switch (act)
            {
                case 1: return Act1Green;
                case 2: return Act2White;
                case 3: return Act3Flame;
                case 4: return Act4Sand;
                case 5: return Act5Void;
                default: return DefaultColor;
            }
        }

        /// <summary>지역 색의 사람이 읽을 이름(개발 씬 UI·로그용).</summary>
        public static string ActColorName(int act)
        {
            switch (act)
            {
                case 1: return "녹색";
                case 2: return "흰색";
                case 3: return "화염 주황";
                case 4: return "사막 금빛";
                case 5: return "공허 보라";
                default: return "기본(불티)";
            }
        }

        /// <summary>
        /// 몬스터 코드에서 지역(Act)을 읽는다 — 코드 규약 <c>90xx=Act1 … 94xx=Act5</c>
        /// (master-data-값.md §9). 규약을 벗어난 코드는 0.
        /// </summary>
        public static int ActOf(int monsterCode)
        {
            int act = (monsterCode / 100) - 89; // 9001→1 … 9499→5
            return act >= 1 && act <= 5 ? act : 0;
        }

        /// <summary>해당 코드가 그 지역의 보스인지 — 보스는 대역의 <c>xx99</c>다(§9).</summary>
        public static bool IsBossCode(int monsterCode)
        {
            return monsterCode % 100 == 99;
        }
    }
}
