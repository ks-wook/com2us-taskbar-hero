using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TaskbarHero.Client.Managers;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 아이템 hover 시 뜨는 상세 툴팁 창. 위젯은 프리팹에 직렬화되고, 계층은 에디터 빌드 시 생성된다.
    /// 커서를 칸→툴팁으로 옮겨도 유지되도록 자체 pointer enter/exit로 닫기를 취소한다(keep-open).
    /// 좌측 버튼은 장비면 '장착', 소모품(item_type=4)이면 '사용'으로 바뀌며, 각각 서버에 요청을 보낸다.
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

        /// <summary>커서와 툴팁 사이 간격. 이만큼 떨어뜨려 툴팁이 커서를 덮지 않게 한다.</summary>
        private const float CursorGap = 18f;

        /// <summary>닫을 시각(<see cref="Time.unscaledTime"/> 기준, -1 = 예약 없음).
        /// <b>Invoke를 쓰지 않는 이유</b>: Invoke는 <see cref="Time.timeScale"/>에 비례해 지연되므로,
        /// 스테이지 클리어 슬로우모션(0.25배, 최저 0.01배) 중에는 닫힘이 4~400배 늦어져 툴팁이 남는다.</summary>
        private float _hideAt = -1f;

        private InventoryItemView.Display _current; // 현재 표시 중인 아이템(장착/해제/사용 대상)
        private Text _actionLabel;                  // 좌측 버튼 라벨('장착'/'사용' 전환, 지연 캐싱)
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

        /// <summary>상세 정보를 채우고 커서 근처에 표시한다.
        /// <b>표시음은 재생하지 않는다</b> — hover만으로 뜨는 툴팁이라 아이템 칸 위를 지나갈 때마다 울려 거슬린다.</summary>
        public void Show(InventoryItemView.Display data, Vector2 screenPos)
        {
            _hideAt = -1f;
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

            // 소모품이면 같은 버튼을 '사용'으로 바꿔 쓴다(장착 개념이 없는 아이템이라 별도 버튼을 두지 않는다).
            // 장비는 종전대로 — 장착: 장비이고 미장착(가방)일 때 활성. 해제: 현재 장착 중일 때 활성.
            bool isEquipped = data.equippedSlot > 0;
            var label = ActionButtonLabel;
            if (label != null)
            {
                label.text = data.usable ? "사용" : "장착";
            }
            _equipButton.interactable = data.usable || (data.equippable && !isEquipped);
            _unequipButton.interactable = isEquipped;

            Reposition(screenPos);
        }

        /// <summary>지연 닫기 예약(칸→툴팁 이동 중 취소될 수 있음).</summary>
        public void RequestHide()
        {
            _hideAt = Time.unscaledTime + HideDelay;
        }

        /// <summary>즉시 숨김(초기화·패널 열닫기용).</summary>
        public void HideImmediate()
        {
            _hideAt = -1f;
            gameObject.SetActive(false);
        }

        /// <summary>예약된 닫기를 처리한다(슬로우모션에 영향받지 않는 unscaled 시간 기준).</summary>
        private void Update()
        {
            if (_hideAt >= 0f && Time.unscaledTime >= _hideAt)
            {
                HideImmediate();
            }
        }

        /// <summary>
        /// 커서 스크린 좌표를 루트 로컬 좌표로 변환해 배치한다. 기본은 커서 우하단이고,
        /// 그쪽에 공간이 없으면 <b>반대쪽으로 뒤집어</b> 배치한다.
        /// <para>
        /// <b>뒤집는 이유</b>: 예전에는 그냥 화면 안으로 clamp했는데, 커서가 우하단 모서리
        /// (이 패널 기준 x&gt;382 · y&lt;-442)에 있으면 x·y가 동시에 당겨져 <b>툴팁이 커서를 덮었다</b>.
        /// 툴팁은 raycastTarget이라 커서를 가로채므로 아래 칸은 pointer exit, 툴팁은 pointer enter를 받아
        /// 닫기 예약이 취소되고, 툴팁이 닫히면 다시 칸이 hover되어 또 열리는 진동이 생겨
        /// "툴팁이 사라지지 않는" 증상이 됐다.
        /// </para>
        /// </summary>
        private void Reposition(Vector2 screenPos)
        {
            if (_rootRect == null)
            {
                return;
            }

            Vector2 cursor;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_rootRect, screenPos, null, out cursor);

            var size = _rt.sizeDelta;
            float halfW = _rootRect.rect.width * 0.5f;
            float halfH = _rootRect.rect.height * 0.5f;

            // pivot (0,1): x는 좌측 기준, y는 상단 기준.
            float x = cursor.x + CursorGap;
            if (x + size.x > halfW)
            {
                x = cursor.x - CursorGap - size.x; // 오른쪽 공간 부족 → 커서 왼쪽
            }

            float y = cursor.y - CursorGap;
            if (y - size.y < -halfH)
            {
                y = cursor.y + CursorGap + size.y; // 아래 공간 부족 → 커서 위쪽
            }

            // 뒤집어도 넘치는 극단(툴팁이 루트보다 큰 경우)에서만 화면 안으로 밀어넣는다.
            x = Mathf.Clamp(x, -halfW, Mathf.Max(-halfW, halfW - size.x));
            y = Mathf.Clamp(y, Mathf.Min(halfH, -halfH + size.y), halfH);
            _rt.anchoredPosition = new Vector2(x, y);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _hideAt = -1f; // 칸 → 툴팁으로 커서가 넘어옴 → 예약된 닫기 취소(keep-open)
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            RequestHide();
        }

        /// <summary>좌측 액션 버튼('장착'/'사용' 겸용)의 라벨. 계층이 프리팹에 구워져 있어 직렬화 참조 대신
        /// 최초 사용 시 자식에서 찾아 캐싱한다(옛 프리팹에도 새 필드 배선 없이 동작하도록).</summary>
        private Text ActionButtonLabel
        {
            get
            {
                if (_actionLabel == null && _equipButton != null)
                {
                    _actionLabel = _equipButton.GetComponentInChildren<Text>(true);
                }
                return _actionLabel;
            }
        }

        private void OnEquip()
        {
            if (Controller != null)
            {
                if (_current.usable)
                {
                    Controller.RequestUseConsumable(_current.itemId); // 소모품 = 사용
                }
                else
                {
                    Controller.RequestEquip(_current.itemId);
                }
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
