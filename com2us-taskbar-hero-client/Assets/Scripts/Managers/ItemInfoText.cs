using System.Collections.Generic;
using TaskbarHero.Common.MasterData;

namespace TaskbarHero.Client.Managers
{
    /// <summary>
    /// 아이템 상세 문구(이름·등급·종류·요구조건·설명)를 마스터 데이터로 만드는 공용 헬퍼.
    /// 인벤토리·스테이지 클리어 보상·우편함(추후 거래소 포함)이 동일한 상세 문구를 쓰도록
    /// 이 어셈블리에 둔다(등급 색 계약 <see cref="GradeColors"/>와 같은 이유 —
    /// UI·Battle 어셈블리가 모두 Managers를 참조).
    /// </summary>
    public static class ItemInfoText
    {
        /// <summary>아이템 상세 팝업(<see cref="ItemDetailPopup"/>)에 표시할 문구 묶음.</summary>
        public struct Info
        {
            public string name;
            public int gradeValue;    // 등급 값(1~5) — 이름 색 결정
            public string grade;      // 등급명(노말·고급·희귀·영웅·전설)
            public string category;   // 종류(무기·보조무기·방어구·재료·재화)
            public string requirement; // 장비 요구조건(레벨·직업), 없으면 빈 문자열
            public string description; // 종류별 설명문
        }

        /// <summary>아이템 코드·수량으로 상세 문구를 만든다(강화 단계 없음 = 미강화 기준).</summary>
        public static Info Build(int itemCode, long quantity) => Build(itemCode, quantity, 0);

        /// <summary>
        /// 아이템 코드·수량·강화 단계로 상세 문구를 만든다(마스터 데이터에 없으면 폴백 문구).
        /// 강화 단계가 1 이상이면 <b>이름 뒤에 "+N"</b>을 붙이고 옵션 스탯에 그 단계의 배율을 적용해 표시한다.
        /// </summary>
        public static Info Build(int itemCode, long quantity, int enhanceLevel)
        {
            var db = MasterDataManager.Db;
            ItemMaster im = null;
            if (db != null)
            {
                db.Items.TryGetValue(itemCode, out im);
            }

            string displayName = im != null ? im.name : $"아이템 {itemCode}";
            if (enhanceLevel > 0)
            {
                displayName += $" +{enhanceLevel}";
            }
            var info = new Info
            {
                name = displayName,
                gradeValue = im != null ? im.grade : 1,
                grade = im != null && db.Grades.TryGetValue(im.grade, out var g) ? g.name : "노말",
                category = Category(im),
                requirement = string.Empty,
                description = Description(im, quantity, enhanceLevel),
            };
            if (im != null && im.itemType == 1)
            {
                string cls = im.classReq == 0
                    ? "공용"
                    : (db.Classes.TryGetValue(im.classReq, out var cm) ? cm.name : $"직업 {im.classReq}");
                info.requirement = im.levelReq > 0 ? $"요구 Lv.{im.levelReq} / {cls}" : cls;
            }
            return info;
        }

        /// <summary>아이템 종류(무기/보조무기/방어구/재료/재화). 장비는 장착 슬롯으로 무기·방어구를 구분한다.</summary>
        public static string Category(ItemMaster im)
        {
            if (im == null)
            {
                return string.Empty;
            }
            switch (im.itemType)
            {
                case 1: // 장비
                    if (im.equipSlot == 1) return "무기";
                    if (im.equipSlot == 2) return "보조무기";
                    return "방어구"; // 투구·갑옷·장갑·신발
                case 2: return "재료";
                case 3: return "재화";
                case 4: return "소모품";
                default: return "기타";
            }
        }

        /// <summary>아이템 설명문(종류별, 강화 단계 없음 = 미강화 기준).</summary>
        public static string Description(ItemMaster im, long quantity) => Description(im, quantity, 0);

        /// <summary>아이템 설명문(종류별). 장비는 옵션 효과(강화 단계 배율 반영), 재료/재화는 용도 설명.</summary>
        public static string Description(ItemMaster im, long quantity, int enhanceLevel)
        {
            if (im == null)
            {
                return string.Empty;
            }
            if (im.itemType == 1) // 장비
            {
                string effect = Stats(im, enhanceLevel);
                return string.IsNullOrEmpty(effect) || effect == "옵션 없음"
                    ? "착용 시 캐릭터에 장착되는 장비입니다."
                    : $"착용 시 다음 효과를 부여합니다.\n{effect}";
            }
            if (im.itemType == 2) // 재료
            {
                string q = quantity > 1 ? $" (수량 {quantity:N0})" : string.Empty;
                return $"강화·합성 등에 사용하는 재료입니다.{q}";
            }
            if (im.itemType == 3) // 재화
            {
                return "게임 내에서 사용하는 재화입니다.";
            }
            if (im.itemType == 4) // 소모품(획득량 버프)
            {
                // 배율·지속시간은 consumable_master(서버 전용 마스터)에 있어 클라이언트가 알지 못한다.
                // 실제 값은 사용 직후 응답(ActiveBuffDto)과 버프 아이콘 툴팁에서 보여준다.
                string q = quantity > 1 ? $" (수량 {quantity:N0})" : string.Empty;
                return $"사용하면 일정 시간 동안 획득량이 증가합니다.{q}";
            }
            return string.Empty;
        }

        /// <summary>장비 옵션 스탯 요약 문자열(장비가 아니면 빈 문자열). 강화 단계는 반영하지 않는다.</summary>
        public static string Stats(ItemMaster im) => Stats(im, 0);

        /// <summary>
        /// 장비 옵션 스탯 요약 문자열(장비가 아니면 빈 문자열).
        /// <paramref name="enhanceLevel"/>이 1 이상이면 그 단계의 배율(<c>enhance_master.stat_multiplier</c>)을
        /// 옵션 스탯 전체에 곱해 <b>실제 적용값</b>으로 표시하고, 원래 값을 괄호로 함께 보여 준다
        /// (인벤토리/아이템/큐브 기획서 §5.3 — 배율은 서버가 내려주지 않고 클라이언트가 마스터에서 읽는다).
        /// </summary>
        public static string Stats(ItemMaster im, int enhanceLevel)
        {
            if (im == null || im.itemType != 1)
            {
                return string.Empty;
            }
            var s = im.baseStats;
            var db = MasterDataManager.Db;
            float mult = db != null ? db.EnhanceMultiplier(enhanceLevel) : 1f;
            bool enhanced = enhanceLevel > 0 && mult != 1f;

            var parts = new List<string>();
            if (s.atk != 0) parts.Add(Line("ATK", s.atk, mult, enhanced));
            if (s.def != 0) parts.Add(Line("DEF", s.def, mult, enhanced));
            if (s.hp != 0) parts.Add(Line("HP", s.hp, mult, enhanced));
            if (s.critChance != 0) parts.Add(Percent("치명확률", s.critChance, mult, enhanced));
            if (s.critDamage != 0) parts.Add(Percent("치명피해", s.critDamage, mult, enhanced));
            // 이동속도·쿨타임은 강화 배율 대상이 아니다(장비 옵션 스탯 중 전투 수치만 배율을 받는다).
            if (s.moveSpeed != 0) parts.Add($"이동속도 +{s.moveSpeed:0.##}");
            if (s.cooldown != 0) parts.Add($"쿨타임 {s.cooldown:0.##}");
            return parts.Count > 0 ? string.Join("\n", parts) : "옵션 없음";
        }

        /// <summary>정수 스탯 한 줄. 강화 상태면 "ATK +126 (105)"처럼 적용값과 원래 값을 함께 보여 준다.</summary>
        private static string Line(string label, long value, float mult, bool enhanced)
        {
            if (!enhanced)
            {
                return $"{label} +{value}";
            }
            long applied = ApplyMultiplier(value, mult);
            return $"{label} +{applied} ({value})";
        }

        /// <summary>비율 스탯 한 줄(치명확률·치명피해). 강화 상태면 적용값과 원래 값을 함께 보여 준다.</summary>
        private static string Percent(string label, float value, float mult, bool enhanced)
        {
            if (!enhanced)
            {
                return $"{label} +{value * 100f:0.#}%";
            }
            return $"{label} +{value * mult * 100f:0.#}% ({value * 100f:0.#}%)";
        }

        /// <summary>정수 스탯에 강화 배율을 적용한다(반올림 — 인벤토리 능력치·전투 계산과 같은 규칙).</summary>
        public static long ApplyMultiplier(long value, float mult)
        {
            return (long)System.Math.Round(value * (double)mult);
        }
    }
}
