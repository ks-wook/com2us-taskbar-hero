using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TaskbarHero.Client.Managers;
using TaskbarHero.Client.MasterData;
using TaskbarHero.Common;
using TaskbarHero.Common.Dto;
using TaskbarHero.Common.MasterData;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 큐브(Hero-dric Cube) 오버레이 패널(inventory-item-cube 기획서 §5.6~5.8). 인벤토리의 '큐브' 버튼으로 진입한다.
    /// 세 가지 연산 탭을 제공한다:
    /// - <b>합성</b>: 같은 등급·슬롯·클래스 장비 combine_count개를 소모해 한 등급 높은 장비 1개(서버 무작위)를 얻는다.
    /// - <b>연금술(분해)</b>: 아이템을 골드로 전환한다(gold_per_scrap × 등급 × 개수).
    /// - <b>제작</b>: 레시피(cube_recipe)의 재료·골드를 소모해 지정 아이템을 만든다.
    /// 정적 계층(제목·골드·닫기·탭·큐브 레벨바·내용 영역·실행 버튼)은 에디터 빌드 시 생성되고, 표시될 때마다
    /// 세션(<see cref="Session.GameData"/>)의 인벤토리·큐브 상태와 마스터 데이터(cube_master·cube_recipe·item_master)로
    /// 내용을 채운다. 실제 소모·지급은 서버가 최종 확정하며, 성공 응답에 담겨 오는 변경분(<c>inventoryDelta</c>)·
    /// 큐브 상태·재화 잔액을 세션 캐시에 반영해 UI를 갱신한다(액션 뒤 재조회 없음).
    /// </summary>
    public class CubePanelController : MonoBehaviour
    {
        private enum Mode { Combine, Dismantle, Craft, Enhance }

        [Header("UI 리소스 (Assets/Art/UI/Cube)")]
        [SerializeField] private Sprite panelBackground; // ui_bg_2(인벤토리·스킬·룬 패널과 공용 프레임)
        [SerializeField] private Sprite slotNormal;
        [SerializeField] private Sprite slotHighlight;

        [Tooltip("실행 버튼 아트(Assets/Art/UI/Cube/pixel_rpg_button). 비우면 슬롯 배경으로 폴백한다.")]
        [SerializeField] private Sprite actionButtonSprite;

        [Tooltip("큐브 경험치 바의 프레임 아트(Assets/Art/Icon/Combat/체력바.png, 768×144). " +
                 "9-slice 테두리 값이 없는 이미지라 늘리면 테두리가 왜곡되므로 " +
                 "원본 비율(16:3)의 정수배 크기로만 그린다. 비우면 단색 트랙으로 폴백한다.")]
        [SerializeField] private Sprite expBarFrame;

        [Header("공용 아이템 슬롯 프리팹 (에디터 빌더가 배선)")]
        [Tooltip("Assets/Prefabs/UI/ItemSlot.prefab — 타일의 아이콘·등급 배경·수량·강화 배지를 그리는 공용 슬롯. " +
                 "인벤토리·거래소·우편함과 같은 프리팹을 써서 아이템 칸 외형을 통일한다.")]
        [SerializeField] private GameObject _itemSlotPrefab;

        [Header("강화 연출 프레임 (Assets/Art/Effect/UI — 에디터 빌더가 배선)")]
        [Tooltip("강화 망치질 프레임(EquipEnhanceHammer_01~). 요청 시작과 함께 재생된다.")]
        [SerializeField] private Sprite[] enhanceHammerFrames;
        [Tooltip("강화 성공 버스트 프레임(EnhanceSuccessBurst_01~). 망치질이 끝나는 시점에 터진다.")]
        [SerializeField] private Sprite[] enhanceBurstFrames;

        [Header("구성 참조 (에디터 빌더가 배선 — 직접 수정 불필요)")]
        [SerializeField] private Image _goldIcon;
        [SerializeField] private Text _goldText;
        [SerializeField] private Button _closeButton;
        [SerializeField] private Button _dimButton;
        [SerializeField] private Button _combineTab;
        [SerializeField] private Button _dismantleTab;
        [SerializeField] private Button _craftTab;
        [SerializeField] private Button _enhanceTab;
        [SerializeField] private Text _levelText;
        [SerializeField] private RectTransform _expFill;
        [SerializeField] private RectTransform _contentArea;
        [SerializeField] private Button _actionButton;
        [SerializeField] private Text _actionLabel;
        [SerializeField] private Text _footerText;
        [SerializeField] private Text _messageText;
        [Tooltip("창 본체(PanelRoot). 표시할 때 인벤토리 패널 자리에 맞추는 데 쓴다.")]
        [SerializeField] private RectTransform _panelRoot;

        private const float CanvasRefWidth = 1080f;
        private const float CanvasRefHeight = 1920f;
        private const int GoldCurrencyType = 1;
        private const int GoldItemCode = 1;
        // ── 큐브 레벨 · 경험치 바 ──
        // 프레임 아트(체력바.png 768×144)는 9-slice 테두리가 없어 늘리면 왜곡되므로 <b>정수배 축소</b>만 쓴다.
        // 1/3 축소 = 256×48. 레벨바 전체 폭도 이에 맞춰 740 → 448로 줄였다.
        private const float ExpTrackWidth = 256f;
        private const float ExpTrackHeight = 48f;
        private const float ExpTrackLeft = 156f;   // 레벨 텍스트(16 + 130) 오른쪽
        private const float LevelBarWidth = ExpTrackLeft + ExpTrackWidth + 36f; // 448
        private const float LevelBarHeight = 56f;

        /// <summary>레벨바의 왼쪽 x. 바 안의 <b>실제 내용</b>(레벨 글자 16 ~ 경험치 바 끝 412)의 중심이
        /// 창 가로 중앙(820의 절반)에 오도록 잡은 값이다 — 바 오른쪽 36px는 빈 여백이라
        /// 바 자체를 중앙에 두면 눈에는 왼쪽으로 치우쳐 보인다.</summary>
        private const float LevelBarLeft = 200f;

        /// <summary>채움 막대가 프레임 테두리를 덮지 않도록 안쪽으로 들이는 여백(프레임 테두리 두께).</summary>
        private const float ExpFillInset = 6f;

        /// <summary>채움 막대의 최대 폭(프레임 안쪽).</summary>
        private const float ExpFillMaxWidth = ExpTrackWidth - ExpFillInset * 2f;
        private const int GridColumns = 5;
        // 탭 4개(합성·연금술·제작·강화)를 내용 영역 폭(x 40 ~ 780) 안에 균등 배치하는 값.
        private const float TabWidth = 170f;
        private const float TabGap = 20f;

        // 내용 영역(아이템 그리드) 여백. 높이를 고정값으로 두면 PanelRoot 높이를 줄였을 때 아래로 넘쳐
        // 실행바·문구를 덮거나(넘침) 반대로 0에 가깝게 눌려 아무것도 안 보이므로, 위·아래·좌우 여백만 정하고
        // **PanelRoot 크기에 맞춰 늘어나도록** 앵커로 잡는다(사용자가 프리팹에서 패널 크기를 조절해도 따라온다).
        private const float ContentSideMargin = 40f;   // 좌우 여백(폭 820 기준 내용 폭 740)
        private const float ContentTopMargin = 289f;   // 탭·큐브 레벨바가 차지하는 상단
        private const float ContentBottomMargin = 231f; // 실행 버튼·하단 문구가 차지하는 하단

        // 각 줄의 y 좌표. 배경 프레임(ui_bg_2)의 장식을 피해 플레이 모드에서 직접 옮겨 확정한 값이다.
        private const float TabRowY = 158.50f;       // 탭 줄(패널 위에서 아래로 +)
        private const float LevelBarY = 225.30f;     // 큐브 레벨·경험치 바(위에서 아래로 +)
        private const float ActionButtonY = 116f;    // 실행 버튼(패널 바닥에서 위로 +)
        private const float FooterY = 45.90f;        // 하단 문구(바닥에서 위로 +)

        private Font _font;
        private RectTransform _rootRect;
        private ItemIconDatabase _iconDb;
        private Mode _mode = Mode.Combine;
        private bool _busy;

        private readonly List<long> _selCombine = new List<long>();
        private readonly List<long> _selDismantle = new List<long>();
        private int _selRecipe;

        // 강화는 한 번에 장비 1개만 다룬다(1회 호출 = 1단계, 기획서 §5.3).
        private long _selEnhance;

        private readonly List<GameObject> _contentChildren = new List<GameObject>();

        private bool AlreadyBuilt => _contentArea != null;

        private void Awake()
        {
            _rootRect = (RectTransform)transform;
            _iconDb = ItemIconDatabase.Load();
            if (AlreadyBuilt)
            {
                _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (_panelRoot == null)
                {
                    // 프리팹이 이 참조가 없던 시절에 구워졌을 때의 폴백(빌더를 다시 실행하면 배선된다).
                    _panelRoot = transform.Find("PanelRoot") as RectTransform;
                }
            }
            else
            {
                Construct();
            }
            WireRuntime();
        }

        /// <summary>패널 표시 시 세션 데이터로 갱신한다. 합성·분해 후보는 가방 아이템이라(코어 로드에는 없다)
        /// <b>패널을 열 때마다</b> 서버에서 다시 받아 목록을 그린다 — 창고와 같은 데이터를 보여주므로 같은 기준이며,
        /// 자동 전투로 전리품이 계속 쌓이는 동안 낡은 캐시를 후보로 내놓지 않기 위함이다.</summary>
        private void OnEnable()
        {
            if (!AlreadyBuilt)
            {
                return;
            }
            SetMessage(string.Empty);
            _selEnhance = 0; // 닫았다 다시 열면 선택은 초기화한다(그 사이 장비가 사라졌을 수 있다)
            ClearEnhanceResult();
            RefreshFromSession();
            InventoryLoader.ReloadBag(RefreshIfOpen, OnBagLoadError);
        }

        /// <summary>가방 조회 완료 후 목록을 다시 그린다(그 사이 패널이 닫혔으면 아무것도 하지 않는다).</summary>
        private void RefreshIfOpen()
        {
            if (this != null && gameObject.activeInHierarchy)
            {
                RefreshFromSession();
            }
        }

        /// <summary>가방 조회 실패: 후보 목록은 비워 둔 채 메시지로 알린다.</summary>
        private void OnBagLoadError(NetworkError error)
        {
            Debug.LogWarning($"[Cube] 가방 조회 실패: {error}");
            SetMessage(ErrorMessages.ToKorean(error));
        }

        /// <summary>에디터 빌드 전용: 전체 정적 계층을 생성하고 참조를 배선한다(프리팹 저장용).</summary>
        public void EditorConstruct()
        {
            Construct();
        }

        // ── 구성 ──

        /// <summary>패널 정적 계층을 1회 생성하고 직렬화 참조를 채운다.</summary>
        private void Construct()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildCanvas();
            BuildDim();
            var container = BuildContainer();
            // 제목("큐브")·보유 골드 블록은 두지 않는다 — 배경 아트(ui_bg_2)의 상단 장식판과 겹쳐 보여 제거했다
            // (골드 갱신 경로는 남겨 두므로 블록을 되살리면 그대로 동작한다 — RefreshGold의 null 가드).
            BuildTabs(container);
            BuildLevelBar(container);
            BuildContentArea(container);
            BuildActionBar(container);
            BuildFooter(container);
        }

        private void BuildCanvas()
        {
            var canvas = gameObject.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = gameObject.AddComponent<Canvas>();
            }
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 112; // 인벤토리(100)·룬(110) 위

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

        private void BuildDim()
        {
            var img = NewImage("Dim", _rootRect, null);
            img.color = new Color(0f, 0f, 0f, 0f); // 배경을 어둡게 하지 않는다 — 밖 클릭 닫기용 투명 차단막(레이캐스트만 유지)
            Stretch(img.rectTransform);
            _dimButton = img.gameObject.AddComponent<Button>();
            _dimButton.transition = Selectable.Transition.None;
        }

        private RectTransform BuildContainer()
        {
            var img = NewImage("PanelRoot", _rootRect, panelBackground);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(820f, 1000f);
            rt.anchoredPosition = Vector2.zero;
            // 다른 창과 같은 등장 연출(작게 시작해 제 크기로 커지기)을 쓰되, 자리는 스스로 정한다
            // (가방처럼 옆으로 도킹하지 않는다 — 마지막으로 끌어다 둔 자리는 PanelDragMove가 복원한다).
            rt.gameObject.AddComponent<SidePanelPop>().ConfigureCentered();
            _panelRoot = rt;
            return rt;
        }

        // 제목·보유 골드 블록은 만들지 않는다(배경 아트의 상단 장식판과 겹쳐 제거 — Construct 참고).
        // 닫기(X) 버튼도 미관상 두지 않는다 — 창은 가방 하단의 '큐브' 버튼을 다시 눌러 닫는다
        // (UIManager.ToggleCube). 스테이지·가방 창과 같은 규칙이다.

        /// <summary>합성/연금술/제작/강화 탭 버튼 4개(가로 배치). 네 칸이 내용 영역 폭(740) 안에 들어가도록
        /// 칸 폭을 <see cref="TabWidth"/>로 좁혀 균등 배치한다.</summary>
        private void BuildTabs(RectTransform container)
        {
            _combineTab = BuildTab(container, "CombineTab", "합성", TabX(0));
            _dismantleTab = BuildTab(container, "DismantleTab", "연금술", TabX(1));
            _craftTab = BuildTab(container, "CraftTab", "제작", TabX(2));
            _enhanceTab = BuildTab(container, "EnhanceTab", "강화", TabX(3));
            // 탭은 전환음(sfx_ui_tab)을 직접 재생하므로 전역 클릭음에서 제외한다.
            UiClickSound.Suppress(_combineTab);
            UiClickSound.Suppress(_dismantleTab);
            UiClickSound.Suppress(_craftTab);
            UiClickSound.Suppress(_enhanceTab);
        }

        /// <summary>탭 i번(0-based)의 왼쪽 x 좌표.</summary>
        private static float TabX(int index) => 40f + index * (TabWidth + TabGap);

        private Button BuildTab(RectTransform container, string name, string label, float x)
        {
            // 탭 배경도 실행 버튼과 같은 공용 버튼 아트(9-slice)를 쓴다. 색은 선택 상태에 따라
            // RefreshTabs/SetTabActive가 런타임에 덮어쓰므로 여기서는 지정하지 않는다.
            bool hasArt = actionButtonSprite != null;
            var img = NewImage(name, container, hasArt ? actionButtonSprite : slotNormal);
            img.type = hasArt ? Image.Type.Sliced : Image.Type.Simple;
            TopLeft(img.rectTransform, x, TabRowY, TabWidth, 64f);
            var t = NewText("Label", img.rectTransform, label, 30, TextAnchor.MiddleCenter);
            t.fontStyle = FontStyle.Bold;
            Stretch(t.rectTransform);
            return img.gameObject.AddComponent<Button>();
        }

        /// <summary>큐브 레벨·경험치 진행바.</summary>
        /// <summary>
        /// 큐브 레벨 + 경험치 바. 바는 프레임 아트(체력바.png)를 쓰고, 그 <b>안쪽</b>에 채움 막대를 둔다.
        /// <para>프레임 아트는 <b>9-slice 테두리 값이 없어</b>(spriteBorder 0) 늘리면 테두리 두께가 왜곡된다.
        /// 그래서 원본 768×144의 <b>정수배 축소</b>(1/3 = 256×48)로만 그려 픽셀이 깨지지 않게 한다.
        /// 프레임이 배선되지 않았으면 종전처럼 단색 트랙으로 폴백한다.</para>
        /// </summary>
        private void BuildLevelBar(RectTransform container)
        {
            var bg = NewImage("CubeLevelBar", container, null);
            bg.color = new Color(0f, 0f, 0f, 0.35f);
            TopLeft(bg.rectTransform, LevelBarLeft, LevelBarY, LevelBarWidth, LevelBarHeight);

            _levelText = NewText("CubeLevel", bg.rectTransform, "Lv.1", 28, TextAnchor.MiddleLeft);
            _levelText.fontStyle = FontStyle.Bold;
            TopLeft(_levelText.rectTransform, 16f, 10f, 130f, 36f);

            var track = NewImage("ExpTrack", bg.rectTransform, expBarFrame);
            track.type = Image.Type.Simple;
            track.color = expBarFrame != null ? Color.white : new Color(0.55f, 0.49f, 0.49f, 0.55f);
            float trackY = (LevelBarHeight - ExpTrackHeight) * 0.5f;
            TopLeft(track.rectTransform, ExpTrackLeft, trackY, ExpTrackWidth, ExpTrackHeight);

            // 채움 막대는 프레임 테두리 안쪽에만 그린다(테두리를 덮지 않게).
            var fill = NewImage("ExpFill", track.rectTransform, null);
            fill.color = new Color(0.35f, 0.72f, 0.4f, 0.95f);
            fill.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            fill.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            fill.rectTransform.pivot = new Vector2(0f, 0.5f);
            fill.rectTransform.anchoredPosition = new Vector2(ExpFillInset, 0f);
            fill.rectTransform.sizeDelta = new Vector2(ExpFillMaxWidth, ExpTrackHeight - ExpFillInset * 2f);
            _expFill = fill.rectTransform;
        }

        /// <summary>모드별 내용(아이템 그리드·레시피 목록)이 채워지는 영역.
        /// 크기를 고정하지 않고 PanelRoot에 맞춰 늘어나게 앵커로 잡는다(<see cref="ContentTopMargin"/> 참고).</summary>
        private void BuildContentArea(RectTransform container)
        {
            var bg = NewImage("ContentArea", container, null);
            bg.color = new Color(0.05f, 0.06f, 0.10f, 0.6f);
            var rt = bg.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(ContentSideMargin, ContentBottomMargin);
            rt.offsetMax = new Vector2(-ContentSideMargin, -ContentTopMargin);
            _contentArea = rt;
        }

        /// <summary>실행 버튼(합성/연금술/제작).</summary>
        private void BuildActionBar(RectTransform container)
        {
            // 실행 버튼은 전용 버튼 아트(pixel_rpg_button)를 9-slice로 늘려 쓴다.
            var btn = NewImage("ActionButton", container, actionButtonSprite != null ? actionButtonSprite : slotNormal);
            btn.type = actionButtonSprite != null ? Image.Type.Sliced : Image.Type.Simple;
            btn.color = actionButtonSprite != null
                ? new Color(1f, 1f, 1f, 0.90f)
                : new Color(0.42f, 0.28f, 0.16f, 0.98f);
            var brt = btn.rectTransform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0f);
            brt.pivot = new Vector2(0.5f, 0f);
            brt.anchoredPosition = new Vector2(0f, ActionButtonY);
            brt.sizeDelta = new Vector2(320f, 96f);
            _actionLabel = NewText("ActionLabel", brt, "합성", 34, TextAnchor.MiddleCenter);
            _actionLabel.fontStyle = FontStyle.Bold;
            Stretch(_actionLabel.rectTransform);
            _actionButton = btn.gameObject.AddComponent<Button>();
        }

        private void BuildFooter(RectTransform container)
        {
            _footerText = NewText("Footer", container, string.Empty, 26, TextAnchor.MiddleCenter);
            var frt = _footerText.rectTransform;
            frt.anchorMin = frt.anchorMax = new Vector2(0.5f, 0f);
            frt.pivot = new Vector2(0.5f, 0f);
            frt.sizeDelta = new Vector2(760f, 96f);
            frt.anchoredPosition = new Vector2(0f, FooterY);

            _messageText = NewText("Message", container, string.Empty, 24, TextAnchor.MiddleCenter);
            _messageText.color = new Color(1f, 0.7f, 0.55f);
            var mrt = _messageText.rectTransform;
            mrt.anchorMin = mrt.anchorMax = new Vector2(0.5f, 0f);
            mrt.pivot = new Vector2(0.5f, 0f);
            mrt.sizeDelta = new Vector2(760f, 34f);
            mrt.anchoredPosition = new Vector2(0f, 12f);
        }

        private void WireRuntime()
        {
            // 등장 연출은 PanelDragMove보다 <b>먼저</b> 붙여야 한다 — PanelDragMove는 Awake에서
            // SidePanelPop을 찾아 캐시하므로, 뒤에 붙이면 저장된 자리를 복원해도 연출의 도착 위치가 갱신되지 않는다.
            EnsurePopAnimation();

            // 창을 끌어 옮길 수 있게 한다(배경의 빈 곳을 잡고 드래그).
            // 마지막으로 둔 자리는 기억했다가 다시 열 때 그 자리에 띄운다.
            PanelDragMove.Attach(_panelRoot, "Cube");

            // 큐브는 가방과 <b>동시에</b> 열리므로, 화면 전체를 덮는 차단막이 있으면 가방 아이템을 집을 수 없다.
            // 그래서 큐브 자신의 차단막은 레이캐스트를 끄고, 밖 클릭 닫기는 <b>가방의 차단막</b>이 대신 처리한다
            // (InventoryPanelController.OnDimClick — 큐브가 함께 열려 있으면 큐브부터 닫는다).
            if (_dimButton != null)
            {
                _dimButton.interactable = false;
                var dimImage = _dimButton.GetComponent<Image>();
                if (dimImage != null)
                {
                    dimImage.raycastTarget = false;
                }
            }

            if (_closeButton != null) _closeButton.onClick.AddListener(Close);
            if (_combineTab != null) _combineTab.onClick.AddListener(() => SwitchMode(Mode.Combine));
            if (_dismantleTab != null) _dismantleTab.onClick.AddListener(() => SwitchMode(Mode.Dismantle));
            if (_craftTab != null) _craftTab.onClick.AddListener(() => SwitchMode(Mode.Craft));
            if (_enhanceTab != null) _enhanceTab.onClick.AddListener(() => SwitchMode(Mode.Enhance));
            if (_actionButton != null) _actionButton.onClick.AddListener(OnAction);
        }

        /// <summary>
        /// 등장 연출(<see cref="SidePanelPop"/>)이 붙어 있는지 확인하고, 없으면 붙인다 —
        /// 이 컴포넌트가 없던 시절에 구워진 프리팹을 위한 폴백이다(빌더를 다시 실행하면 프리팹에 포함된다).
        /// <para>이미 표시된 상태에서 붙이면 그 즉시 <c>OnEnable</c>이 돌아 자리가 흔들릴 수 있으므로,
        /// 현재 자리를 잡아 두었다가 그대로 되돌리고 연출의 도착 위치로 다시 알린다.</para>
        /// </summary>
        private void EnsurePopAnimation()
        {
            if (_panelRoot == null)
            {
                return;
            }

            var pop = _panelRoot.GetComponent<SidePanelPop>();
            if (pop != null)
            {
                pop.ConfigureCentered();
                return;
            }

            var keep = _panelRoot.anchoredPosition;
            pop = _panelRoot.gameObject.AddComponent<SidePanelPop>();
            pop.ConfigureCentered();
            _panelRoot.anchoredPosition = keep;
            pop.SyncRestPosition();
        }

        // ── 세션/마스터 연동 ──

        /// <summary>골드·큐브 레벨바·탭·내용 영역을 세션+마스터로 갱신한다.</summary>
        private void RefreshFromSession()
        {
            MasterDataManager.EnsureLoaded();
            if (_iconDb == null)
            {
                _iconDb = ItemIconDatabase.Load();
            }
            RefreshGold();
            RefreshLevelBar();
            RefreshTabs();
            RebuildContent();
        }

        /// <summary>보유 골드량·아이콘을 세션 재화에서 갱신한다.</summary>
        private void RefreshGold()
        {
            if (_goldText != null)
            {
                _goldText.text = CurrentGold().ToString("N0");
            }
            var sp = _iconDb != null ? _iconDb.Get(GoldItemCode) : null;
            if (_goldIcon != null && sp != null)
            {
                _goldIcon.sprite = sp;
                _goldIcon.color = Color.white;
            }
        }

        /// <summary>큐브 레벨·경험치 진행바를 세션+마스터로 갱신한다.</summary>
        private void RefreshLevelBar()
        {
            int level = CubeLevel();
            long exp = CubeExp();
            long req = 0;
            var db = MasterDataManager.Db;
            if (db != null && db.Cubes.TryGetValue(level, out var cm))
            {
                req = cm.requiredExp;
            }
            if (_levelText != null)
            {
                _levelText.text = req > 0 ? $"Lv.{level}" : $"Lv.{level} MAX";
            }
            if (_expFill != null)
            {
                float ratio = req > 0 ? Mathf.Clamp01((float)exp / req) : 1f;
                _expFill.sizeDelta = new Vector2(ExpFillMaxWidth * ratio, _expFill.sizeDelta.y);
            }
        }

        /// <summary>탭 강조(현재 모드 밝게).</summary>
        private void RefreshTabs()
        {
            SetTabActive(_combineTab, _mode == Mode.Combine);
            SetTabActive(_dismantleTab, _mode == Mode.Dismantle);
            SetTabActive(_craftTab, _mode == Mode.Craft);
            SetTabActive(_enhanceTab, _mode == Mode.Enhance);
        }

        private static void SetTabActive(Button tab, bool active)
        {
            if (tab == null)
            {
                return;
            }
            var img = tab.GetComponent<Image>();
            if (img != null)
            {
                img.color = active ? new Color(0.55f, 0.20f, 0.18f, 0.98f) : new Color(0.20f, 0.18f, 0.22f, 0.9f);
            }
        }

        /// <summary>탭 전환(선택 초기화 후 내용 재구성).</summary>
        private void SwitchMode(Mode mode)
        {
            _mode = mode;
            SoundManager.Sfx(SoundId.UiTab); // 탭 전환음(사운드 정의서 §4.1)
            _selCombine.Clear();
            _selDismantle.Clear();
            _selRecipe = 0;
            _selEnhance = 0;
            ClearEnhanceResult();
            SetMessage(string.Empty);
            RefreshTabs();
            RebuildContent();
        }

        // ── 내용 구성(모드별) ──

        /// <summary>현재 모드에 맞는 내용(그리드·목록)과 하단 정보·실행 버튼을 재구성한다.</summary>
        private void RebuildContent()
        {
            ClearContent();
            switch (_mode)
            {
                case Mode.Combine: BuildCombineContent(); break;
                case Mode.Dismantle: BuildDismantleContent(); break;
                case Mode.Craft: BuildCraftContent(); break;
                case Mode.Enhance: BuildEnhanceContent(); break;
            }
        }

        /// <summary>합성: <b>등록 칸 3개</b>(combine_count). 가방에서 같은 등급·클래스 장비를 끌어다 올린다.</summary>
        private void BuildCombineContent()
        {
            var db = MasterDataManager.Db;
            int combineCount = CombineCount();
            BuildDropSlots(combineCount, combineCount);
            FillDropSlots(_selCombine);

            if (_selCombine.Count > 0 && db.Items.TryGetValue(FirstSelectedCombineCode(), out var fm))
            {
                db.Grades.TryGetValue(fm.grade, out var g);
                string gradeName = g != null ? g.name : fm.grade.ToString();
                SetFooter($"합성 등급 {gradeName} → 등급 {fm.grade + 1} · 선택 {_selCombine.Count}/{combineCount}");
            }
            else
            {
                SetFooter($"가방에서 같은 등급·클래스 장비 {combineCount}개를 끌어다 놓으세요.");
            }
            SetAction("합성", _selCombine.Count == combineCount);
        }

        /// <summary>연금술(분해): <b>등록 칸 9개</b>(3×3). 가방에서 끌어다 올린 아이템을 골드로 전환.</summary>
        private void BuildDismantleContent()
        {
            BuildDropSlots(DismantleSlotCount, DismantleColumns);
            FillDropSlots(_selDismantle);

            long gold = EstimateDismantleGold();
            SetFooter(_selDismantle.Count > 0
                ? $"연금술 작동 시 획득 골드: {GoldFormat.Highlight(gold)}"
                : "가방에서 분해할 아이템을 끌어다 놓으세요(최대 9개).");
            SetAction("연금술", _selDismantle.Count > 0);
        }

        /// <summary>제작: 레시피 그리드. 선택 레시피의 재료·비용·요구 큐브 레벨을 하단에 안내.</summary>
        private void BuildCraftContent()
        {
            var content = BuildScrollGrid();
            var db = MasterDataManager.Db;
            var recipes = new List<CubeRecipe>(db.CubeRecipes.Values);
            recipes.Sort((a, b) => a.recipeCode.CompareTo(b.recipeCode));
            foreach (var r in recipes)
            {
                bool selected = r.recipeCode == _selRecipe;
                int code = r.recipeCode;
                CreateItemTile(content, r.resultItemCode, 0, string.Empty,
                    $"Lv.{r.reqCubeLevel}", selected, () => SelectRecipe(code));
            }
            if (recipes.Count == 0)
            {
                ShowEmptyHint("제작 레시피가 없습니다.");
            }

            RefreshCraftFooter();
        }

        /// <summary>선택 레시피의 재료 보유/요구·비용·요구 레벨을 하단에 표시하고 실행 버튼을 갱신한다.</summary>
        private void RefreshCraftFooter()
        {
            var db = MasterDataManager.Db;
            if (_selRecipe == 0 || db == null || !db.CubeRecipes.TryGetValue(_selRecipe, out var recipe))
            {
                SetFooter("제작할 레시피를 선택하세요.");
                SetAction("제작", false);
                return;
            }

            db.Items.TryGetValue(recipe.resultItemCode, out var resultItem);
            string resultName = resultItem != null ? resultItem.name : recipe.resultItemCode.ToString();

            bool hasAll = true;
            var parts = new List<string>();
            if (recipe.ingredients != null)
            {
                foreach (var ing in recipe.ingredients)
                {
                    int have = MaterialCount(ing.materialCode);
                    bool ok = have >= ing.quantity;
                    hasAll &= ok;
                    db.Items.TryGetValue(ing.materialCode, out var mm);
                    string mName = mm != null ? mm.name : ing.materialCode.ToString();
                    parts.Add($"{mName} {have}/{ing.quantity}");
                }
            }

            int level = CubeLevel();
            long gold = CurrentGold();
            bool levelOk = level >= recipe.reqCubeLevel;
            bool goldOk = gold >= recipe.costGold;

            string mats = parts.Count > 0 ? string.Join(", ", parts) : "재료 없음";
            SetFooter($"{resultName} 제작 (요구 Lv.{recipe.reqCubeLevel})\n재료: {mats}\n비용: {GoldFormat.Highlight(recipe.costGold)}");
            SetAction("제작", hasAll && levelOk && goldOk);
        }

        /// <summary>
        /// 강화: <b>대상 칸 1개 + 결과 칸 1개</b>. 가방에서 장비를 끌어다 왼쪽 칸에 올리고, 강화에 성공하면
        /// 왼쪽이 비고 오른쪽에 결과가 나온다(양쪽에 동시에 두지 않는다).
        /// 장착 중인 장비도 해제 없이 강화할 수 있으므로(기획서 §5.3) 후보에 함께 오른다.
        /// </summary>
        private void BuildEnhanceContent()
        {
            _dropSlots.Clear();
            const float gapX = 120f;
            _dropSlotSize = DropSlotSizeFor(2, 1);
            float half = (_dropSlotSize + gapX) * 0.5f;

            var target = CreateDropSlot(0, true, new Vector2(-half, -DropSlotTopMargin));
            _dropSlots.Add(target);

            var arrow = NewText("Arrow", _contentArea, "▶", 44, TextAnchor.MiddleCenter);
            arrow.color = new Color(0.98f, 0.82f, 0.35f, 0.95f);
            var art = arrow.rectTransform;
            art.anchorMin = art.anchorMax = new Vector2(0.5f, 1f);
            art.pivot = new Vector2(0.5f, 1f);
            art.sizeDelta = new Vector2(gapX, _dropSlotSize);
            art.anchoredPosition = new Vector2(0f, -DropSlotTopMargin);
            _contentChildren.Add(arrow.gameObject);

            var preview = CreateDropSlot(1, false, new Vector2(half, -DropSlotTopMargin)); // 표시 전용(드롭 안 받음)

            var label = NewText("PreviewLabel", _contentArea, "강화 후", 22, TextAnchor.MiddleCenter);
            label.color = new Color(0.75f, 0.78f, 0.85f);
            var lrt = label.rectTransform;
            lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 1f);
            lrt.pivot = new Vector2(0.5f, 1f);
            lrt.sizeDelta = new Vector2(_dropSlotSize, 30f);
            lrt.anchoredPosition = new Vector2(half, -DropSlotTopMargin - _dropSlotSize - 6f);
            _contentChildren.Add(label.gameObject);

            // 강화 전에는 왼쪽에만, 강화 후에는 오른쪽에만 아이템이 있다(양쪽에 동시에 두지 않는다).
            var cand = FindEnhanceCandidate(_selEnhance);
            if (_enhanceResultCode != 0)
            {
                target.SetEmpty();
                preview.SetItem(0, _enhanceResultCode, _enhanceResultLevel, string.Empty);
            }
            else if (cand != null)
            {
                target.SetItem(cand.Value.itemId, cand.Value.itemCode, cand.Value.enhanceLevel, string.Empty);
                preview.SetEmpty();
            }
            else
            {
                target.SetEmpty();
                preview.SetEmpty();
            }

            RefreshEnhanceFooter();
        }

        /// <summary>선택 장비의 현재/다음 단계·비용·스탯 변화를 하단에 표시하고 실행 버튼을 갱신한다.
        /// 최대 단계(enhance_master 행 수 = +10)에 도달했거나 골드가 부족하면 실행할 수 없다 —
        /// 서버도 각각 MaxEnhanceReached(4004)·InsufficientCurrency(4005)로 거부한다.</summary>
        private void RefreshEnhanceFooter()
        {
            var db = MasterDataManager.Db;
            var cand = FindEnhanceCandidate(_selEnhance);
            if (cand == null || db == null)
            {
                // 방금 강화를 마쳤으면 결과 칸을 설명한다(다음 강화는 새로 끌어다 놓으면 된다).
                SetFooter(_enhanceResultCode != 0
                    ? $"강화 완료 — 결과 +{_enhanceResultLevel}\n다음 장비를 끌어다 놓으세요."
                    : "강화할 장비를 끌어다 놓으세요(장착 중인 장비도 가능).");
                SetAction("강화", false);
                return;
            }

            db.Items.TryGetValue(cand.Value.itemCode, out var im);
            string name = im != null ? im.name : cand.Value.itemCode.ToString();
            int cur = cand.Value.enhanceLevel;
            var next = db.NextEnhance(cur);
            if (next == null)
            {
                SetFooter($"{name} +{cur}\n최대 강화 단계입니다.");
                SetAction("강화", false);
                return;
            }

            long gold = CurrentGold();
            bool goldOk = gold >= next.cost;
            string statLine = EnhanceStatPreview(im, cur, next.enhanceLevel);
            string equippedMark = cand.Value.equipped ? " (장착 중)" : string.Empty;
            // 부족 안내는 메시지 줄이 아니라 하단 정보에 붙인다 — 메시지 줄은 액션 결과("강화 성공! +4")를 위해 비워 둔다.
            SetFooter($"{name} +{cur} → +{next.enhanceLevel}{equippedMark}"
                      + (string.IsNullOrEmpty(statLine) ? string.Empty : $"\n{statLine}")
                      + $"\n비용: {GoldFormat.Highlight(next.cost)}"
                      + (goldOk ? string.Empty : "  (골드 부족)"));
            SetAction("강화", goldOk);
        }

        /// <summary>강화 전/후 옵션 스탯 비교 한 줄(예: "ATK 105 → 110"). 옵션이 없으면 빈 문자열.
        /// 배율은 서버가 내려주지 않으므로 마스터(enhance_master)에서 읽는다(기획서 §5.3).</summary>
        private static string EnhanceStatPreview(ItemMaster im, int currentLevel, int nextLevel)
        {
            var db = MasterDataManager.Db;
            if (im == null || db == null || im.itemType != 1)
            {
                return string.Empty;
            }
            float curMult = db.EnhanceMultiplier(currentLevel);
            float nextMult = db.EnhanceMultiplier(nextLevel);
            var parts = new List<string>();
            if (im.baseStats.atk != 0)
            {
                parts.Add($"ATK {ItemInfoText.ApplyMultiplier(im.baseStats.atk, curMult)}"
                          + $" → {ItemInfoText.ApplyMultiplier(im.baseStats.atk, nextMult)}");
            }
            if (im.baseStats.def != 0)
            {
                parts.Add($"DEF {ItemInfoText.ApplyMultiplier(im.baseStats.def, curMult)}"
                          + $" → {ItemInfoText.ApplyMultiplier(im.baseStats.def, nextMult)}");
            }
            if (im.baseStats.hp != 0)
            {
                parts.Add($"HP {ItemInfoText.ApplyMultiplier(im.baseStats.hp, curMult)}"
                          + $" → {ItemInfoText.ApplyMultiplier(im.baseStats.hp, nextMult)}");
            }
            return parts.Count > 0 ? string.Join(" · ", parts) : string.Empty;
        }

        // ── 등록 슬롯(가방에서 드래그해 올리는 빈 칸) ──

        /// <summary>등록 칸 한 변 크기의 <b>상한</b>과 간격. 실제 크기는 내용 영역에 맞춰 줄어든다
        /// (<see cref="DropSlotSizeFor"/>) — 연금술 3×3처럼 칸이 많으면 고정 크기로는 영역을 넘친다.</summary>
        private const float MaxDropSlotSize = 168f;
        private const float DropSlotGap = 24f;

        /// <summary>등록 칸 격자의 위쪽 여백.</summary>
        private const float DropSlotTopMargin = 24f;

        /// <summary>격자 아래에 남겨 둘 여백(강화 탭의 '강화 후' 라벨 등이 들어간다).</summary>
        private const float DropSlotBottomReserve = 40f;

        /// <summary>이번에 그리는 격자의 칸 한 변 크기(<see cref="BuildDropSlots"/>가 정한다).</summary>
        private float _dropSlotSize = MaxDropSlotSize;

        /// <summary>
        /// <paramref name="columns"/>열 × <paramref name="rows"/>행 격자가 내용 영역 안에 <b>다 들어가는</b>
        /// 칸 크기를 구한다. 가로·세로 중 더 빡빡한 쪽에 맞추고 <see cref="MaxDropSlotSize"/>를 넘지 않는다.
        /// </summary>
        private float DropSlotSizeFor(int columns, int rows)
        {
            if (_contentArea == null || columns <= 0 || rows <= 0)
            {
                return MaxDropSlotSize;
            }
            float availW = _contentArea.rect.width;
            float availH = _contentArea.rect.height - DropSlotTopMargin - DropSlotBottomReserve;
            float byWidth = (availW - (columns - 1) * DropSlotGap) / columns;
            float byHeight = (availH - (rows - 1) * DropSlotGap) / rows;
            return Mathf.Max(48f, Mathf.Min(MaxDropSlotSize, byWidth, byHeight));
        }

        /// <summary>연금술(분해) 등록 칸 수와 열 수 — 3×3.</summary>
        private const int DismantleSlotCount = 9;
        private const int DismantleColumns = 3;

        // 강화 결과 표시 상태. 강화에 <b>성공한 뒤에만</b> 오른쪽 칸에 결과가 뜨고 왼쪽 칸은 비워진다
        // (강화 전에는 왼쪽에만 아이템이 있다). 새 아이템을 올리거나 탭을 바꾸면 지운다.
        private int _enhanceResultCode;   // 0 = 결과 없음
        private int _enhanceResultLevel;

        private readonly List<CubeDropSlot> _dropSlots = new List<CubeDropSlot>();

        /// <summary>
        /// 등록 칸을 격자로 만든다(<paramref name="columns"/>열, 총 <paramref name="count"/>칸).
        /// 내용 영역 위쪽 가운데에 배치하며, 만든 칸은 <see cref="_dropSlots"/>에 순서대로 담긴다.
        /// </summary>
        private void BuildDropSlots(int count, int columns, float topOffset = DropSlotTopMargin)
        {
            _dropSlots.Clear();
            int rows = Mathf.CeilToInt(count / (float)columns);
            _dropSlotSize = DropSlotSizeFor(columns, rows); // 영역을 넘치지 않도록 칸 크기를 맞춘다
            float rowWidth = columns * _dropSlotSize + (columns - 1) * DropSlotGap;
            float startX = -rowWidth * 0.5f + _dropSlotSize * 0.5f;

            for (int i = 0; i < count; i++)
            {
                int c = i % columns;
                int r = i / columns;
                var pos = new Vector2(
                    startX + c * (_dropSlotSize + DropSlotGap),
                    -topOffset - r * (_dropSlotSize + DropSlotGap));
                _dropSlots.Add(CreateDropSlot(i, true, pos));
            }
        }

        /// <summary>등록 칸 한 개를 만든다. <paramref name="acceptsDrop"/>가 false면 표시 전용(강화 결과 미리보기).</summary>
        private CubeDropSlot CreateDropSlot(int index, bool acceptsDrop, Vector2 anchoredPos)
        {
            var frame = NewImage($"DropSlot{index}", _contentArea, slotNormal);
            frame.color = Color.white;
            var rt = frame.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(_dropSlotSize, _dropSlotSize);
            rt.anchoredPosition = anchoredPos;
            _contentChildren.Add(frame.gameObject);

            int hintSize = Mathf.Max(20, Mathf.RoundToInt(_dropSlotSize * 0.33f)); // 칸이 작아지면 안내 글자도 함께
            var placeholder = NewText("Placeholder", rt, acceptsDrop ? "+" : "?", hintSize, TextAnchor.MiddleCenter);
            placeholder.color = new Color(1f, 1f, 1f, 0.35f);
            Stretch(placeholder.rectTransform);

            // 아이템 그림(공용 슬롯). 이름 줄이 없는 칸이라 여백 없이 꽉 채운다.
            ItemSlotView view = null;
            if (_itemSlotPrefab != null)
            {
                var go = Instantiate(_itemSlotPrefab, rt);
                go.name = "ItemSlot";
                var srt = (RectTransform)go.transform;
                srt.anchorMin = Vector2.zero;
                srt.anchorMax = Vector2.one;
                srt.offsetMin = new Vector2(8f, 8f);
                srt.offsetMax = new Vector2(-8f, -8f);
                view = go.GetComponent<ItemSlotView>();
            }

            var slot = frame.gameObject.AddComponent<CubeDropSlot>();
            slot.Initialize(this, index, acceptsDrop, frame, placeholder, view);
            return slot;
        }

        /// <summary>등록된 아이템들을 앞에서부터 칸에 채운다(남는 칸은 빈 칸으로 둔다).</summary>
        private void FillDropSlots(List<long> ids)
        {
            for (int i = 0; i < _dropSlots.Count; i++)
            {
                if (i < ids.Count)
                {
                    var it = FindInventoryItem(ids[i]);
                    if (it != null)
                    {
                        _dropSlots[i].SetItem(it.itemId, it.itemCode, it.enhanceLevel, QuantityBadge(it));
                        continue;
                    }
                }
                _dropSlots[i].SetEmpty();
            }
        }

        /// <summary>
        /// 가방에서 끌어온 아이템을 <b>현재 탭</b>의 등록 칸에 올린다(<see cref="InventoryItemView"/>가 호출).
        /// 탭별 조건(개수 상한·같은 등급/클래스·분해 가능 여부 등)은 기존 선택 로직을 그대로 쓴다.
        /// </summary>
        /// <returns>등록했으면 true. 조건에 맞지 않으면 false(안내 문구는 이 안에서 띄운다).</returns>
        public bool TryRegisterFromInventory(long itemId)
        {
            SetMessage(string.Empty);
            switch (_mode)
            {
                case Mode.Combine:
                    if (!IsEligible(EligibleCombineItems(), itemId))
                    {
                        SetMessage("합성할 수 없는 아이템입니다.");
                        return false;
                    }
                    if (_selCombine.Contains(itemId))
                    {
                        return false; // 이미 올려 둔 아이템
                    }
                    ToggleCombine(itemId);
                    return _selCombine.Contains(itemId);

                case Mode.Dismantle:
                    if (!IsEligible(EligibleDismantleItems(), itemId))
                    {
                        SetMessage("분해할 수 없는 아이템입니다.");
                        return false;
                    }
                    if (_selDismantle.Contains(itemId))
                    {
                        return false;
                    }
                    if (_selDismantle.Count >= DismantleSlotCount)
                    {
                        SetMessage($"최대 {DismantleSlotCount}개까지 올릴 수 있습니다.");
                        return false;
                    }
                    ToggleDismantle(itemId);
                    return true;

                case Mode.Enhance:
                    if (FindEnhanceCandidate(itemId) == null)
                    {
                        SetMessage("강화할 수 없는 아이템입니다.");
                        return false;
                    }
                    _selEnhance = itemId;
                    ClearEnhanceResult(); // 새 대상을 올리면 지난 결과 표시를 치운다
                    RebuildContent();
                    return true;

                default:
                    return false; // 제작 탭은 등록 칸이 없다(레시피를 고르는 방식)
            }
        }

        /// <summary>등록 칸을 클릭했을 때 그 아이템의 등록을 해제한다(<see cref="CubeDropSlot"/>가 호출).</summary>
        public void UnregisterSlotItem(long itemId)
        {
            SetMessage(string.Empty);
            switch (_mode)
            {
                case Mode.Combine: _selCombine.Remove(itemId); break;
                case Mode.Dismantle: _selDismantle.Remove(itemId); break;
                case Mode.Enhance: _selEnhance = 0; ClearEnhanceResult(); break;
                default: return;
            }
            RebuildContent();
        }

        /// <summary>강화 결과 표시(오른쪽 칸)를 지운다.</summary>
        private void ClearEnhanceResult()
        {
            _enhanceResultCode = 0;
            _enhanceResultLevel = 0;
        }

        /// <summary>강화 결과 칸을 <b>클릭해서</b> 치운다(<see cref="CubeDropSlot"/>가 호출).</summary>
        public void ClearEnhanceResultDisplay()
        {
            ClearEnhanceResult();
            SetMessage(string.Empty);
            RebuildContent();
        }

        /// <summary>후보 목록에 그 아이템이 있는지.</summary>
        private static bool IsEligible(IEnumerable<InventoryItemDto> candidates, long itemId)
        {
            foreach (var it in candidates)
            {
                if (it != null && it.itemId == itemId)
                {
                    return true;
                }
            }
            return false;
        }

        // ── 상호작용(선택) ──

        private void ToggleCombine(long itemId)
        {
            SetMessage(string.Empty);
            if (_selCombine.Contains(itemId))
            {
                _selCombine.Remove(itemId);
                RebuildContent();
                return;
            }
            if (_selCombine.Count >= CombineCount())
            {
                SetMessage($"최대 {CombineCount()}개까지 선택할 수 있습니다.");
                return;
            }
            if (!MatchesCombineGroup(itemId))
            {
                SetMessage("같은 등급·클래스 장비만 함께 합성할 수 있습니다.");
                return;
            }
            _selCombine.Add(itemId);
            RebuildContent();
        }

        private void ToggleDismantle(long itemId)
        {
            SetMessage(string.Empty);
            if (_selDismantle.Contains(itemId))
            {
                _selDismantle.Remove(itemId);
            }
            else
            {
                _selDismantle.Add(itemId);
            }
            RebuildContent();
        }

        private void SelectRecipe(int recipeCode)
        {
            _selRecipe = recipeCode;
            SetMessage(string.Empty);
            RebuildContent();
        }

        /// <summary>강화 대상 선택(단일). 같은 장비를 다시 누르면 선택을 해제한다.</summary>
        private void SelectEnhance(long itemId)
        {
            _selEnhance = _selEnhance == itemId ? 0 : itemId;
            SetMessage(string.Empty);
            RebuildContent();
        }

        // ── 실행(서버 요청) ──

        private void OnAction()
        {
            if (_busy || NetworkManager.Instance == null || !Session.IsLoggedIn)
            {
                return;
            }
            switch (_mode)
            {
                case Mode.Combine: DoCombine(); break;
                case Mode.Dismantle: DoDismantle(); break;
                case Mode.Craft: DoCraft(); break;
                case Mode.Enhance: DoEnhance(); break;
            }
        }

        /// <summary>
        /// 장비 강화 요청(POST /api/game/inventory/enhance · 1회 = 1단계).
        /// <para>연출은 <b>요청 시작과 동시에 망치질</b>을 시작하고(응답을 기다리지 않는다 — 강화는 실패·하락이
        /// 없어 결과와 어긋날 위험이 없고, 왕복 지연 동안 무반응으로 느껴지는 것을 막는다), 성공 응답이 오면
        /// <b>망치질이 끝나는 시점</b>에 성공 버스트와 함께 화면을 갱신한다(사운드 리소스 정의서 §6).</para>
        /// </summary>
        private void DoEnhance()
        {
            var cand = FindEnhanceCandidate(_selEnhance);
            if (cand == null)
            {
                return;
            }
            var db = MasterDataManager.Db;
            if (db == null || db.NextEnhance(cand.Value.enhanceLevel) == null)
            {
                SetMessage("이미 최대 강화 단계입니다.");
                return;
            }

            long itemId = cand.Value.itemId;
            _busy = true;
            SetMessage(string.Empty);
            EnhanceFxOverlay.PlayHammer(enhanceHammerFrames, EnhanceFxScreenPos());

            var req = new EnhanceRequest
            {
                userId = Session.UserId,
                token = Session.Token,
                data = new EnhanceData { itemId = itemId },
            };
            Debug.Log($"[Cube] 강화 요청 itemId={itemId} current=+{cand.Value.enhanceLevel}");
            NetworkManager.Instance.PostToGame<EnhanceResponse>(
                "/api/game/inventory/enhance", req,
                resp =>
                {
                    var data = resp != null ? resp.data : null;
                    if (data != null)
                    {
                        // 캐시는 즉시 맞춘다(가방·장착 행의 강화 단계 + 골드 잔액). 화면 갱신만 연출에 맞춰 늦춘다.
                        Session.ApplyEnhanceResult(data);
                    }
                    int level = data != null ? data.enhanceLevel : 0;
                    int resultCode = cand.Value.itemCode;
                    EnhanceFxOverlay.PlaySuccess(enhanceBurstFrames, EnhanceFxScreenPos(), () =>
                    {
                        if (this == null || !gameObject.activeInHierarchy)
                        {
                            return; // 연출 도중 패널이 닫혔으면 갱신할 화면이 없다(캐시는 이미 반영됐다)
                        }
                        // 강화가 끝나면 대상 칸(왼쪽)을 비우고 결과를 오른쪽 칸에 보여 준다.
                        _selEnhance = 0;
                        _enhanceResultCode = resultCode;
                        _enhanceResultLevel = level;
                        RefreshAfterAction();               // 먼저 다시 그리고(단계·비용·잔액 갱신)
                        SetMessage($"강화 성공! +{level}"); // 그 위에 결과 메시지를 남긴다
                    });
                    _busy = false;
                },
                OnEnhanceError);
        }

        /// <summary>강화 실패: 망치질을 끊고 오류음을 낸 뒤 공통 실패 처리로 넘긴다(사운드 정의서 §9).</summary>
        private void OnEnhanceError(NetworkError error)
        {
            EnhanceFxOverlay.Cancel();
            SoundManager.Sfx(SoundId.UiError);
            OnActionError(error);
        }

        /// <summary>
        /// 강화 연출(망치질·성공 버스트)을 터뜨릴 화면 좌표 = <b>큐브 창 중앙</b>.
        /// 선택한 타일 위에서 터뜨리면 목록 스크롤 위치·칸 순서에 따라 연출이 화면 구석이나 잘린 칸에서 나와
        /// 크기가 큰 망치 이펙트가 창 밖으로 새어 나간다. 대상 장비는 하단 정보(이름·단계·비용)로 이미 알 수 있으므로
        /// 연출은 항상 같은 자리(창 중앙)에서 보여 준다.
        /// </summary>
        /// <summary>
        /// 망치 연출을 창 중앙에서 위로 얼마나 올릴지(스크린 픽셀).
        /// 강화 칸은 내용 영역 <b>위쪽</b>에 있는데 창 중앙은 그보다 한참 아래라, 그대로 두면 연출이
        /// 칸에서 떨어져 보인다. 창 중앙 → 강화 칸 중심까지의 거리를 캔버스 배율로 환산해 올린다.
        /// </summary>
        private float EnhanceFxLiftPx()
        {
            var target = _panelRoot != null ? _panelRoot : _rootRect;
            if (_contentArea == null || target == null)
            {
                return 0f;
            }
            // 창 중심 기준으로 본 강화 칸 중심의 y(캔버스 단위).
            float contentTopFromPanelCenter = target.rect.height * 0.5f - ContentTopMargin;
            float slotCenterY = contentTopFromPanelCenter - DropSlotTopMargin - _dropSlotSize * 0.5f;

            var canvas = GetComponent<Canvas>();
            float scale = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
            return slotCenterY * scale;
        }

        private Vector2 EnhanceFxScreenPos()
        {
            var target = _panelRoot != null ? _panelRoot : _rootRect;
            if (target == null)
            {
                return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            }
            // 창을 인벤토리 자리에 맞출 때 피벗을 오른쪽 변으로 옮기므로(AlignToInventoryPanel),
            // transform.position이 아니라 rect 중심을 변환해야 실제 창 중앙이 나온다.
            // 창 중앙은 강화 칸보다 아래라 망치가 칸을 벗어나 보인다 — 칸 높이 쪽으로 끌어올린다.
            Vector3 center = target.TransformPoint(target.rect.center);
            Vector3 sp = RectTransformUtility.WorldToScreenPoint(null, center);
            return new Vector2(sp.x, sp.y + EnhanceFxLiftPx());
        }

        /// <summary>큐브 합성 요청(POST /api/game/cube/combine). 성공 시 결과 안내 후 재로드.</summary>
        private void DoCombine()
        {
            if (_selCombine.Count != CombineCount())
            {
                return;
            }
            _busy = true;
            var req = new CubeCombineRequest
            {
                userId = Session.UserId,
                token = Session.Token,
                data = new CubeCombineData { itemIds = new List<long>(_selCombine) },
            };
            Debug.Log($"[Cube] 합성 요청 items={_selCombine.Count}");
            NetworkManager.Instance.PostToGame<CubeCombineResponse>(
                "/api/game/cube/combine", req,
                resp =>
                {
                    var data = resp != null ? resp.data : null;
                    string name = ItemName(data != null ? data.result.itemCode : 0);
                    int grade = data != null ? data.result.grade : 0;
                    SoundManager.Sfx(SoundId.CubeCombine); // 합성 성공음(사운드 정의서 §6)
                    ShowResult("합성 완료", $"{name} (등급 {grade}) 획득!");
                    if (data != null)
                    {
                        Session.ApplyInventoryDelta(data.inventoryDelta); // 입력 소모 + 결과 생성
                        Session.ApplyCube(data.cube);
                    }
                    RefreshAfterAction();
                },
                OnActionError);
        }

        /// <summary>큐브 분해 요청(POST /api/game/cube/dismantle). 성공 시 획득 골드 안내 후 재로드.</summary>
        private void DoDismantle()
        {
            if (_selDismantle.Count == 0)
            {
                return;
            }
            var items = new List<CubeDismantleItemDto>();
            foreach (var id in _selDismantle)
            {
                var it = FindInventoryItem(id);
                if (it != null)
                {
                    items.Add(new CubeDismantleItemDto { itemId = id, count = Mathf.Max(1, (int)it.quantity) });
                }
            }
            if (items.Count == 0)
            {
                return;
            }
            _busy = true;
            var req = new CubeDismantleRequest
            {
                userId = Session.UserId,
                token = Session.Token,
                data = new CubeDismantleData { items = items },
            };
            Debug.Log($"[Cube] 분해 요청 items={items.Count}");
            NetworkManager.Instance.PostToGame<CubeDismantleResponse>(
                "/api/game/cube/dismantle", req,
                resp =>
                {
                    var data = resp != null ? resp.data : null;
                    long gold = data != null ? data.gold : 0;
                    SoundManager.Sfx(SoundId.RewardGet); // 분해는 골드 획득이라 획득음을 쓴다(§8)
                    ShowResult("연금술 완료", $"골드 {GoldFormat.Highlight(gold)} 획득!");
                    if (data != null)
                    {
                        Session.ApplyInventoryDelta(data.inventoryDelta); // 분해한 아이템 제거·수량 차감
                        Session.ApplyCube(data.cube);
                        Session.ApplyBalance(data.balance);
                    }
                    RefreshAfterAction();
                },
                OnActionError);
        }

        /// <summary>큐브 제작 요청(POST /api/game/cube/craft). 성공 시 제작물 안내 후 재로드.</summary>
        private void DoCraft()
        {
            if (_selRecipe == 0)
            {
                return;
            }
            int recipeCode = _selRecipe;
            _busy = true;
            var req = new CubeCraftRequest
            {
                userId = Session.UserId,
                token = Session.Token,
                data = new CubeCraftData { recipeCode = recipeCode },
            };
            Debug.Log($"[Cube] 제작 요청 recipe={recipeCode}");
            NetworkManager.Instance.PostToGame<CubeCraftResponse>(
                "/api/game/cube/craft", req,
                resp =>
                {
                    var data = resp != null ? resp.data : null;
                    string gained = "제작 완료!";
                    if (data != null && data.gained != null
                        && data.gained.items != null && data.gained.items.Count > 0)
                    {
                        var g = data.gained.items[0];
                        gained = $"{ItemName(g.itemCode)} x{g.quantity} 제작!";
                    }
                    // 제작 성공음 + 골드 차감음(제작은 골드를 소모한다 — §6).
                    SoundManager.Sfx(SoundId.CubeCombine);
                    SoundManager.Sfx(SoundId.GoldSpend);
                    ShowResult("제작 완료", gained);
                    if (data != null)
                    {
                        Session.ApplyInventoryDelta(data.inventoryDelta); // 재료 차감 + 제작물 적재
                        Session.ApplyCube(data.cube);
                        Session.ApplyBalance(data.balance);
                    }
                    RefreshAfterAction();
                },
                OnActionError);
        }

        /// <summary>액션 응답(변경분·큐브·잔액)을 반영한 뒤 화면을 다시 그린다(선택 초기화, 재조회 없음).
        /// 합성/분해/제작이 가방을 어떻게 바꿨는지는 서버가 <c>inventoryDelta</c>로 알려준다(§5.0 규약).</summary>
        private void RefreshAfterAction()
        {
            _busy = false;
            _selCombine.Clear();
            _selDismantle.Clear();
            RefreshFromSession();
            Session.RaiseInventoryChanged(); // 인벤토리 변경 → 전투 스탯 재계산 트리거
        }

        /// <summary>가방 캐시가 서버와 어긋났을 때만 가방을 다시 받아 후보 목록을 맞춘다(코어는 받지 않는다).</summary>
        private void ReloadBagAndRefresh()
        {
            InventoryLoader.ReloadBag(RefreshAfterAction, OnBagLoadError);
        }

        /// <summary>큐브 액션 실패. 재료가 이미 사라진 경우(<see cref="ErrorCode.ItemNotFound"/>)는 페이징 이후
        /// 소모된 '유령' 행을 고른 것이므로 계약대로 가방을 새로 고친다(세이브 기획서 5.2).</summary>
        private void OnActionError(NetworkError error)
        {
            _busy = false;
            Debug.LogWarning($"[Cube] 액션 실패: {error}");
            SetMessage(ErrorMessages.ToKorean(error));
            if (error != null && error.ErrorCode == ErrorCode.ItemNotFound)
            {
                ReloadBagAndRefresh();
            }
        }

        private void ShowResult(string title, string message)
        {
            if (ModalManager.Instance != null)
            {
                ModalManager.Instance.ShowConfirm(title, message);
            }
        }

        // ── 파생/조회 ──

        /// <summary>계정 보유 골드(재화 타입 1).</summary>
        private static long CurrentGold()
        {
            var currencies = Session.GameData != null ? Session.GameData.currencies : null;
            if (currencies != null)
            {
                foreach (var c in currencies)
                {
                    if (c != null && c.currencyType == GoldCurrencyType)
                    {
                        return c.amount;
                    }
                }
            }
            return 0;
        }

        private static int CubeLevel()
        {
            var cube = Session.GameData != null ? Session.GameData.cube : null;
            return cube != null && cube.cubeLevel > 0 ? cube.cubeLevel : 1;
        }

        private static long CubeExp()
        {
            var cube = Session.GameData != null ? Session.GameData.cube : null;
            return cube != null ? cube.cubeExp : 0;
        }

        /// <summary>현재 큐브 레벨의 합성 소모 개수(마스터, 폴백 3).</summary>
        private static int CombineCount()
        {
            var db = MasterDataManager.Db;
            if (db != null && db.Cubes.TryGetValue(CubeLevel(), out var cm) && cm.combineCount > 0)
            {
                return cm.combineCount;
            }
            return 3;
        }

        /// <summary>현재 큐브 레벨의 분해 골드 계수(마스터, 폴백 100).</summary>
        private static long GoldPerScrap()
        {
            var db = MasterDataManager.Db;
            if (db != null && db.Cubes.TryGetValue(CubeLevel(), out var cm) && cm.goldPerScrap > 0)
            {
                return cm.goldPerScrap;
            }
            return 100;
        }

        /// <summary>합성 후보: 가방 장비(item_type=1) 중 등급 1~4(등급 5는 상위 없음). 가방 캐시는 장착품을 포함하지 않는다.</summary>
        private static IEnumerable<InventoryItemDto> EligibleCombineItems()
        {
            var bag = Session.Bag;
            var db = MasterDataManager.Db;
            if (bag == null || db == null)
            {
                yield break;
            }
            foreach (var it in bag)
            {
                if (it == null)
                {
                    continue;
                }
                if (db.Items.TryGetValue(it.itemCode, out var im) && im.itemType == 1 && im.grade >= 1 && im.grade < 5)
                {
                    yield return it;
                }
            }
        }

        /// <summary>분해 후보: 가방 아이템(장비·재료). 재화는 코어 로드의 currencies로 분리돼 가방에 없다.</summary>
        private static IEnumerable<InventoryItemDto> EligibleDismantleItems()
        {
            var bag = Session.Bag;
            var db = MasterDataManager.Db;
            if (bag == null || db == null)
            {
                yield break;
            }
            foreach (var it in bag)
            {
                if (it == null)
                {
                    continue;
                }
                if (db.Items.TryGetValue(it.itemCode, out var im) && (im.itemType == 1 || im.itemType == 2))
                {
                    yield return it;
                }
            }
        }

        /// <summary>강화 후보 한 개(가방 장비 또는 장착 중 장비). 두 목록의 DTO가 서로 달라 공통 필드만 모아 쓴다.</summary>
        private struct EnhanceCandidate
        {
            public long itemId;
            public int itemCode;
            public int enhanceLevel;
            public bool equipped;   // 장착 중(해제 없이 강화 가능 — 기획서 §5.3)
        }

        /// <summary>
        /// 강화 후보: 가방의 장비(item_type=1) + <b>장착 중인 장비</b>(코어 로드의 equipped).
        /// 장착 장비는 가방 칸을 반납해 가방 캐시에 없으므로 따로 훑어야 한다(그렇지 않으면 강화할 수 있는 장비가
        /// 목록에서 빠진다 — 실제로 쓰는 장비가 대부분 장착 중이다).
        /// </summary>
        private static IEnumerable<EnhanceCandidate> EligibleEnhanceItems()
        {
            var db = MasterDataManager.Db;
            if (db == null)
            {
                yield break;
            }

            var equipped = Session.Equipped;
            if (equipped != null)
            {
                foreach (var it in equipped)
                {
                    if (it != null && db.Items.TryGetValue(it.itemCode, out var em) && em.itemType == 1)
                    {
                        yield return new EnhanceCandidate
                        {
                            itemId = it.itemId,
                            itemCode = it.itemCode,
                            enhanceLevel = it.enhanceLevel,
                            equipped = true,
                        };
                    }
                }
            }

            var bag = Session.Bag;
            if (bag != null)
            {
                foreach (var it in bag)
                {
                    if (it != null && db.Items.TryGetValue(it.itemCode, out var im) && im.itemType == 1)
                    {
                        yield return new EnhanceCandidate
                        {
                            itemId = it.itemId,
                            itemCode = it.itemCode,
                            enhanceLevel = it.enhanceLevel,
                            equipped = false,
                        };
                    }
                }
            }
        }

        /// <summary>선택된 강화 후보를 찾는다(선택이 없거나 목록에서 사라졌으면 null).</summary>
        private static EnhanceCandidate? FindEnhanceCandidate(long itemId)
        {
            if (itemId == 0)
            {
                return null;
            }
            foreach (var cand in EligibleEnhanceItems())
            {
                if (cand.itemId == itemId)
                {
                    return cand;
                }
            }
            return null;
        }

        /// <summary>선택된 분해 아이템의 예상 획득 골드 합계(gold_per_scrap × 등급 × 개수, 서버가 최종 확정).</summary>
        private long EstimateDismantleGold()
        {
            long perScrap = GoldPerScrap();
            var db = MasterDataManager.Db;
            long total = 0;
            foreach (var id in _selDismantle)
            {
                var it = FindInventoryItem(id);
                if (it != null && db != null && db.Items.TryGetValue(it.itemCode, out var im))
                {
                    total += perScrap * im.grade * Mathf.Max(1, (int)it.quantity);
                }
            }
            return total;
        }

        /// <summary>선택 후보가 이미 선택된 합성 그룹(첫 아이템의 등급·슬롯·클래스)과 일치하는지.</summary>
        private bool MatchesCombineGroup(long itemId)
        {
            if (_selCombine.Count == 0)
            {
                return true;
            }
            var db = MasterDataManager.Db;
            var first = FindInventoryItem(FirstSelectedCombineId());
            var cand = FindInventoryItem(itemId);
            if (first == null || cand == null || db == null)
            {
                return false;
            }
            if (!db.Items.TryGetValue(first.itemCode, out var fm) || !db.Items.TryGetValue(cand.itemCode, out var cm))
            {
                return false;
            }
            // 합성은 같은 등급·클래스면 되고 슬롯은 서로 달라도 된다(inventory-item-cube 기획서 §5.6).
            return fm.grade == cm.grade && fm.classReq == cm.classReq;
        }

        private long FirstSelectedCombineId() => _selCombine.Count > 0 ? _selCombine[0] : 0;

        private int FirstSelectedCombineCode()
        {
            var it = FindInventoryItem(FirstSelectedCombineId());
            return it != null ? it.itemCode : 0;
        }

        /// <summary>재료 코드의 계정 보유 총 수량(가방의 같은 코드 행 합산).</summary>
        private static int MaterialCount(int itemCode)
        {
            var bag = Session.Bag;
            int total = 0;
            if (bag != null)
            {
                foreach (var it in bag)
                {
                    if (it != null && it.itemCode == itemCode)
                    {
                        total += (int)it.quantity;
                    }
                }
            }
            return total;
        }

        /// <summary>가방 캐시에서 아이템 행을 찾는다(선택 목록은 itemId로만 들고 있다).</summary>
        private static InventoryItemDto FindInventoryItem(long itemId)
        {
            var bag = Session.Bag;
            if (bag != null)
            {
                foreach (var it in bag)
                {
                    if (it != null && it.itemId == itemId)
                    {
                        return it;
                    }
                }
            }
            return null;
        }

        private static string ItemName(int itemCode)
        {
            var db = MasterDataManager.Db;
            if (db != null && db.Items.TryGetValue(itemCode, out var im))
            {
                return im.name;
            }
            return itemCode.ToString();
        }

        /// <summary>슬롯 우하단 수량 표기("xN", 1개면 표기 없음). 강화 단계는 공용 슬롯 배지가 그린다.</summary>
        private static string QuantityBadge(InventoryItemDto it)
            => it != null && it.quantity > 1 ? $"x{it.quantity}" : string.Empty;

        // ── 그리드/타일 ──

        /// <summary>내용 영역에 세로 스크롤 그리드를 만들고 아이템 타일 부모(content)를 돌려준다.</summary>
        private RectTransform BuildScrollGrid()
        {
            var viewport = NewImage("Viewport", _contentArea, null);
            viewport.color = new Color(0f, 0f, 0f, 0.001f);
            Stretch(viewport.rectTransform);
            viewport.rectTransform.offsetMin = new Vector2(12f, 12f);
            viewport.rectTransform.offsetMax = new Vector2(-12f, -12f);
            viewport.gameObject.AddComponent<RectMask2D>();
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;
            _contentChildren.Add(viewport.gameObject);

            var content = NewRect("Content", viewport.rectTransform);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, 0f);

            var grid = content.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(126f, 150f);
            grid.spacing = new Vector2(12f, 12f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = GridColumns;
            grid.childAlignment = TextAnchor.UpperCenter;

            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewport.rectTransform;
            scroll.content = content;
            return content;
        }

        /// <summary>
        /// 아이템/레시피 타일 하나를 만든다. <b>아이템 칸(아이콘·등급 배경·수량·강화 배지)은 공용 슬롯
        /// 프리팹(<see cref="ItemSlotView"/>)이 그린다</b> — 인벤토리·거래소·우편함과 같은 외형·같은 강화 표시
        /// (좌측 하단 흰 "+N")를 쓰기 위한 통일 지점이다. 큐브 고유 표시는 슬롯 밖/위에 얹는다:
        /// 아래쪽 <b>이름</b>, 우상단 <b>태그</b>(레시피 "Lv.3" · 강화 후보의 "장착"), 선택 프레임.
        /// <para><paramref name="enhanceLevel"/>은 슬롯 배지로, <paramref name="quantityText"/>는 슬롯 우하단
        /// 수량으로 넘어간다(이전에는 이 셋을 하나의 배지 문자열로 합쳐 우상단에 금색으로 찍고 있었다).</para>
        /// 돌려주는 RectTransform은 타일 컨테이너다(강화 연출을 그 위에서 터뜨리기 위해 위치가 필요하다).
        /// </summary>
        private RectTransform CreateItemTile(RectTransform parent, int itemCode, int enhanceLevel,
            string quantityText, string tag, bool selected, System.Action onClick)
        {
            var db = MasterDataManager.Db;
            db.Items.TryGetValue(itemCode, out var im);
            int grade = im != null ? im.grade : 1;
            string itemName = im != null ? im.name : itemCode.ToString();

            // 타일 컨테이너: 그림 없는 투명 판(클릭 대상). 그림은 공용 슬롯이 그린다.
            var tile = NewImage($"Tile_{itemCode}", parent, null);
            tile.color = new Color(0f, 0f, 0f, 0f);

            // 공용 슬롯 — 이름 줄(아래 36px)을 남기고 그 위 영역을 채운다.
            var slotView = CreateSlot(tile.rectTransform, itemCode, enhanceLevel, quantityText);

            var name = NewText("Name", tile.rectTransform, itemName, 18, TextAnchor.LowerCenter);
            name.color = GradeColors.Name(grade);
            name.raycastTarget = false;
            var nrt = name.rectTransform;
            nrt.anchorMin = new Vector2(0f, 0f);
            nrt.anchorMax = new Vector2(1f, 0f);
            nrt.pivot = new Vector2(0.5f, 0f);
            nrt.offsetMin = new Vector2(2f, 4f);
            nrt.offsetMax = new Vector2(-2f, 36f);
            name.horizontalOverflow = HorizontalWrapMode.Wrap;

            // 태그(우상단): 강화 단계가 아니라 큐브 화면 고유 정보다 — 레시피 요구 큐브 레벨, 장착 중 표시.
            if (!string.IsNullOrEmpty(tag))
            {
                var b = NewText("Tag", tile.rectTransform, tag, 20, TextAnchor.UpperRight);
                b.fontStyle = FontStyle.Bold;
                b.color = new Color(1f, 0.9f, 0.5f);
                b.raycastTarget = false;
                var brt = b.rectTransform;
                brt.anchorMin = new Vector2(0f, 1f);
                brt.anchorMax = new Vector2(1f, 1f);
                brt.pivot = new Vector2(1f, 1f);
                brt.offsetMin = new Vector2(2f, -34f);
                brt.offsetMax = new Vector2(-6f, -4f);
            }

            if (selected)
            {
                var frame = NewImage("Sel", tile.rectTransform, slotHighlight);
                frame.raycastTarget = false;
                Stretch(frame.rectTransform);
                frame.rectTransform.offsetMin = new Vector2(-4f, -4f);
                frame.rectTransform.offsetMax = new Vector2(4f, 4f);
            }

            var btn = tile.gameObject.AddComponent<Button>();
            btn.onClick.AddListener(() => onClick());
            if (slotView == null)
            {
                // 프리팹 미배선 폴백: 등급 배경만이라도 칸으로 보이게 한다(경고는 CreateSlot이 남긴다).
                tile.sprite = slotNormal;
                tile.color = GradeColors.RewardSlotBackground(grade);
            }
            return tile.rectTransform;
        }

        /// <summary>
        /// 타일 안에 공용 아이템 슬롯 프리팹을 붙이고 구성한다(이름 줄 위 영역).
        /// hover 상세 팝업·레이캐스트는 끈다 — 타일 전체가 선택 버튼이므로 슬롯이 클릭을 가로채면 안 된다.
        /// 프리팹이 배선되지 않았으면 경고를 남기고 null을 돌려준다.
        /// </summary>
        private ItemSlotView CreateSlot(RectTransform tile, int itemCode, int enhanceLevel, string quantityText)
        {
            if (_itemSlotPrefab == null)
            {
                Debug.LogWarning("[Cube] 공용 아이템 슬롯 프리팹이 배선되지 않았습니다. " +
                                 "메뉴 'TaskbarHero/UI/아이템 슬롯·상세 팝업 배선'을 실행하세요.");
                return null;
            }
            var go = Instantiate(_itemSlotPrefab, tile);
            go.name = "ItemSlot";
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(0f, 36f); // 아래 36px은 이름 줄
            rt.offsetMax = Vector2.zero;
            var view = go.GetComponent<ItemSlotView>();
            if (view != null)
            {
                view.Setup(itemCode, 0L, quantityText ?? string.Empty, false);
                view.SetEnhanceLevel(enhanceLevel);
            }
            return view;
        }

        private void ShowEmptyHint(string text)
        {
            var t = NewText("Empty", _contentArea, text, 28, TextAnchor.MiddleCenter);
            t.color = new Color(0.7f, 0.72f, 0.8f);
            Stretch(t.rectTransform);
            _contentChildren.Add(t.gameObject);
        }

        private void ClearContent()
        {
            foreach (var go in _contentChildren)
            {
                if (go != null)
                {
                    Destroy(go);
                }
            }
            _contentChildren.Clear();
        }

        private void SetFooter(string text)
        {
            if (_footerText != null)
            {
                _footerText.text = text ?? string.Empty;
            }
        }

        private void SetAction(string label, bool interactable)
        {
            if (_actionLabel != null)
            {
                _actionLabel.text = label;
            }
            if (_actionButton != null)
            {
                _actionButton.interactable = interactable;
                var img = _actionButton.GetComponent<Image>();
                if (img != null)
                {
                    img.color = interactable ? new Color(0.42f, 0.28f, 0.16f, 0.98f) : new Color(0.22f, 0.22f, 0.28f, 0.9f);
                }
            }
        }

        private void SetMessage(string message)
        {
            if (_messageText != null)
            {
                _messageText.text = message ?? string.Empty;
            }
        }

        /// <summary>패널을 닫는다(UIManager 우선).</summary>
        public void Close()
        {
            if (UIManager.Instance != null)
            {
                UIManager.Instance.Hide(UIManager.PanelType.Cube);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }

        // ── UI 생성 헬퍼(RunePanelController와 동일 규약) ──

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

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static void TopLeft(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }
    }
}
