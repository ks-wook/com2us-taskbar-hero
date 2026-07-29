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
    /// <summary>game_player 1행을 세이브 응답용 DTO로 조회한다(계정 세이브 없으면 null).</summary>
    Task<PlayerDto?> GetPlayerAsync(long userId);

    /// <summary>계정의 캐릭터 목록(슬롯 순)을 조회한다.</summary>
    Task<List<CharacterDto>> GetCharactersAsync(long userId);

    /// <summary>코어 로드용 재화 목록(player_item의 row_type=2 행)을 조회한다.</summary>
    Task<List<CurrencyDto>> GetCurrenciesAsync(long userId);

    /// <summary>코어 로드용 장착 장비 목록(player_item_equipped 전 행, 최대 18개)을 조회한다.</summary>
    Task<List<EquippedItemDto>> GetEquippedAsync(long userId);

    /// <summary>가방 아이템(row_type=1, 배치된 행) 총 개수를 센다. 페이징 진행률 표시용.</summary>
    Task<int> GetBagItemCountAsync(long userId);

    /// <summary>계정의 전 캐릭터 보유 스킬(레벨·장착 여부)을 조회한다.</summary>
    Task<List<SkillDto>> GetSkillsAsync(long userId);

    /// <summary>계정 공용 룬 목록(코드·레벨)을 조회한다.</summary>
    Task<List<RuneDto>> GetRunesAsync(long userId);

    /// <summary>계정의 큐브 상태(레벨·경험치)를 조회한다(행 없으면 null).</summary>
    Task<CubeDto?> GetCubeAsync(long userId);

    /// <summary>캐릭터 추가 생성 시 슬롯 배정·직업 중복 검사에 쓸 기존 슬롯 목록(슬롯 번호 + 직업)을 조회한다.</summary>
    Task<List<CharacterSlot>> GetCharacterSlotsAsync(long userId);

    /// <summary>최초 접속: game_player + 1번 슬롯 캐릭터(직업·성별) + 큐브를 한 트랜잭션으로 초기화한다.</summary>
    Task CreatePlayerWithFirstCharacterAsync(long userId, string nickname, int classCode, int gender, int inventoryCapacity, long nowUnix);

    /// <summary>기존 계정에 캐릭터 1개 추가. 생성 비용(goldCost)을 골드에서 확인·차감하고 캐릭터를 삽입하는 한 트랜잭션.</summary>
    Task<AddCharacterOutcome> AddCharacterAsync(long userId, int characterId, int classCode, int gender, long goldCost);

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
    public int Gender { get; set; }
    public int Level { get; set; }
    public long Exp { get; set; }
}

file sealed class CurrencyRow
{
    public int ItemCode { get; set; }
    public long Quantity { get; set; }
}

file sealed class PlayerItemEquippedRow
{
    public long PlayerItemId { get; set; }
    public int ItemCode { get; set; }
    public int EnhanceLevel { get; set; }
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
    private const int RowTypeItem = 1;
    private const int RowTypeCurrency = 2;
    private const int GoldItemCode = 1;
    private const int MySqlDuplicateEntry = 1062;

    private readonly GameDbFactory _dbFactory;

    /// <summary>세이브 DB 커넥션 팩토리를 주입받는다.</summary>
    public SaveRepository(GameDbFactory dbFactory) => _dbFactory = dbFactory;

    /// <summary>
    /// game_player 1행을 조회해 세이브 응답용 <see cref="PlayerDto"/>(닉네임·진행 좌표·최고 클리어·인벤 용량·마지막 활동 시각)로
    /// 변환한다. 읽기 전용 단건 조회이므로 트랜잭션 없이 자체 커넥션을 쓰며, 계정 세이브가 없으면 null.
    /// </summary>
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

    /// <summary>
    /// 계정의 player_character 전 행을 슬롯 번호(character_id) 순으로 조회해 캐릭터 DTO 목록으로 변환한다.
    /// 읽기 전용이므로 트랜잭션 없이 자체 커넥션을 쓴다.
    /// </summary>
    public async Task<List<CharacterDto>> GetCharactersAsync(long userId)
    {
        using var db = _dbFactory.Create();
        var rows = await db.Query("player_character").Where("user_id", userId).OrderBy("character_id")
            .GetAsync<PlayerCharacterRow>();
        return rows.Select(r => new CharacterDto
        {
            characterId = r.CharacterId,
            classCode = r.ClassCode,
            gender = r.Gender,
            level = r.Level,
            exp = r.Exp,
        }).ToList();
    }

    /// <summary>
    /// 계정의 재화 행(player_item의 row_type=2)만 조회해 재화 DTO 목록으로 변환한다. 재화는 종류당 1행이라
    /// 크기가 고정이므로 코어 로드에 포함한다(가방 아이템 페이징 대상에서는 slot이 NULL이라 자동 제외된다).
    /// 읽기 전용이므로 트랜잭션 없이 자체 커넥션을 쓴다.
    /// </summary>
    public async Task<List<CurrencyDto>> GetCurrenciesAsync(long userId)
    {
        using var db = _dbFactory.Create();
        var rows = await db.Query("player_item")
            .Select("item_code", "quantity")
            .Where("user_id", userId).Where("row_type", RowTypeCurrency)
            .GetAsync<CurrencyRow>();
        return rows.Select(r => new CurrencyDto
        {
            currencyType = r.ItemCode,
            amount = r.Quantity,
        }).ToList();
    }

    /// <summary>
    /// 계정의 장착 장비 전 행(player_item_equipped)을 조회해 DTO 목록으로 변환한다. 이 테이블이 item_code·
    /// enhance_level을 함께 보관하므로 player_item 조인이 필요 없다. 행 수가 최대 18개(3캐릭터 × 6슬롯)로
    /// 고정이고 캐릭터 스탯 계산의 입력이라 코어 로드에 포함한다.
    /// 읽기 전용이므로 트랜잭션 없이 자체 커넥션을 쓴다.
    /// </summary>
    public async Task<List<EquippedItemDto>> GetEquippedAsync(long userId)
    {
        using var db = _dbFactory.Create();
        var rows = await db.Query("player_item_equipped")
            .Where("user_id", userId)
            .OrderBy("equipped_character_id", "equipped_slot")
            .GetAsync<PlayerItemEquippedRow>();
        return rows.Select(r => new EquippedItemDto
        {
            itemId = r.PlayerItemId,
            itemCode = r.ItemCode,
            enhanceLevel = r.EnhanceLevel,
            equippedCharacterId = r.EquippedCharacterId,
            equippedSlot = r.EquippedSlot,
        }).ToList();
    }

    /// <summary>
    /// 가방 아이템(row_type=1이면서 인벤 칸에 배치된 행) 총 개수를 센다. 코어 로드가 페이징 진행률·용량 UI용으로
    /// 내려보내는 값이며, 재화 행(slot NULL)은 제외된다. 읽기 전용이라 트랜잭션을 쓰지 않는다.
    /// </summary>
    public async Task<int> GetBagItemCountAsync(long userId)
    {
        using var db = _dbFactory.Create();
        return await db.Query("player_item")
            .Where("user_id", userId).Where("row_type", RowTypeItem).WhereNotNull("slot")
            .CountAsync<int>();
    }

    /// <summary>
    /// 계정의 player_skill 행 중 습득한 스킬(레벨 1 이상)만 조회해 DTO 목록으로 변환한다.
    /// 스킬 초기화는 행을 삭제하지 않고 레벨 0으로 되돌리므로(GrowthRepository.ApplySkillResetAsync),
    /// 레벨 0 행은 미습득으로 보아 여기서 걸러 클라이언트에는 행이 없는 것과 동일하게 보인다.
    /// 읽기 전용이므로 트랜잭션 없이 자체 커넥션을 쓴다.
    /// </summary>
    public async Task<List<SkillDto>> GetSkillsAsync(long userId)
    {
        using var db = _dbFactory.Create();
        var rows = await db.Query("player_skill")
            .Where("user_id", userId).Where("level", ">", 0)
            .GetAsync<PlayerSkillRow>();
        return rows.Select(r => new SkillDto
        {
            characterId = r.CharacterId,
            skillCode = r.SkillCode,
            level = r.Level,
            equipped = r.Equipped,
        }).ToList();
    }

    /// <summary>
    /// 계정 공용 player_rune 전 행(룬 코드·레벨)을 조회해 DTO 목록으로 변환한다.
    /// 읽기 전용이므로 트랜잭션 없이 자체 커넥션을 쓴다.
    /// </summary>
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

    /// <summary>
    /// player_cube 1행(큐브 레벨·경험치)을 조회해 DTO로 변환한다. 행이 없으면(큐브 미초기화) null.
    /// 읽기 전용이므로 트랜잭션 없이 자체 커넥션을 쓴다.
    /// </summary>
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

    /// <summary>
    /// 캐릭터 추가 생성 시 필요한 기존 슬롯 정보(character_id·class_code)만 조회한다.
    /// 서비스가 빈 슬롯 배정과 직업 중복 검사에 사용하며, 읽기 전용이므로 트랜잭션을 쓰지 않는다.
    /// </summary>
    public async Task<List<CharacterSlot>> GetCharacterSlotsAsync(long userId)
    {
        using var db = _dbFactory.Create();
        var rows = await db.Query("player_character").Select("character_id", "class_code").Where("user_id", userId)
            .GetAsync<PlayerCharacterRow>();
        return rows.Select(r => new CharacterSlot(r.CharacterId, r.ClassCode)).ToList();
    }

    /// <summary>
    /// 최초 접속 시 세이브 초기화(플레이어 행 + 1번 슬롯 캐릭터 + 큐브 + 출석 진행도 생성)를 단일 커넥션의
    /// 단일 트랜잭션으로 수행한다. INSERT 중 하나라도 실패하면 전부 롤백하고 예외를 그대로 전파한다
    /// (이미 존재하는 계정이면 PK 중복 예외).
    /// </summary>
    /// <remarks>
    /// 한 트랜잭션으로 묶는 작업(캐릭터·큐브·출석 진행도가 없는 반쪽 세이브가 남지 않게 한다):
    /// <para>1) game_player INSERT — 닉네임·시작 좌표(1-1-1)·최고 클리어 0·초기 인벤 용량·활동/생성/갱신 시각</para>
    /// <para>2) player_character INSERT — 1번 슬롯에 선택 직업·성별 캐릭터를 레벨 1·경험치 0으로 생성</para>
    /// <para>3) player_cube INSERT — 큐브를 레벨 1·경험치 0으로 초기화</para>
    /// <para>4) player_attendance INSERT — 출석 진행도를 0(누적 0·마지막 획득 일자 0)으로 초기화.
    ///     출석 수령은 이 행의 조건부 갱신으로 처리하므로 계정 생성 시 함께 만들어 둔다(attendance 기획서 §4)</para>
    /// </remarks>
    public async Task CreatePlayerWithFirstCharacterAsync(long userId, string nickname, int classCode, int gender, int inventoryCapacity, long nowUnix)
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
                gender,
                level = 1,
                exp = 0,
            }, transaction);

            await db.Query("player_cube").InsertAsync(new
            {
                user_id = userId,
                cube_level = 1,
                cube_exp = 0,
            }, transaction);

            await db.Query("player_attendance").InsertAsync(new
            {
                user_id = userId,
                attend_count = 0,
                last_attend_date = 0,
            }, transaction);

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// 기존 계정에 캐릭터 1개 추가(생성 비용 골드 차감)를 단일 커넥션의 단일 트랜잭션으로 적용한다.
    /// 골드 부족은 InsufficientCurrency, 슬롯/직업 유니크 경합은 DuplicateConflict로 롤백 후 반환하며,
    /// 그 외 예외는 롤백 후 전파한다.
    /// </summary>
    /// <remarks>
    /// 한 트랜잭션으로 묶는 작업(하나라도 실패하면 전부 롤백 — 골드만 차감되고 캐릭터가 안 생기는 상태를 막는다):
    /// <para>1) player_item(재화 행) SELECT — 골드 잔액 확인(행이 없으면 잔액 0, 비용 미달 → InsufficientCurrency)</para>
    /// <para>2) player_item UPDATE — 비용이 0보다 클 때만 골드 차감</para>
    /// <para>3) player_character INSERT — 지정 슬롯에 캐릭터(직업·성별)를 레벨 1·경험치 0으로 생성.
    ///     유니크 제약(슬롯 PK·계정 내 직업 중복) 위반(MySQL 1062)은 동시 생성 경합으로 보고 롤백 → DuplicateConflict</para>
    /// </remarks>
    public async Task<AddCharacterOutcome> AddCharacterAsync(long userId, int characterId, int classCode, int gender, long goldCost)
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
                    gender,
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

    /// <summary>
    /// game_player의 last_active_at·updated_at을 현재 시각으로 갱신한다(오프라인 정산 기준 시각 리셋).
    /// 쓰기가 한 문장이라 그 자체로 원자적이므로 트랜잭션을 열지 않으며, 갱신 행 수(0이면 계정 없음)를 반환한다.
    /// </summary>
    public async Task<int> UpdateLastActiveAsync(long userId, long nowUnix)
    {
        using var db = _dbFactory.Create();
        return await db.Query("game_player")
            .Where("user_id", userId)
            .UpdateAsync(new { last_active_at = nowUnix, updated_at = nowUnix });
    }
}
