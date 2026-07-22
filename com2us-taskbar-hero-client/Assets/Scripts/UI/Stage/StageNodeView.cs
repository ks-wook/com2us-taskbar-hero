using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 스테이지 선택 맵의 노드 한 칸(스테이지 1개). 해금/잠금 스프라이트·자물쇠·선택 하이라이트를
    /// 표시하고, 클릭 시 컨트롤러에 선택을 알린다. 구조는 프리팹에 직렬화된다.
    /// </summary>
    public class StageNodeView : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] private int _stage;
        [SerializeField] private Image _nodeImage;
        [SerializeField] private GameObject _lockIcon;
        [SerializeField] private GameObject _highlight;

        public int Stage => _stage;

        private StagePanelController _controller;

        private StagePanelController Controller =>
            _controller != null ? _controller : (_controller = GetComponentInParent<StagePanelController>());

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
            if (Controller != null)
            {
                Controller.OnNodeClicked(this);
            }
        }
    }
}
