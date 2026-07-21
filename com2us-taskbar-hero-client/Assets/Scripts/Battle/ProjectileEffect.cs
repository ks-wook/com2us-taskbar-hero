using UnityEngine;

namespace TaskbarHero.Client.Battle
{
    /// <summary>
    /// 스킬 이펙트(SpriteSequenceEffect 프레임 애니)를 대상을 향해 날아가는 투사체처럼 이동시킨다.
    /// 발사 위치(시전자)에서 대상으로 이동하고, 도달하면 onArrive(데미지)를 1회 호출한다.
    /// 비행 중에는 이펙트를 루프시켜 도달 전 조기 소멸을 막고, 도달 시 루프를 풀어 애니를 마저 재생한 뒤
    /// 자연히 파괴되도록 한다(비행 거리/속도와 애니 길이가 어긋나도 안전).
    /// </summary>
    public class ProjectileEffect : MonoBehaviour
    {
        private Transform _target;
        private float _speed = 12f;
        private float _aimY = 0.6f;
        private System.Action _onArrive;
        private bool _arrived;
        private float _life = 6f;
        private SpriteSequenceEffect _sse;

        public void Launch(Transform target, float speed, float aimY, System.Action onArrive)
        {
            _target = target;
            _speed = Mathf.Max(1f, speed);
            _aimY = aimY;
            _onArrive = onArrive;
            _arrived = false;

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
                Arrive();
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
                Arrive();
                return;
            }
            transform.position = cur + dir / dist * step;
        }

        private void Arrive()
        {
            _arrived = true;
            _onArrive?.Invoke();
            // 도달 지점에서 애니를 마저 재생하고 끝나면 자동 파괴(루프 해제).
            if (_sse != null) _sse.loop = false;
        }
    }
}
