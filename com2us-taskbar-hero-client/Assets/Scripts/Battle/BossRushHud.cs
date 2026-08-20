using UnityEngine;
using UnityEngine.UI;
using TaskbarHero.Client.Managers;

namespace TaskbarHero.Client.Battle
{
    /// <summary>
    /// 보스러시 도전 중 전투 화면 <b>상단 중앙</b>에 띄우는 HUD(보스러시 UI 기획서 4장) —
    /// <c>ROUND 3 / 5</c>와 경과 기록(<c>03:47.912</c>) 두 줄만 보여 준다.
    /// <para>경과 기록은 <b>그대로 서버에 보고되는 값</b>이며(<see cref="BossRushBattleFlow"/>가 측정),
    /// HUD는 받은 값을 그리기만 한다. 계층은 런타임에 코드로 구성하고 도전이 끝나면 파괴한다.</para>
    /// <para>그 라운드의 처치 진행도는 <see cref="StageProgressBar"/>를 그대로 재사용하므로 여기서 다루지 않는다.
    /// 제한 시간(10분)은 초과 시 <see cref="BossRushBattleFlow"/>가 도전을 실패로 끝내는 규칙으로만 남아 있고
    /// HUD에는 표시하지 않는다 — 잔여 게이지가 전투 화면 위쪽을 가려 라운드·기록보다 눈에 먼저 들어왔다.</para>
    /// </summary>
    public class BossRushHud : MonoBehaviour
    {
        private const float PanelWidth = 620f;
        private const float RoundHeight = 56f;
        private const float RecordGap = 2f;        // 라운드와 경과 기록 사이 간격
        private const float RecordHeight = 52f;
        /// <summary>표시 요소가 실제로 차지하는 높이(라운드 + 간격 + 기록).</summary>
        private const float ContentHeight = RoundHeight + RecordGap + RecordHeight;   // 110
        /// <summary>
        /// 화면 위쪽 끝에서 패널 상단까지(캔버스 단위). GameScene 캔버스는 높이 기준 매칭(match=1)이라
        /// 논리 세로 길이가 창 크기와 무관하게 항상 1440이므로, 이 값 하나로 세로 위치가 고정된다.
        /// <para>배경 띠 윗변 기준으로 계산해 두면(띠를 옮겨도 따라가게) 도킹된 창에서는 몬스터 머리·적 HP바와
        /// 겹치는 자리에 내려앉는다. 하늘 영역 위쪽에 두는 이 위치를 플레이 모드에서 확정했다.</para>
        /// </summary>
        private const float PanelTopY = 353f;

        private Text _roundText;
        private Text _recordText;

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

        /// <summary>표시값을 한 번에 갱신한다 — 라운드 번호·총 라운드 수·경과(ms).</summary>
        public void SetState(int round, int roundCount, int elapsedMs)
        {
            if (_roundText != null)
            {
                _roundText.text = $"ROUND {round} / {roundCount}";
            }
            if (_recordText != null)
            {
                _recordText.text = BossRushFormat.Record(Mathf.Max(1, elapsedMs));
            }
        }

        /// <summary>캔버스와 표시 요소(라운드 · 경과 기록)를 코드로 구성한다.</summary>
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
            panel.sizeDelta = new Vector2(PanelWidth, ContentHeight);
            panel.anchoredPosition = new Vector2(0f, -PanelTopY);

            // 라운드
            _roundText = NewText("Round", panel, font, "ROUND 1 / 5", 40, TextAnchor.MiddleCenter);
            _roundText.color = Color.white;
            _roundText.fontStyle = FontStyle.Bold;
            PlaceTop(_roundText.rectTransform, 0f, PanelWidth, RoundHeight);

            // 경과(= 그대로 보고될 기록)
            _recordText = NewText("Record", panel, font, "0:00.000", 38, TextAnchor.MiddleCenter);
            _recordText.color = new Color(1f, 1f, 1f, 0.92f);
            PlaceTop(_recordText.rectTransform, RoundHeight + RecordGap, PanelWidth, RecordHeight);
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
