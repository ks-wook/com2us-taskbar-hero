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
    /// 유지하는 창 제어기(Windows 스탠드얼론 전용). 원작은 "taskbar에 들어가는 초소형 창" 안에서 자동 전투만 상시
    /// 노출하고 패널은 그 위에 레이어로 연다 — 이를 다음 두 모드로 재현한다:
    /// <list type="bullet">
    /// <item><b>스트립 모드</b>(GameScene 전투 중·패널 없음): 작업표시줄 바로 위 우측에 도킹된 컴팩트한 가로 전투 창.</item>
    /// <item><b>확장 모드</b>(타이틀/캐릭터 생성 씬, 또는 패널·ESC 메뉴 열림): 세로 UI(1080×1920 설계)에 맞는 9:16 세로 창.</item>
    /// </list>
    /// 테두리 제거·항상 위·위치/크기는 user32.dll(P/Invoke)로, 렌더 해상도는 <see cref="Screen.SetResolution"/>으로
    /// 창 크기와 동기화한다(불일치 시 UI가 잘려 보임). 에디터/타 플랫폼에서는 아무 것도 하지 않는다.
    /// <see cref="RuntimeInitializeOnLoadMethod"/>로 씬 배선 없이 부팅 시 1회 생성된다.
    /// </summary>
    public class TaskbarWindow : MonoBehaviour
    {
        public static TaskbarWindow Instance { get; private set; }

        // 확장 모드: 게임 UI 설계 비율(1080×1920 = 9:16), 작업영역 높이의 90%.
        private const float PortraitAspect = 1080f / 1920f;
        private const float ExpandedHeightFrac = 0.90f;

        // 스트립 모드: 원작의 "taskbar 위 소형 전투 창". 높이 = 작업영역의 16%(160~280px), 폭 = 높이 × 4(가로 전투 레인).
        private const float StripHeightFrac = 0.16f;
        private const int StripHeightMin = 160;
        private const int StripHeightMax = 280;
        private const float StripAspect = 4f;

        private bool _panelOpen;     // UIManager 패널/ESC 메뉴 표시 중(확장 필요)
        private bool _inGameScene;   // 전투(GameScene)에서만 스트립 모드 사용

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
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                USceneManager.activeSceneChanged -= OnSceneChanged;
                Instance = null;
            }
        }

        /// <summary>씬 전환 시 모드를 재판정한다(GameScene에서만 스트립, 패널 상태는 씬 전환으로 초기화).</summary>
        private void OnSceneChanged(Scene from, Scene to)
        {
            _inGameScene = to.name == "GameScene";
            _panelOpen = false; // 씬 전환 시 이전 씬의 패널 상태를 이월하지 않음
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            Apply();
#endif
        }

        /// <summary>패널/메뉴 표시 상태를 알린다. 열리면 확장, 모두 닫히면(전투 중일 때) 스트립으로 복귀.</summary>
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
        private const uint WS_POPUP = 0x80000000;
        private const uint WS_VISIBLE = 0x10000000;
        private const uint SPI_GETWORKAREA = 0x0030;
        private const uint SWP_FRAMECHANGED = 0x0020;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int left, top, right, bottom; }

        [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref RECT pvParam, uint fWinIni);

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
            Apply();
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

            bool strip = _inGameScene && !_panelOpen;
            int w, h;
            if (strip)
            {
                // 원작형 소형 전투 창: 작업영역 높이 비례(클램프) × 가로 4배.
                h = Mathf.Clamp(Mathf.RoundToInt(waH * StripHeightFrac), StripHeightMin, StripHeightMax);
                w = Mathf.Min(waW, Mathf.RoundToInt(h * StripAspect));
            }
            else
            {
                // 세로 UI에 맞는 9:16 확장 창(작업영역 높이의 90%).
                h = Mathf.RoundToInt(waH * ExpandedHeightFrac);
                w = Mathf.RoundToInt(h * PortraitAspect);
                if (w > waW)
                {
                    w = waW;
                    h = Mathf.RoundToInt(w / PortraitAspect);
                }
            }

            // 두 모드 모두 작업표시줄 바로 위 우측에 도킹(원작의 taskbar 밀착 느낌).
            int x = wa.right - w;
            int y = wa.bottom - h;

            // Screen.SetResolution은 DPI 논리 좌표로 해석돼 물리 픽셀 창과 어긋나므로 쓰지 않는다.
            // SetWindowPos만으로 창을 조절하면 Unity가 WM_SIZE로 스왑체인을 클라이언트 크기에 맞춘다(1차 빌드 검증).
            ApplyStyleAndPos(x, y, w, h);
            Debug.Log($"[TaskbarWindow] Apply strip={strip} rect=({x},{y},{w}x{h}) screen={Screen.width}x{Screen.height}");

            // Unity가 프레임 경계에서 창을 다시 만질 수 있어, 몇 프레임 뒤 스타일·위치를 재고정한다.
            if (_reassert != null)
            {
                StopCoroutine(_reassert);
            }
            _reassert = StartCoroutine(ReassertPos(x, y, w, h));
        }

        /// <summary>테두리 없는 팝업 스타일과 위치/크기를 함께 적용한다.</summary>
        private void ApplyStyleAndPos(int x, int y, int w, int h)
        {
            SetWindowLongPtr(_hwnd, GWL_STYLE, new IntPtr((long)(WS_POPUP | WS_VISIBLE)));
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
