using System;
using System.Collections.Generic;
using UnityEngine;

namespace TaskbarHero.Client.Battle
{
    /// <summary>스킬 1개의 발동 이펙트/아이콘(스킬 코드로 매핑). 인스펙터에서 직업별로 채운다.</summary>
    [Serializable]
    public class SkillVisual
    {
        public int skillCode;
        [Tooltip("스킬 발동 이펙트(공격=몬스터 위치, 버프=본인, 돌진=본인에 부착)")]
        public GameObject effect;
        [Tooltip("UI 스킬 아이콘 스프라이트")]
        public Sprite icon;
        [Tooltip("발동 이펙트 크기 배율(1=기본). 예: 레인저 정조준·다중 사격 2배")]
        public float effectScale = 1f;
    }

    /// <summary>
    /// 파티 멤버 1인의 전투 설정. <see cref="BattleDevController"/>가 인스펙터 리스트로 보유하며,
    /// 스폰 시 <see cref="PlayerCombatant"/>에 주입한다. 이 리스트만 바꾸면 1~3인 자유 조합이 된다.
    /// </summary>
    [Serializable]
    public class PartyMemberConfig
    {
        [Tooltip("인스펙터 가독용 라벨")]
        public string label = "Member";
        public GameObject prefab;
        [Tooltip("class_master 코드 (기사1 / 레인저2 / 마법사3)")]
        public int classCode = 1;
        [Tooltip("원거리(화살 투사체) 여부")]
        public bool ranged = false;
        [Tooltip("기본 공격 사거리. 대형 행(같은 X)은 이 값으로 자동 결정됨(비슷한 사거리=같은 행)")]
        public float attackRange = 1.5f;
        [Tooltip("공격 애니 이름 필터(예: Bow). 빈 값이면 ATTACK_List index 0")]
        public string attackAnim = "";
        [Tooltip("원거리일 때 화살 투사체 프리팹(ArrowProjectile 포함)")]
        public GameObject arrowPrefab;

        [Header("캐스터 (선택 — 마법사 등)")]
        [Tooltip("스킬 시전 시 공격 애니를 손 든 프레임에서 정지(홀드)했다가 이펙트 종료 후 idle로 복귀")]
        public bool castHold = false;

        [Header("돌진형 스킬 (선택)")]
        [Tooltip("돌진으로 처리할 스킬 코드(0이면 없음). 예: 방패 돌진 101")]
        public int chargeSkillCode = 0;
        [Tooltip("이 거리(x) 이내에 적이 있으면 돌진 발동")]
        public float chargeRange = 4f;
        [Tooltip("돌진 이동 속도(유닛/초). 걷기보다 빠르게 보정됨")]
        public float chargeSpeed = 12f;

        [Header("공중 화살비형 스킬 (선택 — 레인저)")]
        [Tooltip("점프→공중에서 활 하늘로 든 채 정지→대상 위치에 이펙트로 처리할 스킬 코드(0이면 없음). 예: 화살비 203")]
        public int rainSkillCode = 0;

        [Header("광역 스킬 (선택)")]
        [Tooltip("이 근접 스킬은 단일 대상이 아니라 이펙트 범위 내 모든 적에게 데미지(0이면 없음). 예: 기사 강타 103")]
        public int aoeSkillCode = 0;

        [Tooltip("true면 근접 기본공격이 단일 대상이 아니라 공격 범위(사거리) 내 모든 적에게 명중한다. 예: 기사")]
        public bool basicAttackAoe = false;

        [Tooltip("자기 위치 발생 스킬 이펙트의 X 오프셋(+면 오른쪽=적 방향). 예: 기사 강타를 조금 더 오른쪽에")]
        public float selfEffectXOffset = 0f;

        [Header("캐스터 기본공격 투사체 / 전(全)스킬 광역 (선택 — 마법사)")]
        [Tooltip("기본공격을 투사체로 발사할 프리팹(없으면 근접/기존 방식). 예: 마법사 마법 볼트")]
        public GameObject basicAttackProjectile;
        [Tooltip("기본공격 투사체 크기 배율(1=기본)")]
        public float basicAttackProjectileScale = 1f;
        [Tooltip("true면 모든 액티브 공격 스킬이 대상(최전방 몬스터) 위치를 중심으로 범위 내 모든 적에게 데미지. 예: 마법사")]
        public bool allSkillsAoe = false;

        [Header("스킬 이펙트 / 아이콘 (스킬 코드별)")]
        public List<SkillVisual> skills = new List<SkillVisual>();

        /// <summary>스킬 코드에 해당하는 이펙트 프리팹(없으면 null).</summary>
        public GameObject EffectFor(int skillCode)
        {
            if (skills != null)
            {
                foreach (var s in skills)
                {
                    if (s != null && s.skillCode == skillCode) return s.effect;
                }
            }
            return null;
        }

        /// <summary>스킬 코드에 해당하는 발동 이펙트 크기 배율(없으면 1).</summary>
        public float ScaleFor(int skillCode)
        {
            if (skills != null)
            {
                foreach (var s in skills)
                {
                    if (s != null && s.skillCode == skillCode)
                    {
                        return s.effectScale > 0f ? s.effectScale : 1f;
                    }
                }
            }
            return 1f;
        }

        /// <summary>스킬 코드에 해당하는 아이콘(없으면 null).</summary>
        public Sprite IconFor(int skillCode)
        {
            if (skills != null)
            {
                foreach (var s in skills)
                {
                    if (s != null && s.skillCode == skillCode) return s.icon;
                }
            }
            return null;
        }
    }
}
