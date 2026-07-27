using UnityEngine;
using UnityEngine.EventSystems;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 패널(창)을 마우스 드래그로 옮길 수 있게 하는 공용 컴포넌트.
    /// 패널 배경(레이캐스트 대상 Image가 있는 창 루트)에 붙이면, 그 위 어디서든 끌어서 창을 이동할 수 있다
    /// — 단 스크롤 뷰처럼 자체적으로 드래그를 소비하는 자식 컨트롤 위에서는 그 컨트롤이 우선한다
    /// (uGUI 드래그 이벤트는 가장 가까운 IDragHandler가 가져가므로 별도 처리 불필요).
    /// 이동 대상은 <see cref="target"/>(비우면 자신의 RectTransform)이고, 화면 밖으로 완전히
    /// 사라지지 않도록 캔버스 안에 최소 노출 폭을 남기며 클램프한다. UIManager가 패널 인스턴스를
    /// 캐싱(SetActive 토글)하므로 옮긴 위치는 닫았다 다시 열어도 유지된다.
    /// </summary>
    public class DraggableUIPanel : MonoBehaviour, IBeginDragHandler, IDragHandler
    {
        [Tooltip("드래그로 옮길 RectTransform. 비우면 이 컴포넌트가 붙은 RectTransform을 옮긴다.")]
        [SerializeField] private RectTransform target;

        [Tooltip("드래그 후에도 화면(캔버스) 안에 남겨 둘 패널의 최소 노출 폭(px, 캔버스 단위).")]
        [SerializeField] private float keepVisible = 120f;

        private Canvas _canvas;

        /// <summary>드래그 시작 — 대상/캔버스 참조를 확정한다.</summary>
        public void OnBeginDrag(PointerEventData eventData)
        {
            Resolve();
        }

        /// <summary>드래그 중 — 포인터 이동량(스크린 px)을 캔버스 단위로 환산해 창을 옮기고 화면 안으로 클램프한다.</summary>
        public void OnDrag(PointerEventData eventData)
        {
            Resolve();
            if (target == null)
            {
                return;
            }
            float scale = _canvas != null ? Mathf.Max(0.0001f, _canvas.scaleFactor) : 1f;
            target.anchoredPosition += eventData.delta / scale;
            ClampToCanvas();
        }

        /// <summary>이동 대상(미지정 시 자기 RectTransform)과 소속 캔버스를 캐시한다.</summary>
        private void Resolve()
        {
            if (target == null)
            {
                target = (RectTransform)transform;
            }
            if (_canvas == null)
            {
                _canvas = GetComponentInParent<Canvas>();
            }
        }

        /// <summary>패널이 캔버스 밖으로 완전히 나가지 않도록 최소 keepVisible 만큼은 화면 안에 남긴다.</summary>
        private void ClampToCanvas()
        {
            if (_canvas == null || target == null)
            {
                return;
            }
            var canvasRt = (RectTransform)_canvas.transform;
            Rect cr = canvasRt.rect;

            // 패널 네 모서리를 캔버스 로컬 좌표로 환산(캔버스 하위는 스케일 1이라 anchoredPosition 보정량과 동일 단위).
            var corners = new Vector3[4];
            target.GetWorldCorners(corners);
            Vector2 min = canvasRt.InverseTransformPoint(corners[0]);
            Vector2 max = canvasRt.InverseTransformPoint(corners[2]);

            float keep = Mathf.Max(20f, keepVisible);
            Vector2 fix = Vector2.zero;
            if (max.x < cr.xMin + keep) fix.x = cr.xMin + keep - max.x;
            else if (min.x > cr.xMax - keep) fix.x = cr.xMax - keep - min.x;
            if (max.y < cr.yMin + keep) fix.y = cr.yMin + keep - max.y;
            else if (min.y > cr.yMax - keep) fix.y = cr.yMax - keep - min.y;
            if (fix != Vector2.zero)
            {
                target.anchoredPosition += fix;
            }
        }
    }
}
