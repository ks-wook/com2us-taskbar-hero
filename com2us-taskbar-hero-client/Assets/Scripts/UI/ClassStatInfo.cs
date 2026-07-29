using TaskbarHero.Common.MasterData;
using UnityEngine;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// class_master 의 스탯 컬럼(hp·atk·def·cooldown·crit_chance·crit_damage·move_speed)에 1:1 대응하는 능력치 종류.
    /// 이 중 캐릭터 선택 패널의 5각형 레이더에 표시하는 것은 <see cref="ClassStatInfo.DisplayKinds"/> 5종이다.
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
    /// class_master 기본 스탯(<see cref="Stats"/>) 한 항목의 라벨·표시 문자열·차트용 수치를 제공한다.
    /// 비교를 위해 "클수록 좋은 값"으로 정규화한다(공격 주기 cooldown 은 역수인 초당 공격 횟수로 환산).
    /// </summary>
    public static class ClassStatInfo
    {
        /// <summary>
        /// 캐릭터 선택 패널의 5각형 레이더에 표시하는 능력치와 그 순서(맨 위 꼭짓점부터 시계 방향).
        /// 오른쪽에 공격 계열, 왼쪽에 방어/기동 계열이 오도록 배치했다. 치명확률·치명피해는 표시하지 않는다.
        /// </summary>
        public static readonly ClassStatKind[] DisplayKinds =
        {
            ClassStatKind.Hp,
            ClassStatKind.Atk,
            ClassStatKind.AttackSpeed,
            ClassStatKind.MoveSpeed,
            ClassStatKind.Def,
        };

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

        /// <summary>차트 정규화에 쓰는 원시 수치(클수록 좋은 방향). 공격속도는 1/cooldown(초당 공격 횟수).</summary>
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
                    return stats.cooldown > 0f ? (1f / stats.cooldown).ToString("0.00") + "/s" : "-";
                case ClassStatKind.CritChance: return (stats.critChance * 100f).ToString("0.#") + "%";
                case ClassStatKind.CritDamage: return (stats.critDamage * 100f).ToString("0") + "%";
                case ClassStatKind.MoveSpeed: return stats.moveSpeed.ToString("0.0");
                default: return "-";
            }
        }

        /// <summary>능력치별 구분색(레이더 라벨 색 등).</summary>
        public static Color ColorOf(ClassStatKind kind)
        {
            switch (kind)
            {
                case ClassStatKind.Hp: return new Color(0.90f, 0.45f, 0.45f);
                case ClassStatKind.Atk: return new Color(0.95f, 0.66f, 0.35f);
                case ClassStatKind.Def: return new Color(0.50f, 0.72f, 0.95f);
                case ClassStatKind.AttackSpeed: return new Color(0.96f, 0.87f, 0.45f);
                case ClassStatKind.CritChance: return new Color(0.90f, 0.55f, 0.78f);
                case ClassStatKind.CritDamage: return new Color(0.78f, 0.58f, 0.95f);
                case ClassStatKind.MoveSpeed: return new Color(0.55f, 0.87f, 0.65f);
                default: return Color.white;
            }
        }
    }
}
