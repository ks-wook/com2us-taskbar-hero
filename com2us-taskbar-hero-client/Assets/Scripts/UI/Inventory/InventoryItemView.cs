using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TaskbarHero.Client.Managers;

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
            public long itemId;       // 서버 아이템 id(장착/해제 요청용)
            public int equippedSlot;  // 현재 장착 슬롯(1~6). 0 = 가방(미장착)
            public bool equippable;   // 착용 가능 여부(장비 + 현재 캐릭터 클래스·레벨 허용). 장착 버튼 활성 조건
            public bool equipLocked;  // 착용 불가 장비(클래스 불일치 또는 레벨 미달) → 슬롯에 X 표시 + 흐림
            public bool usable;       // 소모품(item_type=4) 여부. true면 툴팁 버튼이 '장착'이 아니라 '사용'이 된다
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

        /// <summary>표시 데이터로 등급 배경 + 아이콘/라벨을 구성한다.
        /// GO 자체 Image는 등급 배경색(레이캐스트 대상), 실제 아이콘 스프라이트는 자식으로 그린다.</summary>
        public void Setup(Display display, Font font)
        {
            _data = display;
            _rt = (RectTransform)transform;

            // 배경(GO 이미지) = 등급 색. 드래그/hover 레이캐스트 대상.
            _icon = gameObject.GetComponent<Image>();
            if (_icon == null)
            {
                _icon = gameObject.AddComponent<Image>();
            }
            _icon.sprite = null;
            _icon.color = InventoryPanelController.GradeBackgroundColor(display.gradeValue);
            _icon.raycastTarget = true;

            bool hasSprite = display.icon != null;
            bool locked = display.equipLocked; // 착용 불가(클래스 불일치·레벨 미달): 흐리게 + X 표시

            // 아이콘 스프라이트(자식) — 배경 위에 표시.
            var iconTf = transform.Find("IconSprite");
            var iconGo = iconTf != null ? iconTf.gameObject
                : new GameObject("IconSprite", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(transform, false);
            var iconImg = iconGo.GetComponent<Image>();
            iconImg.raycastTarget = false;
            iconImg.preserveAspect = true;
            iconImg.sprite = hasSprite ? display.icon : null;
            Color iconBase = hasSprite ? Color.white : display.iconColor;
            iconImg.color = locked ? new Color(iconBase.r, iconBase.g, iconBase.b, 0.35f) : iconBase; // 잠금 시 흐리게
            iconImg.enabled = hasSprite || string.IsNullOrEmpty(display.name); // 스프라이트 없고 이름도 없으면 색 사각형
            var irt = (RectTransform)iconGo.transform;
            irt.anchorMin = Vector2.zero;
            irt.anchorMax = Vector2.one;
            irt.offsetMin = new Vector2(6f, 6f);
            irt.offsetMax = new Vector2(-6f, -6f);

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

        /// <summary>에디터 빌드 호환용 별칭.</summary>
        public void EditorSetup(Display display, Font font) => Setup(display, font);

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
