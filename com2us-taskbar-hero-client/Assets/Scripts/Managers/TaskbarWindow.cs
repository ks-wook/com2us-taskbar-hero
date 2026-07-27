using UnityEngine;
using UnityEngine.SceneManagement;
using USceneManager = UnityEngine.SceneManagement.SceneManager; // 프로젝트 SceneManager와 이름 충돌 방지
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
using System;
using System.Collections;
using System.Runtime.InteropServices;
#endif

namespace TaskbarHero.Client.Managers
{
    /// <summary>
    /// 원작 TBH: Task Bar Hero처럼 게임 창을 <b>작업표시줄 위에 도킹된 항상-위(always-on-top) 소형 전투 창</b>으로
    /// 유지하는 창 제어기(Windows 스탠드얼론 전용). 모드는 두 가지다:
    /// <list type="bullet">
    /// <item><b>전투 확장 모드</b>(BattleDevScene 등 개발용 투명 전투 씬·패널 없음): 낮고 넓은 16:9 가로 창(세로 투명 여백 최소화).</item>
    /// <item><b>확장 모드</b>(타이틀/캐릭터 생성 씬·GameScene 상시·패널/ESC 메뉴 열림): 9:16 설계 기준 폭을 유지하되
    /// 세로를 가로에 맞춘 정사각형 창(세로로 과하게 길지 않게). <b>GameScene은 패널 유무와 무관하게 항상 이 창</b>을 써서
    /// 평상시 UI가 ESC 메뉴가 열렸을 때와 동일한 화면 구성이 되게 한다(구 "스트립 모드" 소형 창은 HUD가 잘려 폐기).</item>
    /// </list>
    /// 테두리 제거·항상 위·위치/크기는 user32.dll(P/Invoke)로, 렌더 해상도는 <see cref="Screen.SetResolution"/>으로
    /// 창 크기와 동기화한다(불일치 시 UI가 잘려 보임). 에디터/타 플랫폼에서는 아무 것도 하지 않는다.
    /// <see cref="RuntimeInitializeOnLoadMethod"/>로 씬 배선 없이 부팅 시 1회 생성된다.
    ///
    /// 추가로 원작처럼 <b>창 배경을 픽셀 단위 투명(per-pixel alpha)</b>으로 만든다:
    /// DWM(<c>DwmExtendFrameIntoClientArea</c>)으로 창 전체를 알파 합성 대상으로 확장하고,
    /// 전투 씬(GameScene·BattleDevScene)에서는 카메라를 Solid RGBA(0,0,0,0)로 클리어해
    /// 게임이 그리지 않은 픽셀에 바탕화면이 그대로 비쳐 보인다(배경은 <c>ScrollingBackground</c>가
    /// 하단 일부만 잘라 그리므로 그 위쪽이 투명 영역이 된다 — 원작의 "길 스트립 + 투명 하늘" 구성).
    /// 또한 커서가 게임 콘텐츠(UI·2D 콜라이더) 위가 아닐 때는 WS_EX_TRANSPARENT로
    /// 마우스 입력을 뒤 창(바탕화면)으로 통과시킨다.
    /// </summary>
    public class TaskbarWindow : MonoBehaviour
    {
        public static TaskbarWindow Instance { get; private set; }

        /// <summary>
        /// 투명 오버레이 모드 활성 여부(Windows 스탠드얼론 빌드에서만 true).
        /// 씬 쪽 코드가 "지금 창이 투명 오버레이인지"를 판단해 표현을 달리할 때 참조한다.
        /// 에디터/타 플랫폼에서는 항상 false라 기존 개발 환경이 그대로 유지된다.
        /// </summary>
        public static bool TransparentOverlayActive { get; private set; }

        /// <summary>
        /// 마우스 드래그로 창을 이동 중인지(Windows 스탠드얼론 빌드 전용 — 에디터/타 플랫폼은 항상 false).
        /// 씬 쪽 코드(예: 타이틀의 "아무 곳이나 클릭해 시작")가 창 드래그를 클릭으로 오인하지 않도록 참조한다.
        /// </summary>
        public static bool DraggingWindow { get; private set; }

        // 창 배경을 투명하게 두는(=바탕화면이 비치는) 씬. 그 외 씬은 불투명 UI가 화면을 채운다.
        private static readonly string[] TransparentScenes = { "GameScene", "BattleDevScene" };

        // 배경(길 스트립)이 노출 중인 월드 y 구간. 이 밴드 위 커서는 게임 콘텐츠로 취급되어
        // 클릭 통과 대상에서 빠지고, 창 드래그의 그립 영역이 된다.
        private static float s_bandBottomY;
        private static float s_bandTopY;
        private static bool s_hasBand;

        /// <summary>배경 렌더러(ScrollingBackground)가 현재 노출 중인 배경 밴드(월드 y 구간)를 보고한다.</summary>
        public static void ReportContentBand(float bottomWorldY, float topWorldY)
        {
            s_bandBottomY = Mathf.Min(bottomWorldY, topWorldY);
            s_bandTopY = Mathf.Max(bottomWorldY, topWorldY);
            s_hasBand = true;
        }

        // 확장 모드: 게임 UI 설계 비율(1080×1920 = 9:16), 작업영역 높이의 90%.
        private const float PortraitAspect = 1080f / 1920f;
        private const float ExpandedHeightFrac = 0.90f;

        // 전투(투명) 씬 확장 창: 세로로 긴 9:16 대신 가로가 넓은 16:9 — 줌아웃된 가로 전장에 맞춤.
        private const float BattleHeightFrac = 0.45f;
        private const float BattleAspect = 16f / 9f;

        private bool _panelOpen;         // UIManager 패널/ESC 메뉴 표시 중(BattleDevScene 등에서 확장 필요)
        private bool _inGameScene;       // GameScene인지(항상 확장 창 — 개발용 16:9 분기 제외 판정)
        private bool _transparentScene;  // 현재 씬이 투명 배경 대상(TransparentScenes)인지

        /// <summary>부팅 시 창 제어기를 1회 생성한다(씬 배선 불필요).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null)
            {
                return;
            }
            var go = new GameObject("TaskbarWindow");
            go.AddComponent<TaskbarWindow>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            USceneManager.activeSceneChanged += OnSceneChanged;
            _inGameScene = USceneManager.GetActiveScene().name == "GameScene";
            _transparentScene = IsTransparentScene(USceneManager.GetActiveScene().name);
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            // Awake는 모든 씬 오브젝트의 Start(ScrollingBackground.Build 포함)보다 먼저 실행되므로
            // 여기서 플래그를 세워야 첫 씬부터 배경 타일 생성이 생략된다.
            TransparentOverlayActive = true;
#endif
        }

        /// <summary>해당 씬이 창 배경을 투명하게 둘 대상(전투 씬)인지 판정한다.</summary>
        private static bool IsTransparentScene(string sceneName)
        {
            foreach (var s in TransparentScenes)
            {
                if (s == sceneName)
                {
                    return true;
                }
            }
            return false;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                USceneManager.activeSceneChanged -= OnSceneChanged;
                Instance = null;
            }
        }

        /// <summary>씬 전환 시 모드를 재판정한다(패널 상태는 씬 전환으로 초기화).</summary>
        private void OnSceneChanged(Scene from, Scene to)
        {
            _inGameScene = to.name == "GameScene";
            _transparentScene = IsTransparentScene(to.name);
            _panelOpen = false; // 씬 전환 시 이전 씬의 패널 상태를 이월하지 않음
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            Apply();
            ApplyTransparentSceneCamera();
#endif
        }

        /// <summary>패널/메뉴 표시 상태를 알린다. GameScene 창은 항상 확장이라 변하지 않고,
        /// BattleDevScene 등 개발용 투명 씬에서만 열리면 확장/닫히면 16:9로 복귀한다.</summary>
        public void SetExpanded(bool panelOpen)
        {
            _panelOpen = panelOpen;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            Apply();
#endif
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        private IntPtr _hwnd;
        private Coroutine _reassert;

        private const int GWL_STYLE = -16;
        private const int GWL_EXSTYLE = -20;
        private const uint WS_POPUP = 0x80000000;
        private const uint WS_VISIBLE = 0x10000000;
        private const uint WS_EX_LAYERED = 0x00080000;
        private const uint WS_EX_TRANSPARENT = 0x00000020;
        private const uint LWA_ALPHA = 0x0002;
        private const uint SPI_GETWORKAREA = 0x0030;
        private const uint SWP_FRAMECHANGED = 0x0020;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const uint SWP_NOSIZE = 0x0001;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int left, top, right, bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x, y; }

        // 마진 -1 = "시트 전체를 프레임으로 확장" → DWM이 창 전체를 per-pixel alpha로 합성한다.
        [StructLayout(LayoutKind.Sequential)]
        private struct MARGINS { public int left, right, top, bottom; }

        [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref RECT pvParam, uint fWinIni);
        [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint crKey, byte bAlpha, uint dwFlags);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT lpPoint);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("Dwmapi.dll")] private static extern uint DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);

        private bool _clickThrough; // 현재 WS_EX_TRANSPARENT(마우스 통과) 적용 여부

        // 창 드래그 이동: 콘텐츠(길 스트립 등)를 LMB로 누른 채 임계값 이상 움직이면 드래그 시작.
        private const int DragThresholdPx = 6;
        private bool _dragCandidate;  // LMB 눌림 → 드래그 시작 대기(임계값 판정 중)
        private int _pressX, _pressY; // 눌렀을 때 커서 좌표(물리 px)
        private bool _dragging;       // 드래그로 창 이동 중
        private int _dragOffX, _dragOffY; // 커서 ↔ 창 좌상단 오프셋
        private bool _userMoved;      // 드래그로 위치를 옮긴 적 있음 → Apply가 도킹 대신 이 위치 유지
        private int _userLeft, _userBottom; // 사용자 위치(좌측 x·하단선 y — 모드 전환 시 하단선 유지)

        private void Start()
        {
            _hwnd = ResolveWindow();
            if (_hwnd == IntPtr.Zero)
            {
                Debug.LogWarning("[TaskbarWindow] 게임 창 핸들을 찾지 못해 창 제어를 건너뜁니다.");
                return;
            }
            // 테두리 제거(팝업 스타일). 이후 위치/크기는 Apply가 담당.
            SetWindowLongPtr(_hwnd, GWL_STYLE, new IntPtr((long)(WS_POPUP | WS_VISIBLE)));
            InitTransparency();
            Apply();
            ApplyTransparentSceneCamera();
        }

        /// <summary>
        /// 창을 per-pixel alpha 투명 창으로 초기화한다: DWM 프레임을 클라이언트 영역 전체로 확장해
        /// 알파 합성을 켜고, 레이어드 창으로 전환한다(클릭 통과 토글의 전제 조건).
        /// 카메라가 RGBA(0,0,0,0)으로 클리어한 픽셀은 바탕화면이 그대로 비쳐 보인다.
        /// </summary>
        private void InitTransparency()
        {
            var margins = new MARGINS { left = -1 };
            uint hr = DwmExtendFrameIntoClientArea(_hwnd, ref margins);
            if (hr != 0)
            {
                Debug.LogWarning($"[TaskbarWindow] DwmExtendFrameIntoClientArea 실패(hr=0x{hr:X8}) — 창 투명화가 비활성됩니다.");
                return;
            }
            ApplyExStyle();
            SetLayeredWindowAttributes(_hwnd, 0, 255, LWA_ALPHA); // 레이어드 창 렌더 활성(창 전체 알파 255)
        }

        /// <summary>확장 스타일을 적용한다: 항상 WS_EX_LAYERED, 클릭 통과 중이면 WS_EX_TRANSPARENT 추가.</summary>
        private void ApplyExStyle()
        {
            uint ex = WS_EX_LAYERED;
            if (_clickThrough)
            {
                ex |= WS_EX_TRANSPARENT;
            }
            SetWindowLongPtr(_hwnd, GWL_EXSTYLE, new IntPtr((long)ex));
        }

        /// <summary>
        /// 매 프레임 ① 드래그로 창 이동을 처리하고, ② 커서 아래에 게임 콘텐츠(UI·2D 콜라이더·길 스트립)가
        /// 있는지 판정해 빈(투명) 영역 위에서는 마우스 입력이 뒤 창으로 통과하도록 WS_EX_TRANSPARENT를 토글한다.
        /// 투명 씬(전투 씬)이 아닐 때는 항상 통과를 끈다(불투명 UI가 화면을 채우므로).
        /// </summary>
        private void Update()
        {
            if (_hwnd == IntPtr.Zero)
            {
                return;
            }
            UpdateDrag();
            if (_dragging || _dragCandidate)
            {
                return; // 드래그(대기) 중에는 클릭 통과를 켜지 않는다(입력이 창에 남아 있어야 함)
            }
            bool want = _transparentScene && !IsPointerOverContent();
            if (want != _clickThrough)
            {
                _clickThrough = want;
                ApplyExStyle();
            }
        }

        /// <summary>
        /// 콘텐츠(길 스트립·캐릭터·비인터랙티브 UI 등, 단 버튼/입력필드 같은 인터랙티브 uGUI와 IMGUI 컨트롤 제외)를
        /// LMB로 누른 채 임계값 이상 움직이면 창을 커서에 붙여 이동시킨다.
        /// 버튼을 놓으면 종료하고, 옮긴 위치를 기억해 이후 Apply가 유지한다.
        /// </summary>
        private void UpdateDrag()
        {
            if (_dragging)
            {
                if (!Input.GetMouseButton(0))
                {
                    EndDrag();
                    return;
                }
                GetCursorPos(out POINT cur);
                SetWindowPos(_hwnd, HWND_TOPMOST, cur.x - _dragOffX, cur.y - _dragOffY, 0, 0, SWP_NOSIZE | SWP_SHOWWINDOW);
                return;
            }

            if (Input.GetMouseButtonDown(0) && CanBeginDrag())
            {
                _dragCandidate = true;
                GetCursorPos(out POINT p);
                _pressX = p.x;
                _pressY = p.y;
                return;
            }

            if (!_dragCandidate)
            {
                return;
            }
            if (!Input.GetMouseButton(0) || !CanBeginDrag())
            {
                _dragCandidate = false; // 버튼을 놓았거나 UI/IMGUI 조작으로 판명 → 드래그 취소
                return;
            }
            GetCursorPos(out POINT now);
            if (Mathf.Abs(now.x - _pressX) + Mathf.Abs(now.y - _pressY) >= DragThresholdPx)
            {
                BeginDrag(now);
            }
        }

        /// <summary>지금 커서 위치에서 드래그를 시작해도 되는지 판정한다.
        /// 인터랙티브 uGUI(버튼·입력필드 등)와 IMGUI 컨트롤 조작만 제외한다 — 로고·배경·딤 같은
        /// 비인터랙티브 UI가 화면을 덮는 씬(타이틀 등)에서도 창 드래그가 가능해야 하므로,
        /// 단순히 "UI 위인가"가 아니라 "클릭/드래그를 소비하는 UI 위인가"로 판정한다.</summary>
        private static bool CanBeginDrag()
        {
            if (GUIUtility.hotControl != 0)
            {
                return false; // 개발 하네스 등 IMGUI 컨트롤을 누르는 중
            }
            return !IsPointerOverInteractiveUi();
        }

        /// <summary>드래그 이동을 시작한다: 커서와 창 좌상단의 오프셋을 기억하고 클릭 통과를 끈다.</summary>
        private void BeginDrag(POINT cursor)
        {
            GetWindowRect(_hwnd, out RECT r);
            _dragOffX = cursor.x - r.left;
            _dragOffY = cursor.y - r.top;
            _dragCandidate = false;
            _dragging = true;
            DraggingWindow = true;
            if (_clickThrough)
            {
                _clickThrough = false;
                ApplyExStyle();
            }
        }

        /// <summary>드래그를 끝내고 최종 위치(좌측 x·하단선 y)를 기억한다 — 이후 모드 전환에도 이 위치를 유지.</summary>
        private void EndDrag()
        {
            _dragging = false;
            DraggingWindow = false;
            GetWindowRect(_hwnd, out RECT r);
            _userMoved = true;
            _userLeft = r.left;
            _userBottom = r.bottom;
            Debug.Log($"[TaskbarWindow] 드래그 이동 완료: left={_userLeft} bottom={_userBottom}");
        }

        private static readonly System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult> s_uiHits =
            new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();

        /// <summary>
        /// 포커스와 무관하게 현재 커서 위치가 uGUI 레이캐스트 대상 위인지 판정한다.
        /// EventSystem.IsPointerOverGameObject()는 창이 비활성(포커스 없음)이면 갱신되지 않으므로,
        /// 오버레이 창 특성상 수동 RaycastAll로 직접 검사한다.
        /// </summary>
        private static bool IsPointerOverUi()
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es == null)
            {
                return false;
            }
            var ped = new UnityEngine.EventSystems.PointerEventData(es) { position = Input.mousePosition };
            s_uiHits.Clear();
            es.RaycastAll(ped, s_uiHits);
            return s_uiHits.Count > 0;
        }

        /// <summary>
        /// 현재 커서 위치가 클릭/드래그를 소비하는 인터랙티브 uGUI(버튼·토글·입력필드·스크롤 등) 위인지 판정한다.
        /// 로고·배경 이미지·딤처럼 레이캐스트 대상이지만 클릭 핸들러가 없는 UI는 인터랙티브로 치지 않는다
        /// (그 위에서는 창 드래그를 허용하기 위함 — 타이틀 씬은 화면 전체가 UI라 이 구분이 없으면 드래그 불가).
        /// </summary>
        private static bool IsPointerOverInteractiveUi()
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es == null)
            {
                return false;
            }
            var ped = new UnityEngine.EventSystems.PointerEventData(es) { position = Input.mousePosition };
            s_uiHits.Clear();
            es.RaycastAll(ped, s_uiHits);
            foreach (var hit in s_uiHits)
            {
                var go = hit.gameObject;
                if (UnityEngine.EventSystems.ExecuteEvents.GetEventHandler<UnityEngine.EventSystems.IPointerClickHandler>(go) != null
                    || UnityEngine.EventSystems.ExecuteEvents.GetEventHandler<UnityEngine.EventSystems.IPointerDownHandler>(go) != null
                    || UnityEngine.EventSystems.ExecuteEvents.GetEventHandler<UnityEngine.EventSystems.IDragHandler>(go) != null
                    || UnityEngine.EventSystems.ExecuteEvents.GetEventHandler<UnityEngine.EventSystems.IScrollHandler>(go) != null)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>커서가 게임 콘텐츠 위인지 판정한다(uGUI 레이캐스트 대상, 2D 콜라이더, 배경 길 스트립 밴드).</summary>
        private static bool IsPointerOverContent()
        {
            if (IsPointerOverUi())
            {
                return true;
            }
            var cam = Camera.main;
            if (cam != null)
            {
                // 창 히트테스트 용도의 시스템 커서 좌표 조회(게임플레이 입력이 아니므로 액션 에셋 대상 아님,
                // Active Input Handling=Both라 레거시 API 사용 가능).
                Vector2 world = cam.ScreenToWorldPoint(Input.mousePosition);
                if (Physics2D.OverlapPoint(world) != null)
                {
                    return true;
                }
                if (s_hasBand && world.y >= s_bandBottomY && world.y <= s_bandTopY)
                {
                    return true; // 배경 길 스트립 위 — 클릭을 받고, 창 드래그 그립으로 쓴다
                }
            }
            return false;
        }

        /// <summary>
        /// 투명 씬(전투 씬)의 카메라를 Solid RGBA(0,0,0,0) 클리어로 강제해, 배경이 없는 픽셀의
        /// 알파가 0이 되도록 한다(GameScene은 Skybox, BattleDevScene은 불투명 남색이 기본이므로 덮어씀).
        /// 에디터 씬 에셋은 건드리지 않고 빌드 런타임에서만 적용된다.
        /// </summary>
        private void ApplyTransparentSceneCamera()
        {
            if (!_transparentScene)
            {
                return;
            }
            var cam = Camera.main;
            if (cam == null)
            {
                return;
            }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        }

        /// <summary>현재 프로세스의 메인 창 핸들을 얻는다(없으면 활성 창으로 폴백).</summary>
        private static IntPtr ResolveWindow()
        {
            try
            {
                var h = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                if (h != IntPtr.Zero) return h;
            }
            catch { }
            return GetActiveWindow();
        }

        /// <summary>현재 모드에 맞는 창 크기·위치를 계산해 적용하고, 렌더 해상도를 창 크기에 동기화한다.</summary>
        private void Apply()
        {
            if (_hwnd == IntPtr.Zero) return;

            RECT wa = default;
            if (!SystemParametersInfo(SPI_GETWORKAREA, 0, ref wa, 0))
            {
                wa.left = 0; wa.top = 0; wa.right = Display.main.systemWidth; wa.bottom = Display.main.systemHeight;
            }
            int waW = wa.right - wa.left;
            int waH = wa.bottom - wa.top;

            int w, h;
            if (_transparentScene && !_inGameScene && !_panelOpen)
            {
                // 개발용 투명 전투 씬(BattleDevScene 등): 낮고 넓은 16:9 창 — 세로 투명 여백을 줄이고 가로 전장을 확보.
                h = Mathf.RoundToInt(waH * BattleHeightFrac);
                w = Mathf.Min(waW, Mathf.RoundToInt(h * BattleAspect));
            }
            else
            {
                // 확장 창(타이틀/캐릭터 생성/GameScene 상시/패널·ESC 메뉴): 가로 폭(9:16 설계 기준 폭)은 유지하고
                // 세로를 가로 길이에 맞춘 정사각형으로 줄인다(세로로 과하게 길지 않게).
                // GameScene은 패널 유무와 무관하게 항상 이 창을 써서 평상시 UI가 ESC 메뉴가 열렸을 때와 동일하게 구성된다.
                w = Mathf.Min(waW, Mathf.RoundToInt(waH * ExpandedHeightFrac * PortraitAspect));
                h = Mathf.Min(waH, w);
            }

            // 기본은 작업표시줄 바로 위 우측 도킹(원작의 taskbar 밀착 느낌).
            // 사용자가 드래그로 옮겼다면 그 위치(좌측 x·하단선 y)를 작업영역 안으로 클램프해 유지한다.
            int x, y;
            if (_userMoved)
            {
                x = Mathf.Clamp(_userLeft, wa.left, Mathf.Max(wa.left, wa.right - w));
                y = Mathf.Clamp(_userBottom - h, wa.top, Mathf.Max(wa.top, wa.bottom - h));
            }
            else
            {
                x = wa.right - w;
                y = wa.bottom - h;
            }

            // Screen.SetResolution은 DPI 논리 좌표로 해석돼 물리 픽셀 창과 어긋나므로 쓰지 않는다.
            // SetWindowPos만으로 창을 조절하면 Unity가 WM_SIZE로 스왑체인을 클라이언트 크기에 맞춘다(1차 빌드 검증).
            ApplyStyleAndPos(x, y, w, h);
            Debug.Log($"[TaskbarWindow] Apply rect=({x},{y},{w}x{h}) screen={Screen.width}x{Screen.height}");

            // Unity가 프레임 경계에서 창을 다시 만질 수 있어, 몇 프레임 뒤 스타일·위치를 재고정한다.
            if (_reassert != null)
            {
                StopCoroutine(_reassert);
            }
            _reassert = StartCoroutine(ReassertPos(x, y, w, h));
        }

        /// <summary>테두리 없는 팝업 스타일·확장 스타일(레이어드/클릭 통과)과 위치/크기를 함께 적용한다.</summary>
        private void ApplyStyleAndPos(int x, int y, int w, int h)
        {
            SetWindowLongPtr(_hwnd, GWL_STYLE, new IntPtr((long)(WS_POPUP | WS_VISIBLE)));
            ApplyExStyle();
            SetWindowPos(_hwnd, HWND_TOPMOST, x, y, w, h, SWP_FRAMECHANGED | SWP_SHOWWINDOW);
        }

        /// <summary>몇 프레임 뒤 스타일·위치를 재고정한다(Unity의 창 재구성이 스타일을 되돌리는 경우 대비).</summary>
        private IEnumerator ReassertPos(int x, int y, int w, int h)
        {
            yield return null;
            yield return null;
            ApplyStyleAndPos(x, y, w, h);
            yield return null;
            yield return null;
            yield return null;
            ApplyStyleAndPos(x, y, w, h);
            Debug.Log($"[TaskbarWindow] Reassert done rect=({x},{y},{w}x{h}) screen={Screen.width}x{Screen.height}");
            _reassert = null;
        }
#endif
    }
}
