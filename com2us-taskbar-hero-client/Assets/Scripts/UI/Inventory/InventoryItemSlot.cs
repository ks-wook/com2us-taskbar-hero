using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 인벤토리 격자 칸(또는 장비 부위 슬롯). 구조 정보(인덱스·타입·프레임·부위라벨)는
    /// 프리팹에 직렬화되고, 아이템 점유는 런타임 상태다. hover 시 하이라이트 프레임을 켜며
    /// 드롭 대상으로 동작하고, 장비 슬롯은 캐릭터별 장착 아이템(데모)을 표시·툴팁한다.
    /// </summary>
    public class InventoryItemSlot : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private int _index;
        [SerializeField] private bool _isEquipSlot;
        [SerializeField] private GameObject _hoverFrame;
        [SerializeField] private Text _partLabel; // 장비 슬롯 부위 이름(장착 시 숨김)

        public int Index => _index;
        public bool IsEquipSlot => _isEquipSlot;
        public InventoryItemView Item { get; private set; } // 런타임 점유 상태

        private InventoryPanelController _controller;
        private GameObject _equippedIcon;
        private Text _equippedLabel;
        private InventoryItemView.Display? _equipped;

        private InventoryPanelController Controller =>
            _controller != null ? _controller : (_controller = GetComponentInParent<InventoryPanelController>());

        private void Awake()
        {
            // 프리팹에 배치된 아이템(더미)을 런타임 점유 상태로 인식한다.
            if (Item == null)
            {
                Item = GetComponentInChildren<InventoryItemView>(true);
            }
        }

        /// <summary>에디터 빌드 전용: 구조 정보를 지정한다.</summary>
        public void EditorInit(int index, bool isEquipSlot, GameObject hoverFrame, Text partLabel)
        {
            _index = index;
            _isEquipSlot = isEquipSlot;
            _hoverFrame = hoverFrame;
            _partLabel = partLabel;
        }

        /// <summary>이 칸에 아이템을 배치한다(null이면 비운다).</summary>
        public void SetItem(InventoryItemView item)
        {
            Item = item;
            if (item != null)
            {
                item.AttachTo(this);
            }
        }

        /// <summary>드래그 시작 시 점유 참조를 비운다(복귀/스왑은 컨트롤러가 결정).</summary>
        public void ClearItem()
        {
            Item = null;
        }

        /// <summary>하이라이트 프레임 표시 토글.</summary>
        public void SetHighlight(bool on)
        {
            if (_hoverFrame != null)
            {
                _hoverFrame.SetActive(on);
            }
        }

        /// <summary>장비 슬롯에 장착 아이템을 표시하거나(값 있음), 비운다(null).
        /// Display.icon이 있으면 실제 아이콘 스프라이트, 없으면 색+첫글자로 폴백한다.</summary>
        public void SetEquipped(InventoryItemView.Display? data, Font font)
        {
            _equipped = data;

            if (!data.HasValue)
            {
                if (_equippedIcon != null)
                {
                    _equippedIcon.SetActive(false);
                }
                if (_partLabel != null)
                {
                    _partLabel.gameObject.SetActive(true);
                }
                return;
            }

            EnsureEquippedIcon(font);
            var img = _equippedIcon.GetComponent<Image>();
            bool hasSprite = data.Value.icon != null;
            img.sprite = hasSprite ? data.Value.icon : null;
            img.color = hasSprite ? Color.white : data.Value.iconColor;
            img.preserveAspect = hasSprite;
            if (_equippedLabel != null)
            {
                _equippedLabel.text = hasSprite || string.IsNullOrEmpty(data.Value.name)
                    ? string.Empty
                    : data.Value.name.Substring(0, 1);
            }
            _equippedIcon.SetActive(true);
            if (_partLabel != null)
            {
                _partLabel.gameObject.SetActive(false);
            }
        }

        /// <summary>장착 아이콘 오브젝트를 최초 1회 생성한다(런타임).</summary>
        private void EnsureEquippedIcon(Font font)
        {
            if (_equippedIcon != null)
            {
                return;
            }

            var go = new GameObject("EquippedIcon", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(14f, 14f);
            rt.offsetMax = new Vector2(-14f, -14f);
            go.GetComponent<Image>().raycastTarget = false; // 슬롯 배경이 hover를 받도록
            _equippedIcon = go;

            var lgo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            lgo.transform.SetParent(go.transform, false);
            var lrt = (RectTransform)lgo.transform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = Vector2.zero;
            _equippedLabel = lgo.GetComponent<Text>();
            _equippedLabel.font = font;
            _equippedLabel.fontSize = 26;
            _equippedLabel.alignment = TextAnchor.MiddleCenter;
            _equippedLabel.color = Color.black;
            _equippedLabel.raycastTarget = false;
            _equippedLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            _equippedLabel.verticalOverflow = VerticalWrapMode.Overflow;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            Controller.OnSlotHover(this, true);
            if (_equipped.HasValue)
            {
                Controller.ShowTooltip(_equipped.Value, eventData.position);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            Controller.OnSlotHover(this, false);
            if (_equipped.HasValue)
            {
                Controller.RequestHideTooltip();
            }
        }
    }
}
