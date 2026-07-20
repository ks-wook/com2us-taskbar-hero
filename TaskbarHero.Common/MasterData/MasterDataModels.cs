using System;

namespace TaskbarHero.Common.MasterData
{
    /// <summary>
    /// 공통 캐릭터 스탯(base_stats / stat_bonus). 없는 필드는 0.
    /// 서버-클라 공유 계약(마스터 데이터 기획서 7.1). Unity JsonUtility 호환을 위해 public 필드 + [Serializable].
    /// </summary>
    [Serializable]
    public struct Stats
    {
        public long hp;          // 체력
        public long atk;         // 공격
        public long def;         // 방어
        public float moveSpeed;  // 이동속도
        public float critChance; // 치명확률 (0~1)
        public float critDamage; // 치명데미지 배율 (1.5 = 150%)
        public float cooldown;   // 재사용 대기시간(초)
    }

    /// <summary>직업(class_master) 정의. player_character.class_code가 참조한다.</summary>
    [Serializable]
    public class ClassMaster
    {
        public int classCode;
        public string name;
        public int unlockType;   // 0:기본 1:해금 2:유료
        public Stats baseStats;
    }
}
