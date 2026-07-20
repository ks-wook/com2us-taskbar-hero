using UnityEngine;
using UnityEngine.InputSystem;
using TaskbarHero.Client.Managers;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 타이틀 화면 컨트롤러.
    /// 로고와 'Press to start' 안내를 표시하고, 화면 어디든 클릭/터치하면
    /// 로그인 UI(<see cref="UIManager"/>)를 띄운 뒤 타이틀 화면을 숨긴다.
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

        private void Update()
        {
            if (_started)
            {
                return;
            }

            BlinkPressToStart();

            var pointer = Pointer.current;
            if (pointer != null && pointer.press.wasPressedThisFrame)
            {
                StartGame();
            }
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

        /// <summary>로그인 UI를 표시하고 타이틀 화면을 숨긴다.</summary>
        public void StartGame()
        {
            if (_started)
            {
                return;
            }

            _started = true;

            if (UIManager.Instance != null)
            {
                UIManager.Instance.ShowLogin();
            }
            else
            {
                Debug.LogError("[TitleScreen] UIManager.Instance가 없습니다. Managers 오브젝트를 확인하세요.", this);
            }

            if (titleRoot != null)
            {
                titleRoot.SetActive(false);
            }
        }
    }
}
