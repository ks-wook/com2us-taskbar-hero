using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TaskbarHero.Client.Managers;
using TaskbarHero.Client.Battle;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 입장 스테이지 선택 패널(2단계). 1단계: 월드맵 배경에서 5개 지역을 hover하면 노랗게 빛나고
    /// 클릭하면 지역 창이 열린다. 2단계: 지역 창에 그 지역의 스테이지 10개가 뱀 모양(위 줄 1→5, 아래 줄 6→10)
    /// 경로로 표시되어 선택·입장한다. 총 5지역 × 10스테이지 = 50스테이지(1-1 ~ 5-10, 난이도1).
    /// 진행도는 서버 세이브(maxStageCleared) 실데이터로 판정한다.
    /// 계층은 에디터 빌드 시 프리팹에 정적 저장된다. 리소스: Assets/Art/UI/Stage.
    /// </summary>
    public class StagePanelController : MonoBehaviour
    {
        private enum StageState { Cleared, Current, Locked }

        [Header("UI 리소스 (Assets/Art/UI/Stage)")]
        [SerializeField] private Sprite nodeUnlocked;   // ui_stage_node_unlocked
        [SerializeField] private Sprite nodeLocked;     // ui_stage_node_locked
        [SerializeField] private Sprite iconLock;       // ui_icon_lock
        [SerializeField] private Sprite nodeHighlight;  // ui_node_highlight
        [SerializeField] private Sprite pathConnector;  // ui_path_connector
        [SerializeField] private Sprite nameplateBar;   // ui_nameplate_bar
        [SerializeField] private Sprite mapBackground;  // dungeon_map_bg(월드맵)
        [Tooltip("지역 스테이지 창 배경(Assets/Art/UI/modal_bg.png). 없으면 단색 패널로 폴백.")]
        [SerializeField] private Sprite regionWindowBackground; // modal_bg(지역 창)
        [SerializeField] private Sprite iconCleared;    // stage_cleared(별) — 지역 전부 클리어
        [SerializeField] private Sprite iconInProgress; // stage_ing(해골) — 진행 중 지역

        [Header("구성 참조 (에디터 빌더가 배선)")]
        [SerializeField] private List<Image> _regionGlows = new List<Image>(); // 지역 hover 글로우(원형)
        [SerializeField] private List<Image> _regionStatusIcons = new List<Image>(); // 지역 상태 아이콘(클리어/진행/잠금)
        [SerializeField] private Text _regionNameLabel;            // 하단: 현재 hover 지역명
        [SerializeField] private GameObject _regionWindow;         // 지역 스테이지 창(기본 숨김)
        [SerializeField] private Text _regionTitle;
        [SerializeField] private Text _nameplateText;
        [SerializeField] private List<StageNodeView> _regionNodes = new List<StageNodeView>();
        [SerializeField] private Button _enterButton;
        // 닫기(X)·뒤로(X) 버튼은 두지 않는다(미관상 제거) — 지역 창은 창 바깥(_windowDimButton) 클릭으로 지도에 돌아가고,
        // 패널 전체는 지도 바깥(_dimButton) 클릭으로 닫는다.
        [SerializeField] private Button _windowDimButton;
        [SerializeField] private Button _dimButton;

        // 5개 지역. 각 지역 10스테이지(stage_master 기준, 10스테이지가 보스).
        private static readonly string[] RegionNames = { "평원", "얼음", "화산", "사막", "묘지" };
        // 지역 클릭 영역(cx,cy 중심 · w,h 폭·높이, 정규화). 이미지 각 지역에 대응.
        private static readonly Vector4[] RegionRects =
        {
            new Vector4(0.50f, 0.50f, 0.26f, 0.30f), // 1 평원(중앙)
            new Vector4(0.22f, 0.76f, 0.38f, 0.40f), // 2 얼음(좌상, 설원)
            new Vector4(0.80f, 0.76f, 0.38f, 0.40f), // 3 화산(우상)
            new Vector4(0.20f, 0.24f, 0.40f, 0.42f), // 4 사막(좌하)
            new Vector4(0.80f, 0.24f, 0.38f, 0.42f), // 5 묘지(우하)
        };

        // 지역당 스테이지 수. 서버 GameServer/MasterData/StageCoords.StagesPerAct(=10)·stage_master와 같은 값이어야
        // 클리어 시퀀스((지역-1)×10 + 스테이지) 판정이 서버와 일치한다.
        private const int StagesPerRegion = 10;
        private const int StageNodeColumns = 5;   // 한 줄에 5칸(위 줄 1~5, 아래 줄 6~10)
        private const int BossStage = 10;         // 각 지역 마지막(10) 스테이지가 보스

        private const float CanvasRefWidth = 1080f;
        private const float CanvasRefHeight = 1920f;
        private const float PanelWidth = 1040f;
        private const float PanelHeight = 580f;

        // 지역 창(2단계). 스테이지가 3개에서 10개로 늘어 창을 넓히고 노드를 두 줄로 나눴다.
        private const float RegionWindowWidth = 900f;
        private const float RegionWindowHeight = 600f;
        // 배경(modal_bg)은 나무 테두리가 두꺼워, 콘텐츠는 프레임 안쪽(대략 세로 0.14~0.86)에만 둔다.
        private const float NodeLeftX = 0.16f;    // 노드 줄의 좌우 끝(창 폭 정규화)
        private const float NodeRightX = 0.84f;
        private const float TopRowY = 0.66f;      // 위 줄(1~5) · 아래 줄(6~10)의 세로 위치
        private const float BottomRowY = 0.45f;
        private const float NodeSize = 84f;
        private const float BossNodeSize = 100f;  // 보스(10스테이지)는 조금 크게

        private static readonly Color DarkText = new Color(0.20f, 0.14f, 0.06f, 1f);

        private const string HoverHint = "지역에 마우스를 올리세요";

        private Font _font;
        private RectTransform _rootRect;
        private int _openRegion = 1;
        private int _selectedStage = -1;
        private int _hoveredRegion = -1;

        private bool AlreadyBuilt => _regionNodes != null && _regionNodes.Count > 0 && _regionNodes[0] != null;

        private void Awake()
        {
            _rootRect = (RectTransform)transform;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (!AlreadyBuilt)
            {
                Construct();
            }
            WireRuntime();
        }

        /// <summary>패널이 표시될 때마다 현재 진행도(세션 세이브)로 상태 아이콘을 갱신하고 지도 화면으로 되돌린다.
        /// (UIManager가 인스턴스를 캐싱·재사용하므로 Awake가 아니라 활성화 시점마다 갱신해야 최신 진행도가 반영된다.)</summary>
        private void OnEnable()
        {
            if (_regionStatusIcons != null && _regionStatusIcons.Count > 0)
            {
                RefreshRegionIcons();
            }
            CloseRegion(); // 다시 열면 지도(1단계)부터
        }

        /// <summary>에디터 빌드 전용: 전체 계층을 생성한다.</summary>
        public void EditorConstruct()
        {
            Construct();
        }

        // ── 구성 ──

        private void Construct()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            BuildCanvas();
            BuildDim();
            BuildMapPanel();
            BuildRegionWindow();
        }

        private void BuildCanvas()
        {
            var canvas = gameObject.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = gameObject.AddComponent<Canvas>();
            }
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // 하단 HUD(10)보다 아래에 둬서 스테이지 선택 중에도 하단 아이콘 줄이 가려지지 않게 한다
            // (클릭도 sortingOrder가 높은 HUD가 먼저 받으므로 딤이 버튼을 가로채지 않는다).
            canvas.sortingOrder = UiSortingOrder.PanelBelowHud;

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

        /// <summary>1단계: 월드맵 배경 + 5개 지역 hover/클릭 핫스팟.</summary>
        private void BuildMapPanel()
        {
            var panel = NewImage("PanelRoot", _rootRect, mapBackground);
            var prt = panel.rectTransform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            prt.anchoredPosition = Vector2.zero;

            var content = NewRect("MapContent", prt);
            content.anchorMin = Vector2.zero;
            content.anchorMax = Vector2.one;
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;

            // 제목(지도 위). 글씨만 두면 배경 위에서 읽기 어려워 이름표 바(하단 지역명 바·스테이지 이름표와 같은 아트)를
            // 깔고 그 위에 텍스트를 올린다. 닫기(X) 버튼은 두지 않는다 — 지도 아트 위에 얹히면 미관을 해쳐 제거했고,
            // 닫기는 딤(바깥 영역) 클릭이 담당한다.
            var titleBar = NewImage("TitleBar", content, nameplateBar);
            titleBar.raycastTarget = false;
            var trt = titleBar.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 1f);
            trt.pivot = new Vector2(0.5f, 0f);
            trt.anchoredPosition = new Vector2(0f, 10f);
            trt.sizeDelta = new Vector2(360f, 64f);
            var title = NewText("Title", titleBar.rectTransform, "지역 선택", 36, TextAnchor.MiddleCenter);
            Stretch(title.rectTransform);

            // 지역 핫스팟 5개
            for (int r = 0; r < RegionRects.Length; r++)
            {
                BuildRegionHotspot(content, r + 1, RegionRects[r]);
            }

            // 하단: 현재 hover 지역명 표시 바
            var bar = NewImage("RegionNameBar", content, nameplateBar);
            var brt = bar.rectTransform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0f);
            brt.pivot = new Vector2(0.5f, 1f);
            brt.anchoredPosition = new Vector2(0f, -16f);
            brt.sizeDelta = new Vector2(420f, 60f);
            _regionNameLabel = NewText("RegionNameLabel", bar.rectTransform, HoverHint, 28, TextAnchor.MiddleCenter);
            Stretch(_regionNameLabel.rectTransform);
        }

        /// <summary>지역 클릭 영역 + 원형 노란 글로우(hover). 중앙 텍스트는 두지 않는다.</summary>
        private void BuildRegionHotspot(RectTransform content, int region, Vector4 rect)
        {
            var area = NewImage($"Region{region}", content, null);
            area.color = new Color(1f, 1f, 1f, 0f); // 투명(레이캐스트 전용)
            area.raycastTarget = true;
            PlaceBox(area.rectTransform, rect.x, rect.y, rect.z, rect.w);

            // 원형 글로우: 정사각형(원 유지) + 런타임 라디얼 스프라이트. 중앙 앵커 고정 크기.
            var glow = NewImage("Glow", area.rectTransform, null);
            glow.color = new Color(1f, 0.88f, 0.20f, 0.75f);
            glow.raycastTarget = false;
            var grt = glow.rectTransform;
            grt.anchorMin = grt.anchorMax = new Vector2(0.5f, 0.5f);
            grt.pivot = new Vector2(0.5f, 0.5f);
            grt.sizeDelta = new Vector2(340f, 340f);
            grt.anchoredPosition = Vector2.zero;
            glow.gameObject.SetActive(false);
            _regionGlows.Add(glow);

            // 지역 상태 아이콘(클리어/진행/잠금) — 지역 중앙, 클릭은 통과.
            var icon = NewImage("StatusIcon", area.rectTransform, null);
            icon.raycastTarget = false;
            var irt = icon.rectTransform;
            irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 0.5f);
            irt.pivot = new Vector2(0.5f, 0.5f);
            irt.sizeDelta = new Vector2(64f, 64f);
            irt.anchoredPosition = Vector2.zero;
            _regionStatusIcons.Add(icon);

            // 아이콘 하단: "N지역" 이름표
            var plate = NewImage("RegionNumPlate", area.rectTransform, nameplateBar);
            plate.raycastTarget = false;
            var plrt = plate.rectTransform;
            plrt.anchorMin = plrt.anchorMax = new Vector2(0.5f, 0.5f);
            plrt.pivot = new Vector2(0.5f, 1f); // 위쪽 피벗 → 아이콘 아래로 배치
            plrt.sizeDelta = new Vector2(96f, 34f);
            plrt.anchoredPosition = new Vector2(0f, -38f);
            var numText = NewText("RegionNumText", plate.rectTransform, $"{region}지역", 22, TextAnchor.MiddleCenter);
            Stretch(numText.rectTransform);

            var hs = area.gameObject.AddComponent<StageRegionHotspot>();
            hs.EditorInit(region, glow.gameObject);
        }

        /// <summary>2단계: 지역 스테이지 창(딤 + 패널 + 10노드 + 경로 + 이름표 + 입장/뒤로).
        /// 노드는 5칸씩 두 줄의 뱀 모양 경로(위 줄 1→5, 아래 줄 6→10)로 배치하고, 두 줄은 우측 세로 경로로 잇는다.</summary>
        private void BuildRegionWindow()
        {
            _regionWindow = NewRect("RegionWindow", _rootRect).gameObject;
            var winRt = (RectTransform)_regionWindow.transform;
            winRt.anchorMin = Vector2.zero;
            winRt.anchorMax = Vector2.one;
            winRt.offsetMin = Vector2.zero;
            winRt.offsetMax = Vector2.zero;

            var dim = NewImage("WinDim", winRt, null);
            dim.color = new Color(0f, 0f, 0f, 0f); // 배경을 어둡게 하지 않는다 — 밖 클릭(지도 복귀)용 투명 차단막
            Stretch(dim.rectTransform);
            _windowDimButton = dim.gameObject.AddComponent<Button>();
            _windowDimButton.transition = Selectable.Transition.None;

            // 배경: 공용 모달 배경(modal_bg)을 그대로 써 다른 팝업과 톤을 맞춘다.
            // 테두리 값이 없는 텍스처라 9-slice가 아니라 Simple로 늘려 쓴다(공용 모달 ModalController와 동일 취급).
            var panel = NewImage("WinPanel", winRt, regionWindowBackground);
            panel.type = Image.Type.Simple;
            panel.color = regionWindowBackground != null
                ? Color.white
                : new Color(0.10f, 0.12f, 0.18f, 0.98f); // 아트 미배선 시 단색 폴백
            var prt = panel.rectTransform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(RegionWindowWidth, RegionWindowHeight);
            prt.anchoredPosition = Vector2.zero;

            var content = NewRect("WinContent", prt);
            content.anchorMin = Vector2.zero;
            content.anchorMax = Vector2.one;
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;

            // 지역 이름표(바 + 텍스트). 지도 제목과 같은 아트를 써 두 화면의 제목 표기를 통일한다.
            // 지도로 돌아가는 X(뒤로) 버튼은 두지 않는다(미관상 제거) — 창 바깥(WinDim) 클릭이 지도 복귀를 담당한다.
            var titleBar = NewImage("RegionTitleBar", content, nameplateBar);
            titleBar.raycastTarget = false;
            PlaceCenter(titleBar.rectTransform, 0.5f, 0.84f, 400f, 64f);
            _regionTitle = NewText("RegionTitle", titleBar.rectTransform, "", 32, TextAnchor.MiddleCenter);
            Stretch(_regionTitle.rectTransform);

            BuildStageNodes(content);

            var plate = NewImage("Nameplate", content, nameplateBar);
            PlaceCenter(plate.rectTransform, 0.5f, 0.28f, 440f, 60f);
            _nameplateText = NewText("NameplateText", plate.rectTransform, "", 28, TextAnchor.MiddleCenter);
            Stretch(_nameplateText.rectTransform);

            var enter = NewImage("EnterButton", content, nodeUnlocked);
            PlaceCenter(enter.rectTransform, 0.5f, 0.16f, 220f, 66f);
            var el = NewText("EnterLabel", enter.rectTransform, "입장", 30, TextAnchor.MiddleCenter);
            el.color = DarkText;
            Stretch(el.rectTransform);
            _enterButton = enter.gameObject.AddComponent<Button>();

            _regionWindow.SetActive(false);
        }

        /// <summary>
        /// 지역 창의 스테이지 노드 10개와 경로를 뱀 모양으로 배치한다 —
        /// 위 줄에 1→5(좌→우), 아래 줄에 6→10(우→좌)을 두고, 두 줄은 우측 끝 세로 경로로 잇는다.
        /// 경로선은 노드보다 먼저 만들어 노드 아래에 깔리게 한다(같은 부모에서는 자식 순서 = 그리기 순서).
        /// </summary>
        private void BuildStageNodes(RectTransform content)
        {
            // 줄 안 노드가 5개라 좌우 여백을 남기고 균등 배치한다.
            var fx = new float[StageNodeColumns];
            for (int c = 0; c < StageNodeColumns; c++)
            {
                fx[c] = NodeLeftX + (NodeRightX - NodeLeftX) * c / (StageNodeColumns - 1);
            }

            // 각 줄의 인접 노드를 잇는 수평 경로 + 위/아래 줄을 잇는 우측 세로 경로.
            int path = 0;
            for (int c = 0; c < StageNodeColumns - 1; c++)
            {
                BuildWindowConnector(content, fx[c], fx[c + 1], TopRowY, path++);
                BuildWindowConnector(content, fx[c], fx[c + 1], BottomRowY, path++);
            }
            BuildWindowConnectorVertical(content, fx[StageNodeColumns - 1], BottomRowY, TopRowY, path);

            for (int i = 0; i < StagesPerRegion; i++)
            {
                int stage = i + 1;
                bool topRow = i < StageNodeColumns;
                // 아래 줄은 오른쪽에서 왼쪽으로 진행한다(6번이 5번 바로 아래).
                int col = topRow ? i : StagesPerRegion - 1 - i;
                float fy = topRow ? TopRowY : BottomRowY;
                float size = stage == BossStage ? BossNodeSize : NodeSize; // 보스(10)는 조금 크게

                var nodeGo = NewImage($"StageNode{stage}", content, nodeUnlocked);
                PlaceCenter(nodeGo.rectTransform, fx[col], fy, size, size);

                var hl = NewImage("Highlight", nodeGo.rectTransform, nodeHighlight);
                hl.raycastTarget = false;
                var hrt = hl.rectTransform;
                hrt.anchorMin = Vector2.zero;
                hrt.anchorMax = Vector2.one;
                hrt.offsetMin = new Vector2(-10f, -10f);
                hrt.offsetMax = new Vector2(10f, 10f);
                hl.gameObject.SetActive(false);

                var num = NewText("Num", nodeGo.rectTransform, stage.ToString(), 28, TextAnchor.MiddleCenter);
                num.color = DarkText;
                num.raycastTarget = false;
                Stretch(num.rectTransform);

                var lockImg = NewImage("Lock", nodeGo.rectTransform, iconLock);
                lockImg.raycastTarget = false;
                var lrt = lockImg.rectTransform;
                lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0.5f);
                lrt.pivot = new Vector2(0.5f, 0.5f);
                lrt.sizeDelta = new Vector2(40f, 40f);
                lrt.anchoredPosition = Vector2.zero;
                lockImg.gameObject.SetActive(false);

                var view = nodeGo.gameObject.AddComponent<StageNodeView>();
                view.EditorInit(stage, nodeGo, lockImg.gameObject, hl.gameObject);
                _regionNodes.Add(view);
            }
        }

        /// <summary>지역 창의 인접 노드 사이 수평 경로 연결선.</summary>
        private void BuildWindowConnector(RectTransform content, float fx1, float fx2, float fy, int idx)
        {
            var img = NewImage($"Path{idx}", content, pathConnector);
            img.type = Image.Type.Tiled;
            img.raycastTarget = false;
            PlaceBox(img.rectTransform, (fx1 + fx2) * 0.5f, fy, fx2 - fx1, 0.045f);
        }

        /// <summary>위/아래 줄을 잇는 세로 경로 연결선. 가로 타일 스프라이트를 90° 돌려 쓴다.</summary>
        private void BuildWindowConnectorVertical(RectTransform content, float fx, float fy1, float fy2, int idx)
        {
            var img = NewImage($"Path{idx}", content, pathConnector);
            img.type = Image.Type.Tiled;
            img.raycastTarget = false;
            var rt = img.rectTransform;
            // 회전은 크기 계산 뒤 적용해야 하므로, 먼저 '가로 막대'로 잡고 90° 돌린다.
            PlaceCenter(rt, fx, (fy1 + fy2) * 0.5f, RegionWindowHeight * (fy2 - fy1), RegionWindowHeight * 0.045f);
            rt.localRotation = Quaternion.Euler(0f, 0f, 90f);
        }

        // ── 런타임 배선 ──

        private void WireRuntime()
        {
            if (_dimButton != null)
            {
                _dimButton.onClick.AddListener(Close);
            }
            if (_windowDimButton != null)
            {
                _windowDimButton.onClick.AddListener(CloseRegion);
            }
            if (_enterButton != null)
            {
                _enterButton.onClick.AddListener(OnEnter);
            }
            if (_regionWindow != null)
            {
                _regionWindow.SetActive(false); // 시작은 지도 화면
            }

            // 원형 라디얼 글로우 스프라이트를 런타임 생성해 모든 지역 글로우에 배정.
            var radial = MakeRadialGlowSprite(128);
            foreach (var glow in _regionGlows)
            {
                if (glow != null)
                {
                    glow.sprite = radial;
                    glow.type = Image.Type.Simple;
                }
            }

            if (_regionNameLabel != null)
            {
                _regionNameLabel.text = HoverHint;
            }
            _hoveredRegion = -1;

            RefreshRegionIcons();
        }

        /// <summary>각 지역의 상태 아이콘(클리어=별 / 진행=해골 / 잠금=자물쇠)을 갱신한다.</summary>
        private void RefreshRegionIcons()
        {
            for (int r = 1; r <= _regionStatusIcons.Count; r++)
            {
                var icon = _regionStatusIcons[r - 1];
                if (icon == null)
                {
                    continue;
                }
                switch (RegionStatus(r))
                {
                    case StageState.Cleared:
                        icon.sprite = iconCleared;
                        break;
                    case StageState.Current:
                        icon.sprite = iconInProgress;
                        break;
                    default:
                        icon.sprite = iconLock;
                        break;
                }
                icon.enabled = icon.sprite != null;
            }
        }

        /// <summary>지역 전체 상태: 10스테이지 모두 클리어=Cleared, 잠기지 않은 스테이지가 있으면 Current, 아니면 Locked.</summary>
        private StageState RegionStatus(int region)
        {
            bool allCleared = true;
            bool anyOpen = false;
            for (int s = 1; s <= StagesPerRegion; s++)
            {
                var st = StageStateOf(region, s);
                if (st != StageState.Cleared)
                {
                    allCleared = false;
                }
                if (st != StageState.Locked)
                {
                    anyOpen = true;
                }
            }
            if (allCleared)
            {
                return StageState.Cleared;
            }
            return anyOpen ? StageState.Current : StageState.Locked;
        }

        /// <summary>지역 hover 시 하단 지역명 바를 갱신한다(핫스팟에서 호출).</summary>
        public void OnRegionHover(int region, bool entered)
        {
            if (entered)
            {
                _hoveredRegion = region;
                if (_regionNameLabel != null)
                {
                    _regionNameLabel.text = $"{RegionNames[region - 1]} 지역";
                }
            }
            else if (_hoveredRegion == region)
            {
                _hoveredRegion = -1;
                if (_regionNameLabel != null)
                {
                    _regionNameLabel.text = HoverHint;
                }
            }
        }

        /// <summary>중앙이 진하고 바깥으로 갈수록 투명해지는 원형 글로우 스프라이트를 생성한다.</summary>
        private static Sprite MakeRadialGlowSprite(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(half, half)) / half;
                    float a = Mathf.Clamp01(1f - d);
                    a *= a; // 바깥으로 갈수록 더 빠르게 투명
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>지역 창을 열고 그 지역의 10스테이지 상태를 채운다.</summary>
        public void OpenRegion(int region)
        {
            _openRegion = region;
            if (_regionTitle != null)
            {
                _regionTitle.text = $"{RegionNames[region - 1]} 지역";
            }

            int frontier = -1;
            foreach (var node in _regionNodes)
            {
                bool locked = StageStateOf(region, node.Stage) == StageState.Locked;
                node.SetVisual(locked ? nodeLocked : nodeUnlocked, locked);
                if (!locked)
                {
                    frontier = node.Stage;
                }
            }

            SetSelected(frontier);
            if (_regionWindow != null)
            {
                _regionWindow.SetActive(true);
            }
        }

        /// <summary>지역 창을 닫고 지도 화면으로 돌아간다.</summary>
        public void CloseRegion()
        {
            if (_regionWindow != null)
            {
                _regionWindow.SetActive(false);
            }
        }

        private void SetSelected(int stage)
        {
            _selectedStage = stage;
            foreach (var node in _regionNodes)
            {
                node.SetHighlight(node.Stage == stage);
            }

            if (stage < 0)
            {
                if (_nameplateText != null)
                {
                    _nameplateText.text = "미해금";
                }
                if (_enterButton != null)
                {
                    _enterButton.interactable = false;
                }
                return;
            }

            if (_nameplateText != null)
            {
                // 각 지역의 10스테이지는 보스전이라 이름표에 함께 표시한다(stage_master의 boss_monster_code).
                _nameplateText.text = stage == BossStage
                    ? $"{_openRegion}-{stage}  보스"
                    : $"{_openRegion}-{stage}";
            }
            if (_enterButton != null)
            {
                _enterButton.interactable = StageStateOf(_openRegion, stage) != StageState.Locked;
            }
        }

        /// <summary>지역 창 노드 클릭: 잠기지 않은 스테이지면 선택.</summary>
        public void OnNodeClicked(StageNodeView node)
        {
            if (StageStateOf(_openRegion, node.Stage) != StageState.Locked)
            {
                SetSelected(node.Stage);
            }
        }

        /// <summary>입장: 선택한 스테이지(지역=act, 난이도 1, 스테이지)를 던전 전투에 "처음부터" 입장시킨다.
        /// GameScene의 <see cref="DungeonBattleFlow"/>가 서버로 stage/enter 요청을 보내고 전투 필드를 리셋해 시작한다.
        /// 요청을 위임한 뒤 스테이지 패널을 닫는다.</summary>
        private void OnEnter()
        {
            if (_selectedStage < 0)
            {
                return;
            }

            var flow = FindAnyObjectByType<DungeonBattleFlow>();
            if (flow == null)
            {
                Debug.LogWarning($"[Stage] 던전 전투(DungeonBattleFlow)를 찾을 수 없어 입장하지 못했습니다: {_openRegion}-{_selectedStage}");
                return;
            }

            // UI는 난이도 1(시퀀스 1~50)만 노출한다. 지역=act, 선택=stage로 매핑.
            Debug.Log($"[Stage] 입장 요청: {_openRegion}-{_selectedStage} (처음부터)");
            flow.EnterSelectedStage(_openRegion, 1, _selectedStage);
            Close();
        }

        public void Close()
        {
            if (UIManager.Instance != null)
            {
                UIManager.Instance.Hide(UIManager.PanelType.Stage);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }

        // ── 진행도(서버 세이브 실데이터: 클리어 시퀀스) ──

        /// <summary>세션에 캐싱된 세이브의 최고 클리어 시퀀스(maxStageCleared). 세션이 없으면 0(1-1만 진행 가능).</summary>
        private int MaxClearedSeq()
        {
            var p = Session.GameData != null ? Session.GameData.player : null;
            return p != null ? p.maxStageCleared : 0;
        }

        /// <summary>스테이지 상태를 서버 진행도로 판정한다.
        /// 난이도1 기준 시퀀스 = (지역-1)×10 + 스테이지(1~50). 클리어=seq≤maxCleared, 진행 중(프런티어)=seq==maxCleared+1, 그 외 잠금.
        /// (서버 EnterAsync의 도달 검증 규칙 seq≤maxCleared+1과 동일.)</summary>
        private StageState StageStateOf(int region, int stage)
        {
            int seq = (region - 1) * StagesPerRegion + stage;
            int maxCleared = MaxClearedSeq();
            if (seq <= maxCleared)
            {
                return StageState.Cleared;
            }
            if (seq == maxCleared + 1)
            {
                return StageState.Current;
            }
            return StageState.Locked;
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

        /// <summary>부모 정규화 좌표(fx,fy)에 중앙 앵커로 고정 크기 배치.</summary>
        private static void PlaceCenter(RectTransform rt, float fx, float fy, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(fx, fy);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(w, h);
        }

        /// <summary>부모 정규화 박스(중심 cx,cy · 크기 w,h)로 앵커 배치(크기 변경에 강함).</summary>
        private static void PlaceBox(RectTransform rt, float cx, float cy, float w, float h)
        {
            rt.anchorMin = new Vector2(cx - w * 0.5f, cy - h * 0.5f);
            rt.anchorMax = new Vector2(cx + w * 0.5f, cy + h * 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
