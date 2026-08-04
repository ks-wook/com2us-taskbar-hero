using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TaskbarHero.Client.Managers;
using TaskbarHero.Client.MasterData;
using TaskbarHero.Common.Dto;
using TaskbarHero.Common.MasterData;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 캐릭터 스킬 레벨업 오버레이 패널(growth 기획서 §5.1·5.2). 인벤토리의 '스킬 레벨업' 버튼으로 진입한다.
    /// 계층은 에디터 빌드 시 정적 부분(제목·닫기·캐릭터 네비·스킬포인트 배너·스크롤·초기화 버튼)이 생성돼
    /// 프리팹에 저장되고, 표시될 때마다 세션 세이브(<see cref="Session.GameData"/>)의 실데이터로
    /// 스킬 목록·레벨·사용 가능 스킬 포인트를 채운다. 스킬 포인트는 저장값이 아니라 캐릭터 레벨에서 파생한다
    /// (사용 가능 = 레벨 비례 총량 − 그 캐릭터가 이미 투자한 스킬 레벨 합). 서버가 최종 확정한다(서버 권위).
    /// 기획서: docs/세부/growth-기획서.md §2·§5.1·§5.2
    /// </summary>
    public class SkillPanelController : MonoBehaviour
    {
        [Header("UI 리소스 (Assets/Art/UI/Inventory 공용)")]
        [SerializeField] private Sprite panelBackground; // ui_panel_background
        [SerializeField] private Sprite slotNormal;      // ui_slot_normal
        [SerializeField] private Sprite slotHighlight;   // ui_slot_highlight

        [Header("구성 참조 (에디터 빌더가 배선 — 직접 수정 불필요)")]
        [SerializeField] private Text _charIndicatorText;   // 직업 Lv.N · 캐릭터 i/N
        [SerializeField] private Text _pointText;           // 스킬 포인트 available / total
        [SerializeField] private Button _prevButton;
        [SerializeField] private Button _nextButton;
        [SerializeField] private Button _closeButton;
        [SerializeField] private Button _dimButton;
        [SerializeField] private Button _resetButton;
        [SerializeField] private Text _resetLabel;
        [SerializeField] private Text _messageText;         // 결과/오류 안내(하단)
        [SerializeField] private RectTransform _listContent; // 스킬 행 부모(스크롤 콘텐츠)
        // 액티브 스킬 장착 슬롯(최대 2). 아이콘/라벨/버튼 참조(에디터 빌더가 배선).
        [SerializeField] private Image _equipSlotIcon0;
        [SerializeField] private Image _equipSlotIcon1;
        [SerializeField] private Text _equipSlotLabel0;
        [SerializeField] private Text _equipSlotLabel1;
        [SerializeField] private Button _equipSlotButton0;
        [SerializeField] private Button _equipSlotButton1;
        // hover 상세 툴팁(인벤토리 아이템 툴팁처럼 별도 UI로 스킬 상세 노출). 정적 계층으로 baked.
        [SerializeField] private RectTransform _tooltip;
        [SerializeField] private Text _tooltipName;
        [SerializeField] private Text _tooltipHeader;
        [SerializeField] private Text _tooltipDesc;      // skill_master.description(스킬 설명)
        [SerializeField] private Text _tooltipEffect;
        [SerializeField] private Text _tooltipCooldown;

        /// <summary>hover 툴팁에 표시할 스킬 상세.</summary>
        private struct SkillTip
        {
            public string name;
            public Color nameColor;
            public string header;      // 타입 · 레벨 x/max
            public string description; // 스킬 설명(skill_master.description)
            public string effect;      // 효과 요약(계수)
            public string cooldown;    // 재사용 대기시간(패시브는 상시)
        }

        private const float CanvasRefWidth = 1080f;
        private const float CanvasRefHeight = 1920f;

        // 스킬 성격(coef_type): 1 공격 / 2 버프 / 3 디버프. 스킬 타입(skill_type): 1 액티브 / 2 패시브.
        private const int ActiveSkillType = 1;

        private Font _font;
        private RectTransform _rootRect;
        private int _selectedCharacter;      // 현재 보고 있는 파티 캐릭터(0-based)
        private int _partyCount = 1;
        private SkillIconDatabase _iconDb;
        private bool _busy;                  // 레벨업/초기화 요청 진행 중(중복 요청 방지)

        private const int MaxActiveSkills = 2; // 캐릭터당 액티브 장착 한도(기획서 5.3)

        // 런타임에 생성된 스킬 행(재오픈/갱신 시 제거 대상).
        private readonly List<GameObject> _rows = new List<GameObject>();

        // 각 장착 슬롯이 현재 담고 있는 스킬 코드(0 = 비었음). 슬롯 클릭 해제에 사용.
        private readonly int[] _equipSlotCodes = new int[MaxActiveSkills];

        /// <summary>정적 계층이 이미 구성돼 있으면 true(프리팹에서 로드된 경우).</summary>
        private bool AlreadyBuilt => _listContent != null;

        private void Awake()
        {
            _rootRect = (RectTransform)transform;
            _iconDb = SkillIconDatabase.Load();
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

        /// <summary>표시될 때마다 세션 실데이터로 스킬 목록·포인트를 갱신한다.</summary>
        private void OnEnable()
        {
            if (!AlreadyBuilt)
            {
                return;
            }
            SetMessage(string.Empty);
            RefreshFromSession();
        }

        /// <summary>에디터 빌드 전용: 전체 정적 계층을 생성하고 참조를 배선한다(프리팹 저장용).</summary>
        public void EditorConstruct()
        {
            Construct();
        }

        // ── 구성(에디터 빌드 또는 런타임 폴백) ──

        /// <summary>패널 정적 계층을 1회 생성하고 직렬화 참조를 채운다.</summary>
        private void Construct()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildCanvas();
            BuildDim();
            var container = BuildContainer();
            BuildHeader(container);
            BuildCharacterNav(container);
            BuildPointBanner(container);
            BuildEquipSlots(container);
            BuildList(container);
            BuildFooter(container);
            BuildTooltip();
        }

        /// <summary>hover 상세 툴팁(루트에 붙어 어느 행 위에서도 표시). 기본 비활성.</summary>
        private void BuildTooltip()
        {
            var bg = NewImage("SkillTooltip", _rootRect, panelBackground);
            bg.color = new Color(0.06f, 0.07f, 0.12f, 0.98f);
            bg.raycastTarget = false; // 정보 전용 → 이벤트를 가로채지 않음(행 hover 유지)
            var rt = bg.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(420f, 300f);
            _tooltip = rt;

            _tooltipName = NewText("TipName", rt, "", 30, TextAnchor.UpperLeft);
            _tooltipName.fontStyle = FontStyle.Bold;
            TopLeft(_tooltipName.rectTransform, 18f, 14f, 384f, 40f);

            _tooltipHeader = NewText("TipHeader", rt, "", 22, TextAnchor.UpperLeft);
            _tooltipHeader.color = new Color(0.75f, 0.78f, 0.88f);
            TopLeft(_tooltipHeader.rectTransform, 18f, 56f, 384f, 28f);

            // 스킬 설명(skill_master.description) — 본문
            _tooltipDesc = NewText("TipDesc", rt, "", 22, TextAnchor.UpperLeft);
            _tooltipDesc.color = new Color(0.9f, 0.92f, 0.98f);
            _tooltipDesc.horizontalOverflow = HorizontalWrapMode.Wrap;
            TopLeft(_tooltipDesc.rectTransform, 18f, 90f, 384f, 96f);

            _tooltipEffect = NewText("TipEffect", rt, "", 22, TextAnchor.UpperLeft);
            _tooltipEffect.color = new Color(0.7f, 0.9f, 0.8f);
            _tooltipEffect.horizontalOverflow = HorizontalWrapMode.Wrap;
            TopLeft(_tooltipEffect.rectTransform, 18f, 190f, 384f, 52f);

            _tooltipCooldown = NewText("TipCooldown", rt, "", 22, TextAnchor.UpperLeft);
            _tooltipCooldown.color = new Color(0.72f, 0.76f, 0.6f);
            TopLeft(_tooltipCooldown.rectTransform, 18f, 246f, 384f, 28f);

            bg.gameObject.SetActive(false);
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
            canvas.sortingOrder = 110; // 인벤토리 패널(100)보다 위(인벤토리에서 진입하므로)

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
            img.color = new Color(0f, 0f, 0f, 0f); // 배경을 어둡게 하지 않는다 — 밖 클릭 닫기용 투명 차단막(레이캐스트만 유지)
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
            rt.sizeDelta = new Vector2(860f, 1020f); // 목록 뷰포트를 넓혀 스킬 2.5개가 기본 노출되도록 확장
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        /// <summary>제목(가로 중앙) + 닫기 버튼(우측 상단).</summary>
        private void BuildHeader(RectTransform container)
        {
            var title = NewText("Title", container, "스킬 레벨업", 46, TextAnchor.UpperCenter);
            title.fontStyle = FontStyle.Bold;
            var trt = title.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 1f);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.anchoredPosition = new Vector2(0f, -36f);
            trt.sizeDelta = new Vector2(500f, 60f);

            var closeImg = NewImage("CloseButton", container, slotNormal);
            var crt = closeImg.rectTransform;
            crt.anchorMin = crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(1f, 1f);
            crt.anchoredPosition = new Vector2(-28f, -28f);
            crt.sizeDelta = new Vector2(72f, 72f);
            var x = NewText("X", crt, "X", 36, TextAnchor.MiddleCenter);
            Stretch(x.rectTransform);
            _closeButton = closeImg.gameObject.AddComponent<Button>();
        }

        /// <summary>캐릭터 전환 네비게이션(◀ 직업 Lv.N · 캐릭터 i/N ▶).</summary>
        private void BuildCharacterNav(RectTransform container)
        {
            const float width = 760f;
            var area = NewRect("CharNav", container);
            area.anchorMin = area.anchorMax = new Vector2(0.5f, 1f);
            area.pivot = new Vector2(0.5f, 1f);
            area.sizeDelta = new Vector2(width, 72f);
            area.anchoredPosition = new Vector2(0f, -120f);

            var prev = NewImage("PrevCharButton", area, slotNormal);
            TopLeft(prev.rectTransform, 0f, 0f, 72f, 72f);
            Stretch(NewText("PrevLabel", prev.rectTransform, "<", 40, TextAnchor.MiddleCenter).rectTransform);
            _prevButton = prev.gameObject.AddComponent<Button>();

            _charIndicatorText = NewText("CharIndicator", area, "", 32, TextAnchor.MiddleCenter);
            _charIndicatorText.fontStyle = FontStyle.Bold;
            TopLeft(_charIndicatorText.rectTransform, 84f, 0f, width - 168f, 72f);

            var next = NewImage("NextCharButton", area, slotNormal);
            TopLeft(next.rectTransform, width - 72f, 0f, 72f, 72f);
            Stretch(NewText("NextLabel", next.rectTransform, ">", 40, TextAnchor.MiddleCenter).rectTransform);
            _nextButton = next.gameObject.AddComponent<Button>();
        }

        /// <summary>사용 가능 스킬 포인트 배너(캐릭터 레벨에서 파생).</summary>
        private void BuildPointBanner(RectTransform container)
        {
            const float width = 760f;
            var bg = NewImage("PointBanner", container, null);
            bg.color = new Color(0.12f, 0.10f, 0.06f, 0.95f);
            var rt = bg.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(width, 76f);
            rt.anchoredPosition = new Vector2(0f, -208f);

            _pointText = NewText("PointText", bg.rectTransform, "스킬 포인트  0 / 0", 34, TextAnchor.MiddleCenter);
            _pointText.fontStyle = FontStyle.Bold;
            _pointText.color = new Color(1f, 0.86f, 0.35f);
            Stretch(_pointText.rectTransform);
        }

        /// <summary>액티브 스킬 장착 슬롯 영역(최대 2). 라벨 + 슬롯 2개(아이콘·이름·클릭 해제).</summary>
        private void BuildEquipSlots(RectTransform container)
        {
            const float width = 760f;
            var area = NewRect("EquipSlots", container);
            area.anchorMin = area.anchorMax = new Vector2(0.5f, 1f);
            area.pivot = new Vector2(0.5f, 1f);
            area.sizeDelta = new Vector2(width, 150f);
            area.anchoredPosition = new Vector2(0f, -296f);

            var label = NewText("EquipTitle", area, "장착 액티브 스킬 (최대 2)", 26, TextAnchor.UpperLeft);
            label.color = new Color(0.8f, 0.85f, 0.95f);
            TopLeft(label.rectTransform, 4f, 0f, 500f, 32f);

            BuildEquipSlot(area, 0, 40f);
            BuildEquipSlot(area, 1, 40f + 210f);
        }

        /// <summary>장착 슬롯 1칸(아이콘 타일 + 이름 + 클릭 시 해제).</summary>
        private void BuildEquipSlot(RectTransform area, int index, float x)
        {
            var slotBg = NewImage($"EquipSlot{index}", area, slotNormal);
            slotBg.color = new Color(0.16f, 0.17f, 0.24f, 0.98f);
            TopLeft(slotBg.rectTransform, x, 40f, 96f, 96f);
            var icon = NewImage("Icon", slotBg.rectTransform, null);
            icon.raycastTarget = false;
            icon.preserveAspect = true;
            Stretch(icon.rectTransform);
            icon.rectTransform.offsetMin = new Vector2(8f, 8f);
            icon.rectTransform.offsetMax = new Vector2(-8f, -8f);
            icon.color = new Color(1f, 1f, 1f, 0f);

            var nameLabel = NewText("Name", area, "비었음", 22, TextAnchor.UpperLeft);
            nameLabel.color = new Color(0.7f, 0.72f, 0.8f);
            TopLeft(nameLabel.rectTransform, x + 106f, 44f, 104f, 88f);

            var btn = slotBg.gameObject.AddComponent<Button>();
            if (index == 0) { _equipSlotIcon0 = icon; _equipSlotLabel0 = nameLabel; _equipSlotButton0 = btn; }
            else { _equipSlotIcon1 = icon; _equipSlotLabel1 = nameLabel; _equipSlotButton1 = btn; }
        }

        private const float ScrollbarWidth = 18f;

        /// <summary>스킬 목록 스크롤 뷰(런타임에 행이 채워짐) + 우측 세로 스크롤바(항상 표시).
        /// 패널 축소로 목록 영역이 짧아졌으므로 넘치는 스킬은 스크롤해서 본다.</summary>
        private void BuildList(RectTransform container)
        {
            const float width = 760f;
            const float height = 372f; // 행 높이(132)+간격(12) 기준 약 2.5개가 보이는 뷰포트(넘치면 스크롤)
            const float viewW = width - ScrollbarWidth - 8f; // 스크롤바 폭·간격 제외

            var area = NewRect("ListArea", container);
            area.anchorMin = area.anchorMax = new Vector2(0.5f, 1f);
            area.pivot = new Vector2(0.5f, 1f);
            area.sizeDelta = new Vector2(width, height);
            area.anchoredPosition = new Vector2(0f, -452f);

            // 스크롤 뷰(좌측, 스크롤바 폭만큼 좁힘)
            var scrollGo = NewImage("SkillScroll", area, null);
            scrollGo.color = new Color(0.06f, 0.07f, 0.12f, 0.6f);
            TopLeft(scrollGo.rectTransform, 0f, 0f, viewW, height);
            var scroll = scrollGo.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;

            var viewport = NewImage("Viewport", scrollGo.rectTransform, null);
            viewport.color = new Color(0f, 0f, 0f, 0.001f);
            Stretch(viewport.rectTransform);
            viewport.gameObject.AddComponent<RectMask2D>();
            scroll.viewport = viewport.rectTransform;

            _listContent = NewRect("Content", viewport.rectTransform);
            _listContent.anchorMin = new Vector2(0f, 1f);
            _listContent.anchorMax = new Vector2(1f, 1f);
            _listContent.pivot = new Vector2(0.5f, 1f);
            _listContent.anchoredPosition = Vector2.zero;
            _listContent.sizeDelta = Vector2.zero;

            var layout = _listContent.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 12f;
            layout.padding = new RectOffset(12, 12, 12, 12);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var fitter = _listContent.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = _listContent;

            // 우측 세로 스크롤바(항상 표시)
            var bar = BuildScrollbar(area, viewW + 8f, 0f, ScrollbarWidth, height);
            scroll.verticalScrollbar = bar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        }

        /// <summary>세로 스크롤바(배경+핸들)를 생성해 ScrollRect에 연결할 컴포넌트를 반환한다.</summary>
        private Scrollbar BuildScrollbar(RectTransform container, float x, float y, float w, float h)
        {
            var barBg = NewImage("SkillScrollbar", container, null);
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

        /// <summary>하단: 결과 안내 텍스트 + 스킬 초기화(무료) 버튼.</summary>
        private void BuildFooter(RectTransform container)
        {
            _messageText = NewText("Message", container, "", 26, TextAnchor.MiddleCenter);
            var mrt = _messageText.rectTransform;
            mrt.anchorMin = mrt.anchorMax = new Vector2(0.5f, 0f);
            mrt.pivot = new Vector2(0.5f, 0f);
            mrt.sizeDelta = new Vector2(760f, 40f);
            mrt.anchoredPosition = new Vector2(0f, 130f);

            var resetImg = NewImage("ResetButton", container, slotNormal);
            resetImg.color = new Color(0.35f, 0.20f, 0.22f, 0.98f);
            var rrt = resetImg.rectTransform;
            rrt.anchorMin = rrt.anchorMax = new Vector2(0.5f, 0f);
            rrt.pivot = new Vector2(0.5f, 0f);
            rrt.sizeDelta = new Vector2(360f, 80f);
            rrt.anchoredPosition = new Vector2(0f, 40f);
            _resetLabel = NewText("ResetLabel", resetImg.rectTransform, "스킬 초기화 (무료)", 30, TextAnchor.MiddleCenter);
            Stretch(_resetLabel.rectTransform);
            _resetButton = resetImg.gameObject.AddComponent<Button>();
        }

        /// <summary>런타임 배선: 버튼 리스너 등록.</summary>
        private void WireRuntime()
        {
            if (_prevButton != null) _prevButton.onClick.AddListener(OnPrevCharacter);
            if (_nextButton != null) _nextButton.onClick.AddListener(OnNextCharacter);
            if (_closeButton != null) _closeButton.onClick.AddListener(Close);
            if (_dimButton != null) _dimButton.onClick.AddListener(Close);
            if (_resetButton != null) _resetButton.onClick.AddListener(OnResetSkills);
            if (_equipSlotButton0 != null) _equipSlotButton0.onClick.AddListener(() => OnUnequipSlot(0));
            if (_equipSlotButton1 != null) _equipSlotButton1.onClick.AddListener(() => OnUnequipSlot(1));
        }

        // ── 세션 실데이터 연동 ──

        /// <summary>세션의 파티 캐릭터 목록(없으면 null).</summary>
        private List<CharacterDto> Characters =>
            Session.GameData != null ? Session.GameData.characters : null;

        /// <summary>선택된 캐릭터 표시·스킬 목록·포인트를 세션 실데이터로 모두 갱신한다.</summary>
        private void RefreshFromSession()
        {
            MasterDataManager.EnsureLoaded();
            if (_iconDb == null)
            {
                _iconDb = SkillIconDatabase.Load();
            }

            var chars = Characters;
            _partyCount = chars != null && chars.Count > 0 ? chars.Count : 1;
            _selectedCharacter = Mathf.Clamp(_selectedCharacter, 0, _partyCount - 1);

            RefreshHeader(chars);
            RefreshEquipSlots(chars);
            RefreshSkillList(chars);
        }

        /// <summary>현재 캐릭터의 장착 액티브 스킬(equipped=1)을 2칸 슬롯에 반영한다(없으면 '비었음').</summary>
        private void RefreshEquipSlots(List<CharacterDto> chars)
        {
            var equipped = EquippedActiveCodes(CurrentCharacter(chars));
            for (int i = 0; i < MaxActiveSkills; i++)
            {
                int code = i < equipped.Count ? equipped[i] : 0;
                _equipSlotCodes[i] = code;
                var icon = i == 0 ? _equipSlotIcon0 : _equipSlotIcon1;
                var label = i == 0 ? _equipSlotLabel0 : _equipSlotLabel1;
                if (code != 0)
                {
                    var sp = _iconDb != null ? _iconDb.Get(code) : null;
                    if (icon != null)
                    {
                        icon.sprite = sp;
                        icon.color = sp != null ? Color.white : new Color(1f, 1f, 1f, 0f);
                    }
                    if (label != null)
                    {
                        label.text = SkillName(code);
                        label.color = Color.white;
                    }
                }
                else
                {
                    if (icon != null) icon.color = new Color(1f, 1f, 1f, 0f);
                    if (label != null)
                    {
                        label.text = "비었음";
                        label.color = new Color(0.7f, 0.72f, 0.8f);
                    }
                }
            }
        }

        /// <summary>선택 캐릭터의 인디케이터(직업·레벨·슬롯)와 스킬 포인트 배너를 갱신한다.</summary>
        private void RefreshHeader(List<CharacterDto> chars)
        {
            CharacterDto cur = CurrentCharacter(chars);
            var db = MasterDataManager.Db;

            if (_charIndicatorText != null)
            {
                if (cur == null)
                {
                    _charIndicatorText.text = "캐릭터 없음";
                }
                else
                {
                    string cls = db != null && db.Classes.TryGetValue(cur.classCode, out var cm)
                        ? cm.name : $"직업 {cur.classCode}";
                    _charIndicatorText.text = $"◀  {cls} Lv.{cur.level}  ·  {_selectedCharacter + 1} / {_partyCount}  ▶";
                }
            }

            if (_pointText != null)
            {
                int total = TotalSkillPoints(cur);
                int spent = SpentSkillPoints(cur);
                int available = Mathf.Max(0, total - spent);
                _pointText.text = $"스킬 포인트  {available} / {total}";
            }
        }

        /// <summary>선택 캐릭터의 직업 소속 스킬을 목록에 (재)생성한다.</summary>
        private void RefreshSkillList(List<CharacterDto> chars)
        {
            ClearRows();

            CharacterDto cur = CurrentCharacter(chars);
            var db = MasterDataManager.Db;
            if (cur == null || db == null)
            {
                return;
            }

            int total = TotalSkillPoints(cur);
            int available = Mathf.Max(0, total - SpentSkillPoints(cur));
            var levels = SkillLevelsOf(cur.characterId);

            // 직업 소속 스킬을 스킬 코드 순으로 정렬(액티브 먼저 노출되도록 skill_type→code 정렬).
            var skills = new List<SkillMaster>();
            foreach (var s in db.Skills.Values)
            {
                if (s.classCode == cur.classCode)
                {
                    skills.Add(s);
                }
            }
            skills.Sort((a, b) =>
            {
                int t = a.skillType.CompareTo(b.skillType);
                return t != 0 ? t : a.skillCode.CompareTo(b.skillCode);
            });

            var equippedSet = new HashSet<int>(EquippedActiveCodes(cur));
            foreach (var s in skills)
            {
                int level = levels.TryGetValue(s.skillCode, out var lv) ? lv : 0;
                bool canLevelUp = level < s.maxLevel && available >= 1;
                CreateSkillRow(cur.characterId, s, level, canLevelUp, equippedSet.Contains(s.skillCode), equippedSet.Count);
            }
        }

        /// <summary>스킬 1개의 행(아이콘+이름/타입/효과+레벨업 버튼, 액티브는 장착/해제 버튼)을 목록에 생성한다.</summary>
        private void CreateSkillRow(int characterId, SkillMaster skill, int level, bool canLevelUp, bool isEquipped, int equippedCount)
        {
            var rowImg = NewImage($"Skill_{skill.skillCode}", _listContent, slotNormal);
            rowImg.color = new Color(0.10f, 0.12f, 0.18f, 0.95f);
            var le = rowImg.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 132f;
            le.minHeight = 132f;
            var row = rowImg.rectTransform;
            _rows.Add(rowImg.gameObject);

            // 아이콘(좌측). 상세 정보는 hover 툴팁으로 노출하므로 행에는 아이콘+이름만 둔다.
            var iconBg = NewImage("IconBg", row, slotNormal);
            TopLeft(iconBg.rectTransform, 12f, 12f, 108f, 108f);
            var icon = NewImage("Icon", iconBg.rectTransform, _iconDb != null ? _iconDb.Get(skill.skillCode) : null);
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            if (icon.sprite == null)
            {
                icon.color = new Color(1f, 1f, 1f, 0f); // 아이콘 없으면 투명
            }
            var irt = icon.rectTransform;
            irt.anchorMin = Vector2.zero;
            irt.anchorMax = Vector2.one;
            irt.offsetMin = new Vector2(8f, 8f);
            irt.offsetMax = new Vector2(-8f, -8f);

            // 이름만 노출(세로 중앙). 타입·레벨·효과·재사용은 hover 툴팁에서.
            bool isActive = skill.skillType == ActiveSkillType;
            var name = NewText("Name", row, skill.name, 32, TextAnchor.MiddleLeft);
            name.fontStyle = FontStyle.Bold;
            TopLeft(name.rectTransform, 138f, 46f, 260f, 40f);

            // hover 상세 툴팁 데이터.
            string typeName = isActive ? (isEquipped ? "액티브 · 장착됨" : "액티브") : "패시브";
            var tip = new SkillTip
            {
                name = skill.name,
                nameColor = isActive ? new Color(1f, 0.85f, 0.5f) : new Color(0.6f, 0.85f, 1f),
                header = $"{typeName}   ·   레벨 {level} / {skill.maxLevel}",
                description = skill.description,
                effect = EffectSummary(skill, level),
                cooldown = isActive ? $"재사용 대기시간 {skill.cooldown:0.#}초" : "상시 적용(패시브)",
            };
            AddHoverTooltip(rowImg.gameObject, tip);

            // 액티브 스킬: 장착/해제 토글 버튼(레벨업 버튼 왼쪽).
            if (isActive)
            {
                CreateEquipButton(row, characterId, skill.skillCode, level >= 1, isEquipped, equippedCount);
            }

            // 레벨업 버튼(우측)
            var btnImg = NewImage("LevelUpButton", row, slotNormal);
            var btnRt = btnImg.rectTransform;
            btnRt.anchorMin = btnRt.anchorMax = new Vector2(1f, 0.5f);
            btnRt.pivot = new Vector2(1f, 0.5f);
            btnRt.anchoredPosition = new Vector2(-16f, 0f);
            btnRt.sizeDelta = new Vector2(150f, 96f);

            string btnLabel;
            if (level >= skill.maxLevel)
            {
                btnLabel = "MAX";
                btnImg.color = new Color(0.28f, 0.26f, 0.18f, 0.9f);
            }
            else if (canLevelUp)
            {
                btnLabel = "레벨업\nSP 1";
                btnImg.color = new Color(0.22f, 0.40f, 0.28f, 0.98f);
            }
            else
            {
                btnLabel = "레벨업\nSP 1";
                btnImg.color = new Color(0.20f, 0.22f, 0.30f, 0.9f); // 포인트 부족(비활성 느낌)
            }
            var btnText = NewText("Label", btnImg.rectTransform, btnLabel, 26, TextAnchor.MiddleCenter);
            btnText.fontStyle = FontStyle.Bold;
            Stretch(btnText.rectTransform);

            var button = btnImg.gameObject.AddComponent<Button>();
            button.interactable = canLevelUp;
            int code = skill.skillCode;
            button.onClick.AddListener(() => OnLevelUp(characterId, code));
        }

        /// <summary>액티브 스킬 행의 장착/해제 토글 버튼을 만든다(레벨업 버튼 왼쪽). 미습득은 비활성.</summary>
        private void CreateEquipButton(RectTransform row, int characterId, int skillCode, bool learned, bool isEquipped, int equippedCount)
        {
            var img = NewImage("EquipButton", row, slotNormal);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(-176f, 0f);
            rt.sizeDelta = new Vector2(150f, 96f);

            string label;
            bool interactable;
            if (!learned)
            {
                label = "미습득";
                img.color = new Color(0.20f, 0.22f, 0.30f, 0.9f);
                interactable = false;
            }
            else if (isEquipped)
            {
                label = "해제";
                img.color = new Color(0.45f, 0.28f, 0.22f, 0.98f);
                interactable = true;
            }
            else
            {
                label = "장착";
                img.color = new Color(0.22f, 0.32f, 0.48f, 0.98f);
                interactable = true;
            }
            var text = NewText("Label", img.rectTransform, label, 28, TextAnchor.MiddleCenter);
            text.fontStyle = FontStyle.Bold;
            Stretch(text.rectTransform);

            var btn = img.gameObject.AddComponent<Button>();
            btn.interactable = interactable;
            btn.onClick.AddListener(() => OnToggleEquip(characterId, skillCode, isEquipped, equippedCount));
        }

        /// <summary>행에 hover 진입/이탈 시 상세 툴팁을 표시/숨김하는 중계기를 부착한다(정보 전용).
        /// EventTrigger가 아니라 <see cref="PointerHoverRelay"/>를 쓰는 이유는, EventTrigger가 드래그
        /// 이벤트까지 구현해 행을 잡고 끌 때 부모 ScrollRect로 드래그가 전달되지 않기 때문이다
        /// (= 스킬 슬롯을 누른 채로는 목록이 스크롤되지 않던 문제).</summary>
        private void AddHoverTooltip(GameObject rowGo, SkillTip tip)
        {
            var relay = rowGo.AddComponent<PointerHoverRelay>();
            relay.Bind(e => ShowSkillTooltip(tip, e.position), HideSkillTooltip);
        }

        /// <summary>스킬 상세 툴팁을 채우고 커서 근처에 표시한다.</summary>
        private void ShowSkillTooltip(SkillTip tip, Vector2 screenPos)
        {
            if (_tooltip == null)
            {
                return;
            }
            _tooltip.gameObject.SetActive(true);
            _tooltip.SetAsLastSibling();
            if (_tooltipName != null) { _tooltipName.text = tip.name; _tooltipName.color = tip.nameColor; }
            if (_tooltipHeader != null) _tooltipHeader.text = tip.header;
            if (_tooltipDesc != null) _tooltipDesc.text = tip.description;
            if (_tooltipEffect != null) _tooltipEffect.text = tip.effect;
            if (_tooltipCooldown != null) _tooltipCooldown.text = tip.cooldown;
            RepositionTooltip(screenPos);
        }

        /// <summary>툴팁을 숨긴다.</summary>
        private void HideSkillTooltip()
        {
            if (_tooltip != null)
            {
                _tooltip.gameObject.SetActive(false);
            }
        }

        /// <summary>커서 스크린 좌표를 루트 로컬로 변환해 배치하고 화면 안으로 클램프.</summary>
        private void RepositionTooltip(Vector2 screenPos)
        {
            if (_tooltip == null || _rootRect == null)
            {
                return;
            }
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_rootRect, screenPos, null, out Vector2 local);
            local += new Vector2(18f, -18f);
            var size = _tooltip.sizeDelta;
            float halfW = _rootRect.rect.width * 0.5f;
            float halfH = _rootRect.rect.height * 0.5f;
            local.x = Mathf.Clamp(local.x, -halfW, halfW - size.x);
            local.y = Mathf.Clamp(local.y, -halfH + size.y, halfH);
            _tooltip.anchoredPosition = local;
        }

        /// <summary>스킬 레벨의 효과 요약 문자열(현재 레벨 계수 기준, 미습득은 안내).</summary>
        private static string EffectSummary(SkillMaster skill, int level)
        {
            if (skill.coefs == null || skill.coefs.Length == 0)
            {
                return level > 0 ? "습득" : "미습득";
            }
            int refLevel = level > 0 ? level : 1;
            SkillCoef? found = null;
            foreach (var c in skill.coefs)
            {
                if (c.skillLevel == refLevel)
                {
                    found = c;
                    break;
                }
            }
            if (found == null)
            {
                return level > 0 ? "습득" : "미습득";
            }

            var coef = found.Value;
            string prefix = level > 0 ? string.Empty : "(Lv.1) ";
            switch (coef.coefType)
            {
                case 1: // 공격
                    return $"{prefix}데미지 배율 x{coef.coef:0.##}";
                case 2: // 버프
                    return $"{prefix}버프 x{coef.coef:0.##} ({coef.duration:0.#}초)";
                case 3: // 디버프
                    return $"{prefix}디버프 x{coef.coef:0.##} ({coef.duration:0.#}초)";
                default:
                    return level > 0 ? "습득" : "미습득";
            }
        }

        // ── 파생 계산(스킬 포인트) ──

        /// <summary>현재 선택 캐릭터(없으면 null).</summary>
        private CharacterDto CurrentCharacter(List<CharacterDto> chars)
            => chars != null && _selectedCharacter < chars.Count ? chars[_selectedCharacter] : null;

        /// <summary>캐릭터 레벨 비례 스킬 포인트 총량(level_master.skillPoints, 파생 근거). 없으면 0.</summary>
        private static int TotalSkillPoints(CharacterDto cur)
        {
            if (cur == null)
            {
                return 0;
            }
            var db = MasterDataManager.Db;
            return db != null && db.Levels.TryGetValue(cur.level, out var lm) ? lm.skillPoints : 0;
        }

        /// <summary>해당 캐릭터가 이미 투자한 스킬 포인트(= 보유 스킬 레벨의 합, 1레벨당 1포인트).</summary>
        private static int SpentSkillPoints(CharacterDto cur)
        {
            if (cur == null || Session.GameData == null || Session.GameData.skills == null)
            {
                return 0;
            }
            int spent = 0;
            foreach (var s in Session.GameData.skills)
            {
                if (s != null && s.characterId == cur.characterId)
                {
                    spent += s.level;
                }
            }
            return spent;
        }

        /// <summary>해당 캐릭터의 스킬 코드→레벨 맵(세션 스냅샷 기준).</summary>
        private static Dictionary<int, int> SkillLevelsOf(int characterId)
        {
            var map = new Dictionary<int, int>();
            var skills = Session.GameData != null ? Session.GameData.skills : null;
            if (skills != null)
            {
                foreach (var s in skills)
                {
                    if (s != null && s.characterId == characterId)
                    {
                        map[s.skillCode] = s.level;
                    }
                }
            }
            return map;
        }

        /// <summary>해당 캐릭터의 장착(equipped=1) 액티브 스킬 코드 목록(스킬 코드 순, 최대 2).</summary>
        private static List<int> EquippedActiveCodes(CharacterDto cur)
        {
            var list = new List<int>();
            if (cur == null || Session.GameData == null || Session.GameData.skills == null)
            {
                return list;
            }
            foreach (var s in Session.GameData.skills)
            {
                if (s != null && s.characterId == cur.characterId && s.equipped == 1)
                {
                    list.Add(s.skillCode);
                }
            }
            list.Sort();
            return list;
        }

        /// <summary>스킬 코드의 이름(마스터). 없으면 코드 표기.</summary>
        private static string SkillName(int skillCode)
        {
            var db = MasterDataManager.Db;
            return db != null && db.Skills.TryGetValue(skillCode, out var s) ? s.name : $"스킬 {skillCode}";
        }

        // ── 캐릭터 전환 ──

        /// <summary>이전 파티 캐릭터로 전환(순환).</summary>
        private void OnPrevCharacter()
        {
            if (_partyCount <= 1)
            {
                return;
            }
            _selectedCharacter = (_selectedCharacter - 1 + _partyCount) % _partyCount;
            SetMessage(string.Empty);
            RefreshFromSession();
        }

        /// <summary>다음 파티 캐릭터로 전환(순환).</summary>
        private void OnNextCharacter()
        {
            if (_partyCount <= 1)
            {
                return;
            }
            _selectedCharacter = (_selectedCharacter + 1) % _partyCount;
            SetMessage(string.Empty);
            RefreshFromSession();
        }

        // ── 서버 연동(레벨업 / 초기화) ──

        /// <summary>지정 캐릭터의 스킬 레벨업 요청(포인트 소모). 성공 시 재로드·갱신.
        /// (POST /api/game/growth/skill/levelup)</summary>
        private void OnLevelUp(int characterId, int skillCode)
        {
            if (_busy || NetworkManager.Instance == null || !Session.IsLoggedIn)
            {
                return;
            }
            _busy = true;
            var req = new SkillLevelUpRequest
            {
                userId = Session.UserId,
                token = Session.Token,
                data = new SkillLevelUpData { characterId = characterId, skillCode = skillCode },
            };
            Debug.Log($"[Skill] 레벨업 요청 char={characterId} skill={skillCode}");
            NetworkManager.Instance.PostToGame<ApiResponse>(
                "/api/game/growth/skill/levelup", req,
                _ =>
                {
                    SoundManager.Sfx(SoundId.UpgradeSuccess); // 스킬 레벨업 성공음(사운드 정의서 §6)
                    ReloadAndRefresh("스킬 레벨업 완료");
                },
                OnActionError);
        }

        /// <summary>지정 캐릭터의 모든 스킬을 초기화(무료, 포인트 전량 회수). 성공 시 재로드·갱신.
        /// (POST /api/game/growth/skill/reset)</summary>
        private void OnResetSkills()
        {
            if (_busy || NetworkManager.Instance == null || !Session.IsLoggedIn)
            {
                return;
            }
            var chars = Characters;
            var cur = CurrentCharacter(chars);
            if (cur == null)
            {
                return;
            }
            _busy = true;
            var req = new SkillResetRequest
            {
                userId = Session.UserId,
                token = Session.Token,
                data = new SkillResetData { characterId = cur.characterId },
            };
            Debug.Log($"[Skill] 초기화 요청 char={cur.characterId}");
            NetworkManager.Instance.PostToGame<ApiResponse>(
                "/api/game/growth/skill/reset", req,
                _ => ReloadAndRefresh("스킬을 초기화했습니다"),
                OnActionError);
        }

        /// <summary>액티브 스킬 장착/해제 토글. 현재 장착 목록에서 대상 스킬을 넣거나 빼서 서버에 통째로 반영한다.
        /// 장착 시도 시 이미 2개면(한도) 클라에서 막고 안내한다(서버도 5007로 재검증).</summary>
        private void OnToggleEquip(int characterId, int skillCode, bool isEquipped, int equippedCount)
        {
            if (_busy)
            {
                return;
            }
            var cur = CurrentCharacter(Characters);
            var codes = cur != null ? EquippedActiveCodes(cur) : new List<int>();
            if (isEquipped)
            {
                codes.Remove(skillCode);
            }
            else
            {
                if (codes.Count >= MaxActiveSkills)
                {
                    SetMessage($"액티브 스킬은 최대 {MaxActiveSkills}개까지 장착할 수 있습니다.");
                    return;
                }
                if (!codes.Contains(skillCode))
                {
                    codes.Add(skillCode);
                }
            }
            OnSetEquip(characterId, codes);
        }

        /// <summary>장착 슬롯(0/1) 클릭 시 그 슬롯의 액티브 스킬을 해제한다(비어 있으면 무시).</summary>
        private void OnUnequipSlot(int index)
        {
            if (_busy || index < 0 || index >= _equipSlotCodes.Length)
            {
                return;
            }
            int code = _equipSlotCodes[index];
            if (code == 0)
            {
                return;
            }
            var cur = CurrentCharacter(Characters);
            if (cur == null)
            {
                return;
            }
            var codes = EquippedActiveCodes(cur);
            codes.Remove(code);
            OnSetEquip(cur.characterId, codes);
        }

        /// <summary>액티브 장착 목록(0~2개)을 서버에 통째로 반영 요청한다. 성공 시 재로드·갱신.
        /// (POST /api/game/growth/skill/equip)</summary>
        private void OnSetEquip(int characterId, List<int> skillCodes)
        {
            if (_busy || NetworkManager.Instance == null || !Session.IsLoggedIn)
            {
                return;
            }
            _busy = true;
            var req = new SkillEquipRequest
            {
                userId = Session.UserId,
                token = Session.Token,
                data = new SkillEquipData { characterId = characterId, skillCodes = skillCodes },
            };
            Debug.Log($"[Skill] 액티브 장착 요청 char={characterId} codes=[{string.Join(",", skillCodes)}]");
            NetworkManager.Instance.PostToGame<ApiResponse>(
                "/api/game/growth/skill/equip", req,
                _ => ReloadAndRefresh("액티브 스킬 장착을 변경했습니다"),
                OnActionError);
        }

        /// <summary>레벨업/초기화 후 세이브 스냅샷을 재로드해 세션·UI·전투를 최신화한다.</summary>
        private void ReloadAndRefresh(string message)
        {
            var req = new AuthRequest { userId = Session.UserId, token = Session.Token };
            NetworkManager.Instance.PostToGame<LoadResponse>("/api/game/load", req, resp =>
            {
                if (resp != null && resp.data != null)
                {
                    Session.SetGameData(resp.data);
                }
                _busy = false;
                RefreshFromSession();
                SetMessage(message);
                Session.RaiseInventoryChanged(); // 스킬 변경 → 전투 스탯/스킬 재계산 트리거
            }, OnActionError);
        }

        /// <summary>레벨업/초기화 실패를 안내한다(에러 코드 → 한글 메시지, ErrorMessages 공용).</summary>
        private void OnActionError(NetworkError error)
        {
            _busy = false;
            Debug.LogWarning($"[Skill] 액션 실패: {error}");
            SetMessage(ErrorMessages.ToKorean(error));
        }

        /// <summary>하단 안내 문구를 설정한다(빈 문자열이면 숨김 효과).</summary>
        private void SetMessage(string message)
        {
            if (_messageText != null)
            {
                _messageText.text = message ?? string.Empty;
            }
        }

        // ── 행 정리 / 닫기 ──

        /// <summary>런타임에 생성한 스킬 행을 모두 제거한다.</summary>
        private void ClearRows()
        {
            HideSkillTooltip(); // 재구성 전 stale 툴팁 숨김
            foreach (var go in _rows)
            {
                if (go != null)
                {
                    Destroy(go);
                }
            }
            _rows.Clear();
        }

        /// <summary>패널을 닫는다(UIManager 우선, 없으면 자체 비활성).</summary>
        public void Close()
        {
            if (UIManager.Instance != null)
            {
                UIManager.Instance.Hide(UIManager.PanelType.Skill);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }

        // ── UI 생성 헬퍼(InventoryPanelController와 동일 규약) ──

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
