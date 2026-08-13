using UnityEngine;

namespace TaskbarHero.Client.Battle
{
    /// <summary>
    /// 스킬 이펙트(SpriteSequenceEffect 프레임 애니)를 대상을 향해 날아가는 투사체처럼 이동시킨다.
    /// 발사 위치(시전자)에서 대상으로 이동하고, 도달하면 onArrive(데미지)를 1회 호출한다.
    /// 비행 중에는 이펙트를 루프시켜 도달 전 조기 소멸을 막고, 도달 시 루프를 풀어 애니를 마저 재생한 뒤
    /// 자연히 파괴되도록 한다(비행 거리/속도와 애니 길이가 어긋나도 안전).
    /// <para><c>stopOnArrive</c>로 발사하면 <b>명중하는 순간 재생을 멈추고 즉시 사라진다</b> —
    /// 기본공격 투사체가 이 경우다. 마법사의 화염구처럼 프레임이 지날수록 <b>커지는</b> 그림은
    /// 명중 뒤에 남은 프레임을 마저 재생하면 <b>맞은 자리에서 불길이 더 크게 번지는 것처럼</b> 보여
    /// 타격이 끝난 시점이 흐려진다(화살은 맞는 즉시 사라진다 — 같은 규칙으로 맞춘다).</para>
    /// </summary>
    public class ProjectileEffect : MonoBehaviour
    {
        private Transform _target;
        private float _speed = 12f;
        private float _aimY = 0.6f;
        private System.Action _onArrive;
        private bool _arrived;
        private bool _stopOnArrive;
        private float _life = 6f;
        private SpriteSequenceEffect _sse;

        /// <param name="stopOnArrive">true면 <b>대상에 명중한 순간</b> 이펙트 재생을 멈추고 즉시 파괴한다.
        /// 대상이 먼저 사라져 헛 도달한 경우는 해당하지 않는다(그때는 애니를 마저 재생하며 자연히 꺼진다).</param>
        public void Launch(Transform target, float speed, float aimY, System.Action onArrive,
                           bool stopOnArrive = false)
        {
            _target = target;
            _speed = Mathf.Max(1f, speed);
            _aimY = aimY;
            _onArrive = onArrive;
            _arrived = false;
            _stopOnArrive = stopOnArrive;

            _sse = GetComponent<SpriteSequenceEffect>();
            if (_sse != null)
            {
                // 비행 중엔 루프시켜 도달 전에 파괴되지 않게 한다.
                _sse.loop = true;
                _sse.destroyOnFinish = true;
            }
        }

        private void Update()
        {
            _life -= Time.deltaTime;
            if (_life <= 0f) { Destroy(gameObject); return; } // 안전장치

            if (_arrived) return;

            if (_target == null)
            {
                Arrive(hit: false); // 대상이 먼저 사라짐 — 명중이 아니므로 애니는 마저 재생한다
                return;
            }

            Vector3 tp = _target.position + Vector3.up * _aimY;
            Vector3 cur = transform.position;
            Vector3 dir = tp - cur;
            float dist = dir.magnitude;
            float step = _speed * Time.deltaTime;

            if (dist <= step || dist < 0.05f)
            {
                transform.position = tp;
                Arrive(hit: true);
                return;
            }
            transform.position = cur + dir / dist * step;
        }

        private void Arrive(bool hit)
        {
            _arrived = true;
            _onArrive?.Invoke();

            if (hit && _stopOnArrive)
            {
                // 명중이 곧 투사체의 끝 — 남은 프레임을 이어 재생하지 않고 그 자리에서 사라진다.
                Destroy(gameObject);
                return;
            }
            // 도달 지점에서 애니를 마저 재생하고 끝나면 자동 파괴(루프 해제).
            if (_sse != null) _sse.loop = false;
        }
    }
}
