using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 이 오브젝트의 자식 글자들이 형제(sibling) 순서대로 하나씩 커졌다 작아지는(pop)
    /// 파도형 애니메이션을 재생한다. 기본적으로 <see cref="repeatInterval"/>(초)마다 반복한다.
    /// 자식 추가 순서가 곧 애니메이션 순서다.
    /// </summary>
    public class LogoIntroAnimation : MonoBehaviour
    {
        [Tooltip("애니메이션 시작 전 대기(초).")]
        [SerializeField] private float startDelay = 0.15f;

        [Tooltip("글자 사이 시작 간격(초). 작을수록 촘촘한 파도.")]
        [SerializeField] private float perLetterDelay = 0.06f;

        [Tooltip("한 글자가 커졌다 작아지는 데 걸리는 시간(초).")]
        [SerializeField] private float popDuration = 0.3f;

        [Tooltip("팝업 시 최대 배율.")]
        [SerializeField] private float popScale = 1.5f;

        [Tooltip("반복 재생 여부.")]
        [SerializeField] private bool loop = true;

        [Tooltip("반복 주기(초). 각 파도가 시작되는 간격.")]
        [SerializeField] private float repeatInterval = 3f;

        private void OnEnable()
        {
            StartCoroutine(Run());
        }

        private void OnDisable()
        {
            StopAllCoroutines();
        }

        private IEnumerator Run()
        {
            if (startDelay > 0f)
            {
                yield return new WaitForSeconds(startDelay);
            }

            do
            {
                // 한 파도의 시작 시각을 기준으로 다음 파도까지 repeatInterval을 유지한다.
                float waveStart = Time.time;
                yield return StartCoroutine(PlayWave());

                if (!loop)
                {
                    yield break;
                }

                float remaining = repeatInterval - (Time.time - waveStart);
                if (remaining > 0f)
                {
                    yield return new WaitForSeconds(remaining);
                }
            }
            while (loop);
        }

        private IEnumerator PlayWave()
        {
            int count = transform.childCount;
            var letters = new List<Transform>(count);
            var baseScales = new List<Vector3>(count);
            for (int i = 0; i < count; i++)
            {
                var child = transform.GetChild(i);
                letters.Add(child);
                baseScales.Add(child.localScale);
            }

            for (int i = 0; i < letters.Count; i++)
            {
                StartCoroutine(Pop(letters[i], baseScales[i]));
                if (perLetterDelay > 0f)
                {
                    yield return new WaitForSeconds(perLetterDelay);
                }
            }
        }

        private IEnumerator Pop(Transform target, Vector3 baseScale)
        {
            float half = Mathf.Max(0.01f, popDuration * 0.5f);

            // 커지기
            float elapsed = 0f;
            while (elapsed < half)
            {
                elapsed += Time.deltaTime;
                float k = EaseOutQuad(Mathf.Clamp01(elapsed / half));
                if (target != null)
                {
                    target.localScale = baseScale * Mathf.Lerp(1f, popScale, k);
                }
                yield return null;
            }

            // 작아지기
            elapsed = 0f;
            while (elapsed < half)
            {
                elapsed += Time.deltaTime;
                float k = EaseInQuad(Mathf.Clamp01(elapsed / half));
                if (target != null)
                {
                    target.localScale = baseScale * Mathf.Lerp(popScale, 1f, k);
                }
                yield return null;
            }

            if (target != null)
            {
                target.localScale = baseScale;
            }
        }

        private static float EaseOutQuad(float x) => 1f - (1f - x) * (1f - x);
        private static float EaseInQuad(float x) => x * x;
    }
}
