using System.Collections;
using UnityEngine;

namespace TaskbarHero.Client.Battle
{
    /// <summary>
    /// 레벨업 시 캐릭터 위에 잠깐 떠오르는 "LEVEL UP!" 배너 이미지(월드 SpriteRenderer).
    /// 기존 레벨업 글로우 이펙트(<see cref="DungeonBattleFlow"/>의 스프라이트 시퀀스)와 별개로 함께 노출된다.
    /// 작게 나타나 살짝 튀어오르듯 커졌다가(팝) 위로 떠오르며 서서히 fade out 되고, 애니가 끝나면 자동 파괴된다.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class LevelUpBanner : MonoBehaviour
    {
        private const float Duration = 1.6f;      // 전체 수명(초)
        private const float PopRatio = 0.16f;     // 이 비율까지 오버슈트 배율로 커짐
        private const float SettleRatio = 0.28f;  // 이 비율까지 오버슈트 → 기준 배율로 되돌아옴
        private const float FadeStart = 0.5f;     // 이 비율부터 알파 감소 시작
        private const float StartScale = 0.45f;   // 시작 배율(기준 크기 대비)
        private const float PopScale = 1.14f;     // 팝 최대 배율(기준 크기 대비)
        private const float RiseWorld = 0.55f;    // 수명 동안 상승하는 월드 거리

        private SpriteRenderer _sr;
        private float _baseScale = 1f;   // 지정 월드 너비에 맞춘 기준 스케일
        private Coroutine _anim;

        /// <summary>지정 캐릭터 위에 레벨업 배너 이미지를 생성해 애니메이션을 재생한다(재생 후 자동 파괴).
        /// <paramref name="parent"/> 캐릭터에 부착되며, <paramref name="localYOffset"/>만큼 위로 올린다(이미지 중심 기준).
        /// <paramref name="worldWidth"/> 월드 너비에 맞춰 스케일하고, <paramref name="sortingLayerId"/>·<paramref name="sortingOrder"/>로
        /// 캐릭터/글로우 위에 표시한다.</summary>
        public static void Spawn(Transform parent, Sprite sprite, float localYOffset, float worldWidth,
                                 int sortingLayerId, int sortingOrder)
        {
            if (sprite == null)
            {
                return;
            }

            var go = new GameObject("LevelUpBanner", typeof(SpriteRenderer), typeof(LevelUpBanner));
            go.transform.SetParent(parent, false);

            float spriteW = sprite.bounds.size.x;
            float scale = spriteW > 0.001f && worldWidth > 0.001f ? worldWidth / spriteW : 1f;

            // 피벗이 좌하단인 스프라이트(레벨업.png)도 중앙 정렬되도록 로컬 중심만큼 역보정한다(스케일 반영).
            Vector3 c = sprite.bounds.center;
            go.transform.localPosition = new Vector3(-c.x * scale, localYOffset - c.y * scale, 0f);

            var sr = go.GetComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingLayerID = sortingLayerId;
            sr.sortingOrder = sortingOrder;

            var banner = go.GetComponent<LevelUpBanner>();
            banner._baseScale = scale;
            banner.Begin();
        }

        /// <summary>애니메이션을 시작한다(초기 스케일/알파 설정 후 코루틴 기동).</summary>
        private void Begin()
        {
            _sr = GetComponent<SpriteRenderer>();
            transform.localScale = Vector3.one * (_baseScale * StartScale);
            SetAlpha(1f);
            if (_anim != null)
            {
                StopCoroutine(_anim);
            }
            _anim = StartCoroutine(Animate());
        }

        /// <summary>수명 동안 팝(오버슈트→정착) → 상승 → fade out을 진행하고 종료 시 오브젝트를 파괴한다.</summary>
        private IEnumerator Animate()
        {
            Vector3 startLocal = transform.localPosition;
            float t = 0f;
            while (t < Duration)
            {
                t += Time.deltaTime;
                float n = Mathf.Clamp01(t / Duration);

                // 스케일: 시작 → 오버슈트(빠르게 커짐) → 기준 배율로 정착
                float mul;
                if (n < PopRatio)
                {
                    mul = Mathf.Lerp(StartScale, PopScale, n / PopRatio);
                }
                else if (n < SettleRatio)
                {
                    mul = Mathf.Lerp(PopScale, 1f, (n - PopRatio) / (SettleRatio - PopRatio));
                }
                else
                {
                    mul = 1f;
                }
                transform.localScale = Vector3.one * (_baseScale * mul);

                // 알파: FadeStart 이후 서서히 0으로(끝으로 갈수록 부드럽게)
                float alpha = n < FadeStart ? 1f : Mathf.SmoothStep(1f, 0f, (n - FadeStart) / (1f - FadeStart));
                SetAlpha(alpha);

                // 위로 떠오름(뒤로 갈수록 감속)
                transform.localPosition = startLocal + Vector3.up * (RiseWorld * Mathf.Sin(n * Mathf.PI * 0.5f));

                yield return null;
            }

            _anim = null;
            Destroy(gameObject);
        }

        /// <summary>배너 이미지의 알파값만 갱신한다(fade out용).</summary>
        private void SetAlpha(float a)
        {
            if (_sr == null)
            {
                return;
            }
            var c = _sr.color;
            c.a = a;
            _sr.color = c;
        }
    }
}
