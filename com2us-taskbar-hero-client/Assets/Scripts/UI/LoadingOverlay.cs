using UnityEngine;
using UnityEngine.UI;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 전체 화면 로딩 오버레이(입력 차단 딤 + 회전 스피너). 로그인·회원가입 등 서버 응답을 기다리는 동안
    /// <see cref="Show"/>로 표시해 모든 UI 입력을 막고 스피너를 재생하고, 완료되면 <see cref="Hide"/>로 감춘다.
    /// 씬별 싱글턴(<see cref="Instance"/>). 계층은 에디터 빌드로 프리팹에 정적으로 굽고(코드 구성),
    /// 스피너 프레임(Assets/Art/Effect/UI/RedLoadingSpinner)은 에디터 빌더가 배선한다.
    /// </summary>
    public class LoadingOverlay : MonoBehaviour
    {
        public static LoadingOverlay Instance { get; private set; }

        private const float CanvasRefWidth = 1080f;
        private const float CanvasRefHeight = 1920f;

        [Header("스피너(에디터 빌더가 배선)")]
        [SerializeField] private Sprite[] _frames;
        [SerializeField] private float _fps = 30f;

        // 계층(에디터 빌드로 baked): 토글되는 내용 홀더 + 스피너 애니메이터.
        [SerializeField] private GameObject _content;
        [SerializeField] private SpriteSequenceAnimator _spinner;

        private bool AlreadyBuilt => _content != null;

        private void Awake()
        {
            Instance = this; // 씬별로 하나만 존재(빌더가 씬당 1개 배치)
            if (!AlreadyBuilt)
            {
                Construct();
            }
            if (_content != null)
            {
                _content.SetActive(false); // 시작은 숨김
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>로딩 오버레이를 표시한다(입력 차단 + 스피너 재생).</summary>
        public void Show()
        {
            if (_content == null)
            {
                return;
            }
            _content.SetActive(true);
            if (_spinner != null)
            {
                _spinner.Play();
            }
        }

        /// <summary>로딩 오버레이를 감춘다.</summary>
        public void Hide()
        {
            if (_content != null)
            {
                _content.SetActive(false);
            }
        }

        /// <summary>현재 표시 중인지.</summary>
        public bool IsShowing => _content != null && _content.activeSelf;

        /// <summary>에디터 빌드 전용: 전체 계층 생성.</summary>
        public void EditorConstruct() => Construct();

        private void Construct()
        {
            // 최상단 Canvas(패널·모달보다 위) — 루트는 항상 활성(Awake에서 Instance 설정).
            var canvas = gameObject.GetComponent<Canvas>();
            if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            var scaler = gameObject.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(CanvasRefWidth, CanvasRefHeight);
            scaler.matchWidthOrHeight = 0.5f;
            if (gameObject.GetComponent<GraphicRaycaster>() == null)
            {
                gameObject.AddComponent<GraphicRaycaster>();
            }

            // 토글되는 내용 홀더(블로커 + 스피너).
            _content = new GameObject("Content", typeof(RectTransform));
            _content.transform.SetParent(transform, false);
            Stretch((RectTransform)_content.transform);

            // 입력 차단(반투명 딤). raycastTarget=true라 뒤 UI 클릭을 막는다.
            var blockerGo = new GameObject("Blocker", typeof(RectTransform), typeof(Image));
            blockerGo.transform.SetParent(_content.transform, false);
            var blocker = blockerGo.GetComponent<Image>();
            blocker.color = new Color(0f, 0f, 0f, 0.55f);
            blocker.raycastTarget = true;
            Stretch(blocker.rectTransform);

            // 스피너 Image + 시퀀스 애니메이터.
            var spinnerGo = new GameObject("Spinner", typeof(RectTransform), typeof(Image));
            spinnerGo.transform.SetParent(_content.transform, false);
            var spImg = spinnerGo.GetComponent<Image>();
            spImg.raycastTarget = false;
            if (_frames != null && _frames.Length > 0)
            {
                spImg.sprite = _frames[0];
            }
            var srt = spImg.rectTransform;
            srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 0.5f);
            srt.pivot = new Vector2(0.5f, 0.5f);
            srt.anchoredPosition = Vector2.zero;
            srt.sizeDelta = new Vector2(180f, 180f);

            _spinner = spinnerGo.AddComponent<SpriteSequenceAnimator>();
            _spinner.Configure(_frames, _fps, true);

            _content.SetActive(false);
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
