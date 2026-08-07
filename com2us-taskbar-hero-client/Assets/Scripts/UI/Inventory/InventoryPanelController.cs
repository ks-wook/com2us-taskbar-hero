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
    /// 인벤토리/장비 오버레이 패널. 계층은 에디터 빌드 시 생성되어 프리팹에 정적으로 저장되고
    /// (에디터에서 바로 보임), 런타임에는 직렬화된 참조에 이벤트·표시만 배선한다.
    /// 표시될 때마다 세션 세이브(<see cref="Session.GameData"/>)의 실데이터로 골드·보유 아이템·장착을 채운다.
    /// 기획서: docs/ui/인벤토리-ui-기획서.md
    /// </summary>
    public class InventoryPanelController : MonoBehaviour
    {
        [Header("UI 리소스 (Assets/Art/UI/Inventory)")]
        [SerializeField] private Sprite panelBackground; // ui_bg_2(스킬·룬 패널과 공용 프레임)
        [SerializeField] private Sprite slotNormal;      // ui_slot_normal
        [SerializeField] private Sprite slotHighlight;   // ui_slot_highlight
        [SerializeField] private Sprite slotPortrait;    // ui_slot_portrait
        [Tooltip("캐릭터 전환 화살표(Assets/Art/UI/화살표버튼.png). 아트는 <b>오른쪽(다음)</b> 방향이며 " +
                 "이전 버튼은 같은 스프라이트를 좌우 반전해 쓴다.")]
        [SerializeField] private Sprite navArrow;
        [Tooltip("경험치 막대 배경 프레임(Assets/Art/Icon/Combat/체력바.png). 전투 몬스터 HP바와 같은 아트를 쓴다.")]
        [SerializeField] private Sprite expBarFrame;

        [Header("공용 아이템 슬롯 프리팹 (에디터 빌더가 배선)")]
        [Tooltip("Assets/Prefabs/UI/ItemSlot.prefab — 가방 칸·장비 부위 칸의 아이콘·등급 배경·수량·강화 배지를 " +
                 "그리는 공용 슬롯. 큐브·거래소·우편함 등 다른 화면과 같은 프리팹을 써서 외형을 통일한다.")]
        [SerializeField] private GameObject _itemSlotPrefab;

        [Header("초상화 캐릭터 프리팹 (classCode → 프리팹, 에디터 빌더가 배선)")]
        [Tooltip("초상화에 렌더할 캐릭터 프리팹. classCode 기준으로 선택된다(기사1·레인저2·마법사3).")]
        [SerializeField] private List<ClassCharacter> _classCharacters = new List<ClassCharacter>();

        [Header("격자 설정")]
        [SerializeField] private int columns = 5;
        [Tooltip("최초 아이템 슬롯 수. 그리드 마지막에는 확장 버튼 1칸이 추가된다(초기 총 칸 = +1).")]
        [SerializeField] private int initialItemSlots = 14;
        [Tooltip("한 번에 보이는 줄 수(스크롤). 2줄 = 10칸.")]
        [SerializeField] private int visibleRows = 2;
        [Tooltip("스크롤 지연 로딩의 페이지 크기(칸 수). 창고를 열면 이만큼만 먼저 받고, 스크롤이 아직 받지 않은 칸에 닿으면 한 페이지씩 더 받는다.")]
        [SerializeField] private int bagPageLimit = InventoryLoader.ScrollPageLimit;
        [Tooltip("미리 받아 둘 여유 줄 수. 뷰포트 아래로 이만큼 더 채워 두어 스크롤이 빈 칸에 닿기 전에 도착하게 한다.")]
        [SerializeField] private int prefetchRows = 2;

        [Header("구성 참조 (에디터 빌더가 배선 — 직접 수정 불필요)")]
        [SerializeField] private List<InventoryItemSlot> _gridSlots = new List<InventoryItemSlot>();
        [SerializeField] private List<InventoryItemSlot> _equipSlots = new List<InventoryItemSlot>();
        [SerializeField] private InventoryTooltip _tooltip;
        [SerializeField] private Text _charIndicatorText;
        [SerializeField] private Text _portraitLabel;
        [SerializeField] private RawImage _portraitImage;     // 초상화 캐릭터 렌더 표시(런타임 텍스처 배정)
        [SerializeField] private Button _prevButton;
        [SerializeField] private Button _nextButton;
        // 화살표 클릭 피드백(잠깐 커졌다 원래대로). 화살표 자식에 붙어 있어 가운데 기준으로 커진다.
        [SerializeField] private ButtonPunchScale _prevPunch;
        [SerializeField] private ButtonPunchScale _nextPunch;
        [SerializeField] private Button _skillButton;   // 스킬 레벨업 패널 진입
        [SerializeField] private Button _runeButton;    // 룬 패널 진입
        [SerializeField] private Button _cubeButton;    // 큐브 패널 진입
        // 닫기(X) 버튼은 미관상 두지 않는다 — 창 밖(딤)을 눌러 닫는다.
        [SerializeField] private Button _dimButton;
        [SerializeField] private RectTransform _gridContent; // 스크롤 콘텐츠(슬롯 부모)
        [SerializeField] private Button _expandButton;        // 확장 요청 버튼(항상 마지막 칸)
        [SerializeField] private Image _goldIcon;             // 골드 아이콘(item_1, 런타임 배정)
        [SerializeField] private Text _goldText;              // 보유 골드량
        [SerializeField] private Text _statNameText;          // 능력치 패널 왼쪽 열(능력치명)
        [SerializeField] private Text _statValueText;         // 능력치 패널 오른쪽 열(값 — 오른쪽 정렬로 줄 맞춤)
        [SerializeField] private RectTransform _expFill;       // 경험치 막대 채움(anchorMax.x = 진행률로 폭 조절)
        [SerializeField] private Text _expText;                // 경험치 텍스트(현재/필요 + 레벨업까지 남은 양)

        // 장착 슬롯 이름(equip_slot_master 1~6, 표시용 상수)
        private static readonly string[] EquipSlotNames = { "무기", "보조무기", "투구", "갑옷", "장갑", "신발" };

        private const int GoldCurrencyType = 1;   // 재화 타입 1 = 골드
        private const int GoldItemCode = 1;       // item_master 골드 코드(아이콘 item_1)
        private const int ConsumableItemType = 4; // item_master.item_type 4 = 소모품(사용 시 획득량 버프)

        private const float CanvasRefWidth = 1080f;
        private const float CanvasRefHeight = 1920f;

        private Font _font;
        private RectTransform _rootRect;      // 전체 화면(Canvas) Rect
        private int _selectedCharacter;       // 현재 보고 있는 파티 캐릭터(0-based)
        private int _partyCount = 1;          // 실제 파티 캐릭터 수(세션 기준)
        private ItemIconDatabase _iconDb;     // 아이템 아이콘 조회

        private BagPager _bagPager;           // 가방 스크롤 지연 로딩 커서(창고를 열 때마다 새로 만든다)
        private ScrollRect _gridScroll;       // 가방 격자 스크롤(런타임에 _gridContent의 부모에서 해석)
        private Text _bagLoadingText;         // 페이지를 받는 동안 격자 하단에 뜨는 안내(런타임 생성)

        private CharacterPortrait _portrait;  // 초상화 렌더러(전용 카메라+RT, 런타임 생성)
        private GameObject _portraitStage;    // 초상화 렌더러가 얹히는 화면 밖 격리 오브젝트
        private int _portraitClassCode = -1;  // 현재 초상화에 렌더 중인 직업(중복 재생성 방지)

        // 초상화 렌더 설정: 전용 격리 레이어(전투 SkillCooldownUI가 쓰는 29~31과 겹치지 않게 28)와 화면 밖 위치.
        private const int PortraitLayer = 28;
        private static readonly Vector3 PortraitStageOrigin = new Vector3(500f, 500f, 0f);
        private const float PortraitOrtho = 0.65f;                       // 전신이 들어오는 직교 크기(작을수록 확대)
        private static readonly Vector2 PortraitAim = new Vector2(0f, 0.42f); // 발 기준 위(몸통 중앙)로 조준
        private static readonly Color PortraitBg = new Color(0.10f, 0.11f, 0.16f, 1f);

        /// <summary>초상화에 렌더할 직업별 캐릭터 프리팹 매핑(classCode → 프리팹).</summary>
        [System.Serializable]
        private struct ClassCharacter
        {
            public int classCode;
            public GameObject prefab;
        }

        /// <summary>정적 계층이 이미 구성돼 있으면 true(프리팹에서 로드된 경우).</summary>
        private bool AlreadyBuilt => _tooltip != null;

        private void Awake()
        {
            _rootRect = (RectTransform)transform;
            _iconDb = ItemIconDatabase.Load();
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

        /// <summary>패널이 표시될 때마다 세션 실데이터(골드·보유 아이템·장착)로 갱신한다.
        /// (UIManager가 인스턴스를 캐싱·재사용하므로 활성화 시점마다 최신 데이터를 반영해야 한다.)
        /// 가방 아이템은 코어 로드에 없으므로 <b>창고를 열 때마다 서버에서 다시 받는다</b>(세이브 기획서 5.2) —
        /// 자동 전투로 전리품이 계속 쌓이므로 로컬 캐시를 신뢰하지 않는다(서버는 이 조회를 캐시로 받는다).
        /// 가방 전량을 한 번에 받지는 않고, <b>보이는 만큼만</b> slot 커서 페이징으로 받는다
        /// (<see cref="BeginPagedBagLoad"/> — 이후는 스크롤이 빈 칸에 닿을 때마다 한 페이지씩).</summary>
        private void OnEnable()
        {
            if (!AlreadyBuilt && _tooltip == null)
            {
                return; // 아직 구성 전(Awake 이전 비정상 활성)
            }
            if (_portraitStage != null)
            {
                _portraitStage.SetActive(true); // 표시 중에만 초상화 렌더
            }
            // 이전에 열렸을 때 뜬 툴팁이 남아 있으면 지운다(아래 OnDisable의 보강 — 어떤 경로로 닫혔든 깨끗하게 시작).
            if (_tooltip != null)
            {
                _tooltip.HideImmediate();
            }
            _bagPager = InventoryLoader.BeginPaged(bagPageLimit); // 캐시를 비우고 커서를 처음으로(열 때마다 새로 받는다)
            RefreshFromSession();  // 코어 데이터(골드·장착·능력치) 즉시 표시 + 빈 격자
            ResetGridScroll();     // 첫 페이지부터 보도록 맨 위로
            LoadNextBagPage();     // 첫 페이지 요청(나머지는 스크롤이 요구할 때)

            // 다른 창(큐브 강화·분해, 거래소 판매, 우편함 수령…)이 가방을 바꾸면 그 즉시 다시 그린다.
            // 이 창은 그 창들과 <b>동시에 열려 있을 수 있어</b> OnEnable만으로는 갱신 시점을 놓친다
            // (강화한 단계가 그대로 보이거나 판 아이템이 남아 있던 원인).
            Session.InventoryChanged += OnSessionInventoryChanged;
        }

        /// <summary>
        /// 다른 창이 가방·장착을 바꿨을 때 화면만 다시 그린다.
        /// <b>변경 이벤트를 다시 올리지 않는다</b> — 이 창이 스스로 바꿨을 때 부르는
        /// <see cref="RefreshAfterInventoryChange"/>와 달리, 여기서 다시 올리면 구독자끼리 서로를 깨워 되돈다.
        /// </summary>
        private void OnSessionInventoryChanged()
        {
            if (this == null || !gameObject.activeInHierarchy)
            {
                return;
            }
            RefreshFromSession();
        }

        // ── 가방 스크롤 지연 로딩(slot 커서 페이징) ──
        //
        // 격자는 인벤토리 <b>용량</b>만큼 항상 그려지므로(빈 칸 포함) 스크롤 범위는 받은 아이템 수와 무관하다.
        // 그래서 "지금 보이는 마지막 칸 번호"가 "마지막으로 받은 칸 번호"를 넘어서면 다음 페이지를 받으면 된다.

        /// <summary>다음 가방 페이지를 요청한다(요청 중·마지막 페이지는 <see cref="BagPager"/>가 걸러낸다).</summary>
        private void LoadNextBagPage()
        {
            if (_bagPager == null || !_bagPager.HasMore || _bagPager.IsLoading)
            {
                return;
            }
            SetBagLoadingVisible(true);
            _bagPager.LoadNext(OnBagPageLoaded, OnBagPageError);
        }

        /// <summary>가방 페이지 도착: 격자를 다시 그리고, 뷰포트가 아직 못 받은 칸을 보고 있으면 이어서 더 받는다.
        /// (조회 도중 패널이 닫혔으면 아무것도 하지 않는다.)</summary>
        private void OnBagPageLoaded()
        {
            if (this == null || !gameObject.activeInHierarchy)
            {
                return;
            }
            SetBagLoadingVisible(false);
            RefreshGrid();
            TryLoadMoreForViewport(); // 한 페이지로 화면을 못 채웠으면(또는 아래로 건너뛰었으면) 계속 이어 받는다
        }

        /// <summary>가방 페이지 조회 실패: 받은 데까지만 두고 로그만 남긴다(코어 데이터 표시는 유지).</summary>
        private void OnBagPageError(NetworkError error)
        {
            SetBagLoadingVisible(false);
            Debug.LogWarning($"[Inventory] 가방 페이지 조회 실패: {error}");
        }

        /// <summary>격자 스크롤 이벤트: 아직 받지 않은 칸이 보이기 시작하면 다음 페이지를 당겨 온다.</summary>
        private void OnGridScrolled(Vector2 _)
        {
            TryLoadMoreForViewport();
        }

        /// <summary>뷰포트(+미리받기 여유 줄)가 아직 받지 않은 칸에 닿았으면 다음 페이지를 요청한다.</summary>
        private void TryLoadMoreForViewport()
        {
            if (_bagPager == null || !_bagPager.HasMore || _bagPager.IsLoading)
            {
                return;
            }
            if (LastNeededSlot() > _bagPager.LoadedSlot)
            {
                LoadNextBagPage();
            }
        }

        /// <summary>지금 채워져 있어야 하는 마지막 칸 번호 = 뷰포트 맨 아래 줄 + 미리받기 여유 줄의 마지막 칸.</summary>
        private int LastNeededSlot()
        {
            if (_gridScroll == null || _gridContent == null)
            {
                return -1;
            }
            var viewport = _gridScroll.viewport != null ? _gridScroll.viewport : (RectTransform)_gridScroll.transform;
            // 콘텐츠는 상단 고정 pivot이라 위로 스크롤한 만큼 anchoredPosition.y가 양수로 커진다.
            float scrolled = Mathf.Max(0f, _gridContent.anchoredPosition.y);
            float bottom = scrolled + viewport.rect.height;
            int lastVisibleRow = Mathf.FloorToInt(bottom / (GridCell + GridSpacing));
            int rows = lastVisibleRow + 1 + Mathf.Max(0, prefetchRows);
            return rows * Mathf.Max(1, columns) - 1;
        }

        /// <summary>격자 스크롤을 맨 위로 되돌린다(창고를 다시 열 때 첫 페이지부터 보이도록).</summary>
        private void ResetGridScroll()
        {
            if (_gridContent != null)
            {
                _gridContent.anchoredPosition = new Vector2(_gridContent.anchoredPosition.x, 0f);
            }
            if (_gridScroll != null)
            {
                _gridScroll.verticalNormalizedPosition = 1f;
            }
        }

        /// <summary>페이지를 받는 동안 격자 하단의 "불러오는 중" 안내를 토글한다.</summary>
        private void SetBagLoadingVisible(bool visible)
        {
            if (_bagLoadingText != null)
            {
                _bagLoadingText.gameObject.SetActive(visible);
            }
        }

        /// <summary>격자 스크롤 참조를 해석하고 스크롤 이벤트를 연결한다(리스너는 프리팹에 직렬화되지 않아 매 실행 재연결).</summary>
        private void WireGridScroll()
        {
            _gridScroll = _gridContent != null ? _gridContent.GetComponentInParent<ScrollRect>() : null;
            if (_gridScroll == null)
            {
                Debug.LogWarning("[Inventory] 가방 스크롤(ScrollRect)을 찾지 못해 지연 로딩이 동작하지 않는다.");
                return;
            }
            _gridScroll.onValueChanged.RemoveListener(OnGridScrolled);
            _gridScroll.onValueChanged.AddListener(OnGridScrolled);
            EnsureBagLoadingText();
        }

        /// <summary>페이지 로딩 안내 라벨을 격자 뷰 하단에 1회 만든다(런타임 전용 — 프리팹에 굽지 않는다).</summary>
        private void EnsureBagLoadingText()
        {
            if (_bagLoadingText != null || _gridScroll == null)
            {
                return;
            }
            var t = NewText("BagLoadingText", _gridScroll.transform, "아이템 불러오는 중...", 22, TextAnchor.MiddleCenter);
            t.fontStyle = FontStyle.Bold;
            t.color = new Color(1f, 0.92f, 0.6f, 0.95f);
            var rt = t.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(0f, 30f);
            rt.anchoredPosition = new Vector2(0f, 2f);
            t.gameObject.SetActive(false);
            _bagLoadingText = t;
        }

        /// <summary>패널이 숨겨지면 초상화 렌더러(카메라)를 꺼 불필요한 렌더를 막고, 툴팁을 닫는다.
        /// <b>툴팁을 닫아야 하는 이유</b>: 툴팁이 표시된 채 패널이 비활성화되면 툴팁의 activeSelf가 true로
        /// 남아, 다음에 패널을 열 때 옛 위치에 그대로 떠 있다. 이때 커서가 그 위에 없으면 pointer exit가
        /// 오지 않아 스스로 닫히지도 못한다.</summary>
        private void OnDisable()
        {
            Session.InventoryChanged -= OnSessionInventoryChanged;
            if (_portraitStage != null)
            {
                _portraitStage.SetActive(false);
            }
            if (_tooltip != null)
            {
                _tooltip.HideImmediate();
            }
            SetBagLoadingVisible(false); // 조회 중 닫혔으면 안내가 남지 않게(다음에 열 때 그대로 떠 있는 것 방지)
        }

        /// <summary>패널 파괴 시 화면 밖 초상화 스테이지(카메라·RT·캐릭터 인스턴스)를 함께 정리한다.</summary>
        private void OnDestroy()
        {
            if (_portraitStage != null)
            {
                Destroy(_portraitStage);
                _portraitStage = null;
            }
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
            // 제목 텍스트는 두지 않는다 — 배경 아트(ui_bg_2)의 상단 장식판이 제목 자리를 그린다.
            BuildGoldArea(container);  // 보유 골드 표시
            BuildEquipArea(container); // 캐릭터 네비게이션 포함, 가로 중앙 정렬
            BuildGrid(container);
            BuildGrowthButtons(container); // 스킬·룬 진입 버튼(인벤토리 아이템 아래, 패널 최하단)
            BuildTooltip();
        }

        /// <summary>성장 진입 버튼(스킬 레벨업·룬·큐브)을 패널 최하단(인벤토리 아이템 아래)에 가로 중앙으로 배치한다.
        /// 가방 격자와 패널 바닥 사이 여백에 맞춰 낮은 높이로 두고, 바닥에서 <c>y</c>만큼 띄운다(겹침 방지).</summary>
        private void BuildGrowthButtons(RectTransform container)
        {
            const float w = 210f, h = 44f, y = 62f, dx = 224f;
            var skillImg = NewImage("SkillButton", container, slotNormal);
            skillImg.color = new Color(0.24f, 0.20f, 0.34f, 0.98f);
            BottomCenter(skillImg.rectTransform, -dx, y, w, h);
            var skillLabel = NewText("SkillButtonLabel", skillImg.rectTransform, "스킬 레벨업", 24, TextAnchor.MiddleCenter);
            Stretch(skillLabel.rectTransform);
            _skillButton = skillImg.gameObject.AddComponent<Button>();

            var runeImg = NewImage("RuneButton", container, slotNormal);
            runeImg.color = new Color(0.30f, 0.22f, 0.16f, 0.98f);
            BottomCenter(runeImg.rectTransform, 0f, y, w, h);
            var runeLabel = NewText("RuneButtonLabel", runeImg.rectTransform, "룬", 24, TextAnchor.MiddleCenter);
            Stretch(runeLabel.rectTransform);
            _runeButton = runeImg.gameObject.AddComponent<Button>();

            var cubeImg = NewImage("CubeButton", container, slotNormal);
            cubeImg.color = new Color(0.42f, 0.28f, 0.16f, 0.98f);
            BottomCenter(cubeImg.rectTransform, dx, y, w, h);
            var cubeLabel = NewText("CubeButtonLabel", cubeImg.rectTransform, "큐브", 24, TextAnchor.MiddleCenter);
            Stretch(cubeLabel.rectTransform);
            _cubeButton = cubeImg.gameObject.AddComponent<Button>();
        }

        // 보유 골드 블록의 표시 배율(1이면 원래 크기). 자식 좌표를 다시 잡지 않고 블록째로 줄인다.
        private const float GoldAreaScale = 0.7f;
        // 패널 좌상단 기준 골드 블록 위치. 플레이 모드에서 직접 옮겨 확정한 값(장비 영역 오른쪽 아래).
        private static readonly Vector2 GoldAreaPos = new Vector2(485f, -596f);

        /// <summary>보유 골드 영역(골드 아이콘 + 수량)을 구성한다. 아이콘/수량은 런타임에 세션에서 채운다.
        /// 과하게 커 보이지 않도록 블록 전체를 0.7배로 축소해 얹는다(자식 크기는 그대로 둔다).</summary>
        private void BuildGoldArea(RectTransform container)
        {
            var area = NewRect("GoldArea", container);
            area.anchorMin = area.anchorMax = new Vector2(0f, 1f);
            area.pivot = new Vector2(0f, 1f);
            area.anchoredPosition = GoldAreaPos;
            area.sizeDelta = new Vector2(280f, 60f);
            area.localScale = new Vector3(GoldAreaScale, GoldAreaScale, GoldAreaScale);

            var bg = NewImage("GoldBg", area, null);
            bg.color = new Color(0f, 0f, 0f, 0.35f);
            Stretch(bg.rectTransform);

            var icon = NewImage("GoldIcon", area, null); // 스프라이트는 런타임(RefreshGold)에서 item_1로 배정
            icon.raycastTarget = false;
            icon.preserveAspect = true;
            TopLeft(icon.rectTransform, 8f, 6f, 48f, 48f);
            _goldIcon = icon;

            _goldText = NewText("GoldText", area, "0", 32, TextAnchor.MiddleLeft);
            TopLeft(_goldText.rectTransform, 66f, 8f, 200f, 44f);
        }

        /// <summary>런타임 배선: 버튼 리스너 등록 + 선택 캐릭터 표시 갱신.</summary>
        private void WireRuntime()
        {
            // 창을 끌어 옮길 수 있게 한다(배경의 빈 곳을 잡고 드래그 — 아이템 칸·스크롤은 종전대로 동작).
            // 마지막으로 둔 자리는 기억했다가 다시 열 때 그 자리에 띄운다.
            PanelDragMove.Attach(transform.Find("PanelRoot") as RectTransform, "Inventory");

            if (_prevButton != null)
            {
                _prevButton.onClick.AddListener(OnPrevCharacter);
            }
            if (_nextButton != null)
            {
                _nextButton.onClick.AddListener(OnNextCharacter);
            }
            if (_skillButton != null)
            {
                _skillButton.onClick.AddListener(OnOpenSkillPanel);
                // 스킬 레벨업 버튼 우측 상단 레드닷: 잔여 스킬 포인트가 있으면 표시(런타임 부착 — 프리팹에 baked 안 됨).
                RedDot.AttachTopRight((RectTransform)_skillButton.transform).Bind(RedDotConditions.HasUnspentSkillPoints);
            }
            if (_runeButton != null)
            {
                _runeButton.onClick.AddListener(OnOpenRunePanel);
            }
            if (_cubeButton != null)
            {
                _cubeButton.onClick.AddListener(OnOpenCubePanel);
            }
            if (_dimButton != null)
            {
                _dimButton.onClick.AddListener(OnDimClick);
            }
            if (_expandButton != null)
            {
                _expandButton.onClick.AddListener(OnExpandInventory);
            }
            WireGridScroll();      // 가방 스크롤 지연 로딩 트리거
            EnsurePortraitStage(); // 초상화 렌더러(전용 카메라+RT) 생성
        }

        // ── 세션 실데이터 연동 ──

        /// <summary>세션 세이브의 실데이터로 골드·파티·보유 아이템·장착을 모두 갱신한다.</summary>
        private void RefreshFromSession()
        {
            MasterDataManager.EnsureLoaded();
            if (_iconDb == null)
            {
                _iconDb = ItemIconDatabase.Load();
            }
            RefreshGold();
            RefreshCharacter(); // 파티 수/초상 라벨/장착 슬롯
            RefreshGrid();      // 가방 아이템
        }

        /// <summary>보유 골드량과 골드 아이콘(item_1)을 세션 재화에서 갱신한다.</summary>
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
            if (_goldIcon != null)
            {
                var sp = _iconDb != null ? _iconDb.Get(GoldItemCode) : null;
                if (sp != null)
                {
                    _goldIcon.sprite = sp;
                    _goldIcon.color = Color.white;
                }
            }
        }

        /// <summary>세션 가방 캐시(비장착 아이템)를 가방 격자에 채운다. 용량만큼 슬롯을 확보한다.
        /// 가방은 코어 로드에 없고 페이징으로 받는 데이터라, 아직 받지 않은 칸은 <b>빈 칸으로 그린다</b>
        /// (조회는 <see cref="LoadNextBagPage"/>·<see cref="ReloadBagAndRefresh"/>가 담당).
        /// 격자를 용량 전체만큼 그리는 덕에 스크롤 범위가 받은 개수와 무관해져, 스크롤 위치로 다음 페이지 시점을
        /// 판정할 수 있다(<see cref="LastNeededSlot"/>).</summary>
        private void RefreshGrid()
        {
            // 기존 표시 아이템 제거(재오픈 대비).
            foreach (var slot in _gridSlots)
            {
                if (slot != null && slot.Item != null)
                {
                    Destroy(slot.Item.gameObject);
                    slot.ClearItem();
                }
            }

            var player = Session.GameData != null ? Session.GameData.player : null;
            int capacity = player != null ? player.inventoryCapacity : initialItemSlots;
            capacity = Mathf.Clamp(capacity, initialItemSlots, 200);
            EnsureGridSlots(capacity);

            var bag = Session.Bag;
            if (bag == null)
            {
                return;
            }
            foreach (var item in bag)
            {
                if (item == null)
                {
                    continue;
                }
                int idx = item.slot;
                if (idx < 0 || idx >= _gridSlots.Count)
                {
                    continue;
                }
                _gridSlots[idx].SetItem(CreateItemView(item));
            }
        }

        /// <summary>격자 슬롯 수가 count 이상이 되도록 확보한다(확장 버튼은 항상 마지막).</summary>
        private void EnsureGridSlots(int count)
        {
            while (_gridSlots.Count < count)
            {
                _gridSlots.Add(CreateGridSlot(_gridSlots.Count, _gridContent));
            }
            if (_expandButton != null)
            {
                _expandButton.transform.SetAsLastSibling();
            }
        }

        /// <summary>인벤토리 아이템 DTO로 아이템 뷰를 생성한다(아이콘·툴팁 데이터 포함).</summary>
        private InventoryItemView CreateItemView(InventoryItemDto item)
        {
            var go = NewRect($"Item_{item.itemId}", transform);
            var view = go.gameObject.AddComponent<InventoryItemView>();
            view.Setup(BuildDisplay(item), _font, _itemSlotPrefab);
            return view;
        }

        /// <summary>인벤토리 확장 버튼: 마스터(inventory_expand_master)에서 다음 칸 비용을 조회해 확인 모달로 안내한다.
        /// 확인 시 서버에 확장을 요청하고, 결과(소모 골드·성공/실패)를 다시 모달로 안내한다.</summary>
        private void OnExpandInventory()
        {
            if (NetworkManager.Instance == null || !Session.IsLoggedIn)
            {
                Debug.LogWarning("[Inventory] 확장 요청 불가(네트워크/세션 없음).");
                return;
            }

            MasterDataManager.EnsureLoaded();
            var db = MasterDataManager.Db;
            var player = Session.GameData != null ? Session.GameData.player : null;
            int capacity = player != null ? player.inventoryCapacity : 0;
            long cost = db != null ? db.NextExpandCost(capacity) : -1L;

            if (cost < 0)
            {
                if (ModalManager.Instance != null)
                {
                    ModalManager.Instance.ShowConfirm("인벤토리 확장", "이미 최대 용량입니다.");
                }
                return;
            }

            if (ModalManager.Instance != null)
            {
                ModalManager.Instance.ShowConfirmCancel(
                    "인벤토리 확장",
                    $"인벤토리를 1칸 확장합니다.\n소모 골드: {GoldFormat.Highlight(cost)}\n확장하시겠습니까?",
                    DoExpandRequest);
            }
            else
            {
                DoExpandRequest();
            }
        }

        /// <summary>실제 확장 요청(확인 모달의 '확인' 콜백). 서버가 최종 비용 차감·검증한다(서버 권위).
        /// (POST /api/game/inventory/expand, 골드 소모 1칸 확장.)</summary>
        private void DoExpandRequest()
        {
            if (NetworkManager.Instance == null || !Session.IsLoggedIn)
            {
                return;
            }
            var req = new AuthRequest { userId = Session.UserId, token = Session.Token };
            Debug.Log("[Inventory] 인벤토리 확장 요청");
            NetworkManager.Instance.PostToGame<ExpandResponse>("/api/game/inventory/expand", req, OnExpandSuccess, OnExpandError);
        }

        /// <summary>확장 성공: 응답의 용량·잔액만 캐시에 반영해 UI를 갱신하고(재조회 없음 — 가방 아이템은 그대로다),
        /// 소모 골드·잔액·확장 후 용량을 공용 모달로 안내한다.</summary>
        private void OnExpandSuccess(ExpandResponse resp)
        {
            // 확장 성공음 + 골드 차감음(사운드 정의서 §6 — 강화 계열과 같은 성공음을 쓴다).
            SoundManager.Sfx(SoundId.UpgradeSuccess);
            SoundManager.Sfx(SoundId.GoldSpend);

            long cost = 0, balance = 0;
            int capacity = 0;
            if (resp != null && resp.data != null)
            {
                if (resp.data.cost != null) cost = resp.data.cost.amount;
                if (resp.data.balance != null && resp.data.balance.Count > 0) balance = resp.data.balance[0].amount;
                capacity = resp.data.inventoryCapacity;
                Session.ApplyInventoryCapacity(capacity);
                Session.ApplyBalance(resp.data.balance);
            }
            RefreshAfterInventoryChange(); // 용량/골드/격자 최신화

            if (ModalManager.Instance != null)
            {
                ModalManager.Instance.ShowConfirm(
                    "인벤토리 확장 완료",
                    $"골드 {GoldFormat.Highlight(cost)} 소모\n남은 골드: {GoldFormat.Highlight(balance)}\n확장 후 용량: {capacity}칸");
            }
        }

        /// <summary>확장 실패: 사유(골드 부족·최대 용량 등)를 공용 모달로 안내한다.</summary>
        private void OnExpandError(NetworkError error)
        {
            Debug.LogWarning($"[Inventory] 확장 실패: {error}");
            if (ModalManager.Instance != null)
            {
                ModalManager.Instance.ShowConfirm("인벤토리 확장 실패", ErrorMessages.ToKorean(error));
            }
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
            rt.sizeDelta = new Vector2(920f, 1640f);
            // 화면 중앙이 아니라 전투 화면 오른쪽 옆에 일정 간격(GameViewLayout.PanelGap)을 두고 붙인다 — 전투를 가리지 않는다.
            SidePanel.Attach(rt, SidePanel.Side.Right);
            return rt;
        }

        // 장비 영역 블록의 내부 폭(능력치 패널 + 초상 + 6부위 슬롯을 포함). 이 블록을 패널 가로 중앙에 둔다.
        private const float StatPanelWidth = 220f;
        private const float PortraitWidth = 220f;
        private const float PortraitX = 232f;   // 능력치 패널(220) + 간격(12)
        private const float EquipSlotsX = 466f; // 초상(PortraitX+220=452) + 간격(14)
        private const float EquipBlockWidth = 682f; // EquipSlotsX + (2*100 + 16)

        /// <summary>장비 영역(캐릭터 네비 + 능력치 패널 + 초상 + 6부위 슬롯)을 패널 가로 중앙 컨테이너에 구성한다.</summary>
        private void BuildEquipArea(RectTransform container)
        {
            // 가로 중앙 정렬 컨테이너(패널 폭과 무관하게 중앙 고정). 자식은 이 블록의 좌상단 기준으로 배치.
            var area = NewRect("EquipArea", container);
            area.anchorMin = area.anchorMax = new Vector2(0.5f, 1f);
            area.pivot = new Vector2(0.5f, 1f);
            area.sizeDelta = new Vector2(EquipBlockWidth, 476f);
            area.anchoredPosition = new Vector2(0f, -104f);

            // 캐릭터 전환 네비게이션 (◀ 인디케이터 ▶). 좌표는 플레이 모드에서 직접 옮겨 확정한 값 —
            // 화살표를 블록 양 끝이 아니라 인디케이터 글자 바로 옆으로 좁혀 붙이고, 줄 전체를 아래로 내렸다.
            _prevButton = BuildNavButton(area, "PrevCharButton", "<", NavPrevX, NavRowY, true, out _prevPunch);

            _charIndicatorText = NewText("CharIndicator", area, "", 28, TextAnchor.MiddleCenter);
            TopLeft(_charIndicatorText.rectTransform, 70f, NavIndicatorY, EquipBlockWidth - 140f, 64f);

            _nextButton = BuildNavButton(area, "NextCharButton", ">", NavNextX, NavRowY, false, out _nextPunch);

            // '장비' 라벨은 두지 않는다 — 배경 아트(ui_bg_2)의 장식과 겹쳐 보여 제거했다(플레이 모드에서 확인).

            // 능력치 패널(초상화 좌측): 장비 포함 현재 캐릭터 능력치.
            BuildStatPanel(area);

            // 캐릭터 초상 슬롯(능력치 패널 우측)
            var portrait = NewImage("PortraitSlot", area, slotPortrait);
            TopLeft(portrait.rectTransform, PortraitX, 144f, PortraitWidth, 300f);

            // 캐릭터 프리팹 렌더 표시(초상 프레임 안쪽). 텍스처/표시는 런타임에 CharacterPortrait가 배정.
            var render = NewRawImage("PortraitRender", portrait.rectTransform);
            var rrt = render.rectTransform;
            rrt.anchorMin = Vector2.zero;
            rrt.anchorMax = Vector2.one;
            rrt.pivot = new Vector2(0.5f, 0.5f);
            rrt.offsetMin = new Vector2(14f, 14f);
            rrt.offsetMax = new Vector2(-14f, -14f);
            render.color = new Color(1f, 1f, 1f, 0f); // 텍스처 배정 전에는 투명
            render.raycastTarget = false;
            _portraitImage = render;

            // 초상화 하단의 갈색 밑줄 아래 밴드에 이름표를 배치(프레임 바닥 기준으로 올림).
            _portraitLabel = NewText("PortraitLabel", portrait.rectTransform, "캐릭터", 22, TextAnchor.MiddleCenter);
            var plrt = _portraitLabel.rectTransform;
            plrt.anchorMin = new Vector2(0f, 0f);
            plrt.anchorMax = new Vector2(1f, 0f);
            plrt.pivot = new Vector2(0.5f, 0f);
            plrt.sizeDelta = new Vector2(0f, 34f);
            plrt.anchoredPosition = new Vector2(0f, 10f);

            // 6부위 장착 슬롯 (2열 x 3행) — 초상화보다 작은 크기
            const float startX = EquipSlotsX;
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

            // 초상화·장비 슬롯 아래: 현재 캐릭터 경험치 진행 막대(가로 막대그래프)
            BuildExpBar(area);
        }

        // 경험치 막대 규격과 프레임 아트 여백.
        // 프레임(체력바.png)은 768×144 · 사방 테두리 24px(12배로 그린 픽셀아트의 2픽셀)인데 <b>9-slice 테두리 값이
        // 없다</b>(spriteBorder 0). 임포트 설정은 건드리지 않는 규칙이라 그대로 늘려 쓰고, 채움이 테두리를 덮지 않도록
        // 축 배율대로 계산한 여백을 준다 — 가로 24 × (220/768) ≈ 7, 세로 24 × (24/144) = 4.
        private const float ExpBarHeight = 24f;
        private const float ExpFrameInsetX = 7f;
        private const float ExpFrameInsetY = 4f;

        /// <summary>초상화 아래 캐릭터 경험치 막대(가로 진행바 + 현재/필요·남은 경험치 텍스트)를 구성한다.
        /// 값은 런타임 <see cref="RefreshExp"/>에서 세션·마스터 데이터로 채운다.</summary>
        private void BuildExpBar(RectTransform area)
        {
            // **초상화 바로 아래**에 초상과 같은 x·폭으로 둔다(장비 블록 가로 중앙이 아니라 초상 기준).
            // 폭은 400 → 250 → 220으로 줄여 왔다.
            var container = NewRect("ExpBar", area);
            TopLeft(container, PortraitX, 448f, PortraitWidth, ExpBarHeight);

            // 배경 = 전투 HP바와 같은 프레임 아트(체력바.png). 아트가 없으면 어두운 사각형으로 폴백한다.
            var bg = NewImage("ExpBarBg", container, expBarFrame);
            if (expBarFrame == null)
            {
                bg.color = new Color(0f, 0f, 0f, 0.55f);
            }
            Stretch(bg.rectTransform);

            // 프레임 테두리 <b>안쪽</b> 영역. 채움을 여기 넣어 테두리를 덮지 않게 한다 — 여백을 채움 자신에게
            // 주면 진행률이 낮을 때 rect 폭이 음수가 되어(= 여백×2보다 좁아짐) 막대가 반대로 뒤집혀 그려진다.
            var inner = NewRect("ExpBarInner", bg.rectTransform);
            Stretch(inner);
            if (expBarFrame != null)
            {
                inner.offsetMin = new Vector2(ExpFrameInsetX, ExpFrameInsetY);
                inner.offsetMax = new Vector2(-ExpFrameInsetX, -ExpFrameInsetY);
            }

            // 채움 막대: 좌측 고정, 폭은 런타임에 anchorMax.x = 진행률로 조절(0이면 폭 0).
            var fill = NewImage("ExpBarFill", inner, null);
            fill.color = new Color(0.30f, 0.80f, 0.55f, 1f); // 경험치(초록)
            fill.raycastTarget = false;
            var frt = fill.rectTransform;
            frt.anchorMin = new Vector2(0f, 0f);
            frt.anchorMax = new Vector2(0f, 1f); // 초기 폭 0(런타임에 진행률로 설정)
            frt.pivot = new Vector2(0f, 0.5f);
            frt.offsetMin = Vector2.zero;
            frt.offsetMax = Vector2.zero;
            _expFill = frt;

            // 막대 위 텍스트: 현재/필요 + 레벨업까지 남은 경험치.
            _expText = NewText("ExpText", container, "", 16, TextAnchor.MiddleCenter);
            _expText.fontStyle = FontStyle.Bold;
            Stretch(_expText.rectTransform);
        }

        private const float NavButtonSize = 64f;      // 캐릭터 전환 화살표 버튼 한 변
        private const float NavArrowPadding = 6f;     // 버튼 안쪽에서 화살표를 줄이는 여백(클릭 영역은 그대로)
        // 캐릭터 전환 줄의 좌표(장비 블록 좌상단 기준, 아래로 +). 플레이 모드에서 확정한 값이다.
        private const float NavRowY = 80f;            // 화살표 버튼 줄의 y
        private const float NavIndicatorY = 83f;      // 인디케이터 글자의 y(버튼보다 3 아래 — 시각 정렬)
        private const float NavPrevX = 168f;          // 이전(◀) 버튼 x
        private const float NavNextX = 452f;          // 다음(▶) 버튼 x

        /// <summary>
        /// 캐릭터 전환 화살표 버튼 하나(이전/다음). 구조는 <b>투명한 루트(클릭 영역) + 화살표 자식</b>이다.
        /// <list type="bullet">
        /// <item>아트(<c>navArrow</c>)는 <b>오른쪽 방향</b> 하나뿐이라 이전 버튼은 화살표 자식만
        ///   <c>localScale.x = -1</c>로 좌우 반전해 쓴다. 반전을 <b>루트가 아니라 자식</b>에 거는 이유는
        ///   루트의 pivot이 좌상단(0, 1)이어서 루트를 반전하면 버튼이 앵커 왼쪽으로 밀려나기 때문이다.</item>
        /// <item>클릭 피드백(<see cref="ButtonPunchScale"/> — 잠깐 커졌다 원래대로)은 화살표 자식에 붙인다.
        ///   자식은 pivot이 가운데(0.5, 0.5)라 <b>가운데 기준으로</b> 커지고, punch가 기준 스케일에 배율만
        ///   곱하므로 반전(-1)도 그대로 유지된다.</item>
        /// <item>아트가 없으면 기존처럼 슬롯 배경 + 꺽쇠 텍스트로 폴백해 버튼이 사라지지 않게 한다.</item>
        /// </list>
        /// </summary>
        private Button BuildNavButton(RectTransform area, string name, string fallbackLabel, float x, float y,
            bool mirrored, out ButtonPunchScale punch)
        {
            bool hasArt = navArrow != null;

            // 루트는 클릭 영역만 담당한다(아트가 있으면 완전 투명 — 알파 0이어도 레이캐스트는 받는다).
            var root = NewImage(name, area, hasArt ? null : slotNormal);
            if (hasArt)
            {
                root.color = new Color(1f, 1f, 1f, 0f);
            }
            TopLeft(root.rectTransform, x, y, NavButtonSize, NavButtonSize);
            var button = root.gameObject.AddComponent<Button>();

            if (!hasArt)
            {
                var label = NewText($"{name}Label", root.rectTransform, fallbackLabel, 36, TextAnchor.MiddleCenter);
                Stretch(label.rectTransform);
                punch = label.gameObject.AddComponent<ButtonPunchScale>();
                return button;
            }

            var arrow = NewImage("Arrow", root.rectTransform, navArrow);
            arrow.preserveAspect = true;
            arrow.raycastTarget = false; // 클릭은 루트가 받는다(반전·확대 중에도 판정이 흔들리지 않게)
            var art = arrow.rectTransform;
            Stretch(art);
            art.offsetMin = new Vector2(NavArrowPadding, NavArrowPadding);
            art.offsetMax = new Vector2(-NavArrowPadding, -NavArrowPadding);
            art.localScale = new Vector3(mirrored ? -1f : 1f, 1f, 1f);
            punch = arrow.gameObject.AddComponent<ButtonPunchScale>();
            return button;
        }

        // 능력치 패널의 두 열. 능력치명은 왼쪽 정렬, 값은 <b>오른쪽 정렬</b>로 두어 자릿수가 달라도 세로 줄이 맞는다.
        // 두 Text는 같은 폰트 크기·줄 간격·같은 줄 수를 쓰므로 행이 어긋나지 않는다(값이 없는 능력치는 양쪽에서 함께 빠진다).
        private const int StatFontSize = 17;
        private const float StatLineSpacing = 1.4f;
        private const float StatPanelPadding = 25f;   // 테두리(초상 슬롯 아트) 안쪽 여백 = 이름 열 왼쪽
        private const float StatNameWidth = 64f;
        private const float StatColumnGap = 6f;
        private const float StatValueWidth = 98f;     // 값 열 폭(오른쪽 여백 27을 남긴다)
        private const float StatTitleInset = 15f;     // 제목 좌우 여백
        private const float StatTitleTop = 35f;
        private const float StatRowsTop = 77f;        // 제목 아래 = 첫 행 위쪽
        private const float StatRowsHeight = 240f;

        /// <summary>초상화 좌측 능력치 패널(제목 + 능력치명 열 + 값 열). 값은 런타임에 RefreshStatPanel로 채운다.
        /// 배경은 초상·장비 칸과 같은 <c>ui_slot_portrait</c> 아트를 써서 <b>같은 톤의 테두리</b>를 얻는다
        /// (바로 옆 초상 슬롯이 220×300, 이 패널이 190×300이라 테두리 두께도 비슷하게 보인다).</summary>
        private void BuildStatPanel(RectTransform area)
        {
            var bg = NewImage("StatPanel", area, slotPortrait);
            if (slotPortrait == null)
            {
                bg.color = new Color(0.09f, 0.11f, 0.18f, 0.9f); // 아트 미배선 폴백
            }
            TopLeft(bg.rectTransform, 0f, 144f, StatPanelWidth, 300f);

            var title = NewText("StatTitle", bg.rectTransform, "능력치", 22, TextAnchor.UpperCenter);
            title.fontStyle = FontStyle.Bold;
            TopLeft(title.rectTransform, StatTitleInset, StatTitleTop,
                StatPanelWidth - StatTitleInset * 2f, 30f);

            _statNameText = NewText("StatNames", bg.rectTransform, "", StatFontSize, TextAnchor.UpperLeft);
            _statNameText.lineSpacing = StatLineSpacing;
            TopLeft(_statNameText.rectTransform, StatPanelPadding, StatRowsTop, StatNameWidth, StatRowsHeight);

            // 값 열은 오른쪽 정렬한다 — 긴 값(체력 등)은 짧은 이름 쪽 여백으로 넘어가 겹치지 않는다.
            float valueLeft = StatPanelPadding + StatNameWidth + StatColumnGap;
            _statValueText = NewText("StatValues", bg.rectTransform, "", StatFontSize, TextAnchor.UpperRight);
            _statValueText.lineSpacing = StatLineSpacing;
            TopLeft(_statValueText.rectTransform, valueLeft, StatRowsTop, StatValueWidth, StatRowsHeight);
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

        /// <summary>세션의 파티 캐릭터 목록(없으면 null).</summary>
        private List<CharacterDto> Characters =>
            Session.GameData != null ? Session.GameData.characters : null;

        /// <summary>이전 파티 캐릭터로 전환(순환).</summary>
        private void OnPrevCharacter()
        {
            if (_partyCount <= 1)
            {
                return; // 전환할 캐릭터가 없으면 연출도 하지 않는다(바뀐 것처럼 보이지 않게)
            }
            if (_prevPunch != null) _prevPunch.Play();
            _selectedCharacter = (_selectedCharacter - 1 + _partyCount) % _partyCount;
            RefreshCharacter();
            RefreshGrid(); // 선택 캐릭터 클래스 변경 → 다른 클래스 장비 X 표시 갱신
        }

        /// <summary>다음 파티 캐릭터로 전환(순환).</summary>
        private void OnNextCharacter()
        {
            if (_partyCount <= 1)
            {
                return; // 전환할 캐릭터가 없으면 연출도 하지 않는다
            }
            if (_nextPunch != null) _nextPunch.Play();
            _selectedCharacter = (_selectedCharacter + 1) % _partyCount;
            RefreshCharacter();
            RefreshGrid(); // 선택 캐릭터 클래스 변경 → 다른 클래스 장비 X 표시 갱신
        }

        /// <summary>스킬 레벨업 패널을 연다(UIManager 위임). 없으면 무시.</summary>
        private void OnOpenSkillPanel()
        {
            if (UIManager.Instance != null)
            {
                UIManager.Instance.Show(UIManager.PanelType.Skill);
            }
            else
            {
                Debug.LogWarning("[Inventory] UIManager 인스턴스를 찾을 수 없어 스킬 패널을 열 수 없습니다.");
            }
        }

        /// <summary>룬 패널을 연다(UIManager 위임). 없으면 무시.</summary>
        private void OnOpenRunePanel()
        {
            if (UIManager.Instance != null)
            {
                UIManager.Instance.Show(UIManager.PanelType.Rune);
            }
            else
            {
                Debug.LogWarning("[Inventory] UIManager 인스턴스를 찾을 수 없어 룬 패널을 열 수 없습니다.");
            }
        }

        /// <summary>큐브 패널을 연다(UIManager 위임). 없으면 무시.</summary>
        private void OnOpenCubePanel()
        {
            if (UIManager.Instance != null)
            {
                UIManager.Instance.Show(UIManager.PanelType.Cube);
            }
            else
            {
                Debug.LogWarning("[Inventory] UIManager 인스턴스를 찾을 수 없어 큐브 패널을 열 수 없습니다.");
            }
        }

        /// <summary>선택된 캐릭터의 인디케이터·초상 라벨·장착 슬롯을 세션 실데이터로 갱신한다.</summary>
        private void RefreshCharacter()
        {
            var chars = Characters;
            _partyCount = chars != null && chars.Count > 0 ? chars.Count : 1;
            _selectedCharacter = Mathf.Clamp(_selectedCharacter, 0, _partyCount - 1);

            if (_charIndicatorText != null)
            {
                _charIndicatorText.text = $"캐릭터 {_selectedCharacter + 1} / {_partyCount}";
            }
            if (_portraitLabel != null)
            {
                _portraitLabel.text = CurrentCharacterLabel(chars);
            }
            UpdatePortrait(chars);
            RefreshEquip(chars);
            RefreshStatPanel(chars);
            RefreshExp(chars);
        }

        /// <summary>선택 캐릭터의 경험치 진행도를 막대와 텍스트에 반영한다.
        /// 저장된 exp는 현재 레벨 내 누적치이므로, 막대 진행률 = exp / requiredExp,
        /// 레벨업까지 남은 경험치 = requiredExp - exp. 최대 레벨(requiredExp=0)은 MAX로 표시한다.</summary>
        private void RefreshExp(List<CharacterDto> chars)
        {
            if (_expFill == null || _expText == null)
            {
                return;
            }
            if (chars == null || _selectedCharacter >= chars.Count)
            {
                _expFill.anchorMax = new Vector2(0f, 1f);
                _expText.text = string.Empty;
                return;
            }

            var c = chars[_selectedCharacter];
            long required = 0;
            var db = MasterDataManager.Db;
            if (db != null && db.Levels.TryGetValue(c.level, out var lm))
            {
                required = lm.requiredExp;
            }

            if (required <= 0) // 최대 레벨(요구 경험치 0) — 더 오르지 않음
            {
                _expFill.anchorMax = new Vector2(1f, 1f);
                _expText.text = "EXP  MAX";
                return;
            }

            long cur = c.exp > 0 ? c.exp : 0;
            long remain = required - cur;
            if (remain < 0)
            {
                remain = 0;
            }
            float frac = Mathf.Clamp01((float)cur / required);
            _expFill.anchorMax = new Vector2(frac, 1f);
            _expText.text = $"EXP  {cur:N0} / {required:N0}  (남은 {remain:N0})";
        }

        /// <summary>선택된 캐릭터의 직업·성별 프리팹을 초상화 렌더러에 반영한다(외형이 바뀔 때만 재생성).</summary>
        private void UpdatePortrait(List<CharacterDto> chars)
        {
            EnsurePortraitStage();
            if (_portrait == null)
            {
                return;
            }

            bool hasChar = chars != null && _selectedCharacter < chars.Count && chars[_selectedCharacter] != null;
            int classCode = hasChar ? chars[_selectedCharacter].classCode : -1;
            int gender = hasChar && chars[_selectedCharacter].gender > 0
                ? chars[_selectedCharacter].gender
                : CharacterPrefabDatabase.DefaultGender;
            int portraitKey = classCode * 10 + gender;
            if (portraitKey == _portraitClassCode)
            {
                return; // 동일 직업·성별이면 인스턴스를 재생성하지 않음
            }
            _portraitClassCode = portraitKey;

            var prefab = PrefabForClass(classCode, gender);
            _portrait.SetCharacter(prefab);
            if (_portraitImage != null)
            {
                _portraitImage.color = prefab != null ? Color.white : new Color(1f, 1f, 1f, 0f);
            }
        }

        /// <summary>초상화 렌더러(전용 카메라+RT를 얹은 화면 밖 오브젝트)를 1회 생성한다(런타임 전용).</summary>
        private void EnsurePortraitStage()
        {
            if (_portrait != null || _portraitImage == null || !Application.isPlaying)
            {
                return;
            }
            _portraitStage = new GameObject("InventoryPortraitStage");
            _portraitStage.transform.position = PortraitStageOrigin;
            _portrait = _portraitStage.AddComponent<CharacterPortrait>();
            _portrait.Initialize(_portraitImage, PortraitLayer, 440, 600, PortraitOrtho, PortraitAim, PortraitBg, PortraitStageOrigin);
        }

        /// <summary>
        /// 직업·성별에 해당하는 초상화 캐릭터 프리팹을 반환한다(없으면 null).
        /// 공용 <see cref="CharacterPrefabDatabase"/>(Resources)를 먼저 보고, 없으면 인스펙터에 배선된
        /// 직업별 프리팹 목록으로 폴백한다(성별 구분 없음).
        /// </summary>
        private GameObject PrefabForClass(int classCode, int gender)
        {
            var fromDb = CharacterPrefabDatabase.PrefabOf(classCode, gender);
            if (fromDb != null)
            {
                return fromDb;
            }
            if (_classCharacters != null)
            {
                foreach (var e in _classCharacters)
                {
                    if (e.prefab != null && e.classCode == classCode)
                    {
                        return e.prefab;
                    }
                }
            }
            return null;
        }

        // 아이템·패시브로 인한 상승분 텍스트 색(연한 파란색).
        private const string BonusColorHex = "#8FC1FF";

        /// <summary>현재 선택 캐릭터의 능력치를 능력치 패널에 표시한다.
        /// 최종값 = (클래스+레벨 기본 + 장착 장비) × 학습 패시브 배율. 아이템·패시브로 인한 상승분은 (+상승분)으로 병기한다.</summary>
        private void RefreshStatPanel(List<CharacterDto> chars)
        {
            if (_statNameText == null || _statValueText == null)
            {
                return;
            }
            if (chars == null || _selectedCharacter >= chars.Count)
            {
                _statNameText.text = string.Empty;
                _statValueText.text = string.Empty;
                return;
            }

            var c = chars[_selectedCharacter];
            Stats baseS = BaseStats(c);   // 클래스 + 레벨(고유)
            Stats eqS = EquipStats(c);    // 장착 장비 합산(가산)

            // 최종 = (기본 + 장비) × 패시브 배율 × 룬(계정 공용) 배율(statType별). 상승분 = 최종 − 기본.
            // 반올림으로 확정한다(버림 시 작은 % 상승분이 정수 표기에서 사라져 "상승 안 함"으로 보이는 문제 방지).
            long atkF = (long)System.Math.Round((baseS.atk + eqS.atk) * (double)PassiveMult(c, 1) * RuneMult(1));
            long defF = (long)System.Math.Round((baseS.def + eqS.def) * (double)PassiveMult(c, 2) * RuneMult(2));
            long hpF = (long)System.Math.Round((baseS.hp + eqS.hp) * (double)PassiveMult(c, 3) * RuneMult(3));
            float critF = (baseS.critChance + eqS.critChance) * PassiveMult(c, 4) * RuneMult(4);
            float critDF = (baseS.critDamage + eqS.critDamage) * PassiveMult(c, 5) * RuneMult(5);
            float moveF = (baseS.moveSpeed + eqS.moveSpeed) * PassiveMult(c, 6) * RuneMult(6);

            // 두 열을 같은 순서·같은 줄 수로 채운다(값이 없는 능력치는 양쪽에서 함께 빠져 행이 어긋나지 않는다).
            var names = new System.Text.StringBuilder();
            var values = new System.Text.StringBuilder();
            AppendStatRow(names, values, "공격력", LongStatValue(baseS.atk, atkF));
            AppendStatRow(names, values, "방어력", LongStatValue(baseS.def, defF));
            AppendStatRow(names, values, "체력", LongStatValue(baseS.hp, hpF));
            if (critF != 0f) AppendStatRow(names, values, "치명확률", PercentStatValue(baseS.critChance, critF));
            if (critDF != 0f) AppendStatRow(names, values, "치명피해", PercentStatValue(baseS.critDamage, critDF));
            if (moveF != 0f) AppendStatRow(names, values, "이동속도", MoveStatValue(baseS.moveSpeed, moveF));
            _statNameText.text = names.ToString().TrimEnd();
            _statValueText.text = values.ToString().TrimEnd();
        }

        /// <summary>능력치 한 행을 두 열에 함께 넣는다(이름 열 · 값 열의 줄 수를 반드시 같게 유지하기 위한 통로).</summary>
        private static void AppendStatRow(System.Text.StringBuilder names, System.Text.StringBuilder values,
            string label, string value)
        {
            names.AppendLine(label);
            values.AppendLine(value);
        }

        /// <summary>정수 스탯 값: "최종 +상승분". 상승분(장비+패시브+룬)은 연한 파란색으로 병기.
        /// 좁은 열에 들어가야 하므로 괄호 없이 <c>+N</c>으로만 적는다.</summary>
        private static string LongStatValue(long baseV, long finalV)
        {
            long bonus = finalV - baseV;
            return bonus > 0 ? $"{finalV} <color={BonusColorHex}>+{bonus}</color>" : finalV.ToString();
        }

        /// <summary>퍼센트 스탯 값(치명확률/치명피해). 값은 0~1 → % 표기.</summary>
        private static string PercentStatValue(float baseV, float finalV)
        {
            float bonus = finalV - baseV;
            string value = $"{finalV * 100f:0.#}%";
            return bonus > 0.0001f
                ? $"{value} <color={BonusColorHex}>+{bonus * 100f:0.#}%</color>"
                : value;
        }

        /// <summary>이동속도 값.</summary>
        private static string MoveStatValue(float baseV, float finalV)
        {
            float bonus = finalV - baseV;
            string value = $"{finalV:0.##}";
            return bonus > 0.001f ? $"{value} <color={BonusColorHex}>+{bonus:0.##}</color>" : value;
        }

        /// <summary>캐릭터 고유 기본 능력치(클래스 + 레벨 보너스, 장비·패시브 제외).</summary>
        private static Stats BaseStats(CharacterDto c)
        {
            var db = MasterDataManager.Db;
            var total = new Stats();
            if (db == null)
            {
                return total;
            }
            if (db.Classes.TryGetValue(c.classCode, out var cls))
            {
                total = Add(total, cls.baseStats);
            }
            if (db.Levels.TryGetValue(c.level, out var lm))
            {
                total = Add(total, lm.statBonus);
            }
            return total;
        }

        /// <summary>이 캐릭터에 장착된 장비 스탯 합산(가산분). 장착 정보는 코어 로드의 equipped가 정본이다.
        /// 각 장비의 옵션 스탯에는 그 장비의 <b>강화 단계 배율</b>(enhance_master)을 먼저 곱한다 —
        /// 서버는 단계만 확정하고 배율은 내려주지 않으므로(기획서 §5.3) 전투 계산(PlayerCombatant)과 같은 규칙을 쓴다.</summary>
        private static Stats EquipStats(CharacterDto c)
        {
            var db = MasterDataManager.Db;
            var total = new Stats();
            var equipped = Session.Equipped;
            if (db == null || equipped == null)
            {
                return total;
            }
            foreach (var item in equipped)
            {
                if (item != null && item.equippedCharacterId == c.characterId
                    && db.Items.TryGetValue(item.itemCode, out var im))
                {
                    total = Add(total, Enhanced(im.baseStats, db.EnhanceMultiplier(item.enhanceLevel)));
                }
            }
            return total;
        }

        /// <summary>장비 옵션 스탯에 강화 배율을 적용한 사본(이동속도·쿨다운은 배율 대상이 아니다 — ItemInfoText.Stats와 같은 규칙).</summary>
        private static Stats Enhanced(Stats s, float mult)
        {
            if (mult == 1f)
            {
                return s;
            }
            s.atk = (long)System.Math.Round(s.atk * (double)mult);
            s.def = (long)System.Math.Round(s.def * (double)mult);
            s.hp = (long)System.Math.Round(s.hp * (double)mult);
            s.critChance *= mult;
            s.critDamage *= mult;
            return s;
        }

        /// <summary>이 캐릭터가 습득(레벨 ≥ 1)한 패시브 스킬 중 대상 statType을 올리는 것들의 레벨별 배율 곱(전투와 동일 규칙).
        /// statType: 1 공격력 · 2 방어력 · 3 체력 · 4 치명확률 · 5 치명피해 · 6 이동속도. 없으면 1.</summary>
        private static float PassiveMult(CharacterDto c, int statType)
        {
            float mult = 1f;
            var db = MasterDataManager.Db;
            var skills = Session.GameData != null ? Session.GameData.skills : null;
            if (db == null || skills == null || c == null)
            {
                return mult;
            }
            foreach (var ps in skills)
            {
                if (ps == null || ps.characterId != c.characterId || ps.level < 1)
                {
                    continue;
                }
                if (db.Skills.TryGetValue(ps.skillCode, out var sm)
                    && sm.skillType == 2 && sm.statType == statType && sm.coefs != null)
                {
                    int lv = Mathf.Clamp(ps.level, 1, Mathf.Max(1, sm.maxLevel));
                    foreach (var coef in sm.coefs)
                    {
                        if (coef.skillLevel == lv)
                        {
                            mult *= coef.coef;
                            break;
                        }
                    }
                }
            }
            return mult;
        }

        /// <summary>계정 공용 룬(레벨 ≥ 1) 중 대상 statType을 올리는 것들의 배율 곱(전투와 동일 규칙).
        /// 룬 stat_value는 레벨당 누적 비율이며 총 보너스 = stat_value × 레벨. statType 7(재사용)은 감소, 그 외는 증가. 없으면 1.</summary>
        private static float RuneMult(int statType)
        {
            float mult = 1f;
            var db = MasterDataManager.Db;
            var runes = Session.GameData != null ? Session.GameData.runes : null;
            if (db == null || runes == null)
            {
                return mult;
            }
            foreach (var pr in runes)
            {
                if (pr == null || pr.level < 1)
                {
                    continue;
                }
                if (db.Runes.TryGetValue(pr.runeCode, out var rm) && rm.statType == statType)
                {
                    float bonus = rm.statValue * pr.level;
                    mult *= statType == 7 ? Mathf.Max(0.05f, 1f - bonus) : (1f + bonus);
                }
            }
            return mult;
        }

        /// <summary>두 Stats를 합산한다.</summary>
        private static Stats Add(Stats a, Stats b)
        {
            a.hp += b.hp;
            a.atk += b.atk;
            a.def += b.def;
            a.moveSpeed += b.moveSpeed;
            a.critChance += b.critChance;
            a.critDamage += b.critDamage;
            a.cooldown += b.cooldown;
            return a;
        }

        /// <summary>현재 선택 캐릭터의 직업명·레벨 라벨(마스터 데이터). 없으면 기본 라벨.</summary>
        private string CurrentCharacterLabel(List<CharacterDto> chars)
        {
            if (chars == null || _selectedCharacter >= chars.Count)
            {
                return "캐릭터";
            }
            var c = chars[_selectedCharacter];
            var db = MasterDataManager.Db;
            string cls = db != null && db.Classes.TryGetValue(c.classCode, out var cm) ? cm.name : $"직업 {c.classCode}";
            return $"{cls} Lv.{c.level}";
        }

        /// <summary>선택된 캐릭터의 6부위 장착 상태를 코어 로드의 장착 목록(equipped)에서 장비 슬롯에 반영한다.</summary>
        private void RefreshEquip(List<CharacterDto> chars)
        {
            CharacterDto cur = chars != null && _selectedCharacter < chars.Count ? chars[_selectedCharacter] : null;
            var equipped = Session.Equipped;

            for (int slot = 0; slot < _equipSlots.Count; slot++)
            {
                InventoryItemView.Display? display = null;
                if (cur != null && equipped != null)
                {
                    foreach (var item in equipped)
                    {
                        if (item != null
                            && item.equippedCharacterId == cur.characterId
                            && item.equippedSlot == slot + 1)
                        {
                            display = BuildDisplay(item);
                            break;
                        }
                    }
                }
                _equipSlots[slot].SetEquipped(display, _font, _itemSlotPrefab);
            }
        }

        /// <summary>가방 아이템(비장착)을 표시 정보로 변환한다. 가방 아이템은 장착 슬롯이 없다(0).</summary>
        private InventoryItemView.Display BuildDisplay(InventoryItemDto item)
            => BuildDisplay(item.itemId, item.itemCode, item.quantity, item.enhanceLevel, 0);

        /// <summary>장착 장비(equipped)를 표시 정보로 변환한다. 장비는 항상 수량 1이며 장착 슬롯(1~6)을 가진다.</summary>
        private InventoryItemView.Display BuildDisplay(EquippedItemDto item)
            => BuildDisplay(item.itemId, item.itemCode, 1L, item.enhanceLevel, item.equippedSlot);

        /// <summary>아이템 식별 정보를 마스터 데이터로 표시 정보(이름·등급·부위·요구·스탯·아이콘)로 변환한다.
        /// 가방 아이템과 장착 장비가 서로 다른 DTO라 공통 필드만 받아 한 곳에서 만든다.</summary>
        private InventoryItemView.Display BuildDisplay(long itemId, int itemCode, long quantity, int enhanceLevel,
            int equippedSlot)
        {
            var db = MasterDataManager.Db;
            ItemMaster im = null;
            if (db != null)
            {
                db.Items.TryGetValue(itemCode, out im);
            }

            // 상세 문구(등급명·종류·요구조건·설명)는 공용 헬퍼로 통일한다 — 스테이지 클리어 보상·우편함·
            // (추후) 거래소의 아이템 상세 팝업(ItemDetailPopup)과 완전히 같은 텍스트가 나오도록 한다.
            // 강화 단계를 함께 넘겨 이름 "+N"과 옵션 스탯 배율(enhance_master)이 한곳에서 정해지게 한다.
            var info = ItemInfoText.Build(itemCode, quantity, enhanceLevel);
            string name = info.name;

            // 착용 가능 판정: 장비이면서 (공용이거나 현재 캐릭터의 직업과 클래스 제한 일치) + (요구 레벨 이하)여야 한다.
            // 클래스 불일치 또는 레벨 미달 장비는 착용 불가(슬롯에 X 표시 + 장착 버튼 비활성).
            bool isEquip = im != null && im.itemType == 1;
            bool isConsumable = im != null && im.itemType == ConsumableItemType; // 소모품 → 툴팁 버튼이 '사용'
            var cur = CurrentCharacter();
            int curClass = cur != null ? cur.classCode : -1;
            int curLevel = cur != null ? cur.level : -1;
            bool classOk = im != null && (im.classReq == 0 || cur == null || im.classReq == curClass);
            bool levelOk = im != null && (im.levelReq <= 0 || cur == null || curLevel >= im.levelReq);
            bool equipLocked = isEquip && cur != null
                && ((im.classReq != 0 && im.classReq != curClass) || (im.levelReq > 0 && curLevel < im.levelReq));

            return new InventoryItemView.Display
            {
                itemCode = itemCode,   // 공용 슬롯이 이 코드로 아이콘·등급을 조회한다
                quantity = quantity,   // 2 이상이면 슬롯 우하단에 "xN"
                name = name,
                grade = info.grade,
                gradeValue = info.gradeValue,
                slotName = info.category,
                requirement = info.requirement,
                stats = ItemInfoText.Stats(im, enhanceLevel),
                description = info.description,
                iconColor = GradeColor(info.gradeValue),
                icon = _iconDb != null ? _iconDb.Get(itemCode) : null,
                itemId = itemId,
                equippedSlot = equippedSlot,
                equipSlot = im != null ? im.equipSlot : 0,  // 드래그로 장비 부위 칸에 놓을 때의 부위 판정
                isEquipment = isEquip,                     // 툴팁의 장착/해제 버튼 노출 여부(재료·재화는 숨김)
                equippable = isEquip && classOk && levelOk,
                equipLocked = equipLocked,
                usable = isConsumable && equippedSlot == 0, // 가방에 있는 소모품만 사용할 수 있다
                enhanceLevel = enhanceLevel,               // 슬롯 좌측 하단 흰 "+N" 배지(검은 외곽선)
            };
        }

        /// <summary>현재 보고 있는 파티 캐릭터(없으면 null). 착용 제한(클래스·레벨) 판정에 사용.</summary>
        private CharacterDto CurrentCharacter()
        {
            var chars = Session.GameData != null ? Session.GameData.characters : null;
            if (chars != null && _selectedCharacter >= 0 && _selectedCharacter < chars.Count)
            {
                return chars[_selectedCharacter];
            }
            return null;
        }

        /// <summary>등급(1~5)별 아이콘 폴백 색(실아이콘 없을 때만 사용). 공용 <see cref="GradeColors"/> 위임.</summary>
        private static Color GradeColor(int grade) => GradeColors.IconFallback(grade);

        /// <summary>등급(1~5)별 아이템 배경색(노말은 투명). 공용 <see cref="GradeColors"/> 위임.</summary>
        public static Color GradeBackgroundColor(int grade) => GradeColors.Background(grade);

        /// <summary>등급(1~5)별 아이템 이름 텍스트 색. 공용 <see cref="GradeColors"/> 위임.</summary>
        public static Color GradeNameColor(int grade) => GradeColors.Name(grade);

        // ── 드래그로 장착 / 해제 ──

        /// <summary>
        /// 장비 부위 칸의 장착 아이템을 <b>가방 격자에 떨어뜨렸을 때</b>의 해제 처리.
        /// 어느 가방 칸에 놓든 결과는 같다 — 서버가 아이템이 들어갈 가방 칸(bagSlot)을 정해 응답하고,
        /// <c>Session.ApplyUnequipResult</c>가 그 칸으로 캐시를 맞춘다.
        /// <para>끌던 아이콘은 호출 전에 이미 제자리로 돌아가 있으므로, 여기서는 요청만 보낸다.
        /// 성공하면 서버 응답 뒤 가방·장비 칸이 함께 다시 그려진다.</para>
        /// </summary>
        /// <param name="equipSlot">끌어낸 장비 부위 칸(<see cref="InventoryItemSlot.Index"/> 0~5 = 부위 1~6)</param>
        public void TryUnequipByDrag(InventoryItemSlot equipSlot)
        {
            if (equipSlot == null || !equipSlot.IsEquipSlot)
            {
                return;
            }
            RequestHideTooltip();
            RequestUnequip(equipSlot.Index + 1);
        }

        // ── 드래그로 장착 ──

        /// <summary>
        /// 가방 아이템을 <b>장비 부위 칸에 떨어뜨렸을 때</b>의 장착 처리.
        /// 툴팁의 '장착' 버튼과 같은 조건을 통과할 때만 서버에 요청하고, 그 밖에는 조용히 되돌린다
        /// (착용 불가 장비의 '장착' 버튼이 비활성인 것과 같은 톤 — 실수로 놓아도 방해하지 않는다).
        /// <para>드래그 시작 시 아이템은 원래 칸에서 떨어져 캔버스로 옮겨져 있으므로, 어떤 결과든
        /// <b>먼저 원래 칸으로 되돌린다</b>. 장착에 성공하면 서버 응답 뒤 격자가 다시 그려진다.</para>
        /// </summary>
        /// <param name="view">드래그한 가방 아이템</param>
        /// <param name="from">드래그를 시작한 가방 칸(되돌릴 자리)</param>
        /// <param name="equipSlot">떨어뜨린 장비 부위 칸(<see cref="InventoryItemSlot.Index"/> 0~5 = 부위 1~6)</param>
        public void TryEquipByDrag(InventoryItemView view, InventoryItemSlot from, InventoryItemSlot equipSlot)
        {
            if (from != null)
            {
                from.SetItem(view); // 성공/실패 무관하게 일단 제자리로
            }
            if (view == null || equipSlot == null)
            {
                return;
            }

            var data = view.Data;
            int part = equipSlot.Index + 1; // 부위 칸 인덱스(0~5) → equip_slot(1~6)

            if (!data.isEquipment)
            {
                Debug.Log($"[Inventory] 드래그 장착 무시 — 장비가 아님(itemCode={data.itemCode}).");
                return;
            }
            if (data.equipSlot != part)
            {
                Debug.Log($"[Inventory] 드래그 장착 무시 — 부위 불일치(아이템 {data.equipSlot} ≠ 칸 {part}).");
                return;
            }
            if (!data.equippable)
            {
                Debug.Log($"[Inventory] 드래그 장착 무시 — 착용 조건 미달(직업·레벨) itemId={data.itemId}.");
                return;
            }

            RequestHideTooltip();
            RequestEquip(data.itemId);
        }

        /// <summary>
        /// 드래그 중인 장비가 들어갈 부위 칸을 강조한다(<paramref name="on"/> false면 전체 해제).
        /// 착용 조건을 못 갖춘 장비는 강조하지 않는다 — 놓아도 장착되지 않으므로 기대를 주지 않는다.
        /// </summary>
        public void HighlightEquipTarget(InventoryItemView.Display data, bool on)
        {
            for (int i = 0; i < _equipSlots.Count; i++)
            {
                if (_equipSlots[i] == null)
                {
                    continue;
                }
                bool target = on && data.isEquipment && data.equippable && data.equipSlot == i + 1;
                _equipSlots[i].SetHighlight(target);
            }
        }

        // ── 장착 / 해제 (서버 연동) ──

        /// <summary>현재 선택 캐릭터에 지정 아이템을 장착 요청한다(성공 시 재로드·갱신).</summary>
        public void RequestEquip(long itemId)
        {
            var chars = Characters;
            if (NetworkManager.Instance == null || !Session.IsLoggedIn || chars == null || _selectedCharacter >= chars.Count)
            {
                Debug.LogWarning("[Inventory] 장착 요청 불가(네트워크/세션/캐릭터 없음).");
                return;
            }
            int characterId = chars[_selectedCharacter].characterId;
            var req = new EquipRequest
            {
                userId = Session.UserId,
                token = Session.Token,
                data = new EquipData { characterId = characterId, itemId = itemId },
            };
            Debug.Log($"[Inventory] 장착 요청 char={characterId} item={itemId}");
            NetworkManager.Instance.PostToGame<EquipResponse>("/api/game/inventory/equip", req, resp =>
            {
                // 응답만으로 캐시를 맞춘다(재조회 없음) — 장착품은 가방에서 빠지고, 스왑된 장비는 서버가 알려준 칸으로.
                SoundManager.Sfx(SoundId.ItemEquip); // 장착 성공음(사운드 정의서 §6)
                Session.ApplyEquipResult(resp != null ? resp.data : null);
                RefreshAfterInventoryChange();
            }, OnActionError);
        }

        /// <summary>현재 선택 캐릭터의 지정 슬롯 장비를 해제 요청한다(성공 시 응답으로 캐시 갱신).</summary>
        public void RequestUnequip(int slot)
        {
            var chars = Characters;
            if (NetworkManager.Instance == null || !Session.IsLoggedIn || chars == null || _selectedCharacter >= chars.Count)
            {
                Debug.LogWarning("[Inventory] 해제 요청 불가(네트워크/세션/캐릭터 없음).");
                return;
            }
            int characterId = chars[_selectedCharacter].characterId;
            var req = new UnequipRequest
            {
                userId = Session.UserId,
                token = Session.Token,
                data = new UnequipData { characterId = characterId, slot = slot },
            };
            Debug.Log($"[Inventory] 해제 요청 char={characterId} slot={slot}");
            NetworkManager.Instance.PostToGame<UnequipResponse>("/api/game/inventory/unequip", req, resp =>
            {
                // 해제한 장비는 서버가 알려준 가방 칸(bagSlot)으로 되돌린다(재조회 없음).
                SoundManager.Sfx(SoundId.ItemEquip); // 장착·해제 공용음(§6)
                Session.ApplyUnequipResult(resp != null ? resp.data : null);
                RefreshAfterInventoryChange();
            }, OnActionError);
        }

        // ── 소모품 사용 (서버 연동) ──

        /// <summary>가방의 소모품 1개를 사용 요청한다(<c>POST /api/game/consumable/use</c>).
        /// 배율·지속시간은 서버가 마스터 데이터에서 확정하므로 클라이언트는 아이템 행(itemId)만 보낸다.
        /// 성공 시 갱신된 활성 버프를 버프 캐시에 반영하고(우상단 버프 아이콘 즉시 갱신), 가방·코어를 재로드한다.</summary>
        public void RequestUseConsumable(long itemId)
        {
            if (NetworkManager.Instance == null || !Session.IsLoggedIn)
            {
                Debug.LogWarning("[Inventory] 소모품 사용 요청 불가(네트워크/세션 없음).");
                return;
            }
            var req = new ConsumableUseRequest
            {
                userId = Session.UserId,
                token = Session.Token,
                data = new ConsumableUseData { itemId = itemId },
            };
            Debug.Log($"[Inventory] 소모품 사용 요청 item={itemId}");
            NetworkManager.Instance.PostToGame<ConsumableUseResponse>("/api/game/consumable/use", req,
                OnUseConsumableSuccess, OnUseConsumableError);
        }

        /// <summary>소모품 사용 성공: 활성 버프 캐시를 응답으로 교체하고 부여된 버프 효과·만료를 모달로 안내한다.
        /// 가방(수량 차감·행 삭제)은 응답의 변경분(inventoryDelta)으로 반영한다 — 재조회하지 않는다.</summary>
        private void OnUseConsumableSuccess(ConsumableUseResponse resp)
        {
            var data = resp != null ? resp.data : null;
            if (data != null)
            {
                BuffManager.Apply(data.activeBuffs); // 우상단 버프 아이콘 즉시 갱신
                BuffManager.Refresh();               // 잔여 시간 기준점(serverTime) 보정
                Session.ApplyInventoryDelta(data.inventoryDelta);
            }
            RefreshAfterInventoryChange();

            var buff = data != null ? data.buff : null;
            if (buff != null && ModalManager.Instance != null)
            {
                string name = BuffManager.DisplayName(buff.buffType);
                string bonus = BuffManager.BonusText(buff.buffValue);
                string remain = BuffManager.RemainText(BuffManager.RemainingSeconds(buff));
                ModalManager.Instance.ShowConfirm(
                    "소모품 사용",
                    $"{name} {bonus} 효과가 적용되었습니다.\n남은 시간: {remain} (만료 {BuffManager.ExpireTimeText(buff.expiresAt)})");
            }
        }

        /// <summary>소모품 사용 실패: 사유(소모품 아님·수량 부족·누적 상한 초과 등)를 모달로 안내한다.
        /// 이미 사라진 행(<see cref="ErrorCode.ItemNotFound"/>)이면 가방 캐시가 낡은 것이므로 목록을 새로 고친다.</summary>
        private void OnUseConsumableError(NetworkError error)
        {
            Debug.LogWarning($"[Inventory] 소모품 사용 실패: {error}");
            if (ModalManager.Instance != null)
            {
                ModalManager.Instance.ShowConfirm("소모품 사용 실패", ErrorMessages.ToKorean(error));
            }
            if (error != null && error.ErrorCode == ErrorCode.ItemNotFound)
            {
                ReloadBagAndRefresh();
            }
        }

        /// <summary>액션 응답을 캐시에 반영한 뒤 화면·전투 스탯을 갱신한다(네트워크 재조회 없음).
        /// 서버가 변경분을 응답에 담아 주므로(§5.0 규약) 액션마다 코어·가방을 다시 받을 필요가 없다.</summary>
        private void RefreshAfterInventoryChange()
        {
            RequestHideTooltip();
            RefreshFromSession();            // 인벤토리 UI 갱신
            Session.RaiseInventoryChanged(); // 전투 스탯 재계산 트리거
        }

        /// <summary>가방 캐시가 서버와 어긋났을 때(<see cref="ErrorCode.ItemNotFound"/>·이동 저장 실패)만
        /// 가방을 다시 받아 화면을 맞춘다. 코어 스냅샷은 이 경우에도 다시 받지 않는다.
        /// 이 경로는 <b>전량</b>을 다시 받아 캐시를 통째로 교체하므로(어긋난 원인이 어느 페이지인지 모른다),
        /// 낡은 커서로 이어 받지 않도록 스크롤 페이저를 완료 처리한다.</summary>
        private void ReloadBagAndRefresh()
        {
            InventoryLoader.ReloadBag(() =>
            {
                if (_bagPager != null)
                {
                    _bagPager.MarkComplete();
                }
                RefreshAfterInventoryChange();
            }, OnBagLoadError);
        }

        /// <summary>가방 전량 재조회 실패: 화면은 현재 캐시 그대로 두고 로그만 남긴다.</summary>
        private void OnBagLoadError(NetworkError error)
        {
            Debug.LogWarning($"[Inventory] 가방 재조회 실패: {error}");
        }

        /// <summary>장착/해제 실패. 대상이 이미 사라진 아이템(<see cref="ErrorCode.ItemNotFound"/>)이면
        /// 페이징 이후 소모된 '유령' 행을 조작한 것이므로 계약대로 목록을 새로 고친다(세이브 기획서 5.2).</summary>
        private void OnActionError(NetworkError error)
        {
            Debug.LogWarning($"[Inventory] 장착/해제 실패: {error}");
            if (error != null && error.ErrorCode == ErrorCode.ItemNotFound)
            {
                ReloadBagAndRefresh();
            }
        }

        // ── 아이템 이동 ──

        /// <summary>드래그된 아이템을 대상 슬롯으로 이동한다(점유 시 스왑). 화면은 즉시 바꾸고(낙관적 반영)
        /// 배치를 서버에 저장한다(POST /api/game/inventory/move). 저장하지 않으면 패널을 닫았다 열 때
        /// 세이브 스냅샷 기준으로 다시 그려져 이동이 사라진다.</summary>
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

            RequestMove(view, from, to, occupant);
        }

        /// <summary>배치 이동을 서버에 저장한다. 성공하면 캐시된 세이브 스냅샷의 칸 번호도 같은 값으로 맞춰
        /// 재조회 없이 패널을 다시 열어도 배치가 유지되게 하고, 실패하면 서버 상태로 되돌린다.</summary>
        private void RequestMove(InventoryItemView view, InventoryItemSlot from, InventoryItemSlot to,
            InventoryItemView occupant)
        {
            if (NetworkManager.Instance == null || !Session.IsLoggedIn)
            {
                return;
            }

            long itemId = view.Data.itemId;
            int toSlot = to.Index;
            int fromSlot = from != null ? from.Index : -1;

            var req = new MoveRequest
            {
                userId = Session.UserId,
                token = Session.Token,
                data = new MoveData { itemId = itemId, toSlot = toSlot },
            };
            Debug.Log($"[Inventory] 배치 이동 요청 item={itemId} slot {fromSlot} → {toSlot}" +
                      (occupant != null ? " (스왑)" : string.Empty));

            NetworkManager.Instance.PostToGame<ApiResponse>("/api/game/inventory/move", req, _ =>
            {
                Session.ApplyBagSlot(itemId, toSlot);
                if (occupant != null && fromSlot >= 0)
                {
                    Session.ApplyBagSlot(occupant.Data.itemId, fromSlot); // 스왑된 아이템도 함께
                }
            }, OnMoveError);
        }

        /// <summary>배치 이동 실패: 낙관적으로 바꿔 둔 화면이 서버와 어긋나므로 가방을 다시 받아 되돌린다.</summary>
        private void OnMoveError(NetworkError error)
        {
            Debug.LogWarning($"[Inventory] 배치 이동 실패: {error}");
            ReloadBagAndRefresh();
        }

        /// <summary>
        /// 창 밖(딤) 클릭 처리. 큐브는 가방 위에 <b>함께</b> 열리고 자기 차단막은 꺼 두므로,
        /// 두 창 바깥의 클릭은 모두 이 딤이 받는다. 그래서 <b>큐브가 함께 열려 있으면 큐브부터</b> 닫고,
        /// 다시 밖을 클릭하면 그때 가방이 닫힌다(위에 있는 창부터 차례로 닫히는 순서).
        /// </summary>
        private void OnDimClick()
        {
            if (UIManager.Instance != null && UIManager.Instance.IsVisible(UIManager.PanelType.Cube))
            {
                UIManager.Instance.Hide(UIManager.PanelType.Cube);
                return;
            }
            Close();
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

        private static RawImage NewRawImage(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RawImage>();
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

        /// <summary>부모의 하단 중앙 기준으로 배치(x=중앙 오프셋, y=바닥에서 위로).</summary>
        private static void BottomCenter(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
        }
    }
}
