using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TaskbarHero.Client.Managers;
using TaskbarHero.Client.MasterData;
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
        [SerializeField] private Sprite panelBackground; // ui_panel_background
        [SerializeField] private Sprite slotNormal;      // ui_slot_normal
        [SerializeField] private Sprite slotHighlight;   // ui_slot_highlight
        [SerializeField] private Sprite slotPortrait;    // ui_slot_portrait

        [Header("초상화 캐릭터 프리팹 (classCode → 프리팹, 에디터 빌더가 배선)")]
        [Tooltip("초상화에 렌더할 캐릭터 프리팹. classCode 기준으로 선택된다(기사1·레인저2·마법사3).")]
        [SerializeField] private List<ClassCharacter> _classCharacters = new List<ClassCharacter>();

        [Header("격자 설정")]
        [SerializeField] private int columns = 5;
        [Tooltip("최초 아이템 슬롯 수. 그리드 마지막에는 확장 버튼 1칸이 추가된다(초기 총 칸 = +1).")]
        [SerializeField] private int initialItemSlots = 14;
        [Tooltip("한 번에 보이는 줄 수(스크롤). 2줄 = 10칸.")]
        [SerializeField] private int visibleRows = 2;

        [Header("구성 참조 (에디터 빌더가 배선 — 직접 수정 불필요)")]
        [SerializeField] private List<InventoryItemSlot> _gridSlots = new List<InventoryItemSlot>();
        [SerializeField] private List<InventoryItemSlot> _equipSlots = new List<InventoryItemSlot>();
        [SerializeField] private InventoryTooltip _tooltip;
        [SerializeField] private Text _charIndicatorText;
        [SerializeField] private Text _portraitLabel;
        [SerializeField] private RawImage _portraitImage;     // 초상화 캐릭터 렌더 표시(런타임 텍스처 배정)
        [SerializeField] private Button _prevButton;
        [SerializeField] private Button _nextButton;
        [SerializeField] private Button _skillButton;   // 스킬 레벨업 패널 진입
        [SerializeField] private Button _runeButton;    // 룬 패널 진입
        [SerializeField] private Button _cubeButton;    // 큐브 패널 진입
        [SerializeField] private Button _closeButton;
        [SerializeField] private Button _dimButton;
        [SerializeField] private RectTransform _gridContent; // 스크롤 콘텐츠(슬롯 부모)
        [SerializeField] private Button _expandButton;        // 확장 요청 버튼(항상 마지막 칸)
        [SerializeField] private Image _goldIcon;             // 골드 아이콘(item_1, 런타임 배정)
        [SerializeField] private Text _goldText;              // 보유 골드량
        [SerializeField] private Text _statPanelText;         // 초상화 좌측 능력치(장비 포함)
        [SerializeField] private RectTransform _expFill;       // 경험치 막대 채움(anchorMax.x = 진행률로 폭 조절)
        [SerializeField] private Text _expText;                // 경험치 텍스트(현재/필요 + 레벨업까지 남은 양)

        // 장착 슬롯 이름(equip_slot_master 1~6, 표시용 상수)
        private static readonly string[] EquipSlotNames = { "무기", "보조무기", "투구", "갑옷", "장갑", "신발" };

        private const int GoldCurrencyType = 1;   // 재화 타입 1 = 골드
        private const int GoldItemCode = 1;       // item_master 골드 코드(아이콘 item_1)

        private const float CanvasRefWidth = 1080f;
        private const float CanvasRefHeight = 1920f;

        private Font _font;
        private RectTransform _rootRect;      // 전체 화면(Canvas) Rect
        private int _selectedCharacter;       // 현재 보고 있는 파티 캐릭터(0-based)
        private int _partyCount = 1;          // 실제 파티 캐릭터 수(세션 기준)
        private ItemIconDatabase _iconDb;     // 아이템 아이콘 조회

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
        /// (UIManager가 인스턴스를 캐싱·재사용하므로 활성화 시점마다 최신 데이터를 반영해야 한다.)</summary>
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
            RefreshFromSession();
        }

        /// <summary>패널이 숨겨지면 초상화 렌더러(카메라)를 꺼 불필요한 렌더를 막는다.</summary>
        private void OnDisable()
        {
            if (_portraitStage != null)
            {
                _portraitStage.SetActive(false);
            }
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
            BuildHeader(container);
            BuildGoldArea(container);  // 보유 골드 표시(좌상단)
            BuildEquipArea(container); // 캐릭터 네비게이션 포함, 가로 중앙 정렬
            BuildGrid(container);
            BuildGrowthButtons(container); // 스킬·룬 진입 버튼(인벤토리 아이템 아래, 패널 최하단)
            BuildTooltip();
        }

        /// <summary>성장 진입 버튼(스킬 레벨업·룬)을 패널 최하단(인벤토리 아이템 아래)에 가로 중앙으로 배치한다.
        /// 가방 격자와 패널 바닥 사이 여백에 맞춰 낮은 높이로 둔다(겹침 방지).</summary>
        private void BuildGrowthButtons(RectTransform container)
        {
            const float w = 210f, h = 44f, y = 3f, dx = 224f;
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

        /// <summary>좌상단 보유 골드 영역(골드 아이콘 + 수량)을 구성한다. 아이콘/수량은 런타임에 세션에서 채운다.</summary>
        private void BuildGoldArea(RectTransform container)
        {
            var area = NewRect("GoldArea", container);
            area.anchorMin = area.anchorMax = new Vector2(0f, 1f);
            area.pivot = new Vector2(0f, 1f);
            area.anchoredPosition = new Vector2(40f, -40f);
            area.sizeDelta = new Vector2(280f, 60f);

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

        /// <summary>세션 인벤토리(비장착 아이템)를 가방 격자에 채운다. 용량만큼 슬롯을 확보한다.</summary>
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

            var inv = Session.GameData != null ? Session.GameData.inventory : null;
            if (inv == null)
            {
                return;
            }
            foreach (var item in inv)
            {
                if (item == null || item.equippedCharacterId != 0)
                {
                    continue; // 장착 중(equippedCharacterId≠0)인 아이템은 장비 슬롯에서 표시
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
            view.Setup(BuildDisplay(item), _font);
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

        /// <summary>확장 성공: 소모 골드·잔액·확장 후 용량을 공용 모달로 안내하고, 세이브를 재로드해 UI를 갱신한다.</summary>
        private void OnExpandSuccess(ExpandResponse resp)
        {
            long cost = 0, balance = 0;
            int capacity = 0;
            if (resp != null && resp.data != null)
            {
                if (resp.data.cost != null) cost = resp.data.cost.amount;
                if (resp.data.balance != null && resp.data.balance.Count > 0) balance = resp.data.balance[0].amount;
                capacity = resp.data.inventoryCapacity;
            }
            ReloadAndRefresh(); // 용량/골드/격자 최신화

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

        // 장비 영역 블록의 내부 폭(능력치 패널 + 초상 + 6부위 슬롯을 포함). 이 블록을 패널 가로 중앙에 둔다.
        private const float StatPanelWidth = 190f;
        private const float PortraitX = 214f;   // 능력치 패널(190) + 간격(24)
        private const float EquipSlotsX = 466f; // 초상(PortraitX+220) + 간격(32)
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

            // 캐릭터 전환 네비게이션 (◀ 인디케이터 ▶)
            var prev = NewImage("PrevCharButton", area, slotNormal);
            TopLeft(prev.rectTransform, 0f, 0f, 64f, 64f);
            Stretch(NewText("PrevLabel", prev.rectTransform, "<", 36, TextAnchor.MiddleCenter).rectTransform);
            _prevButton = prev.gameObject.AddComponent<Button>();

            _charIndicatorText = NewText("CharIndicator", area, "", 28, TextAnchor.MiddleCenter);
            TopLeft(_charIndicatorText.rectTransform, 70f, 0f, EquipBlockWidth - 140f, 64f);

            var next = NewImage("NextCharButton", area, slotNormal);
            TopLeft(next.rectTransform, EquipBlockWidth - 64f, 0f, 64f, 64f);
            Stretch(NewText("NextLabel", next.rectTransform, ">", 36, TextAnchor.MiddleCenter).rectTransform);
            _nextButton = next.gameObject.AddComponent<Button>();

            // '장비' 라벨
            var label = NewText("EquipLabel", area, "장비", 32, TextAnchor.UpperLeft);
            TopLeft(label.rectTransform, 0f, 90f, 200f, 44f);

            // 능력치 패널(초상화 좌측): 장비 포함 현재 캐릭터 능력치.
            BuildStatPanel(area);

            // 캐릭터 초상 슬롯(능력치 패널 우측)
            var portrait = NewImage("PortraitSlot", area, slotPortrait);
            TopLeft(portrait.rectTransform, PortraitX, 144f, 220f, 300f);

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

        /// <summary>초상화 아래 캐릭터 경험치 막대(가로 진행바 + 현재/필요·남은 경험치 텍스트)를 구성한다.
        /// 값은 런타임 <see cref="RefreshExp"/>에서 세션·마스터 데이터로 채운다.</summary>
        private void BuildExpBar(RectTransform area)
        {
            // 장비 블록 하단 가로 중앙에 배치(초상·능력치·장비 슬롯 행 바로 아래).
            const float barWidth = 250f; // 기존 400에서 150 축소
            var container = NewRect("ExpBar", area);
            TopLeft(container, (EquipBlockWidth - barWidth) * 0.5f, 448f, barWidth, 24f);

            var bg = NewImage("ExpBarBg", container, null);
            bg.color = new Color(0f, 0f, 0f, 0.55f);
            Stretch(bg.rectTransform);

            // 채움 막대: 좌측 고정, 폭은 런타임에 anchorMax.x = 진행률로 조절.
            var fill = NewImage("ExpBarFill", bg.rectTransform, null);
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

        /// <summary>초상화 좌측 능력치 패널(제목 + 능력치 텍스트). 값은 런타임에 RefreshStatPanel로 채운다.</summary>
        private void BuildStatPanel(RectTransform area)
        {
            var bg = NewImage("StatPanel", area, null);
            bg.color = new Color(0.09f, 0.11f, 0.18f, 0.9f);
            TopLeft(bg.rectTransform, 0f, 144f, StatPanelWidth, 300f);

            var title = NewText("StatTitle", bg.rectTransform, "능력치", 26, TextAnchor.UpperCenter);
            title.fontStyle = FontStyle.Bold;
            TopLeft(title.rectTransform, 0f, 10f, StatPanelWidth, 34f);

            _statPanelText = NewText("StatValues", bg.rectTransform, "", 22, TextAnchor.UpperLeft);
            _statPanelText.lineSpacing = 1.25f;
            TopLeft(_statPanelText.rectTransform, 14f, 54f, StatPanelWidth - 24f, 236f);
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
                return;
            }
            _selectedCharacter = (_selectedCharacter - 1 + _partyCount) % _partyCount;
            RefreshCharacter();
            RefreshGrid(); // 선택 캐릭터 클래스 변경 → 다른 클래스 장비 X 표시 갱신
        }

        /// <summary>다음 파티 캐릭터로 전환(순환).</summary>
        private void OnNextCharacter()
        {
            if (_partyCount <= 1)
            {
                return;
            }
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

        /// <summary>선택된 캐릭터의 직업 프리팹을 초상화 렌더러에 반영한다(직업이 바뀔 때만 재생성).</summary>
        private void UpdatePortrait(List<CharacterDto> chars)
        {
            EnsurePortraitStage();
            if (_portrait == null)
            {
                return;
            }

            int classCode = chars != null && _selectedCharacter < chars.Count ? chars[_selectedCharacter].classCode : -1;
            if (classCode == _portraitClassCode)
            {
                return; // 동일 직업이면 인스턴스를 재생성하지 않음
            }
            _portraitClassCode = classCode;

            var prefab = PrefabForClass(classCode);
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

        /// <summary>classCode에 해당하는 초상화 캐릭터 프리팹을 반환한다(없으면 null).</summary>
        private GameObject PrefabForClass(int classCode)
        {
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
            if (_statPanelText == null)
            {
                return;
            }
            if (chars == null || _selectedCharacter >= chars.Count)
            {
                _statPanelText.text = string.Empty;
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

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(LongStatLine("공격력", baseS.atk, atkF));
            sb.AppendLine(LongStatLine("방어력", baseS.def, defF));
            sb.AppendLine(LongStatLine("체력", baseS.hp, hpF));
            if (critF != 0f) sb.AppendLine(PercentStatLine("치명확률", baseS.critChance, critF));
            if (critDF != 0f) sb.AppendLine(PercentStatLine("치명피해", baseS.critDamage, critDF));
            if (moveF != 0f) sb.AppendLine(MoveStatLine("이동속도", baseS.moveSpeed, moveF));
            _statPanelText.text = sb.ToString().TrimEnd();
        }

        /// <summary>정수 스탯 한 줄: "라벨  최종  (+상승분)". 상승분(장비+패시브)은 연한 파란색으로 병기.</summary>
        private static string LongStatLine(string label, long baseV, long finalV)
        {
            long bonus = finalV - baseV;
            string extra = bonus > 0 ? $"  <color={BonusColorHex}>(+{bonus})</color>" : string.Empty;
            return $"{label}  {finalV}{extra}";
        }

        /// <summary>퍼센트 스탯 한 줄(치명확률/치명피해). 값은 0~1 → % 표기.</summary>
        private static string PercentStatLine(string label, float baseV, float finalV)
        {
            float bonus = finalV - baseV;
            string extra = bonus > 0.0001f ? $"  <color={BonusColorHex}>(+{bonus * 100f:0.#}%)</color>" : string.Empty;
            return $"{label}  {finalV * 100f:0.#}%{extra}";
        }

        /// <summary>이동속도 한 줄.</summary>
        private static string MoveStatLine(string label, float baseV, float finalV)
        {
            float bonus = finalV - baseV;
            string extra = bonus > 0.001f ? $"  <color={BonusColorHex}>(+{bonus:0.##})</color>" : string.Empty;
            return $"{label}  {finalV:0.##}{extra}";
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

        /// <summary>이 캐릭터에 장착된 장비 스탯 합산(가산분).</summary>
        private static Stats EquipStats(CharacterDto c)
        {
            var db = MasterDataManager.Db;
            var total = new Stats();
            var inv = Session.GameData != null ? Session.GameData.inventory : null;
            if (db == null || inv == null)
            {
                return total;
            }
            foreach (var item in inv)
            {
                if (item != null && item.equippedCharacterId == c.characterId
                    && db.Items.TryGetValue(item.itemCode, out var im))
                {
                    total = Add(total, im.baseStats);
                }
            }
            return total;
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

        /// <summary>선택된 캐릭터의 6부위 장착 상태를 세션 인벤토리(장착품)에서 장비 슬롯에 반영한다.</summary>
        private void RefreshEquip(List<CharacterDto> chars)
        {
            CharacterDto cur = chars != null && _selectedCharacter < chars.Count ? chars[_selectedCharacter] : null;
            var inv = Session.GameData != null ? Session.GameData.inventory : null;

            for (int slot = 0; slot < _equipSlots.Count; slot++)
            {
                InventoryItemView.Display? equipped = null;
                if (cur != null && inv != null)
                {
                    foreach (var item in inv)
                    {
                        if (item != null
                            && item.equippedCharacterId == cur.characterId
                            && item.equippedSlot == slot + 1)
                        {
                            equipped = BuildDisplay(item);
                            break;
                        }
                    }
                }
                _equipSlots[slot].SetEquipped(equipped, _font);
            }
        }

        /// <summary>인벤토리 아이템 DTO를 마스터 데이터로 표시 정보(이름·등급·부위·요구·스탯·아이콘)로 변환한다.</summary>
        private InventoryItemView.Display BuildDisplay(InventoryItemDto item)
        {
            var db = MasterDataManager.Db;
            ItemMaster im = null;
            if (db != null)
            {
                db.Items.TryGetValue(item.itemCode, out im);
            }

            // 상세 문구(등급명·종류·요구조건·설명)는 공용 헬퍼로 통일한다 — 스테이지 클리어 보상·우편함·
            // (추후) 거래소의 아이템 상세 팝업(ItemDetailPopup)과 완전히 같은 텍스트가 나오도록 한다.
            var info = ItemInfoText.Build(item.itemCode, item.quantity);

            string name = info.name;
            if (item.enhanceLevel > 0)
            {
                name += $" +{item.enhanceLevel}";
            }

            // 착용 가능 판정: 장비이면서 (공용이거나 현재 캐릭터의 직업과 클래스 제한 일치) + (요구 레벨 이하)여야 한다.
            // 클래스 불일치 또는 레벨 미달 장비는 착용 불가(슬롯에 X 표시 + 장착 버튼 비활성).
            bool isEquip = im != null && im.itemType == 1;
            var cur = CurrentCharacter();
            int curClass = cur != null ? cur.classCode : -1;
            int curLevel = cur != null ? cur.level : -1;
            bool classOk = im != null && (im.classReq == 0 || cur == null || im.classReq == curClass);
            bool levelOk = im != null && (im.levelReq <= 0 || cur == null || curLevel >= im.levelReq);
            bool equipLocked = isEquip && cur != null
                && ((im.classReq != 0 && im.classReq != curClass) || (im.levelReq > 0 && curLevel < im.levelReq));

            return new InventoryItemView.Display
            {
                name = name,
                grade = info.grade,
                gradeValue = info.gradeValue,
                slotName = info.category,
                requirement = info.requirement,
                stats = ItemInfoText.Stats(im),
                description = info.description,
                iconColor = GradeColor(info.gradeValue),
                icon = _iconDb != null ? _iconDb.Get(item.itemCode) : null,
                itemId = item.itemId,
                equippedSlot = item.equippedSlot,
                equippable = isEquip && classOk && levelOk,
                equipLocked = equipLocked,
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
            NetworkManager.Instance.PostToGame<ApiResponse>("/api/game/inventory/equip", req, _ => ReloadAndRefresh(), OnActionError);
        }

        /// <summary>현재 선택 캐릭터의 지정 슬롯 장비를 해제 요청한다(성공 시 재로드·갱신).</summary>
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
            NetworkManager.Instance.PostToGame<ApiResponse>("/api/game/inventory/unequip", req, _ => ReloadAndRefresh(), OnActionError);
        }

        /// <summary>장착/해제 후 세이브 스냅샷을 재로드해 세션·UI·전투 스탯을 최신화한다.</summary>
        private void ReloadAndRefresh()
        {
            var req = new AuthRequest { userId = Session.UserId, token = Session.Token };
            NetworkManager.Instance.PostToGame<LoadResponse>("/api/game/load", req, resp =>
            {
                if (resp != null && resp.data != null)
                {
                    Session.SetGameData(resp.data);
                }
                RequestHideTooltip();
                RefreshFromSession();          // 인벤토리 UI 갱신
                Session.RaiseInventoryChanged(); // 전투 스탯 재계산 트리거
            }, OnActionError);
        }

        private void OnActionError(NetworkError error)
        {
            Debug.LogWarning($"[Inventory] 장착/해제 실패: {error}");
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
