using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TaskbarHero.Client.Managers;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 타이틀 화면 컨트롤러.
    /// 로고와 'Press to start' 안내를 표시하고, 화면 어디든 클릭/터치하면
    /// 먼저 접속 서버 선택 UI(<see cref="ServerSelectPanelController"/>)를 띄우고, '확인' 시
    /// 로그인 UI(<see cref="UIManager"/>)를 활성화한 뒤 타이틀 화면을 숨긴다.
    /// 클릭 감지는 EventSystem 없이 Input System의 <see cref="Pointer"/>로 직접 처리한다.
    /// </summary>
    public class TitleScreen : MonoBehaviour
    {
        [Tooltip("로고/안내 문구가 들어있는 타이틀 UI 루트. 시작하면 숨긴다.")]
        [SerializeField] private GameObject titleRoot;

        [Tooltip("'Press to start' 문구를 깜빡이게 할 CanvasGroup (선택).")]
        [SerializeField] private CanvasGroup pressToStartGroup;

        [Tooltip("깜빡임 한 주기의 길이(초).")]
        [SerializeField] private float blinkPeriod = 1.2f;

        private bool _started;
        private RectTransform _quitRect; // 우측 상단 종료 버튼(수동 히트테스트)

        private void Awake()
        {
            BuildQuitButton();
        }

        private void Update()
        {
            var pointer = Pointer.current;
            bool pressed = pointer != null && pointer.press.wasPressedThisFrame;

            // 종료 버튼은 시작 전/후(로그인 화면 포함) 모두 동작하도록 _started 체크보다 먼저 처리.
            if (pressed && _quitRect != null
                && RectTransformUtility.RectangleContainsScreenPoint(_quitRect, pointer.position.ReadValue(), null))
            {
                QuitGame();
                return;
            }

            if (_started)
            {
                return;
            }

            BlinkPressToStart();

            if (pressed)
            {
                StartGame();
            }
        }

        /// <summary>게임을 종료한다(에디터에서는 플레이 정지).</summary>
        private static void QuitGame()
        {
            Debug.Log("[TitleScreen] 종료 버튼 → 게임 종료");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>우측 상단에 종료(✕) 버튼을 코드로 구성한다(타이틀 화면 전용, 최상단 캔버스).</summary>
        private void BuildQuitButton()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var canvasGo = new GameObject("QuitButtonCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 120; // 로그인 패널(100) 위
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            var btn = new GameObject("QuitButton", typeof(RectTransform), typeof(Image));
            btn.transform.SetParent(canvasGo.transform, false);
            var img = btn.GetComponent<Image>();
            img.color = new Color(0.72f, 0.22f, 0.20f, 0.96f);
            _quitRect = (RectTransform)btn.transform;
            _quitRect.anchorMin = _quitRect.anchorMax = new Vector2(1f, 1f);
            _quitRect.pivot = new Vector2(1f, 1f);
            _quitRect.anchoredPosition = new Vector2(-24f, -24f);
            _quitRect.sizeDelta = new Vector2(76f, 76f);

            var txtGo = new GameObject("X", typeof(RectTransform), typeof(Text));
            txtGo.transform.SetParent(btn.transform, false);
            var t = txtGo.GetComponent<Text>();
            t.font = font;
            t.text = "✕";
            t.fontSize = 44;
            t.fontStyle = FontStyle.Bold;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = Color.white;
            t.raycastTarget = false;
            var trt = (RectTransform)txtGo.transform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;

            Debug.Log($"[TitleScreen] 종료 버튼 구성 완료 (screen={Screen.width}x{Screen.height})");
        }

        private void BlinkPressToStart()
        {
            if (pressToStartGroup == null)
            {
                return;
            }

            float wave = 0.5f + 0.5f * Mathf.Sin(Time.time * (2f * Mathf.PI / Mathf.Max(0.01f, blinkPeriod)));
            pressToStartGroup.alpha = 0.35f + 0.65f * wave;
        }

        /// <summary>접속 서버 선택 UI를 먼저 띄우고, '확인' 시 로그인 UI를 활성화한다. 타이틀 화면은 숨긴다.</summary>
        public void StartGame()
        {
            if (_started)
            {
                return;
            }

            _started = true;

            // 로그인 이전에 접속 서버 선택 UI를 노출하고, '확인' 후 로그인 UI를 활성화한다.
            ServerSelectPanelController.Show(ShowLogin);

            if (titleRoot != null)
            {
                titleRoot.SetActive(false);
            }
        }

        /// <summary>접속 서버 확정 후 로그인 UI를 활성화한다.</summary>
        private void ShowLogin()
        {
            if (UIManager.Instance != null)
            {
                UIManager.Instance.ShowLogin();
            }
            else
            {
                Debug.LogError("[TitleScreen] UIManager.Instance가 없습니다. Managers 오브젝트를 확인하세요.", this);
            }
        }
    }
}
