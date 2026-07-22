using System.Collections.Generic;
using UnityEngine;
using TaskbarHero.Client.Managers;
using TaskbarHero.Client.MasterData;
using TaskbarHero.Common.MasterData;

namespace TaskbarHero.Client.Battle
{
    /// <summary>
    /// 파티 멤버 1인의 전투를 담당하는 통일 전투 유닛(기사·레인저·마법사 등 모든 직업 공용).
    /// 몬스터/전진/대형/카메라/공용 데미지는 <see cref="BattleDevController"/>가 관리하고,
    /// 이 컴포넌트는 자기 캐릭터의 기본공격(근접/원거리 화살)·스킬(공격/버프)·돌진형 스킬·
    /// 분노 애니·모션 게이트를 처리한다. 설정은 <see cref="PartyMemberConfig"/>로 주입받는다.
    /// </summary>
    public class PlayerCombatant : MonoBehaviour
    {
        private class Skill
        {
            public int code;
            public string name;
            public int coefType;   // 1=공격 2=버프 3=디버프
            public float coef;
            public float duration;
            public float cooldown;
            public float timer;
            public GameObject effect;
            public Sprite icon;
        }

        private BattleDevController _ctrl;
        private PartyMemberConfig _cfg;
        private int _classCode;
        private bool _ranged;
        private float _attackRange;
        private string _attackAnim;
        private bool _castHold;       // 스킬 시전 시 손 든 프레임 정지 홀드(캐스터)
        private GameObject _arrowPrefab;
        private float _arrowSpeed = 9f;

        // 돌진형 스킬
        private int _chargeSkillCode;
        private float _chargeRange;
        private float _chargeSpeed;
        private int _rainSkillCode;   // 공중 화살비형 스킬 코드(0=없음)
        private int _aoeSkillCode;    // 광역(범위) 스킬 코드(0=없음)
        private bool _basicAttackAoe; // true면 근접 기본공격이 사거리 내 모든 적에게 명중
        private float _selfEffectXOffset;   // 자기 위치 이펙트 X 오프셋(+=오른쪽)
        private bool _charging;
        private Skill _chargeSkill;
        public bool IsCharging => _charging;

        private string _name = "Ally";
        private long _atk;
        private long _maxHp = 1;
        private long _hp = 1;
        private float _cooldown;
        private float _moveSpeed;

        /// <summary>아군 현재 체력.</summary>
        public long Hp => _hp;
        /// <summary>아군 최대 체력.</summary>
        public long MaxHp => _maxHp;

        private readonly List<Skill> _skills = new List<Skill>();
        private float _attackTimer;
        private float _busyTimer;
        private float _atkBuffMult = 1f;
        private float _buffTimer;
        private GameObject _buffEffect;

        // 개별 이동(대형 목표를 자기 속도로 추격 — 칼같은 정렬이 아닌 동적 이동)
        private Vector2 _formTarget;
        private bool _hasTarget;
        private bool _moving;
        private float _stillTime;   // 목표에 도달해 멈춰 있던 시간(idle 전환 디바운스)
        private Animator _anim;     // 공격 모션 재생 여부 판정용(SPUM Animator)

        /// <summary>컨트롤러가 매 프레임 이 멤버의 대형 목표(월드 좌표)를 갱신한다.</summary>
        public void SetFormationTarget(Vector2 target) { _formTarget = target; _hasTarget = true; }

        public string DisplayName => _name;
        public float AttackRange => _attackRange;
        public float MoveSpeed => _moveSpeed;

        /// <summary>컨트롤러가 스폰 직후 호출해 설정을 주입한다.</summary>
        public void Configure(BattleDevController ctrl, PartyMemberConfig cfg)
        {
            _ctrl = ctrl;
            _cfg = cfg;
            _classCode = cfg.classCode;
            _ranged = cfg.ranged;
            _attackRange = cfg.attackRange;
            _attackAnim = cfg.attackAnim;
            _castHold = cfg.castHold;
            _arrowPrefab = cfg.arrowPrefab;
            _chargeSkillCode = cfg.chargeSkillCode;
            _chargeRange = cfg.chargeRange;
            _chargeSpeed = cfg.chargeSpeed;
            _rainSkillCode = cfg.rainSkillCode;
            _aoeSkillCode = cfg.aoeSkillCode;
            _basicAttackAoe = cfg.basicAttackAoe;
            _selfEffectXOffset = cfg.selfEffectXOffset;
            _anim = GetComponentInChildren<Animator>();

            LoadStats();
            BuildSkills();
        }

        private void LoadStats()
        {
            var db = MasterDataManager.Db;
            if (db != null && db.Classes.TryGetValue(_classCode, out ClassMaster cls))
            {
                _name = cls.name;
                _atk = cls.baseStats.atk;
                _maxHp = System.Math.Max(1L, cls.baseStats.hp);
                _cooldown = cls.baseStats.cooldown > 0f ? cls.baseStats.cooldown : 1.2f;
                _moveSpeed = cls.baseStats.moveSpeed > 0f ? cls.baseStats.moveSpeed : 3f;
            }
            else
            {
                _name = "Ally(?" + _classCode + ")";
                _atk = 10; _maxHp = 100; _cooldown = 1.2f; _moveSpeed = 3f;
            }
            _hp = _maxHp;
            gameObject.name = "Player_" + _name;
        }

        private void BuildSkills()
        {
            _skills.Clear();
            _chargeSkill = null;
            var db = MasterDataManager.Db;
            if (db == null) return;

            var actives = new List<SkillMaster>();
            foreach (var s in db.Skills.Values)
                if (s.classCode == _classCode && s.skillType == 1) actives.Add(s);
            actives.Sort((a, b) => a.skillCode.CompareTo(b.skillCode));

            float[] initReadyIn = { 2f, 4f, 6f, 8f };
            int level = Mathf.Max(1, _ctrl != null ? _ctrl.DevSkillLevel : 1);
            for (int i = 0; i < actives.Count; i++)
            {
                var s = actives[i];
                int lv = Mathf.Clamp(level, 1, Mathf.Max(1, s.maxLevel));
                float coef = 0f, dur = 0f; int ct = 1;
                if (s.coefs != null)
                    foreach (var c in s.coefs)
                        if (c.skillLevel == lv) { coef = c.coef; dur = c.duration; ct = c.coefType; break; }

                float cd = s.cooldown > 0f ? s.cooldown : (_ctrl != null ? _ctrl.SkillCooldownFallback : 10f);
                float readyIn = i < initReadyIn.Length ? initReadyIn[i] : 2f;
                var sk = new Skill
                {
                    code = s.skillCode, name = s.name, coefType = ct, coef = coef, duration = dur,
                    cooldown = cd, timer = Mathf.Max(0f, cd - readyIn),
                    effect = _cfg != null ? _cfg.EffectFor(s.skillCode) : null,
                    icon = _cfg != null ? _cfg.IconFor(s.skillCode) : null,
                };
                _skills.Add(sk);
                if (sk.code == _chargeSkillCode) _chargeSkill = sk;
            }
        }

        private void Update()
        {
            if (_ctrl == null || _ctrl.IsPaused) return;

            TickTimers();
            if (_busyTimer > 0f) _busyTimer -= Time.deltaTime;

            // 돌진 진행 중이면 그것만 처리(자기 위치를 스스로 이동)
            if (_charging)
            {
                UpdateCharge();
                return;
            }

            bool attacking = IsAttackMotionPlaying();

            // 돌진형 스킬: 모션 중이 아니고 준비됐고 적이 chargeRange 이내면 발동(전진/교전 무관)
            if (_busyTimer <= 0f && !attacking && TryStartCharge())
            {
                return;
            }

            // 전투: 교전 중 + 사거리 안 + 모션 없음이면 스킬/공격
            bool fighting = _ctrl.IsFighting && _ctrl.MonsterAlive && _ctrl.MonsterTransform != null;
            if (fighting)
            {
                float dist = _ctrl.MonsterTransform.position.x - transform.position.x;
                _attackTimer += Time.deltaTime;
                if (_busyTimer <= 0f && !attacking && dist > 0f && dist <= _attackRange)
                {
                    if (!TryCastSkill())
                    {
                        if (_attackTimer >= Mathf.Max(0.05f, _cooldown))
                        {
                            _attackTimer = 0f;
                            BasicAttack();
                        }
                    }
                }
            }

            // 이동: 대형 목표로 자기 속도 추격. **모션(스킬/공격) 중엔 정지**해 스킬을 끊지 않는다.
            UpdateMovement();
        }

        /// <summary>대형 목표를 자기 이동속도로 추격한다(뒤처지면 가속). 모션 중엔 멈춰 시전을 완료.</summary>
        private void UpdateMovement()
        {
            if (!_hasTarget) return;
            if (_busyTimer > 0f) return; // 스킬/공격 모션 진행 중 → 위치·애니 유지(끊김 방지)
            if (IsAttackMotionPlaying()) return; // 공격 애니가 끝날 때까지 이동 금지(모션 완료 후 이동)

            Vector3 p = transform.position;
            var cur = new Vector2(p.x, p.y);
            float gap = Vector2.Distance(cur, _formTarget);

            // 이동 임계값은 작게(목표가 조금이라도 움직이면 계속 추격) → 걷기 애니가 매 프레임 끊기지 않음
            if (gap > 0.02f)
            {
                float spd = Mathf.Max(0.5f, _moveSpeed) * (1f + gap * 0.6f); // 뒤처질수록 가속
                var next = Vector2.MoveTowards(cur, _formTarget, spd * Time.deltaTime);
                transform.position = new Vector3(next.x, next.y, 0f);
                _stillTime = 0f;
                SetMoving(true);
            }
            else
            {
                // 목표에 도달해도 잠깐의 정지로는 idle로 바꾸지 않음(전진 중 미세 정지에 의한 애니 튐 방지).
                // 일정 시간(0.15s) 이상 멈춰 있을 때만 idle로 전환.
                _stillTime += Time.deltaTime;
                if (_stillTime >= 0.15f) SetMoving(false);
            }
        }

        private void SetMoving(bool moving)
        {
            if (moving == _moving) return;
            _moving = moving;
            SendMessage(moving ? "PlayMove" : "PlayIdle", SendMessageOptions.DontRequireReceiver);
        }

        /// <summary>
        /// 공격 애니(SPUM "…Attack…" 클립)가 아직 재생 중인지. 트랜지션 중이거나, 현재 클립명이 "attack"을
        /// 포함하고 정규화 진행도가 1 미만이면 true. 이걸로 공격 모션이 완전히 끝난 뒤에만 이동한다.
        /// (캐스트홀드/화살비 정지도 attack 클립을 nt<1로 고정하므로 이동이 막힌다.)
        /// </summary>
        private bool IsAttackMotionPlaying()
        {
            if (_anim == null) return false;
            var ci = _anim.GetCurrentAnimatorClipInfo(0);
            if (ci == null || ci.Length == 0 || ci[0].clip == null) return false;
            if (ci[0].clip.name.ToLower().IndexOf("attack") < 0) return false;
            // 공격 클립이 아직 1회 재생을 끝내지 않았으면(정규화<1) 모션 진행 중.
            // (시작 순간의 트랜지션 구간은 _busyTimer가 커버하므로 여기선 현재 클립만 본다.)
            return _anim.GetCurrentAnimatorStateInfo(0).normalizedTime < 1f;
        }

        private void TickTimers()
        {
            foreach (var sk in _skills)
                if (sk.timer < sk.cooldown) sk.timer += Time.deltaTime;

            if (_buffTimer > 0f)
            {
                _buffTimer -= Time.deltaTime;
                if (_buffTimer <= 0f)
                {
                    _atkBuffMult = 1f;
                    if (_buffEffect != null) { Destroy(_buffEffect); _buffEffect = null; }
                }
            }
        }

        // ---- 스킬 시전(돌진 제외) ----

        private bool TryCastSkill()
        {
            foreach (var sk in _skills)
            {
                if (sk.code == _chargeSkillCode) continue; // 돌진은 별도 경로
                if (sk.timer >= sk.cooldown)
                {
                    CastSkill(sk);
                    sk.timer = 0f;
                    return true;
                }
            }
            return false;
        }

        private void PlayAttackAnim()
        {
            _moving = false; // 공격 애니가 idle로 복귀하므로 다음 이동 시 PlayMove 재전송되게
            if (string.IsNullOrEmpty(_attackAnim))
                SendMessage("PlayAttackOnce", SendMessageOptions.DontRequireReceiver);
            else
                SendMessage("PlayAttackByName", _attackAnim, SendMessageOptions.DontRequireReceiver);
        }

        private void CastSkill(Skill sk)
        {
            _moving = false; // 스킬 모션 후 idle 복귀 → 이후 이동 시 PlayMove 재전송
            if (sk.coefType == 2) // 버프(자기 강화)
            {
                SendMessage("PlayRage", SendMessageOptions.DontRequireReceiver); // 분노/기합 자세
                _atkBuffMult = Mathf.Max(1f, sk.coef);
                _buffTimer = sk.duration > 0f ? sk.duration : 5f;
                if (_buffEffect != null) { Destroy(_buffEffect); _buffEffect = null; }
                if (sk.effect != null)
                {
                    _buffEffect = Instantiate(sk.effect, transform, false);
                    _buffEffect.transform.localPosition = new Vector3(0f, _ctrl.EffectYOffset, 0f);
                }
                _busyTimer = _ctrl.BasicHitDelay;
            }
            else // 공격 스킬
            {
                long dmg = Damage(sk.coef);
                float motion = EffectDuration(sk.effect);
                string label = $"[{_name}] 스킬 {sk.name} ×{sk.coef:0.##}";

                if (_rainSkillCode != 0 && sk.code == _rainSkillCode)
                {
                    // 화살비: 점프→공중에서 활 하늘로 든 채 정지→대상 위치에 이펙트.
                    // 몬스터가 이미 죽었/없어도 시전 시 무조건 연출되도록 대상 위치를 캡처해 무조건 스폰.
                    _moving = false;
                    SendMessage("PlayArrowRain", motion, SendMessageOptions.DontRequireReceiver);
                    SpawnEffectAt(sk.effect, ArrowRainTargetPos());
                    _ctrl.DealDamageAfter(motion, dmg, label);
                    _busyTimer = motion + 0.4f; // 점프+홀드+착지 동안 대기
                }
                else if (_ranged && sk.effect != null && _ctrl.MonsterTransform != null)
                {
                    // 원거리(레인저): 스킬 이펙트를 투사체로 발사(자기 위치 → 대상). 도달 시 데미지.
                    PlayAttackAnim();
                    Vector3 origin = transform.position + Vector3.up * _ctrl.EffectYOffset;
                    var fx = Instantiate(sk.effect, origin, Quaternion.identity);
                    var proj = fx.GetComponent<ProjectileEffect>();
                    if (proj == null) proj = fx.AddComponent<ProjectileEffect>();
                    proj.Launch(_ctrl.MonsterTransform, _arrowSpeed, _ctrl.EffectYOffset,
                                () => _ctrl.DealDamageAfter(0f, dmg, label));
                    _busyTimer = motion;
                }
                else
                {
                    // 근접/캐스터: 자기 위치에서 이펙트 발생 + 이펙트 종료 후 데미지
                    if (_castHold)
                        SendMessage("PlayCastHold", motion, SendMessageOptions.DontRequireReceiver);
                    else
                        PlayAttackAnim();
                    var fx = SpawnEffectAtSelf(sk.effect);
                    if (_aoeSkillCode != 0 && sk.code == _aoeSkillCode)
                    {
                        // 광역(강타 등): 이펙트 범위 내 모든 적에게 데미지.
                        Vector3 center = fx != null
                            ? fx.transform.position
                            : transform.position + Vector3.up * _ctrl.EffectYOffset + Vector3.right * _selfEffectXOffset;
                        _ctrl.DealAreaDamageAfter(motion, dmg, label, center, EffectRadius(fx));
                    }
                    else
                    {
                        _ctrl.DealDamageAfter(motion, dmg, label);
                    }
                    _busyTimer = motion;
                }
            }
        }

        private void BasicAttack()
        {
            PlayAttackAnim();
            long dmg = Damage(1f);

            if (_ranged && _arrowPrefab != null)
            {
                var monster = _ctrl.MonsterTransform;
                Vector3 origin = transform.position + Vector3.up * _ctrl.EffectYOffset;
                var arrowGo = Instantiate(_arrowPrefab, origin, Quaternion.identity);
                var arrow = arrowGo.GetComponent<ArrowProjectile>();
                string label = $"[{_name}] 화살 → 몬스터";
                if (arrow != null)
                    arrow.Launch(monster, _arrowSpeed, _ctrl.EffectYOffset, () => _ctrl.DealDamageAfter(0f, dmg, label));
                else
                    _ctrl.DealDamageAfter(_ctrl.BasicHitDelay, dmg, label);
                _busyTimer = _ctrl.BasicHitDelay;
            }
            else // 근접
            {
                if (_basicAttackAoe)
                {
                    // 광역 기본공격: 대상(최전방 몬스터) 위치를 중심으로 사거리 내 모든 적에게 명중.
                    var target = _ctrl.MonsterTransform;
                    Vector3 center = target != null
                        ? target.position
                        : transform.position + Vector3.right * Mathf.Max(1f, _attackRange * 0.5f) + Vector3.up * _ctrl.EffectYOffset;
                    _ctrl.DealAreaDamageAfter(_ctrl.BasicHitDelay, dmg, $"[{_name}] 광역 → 적", center, _attackRange);
                }
                else
                {
                    _ctrl.DealDamageAfter(_ctrl.BasicHitDelay, dmg, $"[{_name}] → 몬스터");
                }
                _busyTimer = _ctrl.BasicHitDelay;
            }
        }

        // ---- 돌진형 스킬 ----

        private bool TryStartCharge()
        {
            if (_chargeSkill == null || _chargeSkill.timer < _chargeSkill.cooldown) return false;
            if (!_ctrl.MonsterAlive || _ctrl.MonsterTransform == null) return false;

            float dist = _ctrl.MonsterTransform.position.x - transform.position.x;
            if (dist <= 0f || dist > _chargeRange) return false;

            _charging = true;
            _moving = false;
            _chargeSkill.timer = 0f;

            // 이펙트를 본인에게 두른다(자식 부착 → 돌진 중 함께 이동)
            if (_chargeSkill.effect != null)
            {
                var fx = Instantiate(_chargeSkill.effect, transform, false);
                fx.transform.localPosition = new Vector3(0f, _ctrl.EffectYOffset, 0f);
            }
            SendMessage("PlayChargeDash", SendMessageOptions.DontRequireReceiver);
            return true;
        }

        private float ChargeSpeedEffective() => Mathf.Max(_chargeSpeed, _moveSpeed * 1.5f);

        private void UpdateCharge()
        {
            if (!_ctrl.MonsterAlive || _ctrl.MonsterTransform == null)
            {
                EndCharge();
                return;
            }

            float dist = _ctrl.MonsterTransform.position.x - transform.position.x;
            if (dist <= _attackRange)
            {
                long dmg = Damage(_chargeSkill.coef);
                float motion = EffectDuration(_chargeSkill.effect);
                _ctrl.DealDamageAfter(motion, dmg, $"[{_name}] 돌진 {_chargeSkill.name} ×{_chargeSkill.coef:0.##}");
                _busyTimer = motion;
                EndCharge();
                return;
            }

            Vector3 p = transform.position;
            p.x += ChargeSpeedEffective() * Time.deltaTime;
            p.y = _ctrl.PathY;
            transform.position = p;
        }

        private void EndCharge()
        {
            _charging = false;
            SendMessage("StopChargeDash", SendMessageOptions.DontRequireReceiver);
            _ctrl.RequestFighting(); // 돌진 도달 → 파티 교전 진입
        }

        // ---- 공용 계산 ----

        private long Damage(float coef)
        {
            return System.Math.Max(1L, (long)(_atk * Mathf.Max(1f, _ctrl.DevDamageMultiplier) * coef * _atkBuffMult));
        }

        /// <summary>스킬 이펙트를 자기 위치에서 발생시키고 생성된 인스턴스를 반환한다(없으면 null).
        /// <see cref="_selfEffectXOffset"/>만큼 X로 밀어 발생 위치 보정(기사 강타 등).</summary>
        private GameObject SpawnEffectAtSelf(GameObject effect)
        {
            if (effect == null) return null;
            Vector3 pos = transform.position
                + Vector3.up * _ctrl.EffectYOffset
                + Vector3.right * _selfEffectXOffset;
            return Instantiate(effect, pos, Quaternion.identity);
        }

        /// <summary>광역 스킬 이펙트의 판정 반경(월드). 이펙트 렌더러 크기 기반이며, 최소 사거리 이상을 보장해
        /// 전방에 몰린 적 무리에 확실히 닿게 한다.</summary>
        private float EffectRadius(GameObject fx)
        {
            float r = 0f;
            if (fx != null)
            {
                var rends = fx.GetComponentsInChildren<Renderer>();
                if (rends != null && rends.Length > 0)
                {
                    var b = rends[0].bounds;
                    for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                    r = Mathf.Max(b.extents.x, b.extents.y);
                }
            }
            return Mathf.Max(r, _attackRange);
        }

        /// <summary>지정 위치에 스킬 이펙트를 무조건 발생시킨다(몬스터 생존 여부와 무관 — 화살비 등 대상 기준 연출 보장).</summary>
        private void SpawnEffectAt(GameObject effect, Vector3 pos)
        {
            if (effect == null) return;
            Instantiate(effect, pos, Quaternion.identity);
        }

        /// <summary>화살비 낙하 위치. 최전방 몬스터가 있으면 그 위치, 없으면(이미 죽음) 레인저 앞쪽으로 폴백.</summary>
        private Vector3 ArrowRainTargetPos()
        {
            var mt = _ctrl.MonsterTransform;
            if (mt != null) return mt.position + Vector3.up * _ctrl.EffectYOffset;
            // 몬스터 부재 시: 사거리 지점(레인저는 오른쪽=적 방향)으로 폴백
            return transform.position + Vector3.right * Mathf.Max(1f, _attackRange * 0.5f) + Vector3.up * _ctrl.EffectYOffset;
        }

        private float EffectDuration(GameObject effect)
        {
            if (effect == null) return _ctrl.BasicHitDelay;
            var e = effect.GetComponent<SpriteSequenceEffect>();
            if (e == null || e.frames == null || e.frames.Length == 0 || e.fps <= 0f) return _ctrl.BasicHitDelay;
            return e.frames.Length / e.fps;
        }

        // ---- UI용 ----

        public int SkillCount => _skills.Count;
        public int SkillCodeAt(int i) => (i >= 0 && i < _skills.Count) ? _skills[i].code : 0;
        public Sprite SkillIconAt(int i) => (i >= 0 && i < _skills.Count) ? _skills[i].icon : null;

        public bool TryGetSkillCooldown(int skillCode, out float remaining, out float total)
        {
            foreach (var sk in _skills)
                if (sk.code == skillCode)
                {
                    total = sk.cooldown;
                    remaining = Mathf.Max(0f, sk.cooldown - sk.timer);
                    return true;
                }
            remaining = 0f; total = 0f;
            return false;
        }
    }
}
