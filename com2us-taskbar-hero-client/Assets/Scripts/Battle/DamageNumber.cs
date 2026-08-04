using System.Collections;
using UnityEngine;

namespace TaskbarHero.Client.Battle
{
    /// <summary>
    /// 몬스터 피격 시 표시되는 데미지 숫자(월드 TextMesh, 일반은 흰색).
    /// 빠르게 커진 뒤 그 크기를 유지한 채 <b>위로 떠오르며</b> fade out 되고,
    /// 애니가 끝나면 풀로 반환된다. 오브젝트 생성/파괴 대신 <see cref="DamageNumberPool"/>이 재사용한다.
    /// <para><b>치명타</b>는 노란 숫자 + 더 크게 튀는 팝 + 뒤에 <c>!</c>로 구분한다.</para>
    /// </summary>
    [RequireComponent(typeof(TextMesh))]
    public class DamageNumber : MonoBehaviour
    {
        private const float Duration = 0.75f;    // 전체 수명(초)
        private const float PopRatio = 0.18f;    // 이 비율까지 빠르게 커진다(팝)
        private const float FadeStart = 0.35f;   // 이 비율부터 알파 감소 시작
        private const float PopScale = 1.35f;    // 팝 최대 배율
        private const float StartScale = 0.6f;   // 시작 배율
        // 팝 이후 배율. 1보다 크면 사라질 때까지 아주 조금 더 커진다(축소하면 뒤로 물러나는 느낌이 난다).
        private const float HoldGrowth = 1.05f;
        private const float RiseWorld = 0.9f;    // 수명 동안 위로 떠오르는 월드 거리

        private const float CritPopScale = 1.85f;  // 치명타 팝 최대 배율(일반보다 크게 튄다)

        // 산포(겹침 방지): 같은 자리에 여러 숫자가 뜨면 한 덩어리로 뭉쳐 읽을 수 없다.
        // 시작 위치를 좌우로 흩고, 떠오르는 동안 좌우로도 조금 흘려 서로 갈라지게 한다.
        private const float SpreadX = 0.32f;       // 시작 x 흔들림(±, 월드 단위)
        private const float SpreadY = 0.12f;       // 시작 y 흔들림(±)
        private const float DriftX = 0.28f;        // 수명 동안 좌우로 흐르는 거리(±)

        private static readonly Color NumberColor = Color.white;                  // 일반 데미지(흰색)
        private static readonly Color CritColor = new Color(1f, 0.84f, 0.2f, 1f); // 치명타(노란색)

        private TextMesh _text;
        private DamageNumberPool _pool;
        private Coroutine _anim;
        private bool _crit;
        private float _sizeMul = 1f;
        private float _driftX;

        /// <summary>풀 참조 주입(반환용).</summary>
        public void Init(DamageNumberPool pool)
        {
            _pool = pool;
            _text = GetComponent<TextMesh>();
        }

        /// <summary>지정 월드 위치에 데미지 값을 띄우고 애니메이션을 시작한다.
        /// <paramref name="crit"/>이면 노란 숫자 + 큰 팝 + <c>!</c>로 치명타를 구분한다.
        /// <paramref name="sizeMul"/>은 피해 비중에 따른 크기 배수, <paramref name="delay"/>는 광역에서
        /// 대상별로 뜨는 시차다(그 사이에는 보이지 않는다).</summary>
        public void Play(long damage, Vector3 worldPos, bool crit, float sizeMul = 1f, float delay = 0f)
        {
            if (_text == null)
            {
                _text = GetComponent<TextMesh>();
            }
            _crit = crit;
            _sizeMul = Mathf.Clamp(sizeMul, 0.5f, 2f);
            _text.text = crit ? damage.ToString() + "!" : damage.ToString();

            // 시작 위치를 좌우·상하로 흩는다(연타·광역에서 숫자가 한 점에 겹치지 않게).
            Vector3 spread = worldPos + new Vector3(Random.Range(-SpreadX, SpreadX), Random.Range(-SpreadY, SpreadY), 0f);
            _driftX = Random.Range(-DriftX, DriftX);

            transform.position = spread;
            transform.localScale = Vector3.one * (StartScale * _sizeMul);
            SetAlpha(delay > 0f ? 0f : 1f); // 시차 대기 중에는 보이지 않는다
            gameObject.SetActive(true);

            if (_anim != null)
            {
                StopCoroutine(_anim);
            }
            _anim = StartCoroutine(Animate(spread, delay));
        }

        private IEnumerator Animate(Vector3 startPos, float delay)
        {
            if (delay > 0f)
            {
                // 실시간 대기 — 히트스톱(timeScale 0.05) 중에 시차가 20배로 늘어나면 숫자가 한참 뒤에 뜬다.
                yield return new WaitForSecondsRealtime(delay);
                SetAlpha(1f);
            }

            float t = 0f;
            while (t < Duration)
            {
                t += Time.deltaTime;
                float n = Mathf.Clamp01(t / Duration);

                // 스케일: 시작 → 팝(빠르게 커짐) → 사라질 때까지 그 크기를 유지(아주 조금만 더 커진다).
                // 치명타는 더 크게 튄다.
                float pop = (_crit ? CritPopScale : PopScale) * _sizeMul;
                float scale = n < PopRatio
                    ? Mathf.Lerp(StartScale * _sizeMul, pop, n / PopRatio)
                    : Mathf.Lerp(pop, pop * HoldGrowth, (n - PopRatio) / (1f - PopRatio));
                transform.localScale = Vector3.one * scale;

                // 알파: FadeStart 이후 서서히 0으로
                float alpha = n < FadeStart ? 1f : Mathf.Lerp(1f, 0f, (n - FadeStart) / (1f - FadeStart));
                SetAlpha(alpha);

                // 위로 떠오르며 좌우로도 조금 흘러간다(같은 대상에 연타가 들어와도 서로 갈라진다)
                transform.position = startPos + new Vector3(_driftX * n, RiseWorld * n, 0f);

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
