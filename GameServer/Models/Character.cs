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

/// <summary>
/// 이번 지급으로 <b>레벨이 오른</b> 캐릭터 1건. 스테이지 클리어·오프라인 정산이 공통으로 만들어
/// <c>character.levelup</c> 이벤트 로그로 방출한다(로그 이벤트 정의 5.3) — 성장 곡선 실측의 원본이다.
/// </summary>
/// <param name="CharacterId">레벨이 오른 캐릭터.</param>
/// <param name="ClassCode">그 캐릭터의 직업(직업별 성장 속도를 가르는 축).</param>
/// <param name="FromLevel">지급 전 레벨.</param>
/// <param name="ToLevel">지급 후 레벨. 한 번에 여러 레벨이 오를 수 있어 From과 1 이상 차이날 수 있다.</param>
public sealed record CharacterLevelUp(int CharacterId, int ClassCode, int FromLevel, int ToLevel);
