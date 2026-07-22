using UnityEngine;
using UnityEngine.EventSystems;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 지도 위 한 지역(region)의 클릭 영역. hover 시 노란 글로우를 켜고, 클릭 시 컨트롤러에
    /// 해당 지역의 스테이지 창을 열도록 요청한다. 구조는 프리팹에 직렬화된다.
    /// </summary>
    public class StageRegionHotspot : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        [SerializeField] private int _region;
        [SerializeField] private GameObject _glow;

        public int Region => _region;

        private StagePanelController _controller;

        private StagePanelController Controller =>
            _controller != null ? _controller : (_controller = GetComponentInParent<StagePanelController>());

        /// <summary>에디터 빌드 전용: 지역 번호와 글로우 오브젝트를 지정한다.</summary>
        public void EditorInit(int region, GameObject glow)
        {
            _region = region;
            _glow = glow;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_glow != null)
            {
                _glow.SetActive(true);
            }
            if (Controller != null)
            {
                Controller.OnRegionHover(_region, true);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_glow != null)
            {
                _glow.SetActive(false);
            }
            if (Controller != null)
            {
                Controller.OnRegionHover(_region, false);
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (Controller != null)
            {
                Controller.OpenRegion(_region);
            }
        }
    }
}
