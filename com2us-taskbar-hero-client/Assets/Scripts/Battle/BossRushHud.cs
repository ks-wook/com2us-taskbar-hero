using UnityEngine;
using UnityEngine.UI;
using TaskbarHero.Client.Managers;

namespace TaskbarHero.Client.Battle
{
    /// <summary>
    /// 보스러시 도전 중 전투 화면 <b>상단 중앙</b>에 띄우는 HUD(보스러시 UI 기획서 4장) —
    /// <c>ROUND 3 / 5</c> · 제한 시간 게이지(10:00에서 감소, 1분 미만이면 붉게) · 경과 기록(<c>03:47.912</c>)을 보여 준다.
    /// <para>경과 기록은 <b>그대로 서버에 보고되는 값</b>이며(<see cref="BossRushBattleFlow"/>가 측정),
    /// HUD는 받은 값을 그리기만 한다. 계층은 런타임에 코드로 구성하고 도전이 끝나면 파괴한다.</para>
    /// <para>그 라운드의 처치 진행도는 <see cref="StageProgressBar"/>를 그대로 재사용하므로 여기서 다루지 않는다.</para>
    /// </summary>
    public class BossRushHud : MonoBehaviour
    {
        private const float PanelWidth = 620f;
        private const float PanelTopY = 46f;      // 화면 위쪽 끝에서 패널 상단까지
        private const float RoundHeight = 56f;
        private const float GaugeWidth = 520f;
        private const float GaugeHeight = 26f;
        private const float GaugeInset = 3f;
        private const float RecordHeight = 52f;

        private static readonly Color TimeNormal = new Color(1f, 0.95f, 0.72f);
        private static readonly Color TimeUrgent = new Color(1f, 0.36f, 0.32f);   // 1분 미만
        private static readonly Color GaugeNormal = new Color(0.42f, 0.78f, 1f);
        private static readonly Color GaugeUrgent = new Color(0.95f, 0.28f, 0.26f);

        /// <summary>제한 시간이 이만큼(초) 미만으로 남으면 붉게 표시한다.</summary>
        private const float UrgentSeconds = 60f;

        private Text _roundText;
        private Text _timeText;
        private Text _recordText;
        private RectTransform _gaugeFill;
        private Image _gaugeFillImage;

        /// <summary>HUD를 생성한다(이미 떠 있으면 그것을 돌려준다).</summary>
        public static BossRushHud Show()
        {
            var existing = FindAnyObjectByType<BossRushHud>();
            if (existing != null)
            {
                return existing;
            }
            var go = new GameObject("BossRushHud");
            var hud = go.AddComponent<BossRushHud>();
            hud.Build();
            return hud;
        }

        /// <summary>HUD를 닫는다(도전 종료 시).</summary>
        public void Close()
        {
            Destroy(gameObject);
        }

        /// <summary>표시값을 한 번에 갱신한다 — 라운드 번호·총 라운드 수·경과(ms)·잔여(ms)·제한 시간(ms).</summary>
        public void SetState(int round, int roundCount, int elapsedMs, int remainingMs, int timeLimitMs)
        {
            if (_roundText != null)
            {
                _roundText.text = $"ROUND {round} / {roundCount}";
            }

            bool urgent = remainingMs < UrgentSeconds * 1000f;
            if (_timeText != null)
            {
                _timeText.text = BossRushFormat.Remaining(remainingMs);
                _timeText.color = urgent ? TimeUrgent : TimeNormal;
            }
            if (_recordText != null)
            {
                _recordText.text = BossRushFormat.Record(Mathf.Max(1, elapsedMs));
            }
            if (_gaugeFill != null)
            {
                float ratio = timeLimitMs > 0 ? Mathf.Clamp01((float)remainingMs / timeLimitMs) : 0f;
                float inner = GaugeWidth - GaugeInset * 2f;
                _gaugeFill.sizeDelta = new Vector2(inner * ratio, GaugeHeight - GaugeInset * 2f);
                if (_gaugeFillImage != null)
                {
                    _gaugeFillImage.color = urgent ? GaugeUrgent : GaugeNormal;
                }
            }
        }

        /// <summary>캔버스와 표시 요소(라운드 · 제한 시간 게이지 · 경과 기록)를 코드로 구성한다.</summary>
        private void Build()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = UiSortingOrder.StageProgress; // 진행 바와 같은 띠(전투 연출보다 아래)
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            GameViewLayout.ApplyCurrentScaler(scaler);

            var panel = NewRect("Panel", transform);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 1f);
            panel.pivot = new Vector2(0.5f, 1f);
            panel.sizeDelta = new Vector2(PanelWidth, RoundHeight + GaugeHeight + RecordHeight + 16f);
            panel.anchoredPosition = new Vector2(0f, -PanelTopY);

            // 라운드
            _roundText = NewText("Round", panel, font, "ROUND 1 / 5", 40, TextAnchor.MiddleCenter);
            _roundText.color = Color.white;
            _roundText.fontStyle = FontStyle.Bold;
            PlaceTop(_roundText.rectTransform, 0f, PanelWidth, RoundHeight);

            // 제한 시간 게이지(트랙 + 채움) + 잔여 시각
            var track = NewImage("GaugeTrack", panel, new Color(0f, 0f, 0f, 0.55f));
            PlaceTop(track.rectTransform, RoundHeight, GaugeWidth, GaugeHeight);

            var fill = NewImage("GaugeFill", track.rectTransform, GaugeNormal);
            _gaugeFill = fill.rectTransform;
            _gaugeFillImage = fill;
            _gaugeFill.anchorMin = _gaugeFill.anchorMax = new Vector2(0f, 0.5f);
            _gaugeFill.pivot = new Vector2(0f, 0.5f);
            _gaugeFill.anchoredPosition = new Vector2(GaugeInset, 0f);
            _gaugeFill.sizeDelta = new Vector2(GaugeWidth - GaugeInset * 2f, GaugeHeight - GaugeInset * 2f);

            // 게이지 위 잔여 시각. rect 높이는 글자 크기보다 넉넉히 준다
            // (Text는 줄 높이가 rect를 넘으면 그 줄을 통째로 렌더링하지 않는다).
            _timeText = NewText("Time", track.rectTransform, font, "10:00", 22, TextAnchor.MiddleCenter);
            _timeText.color = TimeNormal;
            _timeText.fontStyle = FontStyle.Bold;
            var trt = _timeText.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.5f);
            trt.pivot = new Vector2(0.5f, 0.5f);
            trt.sizeDelta = new Vector2(GaugeWidth, GaugeHeight + 8f);
            trt.anchoredPosition = Vector2.zero;

            // 경과(= 그대로 보고될 기록)
            _recordText = NewText("Record", panel, font, "0:00.000", 38, TextAnchor.MiddleCenter);
            _recordText.color = new Color(1f, 1f, 1f, 0.92f);
            PlaceTop(_recordText.rectTransform, RoundHeight + GaugeHeight + 8f, PanelWidth, RecordHeight);
        }

        // ── UI 헬퍼 ──

        /// <summary>부모 위쪽 가운데 기준으로 놓는다(y는 아래로 +).</summary>
        private static void PlaceTop(RectTransform rt, float y, float width, float height)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(0f, -y);
        }

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
            img.raycastTarget = false; // 클릭 통과·창 드래그를 막지 않는다
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
