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
        private Func<MonsterUnit, bool> _onAttack; // 아군 공격 시도(주입) — 대상이 있으면 데미지 적용 후 true
        private float _attackInterval;         // 공격 주기(초, 0 이하면 공격 안 함)
        private float _attackTimer;
        private bool _isBoss;

        [Tooltip("사망 애니 후 오브젝트가 사라지기까지 지연(초)")]
        private const float DeathLinger = 0.8f;

        // 보스 연출 상수: 일반 몹 대비 3배 크기, 머리 위 왕관 아이콘의 월드 폭/여백.
        private const float BossScale = 3f;
        private const float CrownWorldWidth = 1.4f;
        private const float CrownYMargin = 0.4f;
        private const int CrownSortingOffset = 50;

        public bool Alive => _alive;
        public bool IsBoss => _isBoss;
        public long Hp => _hp;
        public long MaxHp => _maxHp;
        public long Atk => _atk;
        public string MonsterName => _name;

        /// <summary>스폰 직후 호출해 스탯/이동속도/콜백을 주입하고 idle로 초기화한다.
        /// isBoss=true면 3배 크기로 키우고(이동 방향 부호 유지) 머리 위에 왕관 아이콘을 붙인다.
        /// onAttack/attackInterval을 주면 목표 지점에 멈춘 뒤 주기적으로 아군을 공격한다.</summary>
        public void Init(string monsterName, long hp, long atk, float moveSpeed,
                         Func<bool> isPaused, Action<MonsterUnit> onDeath,
                         bool isBoss = false, Sprite bossIcon = null,
                         Func<MonsterUnit, bool> onAttack = null, float attackInterval = 0f)
        {
            _name = monsterName;
            _maxHp = Math.Max(1L, hp);
            _hp = _maxHp;
            _atk = atk;
            _moveSpeed = Mathf.Max(0f, moveSpeed);
            _isPaused = isPaused;
            _onDeath = onDeath;
            _onAttack = onAttack;
            _attackInterval = attackInterval;
            _attackTimer = 0f;
            _alive = true;
            _isBoss = isBoss;
            _targetX = transform.position.x;
            gameObject.name = (isBoss ? "Boss_" : "Monster_") + _name;

            if (isBoss)
            {
                transform.localScale *= BossScale; // 부호(좌우 방향) 유지한 채 3배 확대
                if (bossIcon != null)
                {
                    StartCoroutine(AttachCrown(bossIcon));
                }
            }

            SendMessage("PlayIdle", SendMessageOptions.DontRequireReceiver);
        }

        /// <summary>보스 머리 위에 왕관 아이콘을 붙인다. SPUM 파트가 구성될 때까지 잠깐 기다린 뒤
        /// 몬스터 렌더러 경계 상단에 배치하고, 확대된 부모 스케일을 보정해 일정한 월드 크기로 표시한다.</summary>
        private IEnumerator AttachCrown(Sprite icon)
        {
            // SPUM이 스프라이트 파트를 붙일 시간을 준다(1~2프레임).
            yield return null;
            yield return null;
            if (this == null || !_alive)
            {
                yield break;
            }

            var rends = GetComponentsInChildren<SpriteRenderer>(true);
            var bounds = new Bounds(transform.position, Vector3.zero);
            bool hasBounds = false;
            int maxOrder = int.MinValue;
            int sortingLayerId = 0;
            foreach (var r in rends)
            {
                if (r == null)
                {
                    continue;
                }
                if (!hasBounds) { bounds = r.bounds; hasBounds = true; }
                else { bounds.Encapsulate(r.bounds); }
                if (r.sortingOrder > maxOrder) { maxOrder = r.sortingOrder; sortingLayerId = r.sortingLayerID; }
            }

            var crownGo = new GameObject("BossCrown");
            var sr = crownGo.AddComponent<SpriteRenderer>();
            sr.sprite = icon;
            if (maxOrder != int.MinValue)
            {
                sr.sortingLayerID = sortingLayerId;
                sr.sortingOrder = maxOrder + CrownSortingOffset; // 항상 몬스터 스프라이트 앞
            }

            crownGo.transform.SetParent(transform, true); // 부모(보스)를 따라 이동하도록 자식으로

            // 확대된 부모 lossyScale을 보정해 왕관을 일정한 월드 폭으로 맞춘다.
            float spriteW = icon.bounds.size.x;
            float worldScale = spriteW > 0.001f ? CrownWorldWidth / spriteW : 1f;
            Vector3 lossy = transform.lossyScale;
            crownGo.transform.localScale = new Vector3(
                worldScale / Mathf.Max(0.0001f, Mathf.Abs(lossy.x)),
                worldScale / Mathf.Max(0.0001f, Mathf.Abs(lossy.y)),
                1f);

            // 머리 위(경계 상단 + 여백)에 배치. 부모를 따라가도록 월드 좌표로 지정.
            float topY = hasBounds ? bounds.max.y : transform.position.y + 2f;
            float centerX = hasBounds ? bounds.center.x : transform.position.x;
            crownGo.transform.position = new Vector3(centerX, topY + CrownYMargin, 0f);
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
                TickAttack(); // 목표 지점(파티 앞)에 멈춘 동안 주기적으로 아군 공격
            }
        }

        /// <summary>목표 지점에 멈춰 있는 동안 공격 주기마다 아군 공격을 시도하고, 대상이 있으면 공격 애니를 재생한다.</summary>
        private void TickAttack()
        {
            if (_onAttack == null || _attackInterval <= 0f) return;
            _attackTimer += Time.deltaTime;
            if (_attackTimer >= _attackInterval)
            {
                _attackTimer = 0f;
                if (_onAttack(this))
                {
                    SendMessage("PlayAttackOnce", SendMessageOptions.DontRequireReceiver);
                }
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
