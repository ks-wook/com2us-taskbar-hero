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
        [Tooltip("지역 스테이지 창 기본 배경(Assets/Art/UI/modal_bg.png). 지역 배경이 없을 때만 쓰는 폴백.")]
        [SerializeField] private Sprite regionWindowBackground; // modal_bg(지역 창 폴백)
        [Tooltip("지역별 스테이지 창 배경(Assets/Art/Background/stage_ui_bg/stage_ui_bg_1~5.png). " +
                 "인덱스 0 = 1지역(평원) … 4 = 5지역(묘지). 비어 있으면 위 기본 배경으로 폴백한다.")]
        [SerializeField] private Sprite[] regionBackgrounds = new Sprite[0];
        [Tooltip("지역 창 테두리(Assets/Art/UI/Trade/01_Frames_Panels/window_frame_hollow.png, 9-slice). " +
                 "가운데가 비어 있어 배경 위에 액자처럼 얹힌다. 없으면 테두리 없이 배경만 보인다.")]
        [SerializeField] private Sprite regionWindowFrame;
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
        [Tooltip("지역 창 배경 이미지. 지역을 열 때 그 지역 배경으로 갈아 끼운다.")]
        [SerializeField] private Image _windowPanel;
        [Tooltip("노드 사이 경로선(9개 = 10노드를 잇는 한 줄 경로). 지역마다 위치·길이·각도를 다시 잡는다.")]
        [SerializeField] private List<Image> _pathConnectors = new List<Image>();
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
        private const int BossStage = 10;         // 각 지역 마지막(10) 스테이지가 보스
        private const int PathCount = StagesPerRegion - 1; // 10노드를 한 줄로 잇는 경로선 수(9)

        private const float CanvasRefWidth = 1080f;
        private const float CanvasRefHeight = 1920f;
        private const float PanelWidth = 1040f;
        private const float PanelHeight = 580f;

        // 지역 창(2단계). 배경이 지역별 풍경 아트(stage_ui_bg_*, 2816×1536)로 바뀌었으므로
        // 창도 그 비율(1.833:1)에 맞춰 잡는다 — 비율이 어긋나면 풍경이 늘어난다.
        // 폭은 우측 패널 여백(GameViewLayout.WidestRightPanel = 1040) 안에 들어가는 값이다.
        private const float RegionWindowWidth = 990f;
        private const float RegionWindowHeight = 540f;   // 990 / 540 = 1.8333 = 2816 / 1536
        private const float NodeSize = 76f;
        private const float BossNodeSize = 92f;   // 보스(10스테이지)는 조금 크게
        private const float PathThickness = 14f;  // 노드를 잇는 경로선 굵기(px)

        // 테두리(액자) 두께(UI px). 원본 9-slice 경계가 20px이라 그대로 쓰면 990×540 창에서 지나치게 얇다.
        // 실제 배율은 BuildRegionFrame이 원본 경계 두께에서 계산한다(아트가 바뀌어도 두께가 유지된다).
        private const float FrameThickness = 40f;

        /// <summary>
        /// 지역별 스테이지 노드 배치(창 기준 정규화 좌표, <b>y는 아래에서부터</b>). 지역마다 10개.
        ///
        /// <para><b>왜 지역마다 다른가</b> — 전에는 다섯 지역이 모두 같은 'ㄷ' 자(5칸 두 줄 뱀 모양)였다.
        /// 배경이 지역별 풍경으로 바뀌었으므로 경로도 그 풍경을 따라가게 해 지역마다 다른 곳이라는 인상을 준다.</para>
        ///
        /// <para><b>배경 구도를 피해서 잡은 좌표다</b> — 위쪽(y &gt; 0.86)은 제목 이름표, 아래쪽(y &lt; 0.18)은
        /// 스테이지 이름표·입장 버튼이 쓰므로 노드는 그 사이 띠에 둔다. 지역별로는
        /// ③ 화산의 <b>가운데 분화구</b>(밝아서 노드가 묻힌다)를 비켜 둘레를 오르고,
        /// ② 얼음은 오른쪽 <b>오두막</b>, ④ 사막은 오른쪽 <b>오아시스</b>가 보스 자리가 되도록 끝점을 맞췄다.</para>
        /// </summary>
        private static readonly Vector2[][] RegionNodeLayouts =
        {
            // 1 평원 — 완만한 S 곡선(강과 흙길을 따라 오르는 흐름)
            new[]
            {
                new Vector2(0.10f, 0.29f), new Vector2(0.21f, 0.23f), new Vector2(0.33f, 0.27f),
                new Vector2(0.43f, 0.36f), new Vector2(0.50f, 0.48f), new Vector2(0.44f, 0.60f),
                new Vector2(0.34f, 0.70f), new Vector2(0.46f, 0.77f), new Vector2(0.62f, 0.78f),
                new Vector2(0.78f, 0.70f),
            },
            // 2 얼음 — 지그재그 계단(설원을 오르내리며 오른쪽 오두막까지)
            new[]
            {
                new Vector2(0.12f, 0.24f), new Vector2(0.26f, 0.34f), new Vector2(0.14f, 0.44f),
                new Vector2(0.28f, 0.54f), new Vector2(0.16f, 0.64f), new Vector2(0.32f, 0.72f),
                new Vector2(0.48f, 0.66f), new Vector2(0.62f, 0.72f), new Vector2(0.74f, 0.62f),
                new Vector2(0.84f, 0.48f),
            },
            // 3 화산 — 분화구 둘레를 돌아 정상으로(가운데 밝은 화구를 피해 아래→왼쪽 사면→정상)
            new[]
            {
                new Vector2(0.86f, 0.24f), new Vector2(0.72f, 0.23f), new Vector2(0.56f, 0.23f),
                new Vector2(0.40f, 0.24f), new Vector2(0.24f, 0.27f), new Vector2(0.12f, 0.36f),
                new Vector2(0.16f, 0.51f), new Vector2(0.26f, 0.61f), new Vector2(0.36f, 0.70f),
                new Vector2(0.50f, 0.77f),
            },
            // 4 사막 — 모래언덕 물결(좌→우 파형)로 오른쪽 오아시스까지
            new[]
            {
                new Vector2(0.10f, 0.30f), new Vector2(0.19f, 0.40f), new Vector2(0.28f, 0.48f),
                new Vector2(0.38f, 0.42f), new Vector2(0.46f, 0.32f), new Vector2(0.55f, 0.27f),
                new Vector2(0.64f, 0.32f), new Vector2(0.71f, 0.43f), new Vector2(0.77f, 0.55f),
                new Vector2(0.86f, 0.66f),
            },
            // 5 묘지 — 바깥에서 안으로 감기는 나선(묘지를 헤매다 가운데에서 보스와 마주친다)
            new[]
            {
                new Vector2(0.14f, 0.28f), new Vector2(0.30f, 0.23f), new Vector2(0.50f, 0.22f),
                new Vector2(0.70f, 0.25f), new Vector2(0.84f, 0.36f), new Vector2(0.80f, 0.53f),
                new Vector2(0.66f, 0.64f), new Vector2(0.48f, 0.70f), new Vector2(0.34f, 0.60f),
                new Vector2(0.46f, 0.46f),
            },
        };

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
            prt.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            // 화면 중앙이 아니라 전투 화면 오른쪽 옆에 일정 간격(GameViewLayout.PanelGap)을 두고 붙인다 — 전투를 가리지 않는다.
            SidePanel.Attach(prt, SidePanel.Side.Right);

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

            // 배경: 지역을 열 때 그 지역 풍경 아트로 갈아 끼운다(ApplyRegionLayout). 여기서는 폴백을 넣어 둔다.
            // 창 비율을 배경 아트 비율(1.833:1)에 맞춰 두었으므로 Simple로 늘려도 풍경이 왜곡되지 않는다.
            var panel = NewImage("WinPanel", winRt, regionWindowBackground);
            panel.type = Image.Type.Simple;
            panel.color = regionWindowBackground != null
                ? Color.white
                : new Color(0.10f, 0.12f, 0.18f, 0.98f); // 아트 미배선 시 단색 폴백
            _windowPanel = panel;
            var prt = panel.rectTransform;
            prt.sizeDelta = new Vector2(RegionWindowWidth, RegionWindowHeight);
            // 지도(1단계)와 같은 쪽(오른쪽)에 붙여 두 단계가 같은 자리에서 열리게 한다.
            SidePanel.Attach(prt, SidePanel.Side.Right);

            var content = NewRect("WinContent", prt);
            content.anchorMin = Vector2.zero;
            content.anchorMax = Vector2.one;
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;

            // 지역 이름표(바 + 텍스트). 지도 제목과 같은 아트를 써 두 화면의 제목 표기를 통일한다.
            // 지도로 돌아가는 X(뒤로) 버튼은 두지 않는다(미관상 제거) — 창 바깥(WinDim) 클릭이 지도 복귀를 담당한다.
            // 그리는 순서 = 자식 순서다. 경로·노드(아래) → 테두리 → 제목·이름표·입장(위) 순으로 만들어,
            // 테두리가 노드를 덮되 제목·이름표는 테두리 위에 얹혀 <b>액자에 걸린 명패</b>처럼 보이게 한다.
            BuildStageNodes(content);
            BuildRegionFrame(content);

            // 제목 명패는 테두리 위쪽 가장자리에 걸친다(테두리보다 나중에 그려 위에 온다).
            var titleBar = NewImage("RegionTitleBar", content, nameplateBar);
            titleBar.raycastTarget = false;
            PlaceCenter(titleBar.rectTransform, 0.5f, 0.94f, 400f, 60f);
            _regionTitle = NewText("RegionTitle", titleBar.rectTransform, "", 32, TextAnchor.MiddleCenter);
            Stretch(_regionTitle.rectTransform);

            // 스테이지 이름표와 입장 버튼은 창 맨 아래에 <b>나란히</b> 둔다.
            // 위아래로 쌓으면 노드가 쓸 세로 공간을 두 줄이나 먹어 지역별 경로를 그릴 자리가 없다.
            var plate = NewImage("Nameplate", content, nameplateBar);
            PlaceCenter(plate.rectTransform, 0.34f, 0.09f, 400f, 56f);
            _nameplateText = NewText("NameplateText", plate.rectTransform, "", 26, TextAnchor.MiddleCenter);
            Stretch(_nameplateText.rectTransform);

            var enter = NewImage("EnterButton", content, nodeUnlocked);
            PlaceCenter(enter.rectTransform, 0.74f, 0.09f, 200f, 62f);
            var el = NewText("EnterLabel", enter.rectTransform, "입장", 30, TextAnchor.MiddleCenter);
            el.color = DarkText;
            Stretch(el.rectTransform);
            _enterButton = enter.gameObject.AddComponent<Button>();

            _regionWindow.SetActive(false);
        }

        /// <summary>
        /// 지역 창의 스테이지 노드 10개와 경로선 9개를 만든다.
        /// <para>위치는 여기서 정하지 않는다 — 지역마다 다르므로 <see cref="ApplyRegionLayout"/>이 지역을 열 때
        /// <see cref="RegionNodeLayouts"/>로 다시 잡는다. 여기서는 개수만큼 만들어 두고 참조를 배선한다.</para>
        /// 경로선은 노드보다 먼저 만들어 노드 아래에 깔리게 한다(같은 부모에서는 자식 순서 = 그리기 순서).
        /// </summary>
        private void BuildStageNodes(RectTransform content)
        {
            _pathConnectors.Clear();
            for (int i = 0; i < PathCount; i++)
            {
                var img = NewImage($"Path{i}", content, pathConnector);
                img.type = Image.Type.Simple;   // 각도가 있는 경로라 타일링하지 않고 늘려 쓴다
                img.raycastTarget = false;
                _pathConnectors.Add(img);
            }

            for (int i = 0; i < StagesPerRegion; i++)
            {
                int stage = i + 1;
                float size = stage == BossStage ? BossNodeSize : NodeSize; // 보스(10)는 조금 크게

                var nodeGo = NewImage($"StageNode{stage}", content, nodeUnlocked);
                PlaceCenter(nodeGo.rectTransform, 0.5f, 0.5f, size, size); // 실제 위치는 ApplyRegionLayout이 잡는다

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

        /// <summary>
        /// 지역 창 테두리(액자)를 만든다. 배경 아트가 프레임 없는 풍경 그림이라 창 경계가 화면에 그냥 잘려
        /// 보이므로, <b>가운데가 빈 9-slice 프레임</b>을 배경 위에 얹어 창의 경계를 만든다.
        /// <para>가운데는 그리지 않고(<c>fillCenter = false</c>) 클릭도 받지 않으므로(노드·버튼이 받아야 한다)
        /// 순수한 장식이다. 두께는 <see cref="FrameThickness"/>가 되도록 <c>pixelsPerUnitMultiplier</c>를 계산한다
        /// (이 값은 <b>낮출수록 경계가 두꺼워진다</b>).</para>
        /// </summary>
        private void BuildRegionFrame(RectTransform content)
        {
            if (regionWindowFrame == null)
            {
                return; // 아트가 없으면 테두리 없이 배경만 보인다(기존 동작)
            }
            var frame = NewImage("WinFrame", content, regionWindowFrame);
            frame.type = Image.Type.Sliced;
            frame.fillCenter = false;          // 가운데(배경·노드)를 덮지 않는다
            float srcBorder = Mathf.Max(regionWindowFrame.border.x, 1f); // 원본 9-slice 경계(20px)
            frame.pixelsPerUnitMultiplier = srcBorder / FrameThickness;
            frame.raycastTarget = false;        // 클릭은 아래의 노드·버튼이 받는다
            frame.color = Color.white;
            Stretch(frame.rectTransform);
        }

        /// <summary>
        /// 지역에 맞는 배경과 노드·경로 배치를 적용한다(지역을 열 때마다 호출).
        /// <para>노드 10개는 <see cref="RegionNodeLayouts"/>의 좌표로 옮기고, 경로선 9개는 인접 노드를 잇도록
        /// 중점·길이·각도를 다시 잡는다(대각선 경로를 쓰므로 회전이 필요하다). 오브젝트를 만들거나 버리지 않고
        /// 이미 있는 것을 다시 배치하므로, 지역을 여닫아도 쓰레기가 생기지 않는다.</para>
        /// </summary>
        private void ApplyRegionLayout(int region)
        {
            // 배경: 지역 아트가 있으면 갈아 끼우고, 없으면 기본(모달) 배경으로 폴백한다.
            if (_windowPanel != null)
            {
                var bg = regionBackgrounds != null && region >= 1 && region <= regionBackgrounds.Length
                    ? regionBackgrounds[region - 1]
                    : null;
                if (bg == null)
                {
                    bg = regionWindowBackground;
                }
                _windowPanel.sprite = bg;
                _windowPanel.color = bg != null ? Color.white : new Color(0.10f, 0.12f, 0.18f, 0.98f);
            }

            var layout = LayoutFor(region);
            foreach (var node in _regionNodes)
            {
                if (node == null)
                {
                    continue;
                }
                int index = Mathf.Clamp(node.Stage - 1, 0, layout.Length - 1);
                float size = node.Stage == BossStage ? BossNodeSize : NodeSize;
                PlaceCenter((RectTransform)node.transform, layout[index].x, layout[index].y, size, size);
            }

            for (int i = 0; i < _pathConnectors.Count; i++)
            {
                var img = _pathConnectors[i];
                if (img == null)
                {
                    continue;
                }
                if (i + 1 >= layout.Length)
                {
                    img.gameObject.SetActive(false);
                    continue;
                }
                img.gameObject.SetActive(true);
                PlacePath(img.rectTransform, layout[i], layout[i + 1]);
            }
        }

        /// <summary>지역 번호(1~5)의 노드 배치. 표에 없는 번호는 1지역 배치로 폴백한다.</summary>
        private static Vector2[] LayoutFor(int region)
        {
            int index = region - 1;
            return index >= 0 && index < RegionNodeLayouts.Length
                ? RegionNodeLayouts[index]
                : RegionNodeLayouts[0];
        }

        /// <summary>
        /// 두 노드(정규화 좌표)를 잇는 경로선을 배치한다 — 중점에 놓고 길이는 두 점 거리, 각도는 두 점의 기울기다.
        /// 창 중앙을 기준으로 픽셀 좌표를 계산하므로 대각선·수직 어느 방향이든 같은 코드로 처리된다.
        /// </summary>
        private static void PlacePath(RectTransform rt, Vector2 a, Vector2 b)
        {
            Vector2 pa = new Vector2(a.x * RegionWindowWidth, a.y * RegionWindowHeight);
            Vector2 pb = new Vector2(b.x * RegionWindowWidth, b.y * RegionWindowHeight);
            Vector2 center = new Vector2(RegionWindowWidth, RegionWindowHeight) * 0.5f;

            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.localRotation = Quaternion.identity;         // 길이를 재기 전에 회전을 초기화한다
            rt.sizeDelta = new Vector2(Vector2.Distance(pa, pb), PathThickness);
            rt.anchoredPosition = (pa + pb) * 0.5f - center;
            rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(pb.y - pa.y, pb.x - pa.x) * Mathf.Rad2Deg);
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

            // 지역별 배경·경로 배치를 먼저 적용한 뒤 해금 상태를 그린다.
            ApplyRegionLayout(region);

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
                SoundManager.Sfx(SoundId.UiSlotSelect); // 노드 선택음(사운드 정의서 §8)
                SetSelected(node.Stage);
            }
            else
            {
                SoundManager.Sfx(SoundId.UiError); // 잠긴 스테이지 클릭(§8)
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
