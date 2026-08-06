using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TaskbarHero.Client.Managers;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 인벤토리 격자 칸(또는 장비 부위 슬롯). 구조 정보(인덱스·타입·프레임·부위라벨)는
    /// 프리팹에 직렬화되고, 아이템 점유는 런타임 상태다. hover 시 하이라이트 프레임을 켜며
    /// 드롭 대상으로 동작하고, 장비 슬롯은 캐릭터별 장착 아이템을 표시·툴팁한다.
    /// <para><b>장비 부위 칸은 장착 중인 아이템을 끌어낼 수 있다</b> — 가방 격자에 떨어뜨리면 해제
    /// 요청이 나간다(<see cref="InventoryPanelController.TryUnequipByDrag"/>). 가방 아이템을 부위 칸에
    /// 떨어뜨려 장착하는 반대 방향은 <see cref="InventoryItemView"/>가 담당한다.</para>
    /// </summary>
    public class InventoryItemSlot : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler
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
        private ItemSlotView _equippedSlotView; // 장착 아이템을 그리는 공용 슬롯(아이콘·등급·강화 배지)
        private Text _equippedLabel;
        private InventoryItemView.Display? _equipped;

        // 장착 아이템 끌어내기(해제) — 별도 고스트를 만들지 않고 장착 아이콘 자체를 커서에 붙였다가 되돌린다.
        private bool _dragging;
        private Canvas _canvas;
        private Transform _dragOriginParent;
        private CanvasGroup _dragGroup;

        /// <summary>끌고 다니는 동안의 아이콘 크기(가방 아이템 드래그와 같은 크기로 맞춘다).</summary>
        private static readonly Vector2 DragIconSize = new Vector2(118f, 118f);

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
        /// 그림은 <b>공용 아이템 슬롯 프리팹</b>(<see cref="ItemSlotView"/>)이 그리므로 가방 칸·큐브·거래소와
        /// 같은 외형이 되고, <b>강화 단계도 같은 자리(좌측 하단 흰 "+N")에</b> 나온다
        /// (이전에는 장착 칸에만 강화 표시가 없었다). 아이콘이 없는 아이템은 첫 글자를 폴백으로 얹는다.</summary>
        public void SetEquipped(InventoryItemView.Display? data, Font font, GameObject slotPrefab)
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

            EnsureEquippedIcon(font, slotPrefab);
            if (_equippedIcon == null)
            {
                return; // 프리팹 미배선(경고는 EnsureEquippedIcon이 남긴다)
            }
            bool hasSprite = data.Value.icon != null;
            if (_equippedSlotView != null)
            {
                // 부위 칸이 이미 자기 프레임을 그리므로 슬롯 프레임은 감춘다. hover 상세는 이 칸이 직접
                // 인벤토리 툴팁을 띄우므로(OnPointerEnter) 슬롯의 공용 팝업은 끈다.
                _equippedSlotView.Setup(data.Value.itemCode, 1L, string.Empty, false);
                _equippedSlotView.SetFrameVisible(false);
                _equippedSlotView.SetEnhanceLevel(data.Value.enhanceLevel);
            }
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

        /// <summary>장착 표시 계층을 최초 1회 만든다(런타임) — 공용 슬롯 프리팹 인스턴스 + 첫 글자 폴백 라벨.
        /// 프리팹이 배선되지 않았으면 경고만 남기고 아무것도 만들지 않는다(코드로 다른 외형을 만들면
        /// 화면마다 아이템 칸이 다시 갈라진다).</summary>
        private void EnsureEquippedIcon(Font font, GameObject slotPrefab)
        {
            if (_equippedIcon != null)
            {
                return;
            }
            if (slotPrefab == null)
            {
                Debug.LogWarning("[Inventory] 공용 아이템 슬롯 프리팹이 배선되지 않았습니다. " +
                                 "메뉴 'TaskbarHero/UI/아이템 슬롯·상세 팝업 배선'을 실행하세요.");
                return;
            }

            var go = Instantiate(slotPrefab, transform);
            go.name = "EquippedIcon";
            _equippedSlotView = go.GetComponent<ItemSlotView>();
            _equippedIcon = go;
            _dragGroup = go.GetComponent<CanvasGroup>();
            if (_dragGroup == null)
            {
                _dragGroup = go.AddComponent<CanvasGroup>(); // 끌고 다니는 동안 아래 칸이 레이캐스트에 잡히도록
            }
            RestoreEquippedIconLayout();

            var lgo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            lgo.transform.SetParent(go.transform, false);
            lgo.transform.SetAsLastSibling(); // 슬롯 그림 위
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

        /// <summary>장착 아이콘을 이 칸 안에 꽉 차게(여백 14) 되돌린다 — 생성 직후와 드래그 취소 시 공용.</summary>
        private void RestoreEquippedIconLayout()
        {
            if (_equippedIcon == null)
            {
                return;
            }
            var rt = (RectTransform)_equippedIcon.transform;
            rt.SetParent(transform, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(14f, 14f);
            rt.offsetMax = new Vector2(-14f, -14f);
            rt.localScale = Vector3.one;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            Controller.OnSlotHover(this, true);
            if (_equipped.HasValue && !_dragging)
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

        // ── 장착 아이템 끌어내기(해제) ──

        /// <summary>
        /// 장착 중인 아이템을 끌기 시작한다(장비 부위 칸 + 장착 상태일 때만).
        /// 별도의 고스트를 만들지 않고 <b>장착 아이콘 자체</b>를 캔버스로 옮겨 커서에 붙인다 —
        /// 놓는 순간 서버 응답으로 UI가 다시 그려지므로, 취소되면 <see cref="RestoreEquippedIconLayout"/>로 되돌린다.
        /// </summary>
        public void OnBeginDrag(PointerEventData eventData)
        {
            if (!_isEquipSlot || !_equipped.HasValue || _equippedIcon == null)
            {
                return; // 가방 격자 칸의 아이템 드래그는 InventoryItemView가 처리한다
            }
            _canvas = GetComponentInParent<Canvas>();
            if (_canvas == null)
            {
                return;
            }

            _dragging = true;
            SoundManager.Sfx(SoundId.UiSlotSelect); // 아이템 칸을 집는 순간(사운드 정의서 §4.1)
            Controller.RequestHideTooltip();

            var rt = (RectTransform)_equippedIcon.transform;
            _dragOriginParent = rt.parent;
            rt.SetParent(_canvas.transform, true);
            rt.SetAsLastSibling();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = DragIconSize;
            rt.position = eventData.position;
            if (_dragGroup != null)
            {
                _dragGroup.blocksRaycasts = false; // 아래 가방 칸이 레이캐스트에 잡히도록
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_dragging && _equippedIcon != null)
            {
                ((RectTransform)_equippedIcon.transform).position = eventData.position;
            }
        }

        /// <summary>가방 격자 칸에 떨어뜨렸으면 해제 요청, 그 밖에는 아이콘을 제자리로 되돌린다.</summary>
        public void OnEndDrag(PointerEventData eventData)
        {
            if (!_dragging)
            {
                return;
            }
            _dragging = false;
            if (_dragGroup != null)
            {
                _dragGroup.blocksRaycasts = true;
            }
            RestoreEquippedIconLayout();
            _dragOriginParent = null;

            var target = FindSlotUnderPointer(eventData);
            if (target != null && !target.IsEquipSlot)
            {
                Controller.TryUnequipByDrag(this);
            }
        }

        /// <summary>포인터 아래의 슬롯을 찾는다(가방 격자 칸·장비 부위 칸 모두). 없으면 null.</summary>
        private static InventoryItemSlot FindSlotUnderPointer(PointerEventData eventData)
        {
            var es = EventSystem.current;
            if (es == null)
            {
                return null;
            }
            var results = new System.Collections.Generic.List<RaycastResult>();
            es.RaycastAll(eventData, results);
            foreach (var r in results)
            {
                var slot = r.gameObject.GetComponentInParent<InventoryItemSlot>();
                if (slot != null)
                {
                    return slot;
                }
            }
            return null;
        }
    }
}
