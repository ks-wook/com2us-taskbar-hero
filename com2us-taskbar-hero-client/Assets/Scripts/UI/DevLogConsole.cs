using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 개발용 로그 출력창. Debug.Log 계열과 예외/네트워크 로그를 화면 좌측 하단에 표시한다.
    /// 씬이 바뀌어도 유지된다(DontDestroyOnLoad). UI를 코드로 직접 구성하므로 프리팹/씬 배선이 필요 없다.
    /// <para><b>기본은 화면에 아무것도 없다</b> — 접기 버튼(화살표)조차 만들지 않은 것처럼 숨겨 두고,
    /// <b><see cref="revealKey"/>(L) 키를 <see cref="RevealPressCount"/>번 빠르게</b>
    /// (<see cref="RevealWindowSeconds"/>초 안에) 눌러야 드러난다. 같은 방법으로 다시 숨긴다.
    /// 개발용 창이 플레이 화면에 상시 노출되지 않도록 한 조치이며, 실수로 눌려 열리지 않게
    /// 연타를 요구한다(입력창에 타이핑 중이면 세지 않는다).</para>
    /// <para>드러난 뒤에는 좌측 하단 화살표 버튼으로 로그 패널만 접었다 펼 수 있다.</para>
    /// </summary>
    public class DevLogConsole : MonoBehaviour
    {
        public static DevLogConsole Instance { get; private set; }

        /// <summary>
        /// 어떤 씬에서 시작하든 로그창이 항상 존재하도록, 첫 씬 로드 전에 인스턴스를 자동 생성한다.
        /// (씬마다 배치할 필요 없이 DontDestroyOnLoad로 모든 씬에서 유지된다.)
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance == null)
            {
                new GameObject("DevLogConsole").AddComponent<DevLogConsole>();
            }
        }

        [Header("설정")]
        [Tooltip("보관할 최대 로그 줄 수.")]
        [SerializeField] private int maxLines = 200;

        [Tooltip("시작 시 로그창을 드러낼지 여부(기본 꺼짐 — 화살표 버튼조차 보이지 않는다).")]
        [SerializeField] private bool visibleOnStart = false;

        [Tooltip("로그창을 드러내는 키. 짧은 시간 안에 여러 번 눌러야 한다.")]
        [SerializeField] private Key revealKey = Key.L;

        /// <summary>로그창을 드러내려면 눌러야 하는 횟수.</summary>
        private const int RevealPressCount = 3;

        /// <summary>연타로 인정하는 간격(초). 이 시간이 지나면 횟수가 처음부터 다시 센다.</summary>
        private const float RevealWindowSeconds = 0.8f;

        // 토글 버튼 크기/여백(캔버스 참조 해상도 360×640 기준). 이 캔버스는 ScaleWithScreenSize라
        // 실제 픽셀 크기는 창 크기에 비례해 커진다 — 하단 UI를 가리지 않도록 작게 유지한다.
        private const float ToggleWidth = 30f;
        private const float ToggleHeight = 16f;
        private const float ToggleMargin = 4f;
        private const int ToggleFontSize = 10;

        // 버튼이 작아 "LOG" 글자를 넣을 공간이 없으므로 상태를 화살표로만 표시한다(▸ 접힘 / ▾ 펼침).
        private const string CollapsedLabel = "▸";
        private const string ExpandedLabel = "▾";

        private readonly List<string> _lines = new List<string>();
        private GameObject _canvasGo;   // 로그창 UI 전체(토글 버튼 포함) — 드러나기 전에는 통째로 꺼 둔다
        private GameObject _logPanel;
        private Text _logText;
        private ScrollRect _scroll;
        private Text _toggleLabel;
        private bool _revealed;         // 로그창 UI가 화면에 나와 있는지(연타로 전환)
        private bool _visible;          // 드러난 상태에서 로그 패널이 펼쳐져 있는지
        private bool _dirty;
        private int _revealPresses;     // 현재까지 이어진 연타 횟수
        private float _lastRevealPress; // 마지막 입력 시각(unscaled)

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            BuildUI();
            SetRevealed(visibleOnStart);
        }

        private void OnEnable()
        {
            Application.logMessageReceived += HandleLog;
        }

        private void OnDisable()
        {
            Application.logMessageReceived -= HandleLog;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            UpdateRevealInput();

            if (_dirty && _revealed && _visible)
            {
                RebuildText(); // 숨겨져 있는 동안은 갱신하지 않는다(드러날 때 SetVisible이 한 번에 그린다)
            }
        }

        /// <summary>
        /// 드러내기 키 연타를 판정한다. <see cref="RevealWindowSeconds"/>초 안에
        /// <see cref="RevealPressCount"/>번 이어서 누르면 로그창을 드러내거나 다시 숨긴다.
        /// 간격이 벌어지면 횟수는 1부터 다시 센다.
        /// </summary>
        private void UpdateRevealInput()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard[revealKey].wasPressedThisFrame || IsTypingInInputField())
            {
                return;
            }

            float now = Time.unscaledTime;
            _revealPresses = now - _lastRevealPress <= RevealWindowSeconds ? _revealPresses + 1 : 1;
            _lastRevealPress = now;
            if (_revealPresses < RevealPressCount)
            {
                return;
            }

            _revealPresses = 0;
            SetRevealed(!_revealed);
        }

        /// <summary>입력창(아이디·비밀번호·검색 등)에 타이핑 중인지 — 그때는 연타로 세지 않는다.</summary>
        private static bool IsTypingInInputField()
        {
            var selected = UnityEngine.EventSystems.EventSystem.current != null
                ? UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject
                : null;
            return selected != null && selected.GetComponent<InputField>() != null;
        }

        private void HandleLog(string message, string stackTrace, LogType type)
        {
            string color;
            switch (type)
            {
                case LogType.Error:
                case LogType.Exception:
                case LogType.Assert:
                    color = "#FF6B6B";
                    break;
                case LogType.Warning:
                    color = "#FFD166";
                    break;
                default:
                    color = "#E8E8E8";
                    break;
            }

            _lines.Add("<color=" + color + ">" + message + "</color>");
            if (_lines.Count > maxLines)
            {
                _lines.RemoveRange(0, _lines.Count - maxLines);
            }

            _dirty = true;
        }

        /// <summary>로그창 표시/숨김을 토글한다.</summary>
        public void Toggle()
        {
            SetVisible(!_visible);
        }

        /// <summary>로그를 모두 지운다.</summary>
        public void Clear()
        {
            _lines.Clear();
            _dirty = true;
        }

        /// <summary>
        /// 로그창 UI 전체(토글 버튼 포함)를 드러내거나 숨긴다. 드러날 때는 로그 패널까지 펼친다 —
        /// 작은 화살표 버튼만 나오면 열렸는지 알아보기 어렵다.
        /// </summary>
        private void SetRevealed(bool value)
        {
            _revealed = value;
            if (_canvasGo != null)
            {
                _canvasGo.SetActive(value);
            }
            SetVisible(value);
        }

        private void SetVisible(bool value)
        {
            _visible = value;
            if (_logPanel != null)
            {
                _logPanel.SetActive(value);
            }

            if (_toggleLabel != null)
            {
                _toggleLabel.text = value ? ExpandedLabel : CollapsedLabel;
            }

            if (value)
            {
                RebuildText();
            }
        }

        private void RebuildText()
        {
            if (_logText == null)
            {
                return;
            }

            _logText.text = string.Join("\n", _lines);
            _dirty = false;

            // 콘텐츠 크기 갱신 후 하단으로 자동 스크롤.
            Canvas.ForceUpdateCanvases();
            if (_scroll != null)
            {
                _scroll.verticalNormalizedPosition = 0f;
            }
        }

        // ---------- UI 구성 ----------

        private void BuildUI()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var canvasGo = new GameObject("DevLogCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            _canvasGo = canvasGo; // 드러나기 전에는 이 캔버스를 통째로 꺼 둔다(토글 버튼도 보이지 않게)
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000; // 항상 최상단
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(360, 640);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            // 토글 버튼 (좌측 하단). 하단 UI를 가리지 않도록 작게 두고, 상태는 화살표만으로 표시한다.
            var toggleBtn = CreateButton(canvasGo.transform, "ToggleButton",
                new Vector2(ToggleMargin, ToggleMargin), new Vector2(ToggleWidth, ToggleHeight),
                CollapsedLabel, font, ToggleFontSize);
            toggleBtn.onClick.AddListener(Toggle);
            _toggleLabel = toggleBtn.GetComponentInChildren<Text>();

            // 로그 패널 (토글 버튼 위)
            _logPanel = CreatePanel(canvasGo.transform, "LogPanel",
                new Vector2(ToggleMargin, ToggleMargin * 2f + ToggleHeight), new Vector2(320f, 240f),
                new Color(0f, 0f, 0f, 0.78f));

            // Clear 버튼 (패널 우측 상단)
            var clearBtn = CreateButton(_logPanel.transform, "ClearButton", Vector2.zero, new Vector2(52f, 22f), "Clear", font, 12);
            var clearRt = clearBtn.GetComponent<RectTransform>();
            clearRt.anchorMin = clearRt.anchorMax = new Vector2(1f, 1f);
            clearRt.pivot = new Vector2(1f, 1f);
            clearRt.anchoredPosition = new Vector2(-4f, -4f);
            clearBtn.onClick.AddListener(Clear);

            // 스크롤 영역
            var scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect));
            scrollGo.transform.SetParent(_logPanel.transform, false);
            var scrollRt = scrollGo.GetComponent<RectTransform>();
            scrollRt.anchorMin = Vector2.zero;
            scrollRt.anchorMax = Vector2.one;
            scrollRt.offsetMin = new Vector2(6f, 6f);
            scrollRt.offsetMax = new Vector2(-6f, -30f); // 상단 Clear 버튼 공간 확보
            _scroll = scrollGo.GetComponent<ScrollRect>();
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 20f;

            // Viewport
            var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D), typeof(Image));
            viewportGo.transform.SetParent(scrollGo.transform, false);
            var viewportRt = viewportGo.GetComponent<RectTransform>();
            viewportRt.anchorMin = Vector2.zero;
            viewportRt.anchorMax = Vector2.one;
            viewportRt.offsetMin = Vector2.zero;
            viewportRt.offsetMax = Vector2.zero;
            viewportRt.pivot = new Vector2(0f, 1f);
            var viewportImg = viewportGo.GetComponent<Image>();
            viewportImg.color = new Color(1f, 1f, 1f, 0.01f); // 스크롤 드래그를 위한 최소 raycast 타깃
            _scroll.viewport = viewportRt;

            // Content (Text)
            var contentGo = new GameObject("Content", typeof(RectTransform), typeof(Text), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(viewportGo.transform, false);
            var contentRt = contentGo.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0f, 1f);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = new Vector2(0f, 0f);
            _logText = contentGo.GetComponent<Text>();
            _logText.font = font;
            _logText.fontSize = 11;
            _logText.color = Color.white;
            _logText.supportRichText = true;
            _logText.alignment = TextAnchor.UpperLeft;
            _logText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _logText.verticalOverflow = VerticalWrapMode.Overflow;
            _logText.raycastTarget = false;
            var fitter = contentGo.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _scroll.content = contentRt;
        }

        private static GameObject CreatePanel(Transform parent, string name, Vector2 anchoredPos, Vector2 size, Color bg)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
            var img = go.GetComponent<Image>();
            img.color = bg;
            return go;
        }

        private static Button CreateButton(Transform parent, string name, Vector2 anchoredPos, Vector2 size, string label, Font font, int fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
            var img = go.GetComponent<Image>();
            img.color = new Color(0.15f, 0.15f, 0.18f, 0.9f);
            var button = go.GetComponent<Button>();
            button.targetGraphic = img;

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(go.transform, false);
            var trt = textGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;
            var text = textGo.GetComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            text.text = label;
            text.raycastTarget = false;

            return button;
        }
    }
}
