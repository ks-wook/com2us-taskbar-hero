using System;
using UnityEngine;
using UnityEngine.UI;
using TaskbarHero.Client.Managers;
using TaskbarHero.Common.MasterData;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 캐릭터 선택 패널(우측)의 컨트롤러. 선택한 캐릭터 정보(이름·설명·기본 능력치)를 표시하고
    /// '선택'/'뒤로' 버튼 이벤트를 외부(CharacterSelectManager)로 전달한다.
    /// 로그인/회원가입 UI와 동일한 픽셀 패널·버튼 스프라이트를 사용한다.
    /// </summary>
    public class CharacterSelectPanelController : MonoBehaviour
    {
        [SerializeField] private Text titleText;
        [SerializeField] private Text descriptionText;
        [SerializeField] private Button selectButton;
        [SerializeField] private Button backButton;

        [Header("기본 능력치(class_master) — 5각형 레이더")]
        [Tooltip("체력·공격력·공격속도·이동속도·방어력 5축 차트(ClassStatInfo.DisplayKinds 순서).")]
        [SerializeField] private StatRadarChart statRadar;
        [Tooltip("각 꼭짓점의 수치 텍스트. ClassStatInfo.DisplayKinds 와 같은 순서로 배선한다.")]
        [SerializeField] private Text[] statValueTexts;

        public event Action Selected;
        public event Action Backed;

        private bool _busy;

        private void Awake()
        {
            if (selectButton != null)
            {
                selectButton.onClick.AddListener(() => OnButtonClicked(selectButton, () => Selected?.Invoke()));
            }

            if (backButton != null)
            {
                backButton.onClick.AddListener(() => OnButtonClicked(backButton, () => Backed?.Invoke()));
            }
        }

        /// <summary>
        /// 버튼 클릭 → 펀치 효과 재생 → 효과가 끝난 뒤에 실제 동작을 실행한다.
        /// (동작이 패널을 비활성화하더라도 효과가 먼저 끝나므로 코루틴 중단 오류가 없다.)
        /// </summary>
        private void OnButtonClicked(Button button, Action action)
        {
            if (_busy)
            {
                return;
            }

            _busy = true;
            var punch = button.GetComponent<ButtonPunchScale>();
            if (punch != null)
            {
                punch.Play(() => { _busy = false; action?.Invoke(); });
            }
            else
            {
                _busy = false;
                action?.Invoke();
            }
        }

        public void SetTitle(string text)
        {
            if (titleText != null)
            {
                titleText.text = text;
            }
        }

        /// <summary>직업 설명(class_master.description)을 표시한다. 빈 값이면 문구 영역을 비운다.</summary>
        public void SetDescription(string text)
        {
            if (descriptionText != null)
            {
                descriptionText.text = string.IsNullOrEmpty(text) ? string.Empty : text;
            }
        }

        /// <summary>
        /// 선택한 직업(class_master)의 기본 능력치를 5각형 레이더와 꼭짓점 수치 텍스트에 채운다.
        /// 각 축의 길이는 전체 직업 중 그 능력치의 최대값 대비 비율이라 직업 간 강점이 한눈에 비교된다.
        /// 마스터 데이터에 직업이 없으면 차트를 0으로 접고 수치를 '-'로 비운다.
        /// </summary>
        public void SetStats(int classCode)
        {
            MasterDataManager.EnsureLoaded();
            var db = MasterDataManager.Db;
            ClassMaster cls = null;
            if (db != null)
            {
                db.Classes.TryGetValue(classCode, out cls);
            }

            var kinds = ClassStatInfo.DisplayKinds;
            var ratios = new float[kinds.Length];
            for (int i = 0; i < kinds.Length; i++)
            {
                if (cls != null)
                {
                    float raw = ClassStatInfo.RawOf(cls.baseStats, kinds[i]);
                    float max = MaxAmongClasses(kinds[i]);
                    ratios[i] = max > 0f ? raw / max : 0f;
                }

                if (statValueTexts != null && i < statValueTexts.Length && statValueTexts[i] != null)
                {
                    statValueTexts[i].text = cls != null ? ClassStatInfo.FormatOf(cls.baseStats, kinds[i]) : "-";
                }
            }

            if (statRadar != null)
            {
                statRadar.SetValues(ratios);
            }
        }

        /// <summary>해당 능력치의 전체 직업 최대값(게이지 정규화 기준). 마스터 데이터가 없으면 0.</summary>
        private static float MaxAmongClasses(ClassStatKind kind)
        {
            var db = MasterDataManager.Db;
            if (db == null)
            {
                return 0f;
            }

            float max = 0f;
            foreach (var cls in db.Classes.Values)
            {
                if (cls == null)
                {
                    continue;
                }
                float value = ClassStatInfo.RawOf(cls.baseStats, kind);
                if (value > max)
                {
                    max = value;
                }
            }
            return max;
        }

        public void Show(bool visible)
        {
            gameObject.SetActive(visible);
        }
    }
}
