using GameServer.Data;
using MySqlConnector;
using SqlKata.Execution;
using TaskbarHero.Common.Dto;

namespace GameServer.Repositories;

/// <summary>기존 캐릭터 슬롯 정보(슬롯 배정·직업 중복 검사용).</summary>
public sealed record CharacterSlot(int CharacterId, int ClassCode);

/// <summary>캐릭터 추가 생성 트랜잭션 결과 상태.</summary>
public enum AddCharacterStatus
{
    Ok,
    InsufficientCurrency, // 생성 비용 골드 부족
    DuplicateConflict,    // 슬롯/직업 유니크 경합(동시 생성)
}

/// <summary>캐릭터 추가 생성 트랜잭션 결과. Cost=차감 골드, GoldBalance=차감 후 잔액.</summary>
public sealed record AddCharacterOutcome(AddCharacterStatus Status, long Cost, long GoldBalance)
{
    public static AddCharacterOutcome Fail(AddCharacterStatus status) => new(status, 0, 0);
}

public interface ISaveRepository
{
    Task<PlayerDto?> GetPlayerAsync(long userId);
    Task<List<CharacterDto>> GetCharactersAsync(long userId);
    Task<(List<CurrencyDto> currencies, List<InventoryItemDto> inventory)> GetInventoryAsync(long userId);
    Task<List<SkillDto>> GetSkillsAsync(long userId);
    Task<List<RuneDto>> GetRunesAsync(long userId);
    Task<CubeDto?> GetCubeAsync(long userId);

    Task<List<CharacterSlot>> GetCharacterSlotsAsync(long userId);

    /// <summary>최초 접속: game_player + 1번 슬롯 캐릭터 + 큐브를 한 트랜잭션으로 초기화한다.</summary>
    Task CreatePlayerWithFirstCharacterAsync(long userId, string nickname, int classCode, int inventoryCapacity, long nowUnix);

    /// <summary>기존 계정에 캐릭터 1개 추가. 생성 비용(goldCost)을 골드에서 확인·차감하고 캐릭터를 삽입하는 한 트랜잭션.</summary>
    Task<AddCharacterOutcome> AddCharacterAsync(long userId, int characterId, int classCode, long goldCost);

    /// <summary>last_active_at 갱신. 갱신된 행 수(0이면 계정 없음) 반환.</summary>
    Task<int> UpdateLastActiveAsync(long userId, long nowUnix);
}

// ── DB 행 매핑용 POCO(제네릭 매핑 전용, dynamic 금지) ──
// Dapper.MatchNamesWithUnderscores=true(Program.cs)로 snake_case 컬럼 → PascalCase 프로퍼티 매핑.
file sealed class GamePlayerRow
{
    public string Nickname { get; set; } = string.Empty;
    public int Act { get; set; }
    public int Stage { get; set; }
    public int Difficulty { get; set; }
    public int MaxStageCleared { get; set; }
    public int InventoryCapacity { get; set; }
    public long LastActiveAt { get; set; }
}

file sealed class PlayerCharacterRow
{
    public int CharacterId { get; set; }
    public int ClassCode { get; set; }
    public int Level { get; set; }
    public long Exp { get; set; }
}

file sealed class PlayerItemRow
{
    public long PlayerItemId { get; set; }
    public int RowType { get; set; }
    public int ItemCode { get; set; }
    public long Quantity { get; set; }
    public int? Slot { get; set; }
    public int EnhanceLevel { get; set; }
}

file sealed class PlayerItemEquippedRow
{
    public long PlayerItemId { get; set; }
    public int EquippedCharacterId { get; set; }
    public int EquippedSlot { get; set; }
}

file sealed class PlayerSkillRow
{
    public int CharacterId { get; set; }
    public int SkillCode { get; set; }
    public int Level { get; set; }
    public int Equipped { get; set; }
}

file sealed class PlayerRuneRow
{
    public int RuneCode { get; set; }
    public int Level { get; set; }
}

file sealed class PlayerCubeRow
{
    public int CubeLevel { get; set; }
    public long CubeExp { get; set; }
}

file sealed class ItemIdQtyRow
{
    public long PlayerItemId { get; set; }
    public long Quantity { get; set; }
}

/// <summary>세이브(taskbar_hero_game) 접근 계층. SqlKata 쿼리 빌더 + 제네릭 매핑만 사용한다(dynamic 금지).</summary>
public sealed class SaveRepository : ISaveRepository
{
    private const int RowTypeCurrency = 2;
    private const int GoldItemCode = 1;
    private const int MySqlDuplicateEntry = 1062;

    private readonly GameDbFactory _dbFactory;

    public SaveRepository(GameDbFactory dbFactory) => _dbFactory = dbFactory;

    public async Task<PlayerDto?> GetPlayerAsync(long userId)
    {
        using var db = _dbFactory.Create();
        var row = await db.Query("game_player").Where("user_id", userId).FirstOrDefaultAsync<GamePlayerRow>();
        if (row is null)
        {
            return null;
        }

        return new PlayerDto
        {
            nickname = row.Nickname,
            act = row.Act,
            stage = row.Stage,
            difficulty = row.Difficulty,
            maxStageCleared = row.MaxStageCleared,
            inventoryCapacity = row.InventoryCapacity,
            lastActiveAt = row.LastActiveAt,
        };
    }

    public async Task<List<CharacterDto>> GetCharactersAsync(long userId)
    {
        using var db = _dbFactory.Create();
        var rows = await db.Query("player_character").Where("user_id", userId).OrderBy("character_id")
            .GetAsync<PlayerCharacterRow>();
        return rows.Select(r => new CharacterDto
        {
            characterId = r.CharacterId,
            classCode = r.ClassCode,
            level = r.Level,
            exp = r.Exp,
        }).ToList();
    }

    public async Task<(List<CurrencyDto>, List<InventoryItemDto>)> GetInventoryAsync(long userId)
    {
        using var db = _dbFactory.Create();

        var itemRows = await db.Query("player_item").Where("user_id", userId).GetAsync<PlayerItemRow>();
        var equippedRows = await db.Query("player_item_equipped").Where("user_id", userId).GetAsync<PlayerItemEquippedRow>();

        // player_item_id → (장착 캐릭터, 장착 슬롯)
        var equipped = equippedRows.ToDictionary(e => e.PlayerItemId, e => (charId: e.EquippedCharacterId, slot: e.EquippedSlot));

        var currencies = new List<CurrencyDto>();
        var inventory = new List<InventoryItemDto>();

        foreach (var r in itemRows)
        {
            if (r.RowType == RowTypeCurrency)
            {
                currencies.Add(new CurrencyDto
                {
                    currencyType = r.ItemCode,
                    amount = r.Quantity,
                });
                continue;
            }

            int equippedCharacterId = 0; // 0 = 미장착
            int equippedSlot = 0;
            if (equipped.TryGetValue(r.PlayerItemId, out var eq))
            {
                equippedCharacterId = eq.charId;
                equippedSlot = eq.slot;
            }

            inventory.Add(new InventoryItemDto
            {
                itemId = r.PlayerItemId,
                slot = r.Slot ?? -1, // -1 = 슬롯 없음(장착 중)
                itemCode = r.ItemCode,
                quantity = r.Quantity,
                enhanceLevel = r.EnhanceLevel,
                equippedCharacterId = equippedCharacterId,
                equippedSlot = equippedSlot,
            });
        }

        return (currencies, inventory);
    }

    public async Task<List<SkillDto>> GetSkillsAsync(long userId)
    {
        using var db = _dbFactory.Create();
        var rows = await db.Query("player_skill").Where("user_id", userId).GetAsync<PlayerSkillRow>();
        return rows.Select(r => new SkillDto
        {
            characterId = r.CharacterId,
            skillCode = r.SkillCode,
            level = r.Level,
            equipped = r.Equipped,
        }).ToList();
    }

    public async Task<List<RuneDto>> GetRunesAsync(long userId)
    {
        using var db = _dbFactory.Create();
        var rows = await db.Query("player_rune").Where("user_id", userId).GetAsync<PlayerRuneRow>();
        return rows.Select(r => new RuneDto
        {
            runeCode = r.RuneCode,
            level = r.Level,
        }).ToList();
    }

    public async Task<CubeDto?> GetCubeAsync(long userId)
    {
        using var db = _dbFactory.Create();
        var row = await db.Query("player_cube").Where("user_id", userId).FirstOrDefaultAsync<PlayerCubeRow>();
        if (row is null)
        {
            return null;
        }

        return new CubeDto
        {
            cubeLevel = row.CubeLevel,
            cubeExp = row.CubeExp,
        };
    }

    public async Task<List<CharacterSlot>> GetCharacterSlotsAsync(long userId)
    {
        using var db = _dbFactory.Create();
        var rows = await db.Query("player_character").Select("character_id", "class_code").Where("user_id", userId)
            .GetAsync<PlayerCharacterRow>();
        return rows.Select(r => new CharacterSlot(r.CharacterId, r.ClassCode)).ToList();
    }

    public async Task CreatePlayerWithFirstCharacterAsync(long userId, string nickname, int classCode, int inventoryCapacity, long nowUnix)
    {
        await using var connection = _dbFactory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var db = _dbFactory.Create(connection);

            await db.Query("game_player").InsertAsync(new
            {
                user_id = userId,
                nickname,
                act = 1,
                stage = 1,
                difficulty = 1,
                max_stage_cleared = 0,
                inventory_capacity = inventoryCapacity,
                last_active_at = nowUnix,
                created_at = nowUnix,
                updated_at = nowUnix,
            }, transaction);

            await db.Query("player_character").InsertAsync(new
            {
                user_id = userId,
                character_id = 1,
                class_code = classCode,
                level = 1,
                exp = 0,
            }, transaction);

            await db.Query("player_cube").InsertAsync(new
            {
                user_id = userId,
                cube_level = 1,
                cube_exp = 0,
            }, transaction);

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<AddCharacterOutcome> AddCharacterAsync(long userId, int characterId, int classCode, long goldCost)
    {
        await using var connection = _dbFactory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var db = _dbFactory.Create(connection);

            // 1) 골드 잔액 확인(비용 > 0일 때). 재화 행(row_type=2, item_code=1)이 없으면 잔액 0.
            var goldRow = await db.Query("player_item")
                .Select("player_item_id", "quantity")
                .Where("user_id", userId).Where("row_type", RowTypeCurrency).Where("item_code", GoldItemCode)
                .FirstOrDefaultAsync<ItemIdQtyRow>(transaction);

            long gold = goldRow?.Quantity ?? 0;
            if (gold < goldCost)
            {
                await transaction.RollbackAsync();
                return AddCharacterOutcome.Fail(AddCharacterStatus.InsufficientCurrency);
            }

            // 2) 골드 차감(비용 > 0일 때만 UPDATE).
            long newBalance = gold - goldCost;
            if (goldCost > 0 && goldRow is not null)
            {
                await db.Query("player_item").Where("player_item_id", goldRow.PlayerItemId)
                    .UpdateAsync(new { quantity = newBalance }, transaction);
            }

            // 3) 캐릭터 삽입. 슬롯/직업 유니크 경합(동시 생성)은 여기서 잡아 롤백.
            try
            {
                await db.Query("player_character").InsertAsync(new
                {
                    user_id = userId,
                    character_id = characterId,
                    class_code = classCode,
                    level = 1,
                    exp = 0,
                }, transaction);
            }
            catch (MySqlException ex) when (ex.Number == MySqlDuplicateEntry)
            {
                await transaction.RollbackAsync();
                return AddCharacterOutcome.Fail(AddCharacterStatus.DuplicateConflict);
            }

            await transaction.CommitAsync();
            return new AddCharacterOutcome(AddCharacterStatus.Ok, goldCost, newBalance);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<int> UpdateLastActiveAsync(long userId, long nowUnix)
    {
        using var db = _dbFactory.Create();
        return await db.Query("game_player")
            .Where("user_id", userId)
            .UpdateAsync(new { last_active_at = nowUnix, updated_at = nowUnix });
    }
}
