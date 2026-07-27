using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 아이템 hover 시 뜨는 상세 툴팁 창. 위젯은 프리팹에 직렬화되고, 계층은 에디터 빌드 시 생성된다.
    /// 커서를 칸→툴팁으로 옮겨도 유지되도록 자체 pointer enter/exit로 닫기를 취소한다(keep-open).
    /// 서버 미연동 상태이므로 장착/해제 버튼은 아직 로그만 남긴다.
    /// </summary>
    public class InventoryTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [Tooltip("툴팁 배경(Assets/Art/UI/item_detail_bg) — 공용 아이템 상세 팝업과 배경 통일. 없으면 단색 배경.")]
        [SerializeField] private Sprite _backgroundSprite;
        [SerializeField] private RectTransform _rootRect;
        [SerializeField] private Text _nameText;
        [SerializeField] private Text _subText;
        [SerializeField] private Text _reqText;
        [SerializeField] private Text _statsText;
        [SerializeField] private Button _equipButton;
        [SerializeField] private Button _unequipButton;

        private RectTransform _rt;
        private const float HideDelay = 0.04f;

        private InventoryItemView.Display _current; // 현재 표시 중인 아이템(장착/해제 대상)
        private InventoryPanelController _controller;
        private InventoryPanelController Controller =>
            _controller != null ? _controller : (_controller = GetComponentInParent<InventoryPanelController>());

        private void Awake()
        {
            _rt = (RectTransform)transform;
            ApplyBackground();
            if (_equipButton != null)
            {
                _equipButton.onClick.AddListener(OnEquip);
            }
            if (_unequipButton != null)
            {
                _unequipButton.onClick.AddListener(OnUnequip);
            }
            HideImmediate();
        }

        /// <summary>에디터 빌드 전용: 툴팁 내부 위젯을 구성한다.</summary>
        public void EditorBuild(Font font, RectTransform rootRect)
        {
            _rt = (RectTransform)transform;
            _rootRect = rootRect;
            _rt.anchorMin = _rt.anchorMax = new Vector2(0.5f, 0.5f);
            _rt.pivot = new Vector2(0f, 1f);

            var bg = GetComponent<Image>();
            if (bg != null)
            {
                bg.raycastTarget = true;
            }

            _nameText = MakeText(font, "Name", "", 26, TextAnchor.UpperLeft, 16f, 14f, 288f, 34f);
            _subText = MakeText(font, "Sub", "", 20, TextAnchor.UpperLeft, 16f, 52f, 288f, 28f);
            _reqText = MakeText(font, "Req", "", 18, TextAnchor.UpperLeft, 16f, 84f, 288f, 28f);
            _statsText = MakeText(font, "Stats", "", 18, TextAnchor.UpperLeft, 16f, 116f, 288f, 60f);

            _equipButton = MakeButton(font, "EquipButton", "장착", 16f, 200f, 130f, 44f);
            _unequipButton = MakeButton(font, "UnequipButton", "해제", 174f, 200f, 130f, 44f);
        }

        /// <summary>배경 스프라이트(item_detail_bg)가 배선돼 있으면 툴팁 배경에 적용해
        /// 공용 아이템 상세 팝업(ItemDetailPopup)과 외형을 통일한다(없으면 기존 단색 유지).</summary>
        private void ApplyBackground()
        {
            if (_backgroundSprite == null)
            {
                return;
            }
            var bg = GetComponent<Image>();
            if (bg != null)
            {
                bg.sprite = _backgroundSprite;
                bg.type = Image.Type.Simple;
                bg.color = Color.white;
            }
        }

        /// <summary>상세 정보를 채우고 커서 근처에 표시한다.</summary>
        public void Show(InventoryItemView.Display data, Vector2 screenPos)
        {
            CancelInvoke(nameof(DoHide));
            gameObject.SetActive(true);
            transform.SetAsLastSibling();

            _current = data;
            _nameText.text = data.name;
            _nameText.color = InventoryPanelController.GradeNameColor(data.gradeValue); // 이름을 등급 색으로
            _subText.text = string.IsNullOrEmpty(data.slotName)
                ? data.grade
                : $"{data.grade} · {data.slotName}"; // 등급 · 종류(무기/방어구/재료 등)
            _reqText.text = data.requirement;
            _statsText.text = data.description;       // 아이템 설명

            // 장착: 장비이고 미장착(가방)일 때 활성. 해제: 현재 장착 중일 때 활성.
            bool isEquipped = data.equippedSlot > 0;
            _equipButton.interactable = data.equippable && !isEquipped;
            _unequipButton.interactable = isEquipped;

            Reposition(screenPos);
        }

        /// <summary>지연 닫기 예약(칸→툴팁 이동 중 취소될 수 있음).</summary>
        public void RequestHide()
        {
            CancelInvoke(nameof(DoHide));
            Invoke(nameof(DoHide), HideDelay);
        }

        /// <summary>즉시 숨김(초기화용).</summary>
        public void HideImmediate()
        {
            CancelInvoke(nameof(DoHide));
            gameObject.SetActive(false);
        }

        private void DoHide()
        {
            gameObject.SetActive(false);
        }

        /// <summary>커서 스크린 좌표를 루트 로컬 좌표로 변환해 배치하고 화면 안으로 클램프.</summary>
        private void Reposition(Vector2 screenPos)
        {
            if (_rootRect == null)
            {
                return;
            }

            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_rootRect, screenPos, null, out local);
            local += new Vector2(18f, -18f);

            var size = _rt.sizeDelta;
            float halfW = _rootRect.rect.width * 0.5f;
            float halfH = _rootRect.rect.height * 0.5f;

            // pivot (0,1): x는 좌측 기준, y는 상단 기준
            local.x = Mathf.Clamp(local.x, -halfW, halfW - size.x);
            local.y = Mathf.Clamp(local.y, -halfH + size.y, halfH);
            _rt.anchoredPosition = local;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            CancelInvoke(nameof(DoHide));
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            RequestHide();
        }

        private void OnEquip()
        {
            if (Controller != null)
            {
                Controller.RequestEquip(_current.itemId);
            }
            HideImmediate();
        }

        private void OnUnequip()
        {
            if (Controller != null && _current.equippedSlot > 0)
            {
                Controller.RequestUnequip(_current.equippedSlot);
            }
            HideImmediate();
        }

        private Text MakeText(Font font, string name, string content, int size, TextAnchor anchor,
            float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(transform, false);
            var t = go.GetComponent<Text>();
            t.font = font;
            t.text = content;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = Color.white;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return t;
        }

        private Button MakeButton(Font font, string name, string label, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            var img = go.GetComponent<Image>();
            img.color = new Color(0.25f, 0.28f, 0.4f, 1f);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);

            var txtGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            txtGo.transform.SetParent(go.transform, false);
            var t = txtGo.GetComponent<Text>();
            t.font = font;
            t.text = label;
            t.fontSize = 22;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = Color.white;
            t.raycastTarget = false;
            var trt = (RectTransform)txtGo.transform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;

            return go.AddComponent<Button>();
        }
    }
}
