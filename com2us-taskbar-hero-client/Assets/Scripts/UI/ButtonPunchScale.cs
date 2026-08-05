using System;
using System.Collections;
using UnityEngine;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 버튼 클릭 시 잠깐 커졌다가 원래 크기로 돌아오는(punch) 효과.
    /// 효과가 끝난 뒤 후속 동작(패널 닫기 등)을 실행할 수 있도록 완료 콜백을 지원한다.
    /// (onClick에 자동 등록하지 않으며, 호출측이 Play(onComplete)로 순서를 제어한다.)
    /// </summary>
    public class ButtonPunchScale : MonoBehaviour
    {
        [Tooltip("클릭 시 커지는 최대 배율.")]
        [SerializeField] private float punchScale = 1.18f;

        [Tooltip("커졌다 돌아오는 전체 시간(초).")]
        [SerializeField] private float duration = 0.18f;

        private RectTransform _rect;
        private Vector3 _baseScale;
        private Coroutine _routine;

        private void Awake()
        {
            _rect = GetComponent<RectTransform>();
            _baseScale = _rect.localScale;
        }

        /// <summary>
        /// 비활성화될 때 크기를 원래대로 되돌린다. 연출 중에 오브젝트가 꺼지면 Unity가 코루틴을 멈추므로
        /// (예: 스테이지 노드를 누른 직후 창을 닫는 경우) 그대로 두면 <b>커진 상태로 굳어</b> 다음에 켤 때도 크게 보인다.
        /// </summary>
        private void OnDisable()
        {
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }
            if (_rect != null)
            {
                _rect.localScale = _baseScale;
            }
        }

        /// <summary>펀치 효과를 처음부터 재생하고, 끝나면 <paramref name="onComplete"/>를 호출한다.</summary>
        public void Play(Action onComplete = null)
        {
            if (_rect == null)
            {
                _rect = GetComponent<RectTransform>();
                _baseScale = _rect.localScale;
            }

            if (_routine != null)
            {
                StopCoroutine(_routine);
            }
            _routine = StartCoroutine(PunchRoutine(onComplete));
        }

        private IEnumerator PunchRoutine(Action onComplete)
        {
            float half = Mathf.Max(0.01f, duration * 0.5f);

            float elapsed = 0f;
            while (elapsed < half)
            {
                elapsed += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(elapsed / half);
                _rect.localScale = _baseScale * Mathf.Lerp(1f, punchScale, k);
                yield return null;
            }

            elapsed = 0f;
            while (elapsed < half)
            {
                elapsed += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(elapsed / half);
                _rect.localScale = _baseScale * Mathf.Lerp(punchScale, 1f, k);
                yield return null;
            }

            _rect.localScale = _baseScale;
            _routine = null;
            onComplete?.Invoke();
        }
    }
}
