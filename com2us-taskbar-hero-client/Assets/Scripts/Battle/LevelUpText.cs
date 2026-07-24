using System.Collections;
using UnityEngine;

namespace TaskbarHero.Client.Battle
{
    /// <summary>
    /// 레벨업 시 캐릭터 위에 잠깐 떠오르는 노란색 "Level Up!" 문구(월드 TextMesh).
    /// 기존 레벨업 글로우 이펙트(<see cref="DungeonBattleFlow"/>의 스프라이트 시퀀스)와 별개로 함께 노출된다.
    /// 생성 시 살짝 커졌다가 위로 떠오르며 서서히 fade out 되고, 애니가 끝나면 자동 파괴된다.
    /// </summary>
    [RequireComponent(typeof(TextMesh))]
    public class LevelUpText : MonoBehaviour
    {
        private const string Message = "Level Up!";
        private const float Duration = 1.2f;     // 전체 수명(초)
        private const float PopRatio = 0.2f;     // 이 비율까지 커졌다가(팝) 이후 유지
        private const float FadeStart = 0.5f;    // 이 비율부터 알파 감소 시작
        private const float PopScale = 1.15f;    // 팝 최대 배율
        private const float StartScale = 0.5f;   // 시작 배율
        private const float RiseWorld = 0.8f;    // 수명 동안 상승하는 월드 거리

        private static readonly Color TextColor = new Color(1f, 0.85f, 0.1f, 1f); // 노란색

        private TextMesh _text;
        private Coroutine _anim;

        /// <summary>지정 캐릭터 위에 "Level Up!" 문구를 생성해 애니메이션을 재생한다(재생 후 자동 파괴).
        /// <paramref name="parent"/> 캐릭터에 부착되며, <paramref name="localYOffset"/>만큼 위로 올린다.
        /// <paramref name="sortingOrder"/>로 캐릭터/글로우 위에 표시한다.</summary>
        public static void Spawn(Transform parent, float localYOffset, int sortingOrder)
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var go = new GameObject("LevelUpText", typeof(TextMesh), typeof(LevelUpText));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, localYOffset, 0f);

            var tm = go.GetComponent<TextMesh>();
            tm.font = font;
            tm.text = Message;
            tm.fontSize = 72;
            tm.characterSize = 0.06f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontStyle = FontStyle.Bold;
            tm.color = TextColor;

            var mr = go.GetComponent<MeshRenderer>();
            mr.material = font.material;
            mr.sortingOrder = sortingOrder;

            go.GetComponent<LevelUpText>().Begin();
        }

        /// <summary>애니메이션을 시작한다(초기 스케일/알파 설정 후 코루틴 기동).</summary>
        private void Begin()
        {
            _text = GetComponent<TextMesh>();
            transform.localScale = Vector3.one * StartScale;
            SetAlpha(1f);
            if (_anim != null)
            {
                StopCoroutine(_anim);
            }
            _anim = StartCoroutine(Animate());
        }

        /// <summary>수명 동안 팝 → 상승 → fade out을 진행하고 종료 시 오브젝트를 파괴한다.</summary>
        private IEnumerator Animate()
        {
            Vector3 startLocal = transform.localPosition;
            float t = 0f;
            while (t < Duration)
            {
                t += Time.deltaTime;
                float n = Mathf.Clamp01(t / Duration);

                // 스케일: 시작→팝(빠르게 커짐)→팝 배율 유지
                float scale = n < PopRatio
                    ? Mathf.Lerp(StartScale, PopScale, n / PopRatio)
                    : PopScale;
                transform.localScale = Vector3.one * scale;

                // 알파: FadeStart 이후 서서히 0으로
                float alpha = n < FadeStart ? 1f : Mathf.Lerp(1f, 0f, (n - FadeStart) / (1f - FadeStart));
                SetAlpha(alpha);

                // 위로 떠오름
                transform.localPosition = startLocal + Vector3.up * (RiseWorld * n);

                yield return null;
            }

            _anim = null;
            Destroy(gameObject);
        }

        /// <summary>문구 색상의 알파값만 갱신한다(fade out용).</summary>
        private void SetAlpha(float a)
        {
            if (_text == null)
            {
                return;
            }
            var c = TextColor;
            c.a = a;
            _text.color = c;
        }
    }
}
