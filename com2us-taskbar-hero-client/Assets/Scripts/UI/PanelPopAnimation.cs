using System.Collections;
using UnityEngine;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 패널이 노출(활성화)될 때 작은 크기에서 원래 크기로 팝업되는 애니메이션.
    /// 활성화될 때마다(OnEnable) 재생되므로, UI가 표시될 때마다 등장 효과가 나타난다.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class PanelPopAnimation : MonoBehaviour
    {
        [Tooltip("시작 배율(작은 상태).")]
        [SerializeField] private float startScale = 0.7f;

        [Tooltip("원래 크기까지 커지는 데 걸리는 시간(초).")]
        [SerializeField] private float duration = 0.25f;

        [Tooltip("살짝 튀어오르는(overshoot) 느낌을 줄지 여부.")]
        [SerializeField] private bool overshoot = true;

        private RectTransform _rect;

        private void Awake()
        {
            _rect = GetComponent<RectTransform>();
        }

        private void OnEnable()
        {
            StartCoroutine(Play());
        }

        private void OnDisable()
        {
            // 다음 표시를 위해 원래 크기로 되돌려 둔다.
            if (_rect != null)
            {
                _rect.localScale = Vector3.one;
            }
        }

        private IEnumerator Play()
        {
            _rect.localScale = Vector3.one * startScale;

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float k = Mathf.Clamp01(elapsed / duration);
                float eased = overshoot ? EaseOutBack(k) : EaseOutQuad(k);
                float s = Mathf.LerpUnclamped(startScale, 1f, eased);
                _rect.localScale = new Vector3(s, s, 1f);
                yield return null;
            }

            _rect.localScale = Vector3.one;
        }

        private static float EaseOutQuad(float x) => 1f - (1f - x) * (1f - x);

        private static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float t = x - 1f;
            return 1f + c3 * (t * t * t) + c1 * (t * t);
        }
    }
}
