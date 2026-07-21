using UnityEngine;

namespace TaskbarHero.Client.Battle
{
    /// <summary>
    /// 대상을 향해 날아가는 화살 투사체. 도달하면 onHit 콜백(데미지 적용)을 호출하고 자기 자신을 파괴한다.
    /// 진행 방향으로 회전한다(스프라이트는 오른쪽 +x를 향하도록 제작).
    /// </summary>
    public class ArrowProjectile : MonoBehaviour
    {
        private Transform _target;
        private float _speed = 14f;
        private System.Action _onHit;
        private float _life = 4f;
        private float _aimY = 0.5f;

        public void Launch(Transform target, float speed, float aimY, System.Action onHit)
        {
            _target = target;
            _speed = speed;
            _aimY = aimY;
            _onHit = onHit;
        }

        private void Update()
        {
            _life -= Time.deltaTime;
            if (_life <= 0f || _target == null)
            {
                Destroy(gameObject);
                return;
            }

            Vector3 tp = _target.position + Vector3.up * _aimY;
            Vector3 dir = tp - transform.position;
            float dist = dir.magnitude;

            if (dist > 0.0001f)
            {
                float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                transform.rotation = Quaternion.Euler(0f, 0f, ang);
            }

            float step = _speed * Time.deltaTime;
            if (dist <= step)
            {
                transform.position = tp;
                _onHit?.Invoke();
                Destroy(gameObject);
                return;
            }
            transform.position += dir / dist * step;
        }
    }
}
