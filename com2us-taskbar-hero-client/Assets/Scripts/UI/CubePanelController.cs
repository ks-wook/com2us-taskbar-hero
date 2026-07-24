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
    /// 큐브(Hero-dric Cube) 오버레이 패널(inventory-item-cube 기획서 §5.6~5.8). 인벤토리의 '큐브' 버튼으로 진입한다.
    /// 세 가지 연산 탭을 제공한다:
    /// - <b>합성</b>: 같은 등급·슬롯·클래스 장비 combine_count개를 소모해 한 등급 높은 장비 1개(서버 무작위)를 얻는다.
    /// - <b>연금술(분해)</b>: 아이템을 골드로 전환한다(gold_per_scrap × 등급 × 개수).
    /// - <b>제작</b>: 레시피(cube_recipe)의 재료·골드를 소모해 지정 아이템을 만든다.
    /// 정적 계층(제목·골드·닫기·탭·큐브 레벨바·내용 영역·실행 버튼)은 에디터 빌드 시 생성되고, 표시될 때마다
    /// 세션(<see cref="Session.GameData"/>)의 인벤토리·큐브 상태와 마스터 데이터(cube_master·cube_recipe·item_master)로
    /// 내용을 채운다. 실제 소모·지급은 서버가 최종 확정하며, 성공 후 <c>/api/game/load</c>로 재로드해 UI를 갱신한다.
    /// </summary>
    public class CubePanelController : MonoBehaviour
    {
        private enum Mode { Combine, Dismantle, Craft }

        [Header("UI 리소스 (Assets/Art/UI/Cube)")]
        [SerializeField] private Sprite panelBackground; // cube_bg
        [SerializeField] private Sprite slotNormal;
        [SerializeField] private Sprite slotHighlight;

        [Header("구성 참조 (에디터 빌더가 배선 — 직접 수정 불필요)")]
        [SerializeField] private Image _goldIcon;
        [SerializeField] private Text _goldText;
        [SerializeField] private Button _closeButton;
        [SerializeField] private Button _dimButton;
        [SerializeField] private Button _combineTab;
        [SerializeField] private Button _dismantleTab;
        [SerializeField] private Button _craftTab;
        [SerializeField] private Text _levelText;
        [SerializeField] private RectTransform _expFill;
        [SerializeField] private RectTransform _contentArea;
        [SerializeField] private Button _actionButton;
        [SerializeField] private Text _actionLabel;
        [SerializeField] private Text _footerText;
        [SerializeField] private Text _messageText;

        private const float CanvasRefWidth = 1080f;
        private const float CanvasRefHeight = 1920f;
        private const int GoldCurrencyType = 1;
        private const int GoldItemCode = 1;
        private const float ExpTrackWidth = 520f;
        private const int GridColumns = 5;

        private Font _font;
        private RectTransform _rootRect;
        private ItemIconDatabase _iconDb;
        private Mode _mode = Mode.Combine;
        private bool _busy;

        private readonly List<long> _selCombine = new List<long>();
        private readonly List<long> _selDismantle = new List<long>();
        private int _selRecipe;

        private readonly List<GameObject> _contentChildren = new List<GameObject>();

        private bool AlreadyBuilt => _contentArea != null;

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
            img.color = new Color(0f, 0f, 0f, 0.6f);
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
            return rt;
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

        /// <summary>합성/연금술/제작 탭 버튼 3개(가로 배치).</summary>
        private void BuildTabs(RectTransform container)
        {
            _combineTab = BuildTab(container, "CombineTab", "합성", 40f);
            _dismantleTab = BuildTab(container, "DismantleTab", "연금술", 296f);
            _craftTab = BuildTab(container, "CraftTab", "제작", 552f);
        }

        private Button BuildTab(RectTransform container, string name, string label, float x)
        {
            var img = NewImage(name, container, slotNormal);
            TopLeft(img.rectTransform, x, 108f, 228f, 64f);
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

        /// <summary>모드별 내용(아이템 그리드·레시피 목록)이 채워지는 영역.</summary>
        private void BuildContentArea(RectTransform container)
        {
            var bg = NewImage("ContentArea", container, null);
            bg.color = new Color(0.05f, 0.06f, 0.10f, 0.6f);
            TopLeft(bg.rectTransform, 40f, 260f, 740f, 900f);
            _contentArea = bg.rectTransform;
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
            _selCombine.Clear();
            _selDismantle.Clear();
            _selRecipe = 0;
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
            }
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
                    string name = ItemName(resp != null && resp.data != null ? resp.data.result.itemCode : 0);
                    int grade = resp != null && resp.data != null ? resp.data.result.grade : 0;
                    ShowResult("합성 완료", $"{name} (등급 {grade}) 획득!");
                    ReloadAndRefresh();
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
                    long gold = resp != null && resp.data != null ? resp.data.gold : 0;
                    ShowResult("연금술 완료", $"골드 {GoldFormat.Highlight(gold)} 획득!");
                    ReloadAndRefresh();
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
                    string gained = "제작 완료!";
                    if (resp != null && resp.data != null && resp.data.gained != null
                        && resp.data.gained.items != null && resp.data.gained.items.Count > 0)
                    {
                        var g = resp.data.gained.items[0];
                        gained = $"{ItemName(g.itemCode)} x{g.quantity} 제작!";
                    }
                    ShowResult("제작 완료", gained);
                    ReloadAndRefresh();
                },
                OnActionError);
        }

        /// <summary>액션 성공 후 세이브 스냅샷을 재로드해 세션·UI·전투를 최신화한다(선택 초기화).</summary>
        private void ReloadAndRefresh()
        {
            var req = new AuthRequest { userId = Session.UserId, token = Session.Token };
            NetworkManager.Instance.PostToGame<LoadResponse>("/api/game/load", req, resp =>
            {
                if (resp != null && resp.data != null)
                {
                    Session.SetGameData(resp.data);
                }
                _busy = false;
                _selCombine.Clear();
                _selDismantle.Clear();
                RefreshFromSession();
                Session.RaiseInventoryChanged(); // 인벤토리 변경 → 전투 스탯 재계산 트리거
            }, OnActionError);
        }

        private void OnActionError(NetworkError error)
        {
            _busy = false;
            Debug.LogWarning($"[Cube] 액션 실패: {error}");
            SetMessage(ErrorMessages.ToKorean(error));
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

        /// <summary>합성 후보: 미장착 장비(item_type=1) 중 등급 1~4(등급 5는 상위 없음).</summary>
        private static IEnumerable<InventoryItemDto> EligibleCombineItems()
        {
            var inv = Session.GameData != null ? Session.GameData.inventory : null;
            var db = MasterDataManager.Db;
            if (inv == null || db == null)
            {
                yield break;
            }
            foreach (var it in inv)
            {
                if (it == null || it.equippedCharacterId != 0)
                {
                    continue;
                }
                if (db.Items.TryGetValue(it.itemCode, out var im) && im.itemType == 1 && im.grade >= 1 && im.grade < 5)
                {
                    yield return it;
                }
            }
        }

        /// <summary>분해 후보: 미장착 아이템(장비·재료, 재화 제외).</summary>
        private static IEnumerable<InventoryItemDto> EligibleDismantleItems()
        {
            var inv = Session.GameData != null ? Session.GameData.inventory : null;
            var db = MasterDataManager.Db;
            if (inv == null || db == null)
            {
                yield break;
            }
            foreach (var it in inv)
            {
                if (it == null || it.equippedCharacterId != 0)
                {
                    continue;
                }
                if (db.Items.TryGetValue(it.itemCode, out var im) && (im.itemType == 1 || im.itemType == 2))
                {
                    yield return it;
                }
            }
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

        /// <summary>재료 코드의 계정 보유 총 수량(같은 코드 행 합산).</summary>
        private static int MaterialCount(int itemCode)
        {
            var inv = Session.GameData != null ? Session.GameData.inventory : null;
            int total = 0;
            if (inv != null)
            {
                foreach (var it in inv)
                {
                    if (it != null && it.itemCode == itemCode && it.equippedCharacterId == 0)
                    {
                        total += (int)it.quantity;
                    }
                }
            }
            return total;
        }

        private static InventoryItemDto FindInventoryItem(long itemId)
        {
            var inv = Session.GameData != null ? Session.GameData.inventory : null;
            if (inv != null)
            {
                foreach (var it in inv)
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

        /// <summary>아이템/레시피 타일 하나(등급 배경·아이콘·이름·배지·선택 프레임·클릭)를 만든다.</summary>
        private void CreateItemTile(RectTransform parent, int itemCode, string badge, bool selected, System.Action onClick)
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
