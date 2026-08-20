using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TaskbarHero.Client.Managers;

namespace TaskbarHero.Client.Battle
{
    /// <summary>
    /// 전투 패배 연출 오버레이. 아군이 전멸하면 화면 중앙에 패배 타이틀 이미지(defeat.png)를
    /// <b>작은 크기에서 원본 크기까지 키우며</b> 띄운다(<see cref="TitlePopIn"/>).
    /// 이미지는 <see cref="StageClearAssets.defeatTitleImage"/>(Resources)에서 가져오며, 배선돼 있지
    /// 않으면 종전처럼 붉은 "패배" 문구로 폴백한다.
    /// 잠시 뒤(또는 클릭 시) 닫히며, 닫힐 때 콜백(현재 스테이지 처음부터 재시작)을 1회 호출한다.
    /// 슬로우모션(timeScale)과 무관하게 unscaled 시간으로 동작한다. 런타임에 자체 Canvas를 코드로 구성한다.
    /// </summary>
    public class BattleDefeatOverlay : MonoBehaviour
    {
        private const float AutoCloseSeconds = 2.5f;

        // 패배 타이틀 이미지 규격·자리. 크기는 스프라이트 원본 비율(defeat_0 = 242×144)을 유지한 값이며,
        // 이것이 등장 연출이 끝나는 "원본 크기"다. 자리는 종전 "패배" 문구가 있던 화면 중앙 위쪽이되,
        // 아래 안내 문구(y -80, 높이 70)와 겹치지 않도록 이미지 아래변이 그보다 위에 오게 올려 둔다.
        private static readonly Vector2 TitleImageSize = new Vector2(504f, 300f);
        private const float TitleImageY = 110f;

        // 등장 연출: 작았다가 원본 크기까지 커진다(클리어 보상 타이틀과 같은 back-out 이징).
        private const float TitlePopDuration = 0.42f;   // 시작 크기 → 원본 크기까지 걸리는 시간
        private const float TitlePopStartScale = 0.25f; // 등장 시작 크기(원본 대비)
        private const float TitlePopOvershoot = 1.4f;   // 원본 크기를 살짝 넘겼다 제자리로(0이면 오버슈트 없음)

        // 텍스트 폴백의 등장 연출(이미지가 없을 때만) — 종전과 같이 살짝 컸다가 안정된다.
        private const float TextPopStartScale = 1.4f;
        private const float TextPopDuration = 0.35f;

        private CanvasGroup _cg;
        private RectTransform _title;
        private bool _titleIsImage;   // 타이틀이 이미지면 커지는 연출, 텍스트면 종전 축소 연출
        private bool _dismissed;
        private float _t;
        private Action _onClosed;
        private string _headline;   // 타이틀 이미지 아래 한 줄(없으면 표시하지 않음)
        private string _hint = DefaultHint;

        private const string DefaultHint = "스테이지를 처음부터 다시 시작합니다";

        /// <summary>패배 오버레이를 생성·표시한다. onClosed는 닫힐 때(클릭/자동) 1회 호출된다.</summary>
        public static void Show(Action onClosed = null)
        {
            Show(null, null, onClosed);
        }

        /// <summary>문구를 지정해 패배 오버레이를 띄운다(보스러시 도전 실패처럼 <b>같은 연출에 다른 사유</b>를
        /// 적어야 하는 경우 — 별도 오버레이를 만들지 않고 이 진입점을 쓴다).
        /// <paramref name="headline"/>은 타이틀 이미지 아래 굵은 한 줄(비우면 생략),
        /// <paramref name="hint"/>는 그 아래 안내 줄(비우면 기본 문구).</summary>
        public static void Show(string headline, string hint, Action onClosed = null)
        {
            var go = new GameObject("BattleDefeatOverlay");
            var overlay = go.AddComponent<BattleDefeatOverlay>();
            overlay._onClosed = onClosed;
            overlay._headline = headline;
            overlay._hint = string.IsNullOrEmpty(hint) ? DefaultHint : hint;
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

            // 패배 타이틀 — 이미지(defeat.png)가 있으면 이미지, 없으면 종전 "패배" 문구로 폴백.
            var assets = StageClearAssets.Load();
            Sprite titleSprite = assets != null ? assets.defeatTitleImage : null;
            if (titleSprite != null)
            {
                _title = CreateChild("TitleImage", transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
                _title.sizeDelta = TitleImageSize;
                _title.anchoredPosition = new Vector2(0f, TitleImageY);
                var img = _title.gameObject.AddComponent<Image>();
                img.sprite = titleSprite;
                img.preserveAspect = true;
                img.raycastTarget = false; // 클릭은 아래 차단막(Dim)이 받아 닫는다
                _titleIsImage = true;
                // 첫 프레임에 원본 크기로 한 번 보였다 줄어드는 깜빡임을 막기 위해 시작 크기를 미리 적용한다.
                _title.localScale = Vector3.one * TitlePopStartScale;
            }
            else
            {
                var title = CreateText("Title", transform, font, "패배", 140, TextAnchor.MiddleCenter);
                title.color = new Color(0.85f, 0.12f, 0.12f);
                title.fontStyle = FontStyle.Bold;
                _title = (RectTransform)title.transform;
                _title.anchorMin = _title.anchorMax = new Vector2(0.5f, 0.5f);
                _title.sizeDelta = new Vector2(900f, 220f);
                _title.anchoredPosition = new Vector2(0f, 60f);
            }

            // 사유 한 줄(지정된 경우에만) — 타이틀 이미지 바로 아래에 굵게 적는다.
            bool hasHeadline = !string.IsNullOrEmpty(_headline);
            if (hasHeadline)
            {
                var head = CreateText("Headline", transform, font, _headline, 52, TextAnchor.MiddleCenter);
                head.color = new Color(1f, 0.86f, 0.5f);
                head.fontStyle = FontStyle.Bold;
                var hdrt = (RectTransform)head.transform;
                hdrt.anchorMin = hdrt.anchorMax = new Vector2(0.5f, 0.5f);
                hdrt.sizeDelta = new Vector2(900f, 76f);
                hdrt.anchoredPosition = new Vector2(0f, -30f);
            }

            // 안내 문구(사유 줄이 있으면 그만큼 아래로 내린다).
            var hint = CreateText("Hint", transform, font, _hint, 40, TextAnchor.MiddleCenter);
            hint.color = new Color(1f, 1f, 1f, 0.85f);
            var hrt = (RectTransform)hint.transform;
            hrt.anchorMin = hrt.anchorMax = new Vector2(0.5f, 0.5f);
            hrt.sizeDelta = new Vector2(900f, 70f);
            hrt.anchoredPosition = new Vector2(0f, hasHeadline ? -110f : -80f);

            StartCoroutine(AutoCloseAfter(AutoCloseSeconds));
        }

        private void Update()
        {
            _t += Time.unscaledDeltaTime;
            // 빠른 페이드 인 + 타이틀 등장 연출(이미지는 작았다 커지고, 텍스트 폴백은 종전대로 컸다 안정된다).
            float fade = Mathf.Clamp01(_t / 0.3f);
            if (_cg != null) _cg.alpha = fade;
            if (_title != null)
            {
                float s = _titleIsImage
                    ? TitlePopIn(_t)
                    : Mathf.Lerp(TextPopStartScale, 1f, Mathf.Clamp01(_t / TextPopDuration));
                _title.localScale = new Vector3(s, s, 1f);
            }
        }

        /// <summary>패배 타이틀 이미지의 등장 배율을 구한다 — <see cref="TitlePopStartScale"/>에서 원본
        /// 크기(1)까지 커지며, 끝에서 1을 살짝 넘겼다 제자리로 돌아오는 easeOutBack이라 "팡 나타났다"로
        /// 읽힌다. 시간은 unscaled라 패배 슬로우모션과 무관하다.</summary>
        private static float TitlePopIn(float elapsed)
        {
            float k = Mathf.Clamp01(elapsed / TitlePopDuration);
            if (k >= 1f)
            {
                return 1f; // 원본 크기로 마무리
            }
            float u = k - 1f;
            float ease = 1f + (TitlePopOvershoot + 1f) * u * u * u + TitlePopOvershoot * u * u;
            return Mathf.LerpUnclamped(TitlePopStartScale, 1f, ease);
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
