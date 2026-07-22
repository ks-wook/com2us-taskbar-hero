using GameServer.Data;
using SqlKata.Execution;
using TaskbarHero.Common.Dto;

namespace GameServer.Repositories;

/// <summary>기존 캐릭터 슬롯 정보(슬롯 배정·직업 중복 검사용).</summary>
public sealed record CharacterSlot(int CharacterId, int ClassCode);

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

    /// <summary>기존 계정에 캐릭터 1개 추가.</summary>
    Task AddCharacterAsync(long userId, int characterId, int classCode);

    /// <summary>last_active_at 갱신. 갱신된 행 수(0이면 계정 없음) 반환.</summary>
    Task<int> UpdateLastActiveAsync(long userId, long nowUnix);
}

/// <summary>세이브(taskbar_hero_game) 접근 계층. SqlKata 쿼리 빌더만 사용한다.</summary>
public sealed class SaveRepository : ISaveRepository
{
    private const int RowTypeItem = 1;
    private const int RowTypeCurrency = 2;

    private readonly GameDbFactory _dbFactory;

    public SaveRepository(GameDbFactory dbFactory) => _dbFactory = dbFactory;

    public async Task<PlayerDto?> GetPlayerAsync(long userId)
    {
        using var db = _dbFactory.Create();
        var row = await db.Query("game_player").Where("user_id", userId).FirstOrDefaultAsync();
        if (row is null)
        {
            return null;
        }

        return new PlayerDto
        {
            nickname = (string)row.nickname,
            act = Convert.ToInt32(row.act),
            stage = Convert.ToInt32(row.stage),
            difficulty = Convert.ToInt32(row.difficulty),
            maxStageCleared = Convert.ToInt32(row.max_stage_cleared),
            inventoryCapacity = Convert.ToInt32(row.inventory_capacity),
            lastActiveAt = Convert.ToInt64(row.last_active_at),
        };
    }

    public async Task<List<CharacterDto>> GetCharactersAsync(long userId)
    {
        using var db = _dbFactory.Create();
        var rows = await db.Query("player_character").Where("user_id", userId).OrderBy("character_id").GetAsync();
        return rows.Select(r => new CharacterDto
        {
            characterId = Convert.ToInt32(r.character_id),
            classCode = Convert.ToInt32(r.class_code),
            level = Convert.ToInt32(r.level),
            exp = Convert.ToInt64(r.exp),
        }).ToList();
    }

    public async Task<(List<CurrencyDto>, List<InventoryItemDto>)> GetInventoryAsync(long userId)
    {
        using var db = _dbFactory.Create();

        var itemRows = await db.Query("player_item").Where("user_id", userId).GetAsync();
        var equippedRows = await db.Query("player_item_equipped").Where("user_id", userId).GetAsync();

        // player_item_id → (장착 캐릭터, 장착 슬롯)
        var equipped = new Dictionary<long, (int charId, int slot)>();
        foreach (var e in equippedRows)
        {
            // dynamic 값은 typed 지역변수로 받아 튜플이 (int,int)로 확정되게 한다
            // (Convert.ToInt32(dynamic)를 튜플에 바로 넣으면 (object,object)로 추론돼 런타임 변환 실패).
            long equippedItemId = Convert.ToInt64(e.player_item_id);
            int charId = Convert.ToInt32(e.equipped_character_id);
            int slot = Convert.ToInt32(e.equipped_slot);
            equipped[equippedItemId] = (charId, slot);
        }

        var currencies = new List<CurrencyDto>();
        var inventory = new List<InventoryItemDto>();

        foreach (var r in itemRows)
        {
            var rowType = Convert.ToInt32(r.row_type);
            if (rowType == RowTypeCurrency)
            {
                currencies.Add(new CurrencyDto
                {
                    currencyType = Convert.ToInt32(r.item_code),
                    amount = Convert.ToInt64(r.quantity),
                });
                continue;
            }

            long itemId = Convert.ToInt64(r.player_item_id);
            int equippedCharacterId = 0; // 0 = 미장착
            int equippedSlot = 0;
            if (equipped.TryGetValue(itemId, out var eq))
            {
                equippedCharacterId = eq.charId;
                equippedSlot = eq.slot;
            }

            inventory.Add(new InventoryItemDto
            {
                itemId = itemId,
                slot = r.slot is null ? -1 : Convert.ToInt32(r.slot), // -1 = 슬롯 없음(장착 중)
                itemCode = Convert.ToInt32(r.item_code),
                quantity = Convert.ToInt64(r.quantity),
                enhanceLevel = Convert.ToInt32(r.enhance_level),
                equippedCharacterId = equippedCharacterId,
                equippedSlot = equippedSlot,
            });
        }

        return (currencies, inventory);
    }

    public async Task<List<SkillDto>> GetSkillsAsync(long userId)
    {
        using var db = _dbFactory.Create();
        var rows = await db.Query("player_skill").Where("user_id", userId).GetAsync();
        return rows.Select(r => new SkillDto
        {
            characterId = Convert.ToInt32(r.character_id),
            skillCode = Convert.ToInt32(r.skill_code),
            level = Convert.ToInt32(r.level),
            equipped = Convert.ToInt32(r.equipped),
        }).ToList();
    }

    public async Task<List<RuneDto>> GetRunesAsync(long userId)
    {
        using var db = _dbFactory.Create();
        var rows = await db.Query("player_rune").Where("user_id", userId).GetAsync();
        return rows.Select(r => new RuneDto
        {
            runeCode = Convert.ToInt32(r.rune_code),
            level = Convert.ToInt32(r.level),
        }).ToList();
    }

    public async Task<CubeDto?> GetCubeAsync(long userId)
    {
        using var db = _dbFactory.Create();
        var row = await db.Query("player_cube").Where("user_id", userId).FirstOrDefaultAsync();
        if (row is null)
        {
            return null;
        }

        return new CubeDto
        {
            cubeLevel = Convert.ToInt32(row.cube_level),
            cubeExp = Convert.ToInt64(row.cube_exp),
        };
    }

    public async Task<List<CharacterSlot>> GetCharacterSlotsAsync(long userId)
    {
        using var db = _dbFactory.Create();
        var rows = await db.Query("player_character").Select("character_id", "class_code").Where("user_id", userId).GetAsync();
        return rows.Select(r => new CharacterSlot(Convert.ToInt32(r.character_id), Convert.ToInt32(r.class_code))).ToList();
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

    public async Task AddCharacterAsync(long userId, int characterId, int classCode)
    {
        using var db = _dbFactory.Create();
        await db.Query("player_character").InsertAsync(new
        {
            user_id = userId,
            character_id = characterId,
            class_code = classCode,
            level = 1,
            exp = 0,
        });
    }

    public async Task<int> UpdateLastActiveAsync(long userId, long nowUnix)
    {
        using var db = _dbFactory.Create();
        return await db.Query("game_player")
            .Where("user_id", userId)
            .UpdateAsync(new { last_active_at = nowUnix, updated_at = nowUnix });
    }
}
