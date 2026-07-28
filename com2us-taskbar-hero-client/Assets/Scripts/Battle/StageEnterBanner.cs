using UnityEngine;
using UnityEngine.UI;
using TaskbarHero.Client.Managers;

namespace TaskbarHero.Client.Battle
{
    /// <summary>
    /// 스테이지 입장 시 화면 중단(전투 레인 위쪽)에 "지역 · 스테이지"를 잠깐 띄웠다가 서서히 사라지는 배너.
    /// 페이드 인 → 유지 → 페이드 아웃 후 자동 파괴한다. 슬로우모션(timeScale)과 무관하게 unscaled 시간으로 재생한다.
    /// <para>
    /// 정적 계층(Canvas·배경 바·지역/스테이지 텍스트)은 <b>프리팹으로 baked</b>되어(에디터 빌더 <c>StageEnterBannerBuilder</c>)
    /// 인스펙터에서 손쉽게 수정할 수 있다. 런타임에는 <see cref="DungeonBattleFlow"/>가 프리팹을 Instantiate해
    /// <see cref="Play"/>로 문구를 채우고 재생한다. 프리팹이 배선되지 않았을 때를 대비해 코드로 계층을 생성하는
    /// 폴백(<see cref="Show"/>·<see cref="Construct"/>)도 유지한다.
    /// </para>
    /// </summary>
    public class StageEnterBanner : MonoBehaviour
    {
        // 지역명(act 1~5). 스테이지 선택 UI의 지역 구성과 일치.
        private static readonly string[] RegionNames = { "평원", "얼음", "화산", "사막", "묘지" };

        [Header("연출 타이밍 (인스펙터에서 수정 가능)")]
        [Tooltip("페이드 인 시간(초).")]
        [SerializeField] private float fadeIn = 0.35f;
        [Tooltip("완전 표시 유지 시간(초).")]
        [SerializeField] private float hold = 1.5f;
        [Tooltip("페이드 아웃 시간(초).")]
        [SerializeField] private float fadeOut = 0.9f;
        [Tooltip("페이드 아웃 시 위로 떠오르는 거리(px).")]
        [SerializeField] private float riseDistance = 40f;

        [Header("구성 참조 (에디터 빌더가 배선 — 직접 수정 불필요)")]
        [SerializeField] private CanvasGroup _cg;
        [SerializeField] private RectTransform _content;
        [SerializeField] private Text _regionText;
        [SerializeField] private Text _stageText;

        private float _baseY;
        private float _t;
        private bool _playing;

        private bool AlreadyBuilt => _content != null;

        private void Awake()
        {
            if (!AlreadyBuilt)
            {
                Construct(); // 폴백(프리팹 없이 붙은 경우)
            }
        }

        /// <summary>에디터 빌드 전용: 정적 계층을 생성하고 참조를 배선한다(프리팹 저장용).</summary>
        public void EditorConstruct()
        {
            Construct();
        }

        /// <summary>지정 스테이지 좌표로 입장 배너를 코드로 생성해 띄운다(프리팹 미배선 시 폴백).</summary>
        public static void Show(int act, int difficulty, int stage)
        {
            var go = new GameObject("StageEnterBanner");
            var banner = go.AddComponent<StageEnterBanner>(); // Awake에서 Construct()
            banner.Play(act, difficulty, stage);
        }

        /// <summary>이미 생성된(프리팹) 배너에 스테이지 문구를 채우고 페이드 연출을 시작한다.</summary>
        public void Play(int act, int difficulty, int stage)
        {
            if (!AlreadyBuilt)
            {
                Construct();
            }

            string region = act >= 1 && act <= RegionNames.Length ? RegionNames[act - 1] : $"{act}지역";
            if (_regionText != null)
            {
                _regionText.text = $"{region} 지역";
            }
            if (_stageText != null)
            {
                _stageText.text = $"STAGE {act}-{stage}";
            }

            _baseY = _content != null ? _content.anchoredPosition.y : -560f;
            _t = 0f;
            _playing = true;
            if (_cg != null)
            {
                _cg.alpha = 0f;
            }
        }

        private void Update()
        {
            if (!_playing)
            {
                return;
            }

            _t += Time.unscaledDeltaTime;

            float alpha;
            float rise = 0f;
            if (_t < fadeIn)
            {
                alpha = _t / fadeIn;
            }
            else if (_t < fadeIn + hold)
            {
                alpha = 1f;
            }
            else
            {
                float f = (_t - fadeIn - hold) / fadeOut; // 0→1
                if (f >= 1f)
                {
                    Destroy(gameObject);
                    return;
                }
                alpha = 1f - f;
                rise = f * riseDistance;
            }

            if (_cg != null)
            {
                _cg.alpha = alpha;
            }
            if (_content != null)
            {
                _content.anchoredPosition = new Vector2(0f, _baseY + rise);
            }
        }

        // ── 계층 생성 ──

        /// <summary>Canvas·배경 바·지역/스테이지 텍스트 계층을 생성하고 직렬화 참조를 채운다.</summary>
        private void Construct()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var canvas = gameObject.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = gameObject.AddComponent<Canvas>();
            }
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = UiSortingOrder.BattleEnterBanner; // HUD·진행 바 위, 기능 패널 아래

            var scaler = gameObject.GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                scaler = gameObject.AddComponent<CanvasScaler>();
            }
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            if (gameObject.GetComponent<GraphicRaycaster>() == null)
            {
                gameObject.AddComponent<GraphicRaycaster>().enabled = false; // 입력은 막지 않음
            }

            _cg = gameObject.GetComponent<CanvasGroup>();
            if (_cg == null)
            {
                _cg = gameObject.AddComponent<CanvasGroup>();
            }
            _cg.interactable = false;
            _cg.blocksRaycasts = false;
            _cg.alpha = 0f;

            // 중앙 컨테이너(상단 앵커 기준으로 아래로 내려 전투 레인에 가깝게 노출)
            _content = NewRect("Content", transform);
            _content.anchorMin = _content.anchorMax = new Vector2(0.5f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.sizeDelta = new Vector2(900f, 200f);
            _content.anchoredPosition = new Vector2(0f, -560f);

            // 반투명 배경 바
            var bg = NewImage("Bar", _content, new Color(0f, 0f, 0f, 0.5f));
            var brt = bg.rectTransform;
            brt.anchorMin = new Vector2(0.5f, 0.5f);
            brt.anchorMax = new Vector2(0.5f, 0.5f);
            brt.pivot = new Vector2(0.5f, 0.5f);
            brt.sizeDelta = new Vector2(560f, 180f);
            brt.anchoredPosition = new Vector2(0f, -90f);

            _regionText = NewText("Region", _content, font, "지역", 60, TextAnchor.MiddleCenter);
            _regionText.color = new Color(1f, 0.92f, 0.5f);
            _regionText.fontStyle = FontStyle.Bold;
            var rrt = _regionText.rectTransform;
            rrt.anchorMin = rrt.anchorMax = new Vector2(0.5f, 1f);
            rrt.pivot = new Vector2(0.5f, 1f);
            rrt.sizeDelta = new Vector2(760f, 76f);
            rrt.anchoredPosition = new Vector2(0f, -30f);

            _stageText = NewText("Stage", _content, font, "STAGE", 44, TextAnchor.MiddleCenter);
            _stageText.color = Color.white;
            var srt = _stageText.rectTransform;
            srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 1f);
            srt.pivot = new Vector2(0.5f, 1f);
            srt.sizeDelta = new Vector2(760f, 60f);
            srt.anchoredPosition = new Vector2(0f, -108f);
        }

        // ── UI 헬퍼 ──

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static Image NewImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private static Text NewText(string name, Transform parent, Font font, string content, int size, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = font;
            t.text = content;
            t.fontSize = size;
            t.alignment = anchor;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }
    }
}
