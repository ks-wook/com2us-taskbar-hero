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

        [Header("기본 능력치(class_master)")]
        [SerializeField] private CharacterStatRow[] statRows;

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
        /// 선택한 직업(class_master)의 기본 능력치를 스탯 행 UI에 채운다.
        /// 각 행의 게이지는 전체 직업 중 그 능력치의 최대값 대비 비율이라 직업 간 강점이 비교된다.
        /// 마스터 데이터에 직업이 없으면 행을 모두 '-'로 비운다.
        /// </summary>
        public void SetStats(int classCode)
        {
            if (statRows == null || statRows.Length == 0)
            {
                return;
            }

            MasterDataManager.EnsureLoaded();
            var db = MasterDataManager.Db;
            ClassMaster cls = null;
            if (db != null)
            {
                db.Classes.TryGetValue(classCode, out cls);
            }

            foreach (var row in statRows)
            {
                if (row == null)
                {
                    continue;
                }
                if (cls == null)
                {
                    row.Set("-", 0f);
                    continue;
                }

                float raw = ClassStatInfo.RawOf(cls.baseStats, row.Kind);
                float max = MaxAmongClasses(row.Kind);
                row.Set(ClassStatInfo.FormatOf(cls.baseStats, row.Kind), max > 0f ? raw / max : 0f);
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
