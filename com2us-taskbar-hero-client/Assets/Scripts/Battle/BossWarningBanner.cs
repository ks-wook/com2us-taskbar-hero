using UnityEngine;
using UnityEngine.UI;
using TaskbarHero.Client.Managers;

namespace TaskbarHero.Client.Battle
{
    /// <summary>
    /// 보스 몬스터 등장 시 화면 중앙에 붉은색 "Warning!!" 문구를 커졌다가 돌아오는 펄스로 3번 반복해 강조한다.
    /// 슬로우모션(timeScale)과 무관하게 unscaled 시간으로 재생하고, 연출이 끝나면 자동 파괴한다.
    /// 런타임에 자체 Canvas를 코드로 구성한다.
    /// </summary>
    public class BossWarningBanner : MonoBehaviour
    {
        private const int Pulses = 3;         // 커졌다가 돌아오는 반복 횟수
        private const float PulsePeriod = 0.55f; // 한 번 펄스(확대→복귀) 주기(초)
        private const float BaseScale = 1f;
        private const float PeakScale = 1.7f;    // 펄스 최대 배율
        private const float FadeIn = 0.12f;
        private const float FadeOut = 0.35f;

        private CanvasGroup _cg;
        private RectTransform _content;
        private float _t;

        /// <summary>보스 등장 경고 배너를 화면 중앙에 띄운다(중복 방지: 기존 배너가 있으면 무시).</summary>
        public static void Show()
        {
            if (Object.FindAnyObjectByType<BossWarningBanner>() != null)
            {
                return; // 이미 재생 중이면 중복 생성하지 않음
            }
            var go = new GameObject("BossWarningBanner");
            var banner = go.AddComponent<BossWarningBanner>();
            banner.Build();
        }

        private void Build()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = UiSortingOrder.BattleBossWarning; // 입장 배너 위·클리어 연출 아래, 기능 패널보다는 아래
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            // 현재 씬 규격(GameScene은 높이 1440 기준)으로 즉시 맞춘다 — 주기 스윕을 기다리면 첫 표시 때 잠깐 크게 그려진다.
            GameViewLayout.ApplyCurrentScaler(scaler);
            gameObject.AddComponent<GraphicRaycaster>().enabled = false; // 입력은 막지 않음

            _cg = gameObject.AddComponent<CanvasGroup>();
            _cg.interactable = false;
            _cg.blocksRaycasts = false;
            _cg.alpha = 0f;

            // 화면 정중앙 컨테이너(펄스 스케일 대상)
            _content = NewRect("Content", transform);
            _content.anchorMin = _content.anchorMax = new Vector2(0.5f, 0.5f);
            _content.pivot = new Vector2(0.5f, 0.5f);
            _content.sizeDelta = new Vector2(900f, 240f);
            _content.anchoredPosition = Vector2.zero;

            // 그림자(가독성) — 살짝 오프셋된 검은 텍스트
            var shadow = NewText("Shadow", _content, font, "Warning!!", 150, TextAnchor.MiddleCenter);
            shadow.color = new Color(0f, 0f, 0f, 0.6f);
            shadow.fontStyle = FontStyle.Bold;
            var shrt = shadow.rectTransform;
            shrt.anchorMin = Vector2.zero; shrt.anchorMax = Vector2.one;
            shrt.offsetMin = new Vector2(6f, -6f);
            shrt.offsetMax = new Vector2(6f, -6f);

            var text = NewText("Warning", _content, font, "Warning!!", 150, TextAnchor.MiddleCenter);
            text.color = new Color(0.9f, 0.1f, 0.1f); // 붉은색
            text.fontStyle = FontStyle.Bold;
            var trt = text.rectTransform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
        }

        private void Update()
        {
            _t += Time.unscaledDeltaTime;

            float total = Pulses * PulsePeriod;
            if (_t >= total)
            {
                Destroy(gameObject);
                return;
            }

            // 스케일 펄스: 한 주기마다 1→PeakScale→1 (sin 반파). 3주기 = 3번 반복.
            float phase = (_t % PulsePeriod) / PulsePeriod;      // 0~1
            float pulse = Mathf.Sin(phase * Mathf.PI);           // 0→1→0
            float scale = Mathf.Lerp(BaseScale, PeakScale, pulse);
            if (_content != null)
            {
                _content.localScale = new Vector3(scale, scale, 1f);
            }

            // 알파: 시작에 빠르게 나타나고 끝에 사라진다.
            float alpha = 1f;
            if (_t < FadeIn)
            {
                alpha = _t / FadeIn;
            }
            else if (_t > total - FadeOut)
            {
                alpha = Mathf.Max(0f, (total - _t) / FadeOut);
            }
            if (_cg != null)
            {
                _cg.alpha = alpha;
            }
        }

        // ── UI 헬퍼 ──

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
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
