using TaskbarHero.Common.MasterData;
using UnityEngine;
using UnityEngine.UI;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 캐릭터 선택 패널에 표시하는 기본 능력치 종류. 열거 순서가 곧 화면 표시 순서다.
    /// class_master 의 스탯 컬럼(hp·atk·def·cooldown·crit_chance·crit_damage·move_speed)에 1:1 대응한다.
    /// </summary>
    public enum ClassStatKind
    {
        Hp = 0,
        Atk = 1,
        Def = 2,
        AttackSpeed = 3,
        CritChance = 4,
        CritDamage = 5,
        MoveSpeed = 6,
    }

    /// <summary>
    /// 기본 능력치 한 줄(라벨 + 수치 + 배경 게이지). 게이지는 전체 직업 중 최대값 대비 비율이라
    /// 직업끼리 어느 스탯이 강한지 한눈에 비교된다. 값 해석·서식은 <see cref="ClassStatInfo"/>가 맡는다.
    /// </summary>
    public class CharacterStatRow : MonoBehaviour
    {
        [SerializeField] private ClassStatKind kind;
        [SerializeField] private Text labelText;
        [SerializeField] private Text valueText;
        [SerializeField] private Image barFill;

        /// <summary>이 행이 표시하는 능력치 종류.</summary>
        public ClassStatKind Kind => kind;

        private void Awake()
        {
            // 라벨은 ClassStatInfo 를 단일 출처로 삼아 런타임에 다시 채운다(프리팹 문구가 어긋나도 교정).
            if (labelText != null)
            {
                labelText.text = ClassStatInfo.LabelOf(kind);
            }
        }

        /// <summary>수치 문자열과 게이지 비율(0~1)을 적용한다.</summary>
        public void Set(string display, float ratio)
        {
            if (valueText != null)
            {
                valueText.text = display;
            }
            if (barFill != null)
            {
                barFill.fillAmount = Mathf.Clamp01(ratio);
            }
        }
    }

    /// <summary>
    /// class_master 기본 스탯(<see cref="Stats"/>) 한 항목의 라벨·표시 문자열·게이지용 수치를 제공한다.
    /// 게이지 비교를 위해 "클수록 좋은 값"으로 정규화한다(공격 주기 cooldown 은 역수인 초당 공격 횟수로 환산).
    /// </summary>
    public static class ClassStatInfo
    {
        /// <summary>화면에 표시할 능력치 이름.</summary>
        public static string LabelOf(ClassStatKind kind)
        {
            switch (kind)
            {
                case ClassStatKind.Hp: return "체력";
                case ClassStatKind.Atk: return "공격력";
                case ClassStatKind.Def: return "방어력";
                case ClassStatKind.AttackSpeed: return "공격속도";
                case ClassStatKind.CritChance: return "치명확률";
                case ClassStatKind.CritDamage: return "치명피해";
                case ClassStatKind.MoveSpeed: return "이동속도";
                default: return string.Empty;
            }
        }

        /// <summary>게이지 정규화에 쓰는 원시 수치(클수록 좋은 방향). 공격속도는 1/cooldown(초당 공격 횟수).</summary>
        public static float RawOf(Stats stats, ClassStatKind kind)
        {
            switch (kind)
            {
                case ClassStatKind.Hp: return stats.hp;
                case ClassStatKind.Atk: return stats.atk;
                case ClassStatKind.Def: return stats.def;
                case ClassStatKind.AttackSpeed: return stats.cooldown > 0f ? 1f / stats.cooldown : 0f;
                case ClassStatKind.CritChance: return stats.critChance;
                case ClassStatKind.CritDamage: return stats.critDamage;
                case ClassStatKind.MoveSpeed: return stats.moveSpeed;
                default: return 0f;
            }
        }

        /// <summary>수치 표시 문자열(단위 포함).</summary>
        public static string FormatOf(Stats stats, ClassStatKind kind)
        {
            switch (kind)
            {
                case ClassStatKind.Hp: return stats.hp.ToString("N0");
                case ClassStatKind.Atk: return stats.atk.ToString("N0");
                case ClassStatKind.Def: return stats.def.ToString("N0");
                case ClassStatKind.AttackSpeed:
                    return stats.cooldown > 0f ? (1f / stats.cooldown).ToString("0.00") + "회/초" : "-";
                case ClassStatKind.CritChance: return (stats.critChance * 100f).ToString("0.#") + "%";
                case ClassStatKind.CritDamage: return (stats.critDamage * 100f).ToString("0") + "%";
                case ClassStatKind.MoveSpeed: return stats.moveSpeed.ToString("0.0");
                default: return "-";
            }
        }

        /// <summary>게이지 색(능력치별 구분색). 알파는 배경 게이지용으로 낮게 잡는다.</summary>
        public static Color ColorOf(ClassStatKind kind)
        {
            switch (kind)
            {
                case ClassStatKind.Hp: return new Color(0.85f, 0.30f, 0.30f, 0.45f);
                case ClassStatKind.Atk: return new Color(0.92f, 0.55f, 0.20f, 0.45f);
                case ClassStatKind.Def: return new Color(0.35f, 0.60f, 0.90f, 0.45f);
                case ClassStatKind.AttackSpeed: return new Color(0.95f, 0.82f, 0.30f, 0.45f);
                case ClassStatKind.CritChance: return new Color(0.88f, 0.42f, 0.72f, 0.45f);
                case ClassStatKind.CritDamage: return new Color(0.72f, 0.45f, 0.92f, 0.45f);
                case ClassStatKind.MoveSpeed: return new Color(0.40f, 0.80f, 0.55f, 0.45f);
                default: return new Color(1f, 1f, 1f, 0.25f);
            }
        }
    }
}
