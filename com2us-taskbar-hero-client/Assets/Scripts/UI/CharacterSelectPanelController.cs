using System;
using UnityEngine;
using UnityEngine.UI;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 캐릭터 선택 패널(우측)의 컨트롤러. 선택한 캐릭터 정보를 표시하고
    /// '선택'/'뒤로' 버튼 이벤트를 외부(CharacterSelectManager)로 전달한다.
    /// 로그인/회원가입 UI와 동일한 픽셀 패널·버튼 스프라이트를 사용한다.
    /// </summary>
    public class CharacterSelectPanelController : MonoBehaviour
    {
        [SerializeField] private Text titleText;
        [SerializeField] private Text descriptionText;
        [SerializeField] private Button selectButton;
        [SerializeField] private Button backButton;

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

        public void Show(bool visible)
        {
            gameObject.SetActive(visible);
        }
    }
}
