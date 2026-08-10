using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TaskbarHero.Client.Managers;
using TaskbarHero.Client.MasterData;
using TaskbarHero.Common;
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
        private const float PanelWidth = 1120f;
        private const float PanelHeight = 1310f;  // 플레이 모드에서 높인 값

        // ── 내용 영역(배경 프레임 ui_bg_2 테두리 안쪽 빈 칸) ──
        // 프레임은 9-slice가 아니라 Simple로 늘려 그리므로 테두리 두께가 창 크기에 비례한다. 그래서 창 크기
        // 기준의 고정 여백(옛 ListInset 96 · ContentTopShift 187)으로는 폭 1120에서 좌우 약 154·144인
        // 테두리를 넘어 내용이 테두리 위에 얹혔다. 이제 <see cref="PanelFrame"/> 비율로 잡은 ContentArea
        // 안에만 내용을 놓고, 그 안의 모든 좌표는 <b>내용 영역 기준</b>이다(802.9 × 956.9).
        private const float ContentWidth = PanelWidth * (1f - PanelFrame.InsetLeft - PanelFrame.InsetRight)
            - PanelFrame.Pad * 2f;
        private const float ContentHeight = PanelHeight * (1f - PanelFrame.InsetTop - PanelFrame.InsetBottom)
            - PanelFrame.Pad * 2f;

        /// <summary>내용 영역 안에서 목록·검색줄이 좌우로 더 들이는 여백(프레임 여백은 ContentArea가 처리).</summary>
        private const float ListInset = 6f;

        // 행(=표 머리) 내부 열 좌표. 폭이 내용 영역에 따라 달라지므로 <b>오른쪽 '거래' 열부터 왼쪽으로</b>
        // 차례로 계산해 열이 서로 겹치거나 행 밖으로 나가지 않게 한다.
        private const float RowWidth = ContentWidth - ListInset * 2f - 20f; // 770.9 (콘텐츠 여백 10×2 제외)
        private const float RowNameX = 100f;      // 아이템 이름 열(아이콘 칸 20~92 오른쪽)
        private const float RowActionWidth = 120f;
        private const float RowActionX = RowWidth - 16f - RowActionWidth;   // 634.9 — 오른쪽 끝에서 여유 16
        private const float RowPriceWidth = 140f;
        private const float RowPriceX = RowActionX - 14f - RowPriceWidth;   // 480.9
        private const float RowCoinSize = 26f;
        private const float RowCoinX = RowPriceX - RowCoinSize;             // 454.9 — 가격 왼쪽에 붙는 코인 아이콘
        private const float RowQtyWidth = 80f;
        private const float RowQtyX = RowCoinX - 10f - RowQtyWidth;         // 364.9
        private const float RowNameWidth = RowQtyX - 10f - RowNameX;        // 254.9
        private const float RowRangeX = RowQtyX;                            // 판매 등록 탭: 시세 범위 열
        private const float RowRangeWidth = RowActionX - 14f - RowRangeX;   // 256

        // 내용 영역 <b>좌상단 기준</b> y(아래로 +). 위에서부터 제목 리본 → 탭 → 검색줄 → 표 머리 → 목록.
        private const float TitleBannerY = 0f;
        private const float TitleBannerWidth = 320f;
        private const float TitleBannerHeight = 72f;
        private const float GoldAreaY = 8f;       // 보유 골드 블록(제목 리본 오른쪽)
        private const float GoldAreaWidth = 230f;
        private const float GoldAreaHeight = 56f;
        private const float TabRowY = 80f;
        private const float TabHeight = 66f;
        private const float SearchRowY = 158f;
        private const float SearchRowHeight = 62f;
        private const float TableHeaderY = 232f;
        private const float TableHeaderHeight = 48f;
        private const float ListTopMargin = 286f;   // 표 머리 아래
        private const float ListBottomMargin = 108f; // 페이지 이동 줄 위까지
        private const float SellListTopMargin = 158f;    // 판매 등록 탭은 검색줄·표 머리가 없다
        private const float SellListBottomMargin = 306f; // 하단 등록 폼 위까지
        // 내용 영역 <b>바닥 기준</b> y(위로 +).
        private const float MessageHeight = 34f;
        private const float PageRowY = 44f;
        private const float PageRowHeight = 52f;
        private const float SellFormBottom = 44f;
        private const float SellFormHeight = 250f;

        private const float SearchButtonWidth = 160f;
        private const float TabWidth = 230f;      // 상단 탭 버튼 폭(3개 + 간격 = 714 ≤ 802.9)
        private const float TabGap = 12f;         // 탭 사이 간격
        private const float RowHeight = 82f;
        // 한 페이지 행 수는 목록 뷰포트 안에 다 들어가는 값으로 잡는다(스크롤 없이 한눈에 보이게).
        // 뷰포트 562.9 = 내용 높이 956.9 − 위 286(ListTop) − 아래 108(ListBottom).
        // 6행 = 6×82 + 5×8(간격) + 20(위아래 여백) = 552 ≤ 562.9.
        private const int PageSize = 6;
        // ── 색 팔레트 ──
        // 창 배경이 공용 프레임(ui_bg_2)으로 바뀌면서 안쪽 바닥이 <b>짙은 남색</b>(RGB 23,35,47)이 되었다.
        // 거래소 아트 키트는 <b>따뜻한 갈색·양피지</b> 계열이라 이 바닥 위에서 겉돌았고, 행 위의 짙은 갈색
        // 글자(수량·가격·시세)와 빈 목록 안내는 어두운 면 위에 어두운 글자라 거의 읽히지 않았다.
        //
        // <para><b>어떻게 맞췄나</b> — ① 목록 행은 아트(row_normal/row_alt)가 <b>붉은 갈색</b>이라 곱셈 틴트로는
        // 남색이 될 수 없으므로 스프라이트를 빼고 프레임 안쪽과 같은 남색 평면으로 깐다(가장 넓은 면).
        // ② 그 밖의 아트(표 머리·리본·탭·폼·버튼·입력창)는 아래 <c>ArtTint*</c> 틴트로 <b>짙고 푸른 쪽</b>으로
        // 눌러 프레임의 색조에 맞춘다(픽셀 디테일과 기능별 색 구분은 유지). ③ 글자는 놓이는 면에 맞춰 다시 고른다.</para>
        private static readonly Color SurfaceRow = new Color(0.13f, 0.16f, 0.23f, 0.96f);      // 목록 행(홀수)
        private static readonly Color SurfaceRowAlt = new Color(0.17f, 0.20f, 0.28f, 0.96f);   // 목록 행(짝수)
        private static readonly Color SurfaceSuggest = new Color(0.10f, 0.12f, 0.18f, 0.98f);  // 자동 완성 후보 목록
        private static readonly Color TextPrimary = new Color(0.91f, 0.93f, 0.97f);            // 어두운 면 위 본문
        private static readonly Color TextMuted = new Color(0.66f, 0.70f, 0.78f, 0.9f);        // 어두운 면 위 보조
        private static readonly Color TextGold = new Color(1f, 0.86f, 0.38f);                  // 금액(프레임 금색 장식과 같은 톤)
        private static readonly Color TextTitle = new Color(1f, 0.92f, 0.72f);                 // 제목·표 머리 글자

        // 거래소 아트 키트에 얹는 톤. 키트 원색이 <b>밝은 양피지·금색·주황</b> 계열이라 프레임 안쪽의 짙은
        // 남색 위에서 지나치게 튄다. 그래서 <b>파랑 채널을 빨강보다 크게</b> 잡은 어두운 틴트를 곱해
        // 전체를 짙고 푸른 쪽으로 눌러 준다(아트의 픽셀 디테일·색 구분은 그대로 남는다).
        // 각 값은 그 스프라이트의 실제 원색을 재서, 결과가 짙은 대역(대략 0.1~0.4)에 오도록 고른 것이다.
        //
        // 입력창 field_search(0.83,0.68,0.47) → (0.16,0.19,0.26) 짙은 남색
        private static readonly Color ArtTintField = new Color(0.19f, 0.28f, 0.55f, 1f);
        // 표 머리 bar_table_header(0.45,0.25,0.16) → (0.16,0.12,0.11)
        private static readonly Color ArtTintSurface = new Color(0.36f, 0.46f, 0.70f, 1f);
        // 제목 리본·탭(0.45,0.26,0.16 / 0.36,0.20,0.13 / 선택 0.52,0.30,0.18) → 0.18~0.26 대역(선택 탭은 더 밝게 남는다)
        private static readonly Color ArtTintPanel = new Color(0.50f, 0.58f, 0.80f, 1f);
        // 버튼 — 금색(0.86,0.62,0.17)→(0.39,0.30,0.10) · 파랑(0.17,0.47,0.76)→(0.08,0.23,0.46)
        //        · 빨강(0.65,0.18,0.18)→(0.29,0.09,0.11) · 나무(0.35,0.20,0.12)→(0.16,0.10,0.07).
        // 기능별 색 구분(구매·등록=금색, 검색·선택=파랑, 취소=빨강)은 유지하면서 밝기만 낮춘다.
        private static readonly Color ArtTintButton = new Color(0.45f, 0.48f, 0.60f, 1f);
        // 이미 어두운 아트(panel_wood 0.14,0.08,0.10)는 더 누르면 검게 죽으므로 살짝만 푸르게 기울인다.
        private static readonly Color ArtTintDarkArt = new Color(0.72f, 0.84f, 1f, 1f);

        // 아트가 배선되지 않았을 때만 쓰는 대체 색(각 스프라이트의 원래 색에 가깝게 둔다).
        private static readonly Color FallbackBanner = new Color(0.23f, 0.15f, 0.13f, 1f);
        private static readonly Color FallbackTab = new Color(0.18f, 0.12f, 0.11f, 1f);
        private static readonly Color FallbackHeader = new Color(0.16f, 0.12f, 0.11f, 1f);
        private static readonly Color FallbackField = new Color(0.16f, 0.19f, 0.26f, 1f);
        private static readonly Color FallbackForm = new Color(0.10f, 0.07f, 0.10f, 1f);
        private static readonly Color FallbackButtonGold = new Color(0.39f, 0.30f, 0.10f, 1f);
        private static readonly Color FallbackButtonBlue = new Color(0.08f, 0.23f, 0.46f, 1f);
        private static readonly Color FallbackButtonRed = new Color(0.29f, 0.09f, 0.11f, 1f);
        private static readonly Color FallbackButtonWood = new Color(0.16f, 0.10f, 0.07f, 1f);

        private const int GoldCurrencyType = 1;  // 재화 타입 1 = 골드
        private const int GoldItemCode = 1;      // item_master 골드 코드(아이콘 item_1)
        private const float PriceMinRate = 0.8f; // 기준가 ±20%(기획서 §3) — 안내용, 최종 판정은 서버
        private const float PriceMaxRate = 1.2f;

        [Header("UI 리소스 (Assets/Art/UI/Trade — 에디터 빌더가 배선)")]
        [SerializeField] private Sprite _windowFrame;      // ui_bg_2(인벤토리·스킬·룬·큐브 패널과 공용 프레임)
        [Tooltip("panel_parchment — 현재 화면에 쓰지 않는다(목록 배경은 창 프레임 내부가 보이도록 투명). 배선만 유지.")]
        [SerializeField] private Sprite _panelParchment;   // panel_parchment
        [SerializeField] private Sprite _panelWood;        // panel_wood (하단 폼)
        [SerializeField] private Sprite _tableHeader;      // bar_table_header
        [Tooltip("row_normal/row_alt — 현재 화면에 쓰지 않는다(목록 행은 프레임 안쪽과 같은 남색 평면). 배선만 유지.")]
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
        [Tooltip("검색어 자동 완성 후보 목록(검색창 바로 아래). 기본 비활성.")]
        [SerializeField] private RectTransform _suggestBox;
        [SerializeField] private RectTransform _suggestContent;
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
        private readonly List<GameObject> _suggestRows = new List<GameObject>();
        private bool _suppressSuggest;    // 후보를 골라 검색창 값을 넣는 중(다시 후보를 띄우지 않도록)

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
            // 내용물은 모두 프레임 테두리 안쪽 빈 칸에만 놓는다(테두리·상단 장식을 침범하지 않도록).
            var content = PanelFrame.CreateContentArea(panel);
            BuildHeader(content);
            BuildGoldArea(content); // 보유 골드 표시(제목 리본 오른쪽, 인벤토리와 동일 표기)
            BuildTabs(content);
            BuildListTab(content);
            BuildSellTab(content);
            BuildMessage(content);
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
            // 창 배경 프레임은 <b>원색 그대로</b> 둔다 — 이 프레임의 짙은 남색이 안쪽 색조의 기준이므로
            // 여기에 틴트를 곱하면 배경이 통째로 어두워진다(키트 아트에만 틴트를 건다).
            ApplySliced(img, _windowFrame, Color.white);
            var rt = img.rectTransform;
            rt.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            // 화면 중앙이 아니라 전투 화면 왼쪽 옆에 일정 간격(GameViewLayout.PanelGap)을 두고 붙인다 — 전투를 가리지 않는다.
            SidePanel.Attach(rt, SidePanel.Side.Left);
            return rt;
        }

        /// <summary>상단 타이틀 리본. <b>닫기(X) 버튼은 두지 않는다</b> — 다른 패널과 같이 미관상 제거했고
        /// 창 밖(딤) 클릭으로 닫는다. 내용 영역 맨 윗줄 가운데에 놓으며, 오른쪽에 보유 골드 블록이 온다.</summary>
        private void BuildHeader(RectTransform content)
        {
            var ribbon = NewImage("TitleBanner", content, FallbackBanner);
            ApplySliced(ribbon, _bannerRibbon, ArtTintPanel);
            var brt = ribbon.rectTransform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 1f);
            brt.pivot = new Vector2(0.5f, 1f);
            brt.anchoredPosition = new Vector2(0f, -TitleBannerY);
            brt.sizeDelta = new Vector2(TitleBannerWidth, TitleBannerHeight);

            var title = NewText("Title", brt, "거래소", 40, TextAnchor.MiddleCenter);
            title.fontStyle = FontStyle.Bold;
            title.color = TextTitle;
            Stretch(title.rectTransform);
        }

        /// <summary>보유 골드 영역(골드 아이콘 + 수량)을 구성한다. 인벤토리 패널과 같은 규격·표기이며,
        /// 아이콘·수량은 런타임에 세션 재화에서 채운다(<see cref="RefreshGold"/>).
        /// 내용 영역 <b>오른쪽 위</b>(제목 리본 옆)에 둔다.</summary>
        private void BuildGoldArea(RectTransform content)
        {
            var area = NewImage("GoldArea", content, new Color(0f, 0f, 0f, 0.35f));
            var art = area.rectTransform;
            art.anchorMin = art.anchorMax = new Vector2(1f, 1f);
            art.pivot = new Vector2(1f, 1f);
            art.anchoredPosition = new Vector2(0f, -GoldAreaY);
            art.sizeDelta = new Vector2(GoldAreaWidth, GoldAreaHeight);
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

            _goldText = NewText("GoldText", art, "0", 30, TextAnchor.MiddleLeft);
            _goldText.fontStyle = FontStyle.Bold;
            _goldText.color = new Color(1f, 0.90f, 0.55f);
            var grt = _goldText.rectTransform;
            grt.anchorMin = grt.anchorMax = new Vector2(0f, 0.5f);
            grt.pivot = new Vector2(0f, 0.5f);
            grt.anchoredPosition = new Vector2(58f, 0f);
            grt.sizeDelta = new Vector2(GoldAreaWidth - 66f, 44f);
        }

        /// <summary>상단 탭 3개: [판매 목록] [판매 등록] [판매 현황]. 가운데 정렬로 나란히 놓는다.</summary>
        private void BuildTabs(RectTransform content)
        {
            _listTabImage = BuildTab(content, "ListTab", "판매 목록", -(TabWidth + TabGap));
            _listTabButton = _listTabImage.gameObject.AddComponent<Button>();

            _sellTabImage = BuildTab(content, "SellTab", "판매 등록", 0f);
            _sellTabButton = _sellTabImage.gameObject.AddComponent<Button>();

            // '판매 등록' 오른쪽 — 내가 등록한 매물(취소 대상)을 보는 탭.
            _mineTabImage = BuildTab(content, "MineTab", "판매 현황", TabWidth + TabGap);
            _mineTabButton = _mineTabImage.gameObject.AddComponent<Button>();
        }

        /// <summary>탭 버튼 하나(내용 영역 가운데 기준 x 오프셋).</summary>
        private Image BuildTab(RectTransform content, string name, string label, float offsetX)
        {
            var img = NewImage(name, content, FallbackTab);
            ApplySliced(img, _btnCategory, ArtTintPanel);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(offsetX, -TabRowY);
            rt.sizeDelta = new Vector2(TabWidth, TabHeight);
            var t = NewText("Label", rt, label, 30, TextAnchor.MiddleCenter);
            t.fontStyle = FontStyle.Bold;
            Stretch(t.rectTransform);
            return img;
        }

        /// <summary>판매 목록 탭: 검색줄 · 표 머리 · 목록 · 페이지 이동.</summary>
        private void BuildListTab(RectTransform content)
        {
            var root = NewChild("ListTabRoot", content);
            Stretch(root);
            _listTabRoot = root.gameObject;

            // 검색줄(아이템 이름 → itemCode 변환).
            var field = NewImage("SearchField", root, FallbackField);
            ApplySliced(field, _fieldSearch, ArtTintField);
            var frt = field.rectTransform;
            frt.anchorMin = new Vector2(0f, 1f);
            frt.anchorMax = new Vector2(1f, 1f);
            frt.pivot = new Vector2(0.5f, 1f);
            // 검색창 오른쪽에 [검색] 버튼 자리를 비운다.
            frt.offsetMin = new Vector2(ListInset, 0f);
            frt.offsetMax = new Vector2(-(ListInset + SearchButtonWidth + 12f), 0f);
            frt.sizeDelta = new Vector2(frt.sizeDelta.x, SearchRowHeight);
            frt.anchoredPosition = new Vector2(frt.anchoredPosition.x, -SearchRowY);
            _searchInput = BuildInput(field.rectTransform, "아이템 이름으로 검색(비우면 전체)");

            var searchBtn = NewImage("SearchButton", root, FallbackButtonBlue);
            ApplySliced(searchBtn, _btnBlue, ArtTintButton);
            var sbrt = searchBtn.rectTransform;
            sbrt.anchorMin = sbrt.anchorMax = new Vector2(1f, 1f);
            sbrt.pivot = new Vector2(1f, 1f);
            sbrt.anchoredPosition = new Vector2(-ListInset, -SearchRowY);
            sbrt.sizeDelta = new Vector2(SearchButtonWidth, SearchRowHeight);
            var sbt = NewText("Label", sbrt, "검색", 28, TextAnchor.MiddleCenter);
            sbt.fontStyle = FontStyle.Bold;
            Stretch(sbt.rectTransform);
            _searchButton = searchBtn.gameObject.AddComponent<Button>();

            // 표 머리.
            var header = NewImage("TableHeader", root, FallbackHeader);
            ApplySliced(header, _tableHeader, ArtTintSurface);
            var hrt = header.rectTransform;
            hrt.anchorMin = new Vector2(0f, 1f);
            hrt.anchorMax = new Vector2(1f, 1f);
            hrt.pivot = new Vector2(0.5f, 1f);
            // 표 머리를 행과 같은 폭(콘텐츠 여백 10 포함)으로 잡아야 열 좌표가 정확히 맞는다.
            hrt.offsetMin = new Vector2(ListInset + 10f, 0f);
            hrt.offsetMax = new Vector2(-(ListInset + 10f), 0f);
            hrt.sizeDelta = new Vector2(hrt.sizeDelta.x, TableHeaderHeight);
            hrt.anchoredPosition = new Vector2(0f, -TableHeaderY);
            AddHeaderLabel(hrt, "아이템", RowNameX, RowNameWidth, TextAnchor.MiddleLeft);
            AddHeaderLabel(hrt, "수량", RowQtyX, RowQtyWidth, TextAnchor.MiddleCenter);
            AddHeaderLabel(hrt, "가격", RowPriceX, RowPriceWidth, TextAnchor.MiddleRight);
            AddHeaderLabel(hrt, "거래", RowActionX, RowActionWidth, TextAnchor.MiddleCenter);

            // 목록 영역. 배경(양피지)은 깔지 않고 <b>투명</b>하게 둔다 — 창 배경 프레임(ui_bg_2)의 어두운
            // 내부가 그대로 보이게 하려는 것이며, 마스크·스크롤 기능 때문에 오브젝트 자체는 남긴다.
            var listBg = NewImage("ListBackground", root, new Color(1f, 1f, 1f, 0f));
            var lbrt = listBg.rectTransform;
            lbrt.anchorMin = new Vector2(0f, 0f);
            lbrt.anchorMax = new Vector2(1f, 1f);
            lbrt.offsetMin = new Vector2(ListInset, ListBottomMargin);
            lbrt.offsetMax = new Vector2(-ListInset, -ListTopMargin);
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
            _emptyText.color = TextMuted;
            var ert = _emptyText.rectTransform;
            ert.anchorMin = ert.anchorMax = new Vector2(0.5f, 0.5f);
            ert.sizeDelta = new Vector2(600f, 60f);
            ert.anchoredPosition = Vector2.zero;
            _emptyText.gameObject.SetActive(false);

            // 페이지 이동(내용 영역 바닥 기준 — 그 아래는 메시지 한 줄뿐이다).
            _prevPageButton = BuildPageButton(root, "PrevPage", "◀", -110f);
            _nextPageButton = BuildPageButton(root, "NextPage", "▶", 110f);
            _pageText = NewText("PageText", root, "1", 28, TextAnchor.MiddleCenter);
            _pageText.color = TextTitle;
            var prt = _pageText.rectTransform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0f);
            prt.pivot = new Vector2(0.5f, 0f);
            prt.anchoredPosition = new Vector2(0f, PageRowY);
            prt.sizeDelta = new Vector2(160f, PageRowHeight);

            // 검색어 자동 완성 후보. **맨 마지막에 만든다** — 형제 중 가장 나중에 그려져야 표 머리·목록 위에 뜬다.
            BuildSuggestBox(root);
        }

        // ── 검색어 자동 완성(item_master) ──

        private const float SuggestRowHeight = 52f;   // 후보 한 줄 높이
        private const int SuggestMaxCount = 6;        // 한 번에 보여 주는 후보 수(스크롤 없이 다 보이는 개수)
        private const float SuggestPadding = 6f;      // 후보 목록 위·아래 여백

        /// <summary>검색창 바로 아래에 뜨는 자동 완성 후보 목록(기본 비활성). 후보 줄은 런타임에 채운다.</summary>
        private void BuildSuggestBox(RectTransform root)
        {
            var bg = NewImage("SuggestBox", root, SurfaceSuggest);
            var rt = bg.rectTransform;
            // 검색창과 같은 폭·왼쪽 정렬로 창 위쪽에 매단다(검색창 아래 = SearchRowY + 검색창 높이 62).
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(ListInset, 0f);
            rt.offsetMax = new Vector2(-(ListInset + SearchButtonWidth + 12f), 0f);
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, SuggestRowHeight * SuggestMaxCount + SuggestPadding * 2f);
            rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, -(SearchRowY + SearchRowHeight + 4f));
            _suggestBox = rt;

            var content = NewChild("Content", rt);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = new Vector2(SuggestPadding, 0f);
            content.offsetMax = new Vector2(-SuggestPadding, 0f);
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, -SuggestPadding);
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 0f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _suggestContent = content;

            bg.gameObject.SetActive(false);
        }

        /// <summary>판매 등록 탭: 판매 가능 아이템 목록 · 가격 입력 · 등록 버튼.</summary>
        private void BuildSellTab(RectTransform content)
        {
            var root = NewChild("SellTabRoot", content);
            Stretch(root);
            _sellTabRoot = root.gameObject;

            // 목록 영역. '판매 목록' 탭과 같이 배경(양피지)을 깔지 않고 <b>투명</b>하게 둔다 — 창 배경
            // 프레임(ui_bg_2)의 어두운 내부가 그대로 보이게 하려는 것이며, 마스크·스크롤 기능 때문에
            // 오브젝트 자체는 남긴다.
            var listBg = NewImage("SellListBackground", root, new Color(1f, 1f, 1f, 0f));
            var lbrt = listBg.rectTransform;
            lbrt.anchorMin = new Vector2(0f, 0f);
            lbrt.anchorMax = new Vector2(1f, 1f);
            lbrt.offsetMin = new Vector2(ListInset, SellListBottomMargin);
            lbrt.offsetMax = new Vector2(-ListInset, -SellListTopMargin);
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
            var form = NewImage("SellForm", root, FallbackForm);
            ApplySliced(form, _panelWood, ArtTintDarkArt);
            var frt = form.rectTransform;
            frt.anchorMin = new Vector2(0f, 0f);
            frt.anchorMax = new Vector2(1f, 0f);
            frt.pivot = new Vector2(0.5f, 0f);
            frt.offsetMin = new Vector2(ListInset, 0f);
            frt.offsetMax = new Vector2(-ListInset, 0f);
            frt.sizeDelta = new Vector2(frt.sizeDelta.x, SellFormHeight);
            frt.anchoredPosition = new Vector2(0f, SellFormBottom);

            _sellHintText = NewText("SellHint", frt, "판매할 아이템을 선택하세요", 26, TextAnchor.UpperLeft);
            _sellHintText.color = new Color(1f, 0.92f, 0.72f, 0.9f);
            _sellHintText.horizontalOverflow = HorizontalWrapMode.Wrap;
            var shrt = _sellHintText.rectTransform;
            shrt.anchorMin = new Vector2(0f, 1f);
            shrt.anchorMax = new Vector2(1f, 1f);
            shrt.pivot = new Vector2(0.5f, 1f);
            shrt.offsetMin = new Vector2(24f, 0f);
            shrt.offsetMax = new Vector2(-24f, 0f);
            shrt.sizeDelta = new Vector2(shrt.sizeDelta.x, 96f);
            shrt.anchoredPosition = new Vector2(0f, -20f);

            var priceField = NewImage("PriceField", frt, FallbackField);
            ApplySliced(priceField, _fieldSearch, ArtTintField);
            var pfrt = priceField.rectTransform;
            pfrt.anchorMin = pfrt.anchorMax = new Vector2(0f, 0f);
            pfrt.pivot = new Vector2(0f, 0f);
            pfrt.anchoredPosition = new Vector2(24f, 34f);
            pfrt.sizeDelta = new Vector2(440f, 62f);
            _priceInput = BuildInput(pfrt, "판매 가격(골드)");
            _priceInput.contentType = InputField.ContentType.IntegerNumber;

            var regBtn = NewImage("RegisterButton", frt, FallbackButtonGold);
            ApplySliced(regBtn, _btnGold, ArtTintButton);
            var rbrt = regBtn.rectTransform;
            rbrt.anchorMin = rbrt.anchorMax = new Vector2(1f, 0f);
            rbrt.pivot = new Vector2(1f, 0f);
            rbrt.anchoredPosition = new Vector2(-24f, 34f);
            rbrt.sizeDelta = new Vector2(200f, 62f);
            var rbt = NewText("Label", rbrt, "등록", 30, TextAnchor.MiddleCenter);
            rbt.fontStyle = FontStyle.Bold;
            Stretch(rbt.rectTransform);
            _registerButton = regBtn.gameObject.AddComponent<Button>();
        }

        /// <summary>하단 공용 안내/오류 메시지(내용 영역 맨 아랫줄).</summary>
        private void BuildMessage(RectTransform content)
        {
            _messageText = NewText("Message", content, string.Empty, 26, TextAnchor.MiddleCenter);
            _messageText.color = new Color(1f, 0.72f, 0.42f);
            var mrt = _messageText.rectTransform;
            mrt.anchorMin = mrt.anchorMax = new Vector2(0.5f, 0f);
            mrt.pivot = new Vector2(0.5f, 0f);
            mrt.anchoredPosition = new Vector2(0f, 0f);
            mrt.sizeDelta = new Vector2(ContentWidth, MessageHeight);
        }

        private void WireRuntime()
        {
            // 가방·스킬 창처럼 배경의 빈 곳을 잡아 창을 끌어 옮길 수 있게 한다(목록 스크롤·버튼은 그대로).
            // 한 번 옮기면 그 자리를 기억하고, 열 때마다 하던 자동 도킹도 멈춘다(PanelDragMove 참고).
            PanelDragMove.Attach(transform.Find("PanelRoot") as RectTransform, "Trade");

            if (_closeButton != null) _closeButton.onClick.AddListener(Close);
            if (_dimButton != null) _dimButton.onClick.AddListener(Close);
            if (_listTabButton != null) _listTabButton.onClick.AddListener(ShowListTab);
            if (_sellTabButton != null) _sellTabButton.onClick.AddListener(ShowSellTab);
            if (_searchButton != null) _searchButton.onClick.AddListener(OnSearch);
            if (_searchInput != null)
            {
                // 자동 완성 — 입력이 바뀔 때마다 item_master에서 후보를 찾아 검색창 아래에 띄운다.
                _searchInput.onValueChanged.RemoveAllListeners();
                _searchInput.onValueChanged.AddListener(OnSearchTextChanged);
                // onEndEdit(엔터·포커스 해제)에는 검색을 걸지 않는다 — 후보를 클릭하면 <b>먼저</b> 검색창이
                // 포커스를 잃어 그 검색이 돌고, 그 과정에서 후보 줄이 지워져 클릭 자체가 사라진다.
            }
            HideSuggestions();
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
            ApplySliced(_listTabImage, tab == TradeTab.List ? _btnCategorySel : _btnCategory, ArtTintPanel);
            ApplySliced(_sellTabImage, tab == TradeTab.Sell ? _btnCategorySel : _btnCategory, ArtTintPanel);
            ApplySliced(_mineTabImage, tab == TradeTab.Mine ? _btnCategorySel : _btnCategory, ArtTintPanel);
            SetMessage(string.Empty);
            HideSuggestions(); // 탭을 옮기면 떠 있던 자동 완성 후보를 접는다

            if (tab == TradeTab.Sell)
            {
                // 판매 후보는 가방 아이템이라 탭을 열 때 서버에서 다시 받는다(창고와 같은 기준).
                // 도착 전에는 현재 캐시로 먼저 그려 빈 화면을 보이지 않게 한다.
                RebuildSellRows();
                InventoryLoader.ReloadBag(RebuildSellRowsIfOpen, OnListError);
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
            HideSuggestions();
            _searchItemCode = ResolveItemCode(_searchInput != null ? _searchInput.text : string.Empty, out string notFound);
            if (notFound != null)
            {
                SetMessage($"'{notFound}' 이름의 아이템을 찾지 못했습니다.");
                return;
            }
            _page = 0;
            RequestList();
        }

        /// <summary>이름으로 아이템 코드를 찾는다(대소문자·공백 무시). <b>정확히 일치 → 앞부분 일치 → 부분 일치</b>
        /// 순으로 고르므로, 자동 완성으로 고른 이름을 그대로 넣었을 때 다른 아이템이 잡히지 않는다
        /// (예: '검'을 이름에 포함하는 아이템이 여럿일 때).
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

            int prefix = 0;
            int contains = 0;
            foreach (var pair in db.Items)
            {
                string name = pair.Value != null ? pair.Value.name : null;
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }
                if (string.Equals(name, q, StringComparison.OrdinalIgnoreCase))
                {
                    return pair.Key;
                }
                if (prefix == 0 && name.StartsWith(q, StringComparison.OrdinalIgnoreCase))
                {
                    prefix = pair.Key;
                }
                else if (contains == 0 && name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    contains = pair.Key;
                }
            }
            if (prefix != 0) return prefix;
            if (contains != 0) return contains;
            notFound = q;
            return 0;
        }

        // ── 검색어 자동 완성 ──

        /// <summary>검색창 입력이 바뀔 때마다 <c>item_master</c>에서 이름이 맞는 아이템을 찾아 후보로 띄운다.
        /// 후보를 골라 값을 채우는 중(<see cref="_suppressSuggest"/>)에는 다시 열지 않는다.</summary>
        private void OnSearchTextChanged(string text)
        {
            if (_suppressSuggest)
            {
                return;
            }
            RefreshSuggestions(text);
        }

        /// <summary>후보 목록을 다시 만든다. 검색어가 비었거나 맞는 아이템이 없으면 목록을 숨긴다.</summary>
        private void RefreshSuggestions(string query)
        {
            ClearSuggestRows();
            string q = (query ?? string.Empty).Trim();
            if (_suggestBox == null || _suggestContent == null || q.Length == 0)
            {
                HideSuggestions();
                return;
            }

            var matches = FindItemsByName(q, SuggestMaxCount);
            if (matches.Count == 0)
            {
                HideSuggestions();
                return;
            }

            foreach (var def in matches)
            {
                _suggestRows.Add(BuildSuggestRow(def));
            }
            // 후보 수에 맞춰 높이를 줄여 빈 칸이 남지 않게 한다.
            _suggestBox.sizeDelta = new Vector2(
                _suggestBox.sizeDelta.x, SuggestRowHeight * matches.Count + SuggestPadding * 2f);
            _suggestBox.gameObject.SetActive(true);
            _suggestBox.SetAsLastSibling(); // 목록 갱신으로 형제 순서가 바뀌어도 항상 위에 그린다
        }

        /// <summary>
        /// <c>item_master</c>에서 이름이 검색어와 맞는 아이템을 최대 <paramref name="limit"/>개 찾는다.
        /// <para><b>거래소에 올라올 수 있는 아이템만</b> 후보로 둔다(<c>sellable=1</c> · 기준가 있음) —
        /// 그 밖의 아이템은 이름을 넣어도 매물이 나올 수 없어 후보로 띄우면 헛걸음이 된다.</para>
        /// <para>정렬은 <b>앞부분 일치 우선 → 이름이 짧은 것 → 코드</b> 순이다(같은 검색어에 항상 같은 순서).</para>
        /// </summary>
        private static List<TaskbarHero.Common.MasterData.ItemMaster> FindItemsByName(string query, int limit)
        {
            var found = new List<TaskbarHero.Common.MasterData.ItemMaster>();
            MasterDataManager.EnsureLoaded();
            var db = MasterDataManager.Db;
            if (db == null)
            {
                return found;
            }

            foreach (var pair in db.Items)
            {
                var def = pair.Value;
                if (def == null || string.IsNullOrEmpty(def.name) || def.sellable != 1 || def.basePrice <= 0)
                {
                    continue;
                }
                if (def.name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    found.Add(def);
                }
            }

            found.Sort((a, b) =>
            {
                bool pa = a.name.StartsWith(query, StringComparison.OrdinalIgnoreCase);
                bool pb = b.name.StartsWith(query, StringComparison.OrdinalIgnoreCase);
                if (pa != pb) return pa ? -1 : 1;
                int len = a.name.Length.CompareTo(b.name.Length);
                return len != 0 ? len : a.itemCode.CompareTo(b.itemCode);
            });

            if (found.Count > limit)
            {
                found.RemoveRange(limit, found.Count - limit);
            }
            return found;
        }

        /// <summary>후보 한 줄(등급색 이름 + 기준가). 누르면 그 아이템으로 바로 검색한다.</summary>
        private GameObject BuildSuggestRow(TaskbarHero.Common.MasterData.ItemMaster def)
        {
            var img = NewImage($"Suggest_{def.itemCode}", _suggestContent, new Color(1f, 1f, 1f, 0f));
            var le = img.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = SuggestRowHeight;
            le.minHeight = SuggestRowHeight;

            var name = NewText("Name", img.rectTransform, def.name, 26, TextAnchor.MiddleLeft);
            name.color = GradeColors.Name(def.grade);
            var nrt = name.rectTransform;
            nrt.anchorMin = new Vector2(0f, 0f);
            nrt.anchorMax = new Vector2(1f, 1f);
            nrt.offsetMin = new Vector2(14f, 0f);
            nrt.offsetMax = new Vector2(-150f, 0f);

            var price = NewText("Price", img.rectTransform, $"{def.basePrice:N0}", 22, TextAnchor.MiddleRight);
            price.color = TextGold;
            var prt = price.rectTransform;
            prt.anchorMin = new Vector2(1f, 0f);
            prt.anchorMax = new Vector2(1f, 1f);
            prt.pivot = new Vector2(1f, 0.5f);
            prt.sizeDelta = new Vector2(140f, 0f);
            prt.anchoredPosition = new Vector2(-14f, 0f);

            int code = def.itemCode;
            string itemName = def.name;
            var btn = img.gameObject.AddComponent<Button>();
            btn.onClick.AddListener(() => OnSuggestionChosen(code, itemName));
            return img.gameObject;
        }

        /// <summary>후보를 골랐을 때 — 검색창을 그 이름으로 채우고 <b>바로 그 아이템 코드로</b> 조회한다
        /// (이름을 다시 코드로 되짚지 않으므로 이름이 겹쳐도 정확하다).</summary>
        private void OnSuggestionChosen(int itemCode, string itemName)
        {
            if (_searchInput != null)
            {
                _suppressSuggest = true;   // 값 대입이 다시 후보를 띄우지 않게 한다
                _searchInput.text = itemName;
                _suppressSuggest = false;
            }
            HideSuggestions();
            SetMessage(string.Empty);
            _searchItemCode = itemCode;
            _page = 0;
            RequestList();
        }

        /// <summary>후보 목록을 숨긴다(줄도 함께 비운다).</summary>
        private void HideSuggestions()
        {
            ClearSuggestRows();
            if (_suggestBox != null)
            {
                _suggestBox.gameObject.SetActive(false);
            }
        }

        /// <summary>런타임에 만든 후보 줄을 모두 제거한다.</summary>
        private void ClearSuggestRows()
        {
            foreach (var go in _suggestRows)
            {
                if (go != null)
                {
                    Destroy(go);
                }
            }
            _suggestRows.Clear();
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

            // 행 배경은 아트(row_normal/row_alt)를 쓰지 않는다 — 붉은 갈색이라 프레임 안쪽 남색과 겉돈다.
            // 홀짝을 색으로 갈라 줄 구분은 유지한다.
            var rowImg = NewImage("ListingRow", _listContent, index % 2 == 0 ? SurfaceRow : SurfaceRowAlt);
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
            nameText.color = GradeColors.Name(def != null ? def.grade : 1);
            nameText.fontStyle = FontStyle.Bold;
            PlaceMiddleLeft(nameText.rectTransform, RowNameX, RowNameWidth, 40f);

            var qtyText = NewText("Quantity", rt, listing.quantity > 1 ? $"x{listing.quantity}" : "-", 26, TextAnchor.MiddleCenter);
            qtyText.color = TextPrimary;
            PlaceMiddleLeft(qtyText.rectTransform, RowQtyX, RowQtyWidth, 40f);

            var priceText = NewText("Price", rt, $"{listing.price:N0}", 26, TextAnchor.MiddleRight);
            priceText.color = TextGold;
            priceText.fontStyle = FontStyle.Bold;
            PlaceMiddleLeft(priceText.rectTransform, RowPriceX, RowPriceWidth, 40f);

            if (_iconCoin != null)
            {
                var coin = NewImage("Coin", rt, Color.white);
                ApplySimple(coin, _iconCoin);
                PlaceMiddleLeft(coin.rectTransform, RowCoinX, RowCoinSize, RowCoinSize);
            }

            // 남의 등록 = 구매, 내 등록 = 취소.
            var actionImg = NewImage(mine ? "CancelButton" : "BuyButton", rt,
                mine ? FallbackButtonRed : FallbackButtonGold);
            ApplySliced(actionImg, mine ? _btnRed : _btnGold, ArtTintButton);
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
                    view.SetEnhanceLevel(listing.enhanceLevel); // 강화 장비는 슬롯에도 "+N"을 표시한다
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
                // 산 아이템은 우편함으로 가므로 가방은 그대로다 — 차감된 골드(balance)만 반영한다.
                Session.ApplyBalance(data != null ? data.balance : null);
                RefreshAfterTrade();
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
                var data = resp != null ? resp.data : null;
                var restored = data != null ? data.restored : null;
                Debug.Log($"[Trade] 판매 취소 완료 listing={listingId}");
                string name = restored != null ? ItemName(restored.itemCode) : "아이템";
                ModalManager.Instance?.ShowConfirm("판매 취소", $"{name}이(가) 인벤토리로 돌아왔습니다.");
                Session.ApplyInventoryDelta(data != null ? data.inventoryDelta : null); // 가방으로 복귀
                RefreshAfterTrade();
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

        /// <summary>가방 캐시에서 판매 가능한 아이템(sellable=1 · 기준가 있음)을 나열한다.
        /// 서버 조회는 하지 않는다 — 판매 탭을 열 때 <see cref="SelectTab"/>이 한 번 받아 두고,
        /// 등록·취소 뒤에는 응답의 변경분이 이미 캐시에 반영돼 있다(§5.0 규약).</summary>
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

            // 가방 캐시(장착품·재화 제외)가 등록 후보다. 장착 중인 장비는 애초에 가방에 없어 등록 대상이 아니다(기획서 §2).
            var bag = Session.Bag;
            if (bag == null)
            {
                return;
            }
            int index = 0;
            foreach (var item in bag)
            {
                if (item == null)
                {
                    continue;
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

        /// <summary>가방 조회 완료 후 판매 등록 후보를 다시 그린다(판매 탭이 열려 있을 때만).</summary>
        private void RebuildSellRowsIfOpen()
        {
            if (this != null && gameObject.activeInHierarchy && _sellTabRoot != null && _sellTabRoot.activeSelf)
            {
                RebuildSellRows();
            }
        }

        /// <summary>판매 등록 후보 행(아이템 슬롯 · 이름 · 기준가 범위 · 선택 버튼).</summary>
        private GameObject BuildSellRow(InventoryItemDto item, TaskbarHero.Common.MasterData.ItemMaster def, int index)
        {
            var rowImg = NewImage("SellRow", _sellContent, index % 2 == 0 ? SurfaceRow : SurfaceRowAlt);
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
                    view.SetEnhanceLevel(item.enhanceLevel); // 강화 장비는 슬롯에도 "+N"을 표시한다
                }
            }

            string label = def.name + (item.enhanceLevel > 0 ? $" +{item.enhanceLevel}" : string.Empty) +
                           (item.quantity > 1 ? $" x{item.quantity}" : string.Empty);
            var nameText = NewText("Name", rt, label, 26, TextAnchor.MiddleLeft);
            nameText.color = GradeColors.Name(def.grade);
            nameText.fontStyle = FontStyle.Bold;
            PlaceMiddleLeft(nameText.rectTransform, RowNameX, RowNameWidth, 40f);

            var rangeText = NewText("Range", rt, PriceRangeLabel(def.basePrice), 22, TextAnchor.MiddleRight);
            rangeText.color = TextMuted;
            PlaceMiddleLeft(rangeText.rectTransform, RowRangeX, RowRangeWidth, 36f);

            var pick = NewImage("PickButton", rt, FallbackButtonBlue);
            ApplySliced(pick, _btnBlue, ArtTintButton);
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
                Session.ApplyInventoryDelta(data != null ? data.inventoryDelta : null); // 가방에서 빠져 에스크로로
                RefreshAfterTrade();
            }, OnActionError);
        }

        // ── 공통 후처리 ──

        /// <summary>거래 응답을 반영한 뒤 화면을 다시 그린다(재조회 없음). 가방 변화는 호출측이 응답의
        /// <c>inventoryDelta</c>(등록·취소)로, 골드는 <c>balance</c>(구매)로 이미 세션에 반영해 둔다.
        /// 거래소 목록 재조회는 다른 사람의 매물 상태를 받는 것이라 그대로 유지한다(가방 조회가 아니다).</summary>
        private void RefreshAfterTrade()
        {
            Session.RaiseInventoryChanged(); // 골드·아이템 표시(HUD·패널) 갱신 트리거
            _busy = false;
            RefreshGold(); // 구매·등록·취소로 골드가 바뀌었을 수 있다
            MailNotifier.Refresh(); // 판매 대금·반송 메일이 도착할 수 있으므로 알림 갱신
            RequestList();
            if (_sellTabRoot != null && _sellTabRoot.activeSelf)
            {
                RebuildSellRows();
            }
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
        /// 서버 응답이 있는 오류일 때만 목록을 1회 재조회한다(연결 실패는 재조회 안 함).
        /// 등록하려던 아이템이 이미 사라진 경우(<see cref="ErrorCode.ItemNotFound"/>)는 가방 캐시가 낡은 것이므로
        /// 계약대로 가방을 새로 고친다(세이브 기획서 5.2).</summary>
        private void OnActionError(NetworkError error)
        {
            _busy = false;
            Debug.LogWarning($"[Trade] 거래 요청 실패: {error}");
            SetMessage(ErrorMessages.ToKorean(error));
            if (error.ErrorCode == ErrorCode.ItemNotFound)
            {
                InventoryLoader.ReloadBag(RebuildSellRowsIfOpen);
            }
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
        /// <summary>9-slice 스프라이트를 얹고 <paramref name="tint"/>를 곱한다(밝은 키트 아트를 짙고 푸르게 누를 때).
        /// <para><b>기본은 원색(흰색)이다</b> — 틴트는 <b>거래소 키트 아트에만</b> 걸어야 한다. 기본값을 틴트로
        /// 두었더니 창 배경 프레임(<c>ui_bg_2</c>)까지 눌려 배경이 통째로 어두워졌다(플레이 모드에서 되돌린 값을
        /// 그대로 반영). 등급 슬롯처럼 <b>색 자체가 정보</b>인 아트도 원색으로 둬야 한다.</para></summary>
        private static void ApplySliced(Image img, Sprite sprite, Color? tint = null)
        {
            if (img == null || sprite == null) return;
            img.sprite = sprite;
            img.type = Image.Type.Sliced;
            img.color = tint ?? Color.white;
        }

        /// <summary>고정 크기 스프라이트(아이콘·닫기 버튼)를 적용한다.</summary>
        /// <summary>스프라이트를 늘려 얹는다. 아이콘은 원색이 곧 정보(코인=금색)라 기본은 원색 그대로 둔다.</summary>
        private static void ApplySimple(Image img, Sprite sprite, Color? tint = null)
        {
            if (img == null || sprite == null) return;
            img.sprite = sprite;
            img.type = Image.Type.Simple;
            img.color = tint ?? Color.white;
        }

        /// <summary>양피지 입력창 위에 올릴 InputField(텍스트 + 플레이스홀더)를 만든다.</summary>
        private InputField BuildInput(RectTransform parent, string placeholder)
        {
            var text = NewText("Text", parent, string.Empty, 26, TextAnchor.MiddleLeft);
            text.color = TextPrimary;
            text.supportRichText = false;
            var trt = text.rectTransform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(18f, 6f);
            trt.offsetMax = new Vector2(-18f, -6f);

            var ph = NewText("Placeholder", parent, placeholder, 24, TextAnchor.MiddleLeft);
            ph.color = TextMuted;
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
            t.color = TextTitle;
            t.fontStyle = FontStyle.Bold;
            PlaceMiddleLeft(t.rectTransform, x, width, 32f);
        }

        /// <summary>페이지 이동 버튼(좌/우).</summary>
        private Button BuildPageButton(RectTransform parent, string name, string label, float offsetX)
        {
            var img = NewImage(name, parent, FallbackButtonWood);
            ApplySliced(img, _btnWood, ArtTintButton);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(offsetX, PageRowY);
            rt.sizeDelta = new Vector2(68f, PageRowHeight);
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
