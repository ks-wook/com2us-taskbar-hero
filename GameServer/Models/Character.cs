namespace GameServer.Models;

// 직업·레벨·캐릭터 생성 비용 마스터의 행 매핑 POCO.

// ── 마스터 적재용 DB 행 매핑 POCO(제네릭 매핑 전용, dynamic 금지). snake_case→PascalCase는 Dapper 규칙으로 매핑.
//    DECIMAL 컬럼은 decimal로 받아 float/double로 캐스팅한다. 적재는 Repositories/MasterDb/MasterDbLoader. ──
class ClassMasterRow
{
    public int ClassCode { get; set; }
    public string Name { get; set; } = string.Empty;
    public int UnlockType { get; set; }
    public long Hp { get; set; }
    public long Atk { get; set; }
    public long Def { get; set; }
    public decimal MoveSpeed { get; set; }
    public decimal CritChance { get; set; }
    public decimal CritDamage { get; set; }
    public decimal Cooldown { get; set; }
}
class LevelMasterRow
{
    public int Level { get; set; }
    public long RequiredExp { get; set; }
    public int SkillPoints { get; set; }
}
class CharacterCreateCostRow
{
    public int CharacterId { get; set; }
    public long GoldCost { get; set; }
}
