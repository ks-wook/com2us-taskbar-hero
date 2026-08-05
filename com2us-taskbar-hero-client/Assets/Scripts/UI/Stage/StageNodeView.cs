using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 스테이지 선택 맵의 노드 한 칸(스테이지 1개). 해금/잠금 스프라이트·자물쇠·선택 하이라이트를
    /// 표시하고, 클릭 시 <b>살짝 커졌다 원래 크기로 돌아오는 피드백</b>(<see cref="ButtonPunchScale"/>)을 재생한 뒤
    /// 컨트롤러에 선택을 알린다. 구조는 프리팹에 직렬화된다.
    /// </summary>
    public class StageNodeView : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] private int _stage;
        [SerializeField] private Image _nodeImage;
        [SerializeField] private GameObject _lockIcon;
        [SerializeField] private GameObject _highlight;

        public int Stage => _stage;

        private StagePanelController _controller;
        private ButtonPunchScale _punch;

        private StagePanelController Controller =>
            _controller != null ? _controller : (_controller = GetComponentInParent<StagePanelController>());

        /// <summary>
        /// 클릭 피드백(살짝 커졌다 원래 크기로). 노드 계층을 굽는 <see cref="StagePanelController"/>가 함께 붙이지만,
        /// 그 이전에 구워진 프리팹에는 없으므로 없으면 실행 시 붙인다(프리팹을 다시 굽지 않아도 연출이 나온다).
        /// </summary>
        private ButtonPunchScale Punch
        {
            get
            {
                if (_punch == null)
                {
                    _punch = GetComponent<ButtonPunchScale>();
                }
                if (_punch == null)
                {
                    _punch = gameObject.AddComponent<ButtonPunchScale>();
                }
                return _punch;
            }
        }

        /// <summary>에디터 빌드 전용: 스테이지 번호와 자식 참조를 지정한다.</summary>
        public void EditorInit(int stage, Image nodeImage, GameObject lockIcon, GameObject highlight)
        {
            _stage = stage;
            _nodeImage = nodeImage;
            _lockIcon = lockIcon;
            _highlight = highlight;
        }

        /// <summary>노드 스프라이트·자물쇠를 반영한다.</summary>
        public void SetVisual(Sprite nodeSprite, bool locked)
        {
            if (_nodeImage != null)
            {
                _nodeImage.sprite = nodeSprite;
            }
            if (_lockIcon != null)
            {
                _lockIcon.SetActive(locked);
            }
        }

        /// <summary>선택 하이라이트 토글.</summary>
        public void SetHighlight(bool on)
        {
            if (_highlight != null)
            {
                _highlight.SetActive(on);
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            // 잠긴 스테이지도 눌린 것은 보여 준다(선택 가능 여부 판단은 컨트롤러가 한다) —
            // 아무 반응이 없으면 클릭이 먹지 않은 것으로 오해한다.
            Punch.Play();
            if (Controller != null)
            {
                Controller.OnNodeClicked(this);
            }
        }
    }
}
