using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TaskbarHero.Client.Managers;

namespace TaskbarHero.Client.Battle
{
    /// <summary>
    /// 전투 패배 연출 오버레이. 아군이 전멸하면 화면 전체를 어둡게 덮고 붉은 "패배" 문구를 표시한다.
    /// 잠시 뒤(또는 클릭 시) 닫히며, 닫힐 때 콜백(현재 스테이지 처음부터 재시작)을 1회 호출한다.
    /// 슬로우모션(timeScale)과 무관하게 unscaled 시간으로 동작한다. 런타임에 자체 Canvas를 코드로 구성한다.
    /// </summary>
    public class BattleDefeatOverlay : MonoBehaviour
    {
        private const float AutoCloseSeconds = 2.5f;

        private CanvasGroup _cg;
        private RectTransform _title;
        private bool _dismissed;
        private float _t;
        private Action _onClosed;

        /// <summary>패배 오버레이를 생성·표시한다. onClosed는 닫힐 때(클릭/자동) 1회 호출된다.</summary>
        public static void Show(Action onClosed = null)
        {
            var go = new GameObject("BattleDefeatOverlay");
            var overlay = go.AddComponent<BattleDefeatOverlay>();
            overlay._onClosed = onClosed;
            overlay.Build();
        }

        private void Build()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = UiSortingOrder.BattleResult; // 클리어 연출과 같은 띠(기능 패널보다 아래)
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            // 현재 씬 규격(GameScene은 높이 1440 기준)으로 즉시 맞춘다 — 주기 스윕을 기다리면 첫 표시 때 잠깐 크게 그려진다.
            GameViewLayout.ApplyCurrentScaler(scaler);
            gameObject.AddComponent<GraphicRaycaster>();

            _cg = gameObject.AddComponent<CanvasGroup>();
            _cg.alpha = 0f;

            // 클릭 닫기용 투명 차단막(배경을 어둡게 하지 않는다).
            var dim = CreateChild("Dim", transform, Vector2.zero, Vector2.one);
            var dimImg = dim.gameObject.AddComponent<Image>();
            dimImg.color = new Color(0f, 0f, 0f, 0f);
            var dimBtn = dim.gameObject.AddComponent<Button>();
            dimBtn.transition = Selectable.Transition.None;
            dimBtn.onClick.AddListener(Dismiss);

            // "패배" 타이틀(중앙, 붉은색).
            var title = CreateText("Title", transform, font, "패배", 140, TextAnchor.MiddleCenter);
            title.color = new Color(0.85f, 0.12f, 0.12f);
            title.fontStyle = FontStyle.Bold;
            _title = (RectTransform)title.transform;
            _title.anchorMin = _title.anchorMax = new Vector2(0.5f, 0.5f);
            _title.sizeDelta = new Vector2(900f, 220f);
            _title.anchoredPosition = new Vector2(0f, 60f);

            // 안내 문구.
            var hint = CreateText("Hint", transform, font, "스테이지를 처음부터 다시 시작합니다", 40, TextAnchor.MiddleCenter);
            hint.color = new Color(1f, 1f, 1f, 0.85f);
            var hrt = (RectTransform)hint.transform;
            hrt.anchorMin = hrt.anchorMax = new Vector2(0.5f, 0.5f);
            hrt.sizeDelta = new Vector2(900f, 70f);
            hrt.anchoredPosition = new Vector2(0f, -80f);

            StartCoroutine(AutoCloseAfter(AutoCloseSeconds));
        }

        private void Update()
        {
            _t += Time.unscaledDeltaTime;
            // 빠른 페이드 인 + 타이틀이 살짝 커졌다 안정되는 임팩트.
            float fade = Mathf.Clamp01(_t / 0.3f);
            if (_cg != null) _cg.alpha = fade;
            if (_title != null)
            {
                float s = Mathf.Lerp(1.4f, 1f, Mathf.Clamp01(_t / 0.35f));
                _title.localScale = new Vector3(s, s, 1f);
            }
        }

        private IEnumerator AutoCloseAfter(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            Dismiss();
        }

        /// <summary>오버레이를 닫고 게임 속도를 정상(1)으로 복원한 뒤 콜백(재시작)을 호출한다.</summary>
        public void Dismiss()
        {
            if (_dismissed) return;
            _dismissed = true;
            Time.timeScale = 1f;
            var cb = _onClosed;
            _onClosed = null;
            Destroy(gameObject);
            cb?.Invoke();
        }

        // ── UI 생성 헬퍼 ──

        private static RectTransform CreateChild(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        private static Text CreateText(string name, Transform parent, Font font, string content, int size, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = font;
            t.text = content;
            t.fontSize = size;
            t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }
    }
}
