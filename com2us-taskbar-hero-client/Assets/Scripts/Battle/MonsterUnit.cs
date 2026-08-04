using System;
using System.Collections;
using UnityEngine;
using TaskbarHero.Client.Managers;

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

        // 보스 연출 상수: 일반 몹 대비 3배 크기, 머리 위 왕관 아이콘의 월드 폭.
        private const float BossScale = 3f;
        private const float CrownWorldWidth = 1.4f;
        private const int CrownSortingOffset = 50;

        /// <summary>보스의 머리(스프라이트 상단)와 왕관 사이에 HP바가 들어갈 수 있도록,
        /// 왕관 하단을 머리에서 띄우는 여백(월드 단위). HP바 배치(<c>BattleDevController</c>)와 공유한다.</summary>
        public const float BossHpBarBand = 0.5f;

        // HP바를 붙일 '머리 위' 기준점(몬스터 transform 기준 월드 오프셋). 스폰 직후 1회만 측정한다.
        private Vector2 _headAnchorOffset;
        private bool _hasHeadAnchor;

        public bool Alive => _alive;
        public bool IsBoss => _isBoss;
        /// <summary>현재 전진(이동) 애니메이션이 재생 중인지. 걷기 먼지 이펙트 노출 판정에 사용.</summary>
        public bool IsMoving => _moving && _alive;
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
            _hasHeadAnchor = false; // 풀에서 재사용될 수 있으므로 머리 기준점을 다시 측정한다
            gameObject.name = (isBoss ? "Boss_" : "Monster_") + _name;

            if (isBoss)
            {
                transform.localScale *= BossScale; // 부호(좌우 방향) 유지한 채 3배 확대
                // 보스 등장 포효 — Warning!! 배너·경보음과 같은 시점이다(사운드 정의서 §5.6).
                SoundManager.Sfx(SoundId.BossRoar);
                if (bossIcon != null)
                {
                    StartCoroutine(AttachCrown(bossIcon));
                }
            }

            SendMessage("PlayIdle", SendMessageOptions.DontRequireReceiver);
            StartCoroutine(MeasureHeadAnchor());
        }

        /// <summary>
        /// HP바를 붙일 '머리 위' 기준점을 돌려준다(몬스터 <c>transform</c> 기준 월드 오프셋 — x는 몸통 중앙,
        /// y는 몸통 상단). 아직 측정되지 않았으면 false.
        /// </summary>
        public bool TryGetHeadAnchor(out Vector2 offset)
        {
            offset = _headAnchorOffset;
            return _hasHeadAnchor;
        }

        /// <summary>
        /// 머리 위 기준점을 <b>스폰 직후 idle 자세에서 한 번만</b> 측정해 캐시한다.
        /// <para>매 프레임 스프라이트 경계를 재면 걷기·공격·피격 애니메이션이 파트를 움직일 때마다 상단·중앙이
        /// 요동쳐 HP바가 심하게 떨린다. 반면 몬스터의 <c>transform</c>은 이동(x)만 갱신되므로,
        /// 여기서 잰 오프셋을 그 위치에 더하면 <b>흔들림 없이 따라다니기만</b> 한다.</para>
        /// <para>왕관(<c>BossCrown</c>)은 머리 위에 따로 띄우는 장식이라 몸통 경계에서 제외한다.
        /// 같은 프레임에 붙을 수 있어(AttachCrown도 2프레임 대기) 이름으로 걸러 낸다.</para>
        /// </summary>
        private IEnumerator MeasureHeadAnchor()
        {
            // SPUM이 스프라이트 파트를 붙이고 idle 첫 프레임이 적용될 시간을 준다(1~2프레임).
            yield return null;
            yield return null;
            if (this == null || !_alive)
            {
                yield break;
            }

            var rends = GetComponentsInChildren<SpriteRenderer>(true);
            Bounds body = default;
            bool has = false;
            foreach (var r in rends)
            {
                if (r == null || r.gameObject.name == "BossCrown")
                {
                    continue;
                }
                if (!has) { body = r.bounds; has = true; } else { body.Encapsulate(r.bounds); }
            }
            if (!has)
            {
                yield break; // 렌더러가 아직 없으면 다음 스폰에서 다시 측정된다(그때까지 폴백 사용)
            }

            Vector3 pos = transform.position;
            _headAnchorOffset = new Vector2(body.center.x - pos.x, body.max.y - pos.y);
            _hasHeadAnchor = true;
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

            // 왕관을 머리 위로 띄워 그 아래(머리와 왕관 사이)에 HP바 공간을 확보한다.
            // 왕관 하단이 머리 상단보다 BossHpBarBand만큼 위에 오도록 중심 Y를 계산한다.
            float topY = hasBounds ? bounds.max.y : transform.position.y + 2f;
            float centerX = hasBounds ? bounds.center.x : transform.position.x;
            float crownWorldHeight = CrownWorldWidth * (icon.bounds.size.y / Mathf.Max(0.0001f, icon.bounds.size.x));
            crownGo.transform.position = new Vector3(centerX, topY + BossHpBarBand + crownWorldHeight * 0.5f, 0f);
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
                    // 몬스터·보스 공격은 전 계열 공용음 하나를 쓴다(사운드 정의서 §5.6·§8).
                    SoundManager.Sfx(SoundId.MonAttack);
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
            // 몬스터·보스 사망 공용음(§5.1·§8). 웨이브 전멸처럼 여러 마리가 동시에 죽어도
            // SoundManager의 같은 클립 쿨다운(0.05초)이 소리가 찢어지는 것을 막는다(§9.3).
            SoundManager.Sfx(SoundId.MonsterDeath);
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
