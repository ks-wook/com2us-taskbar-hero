using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TaskbarHero.Client.Managers;
using TaskbarHero.Client.MasterData;
using TaskbarHero.Common;
using TaskbarHero.Common.Dto;

namespace TaskbarHero.Client.UI.Gacha
{
    /// <summary>
    /// 뽑기(가챠) 패널(가챠 기획서 §2·§5). 하단 HUD의 '뽑기' 아이콘으로 진입한다.
    /// <list type="bullet">
    /// <item><b>배너 목록</b> — 열 때 <c>POST /api/game/gacha/banners</c>로 <b>지금 돌릴 수 있는 배너</b>와
    ///   내 천장 진행도를 받는다. 노출 판정은 서버 시각 기준이라 서버가 하고, 배너 이름·이미지·비용·등급 확률은
    ///   <b>클라이언트 번들 마스터</b>(<c>gacha_master</c>)에서 읽는다(기획서 §5 서두).</item>
    /// <item><b>1연 · 10연 뽑기</b> — <c>POST /api/game/gacha/pull</c> 한 곳에 <c>pullType</c>만 달라진다.
    ///   <b>뽑는 횟수·확률·결과는 전부 서버 소유</b>이므로 요청에는 <c>gachaCode</c>와 <c>pullType</c>만 실린다.
    ///   결과는 <see cref="GachaResultOverlay"/>가 등급 연출 영상과 함께 재생한다.</item>
    /// <item><b>천장 진행도</b> — 서버가 내려준 <c>counters</c>로 "37 / 90 · 천장까지 53회"를 그리고,
    ///   소프트 천장 발동 구간(번들 마스터의 <c>pityRules</c> 기준 70회차)에서는 '확률 상승 중'을 덧붙인다.</item>
    /// <item><b>뽑기 기록</b> — <c>POST /api/game/gacha/history</c>를 커서 페이징으로 최신순 조회한다
    ///   (10연 한 묶음이 한 줄).</item>
    /// </list>
    /// 응답의 <c>balance</c>·<c>inventoryDelta</c>를 세션에 반영하므로 뽑은 뒤 가방·재화를 재조회하지 않는다
    /// (기획서 §5.2). 외형은 <c>Assets/Art/UI/Gacha</c>의 배너·탭·결과 아트와 공용 프레임(<c>modal_bg</c>·
    /// <c>ui_bg</c>·<c>pixel_rpg_button</c>)을 쓰며, 정적 계층은 에디터 빌더(<c>GachaUiBuilder</c>)가 프리팹에 굽고
    /// 배너 탭·기록 행은 조회 결과로 런타임에 생성한다.
    /// </summary>
    public class GachaPanelController : MonoBehaviour
    {
        private const float CanvasRefWidth = 1080f;
        private const float CanvasRefHeight = 1920f;
        // 좌측 패널 최대 폭(GameViewLayout.WidestLeftPanel과 같은 값)까지 쓴다 — 배너(620) + 정보 열(404)이 나란히 들어간다.
        private const float PanelWidth = 1120f;
        // 배너 아래에 천장 게이지가 없어져(천장은 배너 안 텍스트로 옮겼다) 하단 줄까지만 담는 높이로 줄였다.
        private const float PanelHeight = 1090f;
        private const float Inset = 36f;

        private const float TabWidth = 500f;      // gacha_btn_* 원본 512×160 비율
        private const float TabHeight = 156f;
        private const float TabGap = 40f;
        private const float TabRowY = 100f;       // 창 상단에서 탭 줄 위쪽까지

        private const float ContentTop = 268f;    // 탭 줄 아래 = 콘텐츠 시작
        // 배너는 창 폭 대부분을 쓰는 주인공이다(원본 718×558 비율 유지 — 940 / 731 = 1.2866).
        // 기간·픽업 안내와 뽑기 버튼을 배너 <b>안에</b> 얹으므로 배너 밖에는 천장 줄과 하단 줄만 남는다.
        private const float BannerWidth = 940f;
        private const float BannerHeight = 731f;
        private const float BannerBottom = ContentTop + BannerHeight;   // 999

        // 배너 위쪽 안내 띠(기간 + 픽업 설명). 아트 위에 글자를 얹으므로 반투명 판을 깔아 가독성을 확보한다.
        private const float BannerStripHeight = 96f;
        private const float BannerPadding = 24f;

        // 배너 좌측 하단 뽑기 버튼 — 가로로 나란히 두고 **10연을 오른쪽**에 둔다(무게가 큰 상품이 뒤에 온다).
        private const float PullButtonWidth = 260f;
        private const float PullButtonHeight = 92f;
        private const float PullButtonGap = 16f;

        // 천장 잔여 횟수는 10연 버튼 바로 위에 한 줄로 얹는다(게이지 없이 텍스트만).
        private const float PityTextHeight = 36f;
        private const float PityTextGap = 8f;

        // '확률 보기' 버튼은 창 우측 상단(닫기 버튼 왼쪽)에 두고, 팝업은 그 아래로 펼쳐진다.
        private const float ChanceButtonWidth = 200f;
        private const float ChanceButtonHeight = 52f;
        private const float ChanceButtonTop = 24f;
        private const float ChanceButtonRight = 96f;   // 닫기 버튼(우측 24 + 폭 60) 왼쪽으로 비켜 놓는다
        private const float ChancePopupWidth = 500f;
        private const float ChancePopupHeight = 430f;
        private const float ChancePopupGap = 10f;

        // 배너 아래에는 하단 줄(기록 버튼 · 안내 메시지)만 남는다.
        private const float BottomRowTop = BannerBottom + 12f;   // 1011
        private const float BottomRowHeight = 56f;

        private const int GoldCurrencyType = 1;   // 재화 타입 1 = 골드
        private const int GoldItemCode = 1;       // item_master 골드 코드(아이콘 item_1)
        private const int PityGrade = 5;          // 천장 표시 대상 등급(전설) — 규칙이 다른 등급에 붙으면 응답을 따라간다
        private const int SoftPityType = 1;
        private const int HistoryPageSize = 10;
        // 10연은 스택 병합으로 칸을 덜 쓰는 경우가 많아 "10칸 필요"로 막지 않는다(기획서 6.7).
        // 아래 여유보다 빈 칸이 적을 때만 사전 경고를 띄우고, 최종 판정은 서버(InventoryFull)가 한다.
        private const int MultiFreeSlotWarnThreshold = 4;

        [Header("배너 아트 (Assets/Art/UI/Gacha — 에디터 빌더가 코드별로 배선)")]
        [SerializeField] private GachaBannerArt[] _bannerArt;

        [Header("공용 UI 리소스 (에디터 빌더가 배선)")]
        [Tooltip("창 배경(Assets/Art/UI/modal_bg.png, 9-slice).")]
        [SerializeField] private Sprite _panelBackground;
        [Tooltip("내부 박스 배경(Assets/Art/UI/ui_bg.png, 9-slice).")]
        [SerializeField] private Sprite _boxBackground;
        [Tooltip("확률 공시 박스 배경(Assets/Art/UI/item_detail_bg.png, 9-slice).")]
        [SerializeField] private Sprite _detailBackground;
        [Tooltip("버튼 배경(Assets/Art/UI/pixel_rpg_button.png, 9-slice).")]
        [SerializeField] private Sprite _buttonSprite;

        [Header("구성 참조 (에디터 빌더가 배선)")]
        [SerializeField] private Button _closeButton;
        [SerializeField] private Button _dimButton;
        [SerializeField] private Image _goldIcon;
        [SerializeField] private Text _goldText;
        [SerializeField] private RectTransform _tabRow;
        [SerializeField] private GameObject _mainRoot;
        [SerializeField] private Image _bannerImage;
        [SerializeField] private Text _bannerFallbackName;
        [SerializeField] private GameObject _bannerStrip;
        [SerializeField] private Text _periodText;
        [SerializeField] private Text _pickupText;
        [SerializeField] private Button _chanceButton;
        [SerializeField] private Text _chanceButtonLabel;
        [SerializeField] private GameObject _chancePopup;
        [SerializeField] private RectTransform _chanceContent;
        [SerializeField] private Button _chanceCloseButton;
        [SerializeField] private Text _pityText;
        [SerializeField] private Button _singleButton;
        [SerializeField] private Text _singleCostText;
        [SerializeField] private Button _multiButton;
        [SerializeField] private Text _multiCostText;
        [SerializeField] private Text _multiGuaranteeText;
        [SerializeField] private Button _historyButton;
        [SerializeField] private Text _messageText;
        [SerializeField] private Text _emptyText;

        [Header("기록 화면 (에디터 빌더가 배선)")]
        [SerializeField] private GameObject _historyRoot;
        [SerializeField] private RectTransform _historyContent;
        [SerializeField] private Button _historyBackButton;
        [SerializeField] private Button _historyMoreButton;
        [SerializeField] private Text _historyEmptyText;

        [Header("결과 연출")]
        [Tooltip("뽑기 결과 오버레이(패널 프리팹 안의 자식). 에디터 빌더가 함께 굽는다.")]
        [SerializeField] private GachaResultOverlay _resultOverlay;

        private Font _font;
        private bool _busy;
        private int _selectedCode;
        private long _serverTime;              // 배너 조회 응답 시각(Unix ts) — 남은 기간의 기준점
        private float _serverTimeStamp;        // 그 응답을 받은 시점(Time.unscaledTime)
        private float _nextPeriodUpdate;
        private bool _reopenRequested;         // 기간이 끝나 목록을 다시 받아야 하는지(1회만)
        private readonly List<GachaBannerDto> _banners = new List<GachaBannerDto>();
        private readonly List<GameObject> _tabs = new List<GameObject>();
        private readonly List<GameObject> _historyRows = new List<GameObject>();
        private long _historyCursor;
        private bool _historyHasMore;

        private bool AlreadyBuilt => _tabRow != null;

        private void Awake()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (!AlreadyBuilt)
            {
                Construct(); // 폴백(프리팹 미배선 시)
            }
            WireRuntime();
        }

        /// <summary>패널이 표시될 때마다 배너 목록·천장 진행도를 새로 조회한다(노출 판정은 서버 시각 기준).</summary>
        private void OnEnable()
        {
            if (Application.isPlaying && AlreadyBuilt)
            {
                SetMessage(string.Empty);
                ShowMainView();
                RefreshGold();
                // 기간 종료로 인한 목록 재조회는 패널을 열 때마다 한 번만 허용한다(시계 오차로 무한 재조회 방지).
                _reopenRequested = false;
                RequestBanners();
            }
        }

        /// <summary>한정 배너의 남은 기간을 1초마다 갱신한다(로컬 시계가 아니라 서버 시각 + 경과 시간 기준).</summary>
        private void Update()
        {
            if (!Application.isPlaying || _serverTime <= 0 || Time.unscaledTime < _nextPeriodUpdate)
            {
                return;
            }
            _nextPeriodUpdate = Time.unscaledTime + 1f;
            UpdatePeriodUi();
        }

        /// <summary>에디터 빌드 전용: 전체 정적 계층을 생성해 프리팹에 굽는다.</summary>
        public void EditorConstruct()
        {
            Construct();
            if (_resultOverlay != null)
            {
                _resultOverlay.EditorConstruct();
            }
        }

        // ── 정적 계층 구성 ──

        /// <summary>캔버스·딤·창·헤더·탭 줄·배너 화면·천장·뽑기 버튼·기록 화면을 생성한다.</summary>
        private void Construct()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildCanvas();
            BuildDim();
            var panel = BuildPanel();
            BuildHeader(panel);
            BuildGoldArea(panel);
            BuildTabRow(panel);
            BuildMainView(panel);
            BuildHistoryView(panel);
            BuildMessage(panel);
            EnsureResultOverlay();
        }

        /// <summary>패널 전용 오버레이 캔버스(다른 패널과 동일 규격).</summary>
        private void BuildCanvas()
        {
            var canvas = gameObject.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = gameObject.AddComponent<Canvas>();
            }
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = UiSortingOrder.Panel;
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

        /// <summary>뽑기 창 본체(modal_bg 9-slice). 전투 화면 왼쪽에 간격을 두고 붙는다.</summary>
        private RectTransform BuildPanel()
        {
            var img = NewImage("PanelRoot", (RectTransform)transform, new Color(0.10f, 0.11f, 0.18f, 0.98f));
            ApplySliced(img, _panelBackground);
            var rt = img.rectTransform;
            rt.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            SidePanel.Attach(rt, SidePanel.Side.Left);
            return rt;
        }

        /// <summary>상단 제목과 닫기 버튼.</summary>
        private void BuildHeader(RectTransform panel)
        {
            var title = NewText("Title", panel, "뽑기", 44, TextAnchor.MiddleCenter);
            title.fontStyle = FontStyle.Bold;
            title.color = new Color(1f, 0.92f, 0.72f);
            var trt = title.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 1f);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.anchoredPosition = new Vector2(0f, -22f);
            trt.sizeDelta = new Vector2(400f, 60f);

            var close = NewImage("CloseButton", panel, new Color(0.42f, 0.20f, 0.20f, 1f));
            ApplySliced(close, _buttonSprite);
            var crt = close.rectTransform;
            crt.anchorMin = crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(1f, 1f);
            crt.anchoredPosition = new Vector2(-24f, -22f);
            crt.sizeDelta = new Vector2(60f, 56f);
            var xt = NewText("X", crt, "X", 30, TextAnchor.MiddleCenter);
            xt.fontStyle = FontStyle.Bold;
            Stretch(xt.rectTransform);
            _closeButton = close.gameObject.AddComponent<Button>();
        }

        /// <summary>좌상단 보유 골드(아이콘 + 수량). 인벤토리·거래소와 같은 규격이며 값은 런타임에 채운다.</summary>
        private void BuildGoldArea(RectTransform panel)
        {
            var area = NewImage("GoldArea", panel, new Color(0f, 0f, 0f, 0.35f));
            var art = area.rectTransform;
            art.anchorMin = art.anchorMax = new Vector2(0f, 1f);
            art.pivot = new Vector2(0f, 1f);
            art.anchoredPosition = new Vector2(Inset, -22f);
            art.sizeDelta = new Vector2(300f, 56f);
            area.raycastTarget = false;

            _goldIcon = NewImage("GoldIcon", art, Color.white);
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
            grt.sizeDelta = new Vector2(226f, 44f);
        }

        /// <summary>배너 탭이 놓이는 가로 줄. 탭 버튼은 서버 목록을 받은 뒤 런타임에 만든다.</summary>
        private void BuildTabRow(RectTransform panel)
        {
            _tabRow = NewChild("TabRow", panel);
            _tabRow.anchorMin = _tabRow.anchorMax = new Vector2(0.5f, 1f);
            _tabRow.pivot = new Vector2(0.5f, 1f);
            _tabRow.anchoredPosition = new Vector2(0f, -TabRowY);
            _tabRow.sizeDelta = new Vector2(PanelWidth - Inset * 2f, TabHeight);
        }

        /// <summary>
        /// 배너 화면. 배너 이미지가 창 폭 대부분을 쓰고, 그 <b>안에</b> 기간·픽업 안내(위)와
        /// 뽑기 버튼(좌측 하단)·확률 보기 버튼(우측 하단)을 얹는다. 배너 밖에는 천장 줄과 하단 줄만 둔다.
        /// 등급 확률은 팝업이라 기본 화면을 차지하지 않는다.
        /// </summary>
        private void BuildMainView(RectTransform panel)
        {
            var root = NewChild("MainRoot", panel);
            Stretch(root);
            _mainRoot = root.gameObject;

            // 배너 이미지(창 폭 대부분을 쓰는 주인공). 이름이 아트에 새겨져 있어 텍스트를 겹쳐 그리지 않고,
            // 기간·픽업 안내와 뽑기 버튼·확률 보기 버튼을 이 이미지의 자식으로 얹는다.
            _bannerImage = NewImage("BannerImage", root, new Color(0.16f, 0.18f, 0.30f, 1f));
            var brt = _bannerImage.rectTransform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 1f);
            brt.pivot = new Vector2(0.5f, 1f);
            brt.anchoredPosition = new Vector2(0f, -ContentTop);
            brt.sizeDelta = new Vector2(BannerWidth, BannerHeight);
            _bannerImage.preserveAspect = true;
            _bannerImage.raycastTarget = false; // 배너를 눌러도 아무 일이 없다(버튼만 클릭을 받는다)

            _bannerFallbackName = NewText("BannerName", brt, string.Empty, 44, TextAnchor.MiddleCenter);
            _bannerFallbackName.fontStyle = FontStyle.Bold;
            _bannerFallbackName.color = new Color(1f, 0.94f, 0.76f);
            Stretch(_bannerFallbackName.rectTransform);
            _bannerFallbackName.gameObject.SetActive(false);

            BuildBannerStrip(brt);
            BuildPullButtons(brt);      // 좌측 하단: [1연] [10연] 가로 배치 + 그 위 천장 텍스트
            BuildChanceToggle(root);    // 창 우측 상단(닫기 버튼 왼쪽)
            BuildChancePopup(root);     // 마지막 자식 = 가장 위. '확률 보기' 버튼 아래로 펼쳐진다

            _historyButton = BuildTextButton(root, "HistoryButton", "뽑기 기록", 26,
                new Vector2(0f, 1f), new Vector2(Inset, -BottomRowTop), new Vector2(220f, BottomRowHeight));

            _emptyText = NewText("EmptyText", root, "진행 중인 뽑기가 없습니다", 30, TextAnchor.MiddleCenter);
            _emptyText.color = new Color(0.82f, 0.86f, 0.96f, 0.9f);
            var ert = _emptyText.rectTransform;
            ert.anchorMin = ert.anchorMax = new Vector2(0.5f, 0.5f);
            ert.pivot = new Vector2(0.5f, 0.5f);
            ert.anchoredPosition = Vector2.zero;
            ert.sizeDelta = new Vector2(700f, 60f);
            _emptyText.gameObject.SetActive(false);
        }

        /// <summary>
        /// 배너 <b>안쪽 위</b>에 얹는 안내 띠 — 한정 배너의 남은 기간(1줄)과 픽업 설명(1줄).
        /// 아트 위에 글자를 올리므로 반투명 검정 판을 깔아 배경이 밝은 배너에서도 읽히게 한다.
        /// </summary>
        private void BuildBannerStrip(RectTransform banner)
        {
            var strip = NewImage("BannerStrip", banner, new Color(0f, 0f, 0f, 0.55f));
            var rt = strip.rectTransform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.sizeDelta = new Vector2(0f, BannerStripHeight);
            rt.anchoredPosition = Vector2.zero;
            strip.raycastTarget = false;
            _bannerStrip = strip.gameObject;

            _periodText = NewText("PeriodText", rt, string.Empty, 26, TextAnchor.MiddleLeft);
            _periodText.fontStyle = FontStyle.Bold;
            _periodText.color = new Color(1f, 0.84f, 0.58f);
            PlaceTopLeft(_periodText.rectTransform, BannerPadding, BannerWidth - BannerPadding * 2f, 34f, 10f);

            _pickupText = NewText("PickupText", rt, string.Empty, 24, TextAnchor.MiddleLeft);
            _pickupText.color = new Color(1f, 0.90f, 0.62f);
            PlaceTopLeft(_pickupText.rectTransform, BannerPadding, BannerWidth - BannerPadding * 2f, 32f, 48f);
        }

        /// <summary>
        /// 배너 <b>좌측 하단</b>에 1연·10연 뽑기 버튼을 <b>가로로 나란히</b> 두고(왼쪽 1연 · 오른쪽 10연),
        /// 10연 버튼 <b>위</b>에 천장 잔여 횟수 한 줄을 얹는다. 10연이 오른쪽인 이유는 무게가 큰 상품을
        /// 시선 흐름의 끝에 두기 위해서다.
        /// </summary>
        private void BuildPullButtons(RectTransform banner)
        {
            float multiX = BannerPadding + PullButtonWidth + PullButtonGap;
            _singleButton = BuildPullButton(banner, "SingleButton", "1연 뽑기",
                BannerPadding, out _singleCostText);
            _multiButton = BuildPullButton(banner, "MultiButton", "10연 뽑기",
                multiX, out _multiCostText);

            // 10연 보장 안내 — 버튼이 작아져 안에 넣을 자리가 없으므로 버튼 오른쪽 빈 자리에 둔다.
            // 천장 텍스트와 같이 반투명 판을 깔아 배너 아트 위에서도 읽히게 한다.
            var guaranteeBackdrop = NewImage("GuaranteeBackdrop", banner, new Color(0f, 0f, 0f, 0.5f));
            var brt = guaranteeBackdrop.rectTransform;
            brt.anchorMin = brt.anchorMax = new Vector2(0f, 0f);
            brt.pivot = new Vector2(0f, 0f);
            brt.anchoredPosition = new Vector2(multiX + PullButtonWidth + 16f,
                BannerPadding + (PullButtonHeight - PityTextHeight) * 0.5f);
            brt.sizeDelta = new Vector2(320f, PityTextHeight);
            guaranteeBackdrop.raycastTarget = false;

            _multiGuaranteeText = NewText("GuaranteeText", brt, string.Empty, 22, TextAnchor.MiddleLeft);
            _multiGuaranteeText.color = new Color(0.88f, 0.94f, 1f);
            var grt = _multiGuaranteeText.rectTransform;
            grt.anchorMin = Vector2.zero;
            grt.anchorMax = Vector2.one;
            grt.offsetMin = new Vector2(12f, 0f);
            grt.offsetMax = new Vector2(-12f, 0f);

            BuildPityText(banner, multiX);
        }

        /// <summary>
        /// 10연 버튼 위에 얹는 천장 잔여 횟수 한 줄(게이지 없음). 아트 위 글자라 반투명 판을 깔아 가독성을 확보한다.
        /// </summary>
        private void BuildPityText(RectTransform banner, float multiX)
        {
            var backdrop = NewImage("PityBackdrop", banner, new Color(0f, 0f, 0f, 0.5f));
            var rt = backdrop.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = new Vector2(multiX, BannerPadding + PullButtonHeight + PityTextGap);
            rt.sizeDelta = new Vector2(PullButtonWidth + 200f, PityTextHeight);
            backdrop.raycastTarget = false;

            _pityText = NewText("PityText", rt, string.Empty, 24, TextAnchor.MiddleLeft);
            _pityText.color = new Color(0.92f, 0.96f, 1f);
            _pityText.fontStyle = FontStyle.Bold;
            var prt = _pityText.rectTransform;
            prt.anchorMin = Vector2.zero;
            prt.anchorMax = Vector2.one;
            prt.offsetMin = new Vector2(12f, 0f);
            prt.offsetMax = new Vector2(-12f, 0f);
        }

        /// <summary>
        /// 뽑기 버튼 하나(제목 · 비용 2줄). <paramref name="left"/>은 배너 왼쪽에서 띄우는 거리다.
        /// </summary>
        private Button BuildPullButton(RectTransform banner, string name, string label, float left,
            out Text costText)
        {
            var img = NewImage(name, banner, new Color(0.24f, 0.28f, 0.44f, 0.96f));
            ApplySliced(img, _buttonSprite);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f); // 배너 좌측 하단 기준
            rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = new Vector2(left, BannerPadding);
            rt.sizeDelta = new Vector2(PullButtonWidth, PullButtonHeight);

            var t = NewText("Label", rt, label, 28, TextAnchor.MiddleCenter);
            t.fontStyle = FontStyle.Bold;
            t.color = new Color(1f, 0.96f, 0.86f);
            var lrt = t.rectTransform;
            lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 1f);
            lrt.pivot = new Vector2(0.5f, 1f);
            lrt.anchoredPosition = new Vector2(0f, -12f);
            lrt.sizeDelta = new Vector2(PullButtonWidth - 20f, 34f);

            costText = NewText("Cost", rt, string.Empty, 24, TextAnchor.MiddleCenter);
            costText.fontStyle = FontStyle.Bold;
            costText.color = new Color(1f, 0.88f, 0.46f);
            var crt = costText.rectTransform;
            crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0f);
            crt.pivot = new Vector2(0.5f, 0f);
            crt.anchoredPosition = new Vector2(0f, 12f);
            crt.sizeDelta = new Vector2(PullButtonWidth - 20f, 32f);

            return img.gameObject.AddComponent<Button>();
        }

        /// <summary>창 <b>우측 상단</b>(닫기 버튼 왼쪽)의 '확률 보기' 버튼(확률 공시 팝업 토글).</summary>
        private void BuildChanceToggle(RectTransform root)
        {
            var img = NewImage("ChanceButton", root, new Color(0.20f, 0.24f, 0.38f, 0.96f));
            ApplySliced(img, _buttonSprite);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f); // 창 우측 상단 기준
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-ChanceButtonRight, -ChanceButtonTop);
            rt.sizeDelta = new Vector2(ChanceButtonWidth, ChanceButtonHeight);

            _chanceButtonLabel = NewText("Label", rt, "확률 보기", 26, TextAnchor.MiddleCenter);
            _chanceButtonLabel.fontStyle = FontStyle.Bold;
            _chanceButtonLabel.color = new Color(1f, 0.96f, 0.86f);
            Stretch(_chanceButtonLabel.rectTransform);

            _chanceButton = img.gameObject.AddComponent<Button>();
        }

        /// <summary>
        /// 등급 확률 공시 팝업(기본 숨김 — '확률 보기'를 눌렀을 때만 보인다).
        /// 값은 <b>번들 마스터</b>에서 읽으므로 서버 조회가 없다(기획서 §2 확률 공시).
        /// 우측 상단 '확률 보기' 버튼 <b>바로 아래</b>로 펼쳐지므로 좌측 하단 뽑기 버튼을 가리지 않는다.
        /// </summary>
        private void BuildChancePopup(RectTransform root)
        {
            var popup = NewImage("ChancePopup", root, new Color(0.10f, 0.12f, 0.20f, 0.98f));
            ApplySliced(popup, _detailBackground);
            var rt = popup.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f); // 창 우측 상단 기준
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-Inset,
                -(ChanceButtonTop + ChanceButtonHeight + ChancePopupGap));
            rt.sizeDelta = new Vector2(ChancePopupWidth, ChancePopupHeight);
            _chancePopup = popup.gameObject;

            var title = NewText("Title", rt, "등급 확률", 30, TextAnchor.MiddleCenter);
            title.fontStyle = FontStyle.Bold;
            title.color = new Color(1f, 0.92f, 0.72f);
            var trt = title.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 1f);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.anchoredPosition = new Vector2(0f, -18f);
            trt.sizeDelta = new Vector2(ChancePopupWidth - 120f, 38f);

            var close = NewImage("CloseButton", rt, new Color(0.42f, 0.20f, 0.20f, 1f));
            ApplySliced(close, _buttonSprite);
            var crt = close.rectTransform;
            crt.anchorMin = crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(1f, 1f);
            crt.anchoredPosition = new Vector2(-16f, -14f);
            crt.sizeDelta = new Vector2(54f, 50f);
            var xt = NewText("X", crt, "X", 26, TextAnchor.MiddleCenter);
            xt.fontStyle = FontStyle.Bold;
            Stretch(xt.rectTransform);
            _chanceCloseButton = close.gameObject.AddComponent<Button>();

            var hint = NewText("Hint", rt, "괄호 안은 그 등급에서 나올 수 있는 아이템 종류 수", 20, TextAnchor.MiddleCenter);
            hint.color = new Color(0.78f, 0.84f, 0.96f, 0.85f);
            var hrt = hint.rectTransform;
            hrt.anchorMin = hrt.anchorMax = new Vector2(0.5f, 0f);
            hrt.pivot = new Vector2(0.5f, 0f);
            hrt.anchoredPosition = new Vector2(0f, 16f);
            hrt.sizeDelta = new Vector2(ChancePopupWidth - 40f, 26f);

            _chanceContent = NewChild("ChanceContent", rt);
            _chanceContent.anchorMin = new Vector2(0f, 0f);
            _chanceContent.anchorMax = new Vector2(1f, 1f);
            _chanceContent.offsetMin = new Vector2(28f, 52f);
            _chanceContent.offsetMax = new Vector2(-28f, -70f);
            var layout = _chanceContent.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            _chancePopup.SetActive(false);
        }

        /// <summary>뽑기 기록 화면(최신순 커서 페이징. 10연 한 묶음이 한 줄).</summary>
        private void BuildHistoryView(RectTransform panel)
        {
            var root = NewChild("HistoryRoot", panel);
            Stretch(root);
            _historyRoot = root.gameObject;

            var listBg = NewImage("HistoryBackground", root, new Color(0.10f, 0.12f, 0.20f, 0.94f));
            ApplySliced(listBg, _boxBackground);
            var lbrt = listBg.rectTransform;
            lbrt.anchorMin = new Vector2(0f, 0f);
            lbrt.anchorMax = new Vector2(1f, 1f);
            lbrt.offsetMin = new Vector2(Inset, 190f);
            lbrt.offsetMax = new Vector2(-Inset, -ContentTop);
            listBg.gameObject.AddComponent<RectMask2D>();

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(lbrt, false);
            _historyContent = (RectTransform)contentGo.transform;
            _historyContent.anchorMin = new Vector2(0f, 1f);
            _historyContent.anchorMax = new Vector2(1f, 1f);
            _historyContent.pivot = new Vector2(0.5f, 1f);
            _historyContent.offsetMin = new Vector2(14f, 0f);
            _historyContent.offsetMax = new Vector2(-14f, 0f);
            var layout = contentGo.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.padding = new RectOffset(0, 0, 12, 12);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = contentGo.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = listBg.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = lbrt;
            scroll.content = _historyContent;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            _historyEmptyText = NewText("HistoryEmpty", lbrt, "뽑기 기록이 없습니다", 28, TextAnchor.MiddleCenter);
            _historyEmptyText.color = new Color(0.82f, 0.86f, 0.96f, 0.85f);
            var hert = _historyEmptyText.rectTransform;
            hert.anchorMin = hert.anchorMax = new Vector2(0.5f, 0.5f);
            hert.sizeDelta = new Vector2(600f, 60f);
            hert.anchoredPosition = Vector2.zero;
            _historyEmptyText.gameObject.SetActive(false);

            _historyMoreButton = BuildTextButton(root, "MoreButton", "더 보기", 28,
                new Vector2(0.5f, 0f), new Vector2(0f, 110f), new Vector2(280f, 64f));
            _historyBackButton = BuildTextButton(root, "BackButton", "뒤로", 28,
                new Vector2(0f, 0f), new Vector2(Inset, 110f), new Vector2(200f, 64f));

            _historyRoot.SetActive(false);
        }

        /// <summary>하단 공용 안내/오류 메시지.</summary>
        private void BuildMessage(RectTransform panel)
        {
            // 하단 줄에서 '뽑기 기록' 버튼 오른쪽 공간을 쓴다(버튼과 겹치지 않게 왼쪽 여백을 더 준다).
            _messageText = NewText("Message", panel, string.Empty, 24, TextAnchor.MiddleLeft);
            _messageText.color = new Color(1f, 0.72f, 0.42f);
            _messageText.horizontalOverflow = HorizontalWrapMode.Wrap;
            var mrt = _messageText.rectTransform;
            mrt.anchorMin = mrt.anchorMax = new Vector2(0f, 1f);
            mrt.pivot = new Vector2(0f, 1f);
            mrt.anchoredPosition = new Vector2(Inset + 240f, -BottomRowTop);
            mrt.sizeDelta = new Vector2(PanelWidth - Inset * 2f - 250f, BottomRowHeight);
        }

        /// <summary>결과 연출 오버레이 자식을 확보한다(패널 캔버스 안에서 정렬만 위로 덮어쓴다).</summary>
        private void EnsureResultOverlay()
        {
            if (_resultOverlay != null)
            {
                return;
            }
            var go = new GameObject("ResultOverlay", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            _resultOverlay = go.AddComponent<GachaResultOverlay>();
        }

        /// <summary>구워진 고정 버튼의 리스너를 실행 시점에 다시 연결한다(비영구 리스너는 프리팹에 저장되지 않는다).</summary>
        private void WireRuntime()
        {
            Rewire(_closeButton, Close);
            Rewire(_dimButton, Close);
            Rewire(_singleButton, OnSinglePull);
            Rewire(_multiButton, OnMultiPull);
            Rewire(_historyButton, ShowHistoryView);
            Rewire(_historyBackButton, ShowMainView);
            Rewire(_historyMoreButton, RequestHistoryMore);
            Rewire(_chanceButton, ToggleChancePopup);
            Rewire(_chanceCloseButton, HideChancePopup);

            // 자체 사운드를 재생하는 버튼은 전역 클릭음에서 제외한다(사운드 정의서 §8 매핑을 그대로 유지).
            UiClickSound.Suppress(_chanceButton);        // 열림/닫힘음
            UiClickSound.Suppress(_historyButton);       // 기록 화면 표시음
            UiClickSound.Suppress(_historyMoreButton);   // 더 보기 클릭음
        }

        private static void Rewire(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null)
            {
                return;
            }
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
        }

        // ── 배너 목록 ──

        /// <summary>
        /// 지금 돌릴 수 있는 배너와 천장 진행도를 조회한다(<c>POST /api/game/gacha/banners</c>).
        /// 번들 마스터에 없는 <c>gachaCode</c>는 <b>조용히 건너뛰고</b>(서버 마스터가 먼저 갱신된 경우),
        /// 하나도 그리지 못했으면 클라이언트 갱신 안내를 띄운다(기획서 §5.1).
        /// </summary>
        private void RequestBanners()
        {
            if (NetworkManager.Instance == null || !Session.IsLoggedIn)
            {
                SetMessage("로그인이 필요합니다.");
                return;
            }
            var req = new AuthRequest { userId = Session.UserId, token = Session.Token };
            NetworkManager.Instance.PostToGame<GachaBannerListResponse>("/api/game/gacha/banners", req, resp =>
            {
                var data = resp != null ? resp.data : null;
                _serverTime = data != null ? data.serverTime : 0;
                _serverTimeStamp = Time.unscaledTime;
                ApplyBanners(data != null ? data.banners : null);
            }, error =>
            {
                Debug.LogWarning($"[Gacha] 배너 조회 실패: {error}");
                SetMessage(ErrorMessages.ToKorean(error));
            });
        }

        /// <summary>조회한 배너 목록을 반영한다(번들에 있는 배너만, sortOrder 순서 그대로).</summary>
        private void ApplyBanners(List<GachaBannerDto> banners)
        {
            MasterDataManager.EnsureLoaded();
            var db = MasterDataManager.Db;

            _banners.Clear();
            int unknown = 0;
            if (banners != null)
            {
                foreach (var banner in banners)
                {
                    if (banner == null)
                    {
                        continue;
                    }
                    if (db == null || db.GachaOf(banner.gachaCode) == null)
                    {
                        unknown++; // 번들에 없는 코드는 그리지 않는다(오류로 처리하지 않는다)
                        continue;
                    }
                    _banners.Add(banner);
                }
            }

            if (_banners.Count == 0)
            {
                _selectedCode = 0;
                RebuildTabs();
                SetMainVisible(false);
                if (_emptyText != null)
                {
                    _emptyText.text = unknown > 0
                        ? "클라이언트를 갱신해야 표시할 수 있는 뽑기입니다"
                        : "진행 중인 뽑기가 없습니다";
                    _emptyText.gameObject.SetActive(true);
                }
                return;
            }

            if (_emptyText != null) _emptyText.gameObject.SetActive(false);
            SetMainVisible(true);

            if (FindBanner(_selectedCode) == null)
            {
                _selectedCode = _banners[0].gachaCode; // 선택했던 배너가 닫혔으면 첫 배너로
            }
            RebuildTabs();
            RefreshSelectedBanner();
        }

        /// <summary>배너 탭 버튼을 다시 만든다(배너 수에 맞춰 가운데 정렬).</summary>
        private void RebuildTabs()
        {
            foreach (var tab in _tabs)
            {
                if (tab != null) Destroy(tab);
            }
            _tabs.Clear();
            if (_tabRow == null)
            {
                return;
            }

            int count = _banners.Count;
            float step = TabWidth + TabGap;
            float originX = -(count - 1) * step * 0.5f;
            for (int i = 0; i < count; i++)
            {
                var banner = _banners[i];
                _tabs.Add(BuildTab(banner.gachaCode, new Vector2(originX + i * step, 0f)));
            }
        }

        /// <summary>배너 탭 버튼 하나(아트에 배너 이름이 새겨져 있어 선택/비선택 스프라이트만 교체한다).</summary>
        private GameObject BuildTab(int gachaCode, Vector2 pos)
        {
            var art = ArtFor(gachaCode);
            bool selected = gachaCode == _selectedCode;

            var img = NewImage($"Tab_{gachaCode}", _tabRow,
                selected ? new Color(0.30f, 0.36f, 0.56f, 1f) : new Color(0.16f, 0.18f, 0.28f, 1f));
            var sprite = selected
                ? (art != null ? art.tabSelected : null)
                : (art != null ? art.tabNormal : null);
            if (sprite == null && art != null)
            {
                sprite = art.tabNormal != null ? art.tabNormal : art.tabSelected;
            }
            ApplySimple(img, sprite);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(TabWidth, TabHeight);

            if (sprite == null)
            {
                // 아트가 없는 배너는 이름 텍스트로 대체한다(빈 버튼이 되지 않도록).
                var t = NewText("Label", rt, BannerName(gachaCode), 30, TextAnchor.MiddleCenter);
                t.fontStyle = FontStyle.Bold;
                Stretch(t.rectTransform);
            }
            else if (!selected)
            {
                img.color = new Color(0.72f, 0.72f, 0.78f, 1f); // 비선택 탭은 살짝 눌러 둔다(선택 대비)
            }

            int code = gachaCode;
            var button = img.gameObject.AddComponent<Button>();
            UiClickSound.Suppress(button); // 탭 전환음(sfx_ui_tab)을 직접 재생하므로 전역 클릭음 제외
            button.onClick.AddListener(() => SelectBanner(code));
            return img.gameObject;
        }

        /// <summary>탭을 눌러 배너를 바꾼다(같은 배너면 아무것도 하지 않는다).</summary>
        private void SelectBanner(int gachaCode)
        {
            if (_selectedCode == gachaCode)
            {
                return;
            }
            _selectedCode = gachaCode;
            SetMessage(string.Empty);
            SoundManager.Sfx(SoundId.UiTab); // 배너 전환(사운드 정의서 §8 공용음 매핑)
            RebuildTabs();
            RefreshSelectedBanner();
        }

        /// <summary>선택된 배너의 이미지·기간·픽업 안내·확률 공시·천장·비용을 모두 다시 그린다.</summary>
        private void RefreshSelectedBanner()
        {
            var gacha = GachaMasterOf(_selectedCode);
            var art = ArtFor(_selectedCode);

            if (_bannerImage != null)
            {
                var sprite = art != null ? art.banner : null;
                if (sprite != null)
                {
                    ApplySimple(_bannerImage, sprite);
                }
                else
                {
                    _bannerImage.sprite = null;
                    _bannerImage.color = new Color(0.16f, 0.18f, 0.30f, 1f);
                }
                if (_bannerFallbackName != null)
                {
                    _bannerFallbackName.text = BannerName(_selectedCode);
                    _bannerFallbackName.gameObject.SetActive(sprite == null);
                }
            }

            if (_bannerStrip != null)
            {
                _bannerStrip.SetActive(gacha != null); // 배너를 못 그리는 상태에서 빈 띠만 남지 않게
            }

            if (_pickupText != null)
            {
                // 픽업 배너는 최고 등급 슬롯 후보가 그 아이템 하나뿐이라 전설이 나오면 항상 픽업이다(기획서 4.1).
                bool pickup = gacha != null && gacha.pickupItemCode != 0;
                _pickupText.text = pickup
                    ? $"픽업: {ItemName(gacha.pickupItemCode)} — 전설이 나오면 이 아이템으로 확정"
                    : "상시 배너 — 전설은 전설 장비 중 무작위";
                _pickupText.color = pickup ? new Color(1f, 0.90f, 0.62f) : new Color(0.88f, 0.92f, 1f);
            }

            RefreshCostUi(gacha);
            RebuildChanceRows(gacha);
            UpdatePeriodUi();
            UpdatePityUi();
        }

        /// <summary>1연·10연 비용과 10연 보장 안내를 번들 마스터 값으로 표시한다.</summary>
        private void RefreshCostUi(TaskbarHero.Common.MasterData.GachaMaster gacha)
        {
            if (_singleCostText != null)
            {
                _singleCostText.text = gacha != null ? $"{gacha.costSingle:N0} G" : "-";
            }
            if (_multiCostText != null)
            {
                _multiCostText.text = gacha != null ? $"{gacha.costMulti:N0} G" : "-";
            }
            if (_multiGuaranteeText != null)
            {
                // 10연 비용은 1연 × 횟수가 아니라 독립 값(묶음 할인)이라 그대로 표시하고 보장 등급만 덧붙인다.
                _multiGuaranteeText.text = gacha == null
                    ? string.Empty
                    : gacha.multiGuaranteedGrade > 0
                        ? $"{gacha.multiCount}회 · {GradeName(gacha.multiGuaranteedGrade)} 이상 1개 보장"
                        : $"{gacha.multiCount}회 연속 뽑기";
            }
        }

        /// <summary>등급 확률 공시 줄(높은 등급부터)을 다시 만든다. 값은 번들 마스터이므로 서버 조회가 없다.</summary>
        private void RebuildChanceRows(TaskbarHero.Common.MasterData.GachaMaster gacha)
        {
            if (_chanceContent == null)
            {
                return;
            }
            for (int i = _chanceContent.childCount - 1; i >= 0; i--)
            {
                Destroy(_chanceContent.GetChild(i).gameObject);
            }
            if (gacha == null || gacha.gradeWeights == null)
            {
                return;
            }

            MasterDataManager.EnsureLoaded();
            var db = MasterDataManager.Db;

            // 등급 내림차순(전설 → 노말)으로 보여 준다. 마스터 행 순서에 의존하지 않도록 등급으로 훑는다.
            for (int grade = 5; grade >= 1; grade--)
            {
                if (!HasGrade(gacha, grade))
                {
                    continue;
                }
                float chance = db != null ? db.GachaGradeChance(gacha.gachaCode, grade) : 0f;
                int poolCount = db != null ? db.GachaPoolCount(gacha.gachaCode, grade) : 0;

                var row = NewChild($"Chance_{grade}", _chanceContent);
                var le = row.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = 34f;
                le.flexibleHeight = 0f;

                var nameText = NewText("Grade", row, GradeName(grade), 24, TextAnchor.MiddleLeft);
                nameText.color = GradeColors.Name(grade);
                nameText.fontStyle = FontStyle.Bold;
                var nrt = nameText.rectTransform;
                nrt.anchorMin = new Vector2(0f, 0f);
                nrt.anchorMax = new Vector2(0.45f, 1f);
                nrt.offsetMin = Vector2.zero;
                nrt.offsetMax = Vector2.zero;

                var valueText = NewText("Chance", row, $"{chance * 100f:0.##}%  ({poolCount}종)", 24,
                    TextAnchor.MiddleRight);
                valueText.color = new Color(0.90f, 0.94f, 1f);
                var vrt = valueText.rectTransform;
                vrt.anchorMin = new Vector2(0.45f, 0f);
                vrt.anchorMax = new Vector2(1f, 1f);
                vrt.offsetMin = Vector2.zero;
                vrt.offsetMax = Vector2.zero;
            }
        }

        /// <summary>선택 배너의 노출 기간(남은 시간)을 표시한다. 상시 배너(<c>closeAt = 0</c>)는 카운트다운이 없다.</summary>
        private void UpdatePeriodUi()
        {
            if (_periodText == null)
            {
                return;
            }
            var banner = FindBanner(_selectedCode);
            if (banner == null)
            {
                _periodText.text = string.Empty;
                return;
            }
            if (banner.closeAt <= 0)
            {
                _periodText.text = "상시 진행 (종료 예정 없음)";
                _periodText.color = new Color(0.86f, 0.92f, 1f);
                return;
            }

            long now = _serverTime + (long)(Time.unscaledTime - _serverTimeStamp);
            long remain = banner.closeAt - now;
            if (remain <= 0)
            {
                _periodText.text = "기간이 종료되었습니다";
                _periodText.color = new Color(1f, 0.62f, 0.52f);
                // 목록이 낡았으므로 한 번만 다시 받는다(닫힌 배너로 뽑으면 GachaNotAvailable이다).
                if (!_reopenRequested)
                {
                    _reopenRequested = true;
                    RequestBanners();
                }
                return;
            }
            _periodText.text = $"한정 배너 · 남은 기간 {FormatRemain(remain)}";
            _periodText.color = new Color(1f, 0.84f, 0.58f);
        }

        /// <summary>
        /// 천장 진행도를 표시한다("37 / 90 · 천장까지 53회"). 응답 <c>counters</c>가 정본이며,
        /// 소프트 천장 발동 구간(번들 마스터의 소프트 <c>threshold</c> 이상)에서는 '확률 상승 중'을 덧붙인다.
        /// </summary>
        private void UpdatePityUi()
        {
            var banner = FindBanner(_selectedCode);
            var counter = FindPityCounter(banner);
            if (_pityText == null)
            {
                return;
            }
            if (counter == null || counter.pityThreshold <= 0)
            {
                // 천장 규칙이 없는 배너(또는 하드 천장이 없는 등급)는 누적 횟수만 알린다.
                _pityText.text = counter != null
                    ? $"누적 {counter.pityCount}회 (천장 없음)"
                    : "천장 없음";
                _pityText.color = new Color(0.92f, 0.96f, 1f);
                return;
            }

            // 게이지 없이 텍스트 한 줄로 알린다 — 남은 횟수를 앞세우고 진행도를 괄호로 덧붙인다.
            string text = $"천장까지 {counter.remainingToPity}회" +
                          $" ({counter.pityCount} / {counter.pityThreshold})";
            MasterDataManager.EnsureLoaded();
            var db = MasterDataManager.Db;
            int soft = db != null ? db.GachaPityThreshold(_selectedCode, counter.grade, SoftPityType) : 0;
            if (soft > 0 && counter.pityCount + 1 >= soft)
            {
                text += "  ·  확률 상승 중";
                _pityText.color = new Color(1f, 0.86f, 0.46f);
            }
            else
            {
                _pityText.color = new Color(0.92f, 0.96f, 1f);
            }
            _pityText.text = text;
        }

        // ── 뽑기 ──

        /// <summary>1연 뽑기.</summary>
        private void OnSinglePull() => Pull(GachaPullType.Single);

        /// <summary>10연 뽑기.</summary>
        private void OnMultiPull() => Pull(GachaPullType.Multi);

        /// <summary>
        /// 뽑기를 요청한다(<c>POST /api/game/gacha/pull</c>). 요청에는 <c>gachaCode</c>와 <c>pullType</c>만 실린다 —
        /// <b>뽑는 횟수·확률·결과는 서버 소유</b>다(기획서 §3·§5.2). 골드 부족·빈 칸 부족은 불필요한 요청을 줄이려
        /// 먼저 안내하되 최종 판정은 서버가 한다.
        /// </summary>
        private void Pull(GachaPullType pullType)
        {
            if (_busy || _selectedCode == 0)
            {
                return;
            }
            if (NetworkManager.Instance == null || !Session.IsLoggedIn)
            {
                SetMessage("로그인이 필요합니다.");
                return;
            }

            var gacha = GachaMasterOf(_selectedCode);
            long cost = gacha == null ? 0L : pullType == GachaPullType.Single ? gacha.costSingle : gacha.costMulti;
            if (cost > 0 && CurrentGold() < cost)
            {
                SoundManager.Sfx(SoundId.UiError); // 골드 부족(§8)
                SetMessage($"골드가 부족합니다. (필요 {cost:N0} G)");
                return;
            }

            // 가방 빈 칸이 모자라면 미리 확인을 받는다(10연은 InventoryFull이 날 확률이 높다 — 기획서 §5.2).
            int free = FreeSlots();
            int need = pullType == GachaPullType.Single ? 1 : MultiFreeSlotWarnThreshold;
            if (free >= 0 && free < need)
            {
                string warn = free == 0
                    ? "가방에 빈 칸이 없습니다. 뽑기에 실패할 수 있습니다."
                    : $"가방 빈 칸이 {free}칸뿐입니다. 뽑기에 실패할 수 있습니다.";
                if (ModalManager.Instance != null)
                {
                    ModalManager.Instance.ShowConfirmCancel("빈 칸 부족", warn + "\n그래도 진행할까요?",
                        () => SendPull(pullType, cost));
                    return;
                }
                SetMessage(warn);
            }

            SendPull(pullType, cost);
        }

        /// <summary>뽑기 요청을 실제로 보낸다(사전 안내를 거친 뒤 호출된다).</summary>
        private void SendPull(GachaPullType pullType, long cost)
        {
            if (_busy)
            {
                return;
            }
            _busy = true;
            SetButtonsInteractable(false);
            SetMessage(string.Empty);

            var req = new GachaPullRequest
            {
                userId = Session.UserId,
                token = Session.Token,
                data = new GachaPullData { gachaCode = _selectedCode, pullType = (int)pullType },
            };
            NetworkManager.Instance.PostToGame<GachaPullResponse>("/api/game/gacha/pull", req, resp =>
            {
                _busy = false;
                SetButtonsInteractable(true);
                var data = resp != null ? resp.data : null;
                if (data == null)
                {
                    SetMessage("뽑기 결과를 받지 못했습니다.");
                    return;
                }
                Debug.Log($"[Gacha] 뽑기 완료 gacha={data.gachaCode} type={data.pullType} " +
                          $"pullId={data.pullId} results={data.results?.Count ?? 0}");

                // 응답만으로 재화·가방·천장 표시를 갱신한다(재조회하지 않는다 — 기획서 §5.2).
                Session.ApplyBalance(data.balance);
                Session.ApplyInventoryDelta(data.inventoryDelta);
                Session.RaiseInventoryChanged();
                ApplyCounters(data.gachaCode, data.counters);
                RefreshGold();
                UpdatePityUi();

                if (_resultOverlay != null)
                {
                    _resultOverlay.Show(data);
                }
            }, OnPullError);
        }

        /// <summary>뽑기 실패 처리. 닫힌 배너(<c>GachaNotAvailable</c>)는 목록을 다시 받고,
        /// 번들과 서버 마스터가 어긋난 경우(<c>GachaNotFound</c>)는 갱신 안내를 띄운다(기획서 §6.7).</summary>
        private void OnPullError(NetworkError error)
        {
            _busy = false;
            SetButtonsInteractable(true);
            Debug.LogWarning($"[Gacha] 뽑기 실패: {error}");

            // 골드 부족·가방 부족·닫힌 배너는 사용자 상황이므로 오류음으로 알린다(사운드 정의서 §8).
            SoundManager.Sfx(SoundId.UiError);

            switch (error.ErrorCode)
            {
                case ErrorCode.GachaNotAvailable:
                    SetMessage("이 배너는 지금 뽑을 수 없습니다. 목록을 갱신합니다.");
                    RequestBanners();
                    return;
                case ErrorCode.GachaNotFound:
                    SetMessage("서버에 없는 뽑기입니다. 클라이언트를 갱신해 주세요.");
                    return;
                default:
                    SetMessage(ErrorMessages.ToKorean(error));
                    return;
            }
        }

        /// <summary>응답의 천장 진행도를 그 배너의 목록 항목에 반영한다(다음 조회 없이 표시를 최신화).</summary>
        private void ApplyCounters(int gachaCode, List<GachaPityCounterDto> counters)
        {
            if (counters == null)
            {
                return;
            }
            var banner = FindBanner(gachaCode);
            if (banner == null)
            {
                return;
            }
            banner.counters = counters;
        }

        // ── 기록 ──

        /// <summary>배너 화면으로 돌아간다(확률 팝업은 닫은 상태로 시작한다).</summary>
        private void ShowMainView()
        {
            if (_mainRoot != null) _mainRoot.SetActive(true);
            if (_historyRoot != null) _historyRoot.SetActive(false);
            if (_tabRow != null) _tabRow.gameObject.SetActive(true);
            HideChancePopup();
        }

        // ── 확률 공시 팝업 ──

        /// <summary>
        /// 등급 확률 공시를 열고 닫는다. 확률·후보 수는 <b>번들 마스터</b>에 있는 정적 값이라
        /// 열 때 서버를 조회하지 않는다(기획서 §2·§5 서두).
        /// </summary>
        private void ToggleChancePopup()
        {
            if (_chancePopup == null)
            {
                return;
            }
            bool show = !_chancePopup.activeSelf;
            _chancePopup.SetActive(show);
            SoundManager.Sfx(show ? SoundId.UiPanelOpen : SoundId.UiClick);
            if (_chanceButtonLabel != null)
            {
                _chanceButtonLabel.text = show ? "확률 닫기" : "확률 보기";
            }
        }

        /// <summary>확률 공시 팝업을 닫는다(화면 전환·배너 없음 상태에서 남지 않도록).</summary>
        private void HideChancePopup()
        {
            if (_chancePopup != null && _chancePopup.activeSelf)
            {
                _chancePopup.SetActive(false);
            }
            if (_chanceButtonLabel != null)
            {
                _chanceButtonLabel.text = "확률 보기";
            }
        }

        /// <summary>뽑기 기록 화면으로 전환하고 최신 페이지부터 조회한다(전체 가챠 대상).</summary>
        private void ShowHistoryView()
        {
            if (_mainRoot != null) _mainRoot.SetActive(false);
            if (_historyRoot != null) _historyRoot.SetActive(true);
            if (_tabRow != null) _tabRow.gameObject.SetActive(false);
            HideChancePopup(); // 확률 팝업이 열려 있었으면 닫고 버튼 라벨도 되돌린다
            SetMessage(string.Empty);
            SoundManager.Sfx(SoundId.UiPanelOpen); // 기록 화면 표시(§8)

            ClearHistoryRows();
            _historyCursor = 0;
            _historyHasMore = false;
            RequestHistory();
        }

        /// <summary>다음 페이지를 이어 받는다(커서 페이징이므로 이전 페이지 결과는 그대로 남긴다).</summary>
        private void RequestHistoryMore()
        {
            if (_historyHasMore)
            {
                SoundManager.Sfx(SoundId.UiClick); // 페이지 더 보기(§8)
                RequestHistory();
            }
        }

        /// <summary>뽑기 기록을 최신순으로 조회한다(<c>POST /api/game/gacha/history</c>, 커서 페이징).</summary>
        private void RequestHistory()
        {
            if (NetworkManager.Instance == null || !Session.IsLoggedIn)
            {
                SetMessage("로그인이 필요합니다.");
                return;
            }
            var req = new GachaHistoryRequest
            {
                userId = Session.UserId,
                token = Session.Token,
                // gachaCode 0 = 전체 가챠. cursor 0이면 최신부터.
                data = new GachaHistoryData { gachaCode = 0, cursor = _historyCursor, limit = HistoryPageSize },
            };
            NetworkManager.Instance.PostToGame<GachaHistoryResponse>("/api/game/gacha/history", req, resp =>
            {
                var data = resp != null ? resp.data : null;
                _historyHasMore = data != null && data.hasMore;
                _historyCursor = data != null ? data.nextCursor : 0;
                AppendHistoryRows(data != null ? data.pulls : null);
            }, error =>
            {
                Debug.LogWarning($"[Gacha] 기록 조회 실패: {error}");
                SetMessage(ErrorMessages.ToKorean(error));
            });
        }

        /// <summary>조회한 기록을 목록 끝에 덧붙인다(10연 한 묶음이 한 줄).</summary>
        private void AppendHistoryRows(List<GachaHistoryEntryDto> pulls)
        {
            if (pulls != null)
            {
                foreach (var pull in pulls)
                {
                    if (pull != null)
                    {
                        _historyRows.Add(BuildHistoryRow(pull));
                    }
                }
            }
            if (_historyEmptyText != null)
            {
                _historyEmptyText.gameObject.SetActive(_historyRows.Count == 0);
            }
            if (_historyMoreButton != null)
            {
                _historyMoreButton.gameObject.SetActive(_historyHasMore);
            }
        }

        /// <summary>기록 한 줄(배너 이름 · 1연/10연 · 비용 · 결과 요약 · 최고 등급).</summary>
        private GameObject BuildHistoryRow(GachaHistoryEntryDto pull)
        {
            var rowImg = NewImage("HistoryRow", _historyContent, new Color(0.16f, 0.19f, 0.30f, 0.92f));
            var le = rowImg.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 96f;
            le.flexibleHeight = 0f;
            var rt = rowImg.rectTransform;
            rowImg.raycastTarget = false;

            string kind = pull.pullType == (int)GachaPullType.Multi ? "10연" : "1연";
            var headText = NewText("Head", rt, $"{BannerName(pull.gachaCode)} · {kind}", 26, TextAnchor.MiddleLeft);
            headText.fontStyle = FontStyle.Bold;
            headText.color = new Color(1f, 0.94f, 0.80f);
            PlaceTopLeft(headText.rectTransform, 18f, 600f, 36f, 12f);

            long amount = pull.cost != null ? pull.cost.amount : 0L;
            var costText = NewText("Cost", rt, $"-{amount:N0} G", 24, TextAnchor.MiddleRight);
            costText.color = new Color(1f, 0.84f, 0.48f);
            var crt = costText.rectTransform;
            crt.anchorMin = crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(1f, 1f);
            crt.anchoredPosition = new Vector2(-18f, -12f);
            crt.sizeDelta = new Vector2(260f, 34f);

            var itemsText = NewText("Items", rt, HistoryItemsSummary(pull.items), 22, TextAnchor.MiddleLeft);
            itemsText.color = new Color(0.86f, 0.90f, 1f, 0.95f);
            PlaceTopLeft(itemsText.rectTransform, 18f, PanelWidth - Inset * 2f - 60f, 34f, 52f);

            return rowImg.gameObject;
        }

        /// <summary>기록 한 건의 결과를 한 줄 요약으로 만든다(최고 등급 항목을 앞세우고 나머지는 개수로).</summary>
        private static string HistoryItemsSummary(List<GachaHistoryItemDto> items)
        {
            if (items == null || items.Count == 0)
            {
                return "결과 없음";
            }

            GachaHistoryItemDto best = null;
            foreach (var item in items)
            {
                if (item != null && (best == null || item.grade > best.grade))
                {
                    best = item;
                }
            }
            string head = best != null
                ? $"최고 {GradeName(best.grade)} · {ItemName(best.itemCode)}" +
                  (best.quantity > 1 ? $" x{best.quantity}" : string.Empty)
                : "결과 없음";
            return items.Count > 1 ? $"{head}  (총 {items.Count}회)" : head;
        }

        /// <summary>기록 행을 모두 지운다(화면 진입 시 첫 페이지부터 다시 받기 위해).</summary>
        private void ClearHistoryRows()
        {
            foreach (var row in _historyRows)
            {
                if (row != null) Destroy(row);
            }
            _historyRows.Clear();
            if (_historyEmptyText != null) _historyEmptyText.gameObject.SetActive(false);
            if (_historyMoreButton != null) _historyMoreButton.gameObject.SetActive(false);
        }

        // ── 표시 갱신 헬퍼 ──

        /// <summary>배너가 하나도 없을 때 배너 화면 위젯을 감춘다(안내 문구만 남긴다).</summary>
        private void SetMainVisible(bool visible)
        {
            // 배너 이미지를 끄면 그 자식(안내 띠·뽑기 버튼·천장 텍스트)도 함께 사라진다.
            if (_bannerImage != null) _bannerImage.gameObject.SetActive(visible);
            if (_historyButton != null) _historyButton.gameObject.SetActive(visible);
            // '확률 보기'는 창 우측 상단(배너 밖)이라 따로 감춘다 — 배너가 없으면 보여 줄 확률도 없다.
            if (_chanceButton != null) _chanceButton.gameObject.SetActive(visible);
            if (!visible)
            {
                HideChancePopup();
            }
        }

        /// <summary>뽑기 요청 중에는 버튼을 잠근다(이중 요청 방지).</summary>
        private void SetButtonsInteractable(bool interactable)
        {
            if (_singleButton != null) _singleButton.interactable = interactable;
            if (_multiButton != null) _multiButton.interactable = interactable;
        }

        private void SetMessage(string message)
        {
            if (_messageText != null)
            {
                _messageText.text = message ?? string.Empty;
            }
        }

        /// <summary>보유 골드량과 골드 아이콘(item_1)을 세션 재화에서 갱신한다.</summary>
        private void RefreshGold()
        {
            if (_goldText != null)
            {
                _goldText.text = CurrentGold().ToString("N0");
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

        /// <summary>세션에 캐싱된 보유 골드.</summary>
        private static long CurrentGold()
        {
            var currencies = Session.GameData != null ? Session.GameData.currencies : null;
            if (currencies == null)
            {
                return 0L;
            }
            foreach (var c in currencies)
            {
                if (c != null && c.currencyType == GoldCurrencyType)
                {
                    return c.amount;
                }
            }
            return 0L;
        }

        /// <summary>가방 빈 칸 수. 가방을 아직 받지 않았으면 -1(사전 경고를 생략한다).</summary>
        private static int FreeSlots()
        {
            if (!Session.BagLoaded || Session.GameData == null || Session.GameData.player == null)
            {
                return -1;
            }
            int capacity = Session.GameData.player.inventoryCapacity;
            if (capacity <= 0)
            {
                return -1;
            }
            int used = Session.Bag != null ? Session.Bag.Count : 0;
            return Mathf.Max(0, capacity - used);
        }

        /// <summary>패널을 닫는다(UIManager 우선).</summary>
        public void Close()
        {
            if (_resultOverlay != null)
            {
                _resultOverlay.Hide(); // 결과 연출이 떠 있으면 함께 정리
            }
            if (UIManager.Instance != null)
            {
                UIManager.Instance.Hide(UIManager.PanelType.Gacha);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }

        // ── 조회 헬퍼 ──

        /// <summary>서버가 내려준 배너 항목(없으면 null).</summary>
        private GachaBannerDto FindBanner(int gachaCode)
        {
            foreach (var banner in _banners)
            {
                if (banner != null && banner.gachaCode == gachaCode)
                {
                    return banner;
                }
            }
            return null;
        }

        /// <summary>표시할 천장 카운터. 최고 등급(전설)을 우선하고, 없으면 하드 천장이 있는 첫 항목을 쓴다.</summary>
        private static GachaPityCounterDto FindPityCounter(GachaBannerDto banner)
        {
            if (banner == null || banner.counters == null || banner.counters.Count == 0)
            {
                return null;
            }
            GachaPityCounterDto fallback = null;
            foreach (var counter in banner.counters)
            {
                if (counter == null)
                {
                    continue;
                }
                if (counter.grade == PityGrade)
                {
                    return counter;
                }
                if (fallback == null || counter.pityThreshold > 0)
                {
                    fallback = counter;
                }
            }
            return fallback;
        }

        /// <summary>배너 코드의 아트 묶음(배선되지 않았으면 null → 폴백 표시).</summary>
        private GachaBannerArt ArtFor(int gachaCode)
        {
            if (_bannerArt == null)
            {
                return null;
            }
            foreach (var art in _bannerArt)
            {
                if (art != null && art.gachaCode == gachaCode)
                {
                    return art;
                }
            }
            return null;
        }

        /// <summary>번들 마스터의 배너 정의(없으면 null).</summary>
        private static TaskbarHero.Common.MasterData.GachaMaster GachaMasterOf(int gachaCode)
        {
            MasterDataManager.EnsureLoaded();
            var db = MasterDataManager.Db;
            return db != null ? db.GachaOf(gachaCode) : null;
        }

        /// <summary>배너 표시 이름(번들에 없으면 코드로 대체).</summary>
        private static string BannerName(int gachaCode)
        {
            var gacha = GachaMasterOf(gachaCode);
            return gacha != null && !string.IsNullOrEmpty(gacha.name) ? gacha.name : $"뽑기 {gachaCode}";
        }

        /// <summary>그 배너에 해당 등급 슬롯(가중치 행)이 있는지.</summary>
        private static bool HasGrade(TaskbarHero.Common.MasterData.GachaMaster gacha, int grade)
        {
            if (gacha == null || gacha.gradeWeights == null)
            {
                return false;
            }
            foreach (var w in gacha.gradeWeights)
            {
                if (w.grade == grade)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>등급 이름(노말·고급·희귀·영웅·전설). 마스터에 없으면 "N등급".</summary>
        private static string GradeName(int grade)
        {
            MasterDataManager.EnsureLoaded();
            var db = MasterDataManager.Db;
            if (db != null && db.Grades.TryGetValue(grade, out var row) && row != null &&
                !string.IsNullOrEmpty(row.name))
            {
                return row.name;
            }
            return $"{grade}등급";
        }

        /// <summary>아이템 표시 이름(번들에 없으면 코드로 대체).</summary>
        private static string ItemName(int itemCode)
        {
            MasterDataManager.EnsureLoaded();
            var db = MasterDataManager.Db;
            if (db != null && db.Items.TryGetValue(itemCode, out var def) && def != null)
            {
                return def.name;
            }
            return $"아이템 {itemCode}";
        }

        /// <summary>남은 기간을 사람이 읽는 형식으로("13일 4시간" · "4시간 12분" · "12분 30초").</summary>
        private static string FormatRemain(long seconds)
        {
            if (seconds <= 0)
            {
                return "0초";
            }
            long days = seconds / 86400;
            long hours = seconds % 86400 / 3600;
            long minutes = seconds % 3600 / 60;
            long secs = seconds % 60;
            if (days > 0)
            {
                return $"{days}일 {hours}시간";
            }
            if (hours > 0)
            {
                return $"{hours}시간 {minutes}분";
            }
            return $"{minutes}분 {secs}초";
        }

        // ── UI 헬퍼 ──

        /// <summary>라벨만 있는 작은 버튼(기록·뒤로·더 보기).</summary>
        private Button BuildTextButton(RectTransform parent, string name, string label, int fontSize,
            Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var img = NewImage(name, parent, new Color(0.24f, 0.28f, 0.44f, 1f));
            ApplySliced(img, _buttonSprite);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var t = NewText("Label", rt, label, fontSize, TextAnchor.MiddleCenter);
            t.fontStyle = FontStyle.Bold;
            t.color = new Color(1f, 0.96f, 0.86f);
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

        /// <summary>부모 왼쪽 위 기준으로 (x, y) 위치·크기를 지정한다(기록 행 안의 텍스트 배치용).</summary>
        private static void PlaceTopLeft(RectTransform rt, float x, float width, float height, float y)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(width, height);
        }

        private static void ApplySliced(Image img, Sprite sprite)
        {
            if (img == null || sprite == null) return;
            img.sprite = sprite;
            img.type = Image.Type.Sliced;
            img.color = Color.white;
        }

        private static void ApplySimple(Image img, Sprite sprite)
        {
            if (img == null || sprite == null) return;
            img.sprite = sprite;
            img.type = Image.Type.Simple;
            img.color = Color.white;
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
