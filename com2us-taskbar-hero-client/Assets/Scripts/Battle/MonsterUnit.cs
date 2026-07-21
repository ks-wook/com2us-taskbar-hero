using System;
using System.Collections;
using UnityEngine;

namespace TaskbarHero.Client.Battle
{
    /// <summary>
    /// 몰려오는 적(몬스터) 1기. HP/공격력/이름을 가지며, 파티를 향해 왼쪽으로 전진하다
    /// 지정된 목표 x(<see cref="SetTargetX"/>)에서 멈춘다. 데미지를 받아 죽으면 사망 애니 후 스스로 소멸한다.
    /// 생성/추적/정리는 범용 <c>ObjectManager</c>가, 무엇을·언제 스폰할지와 처치/일시정지 판정은
    /// 전투 로직(개발용 <c>BattleDevController</c>)이 콜백으로 주입한다(특정 컨트롤러 타입에 직접 의존하지 않음).
    /// </summary>
    public class MonsterUnit : MonoBehaviour
    {
        private string _name = "Monster";
        private long _maxHp = 1;
        private long _hp = 1;
        private long _atk;
        private float _moveSpeed;
        private float _targetX;
        private bool _alive;
        private bool _moving;

        private Func<bool> _isPaused;          // 전투 일시정지 여부(주입)
        private Action<MonsterUnit> _onDeath;  // 사망 통지(주입) — 누적 킬/로그 등

        [Tooltip("사망 애니 후 오브젝트가 사라지기까지 지연(초)")]
        private const float DeathLinger = 0.8f;

        public bool Alive => _alive;
        public long Hp => _hp;
        public long MaxHp => _maxHp;
        public long Atk => _atk;
        public string MonsterName => _name;

        /// <summary>스폰 직후 호출해 스탯/이동속도/콜백을 주입하고 idle로 초기화한다.</summary>
        public void Init(string monsterName, long hp, long atk, float moveSpeed,
                         Func<bool> isPaused, Action<MonsterUnit> onDeath)
        {
            _name = monsterName;
            _maxHp = Math.Max(1L, hp);
            _hp = _maxHp;
            _atk = atk;
            _moveSpeed = Mathf.Max(0f, moveSpeed);
            _isPaused = isPaused;
            _onDeath = onDeath;
            _alive = true;
            _targetX = transform.position.x;
            gameObject.name = "Monster_" + _name;
            SendMessage("PlayIdle", SendMessageOptions.DontRequireReceiver);
        }

        /// <summary>이 몬스터가 멈출 목표 x(파티 앞 라인). 왼쪽으로만 이동하며 이 지점에서 정지한다.</summary>
        public void SetTargetX(float x) { _targetX = x; }

        /// <summary>데미지를 적용하고, 이번 타격으로 죽었으면 true를 1회 반환한다.</summary>
        public bool TakeDamage(long dmg)
        {
            if (!_alive) return false;
            _hp -= dmg;
            if (_hp <= 0)
            {
                _hp = 0;
                Die();
                return true;
            }
            return false;
        }

        /// <summary>매 프레임 파티를 향해 왼쪽으로만 전진하고, 목표 x에 닿으면 멈춘다(전진/정지 애니 전환).</summary>
        private void Update()
        {
            if (!_alive || (_isPaused != null && _isPaused())) return;

            float x = transform.position.x;
            if (x - _targetX > 0.02f)
            {
                float nx = Mathf.MoveTowards(x, _targetX, _moveSpeed * Time.deltaTime);
                var p = transform.position; p.x = nx; transform.position = p;
                SetMoving(true);
            }
            else
            {
                SetMoving(false);
            }
        }

        /// <summary>이동/정지 상태가 바뀔 때만 SPUM 전진/대기 애니를 전송한다(클립 재시작 방지).</summary>
        private void SetMoving(bool moving)
        {
            if (moving == _moving) return;
            _moving = moving;
            SendMessage(moving ? "PlayMove" : "PlayIdle", SendMessageOptions.DontRequireReceiver);
        }

        /// <summary>HP 0: 사망 처리 — 사망 애니 재생, 처치 콜백 호출, 잠시 뒤 자기 소멸.</summary>
        private void Die()
        {
            _alive = false;
            SendMessage("PlayDeathOnce", SendMessageOptions.DontRequireReceiver);
            _onDeath?.Invoke(this);
            StartCoroutine(DespawnAfter());
        }

        /// <summary>사망 애니가 보이도록 잠깐 대기 후 오브젝트를 파괴한다(ObjectManager는 널을 자동 정리).</summary>
        private IEnumerator DespawnAfter()
        {
            yield return new WaitForSeconds(DeathLinger);
            Destroy(gameObject);
        }
    }
}
