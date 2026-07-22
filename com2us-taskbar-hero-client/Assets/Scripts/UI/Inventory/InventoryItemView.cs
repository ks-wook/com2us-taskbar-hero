using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 인벤토리 아이템 아이콘 뷰(드래그 가능). 표시 데이터·아이콘은 프리팹에 직렬화되고,
    /// 계층은 에디터 빌드 시 생성된다(정적 프리팹). hover 시 상세 툴팁을 띄우고,
    /// 드래그로 다른 격자 칸으로 이동한다(로컬 처리, 서버 미연동).
    /// </summary>
    public class InventoryItemView : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler,
        IPointerEnterHandler, IPointerExitHandler
    {
        /// <summary>툴팁·아이콘에 표시할 아이템 정보(더미).</summary>
        [System.Serializable]
        public struct Display
        {
            public string name;
            public string grade;
            public string slotName;
            public string requirement;
            public string stats;
            public Color iconColor;
        }

        [SerializeField] private Display _data;
        [SerializeField] private Image _icon;

        public Display Data => _data;

        private RectTransform _rt;
        private InventoryItemSlot _originSlot;
        private Canvas _canvas;
        private bool _dragging;
        private InventoryPanelController _controller;

        private const float Padding = 16f;

        private InventoryPanelController Controller =>
            _controller != null ? _controller : (_controller = GetComponentInParent<InventoryPanelController>());

        private void Awake()
        {
            _rt = (RectTransform)transform;
            if (_icon == null)
            {
                _icon = GetComponent<Image>();
            }
        }

        /// <summary>에디터 빌드 전용: 더미 표시 데이터·아이콘·라벨을 구성한다.</summary>
        public void EditorSetup(Display display, Font font)
        {
            _data = display;
            _rt = (RectTransform)transform;

            _icon = gameObject.GetComponent<Image>();
            if (_icon == null)
            {
                _icon = gameObject.AddComponent<Image>();
            }
            _icon.color = display.iconColor;
            _icon.raycastTarget = true;

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(transform, false);
            var label = labelGo.GetComponent<Text>();
            label.font = font;
            label.text = string.IsNullOrEmpty(display.name) ? "?" : display.name.Substring(0, 1);
            label.fontSize = 40;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.black;
            label.raycastTarget = false;
            var lrt = (RectTransform)labelGo.transform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = Vector2.zero;
        }

        /// <summary>지정 슬롯 아래로 배치하고 패딩만큼 여백을 준다.</summary>
        public void AttachTo(InventoryItemSlot slot)
        {
            _originSlot = slot;
            _rt = (RectTransform)transform;
            transform.SetParent(slot.transform, false);
            _rt.anchorMin = Vector2.zero;
            _rt.anchorMax = Vector2.one;
            _rt.pivot = new Vector2(0.5f, 0.5f);
            _rt.offsetMin = new Vector2(Padding, Padding);
            _rt.offsetMax = new Vector2(-Padding, -Padding);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            _canvas = GetComponentInParent<Canvas>();
            if (_canvas == null)
            {
                return;
            }

            _dragging = true;
            _originSlot = GetComponentInParent<InventoryItemSlot>();
            Controller.RequestHideTooltip();

            if (_originSlot != null)
            {
                _originSlot.ClearItem();
            }
            transform.SetParent(_canvas.transform, true);
            transform.SetAsLastSibling();

            _rt.anchorMin = _rt.anchorMax = new Vector2(0.5f, 0.5f);
            _rt.pivot = new Vector2(0.5f, 0.5f);
            _rt.sizeDelta = new Vector2(118f, 118f);
            _icon.raycastTarget = false; // 아래 칸이 레이캐스트에 잡히도록
            _rt.position = eventData.position;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_dragging)
            {
                _rt.position = eventData.position;
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!_dragging)
            {
                return;
            }
            _dragging = false;
            _icon.raycastTarget = true;

            var target = FindSlotUnderPointer(eventData);
            Controller.MoveItem(this, _originSlot, target);
        }

        /// <summary>포인터 아래의 격자 슬롯(장비 슬롯 제외)을 찾는다.</summary>
        private InventoryItemSlot FindSlotUnderPointer(PointerEventData eventData)
        {
            var results = new System.Collections.Generic.List<RaycastResult>();
            EventSystem.current.RaycastAll(eventData, results);
            foreach (var r in results)
            {
                var slot = r.gameObject.GetComponentInParent<InventoryItemSlot>();
                if (slot != null && !slot.IsEquipSlot)
                {
                    return slot;
                }
            }
            return null;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!_dragging)
            {
                Controller.ShowTooltip(this, eventData.position);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (!_dragging)
            {
                Controller.RequestHideTooltip();
            }
        }
    }
}
