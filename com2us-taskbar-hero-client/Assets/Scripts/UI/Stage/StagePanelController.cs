using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TaskbarHero.Client.Managers;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 입장 스테이지 선택 오버레이 패널(포탈). 계층은 에디터 빌드 시 생성되어 프리팹에 정적 저장되고,
    /// 런타임에는 직렬화된 참조에 이벤트·표시만 배선한다. 서버 실데이터는 아직 연동하지 않고
    /// 데모 진행도(Act1 일반: 1~2 클리어·3 진행·4 잠금)로 구성한다.
    /// 리소스: Assets/Art/UI/Stage. 서버 도메인: stage-battle 기획서(3 Act×2 난이도×4 스테이지).
    /// </summary>
    public class StagePanelController : MonoBehaviour
    {
        private enum StageState { Cleared, Current, Locked }

        [Header("UI 리소스 (Assets/Art/UI/Stage)")]
        [SerializeField] private Sprite nodeUnlocked;   // ui_stage_node_unlocked
        [SerializeField] private Sprite nodeLocked;     // ui_stage_node_locked
        [SerializeField] private Sprite iconLock;       // ui_icon_lock
        [SerializeField] private Sprite iconStar;       // ui_icon_star
        [SerializeField] private Sprite nodeHighlight;  // ui_node_highlight
        [SerializeField] private Sprite pathConnector;  // ui_path_connector
        [SerializeField] private Sprite nameplateBar;   // ui_nameplate_bar
        [SerializeField] private Sprite mapIcon;        // 스테이지(지도 아이콘)

        [Header("구성(데모)")]
        [SerializeField] private int actCount = 3;
        [SerializeField] private int stagesPerAct = 4;

        [Header("구성 참조 (에디터 빌더가 배선)")]
        [SerializeField] private List<StageNodeView> _nodes = new List<StageNodeView>();
        [SerializeField] private Text _actLabel;
        [SerializeField] private Text _difficultyLabel;
        [SerializeField] private Text _nameplateText;
        [SerializeField] private Button _prevActButton;
        [SerializeField] private Button _nextActButton;
        [SerializeField] private Button _difficultyButton;
        [SerializeField] private Button _enterButton;
        [SerializeField] private Button _closeButton;
        [SerializeField] private Button _dimButton;

        private const float CanvasRefWidth = 1080f;
        private const float CanvasRefHeight = 1920f;
        private const float BlockWidth = 680f;

        private Font _font;
        private RectTransform _rootRect;
        private int _act = 1;
        private int _difficulty = 1;
        private int _selectedStage = -1;

        private bool AlreadyBuilt => _nodes != null && _nodes.Count > 0 && _nodes[0] != null;

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

        /// <summary>에디터 빌드 전용: 전체 계층을 생성하고 참조를 배선한다(프리팹 저장용).</summary>
        public void EditorConstruct()
        {
            Construct();
        }

        // ── 구성 ──

        /// <summary>패널 전체 계층을 1회 생성한다.</summary>
        private void Construct()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            BuildCanvas();
            BuildDim();
            var content = BuildContainer();
            BuildHeader(content);
            BuildActNav(content);
            BuildMap(content);
            BuildNameplateAndEnter(content);
        }

        /// <summary>루트에 오버레이 Canvas/스케일러/레이캐스터를 부착한다.</summary>
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

            _rootRect = (RectTransform)transform;
        }

        /// <summary>패널 밖 클릭 시 닫히는 반투명 딤 배경.</summary>
        private void BuildDim()
        {
            var img = NewImage("Dim", _rootRect, null);
            img.color = new Color(0f, 0f, 0f, 0.6f);
            Stretch(img.rectTransform);
            _dimButton = img.gameObject.AddComponent<Button>();
            _dimButton.transition = Selectable.Transition.None;
        }

        /// <summary>패널 본체(어두운 배경) + 가로 중앙 콘텐츠 컨테이너를 만들고 컨테이너를 반환한다.</summary>
        private RectTransform BuildContainer()
        {
            var panel = NewImage("PanelRoot", _rootRect, null);
            panel.color = new Color(0.10f, 0.12f, 0.18f, 0.97f);
            var prt = panel.rectTransform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(760f, 760f);
            prt.anchoredPosition = Vector2.zero;

            // 콘텐츠(가로 중앙 정렬). 자식은 이 블록의 좌상단 기준으로 배치.
            var content = NewRect("StageContent", prt);
            content.anchorMin = content.anchorMax = new Vector2(0.5f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = new Vector2(BlockWidth, 620f);
            content.anchoredPosition = new Vector2(0f, -48f);
            return content;
        }

        /// <summary>제목(지도 아이콘 + 스테이지 선택) + 닫기 버튼.</summary>
        private void BuildHeader(RectTransform content)
        {
            var icon = NewImage("MapIcon", content, mapIcon);
            TopLeft(icon.rectTransform, 210f, 0f, 56f, 56f);

            var title = NewText("Title", content, "스테이지 선택", 40, TextAnchor.MiddleLeft);
            TopLeft(title.rectTransform, 276f, 0f, 300f, 56f);

            var closeImg = NewImage("CloseButton", content, nodeUnlocked);
            TopLeft(closeImg.rectTransform, BlockWidth - 60f, 0f, 60f, 60f);
            Stretch(NewText("X", closeImg.rectTransform, "X", 32, TextAnchor.MiddleCenter).rectTransform);
            _closeButton = closeImg.gameObject.AddComponent<Button>();
        }

        /// <summary>Act 전환(◀ Act N ▶) + 난이도 토글.</summary>
        private void BuildActNav(RectTransform content)
        {
            var prev = NewImage("PrevActButton", content, nodeUnlocked);
            TopLeft(prev.rectTransform, 150f, 84f, 60f, 60f);
            Stretch(NewText("PrevLabel", prev.rectTransform, "<", 34, TextAnchor.MiddleCenter).rectTransform);
            _prevActButton = prev.gameObject.AddComponent<Button>();

            _actLabel = NewText("ActLabel", content, "Act 1", 30, TextAnchor.MiddleCenter);
            TopLeft(_actLabel.rectTransform, 220f, 84f, 240f, 60f);

            var next = NewImage("NextActButton", content, nodeUnlocked);
            TopLeft(next.rectTransform, 470f, 84f, 60f, 60f);
            Stretch(NewText("NextLabel", next.rectTransform, ">", 34, TextAnchor.MiddleCenter).rectTransform);
            _nextActButton = next.gameObject.AddComponent<Button>();

            var diff = NewImage("DifficultyButton", content, nameplateBar);
            TopLeft(diff.rectTransform, 250f, 156f, 180f, 56f);
            _difficultyLabel = NewText("DifficultyLabel", diff.rectTransform, "일반", 26, TextAnchor.MiddleCenter);
            Stretch(_difficultyLabel.rectTransform);
            _difficultyButton = diff.gameObject.AddComponent<Button>();
        }

        /// <summary>4개 스테이지 노드 + 사이 경로 연결선.</summary>
        private void BuildMap(RectTransform content)
        {
            const float nodeSize = 110f;
            const float step = 190f;
            const float mapY = 250f;

            for (int i = 0; i < stagesPerAct; i++)
            {
                int stage = i + 1;
                float x = i * step;

                // 경로 연결선(노드 사이). 노드보다 먼저 만들어 뒤에 깔리게 한다.
                if (i > 0)
                {
                    var conn = NewImage($"Path{i}", content, pathConnector);
                    conn.raycastTarget = false;
                    TopLeft(conn.rectTransform, x - step + nodeSize, mapY + nodeSize * 0.5f - 8f, step - nodeSize, 16f);
                }

                var nodeGo = NewImage($"StageNode{stage}", content, nodeUnlocked);
                TopLeft(nodeGo.rectTransform, x, mapY, nodeSize, nodeSize);

                // 선택 하이라이트(노드보다 약간 큰 프레임, 기본 비활성)
                var hl = NewImage("Highlight", nodeGo.rectTransform, nodeHighlight);
                hl.raycastTarget = false;
                var hrt = hl.rectTransform;
                hrt.anchorMin = Vector2.zero;
                hrt.anchorMax = Vector2.one;
                hrt.offsetMin = new Vector2(-10f, -10f);
                hrt.offsetMax = new Vector2(10f, 10f);
                hl.gameObject.SetActive(false);

                // 스테이지 번호(보스는 "보스")
                var num = NewText("Num", nodeGo.rectTransform, stage == stagesPerAct ? "보스" : stage.ToString(), 30, TextAnchor.MiddleCenter);
                num.raycastTarget = false;
                Stretch(num.rectTransform);

                // 자물쇠(잠금 시 표시)
                var lockImg = NewImage("Lock", nodeGo.rectTransform, iconLock);
                lockImg.raycastTarget = false;
                var lrt = lockImg.rectTransform;
                lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0.5f);
                lrt.pivot = new Vector2(0.5f, 0.5f);
                lrt.sizeDelta = new Vector2(48f, 48f);
                lrt.anchoredPosition = Vector2.zero;
                lockImg.gameObject.SetActive(false);

                // 별(클리어 등급) — 노드 위쪽, 최대 3개
                var starsRoot = NewRect("Stars", nodeGo.rectTransform);
                starsRoot.anchorMin = starsRoot.anchorMax = new Vector2(0.5f, 1f);
                starsRoot.pivot = new Vector2(0.5f, 0f);
                starsRoot.sizeDelta = new Vector2(84f, 26f);
                starsRoot.anchoredPosition = new Vector2(0f, 4f);
                var stars = new Image[3];
                for (int s = 0; s < 3; s++)
                {
                    var star = NewImage($"Star{s}", starsRoot, iconStar);
                    star.raycastTarget = false;
                    var srt = star.rectTransform;
                    srt.anchorMin = srt.anchorMax = new Vector2(0f, 0.5f);
                    srt.pivot = new Vector2(0f, 0.5f);
                    srt.sizeDelta = new Vector2(24f, 24f);
                    srt.anchoredPosition = new Vector2(s * 30f, 0f);
                    star.gameObject.SetActive(false);
                    stars[s] = star;
                }

                var view = nodeGo.gameObject.AddComponent<StageNodeView>();
                view.EditorInit(stage, nodeGo, lockImg.gameObject, hl.gameObject, stars);
                _nodes.Add(view);
            }
        }

        /// <summary>선택 스테이지 이름표 + 입장 버튼.</summary>
        private void BuildNameplateAndEnter(RectTransform content)
        {
            var plate = NewImage("Nameplate", content, nameplateBar);
            TopLeft(plate.rectTransform, (BlockWidth - 460f) * 0.5f, 420f, 460f, 68f);
            _nameplateText = NewText("NameplateText", plate.rectTransform, "", 28, TextAnchor.MiddleCenter);
            Stretch(_nameplateText.rectTransform);

            var enter = NewImage("EnterButton", content, nodeUnlocked);
            TopLeft(enter.rectTransform, (BlockWidth - 220f) * 0.5f, 512f, 220f, 74f);
            Stretch(NewText("EnterLabel", enter.rectTransform, "입장", 32, TextAnchor.MiddleCenter).rectTransform);
            _enterButton = enter.gameObject.AddComponent<Button>();
        }

        // ── 런타임 배선 ──

        /// <summary>버튼 리스너 등록 + 맵 상태 초기화.</summary>
        private void WireRuntime()
        {
            if (_prevActButton != null)
            {
                _prevActButton.onClick.AddListener(OnPrevAct);
            }
            if (_nextActButton != null)
            {
                _nextActButton.onClick.AddListener(OnNextAct);
            }
            if (_difficultyButton != null)
            {
                _difficultyButton.onClick.AddListener(OnToggleDifficulty);
            }
            if (_enterButton != null)
            {
                _enterButton.onClick.AddListener(OnEnter);
            }
            if (_closeButton != null)
            {
                _closeButton.onClick.AddListener(Close);
            }
            if (_dimButton != null)
            {
                _dimButton.onClick.AddListener(Close);
            }

            RefreshMap();
        }

        /// <summary>현재 Act/난이도의 노드 상태를 갱신하고 진행 프론티어를 선택한다.</summary>
        private void RefreshMap()
        {
            if (_actLabel != null)
            {
                _actLabel.text = $"Act {_act}";
            }
            if (_difficultyLabel != null)
            {
                _difficultyLabel.text = _difficulty == 1 ? "일반" : "어려움";
            }

            int frontier = -1;
            foreach (var node in _nodes)
            {
                var state = DemoState(_act, _difficulty, node.Stage);
                bool locked = state == StageState.Locked;
                int stars = state == StageState.Cleared ? DemoStars(node.Stage) : 0;
                node.SetVisual(locked ? nodeLocked : nodeUnlocked, locked, stars);
                if (!locked)
                {
                    frontier = node.Stage; // 잠기지 않은 가장 마지막(진행 가능) 스테이지
                }
            }

            SetSelected(frontier);
        }

        /// <summary>선택 스테이지를 반영(하이라이트·이름표·입장 버튼).</summary>
        private void SetSelected(int stage)
        {
            _selectedStage = stage;
            foreach (var node in _nodes)
            {
                node.SetHighlight(node.Stage == stage);
            }

            if (stage < 0)
            {
                if (_nameplateText != null)
                {
                    _nameplateText.text = "미해금 지역";
                }
                if (_enterButton != null)
                {
                    _enterButton.interactable = false;
                }
                return;
            }

            bool boss = stage == stagesPerAct;
            if (_nameplateText != null)
            {
                _nameplateText.text = $"Act {_act} · {_act}-{stage}" + (boss ? " (보스)" : string.Empty);
            }
            if (_enterButton != null)
            {
                _enterButton.interactable = DemoState(_act, _difficulty, stage) != StageState.Locked;
            }
        }

        /// <summary>노드 클릭: 잠기지 않은 스테이지면 선택한다.</summary>
        public void OnNodeClicked(StageNodeView node)
        {
            if (DemoState(_act, _difficulty, node.Stage) != StageState.Locked)
            {
                SetSelected(node.Stage);
            }
        }

        private void OnPrevAct()
        {
            _act = Mathf.Max(1, _act - 1);
            RefreshMap();
        }

        private void OnNextAct()
        {
            _act = Mathf.Min(actCount, _act + 1);
            RefreshMap();
        }

        private void OnToggleDifficulty()
        {
            _difficulty = _difficulty == 1 ? 2 : 1;
            RefreshMap();
        }

        /// <summary>입장 요청(현재는 데모 로그, 추후 POST /api/game/stage/enter 연동).</summary>
        private void OnEnter()
        {
            if (_selectedStage < 0)
            {
                return;
            }
            Debug.Log($"[Stage] 입장 요청(데모): act={_act}, difficulty={_difficulty}, stage={_selectedStage} — 서버 미연동");
        }

        /// <summary>패널을 닫는다(UIManager 우선).</summary>
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

        // ── 데모 진행도(추후 실데이터 player.act/stage/maxStageCleared로 대체) ──

        private StageState DemoState(int act, int difficulty, int stage)
        {
            if (act == 1 && difficulty == 1)
            {
                if (stage <= 2) return StageState.Cleared;
                if (stage == 3) return StageState.Current;
                return StageState.Locked;
            }
            if (act == 1 && difficulty == 2)
            {
                return stage == 1 ? StageState.Current : StageState.Locked;
            }
            return StageState.Locked; // Act2·3: 데모상 미도달
        }

        private int DemoStars(int stage)
        {
            if (stage == 1) return 3;
            if (stage == 2) return 2;
            return 1;
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

        private static void TopLeft(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }
    }
}
