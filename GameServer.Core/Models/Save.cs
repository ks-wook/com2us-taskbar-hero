namespace GameServer.Models;

// SaveRepository 전용 DB 행 매핑용 POCO(제네릭 매핑 전용, dynamic 금지).
// Dapper.MatchNamesWithUnderscores=true(Program.cs)로 snake_case 컬럼 → PascalCase 프로퍼티 매핑.

class GamePlayerRow
{
    public string Nickname { get; set; } = string.Empty;
    public int Act { get; set; }
    public int Stage { get; set; }
    public int Difficulty { get; set; }
    public int MaxStageCleared { get; set; }
    public int InventoryCapacity { get; set; }
    public long LastActiveAt { get; set; }
}

class PlayerCharacterRow
{
    public int CharacterId { get; set; }
    public int ClassCode { get; set; }
    public int Slot { get; set; }
    public int Gender { get; set; }
    public int Level { get; set; }
    public long Exp { get; set; }
}

class CurrencyRow
{
    public int ItemCode { get; set; }
    public long Quantity { get; set; }
}

class PlayerItemEquippedRow
{
    public long PlayerItemId { get; set; }
    public int ItemCode { get; set; }
    public int EnhanceLevel { get; set; }
    public int EquippedCharacterId { get; set; }
    public int EquippedSlot { get; set; }
}

class PlayerSkillRow
{
    public int CharacterId { get; set; }
    public int SkillCode { get; set; }
    public int Level { get; set; }
    public int Equipped { get; set; }
}

class PlayerRuneRow
{
    public int RuneCode { get; set; }
    public int Level { get; set; }
}

class PlayerCubeRow
{
    public int CubeLevel { get; set; }
    public long CubeExp { get; set; }
}
