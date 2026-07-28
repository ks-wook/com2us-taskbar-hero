using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TaskbarHero.Client.Managers;
using TaskbarHero.Client.MasterData;
using TaskbarHero.Common.Dto;

namespace TaskbarHero.Client.UI.Trade
{
    /// <summary>
    /// 거래소(교역선) 패널(trade 기획서 §2·§5). 상단 탭 세 개로 구성된다.
    /// <list type="bullet">
    /// <item><b>판매 목록</b> — 구매 가능한 등록(<c>mine=false</c>: 서버가 본인 등록을 제외해 준다)을
    ///   아이템 코드로 검색·페이징해 보여주고(가격 오름차순) '구매' 버튼을 붙인다.</item>
    /// <item><b>판매 등록</b> — 인벤토리의 판매 가능(<c>sellable=1</c>·미장착) 아이템을 골라 가격을 정해 등록한다.
    ///   가격 범위(기준가 ±20%)는 클라이언트가 먼저 안내하되, 최종 판정은 서버가 한다.</item>
    /// <item><b>판매 현황</b> — 내가 등록한 매물(<c>mine=true</c>)만 모아 '취소' 버튼을 붙인다.
    ///   자기 등록은 구매할 수 없으므로(<c>TradeSelfPurchase</c>) 취소는 이 탭에서만 가능하다.</item>
    /// </list>
    /// <b>구매한 아이템은 인벤토리가 아니라 우편함으로 발급된다</b>(기획서 §5.3) — 구매 시점에는 골드만 차감되고,
    /// 인벤토리 반영은 우편함 수령 시점에 이뤄진다(가방이 가득해도 거래는 성립한다).
    /// 구매 결과는 별도 연출 없이 '확인' 버튼 모달로만 알린다.
    /// 검색은 서버가 <b>아이템 코드</b>로만 받으므로, 이름 검색은 클라이언트가 마스터 데이터에서
    /// 이름 → itemCode로 변환해 보낸다(기획서 §3). 가격·소유권·정산은 모두 서버 권위다.
    /// 외형은 <c>Assets/Art/UI/Trade</c>의 픽셀 UI 키트(9-slice 프레임·버튼·등급 슬롯)를 쓴다.
    /// 정적 계층은 에디터 빌더(TradeUiBuilder)가 프리팹에 굽고, 목록 행은 조회 결과로 런타임에 생성한다.
    /// </summary>
    public class TradePanelController : MonoBehaviour
    {
        private const float CanvasRefWidth = 1080f;
        private const float CanvasRefHeight = 1920f;
        // 행 내부 열 좌표(RowActionX + RowActionWidth = 990)가 잘리지 않을 만큼 폭을 잡는다.
        // 행 폭 = PanelWidth − 목록 배경 여백(40×2) − 콘텐츠 여백(10×2) = PanelWidth − 100.
        private const float PanelWidth = 1120f;   // → 행 폭 1020, '구매' 버튼 오른쪽 끝 990 (여유 30)
        private const float PanelHeight = 1180f;
        private const float ListInset = 40f;      // 창 안쪽 목록 영역 좌우 여백
        private const float RowActionX = 860f;    // 구매/취소·선택 버튼 열 시작
        private const float RowActionWidth = 130f;
        private const float SearchRowY = 178f;    // 창 상단에서 검색줄까지
        private const float SearchButtonWidth = 160f;
        private const float TabWidth = 230f;      // 상단 탭 버튼 폭
        private const float TabGap = 12f;         // 탭 사이 간격
        private const float RowHeight = 96f;
        // 한 페이지 행 수는 목록 뷰포트 안에 다 들어가는 값으로 잡는다(스크롤 없이 한눈에 보이게).
        // 뷰포트 676 = 패널 1180 − 아래 200(페이지·메시지) − 위 304(헤더까지).
        // 6행 = 6×96 + 5×8(간격) + 20(위아래 여백) = 636 ≤ 676.
        private const int PageSize = 6;
        private const int GoldCurrencyType = 1;  // 재화 타입 1 = 골드
        private const int GoldItemCode = 1;      // item_master 골드 코드(아이콘 item_1)
        private const float PriceMinRate = 0.8f; // 기준가 ±20%(기획서 §3) — 안내용, 최종 판정은 서버
        private const float PriceMaxRate = 1.2f;

        [Header("UI 리소스 (Assets/Art/UI/Trade — 에디터 빌더가 배선)")]
        [SerializeField] private Sprite _windowFrame;      // window_frame
        [SerializeField] private Sprite _panelParchment;   // panel_parchment (목록 배경)
        [SerializeField] private Sprite _panelWood;        // panel_wood (하단 폼)
        [SerializeField] private Sprite _tableHeader;      // bar_table_header
        [SerializeField] private Sprite _rowNormal;        // row_normal
        [SerializeField] private Sprite _rowAlt;           // row_alt
        [SerializeField] private Sprite _fieldSearch;      // field_search (입력창)
        [SerializeField] private Sprite _bannerRibbon;     // banner_ribbon (타이틀)
        [SerializeField] private Sprite _dividerGold;      // divider_gold
        [SerializeField] private Sprite _btnGold;          // btn_gold_normal (구매·등록)
        [SerializeField] private Sprite _btnRed;           // btn_red_normal (취소)
        [SerializeField] private Sprite _btnBlue;          // btn_blue_normal (검색)
        [SerializeField] private Sprite _btnWood;          // btn_wood_normal (페이지)
        [SerializeField] private Sprite _btnClose;         // btn_close_normal
        [SerializeField] private Sprite _btnCategory;      // btn_category_normal (탭)
        [SerializeField] private Sprite _btnCategorySel;   // btn_category_selected
        [SerializeField] private Sprite _iconCoin;         // icon_coin
        [SerializeField] private Sprite[] _slotByGrade;    // slot_common/uncommon/rare/epic/legendary (등급 1~5)
        [Tooltip("공용 아이템 슬롯 프리팹(Assets/Prefabs/UI/ItemSlot). 아이콘·수량·hover 상세에 사용.")]
        [SerializeField] private GameObject _itemSlotPrefab;

        [Header("구성 참조 (에디터 빌더가 배선)")]
        [SerializeField] private Button _closeButton;
        [SerializeField] private Button _dimButton;
        [SerializeField] private Button _listTabButton;
        [SerializeField] private Button _sellTabButton;
        [SerializeField] private Image _listTabImage;
        [SerializeField] private Image _sellTabImage;
        [SerializeField] private GameObject _listTabRoot;
        [SerializeField] private GameObject _sellTabRoot;
        [SerializeField] private RectTransform _listContent;
        [SerializeField] private InputField _searchInput;
        [SerializeField] private Button _searchButton;
        [SerializeField] private Button _mineTabButton;
        [SerializeField] private Image _mineTabImage;
        [SerializeField] private Button _prevPageButton;
        [SerializeField] private Button _nextPageButton;
        [SerializeField] private Text _pageText;
        [SerializeField] private Text _emptyText;
        [SerializeField] private RectTransform _sellContent;
        [SerializeField] private InputField _priceInput;
        [SerializeField] private Button _registerButton;
        [SerializeField] private Text _sellHintText;
        [SerializeField] private Text _messageText;
        [SerializeField] private Image _goldIcon;   // 보유 골드 아이콘(item_1, 런타임 배정)
        [SerializeField] private Text _goldText;    // 보유 골드량

        private Font _font;
        private bool _busy;
        private int _page;
        private int _searchItemCode;      // 0 = 전체
        private TradeTab _tab = TradeTab.List;
        private bool _hasMore;

        /// <summary>상단 탭. 판매 목록·판매 현황은 같은 목록 화면을 쓰고 서버 <c>mine</c> 플래그만 다르다.</summary>
        private enum TradeTab
        {
            List, // 구매 가능 매물(mine=false)
            Sell, // 판매 등록
            Mine, // 내가 등록한 매물(mine=true) — 취소 대상
        }

        /// <summary>현재 목록이 '판매 현황'(내 등록)인지.</summary>
        private bool MineOnly => _tab == TradeTab.Mine;
        private long _selectedItemId;     // 판매 등록 대상(0 = 미선택)
        private int _selectedItemCode;
        private readonly List<GameObject> _rows = new List<GameObject>();
        private readonly List<GameObject> _sellRows = new List<GameObject>();

        private bool AlreadyBuilt => _listContent != null;

        private void Awake()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (!AlreadyBuilt)
            {
                Construct(); // 폴백(프리팹 미배선 시)
            }
            WireRuntime();
        }

        /// <summary>패널이 표시될 때마다 목록을 첫 페이지부터 새로 조회한다.</summary>
        private void OnEnable()
        {
            if (Application.isPlaying && AlreadyBuilt)
            {
                SetMessage(string.Empty);
                RefreshGold();
                ShowListTab(); // 열 때는 항상 구매 가능 목록부터(첫 페이지 조회 포함)
            }
        }

        /// <summary>에디터 빌드 전용: 전체 정적 계층을 생성해 프리팹에 굽는다.</summary>
        public void EditorConstruct() => Construct();

        // ── 정적 계층 구성 ──

        /// <summary>캔버스·딤·창 프레임·타이틀·탭·목록/등록 화면·메시지를 생성한다.</summary>
        private void Construct()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildCanvas();
            BuildDim();
            var panel = BuildPanel();
            BuildHeader(panel);
            BuildGoldArea(panel); // 보유 골드 표시(좌상단, 인벤토리와 동일 규격)
            BuildTabs(panel);
            BuildListTab(panel);
            BuildSellTab(panel);
            BuildMessage(panel);
        }

        /// <summary>패널 전용 오버레이 캔버스를 구성한다(다른 패널과 동일 규격, sortingOrder 100).</summary>
        private void BuildCanvas()
        {
            var canvas = gameObject.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = gameObject.AddComponent<Canvas>();
            }
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
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
        }

        /// <summary>패널 밖 클릭 시 닫히는 투명 차단막.</summary>
        private void BuildDim()
        {
            var img = NewImage("Dim", (RectTransform)transform, new Color(0f, 0f, 0f, 0f));
            Stretch(img.rectTransform);
            _dimButton = img.gameObject.AddComponent<Button>();
            _dimButton.transition = Selectable.Transition.None;
        }

        /// <summary>거래소 창 본체(window_frame 9-slice).</summary>
        private RectTransform BuildPanel()
        {
            var img = NewImage("PanelRoot", (RectTransform)transform, new Color(0.12f, 0.10f, 0.08f, 0.98f));
            ApplySliced(img, _windowFrame);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        /// <summary>상단 타이틀 리본과 닫기 버튼.</summary>
        private void BuildHeader(RectTransform panel)
        {
            var ribbon = NewImage("TitleBanner", panel, new Color(0.45f, 0.30f, 0.16f, 1f));
            ApplySliced(ribbon, _bannerRibbon);
            var brt = ribbon.rectTransform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 1f);
            brt.pivot = new Vector2(0.5f, 1f);
            brt.anchoredPosition = new Vector2(0f, -8f);
            brt.sizeDelta = new Vector2(420f, 76f);

            var title = NewText("Title", brt, "거래소", 40, TextAnchor.MiddleCenter);
            title.fontStyle = FontStyle.Bold;
            title.color = new Color(1f, 0.92f, 0.72f);
            Stretch(title.rectTransform);

            var close = NewImage("CloseButton", panel, new Color(0.5f, 0.18f, 0.15f, 1f));
            ApplySimple(close, _btnClose);
            var crt = close.rectTransform;
            crt.anchorMin = crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(1f, 1f);
            crt.anchoredPosition = new Vector2(-26f, -26f);
            crt.sizeDelta = new Vector2(54f, 54f);
            if (_btnClose == null)
            {
                var xt = NewText("X", crt, "X", 30, TextAnchor.MiddleCenter);
                Stretch(xt.rectTransform);
            }
            _closeButton = close.gameObject.AddComponent<Button>();
        }

        /// <summary>좌상단 보유 골드 영역(골드 아이콘 + 수량)을 구성한다. 인벤토리 패널과 같은 규격·표기이며,
        /// 아이콘·수량은 런타임에 세션 재화에서 채운다(<see cref="RefreshGold"/>).
        /// 탭 줄(y 96~162) 위에 놓이도록 높이를 84까지만 쓴다.</summary>
        private void BuildGoldArea(RectTransform panel)
        {
            var area = NewImage("GoldArea", panel, new Color(0f, 0f, 0f, 0.35f));
            var art = area.rectTransform;
            art.anchorMin = art.anchorMax = new Vector2(0f, 1f);
            art.pivot = new Vector2(0f, 1f);
            art.anchoredPosition = new Vector2(36f, -28f);
            art.sizeDelta = new Vector2(280f, 56f);
            area.raycastTarget = false;

            _goldIcon = NewImage("GoldIcon", art, Color.white); // 스프라이트는 RefreshGold에서 item_1로 배정
            _goldIcon.raycastTarget = false;
            _goldIcon.preserveAspect = true;
            _goldIcon.enabled = false; // 아이콘을 찾기 전에는 빈 사각형을 보이지 않게
            var irt = _goldIcon.rectTransform;
            irt.anchorMin = irt.anchorMax = new Vector2(0f, 0.5f);
            irt.pivot = new Vector2(0f, 0.5f);
            irt.anchoredPosition = new Vector2(8f, 0f);
            irt.sizeDelta = new Vector2(44f, 44f);

            _goldText = NewText("GoldText", art, "0", 32, TextAnchor.MiddleLeft);
            _goldText.fontStyle = FontStyle.Bold;
            _goldText.color = new Color(1f, 0.90f, 0.55f);
            var grt = _goldText.rectTransform;
            grt.anchorMin = grt.anchorMax = new Vector2(0f, 0.5f);
            grt.pivot = new Vector2(0f, 0.5f);
            grt.anchoredPosition = new Vector2(62f, 0f);
            grt.sizeDelta = new Vector2(206f, 44f);
        }

        /// <summary>상단 탭 3개: [판매 목록] [판매 등록] [판매 현황]. 가운데 정렬로 나란히 놓는다.</summary>
        private void BuildTabs(RectTransform panel)
        {
            _listTabImage = BuildTab(panel, "ListTab", "판매 목록", -(TabWidth + TabGap));
            _listTabButton = _listTabImage.gameObject.AddComponent<Button>();

            _sellTabImage = BuildTab(panel, "SellTab", "판매 등록", 0f);
            _sellTabButton = _sellTabImage.gameObject.AddComponent<Button>();

            // '판매 등록' 오른쪽 — 내가 등록한 매물(취소 대상)을 보는 탭.
            _mineTabImage = BuildTab(panel, "MineTab", "판매 현황", TabWidth + TabGap);
            _mineTabButton = _mineTabImage.gameObject.AddComponent<Button>();
        }

        /// <summary>탭 버튼 하나(가운데 기준 x 오프셋).</summary>
        private Image BuildTab(RectTransform panel, string name, string label, float offsetX)
        {
            var img = NewImage(name, panel, new Color(0.28f, 0.22f, 0.14f, 1f));
            ApplySliced(img, _btnCategory);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(offsetX, -96f);
            rt.sizeDelta = new Vector2(TabWidth, 66f);
            var t = NewText("Label", rt, label, 30, TextAnchor.MiddleCenter);
            t.fontStyle = FontStyle.Bold;
            Stretch(t.rectTransform);
            return img;
        }

        /// <summary>판매 목록 탭: 검색줄 · 표 머리 · 목록 · 페이지 이동.</summary>
        private void BuildListTab(RectTransform panel)
        {
            var root = NewChild("ListTabRoot", panel);
            Stretch(root);
            _listTabRoot = root.gameObject;

            // 검색줄(아이템 이름 → itemCode 변환).
            var field = NewImage("SearchField", root, new Color(0.86f, 0.78f, 0.58f, 1f));
            ApplySliced(field, _fieldSearch);
            var frt = field.rectTransform;
            frt.anchorMin = new Vector2(0f, 1f);
            frt.anchorMax = new Vector2(1f, 1f);
            frt.pivot = new Vector2(0.5f, 1f);
            // 검색창 오른쪽에 [검색] 버튼 자리를 비운다.
            frt.offsetMin = new Vector2(ListInset, 0f);
            frt.offsetMax = new Vector2(-(ListInset + SearchButtonWidth + 12f), 0f);
            frt.sizeDelta = new Vector2(frt.sizeDelta.x, 62f);
            frt.anchoredPosition = new Vector2(frt.anchoredPosition.x, -SearchRowY);
            _searchInput = BuildInput(field.rectTransform, "아이템 이름으로 검색(비우면 전체)");

            var searchBtn = NewImage("SearchButton", root, new Color(0.24f, 0.40f, 0.62f, 1f));
            ApplySliced(searchBtn, _btnBlue);
            var sbrt = searchBtn.rectTransform;
            sbrt.anchorMin = sbrt.anchorMax = new Vector2(1f, 1f);
            sbrt.pivot = new Vector2(1f, 1f);
            sbrt.anchoredPosition = new Vector2(-ListInset, -SearchRowY);
            sbrt.sizeDelta = new Vector2(SearchButtonWidth, 62f);
            var sbt = NewText("Label", sbrt, "검색", 28, TextAnchor.MiddleCenter);
            sbt.fontStyle = FontStyle.Bold;
            Stretch(sbt.rectTransform);
            _searchButton = searchBtn.gameObject.AddComponent<Button>();

            // 표 머리.
            var header = NewImage("TableHeader", root, new Color(0.30f, 0.23f, 0.14f, 1f));
            ApplySliced(header, _tableHeader);
            var hrt = header.rectTransform;
            hrt.anchorMin = new Vector2(0f, 1f);
            hrt.anchorMax = new Vector2(1f, 1f);
            hrt.pivot = new Vector2(0.5f, 1f);
            // 표 머리를 행과 같은 폭(콘텐츠 여백 10 포함)으로 잡아야 열 좌표가 정확히 맞는다.
            hrt.offsetMin = new Vector2(ListInset + 10f, 0f);
            hrt.offsetMax = new Vector2(-(ListInset + 10f), 0f);
            hrt.sizeDelta = new Vector2(hrt.sizeDelta.x, 48f);
            hrt.anchoredPosition = new Vector2(0f, -252f);
            AddHeaderLabel(hrt, "아이템", 150f, 380f, TextAnchor.MiddleLeft);
            AddHeaderLabel(hrt, "수량", 540f, 110f, TextAnchor.MiddleCenter);
            AddHeaderLabel(hrt, "가격", 660f, 160f, TextAnchor.MiddleRight);
            AddHeaderLabel(hrt, "거래", RowActionX, RowActionWidth, TextAnchor.MiddleCenter);

            // 목록 영역(양피지 배경 + 세로 레이아웃).
            var listBg = NewImage("ListBackground", root, new Color(0.82f, 0.74f, 0.55f, 1f));
            ApplySliced(listBg, _panelParchment);
            var lbrt = listBg.rectTransform;
            lbrt.anchorMin = new Vector2(0f, 0f);
            lbrt.anchorMax = new Vector2(1f, 1f);
            lbrt.offsetMin = new Vector2(ListInset, 200f);
            lbrt.offsetMax = new Vector2(-ListInset, -304f);
            listBg.gameObject.AddComponent<RectMask2D>();

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(lbrt, false);
            _listContent = (RectTransform)contentGo.transform;
            _listContent.anchorMin = new Vector2(0f, 1f);
            _listContent.anchorMax = new Vector2(1f, 1f);
            _listContent.pivot = new Vector2(0.5f, 1f);
            _listContent.offsetMin = new Vector2(10f, 0f);
            _listContent.offsetMax = new Vector2(-10f, 0f);
            var layout = contentGo.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.padding = new RectOffset(0, 0, 10, 10);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = contentGo.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // 내용이 뷰포트를 넘으면 잘리지 않고 스크롤되게 한다(RectMask2D만으로는 그냥 잘린다).
            AddVerticalScroll(listBg.gameObject, lbrt, _listContent);

            _emptyText = NewText("EmptyText", lbrt, "판매 중인 등록이 없습니다", 28, TextAnchor.MiddleCenter);
            _emptyText.color = new Color(0.35f, 0.26f, 0.16f, 0.85f);
            var ert = _emptyText.rectTransform;
            ert.anchorMin = ert.anchorMax = new Vector2(0.5f, 0.5f);
            ert.sizeDelta = new Vector2(600f, 60f);
            ert.anchoredPosition = Vector2.zero;
            _emptyText.gameObject.SetActive(false);

            // 페이지 이동.
            _prevPageButton = BuildPageButton(root, "PrevPage", "◀", -120f);
            _nextPageButton = BuildPageButton(root, "NextPage", "▶", 120f);
            _pageText = NewText("PageText", root, "1", 28, TextAnchor.MiddleCenter);
            _pageText.color = new Color(1f, 0.92f, 0.72f);
            var prt = _pageText.rectTransform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0f);
            prt.pivot = new Vector2(0.5f, 0f);
            prt.anchoredPosition = new Vector2(0f, 132f);
            prt.sizeDelta = new Vector2(160f, 48f);
        }

        /// <summary>판매 등록 탭: 판매 가능 아이템 목록 · 가격 입력 · 등록 버튼.</summary>
        private void BuildSellTab(RectTransform panel)
        {
            var root = NewChild("SellTabRoot", panel);
            Stretch(root);
            _sellTabRoot = root.gameObject;

            var listBg = NewImage("SellListBackground", root, new Color(0.82f, 0.74f, 0.55f, 1f));
            ApplySliced(listBg, _panelParchment);
            var lbrt = listBg.rectTransform;
            lbrt.anchorMin = new Vector2(0f, 0f);
            lbrt.anchorMax = new Vector2(1f, 1f);
            lbrt.offsetMin = new Vector2(ListInset, 320f);
            lbrt.offsetMax = new Vector2(-ListInset, -186f);
            listBg.gameObject.AddComponent<RectMask2D>();

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(lbrt, false);
            _sellContent = (RectTransform)contentGo.transform;
            _sellContent.anchorMin = new Vector2(0f, 1f);
            _sellContent.anchorMax = new Vector2(1f, 1f);
            _sellContent.pivot = new Vector2(0.5f, 1f);
            _sellContent.offsetMin = new Vector2(10f, 0f);
            _sellContent.offsetMax = new Vector2(-10f, 0f);
            var layout = contentGo.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.padding = new RectOffset(0, 0, 10, 10);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = contentGo.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // 판매 가능 아이템 수는 인벤토리에 따라 얼마든지 늘 수 있으므로 스크롤이 필수다.
            AddVerticalScroll(listBg.gameObject, lbrt, _sellContent);

            // 하단 등록 폼(나무 패널).
            var form = NewImage("SellForm", root, new Color(0.32f, 0.24f, 0.15f, 1f));
            ApplySliced(form, _panelWood);
            var frt = form.rectTransform;
            frt.anchorMin = new Vector2(0f, 0f);
            frt.anchorMax = new Vector2(1f, 0f);
            frt.pivot = new Vector2(0.5f, 0f);
            frt.offsetMin = new Vector2(ListInset, 0f);
            frt.offsetMax = new Vector2(-ListInset, 0f);
            frt.sizeDelta = new Vector2(frt.sizeDelta.x, 250f);
            frt.anchoredPosition = new Vector2(0f, 44f);

            _sellHintText = NewText("SellHint", frt, "판매할 아이템을 선택하세요", 26, TextAnchor.UpperLeft);
            _sellHintText.color = new Color(1f, 0.92f, 0.72f, 0.9f);
            _sellHintText.horizontalOverflow = HorizontalWrapMode.Wrap;
            var shrt = _sellHintText.rectTransform;
            shrt.anchorMin = new Vector2(0f, 1f);
            shrt.anchorMax = new Vector2(1f, 1f);
            shrt.pivot = new Vector2(0.5f, 1f);
            shrt.offsetMin = new Vector2(28f, 0f);
            shrt.offsetMax = new Vector2(-28f, 0f);
            shrt.sizeDelta = new Vector2(shrt.sizeDelta.x, 96f);
            shrt.anchoredPosition = new Vector2(0f, -22f);

            var priceField = NewImage("PriceField", frt, new Color(0.86f, 0.78f, 0.58f, 1f));
            ApplySliced(priceField, _fieldSearch);
            var pfrt = priceField.rectTransform;
            pfrt.anchorMin = pfrt.anchorMax = new Vector2(0f, 0f);
            pfrt.pivot = new Vector2(0f, 0f);
            pfrt.anchoredPosition = new Vector2(28f, 34f);
            pfrt.sizeDelta = new Vector2(520f, 62f);
            _priceInput = BuildInput(pfrt, "판매 가격(골드)");
            _priceInput.contentType = InputField.ContentType.IntegerNumber;

            var regBtn = NewImage("RegisterButton", frt, new Color(0.62f, 0.46f, 0.16f, 1f));
            ApplySliced(regBtn, _btnGold);
            var rbrt = regBtn.rectTransform;
            rbrt.anchorMin = rbrt.anchorMax = new Vector2(1f, 0f);
            rbrt.pivot = new Vector2(1f, 0f);
            rbrt.anchoredPosition = new Vector2(-28f, 34f);
            rbrt.sizeDelta = new Vector2(220f, 62f);
            var rbt = NewText("Label", rbrt, "등록", 30, TextAnchor.MiddleCenter);
            rbt.fontStyle = FontStyle.Bold;
            Stretch(rbt.rectTransform);
            _registerButton = regBtn.gameObject.AddComponent<Button>();
        }

        /// <summary>하단 공용 안내/오류 메시지.</summary>
        private void BuildMessage(RectTransform panel)
        {
            _messageText = NewText("Message", panel, string.Empty, 26, TextAnchor.MiddleCenter);
            _messageText.color = new Color(1f, 0.72f, 0.42f);
            var mrt = _messageText.rectTransform;
            mrt.anchorMin = mrt.anchorMax = new Vector2(0.5f, 0f);
            mrt.pivot = new Vector2(0.5f, 0f);
            mrt.anchoredPosition = new Vector2(0f, 22f);
            mrt.sizeDelta = new Vector2(900f, 36f);
        }

        private void WireRuntime()
        {
            if (_closeButton != null) _closeButton.onClick.AddListener(Close);
            if (_dimButton != null) _dimButton.onClick.AddListener(Close);
            if (_listTabButton != null) _listTabButton.onClick.AddListener(ShowListTab);
            if (_sellTabButton != null) _sellTabButton.onClick.AddListener(ShowSellTab);
            if (_searchButton != null) _searchButton.onClick.AddListener(OnSearch);
            if (_mineTabButton != null) _mineTabButton.onClick.AddListener(ShowMineTab);
            if (_prevPageButton != null) _prevPageButton.onClick.AddListener(OnPrevPage);
            if (_nextPageButton != null) _nextPageButton.onClick.AddListener(OnNextPage);
            if (_registerButton != null) _registerButton.onClick.AddListener(OnRegister);
        }

        // ── 탭 전환 ──

        /// <summary>판매 목록(구매 가능 매물) 탭.</summary>
        private void ShowListTab() => SelectTab(TradeTab.List);

        /// <summary>판매 등록 탭.</summary>
        private void ShowSellTab() => SelectTab(TradeTab.Sell);

        /// <summary>판매 현황(내가 등록한 매물) 탭 — 여기서만 판매 취소를 할 수 있다.</summary>
        private void ShowMineTab() => SelectTab(TradeTab.Mine);

        /// <summary>탭을 전환한다. 목록 계열(판매 목록·판매 현황)은 화면을 공유하고 서버 <c>mine</c> 플래그만
        /// 다르므로, 전환 시 첫 페이지부터 다시 조회한다(다른 목록의 페이지 번호를 물고 가지 않도록).</summary>
        private void SelectTab(TradeTab tab)
        {
            _tab = tab;
            bool showList = tab != TradeTab.Sell;
            if (_listTabRoot != null) _listTabRoot.SetActive(showList);
            if (_sellTabRoot != null) _sellTabRoot.SetActive(!showList);
            ApplySliced(_listTabImage, tab == TradeTab.List ? _btnCategorySel : _btnCategory);
            ApplySliced(_sellTabImage, tab == TradeTab.Sell ? _btnCategorySel : _btnCategory);
            ApplySliced(_mineTabImage, tab == TradeTab.Mine ? _btnCategorySel : _btnCategory);
            SetMessage(string.Empty);

            if (tab == TradeTab.Sell)
            {
                RebuildSellRows();
            }
            else
            {
                _page = 0;
                RequestList();
            }
        }

        // ── 목록 조회 ──

        /// <summary>검색어를 아이템 코드로 바꾸고 첫 페이지부터 다시 조회한다.</summary>
        private void OnSearch()
        {
            _searchItemCode = ResolveItemCode(_searchInput != null ? _searchInput.text : string.Empty, out string notFound);
            if (notFound != null)
            {
                SetMessage($"'{notFound}' 이름의 아이템을 찾지 못했습니다.");
                return;
            }
            _page = 0;
            RequestList();
        }

        /// <summary>이름으로 아이템 코드를 찾는다(대소문자·공백 무시, 부분 일치 허용).
        /// 빈 검색어면 0(전체). 찾지 못하면 <paramref name="notFound"/>에 입력값을 담아 돌려준다.</summary>
        private static int ResolveItemCode(string query, out string notFound)
        {
            notFound = null;
            string q = (query ?? string.Empty).Trim();
            if (q.Length == 0)
            {
                return 0;
            }
            MasterDataManager.EnsureLoaded();
            var db = MasterDataManager.Db;
            if (db == null)
            {
                notFound = q;
                return 0;
            }
            foreach (var pair in db.Items)
            {
                string name = pair.Value != null ? pair.Value.name : null;
                if (!string.IsNullOrEmpty(name) &&
                    name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return pair.Key;
                }
            }
            notFound = q;
            return 0;
        }

        private void OnPrevPage()
        {
            if (_page > 0)
            {
                _page--;
                RequestList();
            }
        }

        private void OnNextPage()
        {
            if (_hasMore)
            {
                _page++;
                RequestList();
            }
        }

        /// <summary>거래소 목록을 조회한다(POST /api/game/trade/list). 가격 오름차순·서버 페이징.</summary>
        private void RequestList()
        {
            if (NetworkManager.Instance == null || !Session.IsLoggedIn)
            {
                SetMessage("로그인이 필요합니다.");
                return;
            }
            var req = new TradeListRequest
            {
                userId = Session.UserId,
                token = Session.Token,
                // mine=false면 서버가 본인 등록을 제외한 구매 대상만, true면 본인 등록만 준다.
                data = new TradeListData
                {
                    itemCode = _searchItemCode, mine = MineOnly, page = _page, pageSize = PageSize,
                },
            };
            NetworkManager.Instance.PostToGame<TradeListResponse>("/api/game/trade/list", req, resp =>
            {
                var data = resp != null ? resp.data : null;
                _hasMore = data != null && data.hasMore;
                RebuildRows(data != null ? data.listings : null);
                UpdatePageUi();
            }, OnListError);
        }

        /// <summary>조회 결과로 목록 행을 다시 그린다(홀짝 배경 교차).</summary>
        private void RebuildRows(List<TradeListingDto> listings)
        {
            foreach (var row in _rows)
            {
                if (row != null) Destroy(row);
            }
            _rows.Clear();

            int count = 0;
            if (listings != null)
            {
                foreach (var listing in listings)
                {
                    if (listing == null) continue;
                    _rows.Add(BuildListingRow(listing, count));
                    count++;
                }
            }
            if (_emptyText != null)
            {
                _emptyText.text = MineOnly ? "등록한 판매 매물이 없습니다" : "판매 중인 등록이 없습니다";
                _emptyText.gameObject.SetActive(count == 0);
            }
        }

        /// <summary>등록 1건의 행(등급 슬롯 + 아이콘 · 이름/강화 · 수량 · 가격 · 구매/취소)을 만든다.</summary>
        private GameObject BuildListingRow(TradeListingDto listing, int index)
        {
            // 내 등록 목록이면 취소, 구매 목록이면 구매. 서버가 mine 플래그로 갈라 주지만,
            // 혹시 섞여 오더라도 자기 등록을 구매 버튼으로 노출하지 않도록 판매자도 함께 본다.
            bool mine = MineOnly || listing.sellerUserId == Session.UserId;

            var rowImg = NewImage("ListingRow", _listContent, new Color(0.74f, 0.66f, 0.48f, 1f));
            ApplySliced(rowImg, index % 2 == 0 ? _rowNormal : _rowAlt);
            var le = rowImg.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = RowHeight;
            le.flexibleHeight = 0f;
            var rt = rowImg.rectTransform;

            BuildRowSlot(rt, listing);

            var def = FindItem(listing.itemCode);
            string itemName = def != null ? def.name : $"아이템 {listing.itemCode}";
            if (listing.enhanceLevel > 0)
            {
                itemName += $" +{listing.enhanceLevel}";
            }
            var nameText = NewText("Name", rt, itemName, 26, TextAnchor.MiddleLeft);
            nameText.color = GradeColors.IconFallback(def != null ? def.grade : 1);
            nameText.fontStyle = FontStyle.Bold;
            PlaceMiddleLeft(nameText.rectTransform, 150f, 380f, 40f);

            var qtyText = NewText("Quantity", rt, listing.quantity > 1 ? $"x{listing.quantity}" : "-", 26, TextAnchor.MiddleCenter);
            qtyText.color = new Color(0.28f, 0.21f, 0.13f);
            PlaceMiddleLeft(qtyText.rectTransform, 540f, 110f, 40f);

            var priceText = NewText("Price", rt, $"{listing.price:N0}", 26, TextAnchor.MiddleRight);
            priceText.color = new Color(0.55f, 0.38f, 0.08f);
            priceText.fontStyle = FontStyle.Bold;
            PlaceMiddleLeft(priceText.rectTransform, 660f, 160f, 40f);

            if (_iconCoin != null)
            {
                var coin = NewImage("Coin", rt, Color.white);
                ApplySimple(coin, _iconCoin);
                PlaceMiddleLeft(coin.rectTransform, 632f, 28f, 28f);
            }

            // 남의 등록 = 구매, 내 등록 = 취소.
            var actionImg = NewImage(mine ? "CancelButton" : "BuyButton", rt,
                mine ? new Color(0.60f, 0.22f, 0.18f, 1f) : new Color(0.62f, 0.46f, 0.16f, 1f));
            ApplySliced(actionImg, mine ? _btnRed : _btnGold);
            PlaceMiddleLeft(actionImg.rectTransform, RowActionX, RowActionWidth, 56f);
            var at = NewText("Label", actionImg.rectTransform, mine ? "취소" : "구매", 26, TextAnchor.MiddleCenter);
            at.fontStyle = FontStyle.Bold;
            Stretch(at.rectTransform);
            long listingId = listing.listingId;
            actionImg.gameObject.AddComponent<Button>().onClick.AddListener(
                mine ? (UnityEngine.Events.UnityAction)(() => OnCancel(listingId))
                     : () => OnBuy(listingId));

            return rowImg.gameObject;
        }

        /// <summary>행 좌측의 등급 슬롯 + 아이템 아이콘. 공용 ItemSlot 프리팹이 있으면 그것을 쓰고(호버 상세 포함),
        /// 없으면 등급 슬롯 스프라이트만 표시한다.</summary>
        private void BuildRowSlot(RectTransform row, TradeListingDto listing)
        {
            if (_itemSlotPrefab != null)
            {
                var go = Instantiate(_itemSlotPrefab, row);
                var srt = (RectTransform)go.transform;
                srt.anchorMin = srt.anchorMax = new Vector2(0f, 0.5f);
                srt.pivot = new Vector2(0f, 0.5f);
                srt.anchoredPosition = new Vector2(20f, 0f);
                srt.sizeDelta = new Vector2(72f, 72f);
                var view = go.GetComponent<ItemSlotView>();
                if (view != null)
                {
                    view.SetFrameSprite(SlotSpriteFor(listing.itemCode));
                    view.Setup(listing.itemCode, listing.quantity);
                }
                return;
            }

            var slot = NewImage("Slot", row, Color.white);
            ApplySliced(slot, SlotSpriteFor(listing.itemCode));
            PlaceMiddleLeft(slot.rectTransform, 20f, 72f, 72f);
        }

        /// <summary>아이템 등급(1~5)에 대응하는 슬롯 프레임 스프라이트. 배선이 없으면 null(단색 폴백).</summary>
        private Sprite SlotSpriteFor(int itemCode)
        {
            if (_slotByGrade == null || _slotByGrade.Length == 0)
            {
                return null;
            }
            var def = FindItem(itemCode);
            int grade = def != null ? Mathf.Clamp(def.grade, 1, _slotByGrade.Length) : 1;
            return _slotByGrade[grade - 1];
        }

        private void UpdatePageUi()
        {
            if (_pageText != null)
            {
                _pageText.text = (_page + 1).ToString();
            }
            if (_prevPageButton != null) _prevPageButton.interactable = _page > 0 && !_busy;
            if (_nextPageButton != null) _nextPageButton.interactable = _hasMore && !_busy;
        }

        // ── 구매 / 취소 ──

        /// <summary>등록을 구매한다(POST /api/game/trade/buy). 구매 아이템은 인벤토리가 아니라
        /// <b>우편함으로 발급</b>되므로(기획서 §5.3), 무엇을 샀는지와 수령이 필요함을 '확인' 모달로 안내한다.
        /// 골드는 즉시 차감되므로 세이브를 재로드해 잔액을 최신화한 뒤 목록을 다시 조회한다.</summary>
        private void OnBuy(long listingId)
        {
            if (_busy || NetworkManager.Instance == null || !Session.IsLoggedIn)
            {
                return;
            }
            _busy = true;
            var req = new TradeBuyRequest
            {
                userId = Session.UserId,
                token = Session.Token,
                data = new TradeListingData { listingId = listingId },
            };
            NetworkManager.Instance.PostToGame<TradeBuyResponse>("/api/game/trade/buy", req, resp =>
            {
                var data = resp != null ? resp.data : null;
                Debug.Log($"[Trade] 구매 완료 listing={listingId} mailId={(data != null ? data.mailId : 0)}");
                ShowPurchasedModal(data);
                ReloadSessionAndList();
            }, OnActionError);
        }

        /// <summary>내 등록을 취소한다(POST /api/game/trade/cancel). 아이템은 인벤토리로 복귀한다.</summary>
        private void OnCancel(long listingId)
        {
            if (_busy || NetworkManager.Instance == null || !Session.IsLoggedIn)
            {
                return;
            }
            _busy = true;
            var req = new TradeCancelRequest
            {
                userId = Session.UserId,
                token = Session.Token,
                data = new TradeListingData { listingId = listingId },
            };
            NetworkManager.Instance.PostToGame<TradeCancelResponse>("/api/game/trade/cancel", req, resp =>
            {
                var restored = resp != null && resp.data != null ? resp.data.restored : null;
                Debug.Log($"[Trade] 판매 취소 완료 listing={listingId}");
                string name = restored != null ? ItemName(restored.itemCode) : "아이템";
                ModalManager.Instance?.ShowConfirm("판매 취소", $"{name}이(가) 인벤토리로 돌아왔습니다.");
                ReloadSessionAndList();
            }, OnActionError);
        }

        /// <summary>구매 성공을 '확인' 버튼 모달로 안내한다. 산 아이템과 지불 골드를 적고,
        /// 인벤토리가 아니라 우편함으로 발급된다는 점(기획서 §5.3)을 함께 알린다.</summary>
        private static void ShowPurchasedModal(TradeBuyResultData data)
        {
            var lines = new List<string>();
            if (data != null && data.gained != null && data.gained.items != null)
            {
                foreach (var item in data.gained.items)
                {
                    if (item != null && item.quantity > 0)
                    {
                        string name = ItemName(item.itemCode) +
                                      (item.enhanceLevel > 0 ? $" +{item.enhanceLevel}" : string.Empty);
                        lines.Add(item.quantity > 1 ? $"{name} x{item.quantity}" : name);
                    }
                }
            }
            if (data != null && data.cost != null && data.cost.amount > 0)
            {
                lines.Add($"지불 골드: {GoldFormat.Highlight(data.cost.amount)}");
            }
            lines.Add("우편함에서 수령해야 가방에 들어갑니다.");
            ModalManager.Instance?.ShowConfirm("아이템 구입 성공!", string.Join("\n", lines));
        }

        // ── 판매 등록 ──

        /// <summary>인벤토리에서 판매 가능한 아이템(미장착 · sellable=1 · 기준가 있음)을 나열한다.</summary>
        private void RebuildSellRows()
        {
            foreach (var row in _sellRows)
            {
                if (row != null) Destroy(row);
            }
            _sellRows.Clear();
            _selectedItemId = 0;
            _selectedItemCode = 0;
            UpdateSellHint();

            var inventory = Session.GameData != null ? Session.GameData.inventory : null;
            if (inventory == null)
            {
                return;
            }
            int index = 0;
            foreach (var item in inventory)
            {
                if (item == null || item.equippedCharacterId != 0)
                {
                    continue; // 장착 중인 장비는 등록 불가(기획서 §2)
                }
                var def = FindItem(item.itemCode);
                if (def == null || def.sellable != 1 || def.basePrice <= 0)
                {
                    continue;
                }
                _sellRows.Add(BuildSellRow(item, def, index));
                index++;
            }
        }

        /// <summary>판매 등록 후보 행(아이템 슬롯 · 이름 · 기준가 범위 · 선택 버튼).</summary>
        private GameObject BuildSellRow(InventoryItemDto item, TaskbarHero.Common.MasterData.ItemMaster def, int index)
        {
            var rowImg = NewImage("SellRow", _sellContent, new Color(0.74f, 0.66f, 0.48f, 1f));
            ApplySliced(rowImg, index % 2 == 0 ? _rowNormal : _rowAlt);
            var le = rowImg.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = RowHeight;
            le.flexibleHeight = 0f;
            var rt = rowImg.rectTransform;

            if (_itemSlotPrefab != null)
            {
                var go = Instantiate(_itemSlotPrefab, rt);
                var srt = (RectTransform)go.transform;
                srt.anchorMin = srt.anchorMax = new Vector2(0f, 0.5f);
                srt.pivot = new Vector2(0f, 0.5f);
                srt.anchoredPosition = new Vector2(20f, 0f);
                srt.sizeDelta = new Vector2(72f, 72f);
                var view = go.GetComponent<ItemSlotView>();
                if (view != null)
                {
                    view.SetFrameSprite(SlotSpriteFor(item.itemCode));
                    view.Setup(item.itemCode, item.quantity);
                }
            }

            string label = def.name + (item.enhanceLevel > 0 ? $" +{item.enhanceLevel}" : string.Empty) +
                           (item.quantity > 1 ? $" x{item.quantity}" : string.Empty);
            var nameText = NewText("Name", rt, label, 26, TextAnchor.MiddleLeft);
            nameText.color = GradeColors.IconFallback(def.grade);
            nameText.fontStyle = FontStyle.Bold;
            PlaceMiddleLeft(nameText.rectTransform, 110f, 420f, 40f);

            var rangeText = NewText("Range", rt, PriceRangeLabel(def.basePrice), 22, TextAnchor.MiddleRight);
            rangeText.color = new Color(0.40f, 0.30f, 0.14f);
            PlaceMiddleLeft(rangeText.rectTransform, 540f, 280f, 36f);

            var pick = NewImage("PickButton", rt, new Color(0.24f, 0.40f, 0.62f, 1f));
            ApplySliced(pick, _btnBlue);
            PlaceMiddleLeft(pick.rectTransform, RowActionX, RowActionWidth, 56f);
            var pt = NewText("Label", pick.rectTransform, "선택", 26, TextAnchor.MiddleCenter);
            pt.fontStyle = FontStyle.Bold;
            Stretch(pt.rectTransform);
            long itemId = item.itemId;
            int itemCode = item.itemCode;
            pick.gameObject.AddComponent<Button>().onClick.AddListener(() => SelectSellItem(itemId, itemCode));

            return rowImg.gameObject;
        }

        /// <summary>등록 대상 아이템을 고르고 가격 입력 안내를 갱신한다(권장 가격을 기준가로 미리 채운다).</summary>
        private void SelectSellItem(long itemId, int itemCode)
        {
            _selectedItemId = itemId;
            _selectedItemCode = itemCode;
            var def = FindItem(itemCode);
            if (def != null && _priceInput != null)
            {
                _priceInput.text = def.basePrice.ToString();
            }
            UpdateSellHint();
        }

        /// <summary>선택 아이템과 허용 가격 범위를 안내 문구로 갱신한다.</summary>
        private void UpdateSellHint()
        {
            if (_sellHintText == null)
            {
                return;
            }
            if (_selectedItemId == 0)
            {
                _sellHintText.text = "판매할 아이템을 선택하세요.\n등록 후 3일간 판매되며, 팔리면 판매가의 80%가 우편함으로 지급됩니다.";
                return;
            }
            var def = FindItem(_selectedItemCode);
            string name = def != null ? def.name : $"아이템 {_selectedItemCode}";
            string range = def != null ? PriceRangeLabel(def.basePrice) : string.Empty;
            _sellHintText.text = $"선택: {name}\n등록 가능 가격 {range} · 수수료 20% 차감 후 우편함 지급";
        }

        /// <summary>기준가 ±20% 범위 표기("40,000 ~ 60,000 G").</summary>
        private static string PriceRangeLabel(long basePrice)
        {
            long min = (long)Mathf.Floor(basePrice * PriceMinRate);
            long max = (long)Mathf.Floor(basePrice * PriceMaxRate);
            return $"{min:N0} ~ {max:N0} G";
        }

        /// <summary>판매 등록을 요청한다(POST /api/game/trade/register). 선택·가격 범위를 먼저 클라이언트에서
        /// 걸러 불필요한 요청을 줄이되, 최종 판정은 서버가 한다(서버 권위).</summary>
        private void OnRegister()
        {
            if (_busy || NetworkManager.Instance == null || !Session.IsLoggedIn)
            {
                return;
            }
            if (_selectedItemId == 0)
            {
                SetMessage("판매할 아이템을 먼저 선택하세요.");
                return;
            }
            if (!long.TryParse(_priceInput != null ? _priceInput.text : string.Empty, out long price) || price <= 0)
            {
                SetMessage("판매 가격을 숫자로 입력하세요.");
                return;
            }
            var def = FindItem(_selectedItemCode);
            if (def != null)
            {
                long min = (long)Mathf.Floor(def.basePrice * PriceMinRate);
                long max = (long)Mathf.Floor(def.basePrice * PriceMaxRate);
                if (price < min || price > max)
                {
                    SetMessage($"등록 가격은 {min:N0} ~ {max:N0} G 범위여야 합니다.");
                    return;
                }
            }

            _busy = true;
            var req = new TradeRegisterRequest
            {
                userId = Session.UserId,
                token = Session.Token,
                data = new TradeRegisterData { itemId = _selectedItemId, price = price },
            };
            NetworkManager.Instance.PostToGame<TradeRegisterResponse>("/api/game/trade/register", req, resp =>
            {
                var data = resp != null ? resp.data : null;
                Debug.Log($"[Trade] 판매 등록 완료 listing={(data != null ? data.listingId : 0)}");
                ModalManager.Instance?.ShowConfirm("판매 등록",
                    $"{ItemName(_selectedItemCode)}을(를) {GoldFormat.Highlight(price)}에 등록했습니다.\n" +
                    "3일 안에 팔리지 않으면 우편함으로 반송됩니다.");
                ReloadSessionAndList();
            }, OnActionError);
        }

        // ── 공통 후처리 ──

        /// <summary>거래 후 세이브 스냅샷을 재로드해 세션(골드·인벤토리)을 최신화하고 화면을 다시 그린다.</summary>
        private void ReloadSessionAndList()
        {
            var req = new AuthRequest { userId = Session.UserId, token = Session.Token };
            NetworkManager.Instance.PostToGame<LoadResponse>("/api/game/load", req, resp =>
            {
                if (resp != null && resp.data != null)
                {
                    Session.SetGameData(resp.data);
                    Session.RaiseInventoryChanged(); // 골드·아이템 표시(HUD·패널) 갱신 트리거
                }
                _busy = false;
                RefreshGold(); // 구매·등록·취소로 골드가 바뀌었을 수 있다
                MailNotifier.Refresh(); // 판매 대금·반송 메일이 도착할 수 있으므로 알림 갱신
                RequestList();
                if (_sellTabRoot != null && _sellTabRoot.activeSelf)
                {
                    RebuildSellRows();
                }
            }, OnListError);
        }

        /// <summary>목록 조회/재로드 실패: 메시지만 표시한다(재조회하면 무한 루프가 되므로 하지 않는다).</summary>
        private void OnListError(NetworkError error)
        {
            _busy = false;
            Debug.LogWarning($"[Trade] 목록 조회 실패: {error}");
            SetMessage(ErrorMessages.ToKorean(error));
            UpdatePageUi();
        }

        /// <summary>등록/구매/취소 실패: 메시지를 표시하고, 상태 어긋남(이미 팔림 등) 복구를 위해
        /// 서버 응답이 있는 오류일 때만 목록을 1회 재조회한다(연결 실패는 재조회 안 함).</summary>
        private void OnActionError(NetworkError error)
        {
            _busy = false;
            Debug.LogWarning($"[Trade] 거래 요청 실패: {error}");
            SetMessage(ErrorMessages.ToKorean(error));
            if (!error.IsTransportError)
            {
                RequestList();
            }
        }

        private void SetMessage(string message)
        {
            if (_messageText != null)
            {
                _messageText.text = message ?? string.Empty;
            }
        }

        /// <summary>보유 골드량과 골드 아이콘(item_1)을 세션 재화에서 갱신한다(인벤토리 패널과 동일 규칙).</summary>
        private void RefreshGold()
        {
            long gold = 0;
            var currencies = Session.GameData != null ? Session.GameData.currencies : null;
            if (currencies != null)
            {
                foreach (var c in currencies)
                {
                    if (c != null && c.currencyType == GoldCurrencyType)
                    {
                        gold = c.amount;
                    }
                }
            }
            if (_goldText != null)
            {
                _goldText.text = gold.ToString("N0");
            }
            if (_goldIcon != null && _goldIcon.sprite == null)
            {
                var iconDb = ItemIconDatabase.Load();
                var sp = iconDb != null ? iconDb.Get(GoldItemCode) : null;
                if (sp != null)
                {
                    _goldIcon.sprite = sp;
                    _goldIcon.enabled = true;
                }
            }
        }

        /// <summary>아이템 코드의 마스터 정의(없으면 null).</summary>
        private static TaskbarHero.Common.MasterData.ItemMaster FindItem(int itemCode)
        {
            MasterDataManager.EnsureLoaded();
            var db = MasterDataManager.Db;
            return db != null && db.Items.TryGetValue(itemCode, out var def) ? def : null;
        }

        /// <summary>아이템 코드의 표시 이름(없으면 "아이템 {code}").</summary>
        private static string ItemName(int itemCode)
        {
            var def = FindItem(itemCode);
            return def != null ? def.name : $"아이템 {itemCode}";
        }

        /// <summary>패널을 닫는다(UIManager 우선).</summary>
        public void Close()
        {
            if (UIManager.Instance != null)
            {
                UIManager.Instance.Hide(UIManager.PanelType.Trade);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }

        // ── UI 헬퍼 ──

        /// <summary>목록 영역에 세로 스크롤을 붙인다(뷰포트 = 배경, 콘텐츠 = 세로 레이아웃 자식).
        /// RectMask2D는 넘치는 부분을 '잘라내기만' 하므로, 이것이 없으면 아래 행이 보이지 않는다.</summary>
        private static void AddVerticalScroll(GameObject viewportGo, RectTransform viewport, RectTransform content)
        {
            var scroll = viewportGo.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
        }

        /// <summary>9-slice 프레임/버튼 스프라이트를 적용한다(미배선이면 단색 폴백 유지).</summary>
        private static void ApplySliced(Image img, Sprite sprite)
        {
            if (img == null || sprite == null) return;
            img.sprite = sprite;
            img.type = Image.Type.Sliced;
            img.color = Color.white;
        }

        /// <summary>고정 크기 스프라이트(아이콘·닫기 버튼)를 적용한다.</summary>
        private static void ApplySimple(Image img, Sprite sprite)
        {
            if (img == null || sprite == null) return;
            img.sprite = sprite;
            img.type = Image.Type.Simple;
            img.color = Color.white;
        }

        /// <summary>양피지 입력창 위에 올릴 InputField(텍스트 + 플레이스홀더)를 만든다.</summary>
        private InputField BuildInput(RectTransform parent, string placeholder)
        {
            var text = NewText("Text", parent, string.Empty, 26, TextAnchor.MiddleLeft);
            text.color = new Color(0.22f, 0.16f, 0.09f);
            text.supportRichText = false;
            var trt = text.rectTransform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(18f, 6f);
            trt.offsetMax = new Vector2(-18f, -6f);

            var ph = NewText("Placeholder", parent, placeholder, 24, TextAnchor.MiddleLeft);
            ph.color = new Color(0.42f, 0.34f, 0.22f, 0.7f);
            ph.fontStyle = FontStyle.Italic;
            var phrt = ph.rectTransform;
            phrt.anchorMin = Vector2.zero;
            phrt.anchorMax = Vector2.one;
            phrt.offsetMin = new Vector2(18f, 6f);
            phrt.offsetMax = new Vector2(-18f, -6f);

            var input = parent.gameObject.AddComponent<InputField>();
            input.textComponent = text;
            input.placeholder = ph;
            input.lineType = InputField.LineType.SingleLine;
            return input;
        }

        /// <summary>표 머리 라벨 한 칸.</summary>
        private void AddHeaderLabel(RectTransform header, string label, float x, float width, TextAnchor anchor)
        {
            var t = NewText(label, header, label, 24, anchor);
            t.color = new Color(1f, 0.90f, 0.70f, 0.9f);
            t.fontStyle = FontStyle.Bold;
            PlaceMiddleLeft(t.rectTransform, x, width, 32f);
        }

        /// <summary>페이지 이동 버튼(좌/우).</summary>
        private Button BuildPageButton(RectTransform parent, string name, string label, float offsetX)
        {
            var img = NewImage(name, parent, new Color(0.40f, 0.30f, 0.18f, 1f));
            ApplySliced(img, _btnWood);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(offsetX, 128f);
            rt.sizeDelta = new Vector2(72f, 56f);
            var t = NewText("Label", rt, label, 26, TextAnchor.MiddleCenter);
            t.fontStyle = FontStyle.Bold;
            Stretch(t.rectTransform);
            return img.gameObject.AddComponent<Button>();
        }

        private static RectTransform NewChild(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static Image NewImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            return img;
        }

        private Text NewText(string name, Transform parent, string content, int size, TextAnchor anchor)
        {
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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

        /// <summary>행 안에서 좌측 기준 x 위치·크기로 세로 중앙 배치한다.</summary>
        private static void PlaceMiddleLeft(RectTransform rt, float x, float width, float height)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(x, 0f);
            rt.sizeDelta = new Vector2(width, height);
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
