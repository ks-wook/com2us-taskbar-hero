using System.Collections;
using UnityEngine;

namespace TaskbarHero.Client.Battle
{
    /// <summary>
    /// 몬스터 피격 시 표시되는 붉은 데미지 숫자(월드 TextMesh). 생성 시 잠깐 커졌다가
    /// 점점 작아지며 서서히 fade out 되고, 애니가 끝나면 풀로 반환된다.
    /// 오브젝트 생성/파괴 대신 <see cref="DamageNumberPool"/>이 재사용한다.
    /// <para><b>치명타</b>는 노란 숫자 + 더 크게 튀는 팝 + 뒤에 <c>!</c>로 구분한다.</para>
    /// </summary>
    [RequireComponent(typeof(TextMesh))]
    public class DamageNumber : MonoBehaviour
    {
        private const float Duration = 0.75f;    // 전체 수명(초)
        private const float PopRatio = 0.18f;    // 이 비율까지 커졌다가(팝) 이후 작아짐
        private const float FadeStart = 0.35f;   // 이 비율부터 알파 감소 시작
        private const float PopScale = 1.35f;    // 팝 최대 배율
        private const float StartScale = 0.6f;   // 시작 배율
        private const float EndScale = 0.5f;     // 종료 배율
        private const float RiseWorld = 0.9f;    // 수명 동안 상승하는 월드 거리

        private const float CritPopScale = 1.85f;  // 치명타 팝 최대 배율(일반보다 크게 튄다)
        private const float CritEndScale = 0.7f;   // 치명타 종료 배율

        private static readonly Color NumberColor = new Color(0.95f, 0.15f, 0.1f, 1f);
        private static readonly Color CritColor = new Color(1f, 0.84f, 0.2f, 1f); // 치명타(노란색)

        private TextMesh _text;
        private DamageNumberPool _pool;
        private Coroutine _anim;
        private bool _crit;

        /// <summary>풀 참조 주입(반환용).</summary>
        public void Init(DamageNumberPool pool)
        {
            _pool = pool;
            _text = GetComponent<TextMesh>();
        }

        /// <summary>지정 월드 위치에 데미지 값을 띄우고 애니메이션을 시작한다.
        /// <paramref name="crit"/>이면 노란 숫자 + 큰 팝 + <c>!</c>로 치명타를 구분한다.</summary>
        public void Play(long damage, Vector3 worldPos, bool crit)
        {
            if (_text == null)
            {
                _text = GetComponent<TextMesh>();
            }
            _crit = crit;
            _text.text = crit ? damage.ToString() + "!" : damage.ToString();
            transform.position = worldPos;
            transform.localScale = Vector3.one * StartScale;
            SetAlpha(1f);
            gameObject.SetActive(true);

            if (_anim != null)
            {
                StopCoroutine(_anim);
            }
            _anim = StartCoroutine(Animate(worldPos));
        }

        private IEnumerator Animate(Vector3 startPos)
        {
            float t = 0f;
            while (t < Duration)
            {
                t += Time.deltaTime;
                float n = Mathf.Clamp01(t / Duration);

                // 스케일: 시작→팝(빠르게 커짐)→점점 작아짐. 치명타는 더 크게 튄다.
                float pop = _crit ? CritPopScale : PopScale;
                float end = _crit ? CritEndScale : EndScale;
                float scale = n < PopRatio
                    ? Mathf.Lerp(StartScale, pop, n / PopRatio)
                    : Mathf.Lerp(pop, end, (n - PopRatio) / (1f - PopRatio));
                transform.localScale = Vector3.one * scale;

                // 알파: FadeStart 이후 서서히 0으로
                float alpha = n < FadeStart ? 1f : Mathf.Lerp(1f, 0f, (n - FadeStart) / (1f - FadeStart));
                SetAlpha(alpha);

                // 위로 살짝 떠오름
                transform.position = startPos + Vector3.up * (RiseWorld * n);

                yield return null;
            }

            _anim = null;
            gameObject.SetActive(false);
            if (_pool != null)
            {
                _pool.Release(this);
            }
        }

        private void SetAlpha(float a)
        {
            var c = _crit ? CritColor : NumberColor;
            c.a = a;
            _text.color = c;
        }
    }
}
