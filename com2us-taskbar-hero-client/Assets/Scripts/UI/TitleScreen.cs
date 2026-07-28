using UnityEngine;
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
        private bool _quitModalOpen;      // 종료 확인 모달 중복 표시 방지
        private bool _pressHeld;          // 시작 클릭 후보(버튼 눌림 → 놓을 때까지 추적)
        private bool _draggedDuringPress; // 누르고 있는 동안 창 드래그(오버레이 이동)가 발생했는지

        /// <summary>타이틀 씬 BGM을 시작한다(같은 곡이면 SoundManager가 무시하므로 재진입에도 끊기지 않는다).</summary>
        private void Awake()
        {
            SoundManager.Bgm(SoundId.BgmTitle);
        }

        private void Update()
        {
            // ESC → 종료 확인 모달. 시작 전/후(로그인 화면 포함) 모두 동작.
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                ShowQuitModal();
                return;
            }

            if (_started || _quitModalOpen)
            {
                return; // 종료 모달 표시 중에는 화면 클릭으로 게임이 시작되지 않게 한다
            }

            BlinkPressToStart();

            var pointer = Pointer.current;
            if (pointer == null)
            {
                return;
            }

            // 시작은 "눌렀다 뗐을 때". 누르고 있는 동안 창 드래그(투명 오버레이 창 이동)가
            // 발생했다면 시작 클릭이 아니라 창 이동이므로 게임을 시작하지 않는다.
            if (pointer.press.wasPressedThisFrame)
            {
                _pressHeld = true;
                _draggedDuringPress = false;
            }
            if (_pressHeld && TaskbarWindow.DraggingWindow)
            {
                _draggedDuringPress = true;
            }
            if (_pressHeld && pointer.press.wasReleasedThisFrame)
            {
                _pressHeld = false;
                if (!_draggedDuringPress)
                {
                    StartGame();
                }
            }
        }

        /// <summary>'게임을 종료하시겠습니까?' 확인/취소 모달을 띄운다('확인' 시 종료, '취소' 시 복귀).</summary>
        private void ShowQuitModal()
        {
            if (_quitModalOpen)
            {
                return;
            }
            if (ModalManager.Instance == null)
            {
                Debug.LogWarning("[TitleScreen] ModalManager가 없어 확인 없이 종료합니다.");
                QuitGame();
                return;
            }
            _quitModalOpen = true;
            ModalManager.Instance.ShowConfirmCancel(
                "게임 종료",
                "게임을 종료하시겠습니까?",
                onOk: QuitGame,
                onCancel: () => _quitModalOpen = false);
        }

        /// <summary>게임을 종료한다(에디터에서는 플레이 정지).</summary>
        private static void QuitGame()
        {
            Debug.Log("[TitleScreen] 게임 종료");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
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
            SoundManager.Sfx(SoundId.TitleStart);

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
