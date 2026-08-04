using System.Collections;
using UnityEngine;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 패널이 활성화될 때 <b>배치를 다시 잡고</b>(<see cref="SidePanel.Place"/>)
    /// <b>살짝 작은 상태에서 제 크기로 커지며</b> 나타나게 하는 등장 처리.
    /// 부착은 <see cref="SidePanel.Attach"/>가 담당한다.
    /// <para>패널은 <c>SetActive</c>로 표시되므로 <see cref="OnEnable"/>이 표시마다 호출된다 —
    /// 컨트롤러의 표시 로직을 건드리지 않고 배치·연출만 얹을 수 있다. 표시 시점에 배치를 다시 잡는 이유는
    /// 창이 화면 가장자리에 걸쳐 있을 때 <b>창을 움직이는 대신</b> 패널을 보이는 공간 쪽으로 열기 위함이다.</para>
    /// <para><b>가운데에서 커진다</b> — 패널 루트의 피벗은 도킹 때문에 화면 쪽 가장자리(x=0 또는 1)에
    /// 있어서 그냥 배율만 주면 그 가장자리에서 자라난다. 배율에 맞춰 <c>anchoredPosition</c>을
    /// 보정해 <b>패널 중심을 고정</b>하므로, 피벗이 어디에 구워져 있든 가운데에서 커진다.</para>
    /// <para>연출은 배율만 건드리고 도착 위치·크기는 그대로 두므로, 중간에 끊겨도(연출 중 숨김)
    /// <see cref="OnDisable"/>에서 원상 복구된다.</para>
    /// <para><b>파일명 주의</b> — MonoBehaviour는 파일명이 클래스명과 같아야 Unity가 스크립트 참조를
    /// 해결한다. <c>SidePanel.cs</c>에 함께 두면 에디터 빌더가 프리팹을 구울 때
    /// "missing script"로 저장이 실패하므로 이 파일을 분리해 둔다.</para>
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class SidePanelPop : MonoBehaviour
    {
        /// <summary>등장 시간(초).</summary>
        private const float Duration = 0.15f;

        /// <summary>시작 배율(이 크기에서 1로 커진다).</summary>
        private const float StartScale = 0.8f;

        // 튜닝 값은 상수로 둔다 — 프리팹에 구워진 옛 직렬화 값에 좌우되지 않게 하기 위함이다.

        // 어느 쪽에 붙는 패널인지. 표시할 때마다 이 값을 기준으로 배치를 다시 잡으므로(SidePanel.Place)
        // 반드시 직렬화해야 한다 — 계층은 에디터 빌더가 프리팹에 구워 두고 런타임에 다시 만들지 않는다.
        [Tooltip("전투 화면을 기준으로 패널이 열리는 쪽(공간이 없으면 실행 중 반대쪽으로 바뀔 수 있다).")]
        [SerializeField] private SidePanel.Side side = SidePanel.Side.Left;

        private RectTransform _rect;
        private Vector2 _restPos;
        private bool _restCaptured;
        private Coroutine _anim;

        /// <summary>패널이 열리는 쪽을 지정한다(<see cref="SidePanel.Attach"/>가 호출).</summary>
        public void Configure(SidePanel.Side value)
        {
            side = value;
        }

        private void Awake()
        {
            CaptureRest();
        }

        /// <summary>도착(정지) 위치를 1회 기억한다. 연출 중에 호출돼도 값이 오염되지 않는다.</summary>
        private void CaptureRest()
        {
            if (_restCaptured)
            {
                return;
            }
            _rect = _rect != null ? _rect : GetComponent<RectTransform>();
            _restPos = _rect.anchoredPosition;
            _restCaptured = true;
        }

        private void OnEnable()
        {
            CaptureRest();
            if (!Application.isPlaying)
            {
                Restore(); // 에디터 빌더가 계층을 구울 때는 연출을 돌리지 않는다(제 크기 유지).
                return;
            }
            if (_anim != null)
            {
                StopCoroutine(_anim);
            }
            // 표시할 때마다 배치를 다시 잡는다 — 창이 화면 가장자리에 걸쳐 있으면 창을 움직이는 대신
            // 보이는 공간 쪽으로 열린다. 도착 위치도 그 결과로 갱신한다.
            SidePanel.Place(_rect, side);
            _restPos = _rect.anchoredPosition;

            ApplyScale(StartScale); // 첫 프레임부터 작은 상태로 보이도록 즉시 적용
            _anim = StartCoroutine(Play());
        }

        private void OnDisable()
        {
            // 다음 표시를 위해 제 크기·제자리로 되돌려 둔다(연출 중 숨겨졌을 때 상태가 남지 않도록).
            Restore();
            _anim = null;
        }

        private IEnumerator Play()
        {
            float elapsed = 0f;
            while (elapsed < Duration)
            {
                elapsed += Time.unscaledDeltaTime; // 일시정지(timeScale 0)에서도 동작
                float k = Mathf.Clamp01(elapsed / Duration);
                ApplyScale(Mathf.LerpUnclamped(StartScale, 1f, EaseOutCubic(k)));
                yield return null;
            }

            Restore();
            _anim = null;
        }

        /// <summary>패널 중심을 고정한 채 배율을 적용한다(피벗이 가장자리여도 가운데에서 커진다).</summary>
        private void ApplyScale(float scale)
        {
            if (_rect == null)
            {
                return;
            }
            _rect.localScale = new Vector3(scale, scale, 1f);

            // 피벗 → 중심까지의 거리. 배율 s에서 중심은 그 거리의 s배 지점으로 당겨지므로,
            // (1 - s)만큼 되밀어 주면 중심이 제자리에 머문다.
            var pivotToCenter = new Vector2(
                (0.5f - _rect.pivot.x) * _rect.rect.width,
                (0.5f - _rect.pivot.y) * _rect.rect.height);
            _rect.anchoredPosition = _restPos + pivotToCenter * (1f - scale);
        }

        /// <summary>제 크기·제자리로 되돌린다.</summary>
        private void Restore()
        {
            if (_rect == null || !_restCaptured)
            {
                return;
            }
            _rect.localScale = Vector3.one;
            _rect.anchoredPosition = _restPos;
        }

        /// <summary>끝에서 부드럽게 멎는 감속 곡선.</summary>
        private static float EaseOutCubic(float k)
        {
            float p = 1f - Mathf.Clamp01(k);
            return 1f - p * p * p;
        }
    }
}
