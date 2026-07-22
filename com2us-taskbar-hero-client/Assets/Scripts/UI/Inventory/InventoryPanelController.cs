using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TaskbarHero.Client.Managers;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 인벤토리/장비 오버레이 패널. 계층은 에디터 빌드 시 생성되어 프리팹에 정적으로 저장되고
    /// (에디터에서 바로 보임), 런타임에는 직렬화된 참조에 이벤트·표시만 배선한다.
    /// 서버 실데이터는 아직 연동하지 않으며, 이동 확인용 더미 아이템 1개와 캐릭터별 데모 장착을 포함한다.
    /// 기획서: docs/ui/인벤토리-ui-기획서.md
    /// </summary>
    public class InventoryPanelController : MonoBehaviour
    {
        [Header("UI 리소스 (Assets/Art/UI/Inventory)")]
        [SerializeField] private Sprite panelBackground; // ui_panel_background
        [SerializeField] private Sprite slotNormal;      // ui_slot_normal
        [SerializeField] private Sprite slotHighlight;   // ui_slot_highlight
        [SerializeField] private Sprite slotPortrait;    // ui_slot_portrait

        [Header("격자 설정")]
        [SerializeField] private int columns = 5;
        [Tooltip("최초 아이템 슬롯 수. 그리드 마지막에는 확장 버튼 1칸이 추가된다(초기 총 칸 = +1).")]
        [SerializeField] private int initialItemSlots = 14;
        [Tooltip("한 번에 보이는 줄 수(스크롤). 2줄 = 10칸.")]
        [SerializeField] private int visibleRows = 2;
        [SerializeField] private bool spawnDummyItem = true; // 이동 확인용 더미(추후 삭제)

        [Header("파티 설정(데모)")]
        [Tooltip("파티 캐릭터 수(실데이터 연동 전 데모값, 최대 3).")]
        [SerializeField] private int partyCount = 3;

        [Header("구성 참조 (에디터 빌더가 배선 — 직접 수정 불필요)")]
        [SerializeField] private List<InventoryItemSlot> _gridSlots = new List<InventoryItemSlot>();
        [SerializeField] private List<InventoryItemSlot> _equipSlots = new List<InventoryItemSlot>();
        [SerializeField] private InventoryTooltip _tooltip;
        [SerializeField] private Text _charIndicatorText;
        [SerializeField] private Text _portraitLabel;
        [SerializeField] private Button _prevButton;
        [SerializeField] private Button _nextButton;
        [SerializeField] private Button _closeButton;
        [SerializeField] private Button _dimButton;
        [SerializeField] private RectTransform _gridContent; // 스크롤 콘텐츠(슬롯 부모)
        [SerializeField] private Button _expandButton;        // 확장 요청 버튼(항상 마지막 칸)

        // 장착 슬롯 이름(equip_slot_master 1~6, 표시용 상수)
        private static readonly string[] EquipSlotNames = { "무기", "보조무기", "투구", "갑옷", "장갑", "신발" };

        // 데모용 파티 캐릭터 이름(추후 실데이터 characters로 대체)
        private static readonly string[] DummyClassNames = { "전사", "레인저", "마법사" };

        private const float CanvasRefWidth = 1080f;
        private const float CanvasRefHeight = 1920f;

        private Font _font;
        private RectTransform _rootRect;      // 전체 화면(Canvas) Rect
        private int _selectedCharacter;       // 현재 보고 있는 파티 캐릭터(0-based)

        /// <summary>정적 계층이 이미 구성돼 있으면 true(프리팹에서 로드된 경우).</summary>
        private bool AlreadyBuilt => _tooltip != null;

        private void Awake()
        {
            _rootRect = (RectTransform)transform;
            if (AlreadyBuilt)
            {
                _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            else
            {
                Construct(); // 폴백: 정적 계층이 없으면 런타임 생성
            }

            WireRuntime();
        }

        /// <summary>에디터 빌드 전용: 전체 계층을 생성하고 참조를 배선한다(프리팹 저장용).</summary>
        public void EditorConstruct()
        {
            Construct();
        }

        // ── 구성(에디터 빌드 또는 런타임 폴백) ──

        /// <summary>패널 전체 계층을 1회 생성하고 직렬화 참조를 채운다.</summary>
        private void Construct()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            BuildCanvas();
            BuildDim();
            var container = BuildContainer();
            BuildHeader(container);
            BuildEquipArea(container); // 캐릭터 네비게이션 포함, 가로 중앙 정렬
            BuildGrid(container);
            BuildTooltip();

            if (spawnDummyItem && _gridSlots.Count > 0)
            {
                SpawnDummy(_gridSlots[0]);
            }

            if (_charIndicatorText != null)
            {
                _charIndicatorText.text = $"캐릭터 1 / {partyCount}";
            }
        }

        /// <summary>런타임 배선: 버튼 리스너 등록 + 선택 캐릭터 표시 갱신.</summary>
        private void WireRuntime()
        {
            if (_prevButton != null)
            {
                _prevButton.onClick.AddListener(OnPrevCharacter);
            }
            if (_nextButton != null)
            {
                _nextButton.onClick.AddListener(OnNextCharacter);
            }
            if (_closeButton != null)
            {
                _closeButton.onClick.AddListener(Close);
            }
            if (_dimButton != null)
            {
                _dimButton.onClick.AddListener(Close);
            }
            if (_expandButton != null)
            {
                _expandButton.onClick.AddListener(OnExpandInventory);
            }

            RefreshCharacter();
        }

        /// <summary>인벤토리 확장 요청: 아이템 슬롯을 1칸 늘리고 확장 버튼을 항상 마지막으로 둔다.
        /// 현재는 로컬 데모(서버 미연동). 추후 /api/game/inventory/expand 결과로 대체.</summary>
        private void OnExpandInventory()
        {
            var slot = CreateGridSlot(_gridSlots.Count, _gridContent);
            _gridSlots.Add(slot);
            if (_expandButton != null)
            {
                _expandButton.transform.SetAsLastSibling(); // 확장 버튼은 항상 마지막 칸
            }
            Debug.Log($"[Inventory] 인벤토리 확장(+1) → 아이템 슬롯 {_gridSlots.Count}칸 (로컬 데모, 서버 미연동)");
        }

        /// <summary>루트 GameObject에 오버레이 Canvas/스케일러/레이캐스터를 부착한다.</summary>
        private void BuildCanvas()
        {
            var canvas = gameObject.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = gameObject.AddComponent<Canvas>();
            }
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100; // 게임 뷰/HUD 위에 겹쳐 표시

            var scaler = gameObject.GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                scaler = gameObject.AddComponent<CanvasScaler>();
            }
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(CanvasRefWidth, CanvasRefHeight);
            scaler.matchWidthOrHeight = 0.5f;

            if (gameObject.GetComponent<GraphicRaycaster>() == null)
            {
                gameObject.AddComponent<GraphicRaycaster>();
            }

            _rootRect = (RectTransform)transform;
        }

        /// <summary>패널 밖 클릭 시 닫히는 반투명 딤 배경.</summary>
        private void BuildDim()
        {
            var img = NewImage("Dim", _rootRect, null);
            img.color = new Color(0f, 0f, 0f, 0.6f);
            Stretch(img.rectTransform);
            _dimButton = img.gameObject.AddComponent<Button>();
            _dimButton.transition = Selectable.Transition.None;
        }

        /// <summary>패널 본체(배경 이미지) 컨테이너.</summary>
        private RectTransform BuildContainer()
        {
            var img = NewImage("PanelRoot", _rootRect, panelBackground);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(920f, 1640f);
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        /// <summary>제목(가로 중앙) + 닫기 버튼(우측 상단).</summary>
        private void BuildHeader(RectTransform container)
        {
            // 제목: 패널 가로 중앙 정렬
            var title = NewText("Title", container, "인벤토리", 44, TextAnchor.UpperCenter);
            var trt = title.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 1f);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.anchoredPosition = new Vector2(0f, -40f);
            trt.sizeDelta = new Vector2(500f, 56f);

            // 닫기: 중앙 정렬 콘텐츠(가방 블록 폭)의 우측 상단에 배치
            var closeImg = NewImage("CloseButton", container, slotNormal);
            var crt = closeImg.rectTransform;
            crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.anchoredPosition = new Vector2(BagBlockWidth * 0.5f - 44f, -36f);
            crt.sizeDelta = new Vector2(72f, 72f);
            var x = NewText("X", crt, "X", 36, TextAnchor.MiddleCenter);
            Stretch(x.rectTransform);
            _closeButton = closeImg.gameObject.AddComponent<Button>();
        }

        // 장비 영역 블록의 내부 폭(캐릭터 네비 + 초상 + 6부위 슬롯을 포함). 이 블록을 패널 가로 중앙에 둔다.
        private const float EquipBlockWidth = 466f;

        /// <summary>장비 영역(캐릭터 화살표 네비 + 초상 + 6부위 슬롯)을 패널 가로 중앙 컨테이너에 구성한다.</summary>
        private void BuildEquipArea(RectTransform container)
        {
            // 가로 중앙 정렬 컨테이너(패널 폭과 무관하게 중앙 고정). 자식은 이 블록의 좌상단 기준으로 배치.
            var area = NewRect("EquipArea", container);
            area.anchorMin = area.anchorMax = new Vector2(0.5f, 1f);
            area.pivot = new Vector2(0.5f, 1f);
            area.sizeDelta = new Vector2(EquipBlockWidth, 476f);
            area.anchoredPosition = new Vector2(0f, -104f);

            // 캐릭터 전환 네비게이션 (◀ 인디케이터 ▶)
            var prev = NewImage("PrevCharButton", area, slotNormal);
            TopLeft(prev.rectTransform, 0f, 0f, 64f, 64f);
            Stretch(NewText("PrevLabel", prev.rectTransform, "<", 36, TextAnchor.MiddleCenter).rectTransform);
            _prevButton = prev.gameObject.AddComponent<Button>();

            _charIndicatorText = NewText("CharIndicator", area, "", 28, TextAnchor.MiddleCenter);
            TopLeft(_charIndicatorText.rectTransform, 70f, 0f, 326f, 64f);

            var next = NewImage("NextCharButton", area, slotNormal);
            TopLeft(next.rectTransform, EquipBlockWidth - 64f, 0f, 64f, 64f);
            Stretch(NewText("NextLabel", next.rectTransform, ">", 36, TextAnchor.MiddleCenter).rectTransform);
            _nextButton = next.gameObject.AddComponent<Button>();

            // '장비' 라벨
            var label = NewText("EquipLabel", area, "장비", 32, TextAnchor.UpperLeft);
            TopLeft(label.rectTransform, 0f, 90f, 200f, 44f);

            // 캐릭터 초상 슬롯(크기 유지)
            var portrait = NewImage("PortraitSlot", area, slotPortrait);
            TopLeft(portrait.rectTransform, 0f, 144f, 220f, 300f);
            _portraitLabel = NewText("PortraitLabel", portrait.rectTransform, "캐릭터", 24, TextAnchor.LowerCenter);
            Stretch(_portraitLabel.rectTransform);

            // 6부위 장착 슬롯 (2열 x 3행) — 초상화보다 작은 크기
            const float startX = 250f;
            const float startY = 144f;
            const float cell = 100f;
            const float gap = 16f;
            for (int i = 0; i < 6; i++)
            {
                int col = i % 2;
                int row = i / 2;
                var slotImg = NewImage($"EquipSlot{i + 1}", area, slotPortrait);
                TopLeft(slotImg.rectTransform,
                    startX + col * (cell + gap),
                    startY + row * (cell + gap),
                    cell, cell);

                var frame = NewImage("HoverFrame", slotImg.rectTransform, slotHighlight);
                Stretch(frame.rectTransform);
                frame.raycastTarget = false;
                frame.gameObject.SetActive(false);

                var name = NewText("PartLabel", slotImg.rectTransform, EquipSlotNames[i], 22, TextAnchor.MiddleCenter);
                Stretch(name.rectTransform);

                var slot = slotImg.gameObject.AddComponent<InventoryItemSlot>();
                slot.EditorInit(i, isEquipSlot: true, hoverFrame: frame.gameObject, partLabel: name);
                _equipSlots.Add(slot);
            }
        }

        private const float GridCell = 120f;
        private const float GridSpacing = 12f;
        private const float ScrollbarWidth = 18f;

        // 가방 블록 전체 폭(격자 + 스크롤바 + 좌우 여백). 제목의 닫기 버튼 위치와 가방 컨테이너에 공유.
        private float BagBlockWidth => columns * GridCell + (columns - 1) * GridSpacing + ScrollbarWidth + 32f;

        /// <summary>인벤토리 격자: 스크롤 뷰(visibleRows줄만 표시) + 초기 슬롯 + 마지막 확장 버튼.</summary>
        private void BuildGrid(RectTransform container)
        {
            float viewW = columns * GridCell + (columns - 1) * GridSpacing;
            float viewH = visibleRows * GridCell + (visibleRows - 1) * GridSpacing;

            // 가방 영역도 패널 가로 중앙 컨테이너로 묶는다(장비 영역과 동일 정렬).
            var area = NewRect("BagArea", container);
            area.anchorMin = area.anchorMax = new Vector2(0.5f, 1f);
            area.pivot = new Vector2(0.5f, 1f);
            area.sizeDelta = new Vector2(BagBlockWidth, viewH + 74f);
            area.anchoredPosition = new Vector2(0f, -596f);

            var label = NewText("InventoryLabel", area, "가방", 32, TextAnchor.UpperLeft);
            TopLeft(label.rectTransform, 8f, 0f, 300f, 44f);

            // 가방 영역 배경(다른 영역과 구분되는 어두운 색). 슬롯·스크롤바를 함께 감싼다.
            var bagBg = NewImage("BagBackground", area, null);
            bagBg.color = new Color(0.09f, 0.11f, 0.18f, 0.9f);
            TopLeft(bagBg.rectTransform, 0f, 48f, BagBlockWidth, viewH + 20f);

            float scrollX = 12f;
            float scrollY = 54f;

            // 스크롤 루트(뷰포트 크기 = visibleRows줄). 배경은 BagBackground가 담당하므로 거의 투명.
            var scrollGo = NewImage("InventoryScroll", area, null);
            scrollGo.color = new Color(0f, 0f, 0f, 0.001f);
            TopLeft(scrollGo.rectTransform, scrollX, scrollY, viewW, viewH);
            var scroll = scrollGo.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;

            // 뷰포트(마스크로 넘치는 줄을 잘라냄)
            var viewport = NewImage("Viewport", scrollGo.rectTransform, null);
            viewport.color = new Color(0f, 0f, 0f, 0.001f);
            Stretch(viewport.rectTransform);
            viewport.gameObject.AddComponent<RectMask2D>();
            scroll.viewport = viewport.rectTransform;

            // 콘텐츠(슬롯 부모, 세로로 늘어남)
            _gridContent = NewRect("Content", viewport.rectTransform);
            _gridContent.anchorMin = new Vector2(0f, 1f);
            _gridContent.anchorMax = new Vector2(1f, 1f);
            _gridContent.pivot = new Vector2(0.5f, 1f);
            _gridContent.anchoredPosition = Vector2.zero;
            _gridContent.sizeDelta = new Vector2(0f, 0f);

            var grid = _gridContent.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(GridCell, GridCell);
            grid.spacing = new Vector2(GridSpacing, GridSpacing);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = columns;
            grid.childAlignment = TextAnchor.UpperLeft;

            var fitter = _gridContent.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = _gridContent;

            // 스크롤 가능함을 알리는 우측 세로 스크롤바(항상 표시)
            var bar = BuildScrollbar(area, scrollX + viewW + 8f, scrollY, ScrollbarWidth, viewH);
            scroll.verticalScrollbar = bar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            // 초기 아이템 슬롯
            for (int i = 0; i < initialItemSlots; i++)
            {
                _gridSlots.Add(CreateGridSlot(i, _gridContent));
            }

            // 마지막 칸: 확장 버튼
            CreateExpandButton(_gridContent);
        }

        /// <summary>격자 아이템 슬롯 한 칸을 생성한다(빌드·런타임 확장 공용).</summary>
        private InventoryItemSlot CreateGridSlot(int index, RectTransform content)
        {
            var slotImg = NewImage($"Slot{index}", content, slotNormal);

            var frame = NewImage("HoverFrame", slotImg.rectTransform, slotHighlight);
            Stretch(frame.rectTransform);
            frame.raycastTarget = false;
            frame.gameObject.SetActive(false);

            var slot = slotImg.gameObject.AddComponent<InventoryItemSlot>();
            slot.EditorInit(index, isEquipSlot: false, hoverFrame: frame.gameObject, partLabel: null);
            return slot;
        }

        /// <summary>그리드 마지막의 인벤토리 확장 요청 버튼을 생성한다(아이템 칸 아님).</summary>
        private void CreateExpandButton(RectTransform content)
        {
            var img = NewImage("ExpandButton", content, slotNormal);
            img.color = new Color(0.25f, 0.28f, 0.4f, 0.95f);

            var plus = NewText("Plus", img.rectTransform, "＋", 64, TextAnchor.MiddleCenter);
            Stretch(plus.rectTransform);
            var cap = NewText("ExpandCaption", img.rectTransform, "확장", 22, TextAnchor.LowerCenter);
            Stretch(cap.rectTransform);

            _expandButton = img.gameObject.AddComponent<Button>();
        }

        /// <summary>세로 스크롤바(배경+핸들)를 생성해 ScrollRect에 연결할 컴포넌트를 반환한다.</summary>
        private Scrollbar BuildScrollbar(RectTransform container, float x, float y, float w, float h)
        {
            var barBg = NewImage("InventoryScrollbar", container, null);
            barBg.color = new Color(0f, 0f, 0f, 0.35f);
            TopLeft(barBg.rectTransform, x, y, w, h);

            var sb = barBg.gameObject.AddComponent<Scrollbar>();
            sb.direction = Scrollbar.Direction.BottomToTop;

            var slidingArea = NewRect("Sliding Area", barBg.rectTransform);
            slidingArea.anchorMin = Vector2.zero;
            slidingArea.anchorMax = Vector2.one;
            slidingArea.offsetMin = new Vector2(2f, 2f);
            slidingArea.offsetMax = new Vector2(-2f, -2f);

            var handle = NewImage("Handle", slidingArea, null);
            handle.color = new Color(0.55f, 0.60f, 0.78f, 0.95f);
            handle.rectTransform.offsetMin = Vector2.zero;
            handle.rectTransform.offsetMax = Vector2.zero;

            sb.handleRect = handle.rectTransform;
            sb.targetGraphic = handle;
            return sb;
        }

        /// <summary>hover 상세 툴팁을 루트에 생성한다(에디터 빌드에선 비활성 저장).</summary>
        private void BuildTooltip()
        {
            var go = NewImage("InventoryTooltip", _rootRect, panelBackground);
            go.rectTransform.anchorMin = go.rectTransform.anchorMax = new Vector2(0f, 0f);
            go.rectTransform.pivot = new Vector2(0f, 1f);
            go.rectTransform.sizeDelta = new Vector2(320f, 260f);
            _tooltip = go.gameObject.AddComponent<InventoryTooltip>();
            _tooltip.EditorBuild(_font, _rootRect);
            _tooltip.gameObject.SetActive(false);
        }

        /// <summary>이동 확인용 더미 아이템을 지정 슬롯에 배치한다(추후 삭제).</summary>
        private void SpawnDummy(InventoryItemSlot slot)
        {
            var go = NewRect("DummyItem", slot.transform);
            var view = go.gameObject.AddComponent<InventoryItemView>();
            var display = new InventoryItemView.Display
            {
                name = "테스트 검",
                grade = "영웅",
                slotName = "무기",
                requirement = "요구 Lv.10 / 기사",
                stats = "ATK +50",
                iconColor = new Color(0.95f, 0.78f, 0.28f, 1f),
            };
            view.EditorSetup(display, _font);
            slot.SetItem(view);
        }

        // ── 슬롯/아이템에서 호출하는 콜백 ──

        /// <summary>슬롯 hover 진입/이탈 시 하이라이트 프레임 토글.</summary>
        public void OnSlotHover(InventoryItemSlot slot, bool entered)
        {
            slot.SetHighlight(entered);
        }

        /// <summary>아이템 hover 시 툴팁 표시.</summary>
        public void ShowTooltip(InventoryItemView view, Vector2 screenPos)
        {
            ShowTooltip(view.Data, screenPos);
        }

        /// <summary>표시 데이터로 툴팁 표시(장착 슬롯 등 InventoryItemView가 없는 경우).</summary>
        public void ShowTooltip(InventoryItemView.Display data, Vector2 screenPos)
        {
            if (_tooltip != null)
            {
                _tooltip.Show(data, screenPos);
            }
        }

        /// <summary>툴팁 닫기 예약(칸→툴팁 이동 시 유지되도록 지연).</summary>
        public void RequestHideTooltip()
        {
            if (_tooltip != null)
            {
                _tooltip.RequestHide();
            }
        }

        // ── 캐릭터 전환(파티 네비게이션) ──

        /// <summary>이전 파티 캐릭터로 전환(순환).</summary>
        private void OnPrevCharacter()
        {
            if (partyCount <= 1)
            {
                return;
            }
            _selectedCharacter = (_selectedCharacter - 1 + partyCount) % partyCount;
            RefreshCharacter();
        }

        /// <summary>다음 파티 캐릭터로 전환(순환).</summary>
        private void OnNextCharacter()
        {
            if (partyCount <= 1)
            {
                return;
            }
            _selectedCharacter = (_selectedCharacter + 1) % partyCount;
            RefreshCharacter();
        }

        /// <summary>선택된 캐릭터의 인디케이터·초상 라벨·장착 슬롯을 갱신한다.</summary>
        private void RefreshCharacter()
        {
            if (_charIndicatorText != null)
            {
                _charIndicatorText.text = $"캐릭터 {_selectedCharacter + 1} / {partyCount}";
            }
            if (_portraitLabel != null)
            {
                _portraitLabel.text = _selectedCharacter < DummyClassNames.Length
                    ? DummyClassNames[_selectedCharacter]
                    : $"캐릭터 {_selectedCharacter + 1}";
            }
            RefreshEquip();
        }

        /// <summary>선택된 캐릭터의 6부위 장착 상태를 장비 슬롯에 반영한다(현재는 데모 데이터).</summary>
        private void RefreshEquip()
        {
            for (int slot = 0; slot < _equipSlots.Count; slot++)
            {
                _equipSlots[slot].SetEquippedDemo(DummyEquip(_selectedCharacter, slot), _font);
            }
        }

        /// <summary>데모용 캐릭터별 장착 아이템(추후 실데이터로 대체). 미장착이면 null.</summary>
        private static InventoryItemView.Display? DummyEquip(int character, int slot)
        {
            // slot: 0무기 1보조무기 2투구 3갑옷 4장갑 5신발
            switch (character)
            {
                case 0: // 전사
                    if (slot == 0) return MakeDisplay("롱소드", "영웅", "무기", "요구 Lv.10 / 전사", "ATK +50", new Color(0.95f, 0.78f, 0.28f, 1f));
                    if (slot == 3) return MakeDisplay("판금 갑옷", "고급", "갑옷", "요구 Lv.10 / 전사", "DEF +30", new Color(0.60f, 0.66f, 0.75f, 1f));
                    break;
                case 1: // 레인저
                    if (slot == 0) return MakeDisplay("장궁", "희귀", "무기", "요구 Lv.10 / 레인저", "ATK +40", new Color(0.40f, 0.80f, 0.50f, 1f));
                    if (slot == 5) return MakeDisplay("가죽 부츠", "노말", "신발", "요구 Lv.5 / 레인저", "SPD +8%", new Color(0.70f, 0.55f, 0.35f, 1f));
                    break;
                case 2: // 마법사
                    if (slot == 1) return MakeDisplay("마도서", "전설", "보조무기", "요구 Lv.15 / 마법사", "ATK +70", new Color(0.70f, 0.50f, 0.95f, 1f));
                    break;
            }
            return null;
        }

        private static InventoryItemView.Display MakeDisplay(string name, string grade, string slotName,
            string requirement, string stats, Color iconColor)
        {
            return new InventoryItemView.Display
            {
                name = name,
                grade = grade,
                slotName = slotName,
                requirement = requirement,
                stats = stats,
                iconColor = iconColor,
            };
        }

        // ── 아이템 이동 ──

        /// <summary>드래그된 아이템을 대상 슬롯으로 이동(점유 시 스왑). 서버 미연동 로컬 처리.</summary>
        public void MoveItem(InventoryItemView view, InventoryItemSlot from, InventoryItemSlot to)
        {
            if (to == null || to == from)
            {
                if (from != null)
                {
                    from.SetItem(view); // 원위치
                }
                return;
            }

            var occupant = to.Item;
            to.SetItem(view);
            if (from != null)
            {
                from.SetItem(occupant); // 점유 아이템은 원래 칸으로(스왑), 없으면 null
            }

            Debug.Log($"[Inventory] 더미 이동: slot {(from != null ? from.Index : -1)} → {to.Index}" +
                      (occupant != null ? " (스왑)" : string.Empty));
        }

        /// <summary>패널을 닫는다(UIManager 우선, 없으면 자체 비활성).</summary>
        public void Close()
        {
            if (UIManager.Instance != null)
            {
                UIManager.Instance.Hide(UIManager.PanelType.Inventory);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }

        // ── UI 생성 헬퍼 ──

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static Image NewImage(string name, Transform parent, Sprite sprite)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.type = Image.Type.Simple;
            return img;
        }

        private Text NewText(string name, Transform parent, string content, int size, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = _font;
            t.text = content;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = Color.white;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        /// <summary>부모 Rect를 꽉 채우도록 스트레치.</summary>
        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>부모(컨테이너)의 좌상단 기준으로 배치.</summary>
        private static void TopLeft(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }
    }
}
