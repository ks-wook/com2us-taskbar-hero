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
    /// 룬(Rune Tree) 오버레이 패널(growth 기획서 §5.4). 인벤토리의 '룬' 버튼으로 진입한다.
    /// 룬은 <b>계정 공용</b> 성장 축으로, 골드를 소모해 레벨을 올리는 <b>버프</b>이며 선행 룬(prereq)을
    /// 해금해야 다음 룬을 열 수 있는 트리 구조다. 정적 계층(제목·골드·닫기·상세 패널·트리 영역)은
    /// 에디터 빌드 시 생성되고, 표시될 때마다 세션(<see cref="Session.GameData"/>)의 룬 레벨·골드와
    /// 마스터 데이터(rune_master: prereq·레벨별 비용·효과)로 트리와 상세를 채운다. 서버가 최종 확정한다.
    /// 레벨별 골드 비용은 마스터 `costs`(= 서버 rune_cost)와 동일하므로 표시값과 차감값이 일치한다.
    /// 기획서: docs/세부/growth-기획서.md §2·§5.4
    /// </summary>
    public class RunePanelController : MonoBehaviour
    {
        [Header("UI 리소스 (Assets/Art/UI/Inventory 공용)")]
        [SerializeField] private Sprite panelBackground;
        [SerializeField] private Sprite slotNormal;
        [SerializeField] private Sprite slotHighlight;
        [Tooltip("레벨업 버튼 배경 아트(Assets/Art/UI/ui_bg.png, 9-slice). 없으면 슬롯 배경으로 폴백.")]
        [SerializeField] private Sprite upgradeButtonSprite; // ui_bg

        [Header("룬 아이콘(runeCode → 스프라이트, 에디터 빌더가 Assets/Art/Icon/Rune에서 배선)")]
        [SerializeField] private List<RuneIconEntry> _runeIcons = new List<RuneIconEntry>();

        /// <summary>룬 코드 ↔ 아이콘 스프라이트 매핑(에디터 빌더가 채운다).</summary>
        [System.Serializable]
        private struct RuneIconEntry
        {
            public int runeCode;
            public Sprite sprite;
        }

        [Header("구성 참조 (에디터 빌더가 배선 — 직접 수정 불필요)")]
        [SerializeField] private Image _goldIcon;
        [SerializeField] private Text _goldText;
        [SerializeField] private Button _closeButton;
        [SerializeField] private Button _dimButton;
        [SerializeField] private Button _upgradeButton;
        [SerializeField] private Text _upgradeLabel;
        [SerializeField] private Text _messageText;
        // 상세 패널
        [SerializeField] private Image _detailIcon;
        [SerializeField] private Text _detailName;
        [SerializeField] private Text _detailLevel;
        [SerializeField] private Text _detailEffect;
        [SerializeField] private Image _detailCostIcon;
        [SerializeField] private Text _detailCostText;
        // 트리 노드 부모
        [SerializeField] private RectTransform _treeArea;

        private const float CanvasRefWidth = 1080f;
        private const float CanvasRefHeight = 1920f;
        private const int GoldCurrencyType = 1;
        private const int GoldItemCode = 1;

        private Font _font;
        private RectTransform _rootRect;
        private ItemIconDatabase _iconDb;
        private int _selectedRune = -1;   // 현재 선택 룬(없으면 -1)
        private bool _busy;

        private readonly List<GameObject> _treeChildren = new List<GameObject>();

        private bool AlreadyBuilt => _treeArea != null;

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
                Construct();
            }
            WireRuntime();
        }

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

        // ── 구성 ──

        /// <summary>패널 정적 계층을 1회 생성하고 직렬화 참조를 채운다.</summary>
        private void Construct()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildCanvas();
            BuildDim();
            var container = BuildContainer();
            BuildHeader(container);
            BuildDetailPanel(container);
            BuildTreeArea(container);
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
            canvas.sortingOrder = 110; // 인벤토리(100) 위

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
            rt.sizeDelta = new Vector2(860f, 1020f); // 화면(가로 16:9 포함) 안에 전체 UI가 보이도록 높이 축소
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        // 헤더·상세 패널 배치. 배경 프레임(ui_bg_2)의 장식을 피해 플레이 모드에서 직접 옮겨 확정한 값이다
        // (좌표는 패널/상세 패널 좌상단 기준, 아래로 +).
        private const float GoldAreaX = 66f;
        private const float GoldAreaY = 52f;
        private const float DetailPanelX = 83.33f;
        private const float DetailPanelY = 155f;
        private const float DetailPanelWidth = 693.34f;
        private const float DetailPanelHeight = 159.96f;   // 낮게 줄여 트리 영역을 넓혔다
        private const float CostX = 402f;                  // 다음 레벨 비용 블록의 x(상세 패널 오른쪽 위)
        private const float CostLabelY = 12f;
        private const float CostIconY = 38f;
        private const float CostTextY = 34f;
        private const float UpgradeButtonX = -16f;         // 상세 패널 우하단 기준
        private const float UpgradeButtonY = 81.96f;
        private const float UpgradeButtonWidth = 150f;
        private const float UpgradeButtonHeight = 88f;

        /// <summary>보유 골드 표시만 둔다.
        /// <b>제목("룬")과 닫기(X) 버튼은 만들지 않는다</b> — 제목 자리는 배경 아트(ui_bg_2)의 상단 장식판이
        /// 그리고 있어 글자가 겹쳐 보이고, 닫기 버튼은 다른 패널과 같이 미관상 두지 않는다(딤 클릭으로 닫는다).
        /// 위치는 플레이 모드에서 직접 옮겨 확정한 값이다.</summary>
        private void BuildHeader(RectTransform container)
        {
            // 보유 골드
            var goldBg = NewImage("GoldArea", container, null);
            goldBg.color = new Color(0f, 0f, 0f, 0.35f);
            TopLeft(goldBg.rectTransform, GoldAreaX, GoldAreaY, 300f, 60f);
            var gi = NewImage("GoldIcon", goldBg.rectTransform, null);
            gi.raycastTarget = false;
            gi.preserveAspect = true;
            TopLeft(gi.rectTransform, 8f, 6f, 48f, 48f);
            _goldIcon = gi;
            _goldText = NewText("GoldText", goldBg.rectTransform, "0", 30, TextAnchor.MiddleLeft);
            TopLeft(_goldText.rectTransform, 64f, 8f, 224f, 44f);
        }

        /// <summary>선택 룬 상세(아이콘·이름·레벨·효과·다음 비용·레벨업 버튼).</summary>
        private void BuildDetailPanel(RectTransform container)
        {
            var bg = NewImage("DetailPanel", container, null);
            bg.color = new Color(0.08f, 0.09f, 0.14f, 0.96f);
            TopLeft(bg.rectTransform, DetailPanelX, DetailPanelY, DetailPanelWidth, DetailPanelHeight);

            // 아이콘 타일(좌)
            var iconBg = NewImage("DetailIconBg", bg.rectTransform, slotNormal);
            TopLeft(iconBg.rectTransform, 20f, 18f, 116f, 116f);
            var icon = NewImage("DetailIcon", iconBg.rectTransform, null);
            icon.raycastTarget = false;
            Stretch(icon.rectTransform);
            icon.rectTransform.offsetMin = new Vector2(8f, 8f);
            icon.rectTransform.offsetMax = new Vector2(-8f, -8f);
            _detailIcon = icon;

            _detailName = NewText("DetailName", bg.rectTransform, "룬을 선택하세요", 32, TextAnchor.UpperLeft);
            _detailName.fontStyle = FontStyle.Bold;
            TopLeft(_detailName.rectTransform, 150f, 14f, 600f, 40f);

            _detailLevel = NewText("DetailLevel", bg.rectTransform, string.Empty, 26, TextAnchor.UpperLeft);
            _detailLevel.color = new Color(0.55f, 0.85f, 0.55f);
            TopLeft(_detailLevel.rectTransform, 150f, 54f, 600f, 34f);

            _detailEffect = NewText("DetailEffect", bg.rectTransform, string.Empty, 24, TextAnchor.UpperLeft);
            _detailEffect.color = new Color(0.82f, 0.86f, 0.95f);
            TopLeft(_detailEffect.rectTransform, 150f, 90f, 600f, 54f);

            // 다음 레벨 비용(골드) — 상세 패널을 낮게 줄인 만큼 하단이 아니라 <b>오른쪽 위</b>로 옮겼다.
            var costLabel = NewText("CostLabel", bg.rectTransform, "다음 레벨", 22, TextAnchor.UpperLeft);
            costLabel.color = new Color(0.7f, 0.72f, 0.8f);
            TopLeft(costLabel.rectTransform, CostX, CostLabelY, 200f, 28f);
            var ci = NewImage("DetailCostIcon", bg.rectTransform, null);
            ci.raycastTarget = false;
            ci.preserveAspect = true;
            TopLeft(ci.rectTransform, CostX, CostIconY, 36f, 36f);
            _detailCostIcon = ci;
            _detailCostText = NewText("DetailCostText", bg.rectTransform, string.Empty, 28, TextAnchor.MiddleLeft);
            _detailCostText.fontStyle = FontStyle.Bold;
            TopLeft(_detailCostText.rectTransform, CostX + 42f, CostTextY, 320f, 44f);

            // 레벨업 버튼(우하단). 버튼 아트(ui_bg 9-slice)가 배선돼 있으면 그것을 쓰고, 없으면 슬롯 배경으로 폴백한다.
            bool hasBtnArt = upgradeButtonSprite != null;
            var btn = NewImage("UpgradeButton", bg.rectTransform, hasBtnArt ? upgradeButtonSprite : slotNormal);
            btn.type = hasBtnArt ? Image.Type.Sliced : Image.Type.Simple;
            btn.color = new Color(0.22f, 0.40f, 0.28f, 0.98f);
            var brt = btn.rectTransform;
            brt.anchorMin = brt.anchorMax = new Vector2(1f, 0f);
            brt.pivot = new Vector2(1f, 0f);
            brt.anchoredPosition = new Vector2(UpgradeButtonX, UpgradeButtonY);
            brt.sizeDelta = new Vector2(UpgradeButtonWidth, UpgradeButtonHeight);
            _upgradeLabel = NewText("UpgradeLabel", btn.rectTransform, "레벨업", 32, TextAnchor.MiddleCenter);
            _upgradeLabel.fontStyle = FontStyle.Bold;
            Stretch(_upgradeLabel.rectTransform);
            _upgradeButton = btn.gameObject.AddComponent<Button>();
        }

        /// <summary>룬 트리가 그려지는 영역(런타임에 노드·연결선이 채워짐).</summary>
        private void BuildTreeArea(RectTransform container)
        {
            var bg = NewImage("TreeArea", container, null);
            bg.color = new Color(0.05f, 0.06f, 0.10f, 0.6f);
            TopLeft(bg.rectTransform, 40f, 332f, 780f, 628f); // DetailPanel 축소분을 흡수해 트리 영역 확대(하단 메시지 위까지)
            _treeArea = bg.rectTransform;
        }

        private void BuildFooter(RectTransform container)
        {
            _messageText = NewText("Message", container, string.Empty, 26, TextAnchor.MiddleCenter);
            var mrt = _messageText.rectTransform;
            mrt.anchorMin = mrt.anchorMax = new Vector2(0.5f, 0f);
            mrt.pivot = new Vector2(0.5f, 0f);
            mrt.sizeDelta = new Vector2(780f, 40f);
            mrt.anchoredPosition = new Vector2(0f, 20f);
        }

        private void WireRuntime()
        {
            if (_closeButton != null) _closeButton.onClick.AddListener(Close);
            if (_dimButton != null) _dimButton.onClick.AddListener(Close);
            if (_upgradeButton != null) _upgradeButton.onClick.AddListener(OnUpgrade);
        }

        // ── 세션/마스터 연동 ──

        /// <summary>골드·룬 트리·상세를 세션+마스터로 갱신한다.</summary>
        private void RefreshFromSession()
        {
            MasterDataManager.EnsureLoaded();
            if (_iconDb == null)
            {
                _iconDb = ItemIconDatabase.Load();
            }
            RefreshGold();

            // 선택 룬 기본값: 없으면 첫 루트(선행 0) 중 최소 코드.
            if (_selectedRune < 0)
            {
                _selectedRune = FirstRootRune();
            }

            RebuildTree();
            RefreshDetail();
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
            if (_detailCostIcon != null && sp != null)
            {
                _detailCostIcon.sprite = sp;
                _detailCostIcon.color = Color.white;
            }
        }

        /// <summary>룬 트리(노드+연결선)를 마스터 데이터로 (재)구성한다. 깊이(선행 체인)별 행으로 배치한다.</summary>
        private void RebuildTree()
        {
            ClearTree();
            var db = MasterDataManager.Db;
            if (db == null || _treeArea == null || db.Runes.Count == 0)
            {
                return;
            }

            var runes = new List<RuneMaster>(db.Runes.Values);
            runes.Sort((a, b) => a.runeCode.CompareTo(b.runeCode));

            // 깊이 계산(선행 체인 길이). 루트(prereq 0)=0.
            var depth = new Dictionary<int, int>();
            foreach (var r in runes)
            {
                depth[r.runeCode] = DepthOf(r.runeCode, db);
            }
            int maxDepth = 0;
            foreach (var d in depth.Values)
            {
                if (d > maxDepth) maxDepth = d;
            }

            // 깊이별 그룹.
            var byDepth = new Dictionary<int, List<RuneMaster>>();
            foreach (var r in runes)
            {
                int d = depth[r.runeCode];
                if (!byDepth.TryGetValue(d, out var list))
                {
                    list = new List<RuneMaster>();
                    byDepth[d] = list;
                }
                list.Add(r);
            }

            Vector2 areaSize = _treeArea.rect.size;
            if (areaSize.x < 1f) areaSize = new Vector2(780f, 980f); // 빌드 시점 레이아웃 전 폴백
            float rowH = Mathf.Min(180f, areaSize.y / (maxDepth + 1));
            float usedH = rowH * (maxDepth + 1);

            // 연결선/노드 부모(연결선이 노드 뒤에 그려지도록 먼저 생성).
            var lines = NewRect("Connectors", _treeArea);
            Stretch(lines);
            _treeChildren.Add(lines.gameObject);
            var nodesRoot = NewRect("Nodes", _treeArea);
            Stretch(nodesRoot);
            _treeChildren.Add(nodesRoot.gameObject);

            // 노드 중심 좌표(센터 앵커 기준) 계산.
            var pos = new Dictionary<int, Vector2>();
            for (int d = 0; d <= maxDepth; d++)
            {
                if (!byDepth.TryGetValue(d, out var list))
                {
                    continue;
                }
                int n = list.Count;
                // depth 0을 아래쪽에 두고 위로 쌓는다.
                float y = -usedH / 2f + rowH / 2f + d * rowH;
                for (int i = 0; i < n; i++)
                {
                    float x = -areaSize.x / 2f + (i + 1f) * areaSize.x / (n + 1f);
                    pos[list[i].runeCode] = new Vector2(x, y);
                }
            }

            // 연결선(자식→선행) 먼저.
            foreach (var r in runes)
            {
                if (r.prereqCode != 0 && pos.ContainsKey(r.prereqCode) && pos.ContainsKey(r.runeCode))
                {
                    DrawLine(lines, pos[r.prereqCode], pos[r.runeCode]);
                }
            }

            // 노드.
            foreach (var r in runes)
            {
                if (pos.TryGetValue(r.runeCode, out var p))
                {
                    CreateNode(nodesRoot, r, p, db);
                }
            }
        }

        /// <summary>룬 노드 타일 하나를 생성한다(스탯색 배경·레벨 배지·잠금/선택 표시·클릭 선택).</summary>
        private void CreateNode(RectTransform parent, RuneMaster rune, Vector2 center, MasterDatabase db)
        {
            int level = RuneLevel(rune.runeCode);
            bool locked = IsLocked(rune, db);
            bool maxed = level >= rune.maxLevel;

            var node = NewImage($"Rune_{rune.runeCode}", parent, slotNormal);
            var rt = node.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(112f, 112f);
            rt.anchoredPosition = center;

            Color baseColor = StatColor(rune.statType);
            node.color = locked ? new Color(0.16f, 0.17f, 0.20f, 0.95f)
                                 : Color.Lerp(baseColor, Color.black, level > 0 ? 0f : 0.35f);

            // 선택 하이라이트 프레임.
            if (rune.runeCode == _selectedRune)
            {
                var frame = NewImage("Sel", rt, slotHighlight);
                frame.raycastTarget = false;
                Stretch(frame.rectTransform);
                frame.rectTransform.offsetMin = new Vector2(-6f, -6f);
                frame.rectTransform.offsetMax = new Vector2(6f, 6f);
            }

            // 이름(상단, 축약).
            var name = NewText("Name", rt, rune.name, 20, TextAnchor.UpperCenter);
            name.color = locked ? new Color(0.6f, 0.6f, 0.65f) : Color.white;
            var nrt = name.rectTransform;
            nrt.anchorMin = new Vector2(0f, 1f);
            nrt.anchorMax = new Vector2(1f, 1f);
            nrt.pivot = new Vector2(0.5f, 1f);
            nrt.offsetMin = new Vector2(2f, -52f);
            nrt.offsetMax = new Vector2(-2f, -6f);

            // 레벨 배지(하단).
            string badge = locked ? "잠김" : $"{level} / {rune.maxLevel}";
            var lvl = NewText("Lv", rt, badge, 22, TextAnchor.LowerCenter);
            lvl.fontStyle = FontStyle.Bold;
            lvl.color = maxed ? new Color(1f, 0.82f, 0.30f) : (locked ? new Color(0.7f, 0.5f, 0.5f) : Color.white);
            var lrt = lvl.rectTransform;
            lrt.anchorMin = new Vector2(0f, 0f);
            lrt.anchorMax = new Vector2(1f, 0f);
            lrt.pivot = new Vector2(0.5f, 0f);
            lrt.offsetMin = new Vector2(2f, 4f);
            lrt.offsetMax = new Vector2(-2f, 34f);

            int code = rune.runeCode;
            var btn = node.gameObject.AddComponent<Button>();
            UiClickSound.Suppress(btn); // 룬 선택음(sfx_ui_slot_select)을 직접 재생하므로 전역 클릭음 제외
            btn.onClick.AddListener(() => OnSelectRune(code));

            _treeChildren.Add(node.gameObject);
        }

        /// <summary>두 중심점을 잇는 얇은 연결선 이미지를 만든다(부모는 센터 앵커).</summary>
        private void DrawLine(RectTransform parent, Vector2 a, Vector2 b)
        {
            var img = NewImage("Link", parent, null);
            img.color = new Color(0.5f, 0.52f, 0.6f, 0.75f);
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            Vector2 mid = (a + b) * 0.5f;
            Vector2 delta = b - a;
            float len = delta.magnitude;
            rt.anchoredPosition = mid;
            rt.sizeDelta = new Vector2(len, 6f);
            float ang = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            rt.localRotation = Quaternion.Euler(0f, 0f, ang);
        }

        /// <summary>선택 룬 상세(이름·레벨·효과·다음 비용)와 레벨업 버튼 상태를 갱신한다.</summary>
        private void RefreshDetail()
        {
            var db = MasterDataManager.Db;
            RuneMaster rune = null;
            if (db != null && _selectedRune >= 0)
            {
                db.Runes.TryGetValue(_selectedRune, out rune);
            }

            if (rune == null)
            {
                if (_detailName != null) _detailName.text = "룬을 선택하세요";
                if (_detailLevel != null) _detailLevel.text = string.Empty;
                if (_detailEffect != null) _detailEffect.text = string.Empty;
                if (_detailCostText != null) _detailCostText.text = string.Empty;
                if (_detailIcon != null)
                {
                    _detailIcon.sprite = null;
                    _detailIcon.color = new Color(1f, 1f, 1f, 0f);
                }
                SetUpgrade(false, "레벨업");
                return;
            }

            int level = RuneLevel(rune.runeCode);
            bool locked = IsLocked(rune, db);
            bool maxed = level >= rune.maxLevel;

            if (_detailIcon != null)
            {
                // Assets/Art/Icon/Rune 아이콘을 우선 사용. 없으면 stat 색상 폴백.
                var runeSprite = IconForRune(rune.runeCode);
                _detailIcon.sprite = runeSprite;
                _detailIcon.preserveAspect = true;
                _detailIcon.color = runeSprite != null ? Color.white : StatColor(rune.statType);
            }
            if (_detailName != null) _detailName.text = rune.name;
            if (_detailLevel != null) _detailLevel.text = $"레벨 {level} / {rune.maxLevel}";
            if (_detailEffect != null) _detailEffect.text = EffectSummary(rune, level);

            long nextCost = NextCost(rune, level);
            long gold = CurrentGold();
            bool canUp = !maxed && !locked && nextCost >= 0 && gold >= nextCost;

            if (_detailCostText != null)
            {
                if (maxed)
                {
                    _detailCostText.text = "최대 레벨";
                    _detailCostText.color = new Color(1f, 0.82f, 0.3f);
                }
                else if (locked)
                {
                    _detailCostText.text = "선행 룬 필요";
                    _detailCostText.color = new Color(1f, 0.6f, 0.5f);
                }
                else if (nextCost >= 0)
                {
                    _detailCostText.text = nextCost.ToString("N0");
                    _detailCostText.color = gold >= nextCost ? Color.white : new Color(1f, 0.5f, 0.45f);
                }
                else
                {
                    _detailCostText.text = "-";
                    _detailCostText.color = Color.white;
                }
            }
            if (_detailCostIcon != null)
            {
                _detailCostIcon.enabled = !maxed && !locked && nextCost >= 0;
            }

            SetUpgrade(canUp, maxed ? "MAX" : (locked ? "잠김" : "레벨업"));
        }

        /// <summary>레벨업 버튼 활성/라벨을 설정한다.</summary>
        private void SetUpgrade(bool interactable, string label)
        {
            if (_upgradeButton != null)
            {
                _upgradeButton.interactable = interactable;
                var img = _upgradeButton.GetComponent<Image>();
                if (img != null)
                {
                    img.color = interactable ? new Color(0.22f, 0.40f, 0.28f, 0.98f)
                                              : new Color(0.20f, 0.22f, 0.30f, 0.9f);
                }
            }
            if (_upgradeLabel != null) _upgradeLabel.text = label;
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

        /// <summary>룬 코드의 현재 레벨(세션, 없으면 0).</summary>
        private static int RuneLevel(int runeCode)
        {
            var runes = Session.GameData != null ? Session.GameData.runes : null;
            if (runes != null)
            {
                foreach (var r in runes)
                {
                    if (r != null && r.runeCode == runeCode)
                    {
                        return r.level;
                    }
                }
            }
            return 0;
        }

        /// <summary>선행 룬이 있고 그 레벨이 0이면 잠김(해금 불가). 루트(prereq 0)는 잠기지 않는다.</summary>
        private static bool IsLocked(RuneMaster rune, MasterDatabase db)
            => rune.prereqCode != 0 && RuneLevel(rune.prereqCode) < 1;

        /// <summary>선행 체인 길이(루트=0). 순환/누락은 0으로 방어.</summary>
        private static int DepthOf(int runeCode, MasterDatabase db)
        {
            int d = 0;
            int cur = runeCode;
            var guard = 0;
            while (db.Runes.TryGetValue(cur, out var r) && r.prereqCode != 0 && guard++ < 32)
            {
                d++;
                cur = r.prereqCode;
            }
            return d;
        }

        /// <summary>다음 레벨(cur+1)로 올릴 때 골드 비용. costs에서 조회(없으면 -1).</summary>
        private static long NextCost(RuneMaster rune, int level)
        {
            if (rune.costs == null || level >= rune.maxLevel)
            {
                return -1;
            }
            int target = level + 1;
            foreach (var c in rune.costs)
            {
                if (c.level == target)
                {
                    return c.cost;
                }
            }
            return -1;
        }

        /// <summary>룬 효과 요약(계정 공용 버프). statType→라벨, 누적량 = statValue×레벨(%).</summary>
        private static string EffectSummary(RuneMaster rune, int level)
        {
            string label = StatLabel(rune.statType);
            bool reduce = rune.statType == 7; // 재사용 대기시간은 감소 방향
            if (level <= 0)
            {
                float per = rune.statValue * 100f;
                return $"미해금 · 레벨당 {label} {(reduce ? "-" : "+")}{per:0.#}%";
            }
            float total = rune.statValue * level * 100f;
            return $"전체 히어로 {label} {(reduce ? "-" : "+")}{total:0.#}%";
        }

        /// <summary>스탯 타입(1~7) 한글 라벨.</summary>
        private static string StatLabel(int statType)
        {
            switch (statType)
            {
                case 1: return "공격력";
                case 2: return "방어력";
                case 3: return "체력";
                case 4: return "치명확률";
                case 5: return "치명피해";
                case 6: return "이동속도";
                case 7: return "재사용 대기시간";
                default: return "능력치";
            }
        }

        /// <summary>스탯 타입별 노드 배경색(트리에서 계열 구분).</summary>
        /// <summary>룬 코드에 배선된 아이콘 스프라이트를 반환한다(없으면 null).</summary>
        private Sprite IconForRune(int runeCode)
        {
            if (_runeIcons != null)
            {
                foreach (var e in _runeIcons)
                {
                    if (e.sprite != null && e.runeCode == runeCode)
                    {
                        return e.sprite;
                    }
                }
            }
            return null;
        }

        private static Color StatColor(int statType)
        {
            switch (statType)
            {
                case 1: return new Color(0.55f, 0.20f, 0.18f); // 공격(적)
                case 2: return new Color(0.20f, 0.34f, 0.55f); // 방어(청)
                case 3: return new Color(0.24f, 0.48f, 0.28f); // 체력(녹)
                case 4: return new Color(0.55f, 0.45f, 0.15f); // 치명확률(금)
                case 5: return new Color(0.58f, 0.32f, 0.12f); // 치명피해(주황)
                case 6: return new Color(0.24f, 0.48f, 0.5f);  // 이동속도(청록)
                case 7: return new Color(0.36f, 0.28f, 0.52f); // 재사용(보라)
                default: return new Color(0.3f, 0.3f, 0.35f);
            }
        }

        /// <summary>선행 0인 첫 루트 룬 코드(없으면 -1).</summary>
        private static int FirstRootRune()
        {
            var db = MasterDataManager.Db;
            if (db == null)
            {
                return -1;
            }
            int best = -1;
            foreach (var r in db.Runes.Values)
            {
                if (r.prereqCode == 0 && (best < 0 || r.runeCode < best))
                {
                    best = r.runeCode;
                }
            }
            return best;
        }

        // ── 상호작용 ──

        /// <summary>룬 노드 선택 → 상세 갱신(트리 하이라이트도 다시 그림).</summary>
        private void OnSelectRune(int runeCode)
        {
            _selectedRune = runeCode;
            SoundManager.Sfx(SoundId.UiSlotSelect); // 룬 선택음(사운드 정의서 §8 공용음 매핑)
            SetMessage(string.Empty);
            RebuildTree();
            RefreshDetail();
        }

        /// <summary>선택 룬 업그레이드 요청(골드 소모). 성공 시 재로드·갱신.
        /// (POST /api/game/growth/rune/upgrade)</summary>
        private void OnUpgrade()
        {
            if (_busy || _selectedRune < 0 || NetworkManager.Instance == null || !Session.IsLoggedIn)
            {
                return;
            }
            _busy = true;
            var req = new RuneUpgradeRequest
            {
                userId = Session.UserId,
                token = Session.Token,
                data = new RuneUpgradeData { runeCode = _selectedRune },
            };
            Debug.Log($"[Rune] 업그레이드 요청 rune={_selectedRune}");
            NetworkManager.Instance.PostToGame<RuneUpgradeResponse>(
                "/api/game/growth/rune/upgrade", req,
                resp => ApplyUpgradeResult(resp != null ? resp.data : null, "룬 강화 완료"),
                OnActionError);
        }

        /// <summary>업그레이드 응답(룬 레벨·골드 잔액)으로 세션을 맞추고 UI·전투를 갱신한다.
        /// 룬 강화는 가방을 바꾸지 않으므로 반영할 것이 이 둘뿐이며, 세이브를 재조회하지 않는다.</summary>
        private void ApplyUpgradeResult(RuneUpgradeResultData data, string message)
        {
            // 강화 성공음 + 골드 차감음(사운드 정의서 §6).
            SoundManager.Sfx(SoundId.UpgradeSuccess);
            SoundManager.Sfx(SoundId.GoldSpend);

            if (data != null)
            {
                Session.ApplyRuneLevel(data.runeCode, data.level);
                Session.ApplyBalance(data.balance);
            }
            _busy = false;
            RefreshFromSession();
            SetMessage(message);
            Session.RaiseInventoryChanged(); // 룬(계정 버프) 변경 → 전투 스탯 재계산 트리거
        }

        private void OnActionError(NetworkError error)
        {
            _busy = false;
            Debug.LogWarning($"[Rune] 액션 실패: {error}");
            SetMessage(ErrorMessages.ToKorean(error));
        }

        private void SetMessage(string message)
        {
            if (_messageText != null)
            {
                _messageText.text = message ?? string.Empty;
            }
        }

        private void ClearTree()
        {
            foreach (var go in _treeChildren)
            {
                if (go != null)
                {
                    Destroy(go);
                }
            }
            _treeChildren.Clear();
        }

        /// <summary>패널을 닫는다(UIManager 우선).</summary>
        public void Close()
        {
            if (UIManager.Instance != null)
            {
                UIManager.Instance.Hide(UIManager.PanelType.Rune);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }

        // ── UI 생성 헬퍼(SkillPanelController와 동일 규약) ──

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
