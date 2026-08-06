using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TaskbarHero.Client.Managers;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 인벤토리 아이템 아이콘 뷰(드래그 가능). 표시 데이터·아이콘은 프리팹에 직렬화되고,
    /// 계층은 에디터 빌드 시 생성된다(정적 프리팹). hover 시 상세 툴팁을 띄운다.
    /// <para>드래그하면 놓는 곳에 따라 동작이 갈린다 — <b>가방 격자 칸</b>에 놓으면 배치 이동,
    /// <b>장비 부위 칸</b>에 놓으면 장착 요청이다(<see cref="InventoryPanelController.TryEquipByDrag"/>).
    /// 드래그하는 동안에는 그 장비가 들어갈 부위 칸이 강조된다.</para>
    /// </summary>
    public class InventoryItemView : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler,
        IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        /// <summary>툴팁·아이콘에 표시할 아이템 정보.</summary>
        [System.Serializable]
        public struct Display
        {
            public string name;
            public string grade;      // 등급명(노말·고급·희귀·영웅·전설)
            public int gradeValue;    // 등급 값(1~5) — 이름 색·배경 색 결정
            public string slotName;   // 종류(무기·보조무기·방어구·재료·재화)
            public string requirement;
            public string stats;
            public string description; // 아이템 설명
            public Color iconColor;   // 아이콘 스프라이트가 없을 때의 폴백 색
            public Sprite icon;       // 실제 아이템 아이콘(없으면 iconColor로 폴백)
            public int itemCode;      // 마스터 아이템 코드(공용 슬롯이 아이콘·등급을 직접 조회하는 키)
            public long quantity;     // 보유 수량(2 이상이면 슬롯 우하단에 "xN")
            public long itemId;       // 서버 아이템 id(장착/해제 요청용)
            public int equippedSlot;  // 현재 장착 슬롯(1~6). 0 = 가방(미장착)
            public int equipSlot;     // 이 장비가 들어갈 부위(item_master.equipSlot 1~6). 비장비는 0 — 드래그 장착의 부위 판정
            public bool isEquipment;  // 장비(item_type=1) 여부. 툴팁의 장착/해제 버튼 노출 조건(재료·재화는 버튼 자체가 없다)
            public bool equippable;   // 착용 가능 여부(장비 + 현재 캐릭터 클래스·레벨 허용). 장착 버튼 활성 조건
            public bool equipLocked;  // 착용 불가 장비(클래스 불일치 또는 레벨 미달) → 슬롯에 X 표시 + 흐림
            public bool usable;       // 소모품(item_type=4) 여부. true면 툴팁 버튼이 '장착'이 아니라 '사용'이 된다
            public int enhanceLevel;  // 장비 강화 단계(0 = 미강화). 1 이상이면 슬롯 좌측 하단에 흰 "+N" 배지
        }

        [SerializeField] private Display _data;
        [SerializeField] private Image _icon;

        private ItemSlotView _slotView; // 공용 슬롯 프리팹 인스턴스(아이콘·등급 배경·수량·강화 배지를 그린다)

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

        /// <summary>표시 데이터로 슬롯을 구성한다. <b>아이콘·등급 배경·수량·강화 배지는 공용 슬롯 프리팹
        /// (<see cref="ItemSlotView"/>)이 그린다</b> — 모든 UI가 같은 아이템 칸 외형을 쓰도록 통일한 지점이다.
        /// 이 오브젝트 자체는 <b>드래그·hover를 받는 투명 판</b>이고, 인벤토리 고유 표시(착용 불가 X, 아이콘
        /// 없는 아이템의 첫 글자 폴백)만 슬롯 위에 얹는다.
        /// <paramref name="slotPrefab"/>이 없으면(프리팹 미배선) 경고를 남기고 아이콘 없는 빈 칸이 된다.</summary>
        public void Setup(Display display, Font font, GameObject slotPrefab)
        {
            _data = display;
            _rt = (RectTransform)transform;

            // 루트 이미지 = 투명한 입력 판(드래그/hover 레이캐스트 대상). 그림은 공용 슬롯이 담당한다.
            _icon = gameObject.GetComponent<Image>();
            if (_icon == null)
            {
                _icon = gameObject.AddComponent<Image>();
            }
            _icon.sprite = null;
            _icon.color = new Color(0f, 0f, 0f, 0f);
            _icon.raycastTarget = true;

            bool hasSprite = display.icon != null;
            bool locked = display.equipLocked; // 착용 불가(클래스 불일치·레벨 미달): 흐리게 + X 표시

            EnsureSlotView(slotPrefab);
            if (_slotView != null)
            {
                // 격자 칸이 이미 자기 프레임을 그리므로 슬롯 프레임은 감춘다(이중 테두리 방지).
                // hover 상세는 인벤토리 전용 툴팁(장착·사용 버튼 포함)이 담당하므로 showDetail = false.
                _slotView.Setup(display.itemCode, display.quantity,
                    display.quantity > 1 ? $"x{display.quantity}" : string.Empty, false);
                _slotView.SetFrameVisible(false);
                _slotView.SetEnhanceLevel(display.enhanceLevel);
                _slotView.SetIconDimmed(locked);
            }

            // 라벨: 실제 아이콘이 없을 때만 첫 글자 폴백 표시.
            var labelTf = transform.Find("Label");
            var labelGo = labelTf != null ? labelTf.gameObject
                : new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(transform, false);
            labelGo.transform.SetAsLastSibling();
            var label = labelGo.GetComponent<Text>();
            label.font = font;
            label.text = hasSprite || string.IsNullOrEmpty(display.name) ? string.Empty : display.name.Substring(0, 1);
            label.fontSize = 40;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.black;
            label.raycastTarget = false;
            var lrt = (RectTransform)labelGo.transform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = Vector2.zero;

            // 착용 불가 장비(클래스 불일치·레벨 미달): 착용 불가 표시로 붉은 "X"를 덮어씌운다(잠금 아닐 때는 숨김).
            var lockTf = transform.Find("ClassLock");
            var lockGo = lockTf != null ? lockTf.gameObject
                : new GameObject("ClassLock", typeof(RectTransform), typeof(Text));
            lockGo.transform.SetParent(transform, false);
            lockGo.transform.SetAsLastSibling();
            var lockTxt = lockGo.GetComponent<Text>();
            lockTxt.font = font;
            lockTxt.text = "X";
            lockTxt.fontSize = 72;
            lockTxt.fontStyle = FontStyle.Bold;
            lockTxt.alignment = TextAnchor.MiddleCenter;
            lockTxt.color = new Color(0.92f, 0.16f, 0.16f, 0.92f);
            lockTxt.raycastTarget = false;
            var krt = (RectTransform)lockGo.transform;
            krt.anchorMin = Vector2.zero;
            krt.anchorMax = Vector2.one;
            krt.offsetMin = Vector2.zero;
            krt.offsetMax = Vector2.zero;
            lockGo.SetActive(locked);
        }

        /// <summary>
        /// 공용 아이템 슬롯 프리팹 인스턴스를 이 오브젝트의 <b>첫 번째 자식</b>으로 확보한다(최초 1회 생성).
        /// 첫 자식으로 두어 인벤토리 고유 표시(첫 글자 라벨·착용 불가 X)가 항상 슬롯 그림 위에 오게 한다.
        /// 프리팹이 배선되지 않았으면 경고만 남긴다 — 아이콘 표현을 코드로 따로 만들면 화면마다 외형이
        /// 다시 갈라지므로, 폴백을 두지 않고 <c>TaskbarHero/UI/아이템 슬롯·상세 팝업 배선</c> 실행을 유도한다.
        /// </summary>
        private void EnsureSlotView(GameObject slotPrefab)
        {
            if (_slotView != null)
            {
                return;
            }
            _slotView = GetComponentInChildren<ItemSlotView>(true); // 프리팹에 이미 구워져 있으면 재사용
            if (_slotView != null)
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
            go.name = "ItemSlot";
            go.transform.SetAsFirstSibling();
            var srt = (RectTransform)go.transform;
            srt.anchorMin = Vector2.zero;
            srt.anchorMax = Vector2.one;
            srt.offsetMin = Vector2.zero;
            srt.offsetMax = Vector2.zero;
            _slotView = go.GetComponent<ItemSlotView>();
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
            SoundManager.Sfx(SoundId.UiSlotSelect); // 아이템 칸을 집는 순간(사운드 정의서 §4.1)
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
            RaiseAboveOtherPanels(true); // 큐브 등 다른 패널 뒤로 숨지 않게

            // 이 장비를 놓을 수 있는 부위 칸을 강조해, 어디에 떨어뜨려야 장착되는지 보이게 한다.
            Controller.HighlightEquipTarget(_data, true);
        }

        /// <summary>
        /// 끌고 있는 동안 아이콘을 <b>모든 기능 패널 위</b>에 그린다.
        /// <para>가방 캔버스는 정렬 순서 100이고 큐브는 112라, 그대로 두면 <b>끌고 있는 아이콘이 큐브 창 뒤로
        /// 숨어</b> 어디에 놓는지 보이지 않는다. 아이콘 자신에게 캔버스를 붙여 정렬을 덮어쓰고, 놓으면 되돌린다.</para>
        /// </summary>
        private void RaiseAboveOtherPanels(bool raise)
        {
            var overrideCanvas = GetComponent<Canvas>();
            if (raise)
            {
                if (overrideCanvas == null)
                {
                    overrideCanvas = gameObject.AddComponent<Canvas>();
                }
                overrideCanvas.overrideSorting = true;
                overrideCanvas.sortingOrder = UiSortingOrder.DraggedItem;
            }
            else if (overrideCanvas != null)
            {
                // Destroy는 프레임 끝에 처리되므로, 그 사이에도 정렬이 남지 않도록 덮어쓰기를 먼저 끈다.
                overrideCanvas.overrideSorting = false;
                Destroy(overrideCanvas);
            }
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
            RaiseAboveOtherPanels(false);
            Controller.HighlightEquipTarget(_data, false);

            // 큐브 창의 등록 칸에 떨어뜨렸다 → 그 탭에 아이템을 올린다(가방 칸 이동이 아니다).
            var cubeSlot = FindCubeSlotUnderPointer(eventData);
            if (cubeSlot != null && cubeSlot.AcceptsDrop)
            {
                if (_originSlot != null)
                {
                    _originSlot.SetItem(this); // 등록은 원본을 소비하지 않는다 — 제자리로 되돌린다
                }
                var cube = cubeSlot.GetComponentInParent<CubePanelController>();
                if (cube != null)
                {
                    cube.TryRegisterFromInventory(_data.itemId);
                }
                return;
            }

            var target = FindSlotUnderPointer(eventData);
            if (target != null && target.IsEquipSlot)
            {
                // 장비 부위 칸에 떨어뜨렸다 → 장착 시도(가방 칸 이동이 아니다).
                Controller.TryEquipByDrag(this, _originSlot, target);
                return;
            }
            Controller.MoveItem(this, _originSlot, target);
        }

        /// <summary>포인터 아래의 큐브 등록 칸을 찾는다(큐브 창이 열려 있지 않으면 null).</summary>
        private static CubeDropSlot FindCubeSlotUnderPointer(PointerEventData eventData)
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
                var slot = r.gameObject.GetComponentInParent<CubeDropSlot>();
                if (slot != null)
                {
                    return slot;
                }
            }
            return null;
        }

        /// <summary>포인터 아래의 슬롯을 찾는다(가방 격자 칸·장비 부위 칸 모두). 없으면 null.</summary>
        private InventoryItemSlot FindSlotUnderPointer(PointerEventData eventData)
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

        /// <summary>
        /// <b>우클릭이면 큐브 창의 현재 탭에 바로 등록</b>한다(끌어다 놓는 것과 같은 결과).
        /// 큐브 창이 열려 있지 않으면 아무 일도 하지 않는다 — 좌클릭은 종전대로 아무 동작이 없다.
        /// <para>큐브는 가방과 함께 열리므로 활성 인스턴스를 찾으면 그것이 지금 보이는 창이다.</para>
        /// </summary>
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Right || _dragging)
            {
                return;
            }
            var cube = Object.FindAnyObjectByType<CubePanelController>(); // 비활성은 제외 = 열려 있을 때만
            if (cube == null)
            {
                return;
            }
            Controller.RequestHideTooltip();
            cube.TryRegisterFromInventory(_data.itemId);
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
