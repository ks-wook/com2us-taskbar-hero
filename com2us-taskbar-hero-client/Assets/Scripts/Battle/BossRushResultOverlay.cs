using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TaskbarHero.Client.Managers;
using TaskbarHero.Common.Dto;

namespace TaskbarHero.Client.Battle
{
    /// <summary>
    /// 보스러시 완주(5라운드 클리어) 결과 연출(보스러시 UI 기획서 5장).
    /// <b>기록·순위·라운드 내역이 주인공</b>이라 스테이지 클리어 오버레이를 그대로 쓰지 않고 전용으로 둔다.
    /// <para>구성은 위에서부터 ① 기록(<c>3:34.380</c>, 갱신 시 그 위에 <c>신기록!</c> 배지가 작은 크기에서
    /// 커지며 등장) ② 순위 ③ 라운드별 소요 5행이며, <b>보상 줄은 두지 않는다</b> — 보스러시는 재화·아이템을
    /// 주지 않고(<c>clear</c> 응답에도 보상 필드가 없다) 보상은 시즌 정산의 순위 보상(메일)뿐이다.</para>
    /// <para>시간은 전부 <c>unscaled</c>이며, 클릭하거나 <see cref="AutoCloseSeconds"/>가 지나면 닫히고
    /// 콜백(던전 스테이지 복귀)을 1회 호출한다.</para>
    /// </summary>
    public class BossRushResultOverlay : MonoBehaviour
    {
        private const float AutoCloseSeconds = 6f;

        // 신기록 배지 등장 연출(패배 타이틀·뽑기 결과 명판과 같은 back-out 이징).
        private const float BadgePopDuration = 0.42f;
        private const float BadgePopStartScale = 0.25f;
        private const float BadgePopOvershoot = 1.4f;

        private static readonly Color Gold = new Color(1f, 0.82f, 0.29f);
        private static readonly Color Dim = new Color(1f, 1f, 1f, 0.82f);

        private CanvasGroup _cg;
        private RectTransform _badge;
        private float _t;
        private bool _dismissed;
        private Action _onClosed;
        private BossRushClearResultData _data;
        private List<BossRushRoundTimeDto> _rounds;

        /// <summary>결과 연출을 생성·표시한다. <paramref name="rounds"/>는 라운드별 소요(1~5),
        /// <paramref name="onClosed"/>는 닫힐 때(클릭/자동) 1회 호출된다.</summary>
        public static BossRushResultOverlay Show(BossRushClearResultData data, List<BossRushRoundTimeDto> rounds,
                                                 Action onClosed = null)
        {
            var go = new GameObject("BossRushResultOverlay");
            var overlay = go.AddComponent<BossRushResultOverlay>();
            overlay._data = data;
            overlay._rounds = rounds;
            overlay._onClosed = onClosed;
            overlay.Build();
            return overlay;
        }

        /// <summary>캔버스·기록·순위·라운드 내역 계층을 코드로 구성하고 완주 징글을 울린다.</summary>
        private void Build()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = UiSortingOrder.BattleResult;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            GameViewLayout.ApplyCurrentScaler(scaler);
            gameObject.AddComponent<GraphicRaycaster>();

            _cg = gameObject.AddComponent<CanvasGroup>();
            _cg.alpha = 0f;

            // 클릭 닫기용 반투명 막(연출 대상이 전장이 아니라 기록이라 살짝 어둡게 깐다).
            var dim = NewRect("Dim", transform);
            dim.anchorMin = Vector2.zero;
            dim.anchorMax = Vector2.one;
            dim.offsetMin = Vector2.zero;
            dim.offsetMax = Vector2.zero;
            var dimImg = dim.gameObject.AddComponent<Image>();
            dimImg.color = new Color(0f, 0f, 0f, 0.55f);
            var dimBtn = dim.gameObject.AddComponent<Button>();
            dimBtn.transition = Selectable.Transition.None;
            dimBtn.onClick.AddListener(Dismiss);

            int clearMs = _data != null ? _data.clearMs : 0;
            bool isNew = _data != null && _data.isNewRecord;
            int rank = _data != null ? _data.rank : 0;

            // ① 신기록 배지(갱신했을 때만) — 기록 위, 작은 크기에서 원본 크기까지 커진다.
            if (isNew)
            {
                var badge = NewText("NewRecord", transform, font, "신기록!", 64, TextAnchor.MiddleCenter);
                badge.color = Gold;
                badge.fontStyle = FontStyle.Bold;
                _badge = badge.rectTransform;
                PlaceCenter(_badge, 300f, 720f, 92f);
                _badge.localScale = Vector3.one * BadgePopStartScale;
            }

            // ② 기록(주인공)
            var record = NewText("Record", transform, font, BossRushFormat.Record(clearMs), 128, TextAnchor.MiddleCenter);
            record.color = Color.white;
            record.fontStyle = FontStyle.Bold;
            PlaceCenter(record.rectTransform, 180f, 900f, 170f);

            // ③ 순위 — clear 응답은 순위만 내려주므로(등재 인원은 없다) "시즌 N위"로 적는다.
            //    캐시를 쓸 수 없어 순위가 비면(0) 계산 중으로 안내한다(서버 기획서 5.3).
            var rankText = NewText("Rank", transform, font,
                                   rank > 0 ? $"시즌 {rank:N0}위" : "순위 계산 중", 46, TextAnchor.MiddleCenter);
            rankText.color = Gold;
            PlaceCenter(rankText.rectTransform, 62f, 900f, 70f);

            // ④ 라운드별 소요(어디서 시간을 썼는지 바로 읽히게)
            float rowY = -30f;
            const float RowStep = 56f;
            if (_rounds != null)
            {
                foreach (var r in _rounds)
                {
                    if (r == null) continue;
                    var row = NewText($"Round{r.round}", transform, font,
                                      $"ROUND {r.round}   ·   {BossRushFormat.Record(r.elapsedMs)}", 38,
                                      TextAnchor.MiddleCenter);
                    row.color = Dim;
                    PlaceCenter(row.rectTransform, rowY, 900f, 52f);
                    rowY -= RowStep;
                }
            }

            var hint = NewText("Hint", transform, font, "화면을 누르면 던전으로 돌아갑니다", 34, TextAnchor.MiddleCenter);
            hint.color = new Color(1f, 1f, 1f, 0.6f);
            PlaceCenter(hint.rectTransform, rowY - 30f, 900f, 52f);

            SoundManager.Jingle(SoundId.JingleStageClear);
            if (isNew)
            {
                // 신기록음은 완주 팡파레와 겹치지 않도록 배지가 다 커지는 시점에 얹는다.
                StartCoroutine(PlayNewRecordAfter(BadgePopDuration));
            }
            StartCoroutine(AutoCloseAfter(AutoCloseSeconds));
        }

        private void Update()
        {
            _t += Time.unscaledDeltaTime;
            if (_cg != null)
            {
                _cg.alpha = Mathf.Clamp01(_t / 0.3f);
            }
            if (_badge != null)
            {
                float s = BadgePopIn(_t);
                _badge.localScale = new Vector3(s, s, 1f);
            }
        }

        /// <summary>신기록 배지의 등장 배율 — 작은 크기에서 원본 크기(1)까지 커지고 끝에서 살짝 넘겼다 제자리로.</summary>
        private static float BadgePopIn(float elapsed)
        {
            float k = Mathf.Clamp01(elapsed / BadgePopDuration);
            if (k >= 1f)
            {
                return 1f;
            }
            float u = k - 1f;
            float ease = 1f + (BadgePopOvershoot + 1f) * u * u * u + BadgePopOvershoot * u * u;
            return Mathf.LerpUnclamped(BadgePopStartScale, 1f, ease);
        }

        /// <summary>신기록 배지가 제 크기가 되는 순간에 신기록음을 울린다(완주 팡파레와 시점을 어긋나게 둔다).</summary>
        private IEnumerator PlayNewRecordAfter(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            SoundManager.Sfx(SoundId.NewRecord);
        }

        private IEnumerator AutoCloseAfter(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            Dismiss();
        }

        /// <summary>오버레이를 닫고 게임 속도를 정상(1)으로 복원한 뒤 콜백(던전 복귀)을 호출한다.</summary>
        public void Dismiss()
        {
            if (_dismissed)
            {
                return;
            }
            _dismissed = true;
            Time.timeScale = 1f;
            var cb = _onClosed;
            _onClosed = null;
            Destroy(gameObject);
            cb?.Invoke();
        }

        // ── UI 헬퍼 ──

        /// <summary>화면 중앙 기준으로 놓는다(y는 위로 +).</summary>
        private static void PlaceCenter(RectTransform rt, float y, float width, float height)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(0f, y);
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
            t.raycastTarget = false; // 클릭은 아래 막(Dim)이 받아 닫는다
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }
    }
}
