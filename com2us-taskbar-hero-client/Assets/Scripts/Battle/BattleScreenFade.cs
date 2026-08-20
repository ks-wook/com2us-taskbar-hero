using System;
using UnityEngine;
using UnityEngine.UI;
using TaskbarHero.Client.Managers;

namespace TaskbarHero.Client.Battle
{
    /// <summary>
    /// 전투 화면을 검게 덮었다 다시 여는 전환 페이드. 보스러시의 <b>포탈 이동</b>이 쓴다 —
    /// 화면이 완전히 덮인 순간에 배경 교체·파티 재배치를 하므로, 지역이 통째로 바뀌는 장면이 보이지 않는다.
    /// <para>시간은 <c>unscaled</c>라 보스 처치 슬로우모션과 무관하게 일정한 속도로 재생된다.
    /// 클릭을 가로채지 않도록 <c>raycastTarget = false</c>로 둔다(전환 중 조작이 막히지는 않는다).</para>
    /// </summary>
    public class BattleScreenFade : MonoBehaviour
    {
        private Image _img;
        private float _fadeOut;
        private float _hold;
        private float _fadeIn;
        private Action _onCovered;
        private Action _onFinished;
        private float _t;
        private bool _covered;

        /// <summary>화면을 덮었다 여는 전환을 재생한다.
        /// <paramref name="onCovered"/>는 <b>완전히 덮인 순간</b>(배경 교체·재배치 시점), <paramref name="onFinished"/>는
        /// 다 열린 뒤 1회 호출된다.</summary>
        public static BattleScreenFade Play(float fadeOut, float hold, float fadeIn,
                                            Action onCovered = null, Action onFinished = null)
        {
            var go = new GameObject("BattleScreenFade");
            var fade = go.AddComponent<BattleScreenFade>();
            fade._fadeOut = Mathf.Max(0.01f, fadeOut);
            fade._hold = Mathf.Max(0f, hold);
            fade._fadeIn = Mathf.Max(0.01f, fadeIn);
            fade._onCovered = onCovered;
            fade._onFinished = onFinished;
            fade.Build();
            return fade;
        }

        /// <summary>전체 화면 검은 판 하나짜리 캔버스를 만든다(전투 연출 띠).</summary>
        private void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = UiSortingOrder.BattleResult;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            GameViewLayout.ApplyCurrentScaler(scaler);

            var go = new GameObject("Black", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            _img = go.GetComponent<Image>();
            _img.color = new Color(0f, 0f, 0f, 0f);
            _img.raycastTarget = false;
        }

        private void Update()
        {
            _t += Time.unscaledDeltaTime;

            float alpha;
            if (_t < _fadeOut)
            {
                alpha = _t / _fadeOut;
            }
            else if (_t < _fadeOut + _hold)
            {
                alpha = 1f;
            }
            else
            {
                float k = (_t - _fadeOut - _hold) / _fadeIn;
                alpha = 1f - Mathf.Clamp01(k);
                if (k >= 1f)
                {
                    var done = _onFinished;
                    _onFinished = null;
                    Destroy(gameObject);
                    done?.Invoke();
                    return;
                }
            }

            if (!_covered && _t >= _fadeOut)
            {
                _covered = true;
                var cb = _onCovered;
                _onCovered = null;
                cb?.Invoke();
            }
            if (_img != null)
            {
                _img.color = new Color(0f, 0f, 0f, alpha);
            }
        }
    }
}
