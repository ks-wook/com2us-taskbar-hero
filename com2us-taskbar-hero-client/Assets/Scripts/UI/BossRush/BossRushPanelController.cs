using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TaskbarHero.Client.Battle;
using TaskbarHero.Client.Managers;
using TaskbarHero.Common.Dto;
using TaskbarHero.Common.MasterData;

namespace TaskbarHero.Client.UI.BossRush
{
    /// <summary>
    /// 보스 러시 패널(<c>docs/ui/보스러시-ui-기획서.md</c>). GameScene 하단 HUD의 [보스러시] 아이콘으로 열리며,
    /// <b>[보스 러시](도전 화면)</b>과 <b>[랭킹](시즌 순위)</b> 두 탭을 한 창에서 교체해 보여 준다.
    /// <para>창 규격은 거래소와 같은 1120 × 1310이고, 내용물은 공용 프레임(<c>ui_bg_2</c>) 안쪽
    /// <see cref="PanelFrame"/> 콘텐츠 영역(802.9 × 956.9)에만 놓는다. 전투를 가리지 않도록 전투 화면
    /// <b>왼쪽</b>에 도킹한다(<see cref="SidePanel.Side.Left"/>).</para>
    /// <para><b>닫기(X) 버튼은 두지 않는다</b> — 다른 패널과 같이 창 밖(딤) 클릭으로 닫는다.</para>
    /// <para><b>화면 값은 서버(<c>/api/game/boss-rush/*</c>)와 마스터 번들에서 온다.</b> 시즌·내 기록·해금 여부는
    /// <c>info</c>, 랭킹 목록은 <c>rank</c>, 내 순위는 <c>my-rank</c>가 채운다. 라운드 보스 5칸과 시즌
    /// 순위 보상처럼 <b>정적인 값은 서버를 부르지 않고 번들</b>(<c>boss_rush_round</c>·
    /// <c>boss_rush_rank_reward</c>)로 그린다(보스러시 UI 기획서 2.1).</para>
    /// <para><b>[도전 시작]은 <c>enter</c>로 런을 열고 전투를 <see cref="BossRushBattleFlow"/>에 넘긴다</b> —
    /// 응답의 5라운드 구성으로 전투가 시작되면 패널은 자동으로 닫히고, 완주 시 <c>clear</c> 보고는
    /// 그 흐름이 맡는다(보스러시 UI 기획서 4·5장). 도전 중에는 버튼이 <c>도전 진행 중</c>으로 잠긴다.</para>
    /// 정적 계층(캔버스·창·탭·보스 5칸·랭킹 10행·버튼)은 에디터 빌더(<c>BossRushUiBuilder</c>)가 프리팹에 굽고,
    /// 버튼 핸들러는 실행마다 <see cref="WireRuntime"/>이 다시 연결한다(<c>onClick</c>은 직렬화되지 않는다).
    /// </summary>
    public class BossRushPanelController : MonoBehaviour
    {
        private const float CanvasRefWidth = 1080f;
        private const float CanvasRefHeight = 1920f;

        // 창·콘텐츠 규격 — 거래소와 같은 값이라 표(랭킹) 기하를 그대로 쓸 수 있다.
        private const float PanelWidth = 1120f;
        private const float PanelHeight = 1310f;
        private const float ContentWidth = PanelWidth * (1f - PanelFrame.InsetLeft - PanelFrame.InsetRight)
            - PanelFrame.Pad * 2f;   // 802.9
        private const float ContentHeight = PanelHeight * (1f - PanelFrame.InsetTop - PanelFrame.InsetBottom)
            - PanelFrame.Pad * 2f;   // 956.9

        // 공통 헤더·탭
        // 창 상단 중앙 엠블럼(boss_rush.png) — 콘텐츠 영역이 아니라 <b>창 프레임의 장식 띠</b> 위에 얹는다
        // (프레임 상단 인셋 = PanelHeight × PanelFrame.InsetTop ≒ 209라 190 크기가 그 안에 들어간다).
        private const float TitleEmblemSize = 300f;
        private const float TitleEmblemY = 100f;    // 창 위쪽 끝에서 엠블럼 중심까지
        // 시즌 요약은 <b>도전 화면(보스 러시 탭) 안쪽 아래</b>, 도전 바 바로 위 가운데에 둔다.
        // 콘텐츠 공용이 아니라 탭 소속이다 — 랭킹 탭에서는 이 자리에 목록이 놓인다.
        private const float SeasonTextY = 727f;
        private const float SeasonTextWidth = 440f;
        private const float SeasonTextHeight = 48f;
        private const float TabRowY = 84f;
        private const float TabHeight = 66f;
        private const float TabGap = 20f;
        private const float TabWidth = (ContentWidth - TabGap) * 0.5f;   // 391.5

        // 행 폭·열 좌표(거래소 목록과 같은 계산) — 콘텐츠 폭에서 좌우 여백 16씩을 뺀 값.
        private const float RowWidth = ContentWidth - 32f;               // 770.9
        private const float ColRankX = 16f;
        private const float ColRankWidth = 96f;
        private const float ColRecordWidth = 180f;
        private const float ColRecordX = RowWidth - 16f - ColRecordWidth; // 574.9
        private const float ColNameX = 124f;
        private const float ColNameWidth = ColRecordX - 12f - ColNameX;   // 438.9

        // [보스 러시] 탭
        private const float RecordCardY = 158f;
        private const float RecordCardHeight = 120f;
        // 라운드별 등장 보스 줄 — 5명을 가로로 세우고 그 아래 이름·레벨을 쓴다(왼쪽이 라운드 1).
        private const float BossCardY = 285f;
        private const float BossCardHeight = 274f;
        private const float BossCellStep = RowWidth / BossCellCount;      // 154.2
        // 초상 칸은 칸 폭(154.2)에 갇히므로 <b>세로로 길게 잡지 않는다</b> — 보스 스프라이트는 폭이 높이보다
        // 넓어서(실측 0.88 × 0.81 월드) 세로로 긴 칸에 담으면 폭을 맞추려 카메라가 멀어져 캐릭터가 작아진다.
        private const float BossPortraitWidth = 148f;
        private const float BossPortraitHeight = 176f;                    // 초상 RT(300×356) 비율 유지
        private const float BossPortraitY = 12f;                          // 카드 안쪽 기준
        private const float BossNameY = 196f;
        private const float BossLevelY = 226f;
        private const int BossCellCount = 5;
        // 보스 프리팹을 UI에 보여 주는 초상 렌더러 설정(오프라인 보상 패널과 같은 규격 — 격리 레이어 28).
        private const int PortraitLayer = 28;
        private const int PortraitRtWidth = 300;
        private const int PortraitRtHeight = 356;
        // 보스 스프라이트 실측(0.88 × 0.81 월드, 중심 y +0.35)에서 역산한 값 — 화면 폭에 여유 0.11씩 남기고
        // 세로를 약 60% 채운다. 더 줄이면 무기·날개가 좌우로 잘린다.
        private const float PortraitOrtho = 0.66f;
        private static readonly Vector2 PortraitAim = new Vector2(0f, 0.35f);
        private static readonly Color PortraitBg = new Color(0.08f, 0.09f, 0.13f, 1f);
        // 초상 스테이지(화면 밖 격리 위치) — 파티(700대)·오프라인 보상(1500대)과 겹치지 않는 자리.
        private static readonly Vector3 PortraitStageOrigin = new Vector3(2200f, 1500f, 0f);
        private const float PortraitStageSpacing = 40f;
        // 보상 상자는 <b>라벨과 [자세히 보기]만</b> 담는다(등수별 금액은 팝업이 보여 준다) — 그래서 폭을 행의 절반으로
        // 줄이고 높이도 버튼이 들어갈 만큼만 남겼다(플레이 모드 확정값).
        private const float RewardBoxY = 593f;
        private const float RewardBoxWidth = RowWidth * 0.5f;   // 385.43
        private const float RewardBoxHeight = 55.3f;
        // 보상 요약 오른쪽 끝의 [자세히 보기] 버튼과, 그 버튼이 여는 순위 보상 상세 팝업.
        private const float RewardDetailButtonWidth = 168f;
        private const float RewardDetailButtonHeight = 48f;
        private const float RewardDetailButtonRight = 16f;   // 상자 오른쪽 끝에서 띄우는 거리
        private const float RewardPopupWidth = 560f;
        private const float RewardPopupHeight = 520f;
        private const float RewardPopupPadX = 28f;
        private const float RewardPopupRowsTop = 84f;        // 팝업 위쪽 끝에서 첫 행까지
        private const float RewardPopupRowHeight = 96f;
        private const float RewardPopupRowGap = 12f;
        private const float RewardSlotSize = 72f;            // 골드 아이콘 칸(공용 ItemSlot)
        private const float RewardRankLabelWidth = 96f;
        /// <summary>골드의 아이콘 코드(가방·큐브·우편함과 같은 값 — 공용 슬롯이 이 코드로 골드 아이콘을 찾는다).</summary>
        private const int GoldItemCode = 1;
        private const float ChallengeBarY = 800f;
        private const float ChallengeBarHeight = ContentHeight - ChallengeBarY; // 156.9
        private const float ChallengeButtonWidth = 360f;
        private const float ChallengeButtonHeight = 96f;
        private const float ChallengeHintY = 16.25f;   // 안내 문구를 바 위쪽으로 올린 높이(플레이 모드 확정값)

        // [랭킹] 탭
        private const float TableHeaderY = 158f;
        private const float TableHeaderHeight = 48f;
        private const float RankListY = 210f;
        private const float RankRowHeight = 58f;
        private const int RankRowsPerView = 10;                                  // 한 번에 보이는 행 수
        private const float RankListHeight = RankRowsPerView * RankRowHeight;    // 580
        // 한 번에 받아 두는 페이지 크기(서버 상한 rank_page_limit = 100 이내). 보이는 행보다 넉넉히 받아
        // <b>그 안에서는 서버를 부르지 않고</b> 움직인다 — 스크롤 한 칸마다 조회하면 그때마다 화면이 비어 깜빡인다.
        private const int RankPageSize = 50;
        // 실제로 만들어 두는 행 수 = 보이는 10행 + 위아래로 걸친 행 2개. 페이지 전체(50행)를 만들지 않고
        // 이 행들을 <b>재활용</b>해 다시 채운다(스크롤 위치에 따라 어느 순위를 그릴지만 바뀐다).
        private const int RankRowViewCount = RankRowsPerView + 2;
        private const float RankWheelStep = RankRowHeight * 3f;                  // 휠 한 칸 = 3행
        // 휠로 잡힌 목표까지 따라가는 속도(1/초, 지수 감쇠). 클수록 빨리 붙는다.
        private const float RankScrollEase = 16f;
        // 내 순위 고정 영역 — 스크롤 위치와 무관하게 항상 보인다.
        private const float MyRankAreaY = 802f;
        private const float MyRankAreaHeight = 68f;
        private const float MyRankLabelWidth = 90f;
        private const float MyRankRankX = 112f;
        private const float MyRankRankWidth = 90f;
        private const float MyRankNameX = 210f;
        private const float MyRankNameWidth = 250f;
        private const float RankFooterY = 882f;
        private const float RankFooterHeight = ContentHeight - RankFooterY;      // 74.9
        private const float JumpButtonWidth = 240f;
        private const float JumpButtonHeight = 56f;
        // 페이지 조회를 묶는 간격(초, unscaled) — 빠르게 굴려 페이지 경계를 연달아 넘어도 요청이 몰리지 않게.
        private const float RankRequestInterval = 0.1f;

        // 색 — 거래소 패널과 같은 계열(프레임 안쪽 남색 면 위 밝은 글자).
        private static readonly Color SurfaceCard = new Color(0f, 0f, 0f, 0.32f);
        private static readonly Color SurfaceRow = new Color(0.13f, 0.16f, 0.23f, 0.96f);
        private static readonly Color SurfaceRowAlt = new Color(0.17f, 0.20f, 0.28f, 0.96f);
        private static readonly Color SurfaceRowMine = new Color(0.16f, 0.28f, 0.46f, 0.98f);
        private static readonly Color TextPrimary = new Color(0.91f, 0.93f, 0.97f);
        private static readonly Color TextDim = new Color(0.70f, 0.74f, 0.82f);
        private static readonly Color TextTitle = new Color(1f, 0.92f, 0.72f);
        private static readonly Color TextGold = new Color(1f, 0.85f, 0.35f);
        private static readonly Color ArtTintPanel = new Color(0.50f, 0.58f, 0.80f, 1f);
        // 탭은 선택/비선택 아트가 따로 없으므로(둘 다 pixel_rpg_input_field) 틴트로 구분한다.
        private static readonly Color TabTintSelected = Color.white;
        // 시즌 종료 카운트다운 강조색.
        private static readonly Color TextSeason = new Color(1f, 0f, 0.1097f);
        private static readonly Color FallbackTab = new Color(0.18f, 0.12f, 0.11f, 1f);
        private static readonly Color FallbackHeader = new Color(0.16f, 0.12f, 0.11f, 1f);
        private static readonly Color FallbackButtonGold = new Color(0.39f, 0.30f, 0.10f, 1f);
        private static readonly Color FallbackButtonWood = new Color(0.16f, 0.10f, 0.07f, 1f);
        // 1~3위 강조색(메달 아이콘 아트가 없어 글자색으로만 구분한다).
        private static readonly Color[] MedalColors =
        {
            new Color(1f, 0.82f, 0.29f), new Color(0.85f, 0.89f, 0.93f), new Color(0.82f, 0.55f, 0.29f),
        };

        [Header("UI 리소스 (에디터 빌더가 배선)")]
        [Tooltip("창 배경 프레임(Assets/Art/UI/ui_bg_2.png) — 가방·거래소와 같은 공용 프레임.")]
        [SerializeField] private Sprite _windowFrame;
        [Tooltip("창 상단 중앙 엠블럼(Assets/Art/UI/BossRush/boss_rush.png).")]
        [SerializeField] private Sprite _titleEmblem;
        [Tooltip("표 머리 띠(Assets/Art/UI/Trade/01_Frames_Panels/bar_table_header.png).")]
        [SerializeField] private Sprite _tableHeader;
        [Tooltip("탭 버튼(Assets/Art/UI/pixel_rpg_input_field.png). 선택 상태는 스프라이트가 아니라 틴트로 구분한다.")]
        [SerializeField] private Sprite _btnCategory;
        [Tooltip("선택된 탭 버튼. 현재는 비선택과 같은 아트를 배선한다(구분은 틴트).")]
        [SerializeField] private Sprite _btnCategorySel;
        [Tooltip("도전 시작 버튼(Assets/Art/UI/pixel_rpg_button.png).")]
        [SerializeField] private Sprite _btnGold;
        [Tooltip("내 순위로 이동 버튼(Assets/Art/UI/Trade/02_Buttons/btn_wood_normal.png).")]
        [SerializeField] private Sprite _btnWood;
        [Tooltip("순위 보상 상세 팝업 배경(Assets/Art/UI/item_detail_bg.png) — 공용 상세 팝업과 같은 아트.")]
        [SerializeField] private Sprite _popupBackground;
        [Tooltip("공용 아이템 슬롯 프리팹(Assets/Prefabs/UI/ItemSlot.prefab). 순위 보상 상세의 골드 칸이 쓴다 — " +
                 "아이템이 놓이는 칸은 화면마다 새로 그리지 않고 이 슬롯 하나만 쓴다(ItemSlotBuilder가 배선).")]
        [SerializeField] private GameObject _itemSlotPrefab;

        [Header("구성 참조 (에디터 빌더가 배선)")]
        [SerializeField] private Button _dimButton;
        [SerializeField] private Text _seasonText;
        [SerializeField] private Image _infoTabImage;
        [SerializeField] private Button _infoTabButton;
        [SerializeField] private Image _rankTabImage;
        [SerializeField] private Button _rankTabButton;
        [SerializeField] private GameObject _infoTabRoot;
        [SerializeField] private GameObject _rankTabRoot;

        [Header("[보스 러시] 탭")]
        [SerializeField] private Text _bestRecordText;
        [SerializeField] private Text _myRankText;
        [SerializeField] private Text _rewardLabel;
        [SerializeField] private Button _rewardDetailButton;
        [SerializeField] private GameObject _rewardPopupRoot;      // 팝업 전체(딤 + 창). 기본 숨김
        [SerializeField] private RectTransform _rewardPopupBody;   // 행이 놓이는 창 본체
        [SerializeField] private Button _rewardPopupDimButton;
        [SerializeField] private Button _challengeButton;
        [SerializeField] private Image _challengeButtonImage;
        [SerializeField] private Text _challengeButtonLabel;
        [SerializeField] private BossCellView[] _bossCells = new BossCellView[0];
        [Tooltip("라운드 보스 프리팹 표(monster_9099~9499). 에디터 빌더가 Assets/Prefabs/Character/Monster에서 배선한다.")]
        [SerializeField] private BossPrefabEntry[] _bossPrefabs = new BossPrefabEntry[0];

        [Header("[랭킹] 탭")]
        [SerializeField] private Text _fallbackNoticeText;
        [SerializeField] private RankRowView[] _rankRows = new RankRowView[0];
        [SerializeField] private RankScrollArea _rankScrollArea;
        [Tooltip("행들을 담아 세로로 움직이는 판. 위로 걸친 행만큼 올려 마스크가 잘라 내게 한다.")]
        [SerializeField] private RectTransform _rankRowContainer;
        [SerializeField] private Text _scrollPositionText;
        [SerializeField] private Text _rankEmptyText;
        [SerializeField] private Button _jumpToMyRankButton;
        // 내 순위 고정 영역(스크롤과 무관하게 항상 보인다).
        [SerializeField] private GameObject _myRankArea;
        [SerializeField] private Text _myRankAreaRank;
        [SerializeField] private Text _myRankAreaName;
        [SerializeField] private Text _myRankAreaRecord;
        [SerializeField] private Text _myRankAreaEmpty;
        [SerializeField] private Text _entryTotalText;

        /// <summary>라운드 보스 칸 1개(정적으로 5개를 굽고 값만 채운다). 초상은 프리팹을 실시간 렌더한
        /// <see cref="CharacterPortrait"/>의 RenderTexture를 받는다.</summary>
        [System.Serializable]
        public class BossCellView
        {
            public GameObject root;
            public RawImage portrait;
            public Text nameText;
            public Text levelText;
        }

        /// <summary>몬스터 코드 → 보스 프리팹 한 쌍(초상에 세울 프리팹을 찾는 표).</summary>
        [System.Serializable]
        public class BossPrefabEntry
        {
            public int monsterCode;
            public GameObject prefab;
        }

        /// <summary>랭킹 행 1개(정적으로 10개를 굽고, 조회 결과가 없는 행은 숨긴다).</summary>
        [System.Serializable]
        public class RankRowView
        {
            public GameObject root;
            public Image background;
            public Text rankText;
            public Text nicknameText;
            public Text recordText;
        }

        private Font _font;
        private bool _rankTabOpen;
        // 스크롤 위치는 <b>절대 순위 인덱스(0-based)를 소수까지</b> 가진다 — 0.5면 첫 행이 절반 잘린 상태다.
        // _rankScrollTarget이 입력이 잡은 목표고 _rankScroll이 화면에 그려지는 현재 위치다(휠은 목표만 옮기고
        // Update가 부드럽게 따라간다, 드래그는 둘을 같이 옮긴다).
        private float _rankScroll;
        private float _rankScrollTarget;
        private BossRushInfoResultData _info;      // 마지막 info 응답(미조회·실패면 null)
        // 시즌 남은 시간은 <b>로컬 시계로 계산하지 않는다</b> — 응답의 serverTime을 받은 시점을 기준점으로 잡고
        // 경과를 더해 표시한다(보스러시 UI 기획서 3장).
        private float _seasonRemainingAtFetch;     // info를 받은 시점의 시즌 잔여(초)
        private float _seasonFetchRealtime;        // 그 시점의 Time.unscaledTime
        private bool _seasonExpiredHandled;        // 카운트다운이 0에 닿아 info를 한 번 다시 받았는지
        private bool _infoRequestInFlight;
        private bool _enterRequestInFlight;   // [도전 시작] 중복 클릭 방지(런이 두 개 열리지 않게)
        private CharacterPortrait[] _portraits;   // 칸마다 하나(런타임 생성)
        private GameObject[] _portraitStages;
        private int[] _portraitMonsterCode;       // 칸에 이미 세워 둔 프리팹의 코드(중복 재생성 방지)
        private int _rankTotalEntries;            // 매 응답 값으로 갱신(끝 판정을 캐시하지 않는다)
        private int _rankSource;                  // 1=랭킹 캐시 2=MySQL 폴백. 바뀌면 처음부터 다시 본다
        // 지금 들고 있는 페이지 하나(이어붙이지 않는다 — 새 페이지가 오면 통째로 교체한다).
        private readonly List<BossRushRankEntryDto> _rankPage = new List<BossRushRankEntryDto>();
        private int _rankPageOffset = -1;         // 그 페이지 첫 행의 절대 오프셋(-1 = 들고 있는 페이지 없음)
        private int _rankPendingOffset = -1;      // 지금 받고 있는 페이지의 오프셋(-1 = 조회 중 아님)
        private int _rankFailedOffset = -1;       // 조회가 실패한 페이지(스크롤이 다른 데로 가기 전엔 다시 안 부른다)
        private bool _rankPageRequestPending;     // 조회가 묶여 대기 중
        private bool _rankAnyRowVisible;          // 화면에 그려진 행이 하나라도 있는지(안내 문구 판정용)
        private int _rankBoundFirst = int.MinValue; // 행에 채워 둔 첫 순위 인덱스(같으면 다시 채우지 않는다)
        private bool _rankRebindNeeded = true;    // 페이지가 바뀌어 행 내용을 다시 채워야 하는지
        private float _lastRankRequestTime;
        private int _myRank;                      // 내 순위(0 = 기록 없음) — [내 순위로 이동]과 고정 영역이 쓴다
        private int _rankRequestSeq;              // 늦게 도착한 옛 구간 응답을 버리기 위한 요청 순번
        private bool _rankLoading;                // 조회 대기 중(이전 구간을 화면에 남기지 않는다)
        private string _rankMessage;              // 목록 자리에 띄울 안내(조회 실패 등)
        private int _myRankSeasonId;              // my-rank가 답한 시즌(rank 응답과 다르면 한 번 다시 받는다)
        private bool _myRankSeasonRetried;

        private bool AlreadyBuilt => _infoTabRoot != null && _rankTabRoot != null;

        private void Awake()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (!AlreadyBuilt)
            {
                Construct(); // 폴백(프리팹 미배선 시 런타임 구성)
            }
            WireRuntime();
        }

        /// <summary>패널이 표시될 때마다 진입 화면부터 다시 그린다(<c>info</c>·<c>my-rank</c>를 다시 조회한다).</summary>
        private void OnEnable()
        {
            if (Application.isPlaying && AlreadyBuilt)
            {
                if (_rewardPopupRoot != null)
                {
                    _rewardPopupRoot.SetActive(false); // 지난번 열어 둔 보상 상세를 들고 다시 열지 않는다
                }
                RefreshInfo();
                SelectTab(false);
            }
        }

        /// <summary>패널이 닫히면 초상 렌더 스테이지를 끈다 — 보이지 않는 창을 위해 카메라 5대가 계속 돌지 않게 한다
        /// (스테이지·프리팹은 남겨 두고 다음 표시에서 그대로 다시 쓴다).</summary>
        private void OnDisable()
        {
            if (_portraitStages == null)
            {
                return;
            }
            foreach (var stage in _portraitStages)
            {
                if (stage != null) stage.SetActive(false);
            }
        }

        /// <summary>시즌 남은 시간 표시를 갱신하고, 스크롤로 밀린 랭킹 조회를
        /// <see cref="RankRequestInterval"/> 간격으로 묶어 던진다.
        /// <para>카운트다운이 0에 닿으면 시즌이 넘어간 것이므로 <b>패널을 연 동안 한 번만</b> <c>info</c>를 다시
        /// 받는다(시계 오차로 무한 재조회하지 않게 — 보스러시 UI 기획서 3장).</para></summary>
        private void Update()
        {
            if (_rankTabOpen)
            {
                UpdateRankScroll();
            }
            if (_info == null)
            {
                return;
            }
            ApplySeasonText();

            if (!_seasonExpiredHandled && SeasonRemainingSeconds <= 0f)
            {
                _seasonExpiredHandled = true;
                ApplyChallengeButton();   // 정산 중이면 도전할 수 없다
                RequestInfo();
            }
        }

        /// <summary>
        /// 응답에 <b>쓸 수 있는 시즌 정보</b>가 있는지. <c>seasonId</c>까지 보는 이유는
        /// <b>Unity 직렬화가 중첩 클래스의 null을 표현하지 못해</b>(`JsonUtility`) 서버가 내려준
        /// <c>"season": null</c>이 값이 0으로 채워진 객체가 될 수 있기 때문이다 — null 검사만으로는
        /// `시즌 0`이 화면에 뜬다.
        /// </summary>
        private bool HasSeason => _info != null && _info.season != null && _info.season.seasonId > 0;

        /// <summary>지금 시점의 시즌 잔여(초). 서버 <c>serverTime</c> 기준점에 로컬 경과를 더해 계산한다
        /// (기기 시계를 직접 읽지 않는다). 시즌 정보가 없으면 0 = 정산 중으로 본다.</summary>
        private float SeasonRemainingSeconds =>
            !HasSeason
                ? 0f
                : Mathf.Max(0f, _seasonRemainingAtFetch - (Time.unscaledTime - _seasonFetchRealtime));

        /// <summary>에디터 빌드 전용: 전체 정적 계층을 생성해 프리팹에 굽는다.</summary>
        public void EditorConstruct() => Construct();

        // ── 정적 계층 구성 ──

        /// <summary>캔버스·딤·창 프레임·헤더·탭·두 탭 화면을 생성한다.</summary>
        private void Construct()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildCanvas();
            BuildDim();
            var panel = BuildPanel();
            var content = PanelFrame.CreateContentArea(panel);
            BuildTitleEmblem(panel);
            BuildTabs(content);
            BuildInfoTab(content);
            BuildRankTab(content);
            BuildRewardPopup(panel);   // 창 전체를 덮는 팝업이라 콘텐츠가 아니라 창 루트에 마지막으로 붙인다
        }

        /// <summary>패널 전용 오버레이 캔버스(다른 패널과 동일 규격, sortingOrder 100).</summary>
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

        /// <summary>패널 밖 클릭 시 닫히는 투명 차단막(닫기 버튼 대신).</summary>
        private void BuildDim()
        {
            var img = NewImage("Dim", (RectTransform)transform, new Color(0f, 0f, 0f, 0f));
            Stretch(img.rectTransform);
            _dimButton = img.gameObject.AddComponent<Button>();
            _dimButton.transition = Selectable.Transition.None;
        }

        /// <summary>창 본체(공용 프레임 9-slice)를 만들고 전투 화면 왼쪽에 도킹한다.</summary>
        private RectTransform BuildPanel()
        {
            var img = NewImage("PanelRoot", (RectTransform)transform, new Color(0.12f, 0.10f, 0.08f, 0.98f));
            ApplySliced(img, _windowFrame, Color.white); // 프레임은 원색 그대로(틴트를 걸면 안쪽이 어두워진다)
            var rt = img.rectTransform;
            rt.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            SidePanel.Attach(rt, SidePanel.Side.Left);
            return rt;
        }

        /// <summary>창 <b>상단 중앙</b>에 보스 러시 엠블럼(boss_rush.png)을 얹는다. 콘텐츠 영역이 아니라
        /// 창 프레임의 상단 장식 띠 위에 두므로 아래 내용 배치를 밀지 않는다. 엠블럼에 콘텐츠 이름이
        /// 새겨져 있어 <b>별도 제목 리본을 두지 않는다</b>(탭에도 같은 이름이 있다).</summary>
        private void BuildTitleEmblem(RectTransform panel)
        {
            if (_titleEmblem == null)
            {
                return; // 아트 미배선 시에는 아무것도 얹지 않는다(프레임 장식만 남는다)
            }
            var emblem = NewImage("TitleEmblem", panel, Color.white);
            emblem.sprite = _titleEmblem;
            emblem.preserveAspect = true;
            emblem.raycastTarget = false; // 창 끌어 옮기기(배경 드래그)를 막지 않는다
            var rt = emblem.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, -TitleEmblemY);
            rt.sizeDelta = new Vector2(TitleEmblemSize, TitleEmblemSize);
        }

        /// <summary>탭 2개: [시즌 정보] [랭킹].</summary>
        private void BuildTabs(RectTransform content)
        {
            _infoTabImage = BuildTab(content, "InfoTab", "시즌 정보", 0f);
            _infoTabButton = _infoTabImage.gameObject.AddComponent<Button>();
            _rankTabImage = BuildTab(content, "RankTab", "랭킹", TabWidth + TabGap);
            _rankTabButton = _rankTabImage.gameObject.AddComponent<Button>();
        }

        /// <summary>탭 버튼 하나(콘텐츠 좌측 기준 x 오프셋).</summary>
        private Image BuildTab(RectTransform content, string name, string label, float offsetX)
        {
            var img = NewImage(name, content, FallbackTab);
            ApplySliced(img, _btnCategory, ArtTintPanel);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(offsetX, -TabRowY);
            rt.sizeDelta = new Vector2(TabWidth, TabHeight);
            var t = NewText("Label", rt, label, 30, TextAnchor.MiddleCenter);
            t.fontStyle = FontStyle.Bold;
            Stretch(t.rectTransform);
            return img;
        }

        // ── [보스 러시] 탭 ──

        /// <summary>도전 화면: 내 기록 카드 · 라운드 보스 5명 줄 · 완주 보상 요약 · 하단 도전 바.</summary>
        private void BuildInfoTab(RectTransform content)
        {
            var root = NewChild("InfoTabRoot", content);
            Stretch(root);
            _infoTabRoot = root.gameObject;

            BuildRecordCard(root);
            BuildBossRow(root);
            BuildRewardBox(root);
            BuildSeasonText(root);
            BuildChallengeBar(root);
        }

        /// <summary>내 기록 카드: 이번 시즌 최고 기록(좌) + 등재 인원 중 순위(우).</summary>
        private void BuildRecordCard(RectTransform root)
        {
            var card = NewImage("RecordCard", root, SurfaceCard);
            var crt = card.rectTransform;
            PlaceTopCenter(crt, RecordCardY, RowWidth, RecordCardHeight);

            var label = NewText("Label", crt, "이번 시즌 최고 기록", 24, TextAnchor.UpperLeft);
            label.color = TextDim;
            PlaceInside(label.rectTransform, 20f, -14f, 360f, 30f);

            _bestRecordText = NewText("BestRecord", crt, "-", 46, TextAnchor.UpperLeft);
            _bestRecordText.fontStyle = FontStyle.Bold;
            _bestRecordText.color = TextGold;
            PlaceInside(_bestRecordText.rectTransform, 20f, -48f, 420f, 56f);

            _myRankText = NewText("MyRank", crt, string.Empty, 30, TextAnchor.MiddleRight);
            _myRankText.color = TextPrimary;
            var mrt = _myRankText.rectTransform;
            mrt.anchorMin = mrt.anchorMax = new Vector2(1f, 0.5f);
            mrt.pivot = new Vector2(1f, 0.5f);
            mrt.anchoredPosition = new Vector2(-20f, 0f);
            mrt.sizeDelta = new Vector2(340f, 44f);
        }

        /// <summary>라운드 보스 5명을 가로로 세운다(왼쪽이 라운드 1). 각 칸은 <b>보스 프리팹을 실시간 렌더한
        /// 초상</b> + 이름 + 레벨이며, 프리팹·레벨은 런타임에 채운다.</summary>
        private void BuildBossRow(RectTransform root)
        {
            var card = NewImage("BossCard", root, SurfaceCard);
            var crt = card.rectTransform;
            PlaceTopCenter(crt, BossCardY, RowWidth, BossCardHeight);

            _bossCells = new BossCellView[BossCellCount];
            for (int i = 0; i < BossCellCount; i++)
            {
                var cell = NewChild($"BossCell{i + 1}", crt);
                cell.anchorMin = cell.anchorMax = new Vector2(0f, 1f);
                cell.pivot = new Vector2(0f, 1f);
                cell.anchoredPosition = new Vector2(i * BossCellStep, 0f);
                cell.sizeDelta = new Vector2(BossCellStep, BossCardHeight);

                // 초상(프리팹 렌더 결과를 받는 RawImage). 텍스처는 런타임에 CharacterPortrait가 배정한다.
                var portraitGo = new GameObject("Portrait", typeof(RectTransform), typeof(RawImage));
                portraitGo.transform.SetParent(cell, false);
                var portrait = portraitGo.GetComponent<RawImage>();
                portrait.color = Color.white;
                portrait.raycastTarget = false;
                var prt = portrait.rectTransform;
                prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 1f);
                prt.pivot = new Vector2(0.5f, 1f);
                prt.anchoredPosition = new Vector2(0f, -BossPortraitY);
                prt.sizeDelta = new Vector2(BossPortraitWidth, BossPortraitHeight);

                var nameText = NewText("Name", cell, string.Empty, 22, TextAnchor.MiddleCenter);
                nameText.color = TextPrimary;
                PlaceTopCenter(nameText.rectTransform, BossNameY, BossCellStep - 6f, 26f);

                var levelText = NewText("Level", cell, string.Empty, 28, TextAnchor.MiddleCenter);
                levelText.fontStyle = FontStyle.Bold;
                levelText.color = TextGold;
                PlaceTopCenter(levelText.rectTransform, BossLevelY, BossCellStep - 6f, 34f);

                _bossCells[i] = new BossCellView
                {
                    root = cell.gameObject,
                    portrait = portrait,
                    nameText = nameText,
                    levelText = levelText,
                };
            }
        }

        /// <summary>
        /// 시즌 순위 보상 <b>입구</b>. <b>보스러시는 완주·라운드 보상이 없고 보상은 시즌 순위 보상(골드)뿐</b>이므로
        /// (보스러시 기획서 2장) 이 자리에는 제목과 [자세히 보기]만 두고, <b>등수별 금액은 팝업</b>이 보여 준다.
        /// <para>한 줄 요약(<c>1위 30,000,000 …</c>)을 함께 적지 않는다 — 같은 값을 두 곳에 쓰면 좁은 폭에 눌려
        /// 읽기 어렵고, 팝업이 등수·아이콘·금액을 훨씬 또렷하게 보여 준다(플레이 모드 확정).</para>
        /// </summary>
        private void BuildRewardBox(RectTransform root)
        {
            var box = NewImage("RewardBox", root, SurfaceCard);
            var brt = box.rectTransform;
            PlaceTopCenter(brt, RewardBoxY, RewardBoxWidth, RewardBoxHeight);

            _rewardLabel = NewText("Label", brt, "시즌 순위 보상", 24, TextAnchor.UpperLeft);
            _rewardLabel.color = TextDim;
            PlaceInside(_rewardLabel.rectTransform, 20f, -12f, 300f, 28f);

            // [자세히 보기] — 등수별 보상을 아이콘과 함께 세로로 보여 주는 팝업을 연다.
            var detail = NewImage("RewardDetailButton", brt, FallbackButtonWood);
            ApplySliced(detail, _btnWood, Color.white);
            var drt = detail.rectTransform;
            drt.anchorMin = drt.anchorMax = new Vector2(1f, 0.5f);
            drt.pivot = new Vector2(1f, 0.5f);
            drt.anchoredPosition = new Vector2(-RewardDetailButtonRight, 0f);
            drt.sizeDelta = new Vector2(RewardDetailButtonWidth, RewardDetailButtonHeight);
            var dlabel = NewText("Label", drt, "자세히 보기", 24, TextAnchor.MiddleCenter);
            dlabel.color = TextPrimary;
            Stretch(dlabel.rectTransform);
            _rewardDetailButton = detail.gameObject.AddComponent<Button>();
            detail.gameObject.AddComponent<ButtonPunchScale>();
        }

        /// <summary>
        /// 시즌 순위 보상 상세 팝업(기본 숨김 — [자세히 보기]를 눌렀을 때만 보인다).
        /// <para>등수(1~3위)를 <b>세로로 나열</b>하고 각 줄에 <b>공용 아이템 슬롯</b>(골드 아이콘)과 금액을 함께 둔다.
        /// 값은 번들 <c>boss_rush_rank_reward</c>에서 읽으므로 서버 조회가 없다.</para>
        /// <para>창 전체를 덮는 딤을 깔아 <b>팝업 밖을 누르면 닫히게</b> 한다 — 패널 자체를 닫는 바깥 딤보다
        /// 위에 있어야 팝업이 열린 동안 패널이 먼저 닫히지 않는다(그래서 콘텐츠가 아니라 창 루트에 붙인다).
        /// <b>닫기(X) 버튼은 두지 않는다</b> — 다른 창과 같이 바깥 클릭으로만 닫는다.</para>
        /// </summary>
        private void BuildRewardPopup(RectTransform panel)
        {
            var root = NewChild("RewardPopup", panel);
            Stretch(root);
            _rewardPopupRoot = root.gameObject;

            var dim = NewImage("PopupDim", root, new Color(0f, 0f, 0f, 0.55f));
            Stretch(dim.rectTransform);
            _rewardPopupDimButton = dim.gameObject.AddComponent<Button>();
            _rewardPopupDimButton.transition = Selectable.Transition.None;

            var body = NewImage("PopupBody", root, new Color(0.10f, 0.12f, 0.20f, 0.98f));
            ApplySliced(body, _popupBackground, Color.white);
            var brt = body.rectTransform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f);
            brt.pivot = new Vector2(0.5f, 0.5f);
            brt.anchoredPosition = Vector2.zero;
            brt.sizeDelta = new Vector2(RewardPopupWidth, RewardPopupHeight);
            _rewardPopupBody = brt;

            var title = NewText("Title", brt, "시즌 순위 보상", 30, TextAnchor.MiddleCenter);
            title.color = TextTitle;
            title.fontStyle = FontStyle.Bold;
            PlaceTopCenter(title.rectTransform, 20f, RewardPopupWidth - 140f, 40f);

            // 닫기(X) 버튼은 두지 않는다 — 팝업 밖(창 안쪽)을 눌러 닫는다(패널·스테이지·인벤토리와 같은 규칙).
            var hint = NewText("Hint", brt, "보상은 시즌 종료후 메일로 발송됩니다.", 22, TextAnchor.MiddleCenter);
            hint.color = TextDim;
            var hrt = hint.rectTransform;
            hrt.anchorMin = hrt.anchorMax = new Vector2(0.5f, 0f);
            hrt.pivot = new Vector2(0.5f, 0f);
            hrt.anchoredPosition = new Vector2(0f, 18f);
            hrt.sizeDelta = new Vector2(RewardPopupWidth - 40f, 30f);

            root.gameObject.SetActive(false);
        }

        /// <summary>시즌 요약(번호·남은 시간) — 도전 바 바로 위 가운데. 남은 시간이 곧 도전 마감이라 붉게 강조한다.</summary>
        private void BuildSeasonText(RectTransform root)
        {
            _seasonText = NewText("SeasonText", root, string.Empty, 28, TextAnchor.MiddleCenter);
            _seasonText.color = TextSeason;
            PlaceTopCenter(_seasonText.rectTransform, SeasonTextY, SeasonTextWidth, SeasonTextHeight);
        }

        /// <summary>하단 도전 바: 규칙 안내 한 줄 · [도전 시작] 버튼.
        /// <para>제한 시간도 도전 횟수도 없으므로 잔여 횟수 줄을 두지 않는다 — 도전은 완주하거나 전멸할 때 끝난다.</para></summary>
        private void BuildChallengeBar(RectTransform root)
        {
            var bar = NewChild("ChallengeBar", root);
            PlaceTopCenter(bar, ChallengeBarY, RowWidth, ChallengeBarHeight);

            var hint = NewText("Hint", bar, "5라운드를 회복 없이 이어 클리어해야 기록이 남습니다", 24, TextAnchor.UpperCenter);
            hint.color = TextDim;
            PlaceTopCenter(hint.rectTransform, -ChallengeHintY, RowWidth, 28f);

            _challengeButtonImage = NewImage("ChallengeButton", bar, FallbackButtonGold);
            ApplySliced(_challengeButtonImage, _btnGold, Color.white);
            PlaceTopCenter(_challengeButtonImage.rectTransform, 58f, ChallengeButtonWidth, ChallengeButtonHeight);
            _challengeButtonLabel = NewText("Label", _challengeButtonImage.rectTransform, "도전 시작", 34,
                TextAnchor.MiddleCenter);
            _challengeButtonLabel.fontStyle = FontStyle.Bold;
            Stretch(_challengeButtonLabel.rectTransform);
            _challengeButton = _challengeButtonImage.gameObject.AddComponent<Button>();
            _challengeButtonImage.gameObject.AddComponent<ButtonPunchScale>();
        }

        // ── [랭킹] 탭 ──

        /// <summary>랭킹 화면: 표 머리 · 10행 목록 · 페이지 이동 줄 · 내 순위 고정 바.</summary>
        private void BuildRankTab(RectTransform content)
        {
            var root = NewChild("RankTabRoot", content);
            Stretch(root);
            _rankTabRoot = root.gameObject;

            BuildTableHeader(root);
            BuildRankList(root);
            BuildMyRankArea(root);
            BuildRankFooter(root);
        }

        /// <summary>표 머리(순위·닉네임·기록).</summary>
        private void BuildTableHeader(RectTransform root)
        {
            var header = NewImage("TableHeader", root, FallbackHeader);
            ApplySliced(header, _tableHeader, ArtTintPanel);
            var hrt = header.rectTransform;
            PlaceTopCenter(hrt, TableHeaderY, RowWidth, TableHeaderHeight);

            var rank = NewText("RankHead", hrt, "순위", 24, TextAnchor.MiddleCenter);
            rank.color = TextTitle;
            PlaceMiddleLeft(rank.rectTransform, ColRankX, ColRankWidth, 30f);

            var name = NewText("NameHead", hrt, "닉네임", 24, TextAnchor.MiddleLeft);
            name.color = TextTitle;
            PlaceMiddleLeft(name.rectTransform, ColNameX, 200f, 30f);

            var record = NewText("RecordHead", hrt, "기록", 24, TextAnchor.MiddleRight);
            record.color = TextTitle;
            PlaceMiddleLeft(record.rectTransform, ColRecordX, ColRecordWidth, 30f);

            // 스크롤 위치·폴백 안내는 하단 줄에 둔다(표 머리 위에 두면 탭 줄과 겹친다).
        }

        /// <summary>랭킹 목록: 스크롤 입력 영역(마스크) + 그 안을 움직이는 행 판 + 재활용 행
        /// <see cref="RankRowViewCount"/>개.
        /// <para><b>목록 전체를 만들어 두지 않는다</b> — 순위는 서버에서 한 페이지씩 오므로(서버 기획서 5.4)
        /// 만들어 둘 내용이 없다. 그래서 <c>ScrollRect</c>가 아니라 입력만 받는
        /// <see cref="RankScrollArea"/> + 직접 움직이는 행 판을 쓴다.</para>
        /// <para><b>움직임은 픽셀 단위다.</b> 행 판(<see cref="_rankRowContainer"/>)을 스크롤 위치의 소수부만큼
        /// 올려 위아래로 걸친 행이 자연스럽게 잘리게 하고, 자르는 일은 영역에 붙인 <c>RectMask2D</c>가 한다
        /// (텍스트 선명화 셰이더도 <c>UNITY_UI_CLIP_RECT</c>를 지원하므로 글자까지 함께 잘린다).</para>
        /// <para><b>스크롤바를 두지 않는다</b> — 손잡이 위치가 "전체 목록을 들고 있다"는 인상을 주지만 실제로는
        /// 한 페이지만 존재한다. 이동은 휠·드래그로 하고 위치는 하단의 <c>N위 ~ M위</c> 표시로 알린다.</para></summary>
        private void BuildRankList(RectTransform root)
        {
            // 스크롤 입력을 받는 판(휠·드래그). 투명하지만 raycastTarget이 켜져 있어야 입력이 들어온다.
            var area = NewImage("RankScrollArea", root, new Color(0f, 0f, 0f, 0.001f));
            PlaceTopCenter(area.rectTransform, RankListY, RowWidth, RankListHeight);
            _rankScrollArea = area.gameObject.AddComponent<RankScrollArea>();
            // 위아래로 걸친 행을 뷰포트 경계에서 잘라 낸다(행 판이 뷰포트보다 크다).
            area.gameObject.AddComponent<RectMask2D>();

            // 행을 담아 세로로 움직이는 판. 마스크는 영역이 하므로 이 판은 크기와 무관하게 움직이기만 한다.
            _rankRowContainer = NewChild("RankRowContainer", area.rectTransform);
            _rankRowContainer.anchorMin = _rankRowContainer.anchorMax = new Vector2(0.5f, 1f);
            _rankRowContainer.pivot = new Vector2(0.5f, 1f);
            _rankRowContainer.anchoredPosition = Vector2.zero;
            _rankRowContainer.sizeDelta = new Vector2(RowWidth, RankListHeight);

            _rankRows = new RankRowView[RankRowViewCount];
            for (int i = 0; i < _rankRows.Length; i++)
            {
                // 줄무늬 색은 런타임에 <b>절대 순위</b>로 다시 정한다(여기 값은 에디터에서 보이는 초기 모습뿐).
                var rowImg = NewImage($"RankRow{i + 1}", _rankRowContainer, i % 2 == 0 ? SurfaceRow : SurfaceRowAlt);
                rowImg.raycastTarget = false; // 스크롤 입력은 아래 판이 받는다
                var rt = rowImg.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.anchoredPosition = new Vector2(0f, -i * RankRowHeight);
                rt.sizeDelta = new Vector2(RowWidth, RankRowHeight);

                var rank = NewText("Rank", rt, string.Empty, 26, TextAnchor.MiddleCenter);
                rank.fontStyle = FontStyle.Bold;
                PlaceMiddleLeft(rank.rectTransform, ColRankX, ColRankWidth, 32f);

                var nickname = NewText("Nickname", rt, string.Empty, 26, TextAnchor.MiddleLeft);
                nickname.color = TextPrimary;
                PlaceMiddleLeft(nickname.rectTransform, ColNameX, ColNameWidth, 32f);

                var record = NewText("Record", rt, string.Empty, 26, TextAnchor.MiddleRight);
                record.color = TextPrimary;
                PlaceMiddleLeft(record.rectTransform, ColRecordX, ColRecordWidth, 32f);

                _rankRows[i] = new RankRowView
                {
                    root = rowImg.gameObject,
                    background = rowImg,
                    rankText = rank,
                    nicknameText = nickname,
                    recordText = record,
                };
            }

            // 등재 인원이 0인 시즌(전환 직후) 안내 — 목록 자리 가운데.
            _rankEmptyText = NewText("RankEmpty", area.rectTransform, "아직 등재된 기록이 없습니다", 26,
                TextAnchor.MiddleCenter);
            _rankEmptyText.color = TextDim;
            Stretch(_rankEmptyText.rectTransform);
            _rankEmptyText.gameObject.SetActive(false);
        }

        /// <summary>
        /// <b>내 순위 고정 영역</b> — 스크롤을 어디로 옮겨도 항상 보인다. 목록 행과 <b>같은 열 좌표를 쓰지 않고</b>
        /// 왼쪽에 `내 순위` 라벨을 두고 순위·닉네임·기록을 잇는다(목록과 구분되게 강조 배경).
        /// 기록이 없으면 안내 한 줄로 대체한다.
        /// </summary>
        private void BuildMyRankArea(RectTransform root)
        {
            var area = NewImage("MyRankArea", root, SurfaceRowMine);
            var art = area.rectTransform;
            PlaceTopCenter(art, MyRankAreaY, RowWidth, MyRankAreaHeight);
            _myRankArea = area.gameObject;

            var label = NewText("Label", art, "내 순위", 22, TextAnchor.MiddleLeft);
            label.color = TextTitle;
            PlaceMiddleLeft(label.rectTransform, ColRankX, MyRankLabelWidth, 28f);

            _myRankAreaRank = NewText("Rank", art, string.Empty, 30, TextAnchor.MiddleCenter);
            _myRankAreaRank.fontStyle = FontStyle.Bold;
            _myRankAreaRank.color = TextGold;
            PlaceMiddleLeft(_myRankAreaRank.rectTransform, MyRankRankX, MyRankRankWidth, 36f);

            _myRankAreaName = NewText("Nickname", art, string.Empty, 26, TextAnchor.MiddleLeft);
            _myRankAreaName.color = TextPrimary;
            PlaceMiddleLeft(_myRankAreaName.rectTransform, MyRankNameX, MyRankNameWidth, 32f);

            _myRankAreaRecord = NewText("Record", art, string.Empty, 28, TextAnchor.MiddleRight);
            _myRankAreaRecord.fontStyle = FontStyle.Bold;
            _myRankAreaRecord.color = TextPrimary;
            PlaceMiddleLeft(_myRankAreaRecord.rectTransform, ColRecordX, ColRecordWidth, 34f);

            _myRankAreaEmpty = NewText("Empty", art, "이번 시즌 기록 없음 — 완주하면 등재됩니다", 24,
                TextAnchor.MiddleCenter);
            _myRankAreaEmpty.color = TextDim;
            Stretch(_myRankAreaEmpty.rectTransform);
            _myRankAreaEmpty.gameObject.SetActive(false);
        }

        /// <summary>하단 줄: 등재 인원·폴백 안내(좌) + [내 순위로 이동] 버튼(가운데) + 스크롤 위치(우).</summary>
        private void BuildRankFooter(RectTransform root)
        {
            var row = NewChild("RankFooter", root);
            PlaceTopCenter(row, RankFooterY, RowWidth, RankFooterHeight);

            _entryTotalText = NewText("EntryTotal", row, string.Empty, 24, TextAnchor.MiddleLeft);
            _entryTotalText.color = TextDim;
            var ert = _entryTotalText.rectTransform;
            ert.anchorMin = ert.anchorMax = new Vector2(0f, 0.5f);
            ert.pivot = new Vector2(0f, 0.5f);
            ert.anchoredPosition = new Vector2(ColRankX, 14f);
            ert.sizeDelta = new Vector2(280f, 28f);

            // 랭킹 캐시(Redis) 폴백 안내 — 서버가 source=2를 내려줄 때만 노출한다(더미에서는 항상 숨김).
            _fallbackNoticeText = NewText("FallbackNotice", row, "순위 반영 지연 가능", 22, TextAnchor.MiddleLeft);
            _fallbackNoticeText.color = TextDim;
            var frt = _fallbackNoticeText.rectTransform;
            frt.anchorMin = frt.anchorMax = new Vector2(0f, 0.5f);
            frt.pivot = new Vector2(0f, 0.5f);
            frt.anchoredPosition = new Vector2(ColRankX, -16f);
            frt.sizeDelta = new Vector2(300f, 26f);
            _fallbackNoticeText.gameObject.SetActive(false);

            // 지금 보고 있는 구간(스크롤 위치) — 페이지 번호가 없으므로 "몇 위 ~ 몇 위"로 알려 준다.
            _scrollPositionText = NewText("ScrollPosition", row, string.Empty, 24, TextAnchor.MiddleRight);
            _scrollPositionText.color = TextDim;
            var srt = _scrollPositionText.rectTransform;
            srt.anchorMin = srt.anchorMax = new Vector2(1f, 0.5f);
            srt.pivot = new Vector2(1f, 0.5f);
            srt.anchoredPosition = new Vector2(-ColRankX, 0f);
            srt.sizeDelta = new Vector2(280f, 30f);

            // [내 순위로 이동] — 내 순위가 화면 가운데 오도록 스크롤 위치를 옮긴다.
            var img = NewImage("JumpToMyRank", row, FallbackButtonWood);
            ApplySliced(img, _btnWood, Color.white);
            var jrt = img.rectTransform;
            jrt.anchorMin = jrt.anchorMax = new Vector2(0.5f, 0.5f);
            jrt.pivot = new Vector2(0.5f, 0.5f);
            jrt.anchoredPosition = Vector2.zero;
            jrt.sizeDelta = new Vector2(JumpButtonWidth, JumpButtonHeight);
            var t = NewText("Label", jrt, "내 순위로 이동", 26, TextAnchor.MiddleCenter);
            t.fontStyle = FontStyle.Bold;
            Stretch(t.rectTransform);
            _jumpToMyRankButton = img.gameObject.AddComponent<Button>();
        }

        // ── 런타임 배선·갱신 ──

        /// <summary>버튼 핸들러를 실행마다 다시 연결한다(<c>onClick</c>은 프리팹에 직렬화되지 않는다).</summary>
        private void WireRuntime()
        {
            PanelDragMove.Attach(transform.Find("PanelRoot") as RectTransform, "BossRush");

            if (_dimButton != null)
            {
                _dimButton.onClick.RemoveAllListeners();
                _dimButton.onClick.AddListener(Close);
            }
            if (_infoTabButton != null)
            {
                _infoTabButton.onClick.RemoveAllListeners();
                _infoTabButton.onClick.AddListener(() => SelectTab(false));
            }
            if (_rankTabButton != null)
            {
                _rankTabButton.onClick.RemoveAllListeners();
                _rankTabButton.onClick.AddListener(() => SelectTab(true));
            }
            if (_challengeButton != null)
            {
                _challengeButton.onClick.RemoveAllListeners();
                _challengeButton.onClick.AddListener(OnChallengeButton);
            }
            if (_rankScrollArea != null)
            {
                _rankScrollArea.Configure(RankWheelStep);
                _rankScrollArea.OnWheel = OnRankWheel;
                _rankScrollArea.OnDragMove = OnRankDrag;
            }
            if (_jumpToMyRankButton != null)
            {
                _jumpToMyRankButton.onClick.RemoveAllListeners();
                _jumpToMyRankButton.onClick.AddListener(OnJumpToMyRank);
            }
            if (_rewardDetailButton != null)
            {
                _rewardDetailButton.onClick.RemoveAllListeners();
                _rewardDetailButton.onClick.AddListener(() => SetRewardPopup(true));
            }
            if (_rewardPopupDimButton != null)
            {
                _rewardPopupDimButton.onClick.RemoveAllListeners();
                _rewardPopupDimButton.onClick.AddListener(() => SetRewardPopup(false));
            }
            if (_rewardPopupRoot != null)
            {
                _rewardPopupRoot.SetActive(false); // 열린 채로 구워졌더라도 시작은 항상 닫힘
            }
        }

        /// <summary>패널을 닫는다(창 밖 클릭).</summary>
        public void Close()
        {
            if (UIManager.Instance != null)
            {
                UIManager.Instance.Hide(UIManager.PanelType.BossRush);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }

        /// <summary>탭을 전환한다. [랭킹]으로 들어가면 1위 구간부터 조회하고 내 순위 고정 영역을 채운다.</summary>
        private void SelectTab(bool rank)
        {
            _rankTabOpen = rank;
            if (rank && _rewardPopupRoot != null)
            {
                _rewardPopupRoot.SetActive(false); // 보상 상세는 도전 화면의 팝업이다(랭킹 탭에서는 닫는다)
            }
            if (_infoTabRoot != null) _infoTabRoot.SetActive(!rank);
            if (_rankTabRoot != null) _rankTabRoot.SetActive(rank);
            // 선택/비선택 아트가 같은 스프라이트라(pixel_rpg_input_field) 밝기로 구분한다.
            ApplySliced(_infoTabImage, rank ? _btnCategory : _btnCategorySel, rank ? ArtTintPanel : TabTintSelected);
            ApplySliced(_rankTabImage, rank ? _btnCategorySel : _btnCategory, rank ? TabTintSelected : ArtTintPanel);

            if (rank)
            {
                // 탭에 들어올 때는 항상 1위부터 본다(이전 스크롤 위치를 기억하지 않는다 —
                // 페이지를 캐시하지 않고, 그 사이 순위가 바뀌었을 수 있다).
                _rankScroll = 0f;
                _rankScrollTarget = 0f;
                _rankPage.Clear();
                _rankPageOffset = -1;
                _rankPendingOffset = -1;
                _rankFailedOffset = -1;
                _rankPageRequestPending = false;
                _rankMessage = null;
                _rankRebindNeeded = true;
                _rankSource = 0;            // source 비교는 이 탭 세션 안에서만 의미가 있다
                _myRankSeasonRetried = false;
                ApplyRankView();
                // 내 순위는 패널을 열 때 이미 받아 두므로 다시 부르지 않는다 — 값이 없을 때(실패)만 재시도한다.
                if (_myRankSeasonId == 0)
                {
                    RequestMyRank();
                }
                RequestRankPage(0);
            }
        }

        /// <summary>진입 화면을 다시 그린다 — 서버에서 <c>info</c>(시즌·내 기록·해금 여부)와
        /// <c>my-rank</c>(내 순위·등재 인원)를 받고, 정적인 값(라운드 보스·순위 보상)은 번들로 채운다.
        /// <para><c>my-rank</c>를 패널을 열 때 함께 받는 이유는 두 가지다 — 내 기록 카드의 <c>N명 중 M위</c>에
        /// 필요한 <c>totalEntries</c>가 <c>info</c> 응답에 없고, 랭킹 탭의 고정 영역이 탭을 여는 즉시 채워진다.</para></summary>
        private void RefreshInfo()
        {
            ApplyStaticFromBundle();
            RequestInfo();
            RequestMyRank();
        }

        /// <summary>보스러시 정보를 조회한다(POST /api/game/boss-rush/info).</summary>
        private void RequestInfo()
        {
            if (NetworkManager.Instance == null || !Session.IsLoggedIn)
            {
                ApplyInfo(null);
                return;
            }
            if (_infoRequestInFlight)
            {
                return;
            }
            _infoRequestInFlight = true;

            var req = new AuthRequest { userId = Session.UserId, token = Session.Token };
            NetworkManager.Instance.PostToGame<BossRushInfoResponse>("/api/game/boss-rush/info", req, resp =>
            {
                if (this == null) return;
                _infoRequestInFlight = false;
                ApplyInfo(resp != null ? resp.data : null);
            }, error =>
            {
                if (this == null) return;
                _infoRequestInFlight = false;
                Debug.LogWarning($"[BossRush] 정보 조회 실패: {error}");
                ApplyInfo(null);
                ShowModal("보스 러시", ErrorMessages.ToKorean(error));
            });
        }

        /// <summary>
        /// <c>info</c> 응답으로 시즌·내 기록·도전 버튼을 채운다. <paramref name="data"/>가 null이면 조회 실패이며,
        /// 이때는 값을 지우고 버튼을 잠근다 — 옛 값을 남겨 두면 잠긴 콘텐츠에 도전을 시도하게 된다.
        /// </summary>
        private void ApplyInfo(BossRushInfoResultData data)
        {
            _info = data;
            if (data != null)
            {
                _seasonFetchRealtime = Time.unscaledTime;
                _seasonRemainingAtFetch = HasSeason
                    ? Mathf.Max(0f, data.season.endAt - data.serverTime)
                    : 0f;
                // 이미 0인 응답을 받았을 때 또 조회하면 무한 반복이 된다 — 잔여가 남았을 때만 감시를 켠다.
                _seasonExpiredHandled = _seasonRemainingAtFetch <= 0f;
            }

            ApplySeasonText();
            ApplyRecordCard();
            ApplyChallengeButton();
        }

        /// <summary>내 기록 카드(이번 시즌 최고 기록 + 순위)를 지금 가진 값으로 다시 그린다.
        /// 기록은 <c>info</c>, 순위 옆의 등재 인원은 <c>my-rank</c>가 채우므로 둘 중 하나가 늦게 와도 맞춰진다.</summary>
        private void ApplyRecordCard()
        {
            var record = _info != null ? _info.myRecord : null;
            bool hasRecord = record != null && record.bestClearMs > 0;

            if (_bestRecordText != null)
            {
                _bestRecordText.text = _info == null ? "기록을 불러오지 못했습니다"
                    : hasRecord ? BossRushFormat.Record(record.bestClearMs)
                    : "아직 기록이 없습니다";
                _bestRecordText.fontSize = hasRecord ? 46 : 28;
            }
            if (_myRankText != null)
            {
                int rank = hasRecord ? record.rank : 0;
                _myRankText.text = !hasRecord ? string.Empty
                    : rank <= 0 ? "순위 계산 중"                                     // 랭킹 캐시 폴백(서버가 0으로 내려준다)
                    : _rankTotalEntries > 0 ? $"{_rankTotalEntries:N0}명 중 {rank:N0}위"
                    : $"현재 {rank:N0}위";
            }
        }

        /// <summary>서버를 부르지 않고 번들만으로 그리는 부분(라운드 보스 5칸 · 시즌 순위 보상)을 채운다.</summary>
        private void ApplyStaticFromBundle()
        {
            MasterDataManager.EnsureLoaded();
            var db = MasterDataManager.Db;
            ApplyBossCells(db != null ? db.BossRushRoundsOrdered() : null);
            ApplyRankReward(db);
        }

        /// <summary>시즌 순위 보상을 번들(<c>boss_rush_rank_reward</c>)로 채운다 — 현재 1~3위 골드뿐이다.
        /// <para>도전 화면에는 제목만 두고 <b>등수별 금액은 상세 팝업의 줄</b>이 보여 주므로, 여기서 하는 일은
        /// 라벨 확정과 팝업 줄 재구성이다.</para></summary>
        private void ApplyRankReward(TaskbarHero.Client.MasterData.MasterDatabase db)
        {
            if (_rewardLabel != null)
            {
                _rewardLabel.text = "시즌 순위 보상";
            }
            RebuildRewardPopupRows(db != null ? db.BossRushRankRewardsOrdered() : null);
        }

        /// <summary>
        /// 순위 보상 상세 팝업의 등수 줄을 다시 만든다 — 위에서부터 1위·2위·3위 순으로 <b>세로로 나열</b>하고,
        /// 각 줄에 <b>공용 아이템 슬롯</b>(골드 = 아이템 코드 1)과 금액을 둔다.
        /// <para>번들 행 수에 맞춰 런타임에 만든다(구간이 늘면 줄도 늘어난다). 슬롯은 화면마다 새로 그리지 않고
        /// <c>ItemSlot.prefab</c> 하나만 쓰며, 골드는 마스터 상세가 없으므로 hover 상세를 끈다.</para>
        /// </summary>
        private void RebuildRewardPopupRows(List<BossRushRankReward> rows)
        {
            if (_rewardPopupBody == null)
            {
                return;
            }

            // 이전 줄 제거(같은 이름의 자식만 지운다 — 제목·닫기·안내는 구워진 계층이다).
            var old = new List<GameObject>();
            for (int i = 0; i < _rewardPopupBody.childCount; i++)
            {
                var child = _rewardPopupBody.GetChild(i);
                if (child != null && child.name.StartsWith("RewardRow"))
                {
                    old.Add(child.gameObject);
                }
            }
            foreach (var go in old)
            {
                Destroy(go);
            }

            if (rows == null || rows.Count == 0)
            {
                return;
            }

            float rowWidth = RewardPopupWidth - RewardPopupPadX * 2f;
            for (int i = 0; i < rows.Count; i++)
            {
                var reward = rows[i];
                if (reward == null)
                {
                    continue;
                }
                float y = RewardPopupRowsTop + i * (RewardPopupRowHeight + RewardPopupRowGap);

                var row = NewImage($"RewardRow{i}", _rewardPopupBody, i % 2 == 0 ? SurfaceRow : SurfaceRowAlt);
                PlaceTopCenter(row.rectTransform, y, rowWidth, RewardPopupRowHeight);

                // 등수 — 1~3위는 랭킹 목록과 같은 메달 색으로 구분한다.
                string rankText = reward.rankFrom == reward.rankTo
                    ? $"{reward.rankFrom}위"
                    : $"{reward.rankFrom}~{reward.rankTo}위";
                var rank = NewText("Rank", row.rectTransform, rankText, 30, TextAnchor.MiddleLeft);
                rank.fontStyle = FontStyle.Bold;
                rank.color = reward.rankFrom >= 1 && reward.rankFrom <= MedalColors.Length
                    ? MedalColors[reward.rankFrom - 1]
                    : TextPrimary;
                var rrt = rank.rectTransform;
                rrt.anchorMin = rrt.anchorMax = new Vector2(0f, 0.5f);
                rrt.pivot = new Vector2(0f, 0.5f);
                rrt.anchoredPosition = new Vector2(20f, 0f);
                rrt.sizeDelta = new Vector2(RewardRankLabelWidth, 40f);

                // 골드 아이콘 칸(공용 슬롯). 수량 표기는 칸 안에 넣기엔 자릿수가 길어 옆의 금액 문구로 뺀다.
                if (_itemSlotPrefab != null)
                {
                    var slot = Instantiate(_itemSlotPrefab, row.rectTransform);
                    var srt = (RectTransform)slot.transform;
                    srt.anchorMin = srt.anchorMax = new Vector2(0f, 0.5f);
                    srt.pivot = new Vector2(0f, 0.5f);
                    srt.anchoredPosition = new Vector2(20f + RewardRankLabelWidth + 16f, 0f);
                    srt.sizeDelta = new Vector2(RewardSlotSize, RewardSlotSize);
                    var view = slot.GetComponent<ItemSlotView>();
                    if (view != null)
                    {
                        view.Setup(GoldItemCode, reward.rewardGold, string.Empty, false);
                    }
                }

                var amount = NewText("Amount", row.rectTransform, $"{reward.rewardGold:N0}", 30, TextAnchor.MiddleRight);
                amount.fontStyle = FontStyle.Bold;
                amount.color = TextGold;
                var art = amount.rectTransform;
                art.anchorMin = art.anchorMax = new Vector2(1f, 0.5f);
                art.pivot = new Vector2(1f, 0.5f);
                art.anchoredPosition = new Vector2(-20f, 0f);
                art.sizeDelta = new Vector2(rowWidth - RewardRankLabelWidth - RewardSlotSize - 96f, 40f);
            }
        }

        /// <summary>순위 보상 상세 팝업을 열고 닫는다(열 때 번들 값으로 줄을 다시 그린다).</summary>
        private void SetRewardPopup(bool show)
        {
            if (_rewardPopupRoot == null)
            {
                return;
            }
            if (show)
            {
                MasterDataManager.EnsureLoaded();
                var db = MasterDataManager.Db;
                RebuildRewardPopupRows(db != null ? db.BossRushRankRewardsOrdered() : null);
            }
            _rewardPopupRoot.SetActive(show);
            SoundManager.Sfx(SoundId.UiClick);
        }

        /// <summary>시즌 번호·남은 시간 문구를 갱신한다(남은 시간이 0 이하면 정산 중으로 표시).</summary>
        private void ApplySeasonText()
        {
            if (_seasonText == null)
            {
                return;
            }
            if (!HasSeason)
            {
                _seasonText.text = string.Empty;
                return;
            }
            float remaining = SeasonRemainingSeconds;
            _seasonText.text = remaining > 0f
                ? $"시즌 {_info.season.seasonId} · 종료까지 {BossRushFormat.Countdown(remaining)}"
                : $"시즌 {_info.season.seasonId} · 시즌 정산 중";
        }

        /// <summary>라운드 보스 칸에 프리팹 초상·이름·레벨을 채운다(왼쪽이 라운드 1).
        /// <para>값은 <b>번들 <c>boss_rush_round</c></b>에서 온다 — 진입 화면에서 미리 보여 주려면 서버 호출
        /// (<c>enter</c>)이 필요해지는 모순이 생기므로 표시는 번들, 실제 전투 스폰은 <c>enter</c> 응답을 쓴다
        /// (보스러시 UI 기획서 2.1).</para></summary>
        private void ApplyBossCells(List<BossRushRoundMaster> rounds)
        {
            if (_bossCells == null)
            {
                return;
            }
            EnsurePortraits();
            for (int i = 0; i < _bossCells.Length; i++)
            {
                var view = _bossCells[i];
                if (view == null || view.root == null)
                {
                    continue;
                }
                bool has = rounds != null && i < rounds.Count;
                view.root.SetActive(has);
                if (!has)
                {
                    continue;
                }
                var r = rounds[i];
                if (view.nameText != null) view.nameText.text = ResolveMonsterName(r.bossMonsterCode);
                if (view.levelText != null) view.levelText.text = $"Lv.{r.bossMonsterLevel}";
                ApplyPortrait(i, r.bossMonsterCode);
            }
        }

        /// <summary>번들(<c>monster_master</c>)에서 몬스터 이름을 찾는다(없으면 코드를 그대로 보여 준다).
        /// <para>마스터의 보스 이름에는 <c>"암흑 마법사 (Act1 보스)"</c>처럼 <b>지역 표기 괄호가 붙어 있어</b>
        /// 칸에 그대로 쓰면 옆 칸을 침범한다 — 칸 순서가 곧 라운드이므로 괄호 앞까지만 쓴다.</para></summary>
        private static string ResolveMonsterName(int monsterCode)
        {
            MasterDataManager.EnsureLoaded();
            var db = MasterDataManager.Db;
            if (db != null && db.Monsters.TryGetValue(monsterCode, out var m) && !string.IsNullOrEmpty(m.name))
            {
                int paren = m.name.IndexOf(" (");
                return paren > 0 ? m.name.Substring(0, paren) : m.name;
            }
            return $"#{monsterCode}";
        }

        /// <summary>칸별 초상 렌더러(전용 카메라 + RenderTexture)를 최초 1회 만든다. 칸마다 화면 밖 격리 위치를
        /// 따로 써서(<see cref="PortraitStageSpacing"/>) 같은 레이어를 쓰는 5개 프리팹이 서로의 카메라에
        /// 잡히지 않게 한다. 플레이 중에만 만든다(에디터 빌드에서는 계층만 굽는다).</summary>
        private void EnsurePortraits()
        {
            if (!Application.isPlaying || _bossCells == null)
            {
                return;
            }
            if (_portraits != null)
            {
                foreach (var stage in _portraitStages)
                {
                    if (stage != null) stage.SetActive(true);
                }
                return;
            }

            _portraits = new CharacterPortrait[_bossCells.Length];
            _portraitStages = new GameObject[_bossCells.Length];
            _portraitMonsterCode = new int[_bossCells.Length];
            for (int i = 0; i < _bossCells.Length; i++)
            {
                _portraitMonsterCode[i] = 0;
                var origin = PortraitStageOrigin + new Vector3(i * PortraitStageSpacing, 0f, 0f);
                var stage = new GameObject($"BossPortraitStage{i}");
                stage.transform.position = origin;
                var p = stage.AddComponent<CharacterPortrait>();
                p.Initialize(_bossCells[i].portrait, PortraitLayer, PortraitRtWidth, PortraitRtHeight,
                    PortraitOrtho, PortraitAim, PortraitBg, origin);
                _portraits[i] = p;
                _portraitStages[i] = stage;
            }
        }

        /// <summary>칸 <paramref name="index"/>의 초상에 그 라운드 보스 프리팹을 세운다(같은 코드면 그대로 둔다).</summary>
        private void ApplyPortrait(int index, int monsterCode)
        {
            if (_portraits == null || index >= _portraits.Length || _portraits[index] == null)
            {
                return;
            }
            if (_portraitMonsterCode[index] == monsterCode)
            {
                return;
            }
            _portraitMonsterCode[index] = monsterCode;
            _portraits[index].SetCharacter(FindBossPrefab(monsterCode));
        }

        /// <summary>몬스터 코드로 보스 프리팹을 찾는다(배선이 없으면 null — 초상은 배경색만 남는다).</summary>
        private GameObject FindBossPrefab(int monsterCode)
        {
            if (_bossPrefabs == null)
            {
                return null;
            }
            foreach (var e in _bossPrefabs)
            {
                if (e != null && e.monsterCode == monsterCode)
                {
                    return e.prefab;
                }
            }
            Debug.LogWarning($"[BossRush] 보스 프리팹이 배선되지 않았습니다: monster_{monsterCode}");
            return null;
        }

        /// <summary>[도전 시작] 버튼의 표시·활성 상태를 상태에 맞춘다.
        /// <para>조회 실패(<c>_info == null</c>)도 하나의 상태로 다룬다 — 버튼을 잠그고 그 사실을 적는다.</para>
        /// <para>도전 횟수 제한이 없으므로 막는 조건은 <b>해금 미달 · 시즌 정산 중 · 도전 진행 중</b> 셋뿐이다.</para></summary>
        private void ApplyChallengeButton()
        {
            bool loaded = _info != null;
            bool locked = loaded && !_info.unlocked;
            bool settling = loaded && SeasonRemainingSeconds <= 0f;
            var flow = BossRushBattleFlow.Find();
            bool running = flow != null && flow.IsRunning;   // 전투 중에는 다시 도전할 수 없다
            bool canChallenge = loaded && !locked && !settling && !running;

            if (_challengeButtonLabel != null)
            {
                _challengeButtonLabel.text = !loaded ? "정보를 불러오지 못했습니다"
                    : running ? "도전 진행 중"
                    : locked ? $"스테이지 {_info.unlockStageSequence} 클리어 후 해금"
                    : settling ? "시즌 정산 중"
                    : "도전 시작";
                _challengeButtonLabel.fontSize = canChallenge ? 34 : locked || !loaded ? 24 : 28;
            }
            if (_challengeButton != null)
            {
                _challengeButton.interactable = canChallenge;
            }
            if (_challengeButtonImage != null)
            {
                // 비활성은 색을 눌러 표시한다(전용 disabled 아트를 따로 쓰지 않는다).
                _challengeButtonImage.color = canChallenge ? Color.white : new Color(0.55f, 0.55f, 0.55f, 1f);
            }
        }

        /// <summary>
        /// [도전 시작] — <c>enter</c>로 런을 열고 응답의 5라운드 구성으로
        /// <see cref="BossRushBattleFlow"/>에 전투를 넘긴 뒤 패널을 닫는다.
        /// <para><b>응답을 받기 전에는 전투를 시작하지 않는다</b> — <c>runId</c>가 있어야 완주 시 기록을 보고할 수 있고,
        /// 잠금·시즌 상태 판정은 서버가 정본이기 때문이다. 실패하면 문구를 안내하고
        /// <c>info</c>를 다시 받아 화면 상태를 서버 값으로 되돌린다.</para>
        /// </summary>
        private void OnChallengeButton()
        {
            var flow = BossRushBattleFlow.Find();
            if (flow == null)
            {
                ShowModal("보스 러시", "전투 화면을 찾지 못해 도전을 시작할 수 없습니다.");
                return;
            }
            if (flow.IsRunning)
            {
                ShowModal("보스 러시", "이미 도전이 진행 중입니다.");
                return;
            }
            if (_enterRequestInFlight)
            {
                return;
            }
            if (NetworkManager.Instance == null || !Session.IsLoggedIn)
            {
                ShowModal("보스 러시", "서버에 연결되어 있지 않아 도전을 시작할 수 없습니다.");
                return;
            }

            _enterRequestInFlight = true;
            var req = new AuthRequest { userId = Session.UserId, token = Session.Token };
            NetworkManager.Instance.PostToGame<BossRushEnterResponse>("/api/game/boss-rush/enter", req, resp =>
            {
                if (this == null) return;
                _enterRequestInFlight = false;

                var data = resp != null ? resp.data : null;
                if (data == null || data.rounds == null || data.rounds.Count == 0)
                {
                    ShowModal("보스 러시", "라운드 구성을 받지 못해 도전을 시작하지 못했습니다.");
                    RequestInfo();
                    return;
                }

                Debug.Log($"[BossRush] 도전 시작 run={data.runId} 시즌={data.seasonId} 라운드={data.rounds.Count}");

                flow.StartRun(data, ErrorMessages.ToKorean);
                Close();   // 전투가 시작되므로 패널은 자동으로 닫는다
            }, error =>
            {
                if (this == null) return;
                _enterRequestInFlight = false;
                Debug.LogWarning($"[BossRush] 도전 시작 실패: {error}");
                ShowModal("보스 러시", ErrorMessages.ToKorean(error));
                RequestInfo();   // 해금·시즌 상태를 서버 값으로 되돌린다
            });
        }

        /// <summary>내 순위를 조회한다(POST /api/game/boss-rush/my-rank). 랭킹 UI의 고정 영역이 쓰는 값이라
        /// 목록을 스크롤하는 동안 다시 부르지 않는다(서버 기획서 5.5).</summary>
        private void RequestMyRank()
        {
            if (NetworkManager.Instance == null || !Session.IsLoggedIn)
            {
                ApplyMyRank(null, 0);
                return;
            }
            var req = new BossRushMyRankRequest
            {
                userId = Session.UserId,
                token = Session.Token,
                data = new BossRushMyRankData { seasonId = 0 },   // 0 = 현재 시즌
            };
            NetworkManager.Instance.PostToGame<BossRushMyRankResponse>("/api/game/boss-rush/my-rank", req, resp =>
            {
                if (this == null) return;
                var data = resp != null ? resp.data : null;
                _myRankSeasonId = data != null ? data.seasonId : 0;
                ApplyMyRank(data != null ? data.myRank : null, data != null ? data.totalEntries : 0);
            }, error =>
            {
                if (this == null) return;
                Debug.LogWarning($"[BossRush] 내 순위 조회 실패: {error}");
                ApplyMyRank(null, _rankTotalEntries);
            });
        }

        /// <summary>내 순위 고정 영역을 채운다. 기록이 없으면(<paramref name="me"/> null) 안내 한 줄로 대체한다.
        /// 등재 인원은 내 기록 카드의 <c>N명 중 M위</c>에도 쓰이므로 함께 반영한다.</summary>
        private void ApplyMyRank(BossRushRankEntryDto me, int totalEntries)
        {
            // rank까지 보는 이유는 HasSeason과 같다 — 서버가 내려준 "myRank": null이 Unity 직렬화에서
            // 0으로 채워진 객체가 될 수 있어, null 검사만으로는 `0위`가 화면에 뜬다.
            bool has = me != null && me.rank > 0;
            _myRank = has ? me.rank : 0;
            if (totalEntries > 0)
            {
                _rankTotalEntries = totalEntries;
            }
            if (_myRankAreaEmpty != null) _myRankAreaEmpty.gameObject.SetActive(!has);
            if (_myRankAreaRank != null)
            {
                _myRankAreaRank.gameObject.SetActive(has);
                if (has) _myRankAreaRank.text = $"{me.rank:N0}위";
            }
            if (_myRankAreaName != null)
            {
                _myRankAreaName.gameObject.SetActive(has);
                if (has) _myRankAreaName.text = me.nickname;
            }
            if (_myRankAreaRecord != null)
            {
                _myRankAreaRecord.gameObject.SetActive(has);
                if (has) _myRankAreaRecord.text = BossRushFormat.Record(me.clearMs);
            }
            if (_jumpToMyRankButton != null)
            {
                _jumpToMyRankButton.interactable = has;
            }

            ApplyRecordCard();
            ApplyScrollState();
        }

        /// <summary>스크롤 위치의 상한(절대 행 단위) — 마지막 화면이 목록의 끝에 딱 맞게 선다.</summary>
        private float MaxRankScroll => Mathf.Max(0, _rankTotalEntries - RankRowsPerView);

        /// <summary>휠: 목표 위치만 옮긴다(현재 위치는 <see cref="UpdateRankScroll"/>이 부드럽게 따라간다).</summary>
        private void OnRankWheel(float pixels)
        {
            if (!_rankTabOpen)
            {
                return;
            }
            SetRankScroll(_rankScrollTarget + pixels / RankRowHeight, false);
        }

        /// <summary>드래그: 손을 따라 <b>즉시</b> 옮긴다(이징을 넣으면 끌리는 느낌이 늦게 따라온다).</summary>
        private void OnRankDrag(float pixels)
        {
            if (!_rankTabOpen)
            {
                return;
            }
            SetRankScroll(_rankScroll + pixels / RankRowHeight, true);
        }

        /// <summary>
        /// 스크롤 위치를 옮긴다(절대 행 단위, 소수 허용). <paramref name="immediate"/>면 화면도 그 자리로 바로
        /// 옮기고, 아니면 목표만 잡아 두고 <see cref="UpdateRankScroll"/>이 따라간다.
        /// </summary>
        private void SetRankScroll(float rows, bool immediate)
        {
            _rankScrollTarget = Mathf.Clamp(rows, 0f, MaxRankScroll);
            if (immediate)
            {
                _rankScroll = _rankScrollTarget;
            }
            ApplyRankView();
            ApplyScrollState();
            EnsureRankPage();
        }

        /// <summary>휠로 잡힌 목표까지 부드럽게 따라가고, 묶여 있던 페이지 조회를 내보낸다(매 프레임).</summary>
        private void UpdateRankScroll()
        {
            if (!Mathf.Approximately(_rankScroll, _rankScrollTarget))
            {
                // 지수 감쇠 — 남은 거리에 비례해 줄어들어 멀리 튀는 이동도 같은 시간에 붙는다.
                float t = 1f - Mathf.Exp(-RankScrollEase * Time.unscaledDeltaTime);
                _rankScroll = Mathf.Lerp(_rankScroll, _rankScrollTarget, t);
                if (Mathf.Abs(_rankScrollTarget - _rankScroll) < 0.002f)
                {
                    _rankScroll = _rankScrollTarget;
                }
                ApplyRankView();
                ApplyScrollState();
            }
            if (_rankPageRequestPending)
            {
                EnsureRankPage();
            }
        }

        /// <summary>
        /// 보고 있는 구간이 <b>들고 있는 페이지 밖으로 나가면</b> 새 페이지를 받는다 — 페이지 안에서는 서버를
        /// 부르지 않으므로 스크롤이 끊기지 않는다.
        /// <para>같은 페이지를 두 번 부르지 않고(들고 있거나 받고 있거나 방금 실패한 오프셋은 건너뛴다),
        /// 빠르게 굴려 경계를 연달아 넘을 때는 <see cref="RankRequestInterval"/>로 묶는다.</para>
        /// </summary>
        private void EnsureRankPage()
        {
            int first = Mathf.FloorToInt(_rankScrollTarget);
            int last = first + RankRowsPerView;                 // 아래로 걸친 행까지 덮여야 한다
            if (_rankTotalEntries > 0)
            {
                last = Mathf.Min(last, _rankTotalEntries - 1);
            }
            bool covered = _rankPageOffset >= 0
                && first >= _rankPageOffset
                && last < _rankPageOffset + _rankPage.Count;
            if (covered)
            {
                _rankPageRequestPending = false;
                return;
            }

            int wanted = PageOffsetFor(first);
            if (wanted == _rankPageOffset || wanted == _rankPendingOffset || wanted == _rankFailedOffset)
            {
                _rankPageRequestPending = false;
                return;
            }
            if (Time.unscaledTime - _lastRankRequestTime < RankRequestInterval)
            {
                _rankPageRequestPending = true;   // 조금 뒤에 다시 본다(Update)
                return;
            }
            RequestRankPage(wanted);
        }

        /// <summary>보고 있는 구간을 <b>페이지 가운데</b>에 두는 페이지 오프셋. 어느 쪽으로 스크롤하든
        /// 다음 조회까지 여유가 같아진다(위아래로 각각 20행).</summary>
        private int PageOffsetFor(int firstVisible)
        {
            int maxPageOffset = Mathf.Max(0, _rankTotalEntries - RankPageSize);
            return Mathf.Clamp(firstVisible - (RankPageSize - RankRowsPerView) / 2, 0, maxPageOffset);
        }

        /// <summary>
        /// 랭킹 한 페이지를 조회한다(POST /api/game/boss-rush/rank, <see cref="RankPageSize"/>행).
        /// <para>페이지를 이어붙이지 않는다 — 응답이 오면 들고 있던 페이지를 <b>통째로 교체</b>한다
        /// (서버 기획서 5.4의 페이지 교체형 규약). 다만 <b>응답을 기다리는 동안 화면을 비우지 않는다</b>:
        /// 행은 자기 순위의 데이터만 그리므로(<see cref="EntryAt"/>) 아직 없는 순위는 빈 자리로 남고
        /// 이미 있는 순위는 그대로 보인다 — 다른 구간의 데이터가 보이는 순간은 생기지 않는다.</para>
        /// <para>늦게 도착한 옛 응답은 요청 순번(<see cref="_rankRequestSeq"/>)으로 버린다.</para>
        /// </summary>
        private void RequestRankPage(int pageOffset)
        {
            _lastRankRequestTime = Time.unscaledTime;
            _rankPageRequestPending = false;
            _rankMessage = null;

            if (NetworkManager.Instance == null || !Session.IsLoggedIn)
            {
                _rankPendingOffset = -1;
                SetRankMessage("로그인이 필요합니다.");
                return;
            }

            int seq = ++_rankRequestSeq;
            _rankPendingOffset = pageOffset;
            ApplyRankMessage();

            var req = new BossRushRankRequest
            {
                userId = Session.UserId,
                token = Session.Token,
                data = new BossRushRankData { seasonId = 0, offset = pageOffset, limit = RankPageSize },
            };
            NetworkManager.Instance.PostToGame<BossRushRankResponse>("/api/game/boss-rush/rank", req, resp =>
            {
                if (this == null || seq != _rankRequestSeq) return;   // 뒤늦게 온 옛 페이지는 버린다
                _rankPendingOffset = -1;
                ApplyRankPage(resp != null ? resp.data : null, pageOffset);
            }, error =>
            {
                if (this == null || seq != _rankRequestSeq) return;
                _rankPendingOffset = -1;
                _rankFailedOffset = pageOffset;   // 스크롤이 다른 페이지로 갈 때까지 같은 실패를 되풀이하지 않는다
                Debug.LogWarning($"[BossRush] 랭킹 조회 실패: {error}");
                SetRankMessage(ErrorMessages.ToKorean(error));
            });
        }

        /// <summary>
        /// 받은 페이지로 들고 있던 페이지를 교체한다.
        /// <para><b>끝 판정을 캐시하지 않는다</b>: 매 응답의 <c>totalEntries</c>로 스크롤 상한을 다시 잡고,
        /// 요청 구간이 끝을 넘어 빈 페이지가 오면 마지막 페이지로 당겨 다시 조회한다. <c>source</c>가 바뀌면
        /// (캐시 ↔ MySQL 폴백) 경계가 크게 어긋날 수 있어 <b>1위부터 다시</b> 본다. <c>my-rank</c>와 시즌이
        /// 다르면 그 사이 시즌이 넘어간 것이므로 내 순위를 한 번 다시 받는다(서버 기획서 6.5).</para>
        /// </summary>
        private void ApplyRankPage(BossRushRankResultData data, int requestedOffset)
        {
            if (data == null)
            {
                _rankFailedOffset = requestedOffset;
                SetRankMessage("랭킹을 불러오지 못했습니다.");
                return;
            }

            _rankTotalEntries = data.totalEntries;
            int count = data.entries != null ? data.entries.Count : 0;

            // 요청 구간이 끝을 넘었으면(등재 인원이 줄었을 수 있다) 마지막 페이지로 당겨 다시 조회한다.
            if (count == 0 && _rankTotalEntries > 0 && data.offset > 0)
            {
                RequestRankPage(Mathf.Max(0, _rankTotalEntries - RankPageSize));
                return;
            }

            if (_rankSource != 0 && data.source != _rankSource)
            {
                _rankSource = data.source;
                _rankPage.Clear();
                _rankPageOffset = -1;
                _rankFailedOffset = -1;
                _rankRebindNeeded = true;
                SetRankScroll(0f, true);   // 출처가 바뀌면 1위부터 다시(그 과정에서 페이지를 새로 받는다)
                return;
            }
            _rankSource = data.source;

            if (!_myRankSeasonRetried && _myRankSeasonId != 0 && data.seasonId != _myRankSeasonId)
            {
                _myRankSeasonRetried = true;
                RequestMyRank();
            }

            _rankPage.Clear();
            if (data.entries != null)
            {
                _rankPage.AddRange(data.entries);
            }
            // 행이 0개여도 오프셋을 남긴다 — "그 페이지는 이미 받아 봤다"는 표시가 되어 같은 조회를 되풀이하지 않는다.
            _rankPageOffset = data.offset;
            _rankFailedOffset = -1;
            _rankMessage = null;
            _rankRebindNeeded = true;

            SetRankScroll(_rankScrollTarget, false);   // 등재 인원이 줄었으면 위치를 당긴다

            if (_fallbackNoticeText != null)
            {
                _fallbackNoticeText.gameObject.SetActive(data.source == 2);
            }
        }

        /// <summary>절대 순위 인덱스(0-based)에 해당하는 행 데이터. 들고 있는 페이지 밖이면 null(빈 자리).</summary>
        private BossRushRankEntryDto EntryAt(int index)
        {
            if (_rankPageOffset < 0 || index < _rankPageOffset)
            {
                return null;
            }
            int local = index - _rankPageOffset;
            return local < _rankPage.Count ? _rankPage[local] : null;
        }

        /// <summary>
        /// 지금 스크롤 위치에 맞춰 행 판을 옮기고 재활용 행에 데이터를 채운다.
        /// <para>행 판을 스크롤 위치의 <b>소수부만큼 위로</b> 올려 위아래로 걸친 행이 잘리게 하고, 행 <c>i</c>에는
        /// 절대 순위 <c>floor(scroll) + i</c>를 그린다. 들고 있는 페이지 밖의 행은 숨긴다(빈 자리).</para>
        /// </summary>
        private void ApplyRankView()
        {
            if (_rankRows == null)
            {
                return;
            }

            int first = Mathf.FloorToInt(_rankScroll);
            float frac = _rankScroll - first;
            if (_rankRowContainer != null)
            {
                _rankRowContainer.anchoredPosition = new Vector2(0f, frac * RankRowHeight);
            }

            // 행 한 칸을 넘지 않는 이동이면 <b>판만 움직이고 내용은 그대로 둔다</b> — 매 프레임 같은 값을
            // 다시 넣으면 문자열이 프레임마다 새로 만들어져 스크롤 중 GC가 돈다.
            if (first == _rankBoundFirst && !_rankRebindNeeded)
            {
                return;
            }
            _rankBoundFirst = first;
            _rankRebindNeeded = false;
            _rankAnyRowVisible = false;

            for (int i = 0; i < _rankRows.Length; i++)
            {
                var view = _rankRows[i];
                if (view == null || view.root == null)
                {
                    continue;
                }
                int index = first + i;
                var e = EntryAt(index);
                bool has = e != null;
                view.root.SetActive(has);
                if (!has)
                {
                    continue;
                }
                _rankAnyRowVisible = true;

                if (view.rankText != null)
                {
                    view.rankText.text = $"{e.rank:N0}";
                    view.rankText.color = e.rank >= 1 && e.rank <= MedalColors.Length
                        ? MedalColors[e.rank - 1]
                        : TextPrimary;
                }
                if (view.nicknameText != null) view.nicknameText.text = e.nickname;
                if (view.recordText != null) view.recordText.text = BossRushFormat.Record(e.clearMs);
                if (view.background != null)
                {
                    // 줄무늬는 <b>절대 순위</b>로 정한다 — 행 번호로 정하면 스크롤할 때마다 무늬가 뒤집힌다.
                    bool isMe = e.userId == Session.UserId;
                    view.background.color = isMe ? SurfaceRowMine : (index % 2 == 0 ? SurfaceRow : SurfaceRowAlt);
                }
            }
        }

        /// <summary>스크롤 위치 표시(`N위 ~ M위`)와 등재 인원 문구를 현재 위치에 맞춘다.
        /// <para>스크롤바를 두지 않으므로 <b>이 문구가 유일한 위치 표시</b>다.</para></summary>
        private void ApplyScrollState()
        {
            int first = Mathf.FloorToInt(_rankScroll);
            int shown = Mathf.Min(RankRowsPerView, Mathf.Max(0, _rankTotalEntries - first));

            if (_scrollPositionText != null)
            {
                _scrollPositionText.text = shown > 0
                    ? $"{first + 1:N0}위 ~ {first + shown:N0}위"
                    : string.Empty;
            }
            if (_entryTotalText != null)
            {
                _entryTotalText.text = $"등재 {_rankTotalEntries:N0}명";
            }
            ApplyRankMessage();
        }

        /// <summary>목록 자리 안내(조회 실패 등)를 지정하고 즉시 반영한다. null·빈 문자열이면 안내를 지운다.</summary>
        private void SetRankMessage(string message)
        {
            _rankMessage = message;
            ApplyScrollState();
        }

        /// <summary>
        /// 목록 자리의 안내 한 줄을 상태에 맞춘다.
        /// <para><b>그려진 행이 하나라도 있으면 안내를 띄우지 않는다</b> — 행을 덮어 가리는 것이 조회 중임을
        /// 알리는 것보다 나쁘고, 보이는 행은 자기 순위의 정확한 값이다. 화면이 빌 때만 조회 중·오류·등재 0명을
        /// 알린다.</para>
        /// </summary>
        private void ApplyRankMessage()
        {
            if (_rankEmptyText == null)
            {
                return;
            }
            string text = _rankAnyRowVisible ? null
                : !string.IsNullOrEmpty(_rankMessage) ? _rankMessage
                : _rankPendingOffset >= 0 ? "불러오는 중…"
                : _rankTotalEntries <= 0 ? "아직 등재된 기록이 없습니다"
                : null;
            _rankEmptyText.gameObject.SetActive(text != null);
            if (text != null)
            {
                _rankEmptyText.text = text;
            }
        }

        /// <summary>[내 순위로 이동]: 내 순위가 <b>화면 가운데</b>에 오도록 스크롤 위치를 옮긴다
        /// (위치 계산은 클라이언트 몫 — 서버는 계산해 주지 않는다).
        /// <para>부드럽게 따라가지 않고 <b>즉시</b> 옮긴다 — 수백 행을 건너뛰는 이동이라 따라가는 동안
        /// 들고 있지 않은 빈 구간만 스쳐 지나가게 된다.</para></summary>
        private void OnJumpToMyRank()
        {
            if (_myRank <= 0)
            {
                ShowModal("보스 러시", "이번 시즌 기록이 없어 이동할 순위가 없습니다.");
                return;
            }
            SetRankScroll(_myRank - 1 - RankRowsPerView / 2f, true);
        }

        /// <summary>안내·오류는 패널 안 메시지 줄이 아니라 공용 모달로 띄운다.</summary>
        private static void ShowModal(string title, string message)
        {
            if (ModalManager.Instance != null)
            {
                ModalManager.Instance.ShowConfirm(title, message);
            }
            else
            {
                Debug.Log($"[BossRush] {title}: {message}");
            }
        }

        // ── UI 생성 헬퍼 ──

        /// <summary>콘텐츠(또는 부모) <b>위쪽 가운데</b> 기준으로 놓는다(y는 아래로 +).</summary>
        private static void PlaceTopCenter(RectTransform rt, float y, float width, float height)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -y);
            rt.sizeDelta = new Vector2(width, height);
        }

        /// <summary>행 안에서 좌측 기준 x 위치·크기로 세로 중앙 배치한다.</summary>
        private static void PlaceMiddleLeft(RectTransform rt, float x, float width, float height)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(x, 0f);
            rt.sizeDelta = new Vector2(width, height);
        }

        /// <summary>카드 안에서 좌측 상단 기준으로 배치한다(y는 음수가 아래).</summary>
        private static void PlaceInside(RectTransform rt, float x, float y, float width, float height)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(width, height);
        }

        private static void ApplySliced(Image img, Sprite sprite, Color? tint = null)
        {
            if (img == null || sprite == null) return;
            img.sprite = sprite;
            img.type = Image.Type.Sliced;
            img.color = tint ?? Color.white;
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
