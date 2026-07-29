using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TaskbarHero.Client.Managers;
using TaskbarHero.Client.MasterData;
using TaskbarHero.Common.Dto;
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
            public float scale;    // 발동 이펙트 크기 배율(1=기본)
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
        private GameObject _basicAttackProjectile; // 기본공격 투사체 프리팹(마법사 등, 없으면 근접/기존 방식)
        private float _basicAttackProjectileScale = 1f;
        private bool _allSkillsAoe;    // true면 모든 액티브 공격 스킬이 대상 중심 범위 데미지(마법사)
        private bool _charging;
        private Skill _chargeSkill;
        public bool IsCharging => _charging;

        /// <summary>돌진 자세를 최소 이만큼은 유지한다(초). 적이 이미 사거리 안이라 돌진 이동 거리가 0인
        /// **제자리 발동**에서는, 이 하한이 없으면 자세 진입(애니메이터 트랜지션)이 끝나기도 전에
        /// 종료 처리돼 이펙트만 나오고 애니메이션이 보이지 않는다.</summary>
        private const float MinChargeMotion = 0.45f;

        private bool _chargeImpacted;  // 이번 돌진의 타격을 이미 예약했는지(중복 데미지 방지)
        private float _chargeElapsed;  // 돌진 시작 후 경과 시간
        private float _chargeMotion;   // 이번 돌진의 자세 유지 시간(이펙트 길이와 최소 시간 중 큰 값)

        private string _name = "Ally";
        private long _atk;
        private long _baseAtk;        // 장비 제외 기본 공격(클래스+레벨)
        private long _def;            // 최종 방어력(클래스+레벨+장비 × 패시브 × 룬). 피격 데미지 경감에 사용
        private long _baseDef;        // 장비 제외 기본 방어력(클래스+레벨)
        private long _baseMaxHp;      // 장비 제외 기본 체력
        private int _characterId;     // 연결된 계정 캐릭터 id(serverMode, 0=없음)
        private long _maxHp = 1;
        private long _hp = 1;
        private bool _dead;
        private float _cooldown;
        private float _baseCooldown = 1.2f; // 룬(재사용 단축) 적용 전 기준 쿨다운

        [Tooltip("사망 애니 후 오브젝트가 사라지기까지 지연(초)")]
        private const float DeathLinger = 1.0f;
        private float _moveSpeed;
        private float _baseMoveSpeed = 3f;  // 패시브 제외 기본 이동속도(클래스)

        /// <summary>아군 현재 체력.</summary>
        public long Hp => _hp;
        /// <summary>아군 최대 체력.</summary>
        public long MaxHp => _maxHp;
        /// <summary>생존 여부(사망 시 false).</summary>
        public bool Alive => !_dead;
        /// <summary>현재 공격력(클래스+레벨+장비 합산).</summary>
        public long Attack => _atk;
        /// <summary>현재 방어력(클래스+레벨+장비 × 패시브 × 룬). 피격 데미지 경감에 사용.</summary>
        public long Defense => _def;
        /// <summary>연결된 계정 캐릭터 id(serverMode).</summary>
        public int CharacterId => _characterId;

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
        /// <summary>현재 걷기(이동) 애니메이션이 재생 중인지. 걷기 먼지 이펙트 노출 판정에 사용.</summary>
        public bool IsMoving => _moving && !_dead;

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
            _basicAttackProjectile = cfg.basicAttackProjectile;
            _basicAttackProjectileScale = cfg.basicAttackProjectileScale;
            _allSkillsAoe = cfg.allSkillsAoe;
            _anim = GetComponentInChildren<Animator>();

            LoadStats();
            BuildSkills();
        }

        private void LoadStats()
        {
            var db = MasterDataManager.Db;
            long atk, hp, def;
            if (db != null && db.Classes.TryGetValue(_classCode, out ClassMaster cls))
            {
                _name = cls.name;
                atk = cls.baseStats.atk;
                hp = System.Math.Max(1L, cls.baseStats.hp);
                def = cls.baseStats.def;
                _cooldown = cls.baseStats.cooldown > 0f ? cls.baseStats.cooldown : 1.2f;
                _moveSpeed = cls.baseStats.moveSpeed > 0f ? cls.baseStats.moveSpeed : 3f;
            }
            else
            {
                _name = "Ally(?" + _classCode + ")";
                atk = 10; hp = 100; def = 0; _cooldown = 1.2f; _moveSpeed = 3f;
            }

            // 서버 구동 모드: 계정 캐릭터의 레벨 보너스를 기본 스탯에 반영하고 characterId를 기록(장비 합산용).
            _characterId = 0;
            if (_ctrl != null && _ctrl.serverMode)
            {
                var ch = FindAccountCharacter(_classCode);
                if (ch != null)
                {
                    _characterId = ch.characterId;
                    if (db != null && db.Levels.TryGetValue(ch.level, out LevelMaster lm))
                    {
                        atk += lm.statBonus.atk;
                        hp += lm.statBonus.hp;
                        def += lm.statBonus.def;
                    }
                }
            }

            _baseAtk = atk;
            _baseDef = def;
            _baseMaxHp = System.Math.Max(1L, hp);
            _baseMoveSpeed = _moveSpeed; // 패시브 적용 전 기준 이동속도
            _baseCooldown = _cooldown;   // 룬 적용 전 기준 쿨다운
            ApplyEquipStats(); // 장비 스탯 + 패시브 배율 합산 → _atk/_maxHp/_moveSpeed 확정
            _hp = _maxHp;
            gameObject.name = "Player_" + _name;
        }

        /// <summary>기본 스탯(_baseAtk/_baseMaxHp/_baseMoveSpeed)에 장착 장비 합산 + 학습한 패시브 스킬 배율을
        /// 적용해 _atk/_maxHp/_moveSpeed를 확정한다. 패시브는 장착과 무관하게 습득(레벨 ≥ 1) 시 상시 적용된다.</summary>
        private void ApplyEquipStats()
        {
            long atk = _baseAtk;
            long hp = _baseMaxHp;
            long def = _baseDef;
            var db = MasterDataManager.Db;
            // 장착 장비는 코어 로드(equipped)에만 있다 — 가방 아이템(페이징 조회)과 겹치지 않으므로
            // 가방을 아직 받지 않은 상태(접속 직후)에도 전투 스탯을 온전히 계산할 수 있다.
            var equipped = Session.Equipped;
            if (_characterId != 0 && equipped != null && db != null)
            {
                foreach (var it in equipped)
                {
                    if (it != null && it.equippedCharacterId == _characterId
                        && db.Items.TryGetValue(it.itemCode, out ItemMaster im))
                    {
                        atk += im.baseStats.atk;
                        hp += im.baseStats.hp;
                        def += im.baseStats.def;
                    }
                }
            }

            // 패시브 스킬 배율 + 룬(계정 공용) 배율(statType: 1 공격력 · 2 방어력 · 3 체력 · 6 이동속도 · 7 쿨다운)을 곱한다.
            // 반올림으로 확정한다(버림 시 작은 % 상승분이 사라지는 문제 방지 — 인벤토리 능력치 패널과 동일 규칙).
            _atk = System.Math.Max(1L, (long)System.Math.Round(atk * (double)PassiveMult(1) * RuneMult(1)));
            _def = System.Math.Max(0L, (long)System.Math.Round(def * (double)PassiveMult(2) * RuneMult(2)));
            _maxHp = System.Math.Max(1L, (long)System.Math.Round(hp * (double)PassiveMult(3) * RuneMult(3)));
            _moveSpeed = _baseMoveSpeed * PassiveMult(6) * RuneMult(6);
            _cooldown = Mathf.Max(0.1f, _baseCooldown * RuneMult(7)); // 룬 재사용 단축(감소 방향)
        }

        /// <summary>계정 공용 룬(레벨 ≥ 1) 중 대상 statType을 올리는 것들의 배율 곱. 룬 stat_value는 레벨당 누적 비율이며
        /// 총 보너스 = stat_value × 레벨. statType 7(재사용 대기시간)은 감소(1 − 보너스), 그 외는 증가(1 + 보너스). 없으면 1.</summary>
        private static float RuneMult(int statType)
        {
            float mult = 1f;
            var db = MasterDataManager.Db;
            var runes = Session.GameData != null ? Session.GameData.runes : null;
            if (db == null || runes == null)
            {
                return mult;
            }
            foreach (var pr in runes)
            {
                if (pr == null || pr.level < 1)
                {
                    continue;
                }
                if (db.Runes.TryGetValue(pr.runeCode, out RuneMaster rm) && rm.statType == statType)
                {
                    float bonus = rm.statValue * pr.level;
                    mult *= statType == 7 ? Mathf.Max(0.05f, 1f - bonus) : (1f + bonus);
                }
            }
            return mult;
        }

        /// <summary>이 캐릭터가 습득(레벨 ≥ 1)한 패시브 스킬 중 대상 statType을 올리는 것들의 레벨별 배율 곱.
        /// 없으면 1(변화 없음). serverMode 계정 캐릭터에만 적용된다(세션 player_skill 기준).</summary>
        private float PassiveMult(int statType)
        {
            float mult = 1f;
            if (_characterId == 0)
            {
                return mult;
            }
            var db = MasterDataManager.Db;
            var skills = Session.GameData != null ? Session.GameData.skills : null;
            if (db == null || skills == null)
            {
                return mult;
            }
            foreach (var ps in skills)
            {
                if (ps == null || ps.characterId != _characterId || ps.level < 1)
                {
                    continue;
                }
                if (db.Skills.TryGetValue(ps.skillCode, out SkillMaster sm)
                    && sm.skillType == 2 && sm.statType == statType && sm.coefs != null)
                {
                    int lv = Mathf.Clamp(ps.level, 1, Mathf.Max(1, sm.maxLevel));
                    foreach (var c in sm.coefs)
                    {
                        if (c.skillLevel == lv)
                        {
                            mult *= c.coef;
                            break;
                        }
                    }
                }
            }
            return mult;
        }

        /// <summary>장비 변경 등으로 스탯을 재계산한다(현재 체력 비율 유지). 전투 중 즉시 반영.</summary>
        public void RefreshStats()
        {
            float ratio = _maxHp > 0 ? (float)_hp / _maxHp : 1f;
            ApplyEquipStats();
            _hp = System.Math.Max(1L, (long)(_maxHp * ratio));
        }

        /// <summary>클래스 코드에 해당하는 계정 캐릭터(없으면 null).</summary>
        private static CharacterDto FindAccountCharacter(int classCode)
        {
            var chars = Session.GameData != null ? Session.GameData.characters : null;
            if (chars != null)
            {
                foreach (var c in chars)
                {
                    if (c != null && c.classCode == classCode)
                    {
                        return c;
                    }
                }
            }
            return null;
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

            // serverMode + 연결된 계정 캐릭터: **장착(equipped=1)한 액티브 스킬만** 실제 습득 레벨로 사용한다
            // (캐릭터당 최대 2개, growth 기획서 5.3). 그 외(개발 하네스)에서는 모든 액티브를 DevSkillLevel로 사용.
            bool useEquipped = _ctrl != null && _ctrl.serverMode && _characterId != 0;

            float[] initReadyIn = { 2f, 4f, 6f, 8f };
            int devLevel = Mathf.Max(1, _ctrl != null ? _ctrl.DevSkillLevel : 1);
            int added = 0;
            for (int i = 0; i < actives.Count; i++)
            {
                var s = actives[i];
                int lv;
                if (useEquipped)
                {
                    if (!TryGetEquippedSkillLevel(s.skillCode, out int plv)) continue; // 미장착 → 전투에서 사용 안 함
                    lv = Mathf.Clamp(plv, 1, Mathf.Max(1, s.maxLevel));
                }
                else
                {
                    lv = Mathf.Clamp(devLevel, 1, Mathf.Max(1, s.maxLevel));
                }

                float coef = 0f, dur = 0f; int ct = 1;
                if (s.coefs != null)
                    foreach (var c in s.coefs)
                        if (c.skillLevel == lv) { coef = c.coef; dur = c.duration; ct = c.coefType; break; }

                float cd = s.cooldown > 0f ? s.cooldown : (_ctrl != null ? _ctrl.SkillCooldownFallback : 10f);
                float readyIn = added < initReadyIn.Length ? initReadyIn[added] : 2f;
                var sk = new Skill
                {
                    code = s.skillCode, name = s.name, coefType = ct, coef = coef, duration = dur,
                    cooldown = cd, timer = Mathf.Max(0f, cd - readyIn),
                    effect = _cfg != null ? _cfg.EffectFor(s.skillCode) : null,
                    icon = _cfg != null ? _cfg.IconFor(s.skillCode) : null,
                    scale = _cfg != null ? _cfg.ScaleFor(s.skillCode) : 1f,
                };
                _skills.Add(sk);
                if (sk.code == _chargeSkillCode) _chargeSkill = sk;
                added++;
            }
        }

        /// <summary>serverMode: 이 캐릭터가 장착(equipped=1)한 스킬이면 습득 레벨을 돌려준다(미장착이면 false).</summary>
        private bool TryGetEquippedSkillLevel(int skillCode, out int level)
        {
            level = 0;
            var skills = Session.GameData != null ? Session.GameData.skills : null;
            if (skills == null) return false;
            foreach (var s in skills)
                if (s != null && s.characterId == _characterId && s.skillCode == skillCode && s.equipped == 1)
                {
                    level = s.level;
                    return true;
                }
            return false;
        }

        /// <summary>장착/레벨 변경 후 전투에서 사용할 스킬 세트를 세션 기준으로 다시 구성한다(장비 스탯도 함께 갱신).</summary>
        public void RebuildSkills()
        {
            BuildSkills();
        }

        /// <summary>적에게 데미지를 받는다. 체력이 0 이하가 되면 사망 처리한다(1회).</summary>
        public void TakeDamage(long dmg)
        {
            if (_dead) return;
            _hp -= dmg;
            if (_hp <= 0)
            {
                _hp = 0;
                Die();
            }
        }

        /// <summary>체력 0: 사망 처리 — 사망 애니 재생, 컨트롤러에 통지, 잠시 뒤 자기 소멸.</summary>
        private void Die()
        {
            if (_dead) return;
            _dead = true;
            _charging = false;
            SendMessage("PlayDeathOnce", SendMessageOptions.DontRequireReceiver);
            if (_ctrl != null) _ctrl.OnAllyKilled(this);
            StartCoroutine(DespawnAfter());
        }

        /// <summary>사망 애니가 보이도록 잠깐 대기 후 오브젝트를 파괴한다.</summary>
        private IEnumerator DespawnAfter()
        {
            yield return new WaitForSeconds(DeathLinger);
            Destroy(gameObject);
        }

        private void Update()
        {
            if (_dead) return;
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

            // 전투: 교전 중이면 스킬/기본공격.
            // 마법사(자기중심 광역 캐스터, _allSkillsAoe)는 몬스터 생존/사거리와 무관하게 준비된 스킬을 시전해
            // **이미 몬스터가 죽었어도 스킬 이펙트가 무조건 나가도록** 한다. 기본공격만 대상 사거리를 요구한다.
            if (_ctrl.IsFighting)
            {
                bool monsterReady = _ctrl.MonsterAlive && _ctrl.MonsterTransform != null;
                if (monsterReady) _attackTimer += Time.deltaTime; // 기본공격 간격 누적(기존 동작 유지)
                float dist = monsterReady ? _ctrl.MonsterTransform.position.x - transform.position.x : float.MaxValue;
                bool monsterInRange = monsterReady && dist > 0f && dist <= _attackRange;

                if (_busyTimer <= 0f && !attacking)
                {
                    if (_allSkillsAoe)
                    {
                        // 마법사: 준비된 스킬은 대상 유무·거리와 무관하게 자기 기준으로 시전(이펙트 보장).
                        if (!TryCastSkill() && monsterInRange && _attackTimer >= Mathf.Max(0.05f, _cooldown))
                        {
                            _attackTimer = 0f;
                            BasicAttack();
                        }
                    }
                    else if (monsterInRange)
                    {
                        // 근접/원거리: 대상이 사거리 안일 때만 스킬/기본공격.
                        if (!TryCastSkill() && _attackTimer >= Mathf.Max(0.05f, _cooldown))
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
                    SpawnEffectAt(sk.effect, ArrowRainTargetPos(), sk.scale);
                    _ctrl.DealDamageAfter(motion, dmg, label);
                    _busyTimer = motion + 0.4f; // 점프+홀드+착지 동안 대기
                }
                else if (_allSkillsAoe && sk.effect != null)
                {
                    // 캐스터 전(全)스킬 광역(마법사): 이펙트를 **자기 위치 기준**으로 띄우고(대상 위치가 아님)
                    // 이펙트 크기(EffectRadius) 내 모든 적에게 데미지. 몬스터가 이미 죽었어도 이펙트는 무조건 나간다.
                    if (_castHold)
                        SendMessage("PlayCastHold", motion, SendMessageOptions.DontRequireReceiver);
                    else
                        PlayAttackAnim();
                    Vector3 center = transform.position
                        + Vector3.up * _ctrl.EffectYOffset
                        + Vector3.right * _selfEffectXOffset;
                    var fx = SpawnEffectAt(sk.effect, center, sk.scale);
                    _ctrl.DealAreaDamageAfter(motion, dmg, label, center, EffectRadius(fx));
                    _busyTimer = motion;
                }
                else if (_ranged && sk.effect != null && _ctrl.MonsterTransform != null)
                {
                    // 원거리(레인저): 스킬 이펙트를 투사체로 발사(자기 위치 → 대상). 도달 시 데미지.
                    PlayAttackAnim();
                    Vector3 origin = transform.position + Vector3.up * _ctrl.EffectYOffset;
                    var fx = Instantiate(sk.effect, origin, Quaternion.identity);
                    if (sk.scale > 0f && sk.scale != 1f) fx.transform.localScale *= sk.scale; // 이펙트 크기 배율(정조준·다중 사격 2배 등)
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
                    if (fx != null && sk.scale > 0f && sk.scale != 1f) fx.transform.localScale *= sk.scale;
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

            if (_basicAttackProjectile != null && _ctrl.MonsterTransform != null)
            {
                // 캐스터(마법사 등): 기본공격을 투사체로 발사(자기 위치 → 대상). 도달 시 데미지.
                Vector3 origin = transform.position + Vector3.up * _ctrl.EffectYOffset;
                var fx = Instantiate(_basicAttackProjectile, origin, Quaternion.identity);
                if (_basicAttackProjectileScale > 0f && _basicAttackProjectileScale != 1f)
                {
                    fx.transform.localScale *= _basicAttackProjectileScale;
                }
                var proj = fx.GetComponent<ProjectileEffect>();
                if (proj == null) proj = fx.AddComponent<ProjectileEffect>();
                string label = $"[{_name}] → 몬스터";
                proj.Launch(_ctrl.MonsterTransform, _arrowSpeed, _ctrl.EffectYOffset,
                            () => _ctrl.DealDamageAfter(0f, dmg, label));
                _busyTimer = _ctrl.BasicHitDelay;
            }
            else if (_ranged && _arrowPrefab != null)
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
            _chargeImpacted = false;
            _chargeElapsed = 0f;
            // 자세 유지 시간은 이펙트 길이에 맞추되, 애니메이터 트랜지션이 보이도록 하한을 둔다.
            _chargeMotion = Mathf.Max(MinChargeMotion, EffectDuration(_chargeSkill.effect));

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

        /// <summary>돌진 진행: 사거리에 닿을 때까지 전진하고, 닿으면 타격을 예약한다.
        /// 타격 뒤에도 <see cref="_chargeMotion"/>이 끝날 때까지 돌진 자세를 유지해,
        /// 적이 이미 코앞이라 이동 거리가 0인 **제자리 발동**에서도 돌진 애니메이션이 보이게 한다.</summary>
        private void UpdateCharge()
        {
            _chargeElapsed += Time.deltaTime;

            if (!_chargeImpacted)
            {
                // 타격 전에 대상이 사라졌으면 유지할 이유가 없으므로 즉시 종료.
                if (!_ctrl.MonsterAlive || _ctrl.MonsterTransform == null)
                {
                    EndCharge();
                    return;
                }

                float dist = _ctrl.MonsterTransform.position.x - transform.position.x;
                if (dist > _attackRange)
                {
                    Vector3 p = transform.position;
                    p.x += ChargeSpeedEffective() * Time.deltaTime;
                    p.y = _ctrl.PathY;
                    transform.position = p;
                    return;
                }

                // 도달(또는 발동 시점부터 사거리 안). 데미지는 자세가 끝나는 순간에 들어간다.
                _chargeImpacted = true;
                long dmg = Damage(_chargeSkill.coef);
                _ctrl.DealDamageAfter(Mathf.Max(0f, _chargeMotion - _chargeElapsed), dmg,
                    $"[{_name}] 돌진 {_chargeSkill.name} ×{_chargeSkill.coef:0.##}");
            }

            // 타격 후 남은 자세 유지(이 동안 _charging이 다른 행동을 막으므로 별도 _busyTimer는 불필요).
            if (_chargeElapsed >= _chargeMotion)
            {
                EndCharge();
            }
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

        /// <summary>광역 스킬 이펙트의 판정 반경(월드). **이펙트의 실제 렌더 크기에 맞춘다** — 이전에는
        /// 사거리(_attackRange)를 최소값으로 강제해 이펙트보다 넓은 범위의 적이 피격되던 문제가 있었다.
        /// 렌더러가 없어 크기를 구하지 못한 경우에만 사거리로 폴백한다.</summary>
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
            // 이펙트 크기를 그대로 판정 반경으로 사용(과도한 광역 피격 방지). 크기 미상일 때만 사거리 폴백.
            return r > 0f ? r : _attackRange;
        }

        /// <summary>지정 위치에 스킬 이펙트를 무조건 발생시키고 인스턴스를 반환한다(몬스터 생존 여부와 무관 —
        /// 화살비·마법사 광역 등 대상 기준 연출 보장). scale로 이펙트 크기 배율을 적용한다.</summary>
        private GameObject SpawnEffectAt(GameObject effect, Vector3 pos, float scale = 1f)
        {
            if (effect == null) return null;
            var fx = Instantiate(effect, pos, Quaternion.identity);
            if (scale > 0f && scale != 1f) fx.transform.localScale *= scale;
            return fx;
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

        /// <summary>슬롯 hover 툴팁용: 스킬 코드의 유효(장착 레벨 반영) 정보를 돌려준다.</summary>
        public bool TryGetSkillInfo(int skillCode, out string name, out int coefType, out float coef, out float duration, out float cooldown)
        {
            foreach (var sk in _skills)
                if (sk.code == skillCode)
                {
                    name = sk.name;
                    coefType = sk.coefType;
                    coef = sk.coef;
                    duration = sk.duration;
                    cooldown = sk.cooldown;
                    return true;
                }
            name = null; coefType = 0; coef = 0f; duration = 0f; cooldown = 0f;
            return false;
        }
    }
}
