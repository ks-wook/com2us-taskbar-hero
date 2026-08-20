using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TaskbarHero.Client.Managers;

namespace TaskbarHero.Client.Battle
{
    /// <summary>
    /// 보스러시 도전 중 전투 화면 <b>상단 중앙</b>에 띄우는 HUD(보스러시 UI 기획서 4장) —
    /// <c>RECORD</c> 제목과 경과 기록(<c>03:47.912</c>) 두 줄만 보여 준다.
    /// <para><b>라운드 번호는 표시하지 않는다</b> — 라운드는 진입 배너(명판 + 숫자 아트)가 알려 주고,
    /// 전투 중 상단에 남겨 두는 값은 <b>기록</b> 하나로 좁혔다.</para>
    /// <para>경과 기록은 <b>그대로 서버에 보고되는 값</b>이며(<see cref="BossRushBattleFlow"/>가 측정),
    /// HUD는 받은 값을 그리기만 한다. 계층은 런타임에 코드로 구성하고 도전이 끝나면 파괴한다.</para>
    /// <para>그 라운드의 처치 진행도는 <see cref="StageProgressBar"/>를 그대로 재사용하므로 여기서 다루지 않는다.
    /// <b>제한 시간은 없다</b> — 경과 기록은 랭킹 점수일 뿐이고 전투를 끊는 상한이 아니다.</para>
    /// <para><b>타이머는 픽셀 폰트 + 글자별 고정 칸(등폭)</b>으로 그린다. 기본 UI 폰트는 자릿폭이 달라
    /// 밀리초가 바뀔 때마다 문자열 폭이 흔들리는데, 타이머는 매 프레임 갱신되므로 그 흔들림이 그대로 보인다.
    /// 그래서 글자를 한 칸씩 나눠 숫자 칸 폭을 고정하고(구분자 <c>:</c>·<c>.</c>는 좁은 칸) 전체를 가운데 정렬한다.
    /// 폰트는 <see cref="BossRushBattleFlow"/>가 배선해 넘겨주며, 없으면 기본 폰트로 같은 등폭 배치를 쓴다.</para>
    /// </summary>
    public class BossRushHud : MonoBehaviour
    {
        private const float PanelWidth = 620f;
        private const float LabelHeight = 60f;     // 'RECORD' 제목 줄(픽셀 폰트가 들어갈 여유를 둔 높이)
        private const float RecordGap = 16f;       // 제목과 기록 사이 간격(합이 76 — 타이머 위치는 그대로 유지)
        private const float RecordHeight = 52f;
        /// <summary>표시 요소가 실제로 차지하는 높이(제목 + 간격 + 기록).</summary>
        private const float ContentHeight = LabelHeight + RecordGap + RecordHeight;   // 128
        /// <summary>제목 글자 크기(픽셀 폰트 기준). 픽셀 폰트는 em 대비 글자가 작아(기본 폰트의 0.66배)
        /// 같은 수치로 두면 작아 보이므로 키운다. 줄 높이가 <see cref="LabelHeight"/>를 넘지 않게 4px 여유를 남긴다.</summary>
        private const int LabelFontSizePixel = 56;
        /// <summary>제목 글자 크기(기본 폰트 폴백). 픽셀 폰트가 배선되지 않았을 때 쓴다.</summary>
        private const int LabelFontSizeFallback = 40;
        /// <summary>
        /// 화면 위쪽 끝에서 패널 상단까지(캔버스 단위). GameScene 캔버스는 높이 기준 매칭(match=1)이라
        /// 논리 세로 길이가 창 크기와 무관하게 항상 1440이므로, 이 값 하나로 세로 위치가 고정된다.
        /// <para>배경 띠 윗변 기준으로 계산해 두면(띠를 옮겨도 따라가게) 도킹된 창에서는 몬스터 머리·적 HP바와
        /// 겹치는 자리에 내려앉는다. 하늘 영역 위쪽에 두는 이 위치를 플레이 모드에서 확정했다.</para>
        /// </summary>
        private const float PanelTopY = 353f;

        /// <summary>타이머 글자 크기(px). 픽셀 폰트의 획이 고르게 나오는 크기로 잡았다.</summary>
        private const int RecordFontSize = 40;
        /// <summary>숫자 한 칸의 폭(글자 크기 대비). 픽셀 폰트 숫자 자폭(0.68em)에 약간의 자간을 더한 값.</summary>
        private const float RecordDigitCellRatio = 0.72f;
        /// <summary>구분자(<c>:</c>·<c>.</c>) 한 칸의 폭(글자 크기 대비) — 숫자보다 좁게 둔다.</summary>
        private const float RecordSepCellRatio = 0.34f;

        private Text _labelText;
        private RectTransform _recordRow;
        private Font _recordFont;
        private readonly List<Text> _recordCells = new List<Text>();

        /// <summary>HUD를 생성한다(이미 떠 있으면 그것을 돌려준다).
        /// <paramref name="timerFont"/>는 타이머에 쓸 픽셀 폰트로, null이면 기본 UI 폰트를 쓴다.</summary>
        public static BossRushHud Show(Font timerFont = null)
        {
            var existing = FindAnyObjectByType<BossRushHud>();
            if (existing != null)
            {
                return existing;
            }
            var go = new GameObject("BossRushHud");
            var hud = go.AddComponent<BossRushHud>();
            hud.Build(timerFont);
            return hud;
        }

        /// <summary>HUD를 닫는다(도전 종료 시).</summary>
        public void Close()
        {
            Destroy(gameObject);
        }

        /// <summary>표시값을 갱신한다 — 경과(ms). 제목(<c>RECORD</c>)은 고정이라 갱신할 것이 없다.</summary>
        public void SetElapsed(int elapsedMs)
        {
            SetRecord(BossRushFormat.Record(Mathf.Max(1, elapsedMs)));
        }

        /// <summary>캔버스와 표시 요소(RECORD 제목 · 경과 기록)를 코드로 구성한다.</summary>
        private void Build(Font timerFont)
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _recordFont = timerFont != null ? timerFont : font;
            if (timerFont != null)
            {
                // 픽셀 폰트는 아틀라스 필터가 Bilinear면 획이 뭉개진다 — Point로 고정한다.
                PixelFontAtlas.KeepCrisp(timerFont);
            }

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

            // 제목(RECORD) — 기록과 같은 픽셀 폰트로 쓴다. 픽셀 폰트에는 볼드를 쓰지 않는다
            // (합성 볼드가 글리프를 밀어 겹쳐 그려 획이 뭉개진다 — 데미지 숫자와 같은 이유).
            bool pixel = timerFont != null;
            _labelText = NewText("RecordLabel", panel, _recordFont, "RECORD",
                                 pixel ? LabelFontSizePixel : LabelFontSizeFallback, TextAnchor.MiddleCenter);
            _labelText.color = Color.white;
            _labelText.fontStyle = pixel ? FontStyle.Normal : FontStyle.Bold;
            PlaceTop(_labelText.rectTransform, 0f, PanelWidth, LabelHeight);

            // 경과(= 그대로 보고될 기록). 글자별 칸으로 그리므로 여기서는 담을 행만 만든다.
            _recordRow = NewRect("Record", panel);
            PlaceTop(_recordRow, LabelHeight + RecordGap, PanelWidth, RecordHeight);
            SetRecord(BossRushFormat.Record(1));
        }

        // ── 타이머(등폭 배치) ──

        /// <summary>타이머 문자열을 글자별 고정 칸에 채운다 — 숫자 칸 폭이 고정이라 값이 바뀌어도 흔들리지 않는다.
        /// 자리 수가 늘어나면(<c>10:00.000</c>) 칸을 더 만들고, 줄어들면 남는 칸을 끈다.</summary>
        private void SetRecord(string record)
        {
            if (_recordRow == null || string.IsNullOrEmpty(record))
            {
                return;
            }

            float total = 0f;
            for (int i = 0; i < record.Length; i++)
            {
                total += CellWidth(record[i]);
            }

            float cursor = -total * 0.5f;
            for (int i = 0; i < record.Length; i++)
            {
                float w = CellWidth(record[i]);
                var cell = CellAt(i);
                cell.text = record[i].ToString();
                var rt = cell.rectTransform;
                rt.sizeDelta = new Vector2(w, RecordHeight);
                rt.anchoredPosition = new Vector2(cursor + w * 0.5f, 0f);
                cell.gameObject.SetActive(true);
                cursor += w;
            }

            for (int i = record.Length; i < _recordCells.Count; i++)
            {
                _recordCells[i].gameObject.SetActive(false);
            }
        }

        /// <summary>글자 한 칸의 폭 — 숫자는 같은 폭, 구분자(<c>:</c>·<c>.</c>)는 좁은 폭.</summary>
        private static float CellWidth(char c)
        {
            return char.IsDigit(c) ? RecordFontSize * RecordDigitCellRatio : RecordFontSize * RecordSepCellRatio;
        }

        /// <summary>i번째 글자 칸을 얻는다(없으면 만들어 붙인다).</summary>
        private Text CellAt(int i)
        {
            while (_recordCells.Count <= i)
            {
                var cell = NewText($"Cell{_recordCells.Count}", _recordRow, _recordFont, "0",
                                   RecordFontSize, TextAnchor.MiddleCenter);
                cell.color = new Color(1f, 1f, 1f, 0.92f);
                var rt = cell.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                _recordCells.Add(cell);
            }
            return _recordCells[i];
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
