using UnityEngine;
using UnityEngine.UI;
using TaskbarHero.Client.Managers;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 자동 로그인 성공을 알리는 환영 배너. 화면 <b>중앙 상단</b>에 "{이메일} 님 환영합니다." 문구가
    /// 위에서 <b>내려왔다가</b> 잠깐 머문 뒤 <b>다시 올라가</b> 사라진다(끝나면 자동 파괴).
    ///
    /// <para>런타임에 자체 Canvas를 코드로 구성한다 — 타이틀 화면에만 잠깐 뜨는 연출이라 씬·프리팹에
    /// 굽지 않는다(보스 경고 배너 <see cref="Battle.BossWarningBanner"/>와 같은 방식).</para>
    ///
    /// <para>시간은 <c>unscaledDeltaTime</c>으로 흐르므로 로딩 중 <c>timeScale</c> 변화에 영향받지 않는다.</para>
    /// </summary>
    public class WelcomeBanner : MonoBehaviour
    {
        private const float SlideIn = 0.45f;   // 내려오는 시간(초)
        // 머무는 시간(초). 1.6초는 문구를 읽기 전에 사라져 계정을 확인할 틈이 없었다 —
        // 이메일 한 줄을 읽고 "내 계정이 맞나" 확인할 여유를 두려고 늘렸다.
        // 배너가 떠 있어도 화면을 누르면 바로 게임에 들어가므로, 길어도 진입을 막지 않는다.
        private const float Hold = 3.2f;
        private const float SlideOut = 0.4f;   // 올라가는 시간(초)

        private const float BannerWidth = 720f;
        private const float BannerHeight = 96f;
        private const float RestY = -84f;      // 내려와 머무는 위치(화면 상단 기준, 아래로 −)

        private RectTransform _content;
        private float _hiddenY;                // 화면 위로 숨은 위치
        private float _t;

        /// <summary>환영 배너를 띄운다(중복 방지: 이미 떠 있으면 무시).</summary>
        /// <param name="email">계정 이메일. 비어 있으면 이메일 없이 "환영합니다."만 표시한다.</param>
        public static void Show(string email)
        {
            if (Object.FindAnyObjectByType<WelcomeBanner>() != null)
            {
                return;
            }
            var go = new GameObject("WelcomeBanner");
            go.AddComponent<WelcomeBanner>().Build(email);
        }

        private void Build(string email)
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = UiSortingOrder.Topmost; // 타이틀 로고·안내 위에 뜬다
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            GameViewLayout.ApplyCurrentScaler(scaler);
            gameObject.AddComponent<GraphicRaycaster>().enabled = false; // 화면 클릭을 막지 않는다

            // 화면 상단 중앙에 매달고, 시작은 화면 위(보이지 않는 곳)에 둔다.
            _content = NewRect("Content", transform);
            _content.anchorMin = _content.anchorMax = new Vector2(0.5f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.sizeDelta = new Vector2(BannerWidth, BannerHeight);
            _hiddenY = BannerHeight + 20f; // 배너 높이만큼 위로 완전히 빼 둔다
            _content.anchoredPosition = new Vector2(0f, _hiddenY);

            // 배경 띠(글자 가독성) — 반투명 검정.
            var bg = new GameObject("Bg", typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(_content, false);
            var bgImg = bg.GetComponent<Image>();
            bgImg.color = new Color(0f, 0f, 0f, 0.55f);
            bgImg.raycastTarget = false;
            var brt = bgImg.rectTransform;
            brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one;
            brt.offsetMin = Vector2.zero; brt.offsetMax = Vector2.zero;

            string label = string.IsNullOrEmpty(email) ? "환영합니다." : $"{email} 님 환영합니다.";
            var text = NewText("Message", _content, font, label, 34);
            text.color = new Color(1f, 0.94f, 0.78f);
            text.fontStyle = FontStyle.Bold;
            var trt = text.rectTransform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(16f, 0f);
            trt.offsetMax = new Vector2(-16f, 0f);
        }

        private void Update()
        {
            _t += Time.unscaledDeltaTime;
            float total = SlideIn + Hold + SlideOut;
            if (_t >= total)
            {
                Destroy(gameObject);
                return;
            }
            if (_content == null)
            {
                return;
            }

            float y;
            if (_t < SlideIn)
            {
                // 내려오기 — 끝에서 부드럽게 멎는다.
                y = Mathf.Lerp(_hiddenY, RestY, EaseOutCubic(_t / SlideIn));
            }
            else if (_t < SlideIn + Hold)
            {
                y = RestY;
            }
            else
            {
                // 올라가기 — 처음엔 천천히, 뒤로 갈수록 빠르게 빠진다.
                float k = (_t - SlideIn - Hold) / SlideOut;
                y = Mathf.Lerp(RestY, _hiddenY, EaseInCubic(k));
            }
            _content.anchoredPosition = new Vector2(0f, y);
        }

        private static float EaseOutCubic(float k)
        {
            float p = 1f - Mathf.Clamp01(k);
            return 1f - p * p * p;
        }

        private static float EaseInCubic(float k)
        {
            k = Mathf.Clamp01(k);
            return k * k * k;
        }

        // ── UI 헬퍼 ──

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static Text NewText(string name, Transform parent, Font font, string content, int size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = font;
            t.text = content;
            t.fontSize = size;
            t.alignment = TextAnchor.MiddleCenter;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }
    }
}
