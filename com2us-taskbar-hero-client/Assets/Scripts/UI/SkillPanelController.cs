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
    /// 계층은 에디터 빌드 시 정적 부분(캐릭터 네비·스킬포인트 배너·장착 슬롯·액티브/패시브 두 목록·초기화 버튼)이
    /// 생성돼 프리팹에 저장되고, 표시될 때마다 세션 세이브(<see cref="Session.GameData"/>)의 실데이터로
    /// 스킬 목록·레벨·사용 가능 스킬 포인트를 채운다. 스킬 포인트는 저장값이 아니라 캐릭터 레벨에서 파생한다
    /// (사용 가능 = 레벨 비례 총량 − 그 캐릭터가 이미 투자한 스킬 레벨 합). 서버가 최종 확정한다(서버 권위).
    /// <para>창은 <see cref="PanelDragMove"/>로 <b>끌어 옮길 수 있고</b>(가방·큐브와 동일), 스킬 목록은
    /// <b>액티브(위) · 패시브(아래)</b> 두 영역으로 나뉘어 각각 자기 머리글·색·스크롤을 가진다.</para>
    /// 기획서: docs/세부/growth-기획서.md §2·§5.1·§5.2
    /// </summary>
    public class SkillPanelController : MonoBehaviour
    {
        [Header("UI 리소스 (Assets/Art/UI/Inventory 공용)")]
        [SerializeField] private Sprite panelBackground; // ui_bg_2(인벤토리·룬 패널과 공용 프레임)
        [SerializeField] private Sprite slotNormal;      // ui_slot_normal
        [SerializeField] private Sprite slotHighlight;   // ui_slot_highlight
        [Tooltip("캐릭터 전환 버튼 아트(Assets/Art/UI/화살표버튼.png). 오른쪽을 가리키는 그림이라 이전 버튼은 좌우 반전해 쓴다. 없으면 슬롯 배경 + '<'/'>' 글자로 폴백.")]
        [SerializeField] private Sprite charNavArrow;    // 화살표버튼
        [Tooltip("하단 '스킬 초기화' 버튼 아트(Assets/Art/UI/pixel_rpg_button.png, 9-slice). 없으면 슬롯 배경으로 폴백.")]
        [SerializeField] private Sprite buttonSprite;    // pixel_rpg_button

        [Header("구성 참조 (에디터 빌더가 배선 — 직접 수정 불필요)")]
        [SerializeField] private Text _charIndicatorText;   // 직업 Lv.N · 캐릭터 i/N
        [SerializeField] private Text _pointText;           // 스킬 포인트 available / total
        [SerializeField] private Button _prevButton;
        [SerializeField] private Button _nextButton;
        [SerializeField] private Button _dimButton;
        [SerializeField] private Button _resetButton;
        [SerializeField] private Text _resetLabel;
        [SerializeField] private Text _messageText;         // 결과/오류 안내(하단)
        [SerializeField] private RectTransform _listContent;        // 액티브 스킬 행 부모(위쪽 스크롤 콘텐츠)
        [SerializeField] private RectTransform _passiveListContent; // 패시브 스킬 행 부모(아래쪽 스크롤 콘텐츠)
        // 액티브 스킬 장착 슬롯(최대 2). 아이콘/버튼 참조(에디터 빌더가 배선).
        // 스킬 이름 라벨은 두지 않는다 — 타일 2개를 붙여 좁게 두는 배치라 이름이 들어갈 자리가 없다.
        [SerializeField] private Image _equipSlotIcon0;
        [SerializeField] private Image _equipSlotIcon1;
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
            var panel = BuildContainer();
            // 내용물은 모두 프레임 안쪽 빈 칸(ContentArea)에만 놓는다 — 테두리·상단 장식판을 침범하지 않게 한다.
            // 제목 텍스트("스킬 레벨업")는 두지 않는다 — 배경 아트(ui_bg_2)의 상단 장식판이 제목 자리를 그린다.
            var content = PanelFrame.CreateContentArea(panel);
            BuildCharacterNav(content);
            BuildTopRow(content);
            BuildSkillSections(content);
            BuildFooter(content);
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

        // 패널 본체 배치. 사용자가 에디터/플레이모드에서 맞춘 값이며, 빌더(SkillUiBuilder)가
        // 프리팹 재생성 시 기존 프리팹의 앵커·pivot·크기·위치를 보존하므로 여기 값은 최초 생성 기본값이다.
        // 화면 <b>오른쪽에 붙는 배치</b>라 앵커·pivot을 모두 오른쪽(x=1)에 두고 오프셋으로 표현한다
        // (중앙 앵커 + 큰 x 오프셋으로 두면 캔버스 폭이 바뀔 때 위치가 어긋난다).
        private static readonly Vector2 PanelAnchor = new Vector2(1f, 0.5f);
        private static readonly Vector2 PanelPivot = new Vector2(1f, 0.5f);
        private static readonly Vector2 PanelPosition = new Vector2(14f, 0f);
        // 창 크기 — 가로는 GameScene 설계가 우측 패널에 허용한 최대 폭(GameViewLayout.WidestRightPanel = 1040),
        // 세로는 논리 캔버스 높이 1440(GameViewLayout.DesignHeight)에서 위아래 20씩만 남긴 값이다.
        // 프레임(ui_bg_2) 테두리가 창 크기에 비례해 두꺼워지므로, 실제 내용이 놓이는 안쪽 칸을 넉넉히 얻으려면
        // 창 자체를 이만큼 키워야 한다(가로 860 → 1040, 세로 1360 → 1400).
        private const float PanelWidth = 1040f;
        private const float PanelHeight = 1400f;
        private static readonly Vector2 PanelSize = new Vector2(PanelWidth, PanelHeight);

        // 내용 영역(ContentArea)의 실제 크기 — 배경 프레임 테두리 안쪽 빈 칸(<see cref="PanelFrame"/>)이다.
        // 창 크기에서 파생되는 상수식이라 PanelWidth·PanelHeight만 고치면 아래 배치가 함께 따라온다
        // (1040×1400 → 744.09 × 1023.98).
        private const float ContentWidth = PanelWidth * (1f - PanelFrame.InsetLeft - PanelFrame.InsetRight)
            - PanelFrame.Pad * 2f;
        private const float ContentHeight = PanelHeight * (1f - PanelFrame.InsetTop - PanelFrame.InsetBottom)
            - PanelFrame.Pad * 2f;

        /// <summary>패널 본체(배경 이미지) 컨테이너.</summary>
        private RectTransform BuildContainer()
        {
            var img = NewImage("PanelRoot", _rootRect, panelBackground);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = PanelAnchor;
            rt.pivot = PanelPivot;
            rt.sizeDelta = PanelSize;
            rt.anchoredPosition = PanelPosition;
            // 다른 창과 같은 등장 연출(작게 시작해 제 크기로 커지기). 자리는 UIManager가 가방 창에 맞춰 잡으므로
            // 자동 도킹은 끄고(ConfigureCentered) 연출만 쓴다. 프리팹에 구워 두면 실행 시 PanelDragMove보다
            // 먼저 존재하게 되어(컴포넌트 순서) 저장된 자리 복원과 도착 위치 동기화 순서도 맞는다.
            rt.gameObject.AddComponent<SidePanelPop>().ConfigureCentered();
            return rt;
        }

        // ── 내용 영역(ContentArea) 안의 배치 ──
        // 모든 y는 ContentArea <b>좌상단 기준, 아래로 +</b>다(TopLeft 규약). 세로 합이 ContentHeight(1024)에
        // 정확히 맞도록 잡아, 어느 줄도 프레임 테두리를 넘지 않는다.
        private const float CharNavY = 0f;            // 캐릭터 전환 줄
        private const float CharNavHeight = 72f;
        private const float NavArrowSize = 72f;
        // 인디케이터 글자 블록(◀ 직업 Lv.N · i/N ▶)은 줄 가운데에 그대로 두고, 화살표 <b>버튼</b>은
        // 그 줄이 아니라 <b>한 줄 아래(윗줄=스킬 포인트·장착 슬롯 옆)</b> 좌우 끝에 놓는다 —
        // 글자에도 ◀▶가 있어 버튼을 글자 옆에 붙이면 화살표가 두 번 겹쳐 보인다.
        private const float CharIndicatorX = 141f;
        private const float CharIndicatorWidth = 462f;
        private const float NavArrowY = 127f;         // 화살표 버튼 y(CharNav 상단 기준, 아래로 +)
        private const float NavPrevX = 29f;           // 이전(◀) 화살표 x — 내용 영역 왼쪽 끝
        private const float NavNextX = 704f;          // 다음(▶) 화살표 x — 내용 영역 오른쪽 끝

        // 스킬 포인트 배너 + 장착 액티브 슬롯을 <b>한 줄에</b> 둔다. 목록을 둘로 나눈 만큼 위쪽에서 세로를
        // 아껴야 각 목록에 스킬 3개가 스크롤 없이 들어간다(따로 두면 한 줄당 약 90이 더 든다).
        // 윗줄은 내용 영역 전체 폭이 아니라, 좌우에 놓인 캐릭터 전환 화살표 사이에 들어가도록 좁혀 가운데에 둔다.
        private const float TopRowX = 63.55f;
        private const float TopRowY = 92f;
        private const float TopRowWidth = 616.98f;
        private const float TopRowHeight = 150f;
        private const float PointBannerWidth = 330f;  // 배너 폭(글자 길이에 맞춰 좁혔다)
        private const float PointBannerHeight = 76f;
        private const float EquipTitleX = 338.2f;     // "장착 액티브 스킬 (최대 2)" 라벨
        private const float EquipTitleWidth = 282.81f;
        private const float EquipAreaX = 352f;        // 배너 오른쪽에 붙는 장착 슬롯 블록(타일 시작 x)
        private const float EquipSlotUnitWidth = 102.4f; // 아이콘 타일(88) + 간격(14.4) — 이름 라벨을 두지 않아 타일만 붙는다

        // ── 액티브/패시브 2분할 목록 영역 ──
        // 같은 목록에 섞여 있던 스킬을 <b>가로 구분선으로 위아래 두 영역</b>으로 나눈다(위=액티브, 아래=패시브).
        // 각 영역은 머리글 + 자체 스크롤 목록이며, 높이는 행 3개(=직업당 스킬 수)가 스크롤 없이 들어가는 값이다.
        private const float SectionWidth = ContentWidth;
        private const float SectionHeaderHeight = 40f;
        private const float SectionListHeight = 253f;  // 행 75 × 3 + 간격 8 × 2 + 패딩 6 × 2
        private const float ActiveHeaderY = 256f;
        private const float ActiveListY = 304f;        // 액티브 목록: 304 ~ 557
        private const float DividerY = 571f;           // 두 영역을 가르는 가로 구분선
        private const float DividerHeight = 3f;
        private const float PassiveHeaderY = 588f;
        private const float PassiveListY = 636f;       // 패시브 목록: 636 ~ 889
        private const float MessageY = 904f;           // 하단 안내 문구
        private const float MessageHeight = 34f;
        private const float ResetButtonY = 948f;       // 하단 초기화 버튼(948 + 76 = 1024 = 내용 영역 바닥)
        private const float ResetButtonWidth = 360f;
        private const float ResetButtonHeight = 76f;

        /// <summary>액티브 장착 한도(2개)를 이미 채웠을 때 안내하는 문구. 미습득 액티브 스킬의 레벨업을 막고
        /// 그 이유를 알릴 때 쓴다(버튼은 흑백으로 보이지만 눌리면 이 문구가 뜬다).</summary>
        private const string EquipLimitMessage = "액티브 스킬은 2개까지만 활성화 할 수 있습니다.";

        // 눌릴 수 없는 버튼의 흑백(무채색) 톤 — 다른 비활성 버튼과 달리 "규칙에 막혔음"을 색으로 구분한다.
        private static readonly Color DisabledButtonColor = new Color(0.32f, 0.32f, 0.32f, 0.95f);
        private static readonly Color DisabledLabelColor = new Color(0.62f, 0.62f, 0.62f);

        // 영역 구분 색 — 액티브는 따뜻한 금색, 패시브는 차가운 하늘색 계열로 확실히 구분한다.
        private static readonly Color ActiveAccent = new Color(1f, 0.82f, 0.42f);
        private static readonly Color ActiveHeaderBg = new Color(0.24f, 0.17f, 0.07f, 0.95f);
        private static readonly Color ActiveListBg = new Color(0.12f, 0.09f, 0.05f, 0.6f);
        private static readonly Color PassiveAccent = new Color(0.55f, 0.82f, 1f);
        private static readonly Color PassiveHeaderBg = new Color(0.09f, 0.15f, 0.26f, 0.95f);
        private static readonly Color PassiveListBg = new Color(0.05f, 0.08f, 0.14f, 0.6f);

        /// <summary>캐릭터 전환 네비게이션(◀ 직업 Lv.N · 캐릭터 i/N ▶). 내용 영역 맨 윗줄이며
        /// 화살표는 블록 양 끝이 아니라 글자 옆으로 좁혀 붙인다(좌우 대칭).</summary>
        private void BuildCharacterNav(RectTransform content)
        {
            var area = NewRect("CharNav", content);
            TopLeft(area, 0f, CharNavY, ContentWidth, CharNavHeight);

            var prev = BuildNavArrow("PrevCharButton", area, "<", flip: true);
            TopLeft(prev.rectTransform, NavPrevX, NavArrowY, NavArrowSize, CharNavHeight);
            _prevButton = prev.gameObject.AddComponent<Button>();
            AddPunch(prev);

            _charIndicatorText = NewText("CharIndicator", area, "", 32, TextAnchor.MiddleCenter);
            _charIndicatorText.fontStyle = FontStyle.Bold;
            TopLeft(_charIndicatorText.rectTransform, CharIndicatorX, 0f, CharIndicatorWidth, CharNavHeight);

            var next = BuildNavArrow("NextCharButton", area, ">", flip: false);
            TopLeft(next.rectTransform, NavNextX, NavArrowY, NavArrowSize, CharNavHeight);
            _nextButton = next.gameObject.AddComponent<Button>();
            AddPunch(next);
        }

        /// <summary>캐릭터 전환 화살표 버튼 이미지를 만든다. 화살표 아트가 배선돼 있으면 그것을 쓰고
        /// (오른쪽 방향 그림이라 이전 버튼은 <paramref name="flip"/>으로 좌우 반전), 없으면 종전처럼
        /// 슬롯 배경 + 글자(<paramref name="fallbackLabel"/>)로 폴백한다.</summary>
        private Image BuildNavArrow(string name, RectTransform parent, string fallbackLabel, bool flip)
        {
            if (charNavArrow == null)
            {
                var box = NewImage(name, parent, slotNormal);
                Stretch(NewText(name + "Label", box.rectTransform, fallbackLabel, 40, TextAnchor.MiddleCenter).rectTransform);
                return box;
            }
            var img = NewImage(name, parent, charNavArrow);
            // 9-slice 테두리가 없는 아트라 Simple로 그리고 비율을 지켜 왜곡을 막는다.
            img.preserveAspect = true;
            if (flip)
            {
                // 좌우 반전은 스케일로 처리한다(반전용 아트를 따로 두지 않는다).
                img.rectTransform.localScale = new Vector3(-1f, 1f, 1f);
            }
            return img;
        }

        /// <summary>캐릭터 전환 버튼에 클릭 피드백(잠깐 커졌다 작아짐)을 붙인다.
        /// <see cref="ButtonPunchScale"/>은 Awake의 현재 스케일을 기준으로 배율을 곱하므로
        /// 좌우 반전된(scale.x = -1) 이전 버튼도 반전 상태를 유지한 채 커졌다 돌아온다.</summary>
        private static ButtonPunchScale AddPunch(Image img)
        {
            return img.gameObject.AddComponent<ButtonPunchScale>();
        }

        /// <summary>스킬 포인트 배너(왼쪽)와 장착 액티브 슬롯 블록(오른쪽)을 한 줄에 배치한다.</summary>
        private void BuildTopRow(RectTransform content)
        {
            var area = NewRect("TopRow", content);
            TopLeft(area, TopRowX, TopRowY, TopRowWidth, TopRowHeight);
            BuildPointBanner(area);
            BuildEquipSlots(area);
        }

        /// <summary>사용 가능 스킬 포인트 배너(캐릭터 레벨에서 파생). 윗줄 왼쪽에 세로 중앙으로 놓는다.</summary>
        private void BuildPointBanner(RectTransform row)
        {
            var bg = NewImage("PointBanner", row, null);
            bg.color = new Color(0.12f, 0.10f, 0.06f, 0.95f);
            TopLeft(bg.rectTransform, 0f, (TopRowHeight - PointBannerHeight) * 0.5f,
                PointBannerWidth, PointBannerHeight);

            _pointText = NewText("PointText", bg.rectTransform, "스킬 포인트  0 / 0", 30, TextAnchor.MiddleCenter);
            _pointText.fontStyle = FontStyle.Bold;
            _pointText.color = new Color(1f, 0.86f, 0.35f);
            Stretch(_pointText.rectTransform);
        }

        /// <summary>액티브 스킬 장착 슬롯 블록(최대 2). 라벨 + 아이콘 타일 2개(클릭하면 해제). 윗줄 오른쪽.</summary>
        private void BuildEquipSlots(RectTransform row)
        {
            var label = NewText("EquipTitle", row, "장착 액티브 스킬 (최대 2)", 24, TextAnchor.MiddleLeft);
            label.color = new Color(0.8f, 0.85f, 0.95f);
            TopLeft(label.rectTransform, EquipTitleX, 4f, EquipTitleWidth, 30f);

            for (int i = 0; i < MaxActiveSkills; i++)
            {
                BuildEquipSlot(row, i, EquipAreaX + i * EquipSlotUnitWidth);
            }
        }

        private const float EquipSlotTileSize = 88f;
        private const float EquipSlotTileY = 42f;   // 윗줄 안에서 타일 상단 y(제목 아래)

        /// <summary>장착 슬롯 1칸(아이콘 타일 + 클릭 시 해제). 스킬 이름은 붙이지 않는다(타일만 좁게 둔다 —
        /// 어떤 스킬인지는 아이콘과 아래 목록의 '해제' 표시로 알 수 있다).</summary>
        private void BuildEquipSlot(RectTransform area, int index, float x)
        {
            var slotBg = NewImage($"EquipSlot{index}", area, slotNormal);
            slotBg.color = new Color(0.16f, 0.17f, 0.24f, 0.98f);
            TopLeft(slotBg.rectTransform, x, EquipSlotTileY, EquipSlotTileSize, EquipSlotTileSize);
            var icon = NewImage("Icon", slotBg.rectTransform, null);
            icon.raycastTarget = false;
            icon.preserveAspect = true;
            Stretch(icon.rectTransform);
            icon.rectTransform.offsetMin = new Vector2(8f, 8f);
            icon.rectTransform.offsetMax = new Vector2(-8f, -8f);
            icon.color = new Color(1f, 1f, 1f, 0f);

            var btn = slotBg.gameObject.AddComponent<Button>();
            if (index == 0) { _equipSlotIcon0 = icon; _equipSlotButton0 = btn; }
            else { _equipSlotIcon1 = icon; _equipSlotButton1 = btn; }
        }

        private const float ScrollbarWidth = 18f;

        // 스킬 행 규격. 목록을 두 영역으로 나눈 만큼 행을 낮춰(132 → 75) 각 영역에 3개가 모두 들어가게 했다
        // (75 × 3 + 간격 8 × 2 + 패딩 6 × 2 = 253 = SectionListHeight).
        private const float RowHeight = 75f;
        private const int RowSpacing = 8;
        private const int RowPadding = 6;

        /// <summary>
        /// 스킬 목록을 <b>액티브(위) · 패시브(아래) 두 영역으로 나눠</b> 구성한다.
        /// 두 영역은 각각 머리글(색 띠 + 제목 + 설명)과 자체 스크롤 목록을 가지며, 사이에 가로 구분선을 둬
        /// 액티브와 패시브가 서로 다른 성격의 스킬임을 한눈에 보이게 한다.
        /// </summary>
        private void BuildSkillSections(RectTransform content)
        {
            _listContent = BuildSkillSection(
                content, "Active", "액티브 스킬", "장착한 2개만 전투에서 발동",
                ActiveHeaderY, ActiveListY, ActiveHeaderBg, ActiveAccent, ActiveListBg);

            BuildSectionDivider(content);

            _passiveListContent = BuildSkillSection(
                content, "Passive", "패시브 스킬", "배우면 항상 적용",
                PassiveHeaderY, PassiveListY, PassiveHeaderBg, PassiveAccent, PassiveListBg);
        }

        /// <summary>영역 1개(머리글 + 스크롤 목록)를 만들고 행을 담을 콘텐츠 RectTransform을 돌려준다.</summary>
        private RectTransform BuildSkillSection(
            RectTransform content, string id, string title, string hint,
            float headerY, float listY, Color headerBg, Color accent, Color listBg)
        {
            BuildSectionHeader(content, id, title, hint, headerY, headerBg, accent);
            return BuildSectionList(content, id, listY, listBg);
        }

        /// <summary>영역 머리글 — 좌측 색 띠 + 제목(영역 색) + 우측 짧은 설명.</summary>
        private void BuildSectionHeader(
            RectTransform content, string id, string title, string hint, float y, Color headerBg, Color accent)
        {
            var bg = NewImage($"{id}SectionHeader", content, null);
            bg.color = headerBg;
            var rt = bg.rectTransform;
            TopLeft(rt, 0f, y, SectionWidth, SectionHeaderHeight);

            // 좌측 세로 띠 — 영역 색을 가장 눈에 띄게 드러내는 표식.
            var stripe = NewImage("Accent", rt, null);
            stripe.color = accent;
            TopLeft(stripe.rectTransform, 0f, 0f, 8f, SectionHeaderHeight);

            var titleText = NewText("Title", rt, title, 26, TextAnchor.MiddleLeft);
            titleText.fontStyle = FontStyle.Bold;
            titleText.color = accent;
            TopLeft(titleText.rectTransform, 22f, 0f, 300f, SectionHeaderHeight);

            var hintText = NewText("Hint", rt, hint, 21, TextAnchor.MiddleRight);
            hintText.color = new Color(0.72f, 0.75f, 0.84f);
            TopLeft(hintText.rectTransform, SectionWidth - 414f, 0f, 400f, SectionHeaderHeight);
        }

        /// <summary>두 영역을 가르는 가로 구분선(내용 영역 폭 전체에 걸치는 얇은 띠).</summary>
        private void BuildSectionDivider(RectTransform content)
        {
            var line = NewImage("SectionDivider", content, null);
            line.color = new Color(0.45f, 0.48f, 0.58f, 0.55f);
            line.raycastTarget = false;
            TopLeft(line.rectTransform, 0f, DividerY, SectionWidth, DividerHeight);
        }

        /// <summary>영역 1개의 스킬 목록 스크롤 뷰(런타임에 행이 채워짐) + 우측 세로 스크롤바(항상 표시).</summary>
        private RectTransform BuildSectionList(RectTransform content, string id, float y, Color listBg)
        {
            const float viewW = SectionWidth - ScrollbarWidth - 8f; // 스크롤바 폭·간격 제외

            var area = NewRect($"{id}ListArea", content);
            TopLeft(area, 0f, y, SectionWidth, SectionListHeight);

            // 스크롤 뷰(좌측, 스크롤바 폭만큼 좁힘)
            var scrollGo = NewImage($"{id}SkillScroll", area, null);
            scrollGo.color = listBg;
            TopLeft(scrollGo.rectTransform, 0f, 0f, viewW, SectionListHeight);
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

            var rows = NewRect("Content", viewport.rectTransform);
            rows.anchorMin = new Vector2(0f, 1f);
            rows.anchorMax = new Vector2(1f, 1f);
            rows.pivot = new Vector2(0.5f, 1f);
            rows.anchoredPosition = Vector2.zero;
            rows.sizeDelta = Vector2.zero;

            var layout = rows.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = RowSpacing;
            layout.padding = new RectOffset(RowPadding, RowPadding, RowPadding, RowPadding);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var fitter = rows.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = rows;

            // 우측 세로 스크롤바(항상 표시)
            var bar = BuildScrollbar(area, viewW + 8f, 0f, ScrollbarWidth, SectionListHeight);
            scroll.verticalScrollbar = bar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            return rows;
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
        private void BuildFooter(RectTransform content)
        {
            _messageText = NewText("Message", content, "", 26, TextAnchor.MiddleCenter);
            TopLeft(_messageText.rectTransform, 0f, MessageY, ContentWidth, MessageHeight);

            // 버튼 아트는 공용 pixel_rpg_button(9-slice) — 다른 패널 버튼과 외형을 맞췄다.
            // 미배선 시에는 종전처럼 슬롯 배경 + 붉은 톤으로 폴백한다.
            bool hasButtonArt = buttonSprite != null;
            var resetImg = NewImage("ResetButton", content, hasButtonArt ? buttonSprite : slotNormal);
            resetImg.type = hasButtonArt ? Image.Type.Sliced : Image.Type.Simple;
            resetImg.color = hasButtonArt
                ? new Color(0.629f, 0.629f, 0.629f, 0.98f)
                : new Color(0.35f, 0.20f, 0.22f, 0.98f);
            TopLeft(resetImg.rectTransform, (ContentWidth - ResetButtonWidth) * 0.5f, ResetButtonY,
                ResetButtonWidth, ResetButtonHeight);
            _resetLabel = NewText("ResetLabel", resetImg.rectTransform, "스킬 초기화 (무료)", 30, TextAnchor.MiddleCenter);
            Stretch(_resetLabel.rectTransform);
            _resetButton = resetImg.gameObject.AddComponent<Button>();
        }

        /// <summary>런타임 배선: 창 드래그 이동 부착 + 버튼 리스너 등록.</summary>
        private void WireRuntime()
        {
            var panelRoot = transform.Find("PanelRoot") as RectTransform;

            // 등장 연출은 PanelDragMove보다 <b>먼저</b> 있어야 한다 — PanelDragMove는 Awake에서 SidePanelPop을
            // 찾아 캐시하므로, 뒤에 붙으면 저장된 자리를 복원해도 연출의 도착 위치가 갱신되지 않는다.
            // 보통은 프리팹에 구워져 있고(BuildContainer), 옛 프리팹을 위한 보정으로만 여기서 붙인다.
            SidePanel.AttachCentered(panelRoot);

            // 가방·큐브 창처럼 배경의 빈 곳을 잡아 창을 끌어 옮길 수 있게 한다.
            // 마지막으로 둔 자리는 기억했다가 다시 열 때 그 자리에 띄운다(스킬 행·스크롤 동작은 그대로).
            PanelDragMove.Attach(panelRoot, "Skill");

            if (_prevButton != null) _prevButton.onClick.AddListener(OnPrevCharacter);
            if (_nextButton != null) _nextButton.onClick.AddListener(OnNextCharacter);
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

        /// <summary>현재 캐릭터의 장착 액티브 스킬(equipped=1)을 2칸 슬롯에 반영한다.
        /// 빈 칸은 아이콘을 투명하게 두어 빈 타일로 보이게 한다(이름 라벨은 두지 않는다).</summary>
        private void RefreshEquipSlots(List<CharacterDto> chars)
        {
            var equipped = EquippedActiveCodes(CurrentCharacter(chars));
            for (int i = 0; i < MaxActiveSkills; i++)
            {
                int code = i < equipped.Count ? equipped[i] : 0;
                _equipSlotCodes[i] = code;
                var icon = i == 0 ? _equipSlotIcon0 : _equipSlotIcon1;
                if (icon == null)
                {
                    continue;
                }
                var sp = code != 0 && _iconDb != null ? _iconDb.Get(code) : null;
                icon.sprite = sp;
                icon.color = sp != null ? Color.white : new Color(1f, 1f, 1f, 0f);
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
                CreateSkillRow(ListOf(s), cur.characterId, s, level, canLevelUp,
                    equippedSet.Contains(s.skillCode), equippedSet.Count);
            }
        }

        /// <summary>스킬이 들어갈 목록(액티브는 위 영역, 패시브는 아래 영역)을 고른다.
        /// 패시브 영역이 없는 옛 프리팹에서는 종전처럼 한 목록에 모두 담아 표시가 비지 않게 한다.</summary>
        private RectTransform ListOf(SkillMaster skill)
        {
            if (skill.skillType == ActiveSkillType || _passiveListContent == null)
            {
                return _listContent;
            }
            return _passiveListContent;
        }

        /// <summary>스킬 1개의 행(아이콘+이름/타입/효과+레벨업 버튼, 액티브는 장착/해제 버튼)을 지정 목록에 생성한다.</summary>
        private void CreateSkillRow(RectTransform parent, int characterId, SkillMaster skill, int level, bool canLevelUp, bool isEquipped, int equippedCount)
        {
            var rowImg = NewImage($"Skill_{skill.skillCode}", parent, slotNormal);
            rowImg.color = new Color(0.10f, 0.12f, 0.18f, 0.95f);
            var le = rowImg.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = RowHeight;
            le.minHeight = RowHeight;
            var row = rowImg.rectTransform;
            _rows.Add(rowImg.gameObject);

            // 아이콘(좌측). 상세 정보는 hover 툴팁으로 노출하므로 행에는 아이콘+이름만 둔다.
            var iconBg = NewImage("IconBg", row, slotNormal);
            TopLeft(iconBg.rectTransform, 6f, 5f, 64f, 64f);
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
            var name = NewText("Name", row, skill.name, 28, TextAnchor.MiddleLeft);
            name.fontStyle = FontStyle.Bold;
            name.color = isActive ? ActiveAccent : PassiveAccent; // 행 하나만 봐도 성격을 알 수 있게 영역 색과 맞춘다
            TopLeft(name.rectTransform, 78f, 18f, 260f, 40f);

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

            // 장착 한도(2개)를 이미 채웠으면 <b>아직 배우지 않은</b> 다른 액티브 스킬은 레벨업을 막는다 —
            // 배워도 장착할 자리가 없어 전투에서 쓸 수 없으므로 포인트를 헛되게 쓰지 않도록 한다.
            bool blockedByEquipLimit = isActive && level <= 0 && equippedCount >= MaxActiveSkills;

            // 레벨업 버튼(우측)
            var btnImg = NewImage("LevelUpButton", row, slotNormal);
            var btnRt = btnImg.rectTransform;
            btnRt.anchorMin = btnRt.anchorMax = new Vector2(1f, 0.5f);
            btnRt.pivot = new Vector2(1f, 0.5f);
            btnRt.anchoredPosition = new Vector2(-8f, 0f);
            btnRt.sizeDelta = new Vector2(122f, 60f);

            string btnLabel;
            Color labelColor = Color.white;
            if (level >= skill.maxLevel)
            {
                btnLabel = "MAX";
                btnImg.color = new Color(0.28f, 0.26f, 0.18f, 0.9f);
            }
            else if (blockedByEquipLimit)
            {
                // 흑백(무채색)으로 눌릴 수 없는 상태임을 드러낸다. 다만 버튼 자체는 살려 둬야
                // 눌렀을 때 이유를 알려 줄 수 있다(interactable=false면 클릭이 오지 않는다).
                btnLabel = "레벨업\nSP 1";
                btnImg.color = DisabledButtonColor;
                labelColor = DisabledLabelColor;
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
            var btnText = NewText("Label", btnImg.rectTransform, btnLabel, 23, TextAnchor.MiddleCenter);
            btnText.fontStyle = FontStyle.Bold;
            btnText.color = labelColor;
            Stretch(btnText.rectTransform);

            var button = btnImg.gameObject.AddComponent<Button>();
            int code = skill.skillCode;
            if (blockedByEquipLimit)
            {
                // 눌러도 레벨업은 하지 않고 이유만 안내한다(색 변화 연출도 끈다 — 흑백을 유지).
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(OnLevelUpBlockedByEquipLimit);
            }
            else
            {
                button.interactable = canLevelUp;
                button.onClick.AddListener(() => OnLevelUp(characterId, code));
            }
        }

        /// <summary>장착 한도 때문에 막힌 레벨업 버튼을 눌렀을 때의 안내(레벨업은 하지 않는다).</summary>
        private void OnLevelUpBlockedByEquipLimit()
        {
            SetMessage(EquipLimitMessage);
        }

        /// <summary>액티브 스킬 행의 장착/해제 토글 버튼을 만든다(레벨업 버튼 왼쪽). 미습득은 비활성.</summary>
        private void CreateEquipButton(RectTransform row, int characterId, int skillCode, bool learned, bool isEquipped, int equippedCount)
        {
            var img = NewImage("EquipButton", row, slotNormal);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(-136f, 0f);
            rt.sizeDelta = new Vector2(122f, 60f);

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
            var text = NewText("Label", img.rectTransform, label, 25, TextAnchor.MiddleCenter);
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

        // ── 캐릭터 전환 ──

        /// <summary>이전 파티 캐릭터로 전환(순환).</summary>
        private void OnPrevCharacter()
        {
            PlayNavPunch(_prevButton);
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
            PlayNavPunch(_nextButton);
            if (_partyCount <= 1)
            {
                return;
            }
            _selectedCharacter = (_selectedCharacter + 1) % _partyCount;
            SetMessage(string.Empty);
            RefreshFromSession();
        }

        /// <summary>캐릭터 전환 버튼의 클릭 피드백(커졌다 작아짐)을 재생한다.
        /// 화면 갱신은 기다리지 않고 바로 진행하므로(전환 반응이 늦으면 답답하다) 연출만 겹쳐 돌린다.
        /// 파티가 1명이라 전환이 없을 때도 눌린 느낌은 주도록 이 호출은 조기 반환보다 앞에 둔다.</summary>
        private static void PlayNavPunch(Button button)
        {
            if (button == null)
            {
                return;
            }
            var punch = button.GetComponent<ButtonPunchScale>();
            if (punch != null)
            {
                punch.Play();
            }
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
