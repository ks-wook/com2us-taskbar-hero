using UnityEngine;

// 배경 생동감 효과 컴포넌트 (TitleScene 배경에 적용).
namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 정적 배경 이미지에 생동감을 더하는 효과.
    /// Ken Burns 기법(느린 확대/축소 + 완만한 상하좌우 이동)에 미세한 기울임을 조합해
    /// RectTransform을 부드럽게 애니메이션한다.
    /// 이동 시 가장자리가 드러나지 않도록 기본 배율을 1보다 크게(오버스케일) 유지한다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public class BackgroundEffect : MonoBehaviour
    {
        [Header("확대/축소 (Ken Burns)")]
        [Tooltip("기본 확대 배율. 이동·기울임 시 가장자리가 보이지 않도록 1보다 크게 유지한다.")]
        [SerializeField] private float baseScale = 1.2f;

        [Tooltip("배율이 오르내리는 폭.")]
        [SerializeField] private float scaleAmplitude = 0.05f;

        [Tooltip("확대/축소 한 주기의 길이(초).")]
        [SerializeField] private float scalePeriod = 12f;

        [Header("이동 (Pan / Drift)")]
        [Tooltip("가로/세로로 흔들리는 최대 이동량.")]
        [SerializeField] private Vector2 panAmplitude = new Vector2(18f, 14f);

        [SerializeField] private float panPeriodX = 17f;
        [SerializeField] private float panPeriodY = 13f;

        [Header("기울임 (Tilt)")]
        [Tooltip("좌우로 살짝 기우는 최대 각도(도).")]
        [SerializeField] private float tiltAmplitude = 0.7f;

        [SerializeField] private float tiltPeriod = 20f;

        private RectTransform _rect;
        private Vector2 _basePosition;
        private float _phaseSeed;

        private void Awake()
        {
            _rect = GetComponent<RectTransform>();
            _basePosition = _rect.anchoredPosition;
            // 여러 배경 인스턴스가 있어도 위상이 겹치지 않도록 임의의 시작 오프셋을 준다.
            _phaseSeed = Random.Range(0f, 10f);
        }

        private void OnDisable()
        {
            // 비활성화 시 원래 상태로 되돌려 둔다.
            _rect.anchoredPosition = _basePosition;
            _rect.localScale = Vector3.one * baseScale;
            _rect.localRotation = Quaternion.identity;
        }

        private void Update()
        {
            float t = Time.time + _phaseSeed;

            float scale = baseScale + scaleAmplitude * Mathf.Sin(TwoPiOver(scalePeriod) * t);
            _rect.localScale = new Vector3(scale, scale, 1f);

            float px = panAmplitude.x * Mathf.Sin(TwoPiOver(panPeriodX) * t);
            float py = panAmplitude.y * Mathf.Cos(TwoPiOver(panPeriodY) * t);
            _rect.anchoredPosition = _basePosition + new Vector2(px, py);

            float tilt = tiltAmplitude * Mathf.Sin(TwoPiOver(tiltPeriod) * t);
            _rect.localRotation = Quaternion.Euler(0f, 0f, tilt);
        }

        private static float TwoPiOver(float period)
        {
            return 2f * Mathf.PI / Mathf.Max(0.01f, period);
        }
    }
}
