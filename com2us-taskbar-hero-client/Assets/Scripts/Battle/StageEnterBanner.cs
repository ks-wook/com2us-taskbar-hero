using UnityEngine;
using UnityEngine.UI;

namespace TaskbarHero.Client.Battle
{
    /// <summary>
    /// 스테이지 입장 시 화면 상단 중앙에 "지역 · 스테이지"를 잠깐 띄웠다가 서서히 사라지는 배너.
    /// 페이드 인 → 유지 → 페이드 아웃 후 자동 파괴한다. 슬로우모션(timeScale)과 무관하게 unscaled 시간으로 재생한다.
    /// 런타임에 자체 Canvas를 코드로 구성한다.
    /// </summary>
    public class StageEnterBanner : MonoBehaviour
    {
        // 지역명(act 1~5). 스테이지 선택 UI의 지역 구성과 일치.
        private static readonly string[] RegionNames = { "평원", "얼음", "화산", "사막", "묘지" };

        private const float FadeIn = 0.35f;
        private const float Hold = 1.5f;
        private const float FadeOut = 0.9f;
        private const float RiseDistance = 40f; // 페이드아웃 시 살짝 위로 떠오름

        private CanvasGroup _cg;
        private RectTransform _content;
        private float _baseY;
        private float _t;

        /// <summary>지정 스테이지 좌표로 입장 배너를 띄운다.</summary>
        public static void Show(int act, int difficulty, int stage)
        {
            var go = new GameObject("StageEnterBanner");
            var banner = go.AddComponent<StageEnterBanner>();
            banner.Build(act, difficulty, stage);
        }

        private void Build(int act, int difficulty, int stage)
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200; // HUD(10)·패널(100) 위, 클리어 연출(300) 아래
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>().enabled = false; // 입력은 막지 않음

            _cg = gameObject.AddComponent<CanvasGroup>();
            _cg.interactable = false;
            _cg.blocksRaycasts = false;
            _cg.alpha = 0f;

            // 상단 중앙 컨테이너
            _content = NewRect("Content", transform);
            _content.anchorMin = _content.anchorMax = new Vector2(0.5f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.sizeDelta = new Vector2(900f, 200f);
            _baseY = -160f;
            _content.anchoredPosition = new Vector2(0f, _baseY);

            // 반투명 배경 바
            var bg = NewImage("Bar", _content, new Color(0f, 0f, 0f, 0.5f));
            var brt = bg.rectTransform;
            brt.anchorMin = new Vector2(0.5f, 0.5f);
            brt.anchorMax = new Vector2(0.5f, 0.5f);
            brt.pivot = new Vector2(0.5f, 0.5f);
            brt.sizeDelta = new Vector2(560f, 180f);
            brt.anchoredPosition = new Vector2(0f, -90f);

            string region = act >= 1 && act <= RegionNames.Length ? RegionNames[act - 1] : $"{act}지역";

            var regionText = NewText("Region", _content, font, $"{region} 지역", 60, TextAnchor.MiddleCenter);
            regionText.color = new Color(1f, 0.92f, 0.5f);
            regionText.fontStyle = FontStyle.Bold;
            var rrt = regionText.rectTransform;
            rrt.anchorMin = rrt.anchorMax = new Vector2(0.5f, 1f);
            rrt.pivot = new Vector2(0.5f, 1f);
            rrt.sizeDelta = new Vector2(760f, 76f);
            rrt.anchoredPosition = new Vector2(0f, -30f);

            var stageText = NewText("Stage", _content, font, $"STAGE {act}-{stage}", 44, TextAnchor.MiddleCenter);
            stageText.color = Color.white;
            var srt = stageText.rectTransform;
            srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 1f);
            srt.pivot = new Vector2(0.5f, 1f);
            srt.sizeDelta = new Vector2(760f, 60f);
            srt.anchoredPosition = new Vector2(0f, -108f);
        }

        private void Update()
        {
            _t += Time.unscaledDeltaTime;

            float alpha;
            float rise = 0f;
            if (_t < FadeIn)
            {
                alpha = _t / FadeIn;
            }
            else if (_t < FadeIn + Hold)
            {
                alpha = 1f;
            }
            else
            {
                float f = (_t - FadeIn - Hold) / FadeOut; // 0→1
                if (f >= 1f)
                {
                    Destroy(gameObject);
                    return;
                }
                alpha = 1f - f;
                rise = f * RiseDistance;
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
