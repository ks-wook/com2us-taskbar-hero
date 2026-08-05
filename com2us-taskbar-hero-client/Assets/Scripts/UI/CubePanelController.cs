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
        [SerializeField] private Sprite panelBackground; // cube_bg
        [SerializeField] private Sprite slotNormal;
        [SerializeField] private Sprite slotHighlight;

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
        private const float ExpTrackWidth = 520f;
        private const int GridColumns = 5;
        // 탭 4개(합성·연금술·제작·강화)를 내용 영역 폭(x 40 ~ 780) 안에 균등 배치하는 값.
        private const float TabWidth = 170f;
        private const float TabGap = 20f;

        // 내용 영역(아이템 그리드) 여백. 높이를 고정값으로 두면 PanelRoot 높이를 줄였을 때 아래로 넘쳐
        // 실행바·문구를 덮거나(넘침) 반대로 0에 가깝게 눌려 아무것도 안 보이므로, 위·아래·좌우 여백만 정하고
        // **PanelRoot 크기에 맞춰 늘어나도록** 앵커로 잡는다(사용자가 프리팹에서 패널 크기를 조절해도 따라온다).
        private const float ContentSideMargin = 40f;   // 좌우 여백(폭 820 기준 내용 폭 740)
        private const float ContentTopMargin = 260f;   // 제목·탭·큐브 레벨바가 차지하는 상단
        private const float ContentBottomMargin = 260f; // 실행 버튼(150~246)·하단 문구가 차지하는 하단

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
            AlignToInventoryPanel(); // 방금 보고 있던 인벤토리 창 자리에 띄운다
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
            BuildHeader(container);
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
            rt.sizeDelta = new Vector2(820f, 1400f);
            rt.anchoredPosition = Vector2.zero;
            _panelRoot = rt;
            return rt;
        }

        /// <summary>
        /// 큐브 패널을 <b>인벤토리 패널이 있던 자리</b>에 맞춘다(표시할 때마다). 큐브는 인벤토리의 '큐브' 버튼으로만
        /// 열리므로, 화면 중앙에 뜨면 방금 보고 있던 창에서 시선이 크게 튄다.
        /// <para>기준 창의 <b>오른쪽 변·세로 중심</b>에 맞춘다 — 인벤토리 창은 화면 오른쪽에 붙어 있고 큐브 창이 더
        /// 넓으므로, 중심을 맞추면 오른쪽이 화면 밖으로 밀린다.</para>
        /// <para>인벤토리 인스턴스가 아직 없으면(단독 호출) 프리팹에 구워진 중앙 배치를 그대로 둔다.
        /// 두 패널 모두 ScreenSpaceOverlay 캔버스라 월드 좌표가 곧 화면 픽셀이고, 화면 픽셀을 이 패널 캔버스의
        /// 로컬 좌표로 되돌려 배치한다(두 캔버스의 배율이 같아도 좌표계를 직접 가정하지 않는다).</para>
        /// </summary>
        private void AlignToInventoryPanel()
        {
            if (_panelRoot == null || _rootRect == null || UIManager.Instance == null)
            {
                return;
            }
            var reference = UIManager.Instance.FindPanelRoot(UIManager.PanelType.Inventory);
            if (reference == null || reference == _panelRoot)
            {
                return;
            }

            var corners = new Vector3[4]; // 0=좌하 1=좌상 2=우상 3=우하
            reference.GetWorldCorners(corners);
            var rightCenter = new Vector2(corners[2].x, (corners[0].y + corners[2].y) * 0.5f);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_rootRect, rightCenter, null, out var local))
            {
                return;
            }

            _panelRoot.anchorMin = _panelRoot.anchorMax = new Vector2(0.5f, 0.5f);
            _panelRoot.pivot = new Vector2(1f, 0.5f); // 오른쪽 변 기준
            _panelRoot.anchoredPosition = local;
        }

        /// <summary>제목(중앙) + 보유 골드(좌상단) + 닫기(우상단).</summary>
        private void BuildHeader(RectTransform container)
        {
            var title = NewText("Title", container, "큐브", 46, TextAnchor.UpperCenter);
            title.fontStyle = FontStyle.Bold;
            var trt = title.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 1f);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.anchoredPosition = new Vector2(0f, -28f);
            trt.sizeDelta = new Vector2(400f, 60f);

            var goldBg = NewImage("GoldArea", container, null);
            goldBg.color = new Color(0f, 0f, 0f, 0.35f);
            TopLeft(goldBg.rectTransform, 28f, 26f, 300f, 60f);
            var gi = NewImage("GoldIcon", goldBg.rectTransform, null);
            gi.raycastTarget = false;
            gi.preserveAspect = true;
            TopLeft(gi.rectTransform, 8f, 6f, 48f, 48f);
            _goldIcon = gi;
            _goldText = NewText("GoldText", goldBg.rectTransform, "0", 30, TextAnchor.MiddleLeft);
            TopLeft(_goldText.rectTransform, 64f, 8f, 224f, 44f);

            var closeImg = NewImage("CloseButton", container, slotNormal);
            var crt = closeImg.rectTransform;
            crt.anchorMin = crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(1f, 1f);
            crt.anchoredPosition = new Vector2(-28f, -26f);
            crt.sizeDelta = new Vector2(72f, 72f);
            var x = NewText("X", crt, "X", 36, TextAnchor.MiddleCenter);
            Stretch(x.rectTransform);
            _closeButton = closeImg.gameObject.AddComponent<Button>();
        }

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
            var img = NewImage(name, container, slotNormal);
            TopLeft(img.rectTransform, x, 108f, TabWidth, 64f);
            var t = NewText("Label", img.rectTransform, label, 30, TextAnchor.MiddleCenter);
            t.fontStyle = FontStyle.Bold;
            Stretch(t.rectTransform);
            return img.gameObject.AddComponent<Button>();
        }

        /// <summary>큐브 레벨·경험치 진행바.</summary>
        private void BuildLevelBar(RectTransform container)
        {
            var bg = NewImage("CubeLevelBar", container, null);
            bg.color = new Color(0f, 0f, 0f, 0.35f);
            TopLeft(bg.rectTransform, 40f, 188f, 740f, 56f);

            _levelText = NewText("CubeLevel", bg.rectTransform, "Lv.1", 28, TextAnchor.MiddleLeft);
            _levelText.fontStyle = FontStyle.Bold;
            TopLeft(_levelText.rectTransform, 16f, 10f, 150f, 36f);

            var track = NewImage("ExpTrack", bg.rectTransform, null);
            track.color = new Color(0f, 0f, 0f, 0.55f);
            TopLeft(track.rectTransform, 176f, 16f, ExpTrackWidth, 24f);
            var fill = NewImage("ExpFill", track.rectTransform, null);
            fill.color = new Color(0.35f, 0.72f, 0.4f, 0.95f);
            fill.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            fill.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            fill.rectTransform.pivot = new Vector2(0f, 0.5f);
            fill.rectTransform.anchoredPosition = Vector2.zero;
            fill.rectTransform.sizeDelta = new Vector2(ExpTrackWidth, 24f);
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
            var btn = NewImage("ActionButton", container, slotNormal);
            btn.color = new Color(0.42f, 0.28f, 0.16f, 0.98f);
            var brt = btn.rectTransform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0f);
            brt.pivot = new Vector2(0.5f, 0f);
            brt.anchoredPosition = new Vector2(0f, 150f);
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
            frt.anchoredPosition = new Vector2(0f, 44f);

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
            if (_closeButton != null) _closeButton.onClick.AddListener(Close);
            if (_dimButton != null) _dimButton.onClick.AddListener(Close);
            if (_combineTab != null) _combineTab.onClick.AddListener(() => SwitchMode(Mode.Combine));
            if (_dismantleTab != null) _dismantleTab.onClick.AddListener(() => SwitchMode(Mode.Dismantle));
            if (_craftTab != null) _craftTab.onClick.AddListener(() => SwitchMode(Mode.Craft));
            if (_enhanceTab != null) _enhanceTab.onClick.AddListener(() => SwitchMode(Mode.Enhance));
            if (_actionButton != null) _actionButton.onClick.AddListener(OnAction);
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
                _expFill.sizeDelta = new Vector2(ExpTrackWidth * ratio, _expFill.sizeDelta.y);
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

        /// <summary>합성: 미장착 장비(등급 1~4) 그리드. 같은 등급·클래스면 combine_count개까지 선택(슬롯 무관).</summary>
        private void BuildCombineContent()
        {
            var content = BuildScrollGrid();
            var db = MasterDataManager.Db;
            int count = 0;
            foreach (var it in EligibleCombineItems())
            {
                bool selected = _selCombine.Contains(it.itemId);
                long id = it.itemId;
                CreateItemTile(content, it.itemCode, EnhanceBadge(it), selected, () => ToggleCombine(id));
                count++;
            }
            if (count == 0)
            {
                ShowEmptyHint("합성할 수 있는 장비가 없습니다.");
            }

            int combineCount = CombineCount();
            if (_selCombine.Count > 0 && db.Items.TryGetValue(FirstSelectedCombineCode(), out var fm))
            {
                db.Grades.TryGetValue(fm.grade, out var g);
                string gradeName = g != null ? g.name : fm.grade.ToString();
                SetFooter($"합성 등급 {gradeName} → 등급 {fm.grade + 1} · 선택 {_selCombine.Count}/{combineCount}");
            }
            else
            {
                SetFooter($"같은 등급·클래스 장비 {combineCount}개를 선택하세요(슬롯 무관).");
            }
            SetAction("합성", _selCombine.Count == combineCount);
        }

        /// <summary>연금술(분해): 미장착 아이템(장비·재료) 그리드. 선택분을 골드로 전환.</summary>
        private void BuildDismantleContent()
        {
            var content = BuildScrollGrid();
            int count = 0;
            foreach (var it in EligibleDismantleItems())
            {
                bool selected = _selDismantle.Contains(it.itemId);
                long id = it.itemId;
                CreateItemTile(content, it.itemCode, QuantityBadge(it), selected, () => ToggleDismantle(id));
                count++;
            }
            if (count == 0)
            {
                ShowEmptyHint("분해할 수 있는 아이템이 없습니다.");
            }

            long gold = EstimateDismantleGold();
            SetFooter($"연금술 작동 시 획득 골드: {GoldFormat.Highlight(gold)}");
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
                CreateItemTile(content, r.resultItemCode, $"Lv.{r.reqCubeLevel}", selected, () => SelectRecipe(code));
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

        /// <summary>강화: 장비 그리드(가방 + <b>장착 중</b>). 한 개만 선택하며 하단에 다음 단계·비용·스탯 변화를 안내한다.
        /// 장착 중인 장비도 해제 없이 강화할 수 있으므로(기획서 §5.3) 후보에 함께 올리고 '장착' 표시를 붙인다.</summary>
        private void BuildEnhanceContent()
        {
            var content = BuildScrollGrid();
            int count = 0;
            foreach (var cand in EligibleEnhanceItems())
            {
                bool selected = cand.itemId == _selEnhance;
                long id = cand.itemId;
                CreateItemTile(content, cand.itemCode,
                    EnhanceTileBadge(cand.enhanceLevel, cand.equipped), selected, () => SelectEnhance(id));
                count++;
            }
            if (count == 0)
            {
                ShowEmptyHint("강화할 장비가 없습니다.");
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
                SetFooter("강화할 장비를 선택하세요.");
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
                    EnhanceFxOverlay.PlaySuccess(enhanceBurstFrames, EnhanceFxScreenPos(), () =>
                    {
                        if (this == null || !gameObject.activeInHierarchy)
                        {
                            return; // 연출 도중 패널이 닫혔으면 갱신할 화면이 없다(캐시는 이미 반영됐다)
                        }
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
        private Vector2 EnhanceFxScreenPos()
        {
            var target = _panelRoot != null ? _panelRoot : _rootRect;
            if (target == null)
            {
                return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            }
            // 창을 인벤토리 자리에 맞출 때 피벗을 오른쪽 변으로 옮기므로(AlignToInventoryPanel),
            // transform.position이 아니라 rect 중심을 변환해야 실제 창 중앙이 나온다.
            Vector3 sp = RectTransformUtility.WorldToScreenPoint(null, target.TransformPoint(target.rect.center));
            return new Vector2(sp.x, sp.y);
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

        /// <summary>강화 후보 타일의 배지 문구("장착 +3" / "+3" / "장착").</summary>
        private static string EnhanceTileBadge(int enhanceLevel, bool equipped)
        {
            string level = enhanceLevel > 0 ? $"+{enhanceLevel}" : string.Empty;
            if (!equipped)
            {
                return level;
            }
            return string.IsNullOrEmpty(level) ? "장착" : $"장착 {level}";
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

        private static string EnhanceBadge(InventoryItemDto it)
            => it != null && it.enhanceLevel > 0 ? $"+{it.enhanceLevel}" : string.Empty;

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

        /// <summary>아이템/레시피 타일 하나(등급 배경·아이콘·이름·배지·선택 프레임·클릭)를 만든다.
        /// 만들어진 타일의 RectTransform을 돌려준다(강화 연출을 그 위에서 터뜨리기 위해 위치가 필요하다).</summary>
        private RectTransform CreateItemTile(RectTransform parent, int itemCode, string badge, bool selected, System.Action onClick)
        {
            var db = MasterDataManager.Db;
            db.Items.TryGetValue(itemCode, out var im);
            int grade = im != null ? im.grade : 1;
            string itemName = im != null ? im.name : itemCode.ToString();

            var tile = NewImage($"Tile_{itemCode}", parent, slotNormal);
            tile.color = GradeColors.RewardSlotBackground(grade);

            var icon = NewImage("Icon", tile.rectTransform, null);
            icon.raycastTarget = false;
            icon.preserveAspect = true;
            var irt = icon.rectTransform;
            irt.anchorMin = new Vector2(0f, 0f);
            irt.anchorMax = new Vector2(1f, 1f);
            irt.pivot = new Vector2(0.5f, 0.5f);
            irt.offsetMin = new Vector2(14f, 40f);
            irt.offsetMax = new Vector2(-14f, -8f);
            var sp = _iconDb != null ? _iconDb.Get(itemCode) : null;
            if (sp != null)
            {
                icon.sprite = sp;
                icon.color = Color.white;
            }
            else
            {
                icon.color = GradeColors.IconFallback(grade);
            }

            var name = NewText("Name", tile.rectTransform, itemName, 18, TextAnchor.LowerCenter);
            name.color = GradeColors.Name(grade);
            var nrt = name.rectTransform;
            nrt.anchorMin = new Vector2(0f, 0f);
            nrt.anchorMax = new Vector2(1f, 0f);
            nrt.pivot = new Vector2(0.5f, 0f);
            nrt.offsetMin = new Vector2(2f, 4f);
            nrt.offsetMax = new Vector2(-2f, 36f);
            name.horizontalOverflow = HorizontalWrapMode.Wrap;

            if (!string.IsNullOrEmpty(badge))
            {
                var b = NewText("Badge", tile.rectTransform, badge, 20, TextAnchor.UpperRight);
                b.fontStyle = FontStyle.Bold;
                b.color = new Color(1f, 0.9f, 0.5f);
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
            return tile.rectTransform;
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
