using System.Data.Common;
using GameServer.Data;
using GameServer.Models;
using GameServer.Repositories.Interfaces;
using MySqlConnector;
using SqlKata.Execution;
using TaskbarHero.Common.Dto;

namespace GameServer.Repositories;

/// <summary>보유 캐릭터 요약(식별자 배정·직업 중복 검사·파티 자리 배정용). Slot은 파티 자리(0=미편성, 1~3).</summary>
public sealed record CharacterSlot(int CharacterId, int ClassCode, int Slot);

/// <summary>
/// 캐릭터 생성 시 함께 지급·장착할 기본 장비 1개(현재는 직업별 기본 무기). ItemCode는 지급 아이템,
/// EquipSlot은 장착 슬롯(equip_slot_master)이며 둘 다 마스터(item_master)에서 서비스가 확정해 넘긴다.
/// </summary>
public sealed record StartingEquipment(int ItemCode, int EquipSlot);

/// <summary>캐릭터 추가 생성 트랜잭션 결과. Cost=차감 골드, GoldBalance=차감 후 잔액.</summary>
public sealed record AddCharacterOutcome(AddCharacterStatus Status, long Cost, long GoldBalance)
{
    public static AddCharacterOutcome Fail(AddCharacterStatus status) => new(status, 0, 0);
}

/// <summary>파티 편성 저장 트랜잭션 결과. Characters=갱신된 보유 캐릭터 전체(파티 자리 순).</summary>
public sealed record ArrangePartyOutcome(ArrangePartyStatus Status, List<CharacterDto> Characters)
{
    public static ArrangePartyOutcome Fail(ArrangePartyStatus status) => new(status, new List<CharacterDto>());
}

/// <summary>세이브(taskbar_hero_game) 접근 계층. SqlKata 쿼리 빌더 + 제네릭 매핑만 사용한다(dynamic 금지).</summary>
public sealed class SaveRepository : ISaveRepository
{
    private const int RowTypeItem = 1;
    private const int RowTypeCurrency = 2;
    private const int GoldItemCode = 1;
    private const int MySqlDuplicateEntry = 1062;

    /// <summary>player_character.slot의 "미편성"(파티에 속하지 않음) 값. 1~3은 파티 자리다.</summary>
    private const int PartySlotUnassigned = 0;

    /// <summary>캐릭터 생성 시 함께 습득시키는 기본 액티브 스킬의 레벨(1레벨 = 스킬 포인트 1을 미리 투자한 상태).</summary>
    private const int StartingSkillLevel = 1;

    /// <summary>player_skill.equipped의 "장착됨" 값(1). 기본 스킬은 습득과 동시에 장착해 곧바로 전투에 쓰이게 한다.</summary>
    private const int SkillEquipped = 1;

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
    /// 계정의 player_character 전 행(보유 캐릭터)을 조회해 캐릭터 DTO 목록으로 변환한다.
    /// 편성된 캐릭터(slot 1~3)를 자리 순으로 먼저 두고, 미편성(slot 0)은 뒤에 식별자 순으로 붙인다
    /// — 클라이언트 파티 UI가 정렬 없이 그대로 그릴 수 있게 하기 위함이다.
    /// 읽기 전용이므로 트랜잭션 없이 자체 커넥션을 쓴다.
    /// </summary>
    public async Task<List<CharacterDto>> GetCharactersAsync(long userId)
    {
        using var db = _dbFactory.Create();
        var rows = await db.Query("player_character").Where("user_id", userId)
            .OrderByRaw("CASE WHEN slot = 0 THEN 1 ELSE 0 END, slot, character_id")
            .GetAsync<PlayerCharacterRow>();
        return SortCharactersForResponse(rows);
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
    /// 캐릭터 추가 생성 시 필요한 보유 캐릭터 정보(character_id·class_code·slot)만 조회한다.
    /// 서비스가 다음 식별자 배정·직업 중복 검사·빈 파티 자리 배정에 사용하며, 읽기 전용이므로 트랜잭션을 쓰지 않는다.
    /// </summary>
    public async Task<List<CharacterSlot>> GetCharacterSlotsAsync(long userId)
    {
        using var db = _dbFactory.Create();
        var rows = await db.Query("player_character").Select("character_id", "class_code", "slot").Where("user_id", userId)
            .GetAsync<PlayerCharacterRow>();
        return rows.Select(r => new CharacterSlot(r.CharacterId, r.ClassCode, r.Slot)).ToList();
    }

    /// <summary>
    /// 최초 접속 시 세이브 초기화(플레이어 행 + 1번 슬롯 캐릭터 + 큐브 + 출석 진행도 생성)를 단일 커넥션의
    /// 단일 트랜잭션으로 수행한다. INSERT 중 하나라도 실패하면 전부 롤백하고 예외를 그대로 전파한다
    /// (이미 존재하는 계정이면 PK 중복 예외).
    /// </summary>
    /// <remarks>
    /// 한 트랜잭션으로 묶는 작업(캐릭터·큐브·출석 진행도가 없는 반쪽 세이브가 남지 않게 한다):
    /// <para>1) game_player INSERT — 닉네임·시작 좌표(1-1-1)·최고 클리어 0·초기 인벤 용량·활동/생성/갱신 시각</para>
    /// <para>2) player_character INSERT — 첫 캐릭터(식별자 1)를 선택 직업·성별로, 파티 1번 자리에 레벨 1·경험치 0으로 생성</para>
    /// <para>3) player_item + player_item_equipped INSERT — 직업 기본 무기를 지급해 무기 슬롯에 장착한 상태로 만든다
    ///     (startingEquipment가 있을 때만). 장착 중인 장비는 가방 칸을 쓰지 않으므로 slot은 NULL이다</para>
    /// <para>4) player_skill INSERT — 직업 기본 액티브 스킬을 레벨 1·장착 상태로 습득시킨다(startingSkillCode가 있을 때만)</para>
    /// <para>5) player_cube INSERT — 큐브를 레벨 1·경험치 0으로 초기화</para>
    /// <para>6) player_attendance INSERT — 출석 진행도를 0(누적 0·마지막 획득 일자 0)으로 초기화.
    ///     출석 수령은 이 행의 조건부 갱신으로 처리하므로 계정 생성 시 함께 만들어 둔다(attendance 기획서 §4)</para>
    /// <para>7) player_mail(+player_mail_reward) INSERT — 신규 가입 지원금 메일 발급(welcomeMail이 있을 때만).
    ///     game_player가 계정당 1행이라 이 트랜잭션은 계정 생애에 한 번만 성공하므로, 지급 여부 플래그 없이
    ///     중복 지급이 원천 차단된다(세이브 데이터 기획서 5.3)</para>
    /// </remarks>
    public async Task CreatePlayerWithFirstCharacterAsync(
        long userId, string nickname, int classCode, int gender, int inventoryCapacity, long nowUnix,
        MailDraft? welcomeMail, StartingEquipment? startingEquipment, int? startingSkillCode)
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
                slot = 1, // 첫 캐릭터는 파티 1번 자리에 편성된 상태로 시작
                gender,
                level = 1,
                exp = 0,
            }, transaction);

            if (startingEquipment is not null)
            {
                await GrantEquippedStartingItemAsync(db, transaction, userId, 1, startingEquipment, nowUnix);
            }

            if (startingSkillCode is not null)
            {
                await GrantEquippedStartingSkillAsync(db, transaction, userId, 1, startingSkillCode.Value);
            }

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

            if (welcomeMail is not null)
            {
                await MailRepository.InsertMailAsync(db, transaction, userId, welcomeMail, nowUnix);
            }

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
    /// <para>3) player_character INSERT — 지정 식별자·파티 자리(slot, 빈 자리 없으면 0=미편성)로 캐릭터(직업·성별)를
    ///     레벨 1·경험치 0으로 생성. 유니크 제약(식별자 PK·계정 내 직업 중복) 위반(MySQL 1062)은
    ///     동시 생성 경합으로 보고 롤백 → DuplicateConflict</para>
    /// <para>4) player_item + player_item_equipped INSERT — 직업 기본 무기를 지급해 무기 슬롯에 장착한 상태로 만든다
    ///     (startingEquipment가 있을 때만). 장착 중이라 가방 칸(slot)은 NULL이므로 용량이 가득 차도 실패하지 않는다</para>
    /// <para>5) player_skill INSERT — 직업 기본 액티브 스킬을 레벨 1·장착 상태로 습득시킨다(startingSkillCode가 있을 때만)</para>
    /// </remarks>
    public async Task<AddCharacterOutcome> AddCharacterAsync(
        long userId, int characterId, int classCode, int slot, int gender, long goldCost,
        StartingEquipment? startingEquipment, int? startingSkillCode, long nowUnix)
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
                    slot,
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

            // 4) 기본 무기 지급 + 장착(정의가 있을 때만).
            if (startingEquipment is not null)
            {
                await GrantEquippedStartingItemAsync(db, transaction, userId, characterId, startingEquipment, nowUnix);
            }

            // 5) 기본 액티브 스킬 습득 + 장착(정의가 있을 때만).
            if (startingSkillCode is not null)
            {
                await GrantEquippedStartingSkillAsync(db, transaction, userId, characterId, startingSkillCode.Value);
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
    /// 캐릭터 생성 트랜잭션 안에서 기본 장비 1개를 지급하고 곧바로 그 캐릭터에 장착시킨다
    /// (player_item 1행 + player_item_equipped 1행). <b>호출자의 트랜잭션을 그대로 쓰므로</b> 캐릭터와 장비가 함께 확정된다.
    /// <para>장착 중인 장비는 가방 칸을 쓰지 않는다는 규칙(InventoryRepository.ApplyEquipAsync)에 맞춰
    /// player_item.slot을 NULL로 넣는다 — 그래서 가방이 가득 차 있어도 지급이 실패하지 않고, 유니크 (user_id, slot)에도 걸리지 않는다.</para>
    /// <para>새로 만든 캐릭터의 빈 슬롯에 넣으므로 기존 장비와의 스왑·해제 처리가 필요 없다.</para>
    /// </summary>
    private static async Task GrantEquippedStartingItemAsync(
        QueryFactory db, DbTransaction transaction, long userId, int characterId, StartingEquipment equipment, long nowUnix)
    {
        long playerItemId = await db.Query("player_item").InsertGetIdAsync<long>(new
        {
            user_id = userId,
            row_type = RowTypeItem,
            item_code = equipment.ItemCode,
            quantity = 1,
            slot = (int?)null, // 장착 중이라 가방 칸을 점유하지 않는다
            enhance_level = 0,
            acquired_at = nowUnix,
        }, transaction);

        await db.Query("player_item_equipped").InsertAsync(new
        {
            player_item_id = playerItemId,
            user_id = userId,
            item_code = equipment.ItemCode,
            enhance_level = 0,
            equipped_character_id = characterId,
            equipped_slot = equipment.EquipSlot,
        }, transaction);
    }

    /// <summary>
    /// 캐릭터 생성 트랜잭션 안에서 직업 기본 액티브 스킬 1개를 레벨 1로 습득시키고 곧바로 장착까지 마친다(player_skill 1행).
    /// <b>호출자의 트랜잭션을 그대로 쓰므로</b> 캐릭터와 기본 스킬이 함께 확정된다.
    /// <para>레벨 1로 넣는다는 것은 <b>스킬 포인트 1을 미리 투자한 상태</b>라는 뜻이다 — 사용 포인트는 저장하지 않고
    /// 그 캐릭터의 스킬 레벨 합으로 파생하므로(GrowthRepository.ApplySkillLevelUpAsync), 별도 회계 처리 없이
    /// 레벨 1 캐릭터의 잔여 포인트가 0이 되고 스킬 초기화 시 그 1포인트가 그대로 회수된다.</para>
    /// <para>장착 한도(2개) 안에서 첫 칸을 채우는 것이라 기존 장착과 충돌하지 않는다(새 캐릭터라 다른 스킬이 없다).</para>
    /// </summary>
    private static async Task GrantEquippedStartingSkillAsync(
        QueryFactory db, DbTransaction transaction, long userId, int characterId, int skillCode)
    {
        await db.Query("player_skill").InsertAsync(new
        {
            user_id = userId,
            character_id = characterId,
            skill_code = skillCode,
            level = StartingSkillLevel,
            equipped = SkillEquipped,
        }, transaction);
    }

    /// <summary>
    /// 클라이언트가 보낸 <b>파티 편성 스냅샷</b>(저장 후의 파티 전체)을 단일 커넥션의 단일 트랜잭션으로 저장한다.
    /// 이동 절차가 아니라 결과 상태를 기록하는 방식이라 추가·추방·교체·자리 바꾸기가 한 번에 반영되고,
    /// 같은 요청을 반복해도 결과가 같다(멱등). 캐릭터의 성장·장비는 건드리지 않고 player_character.slot만 쓰므로
    /// 파티에서 내려도 레벨·스킬·장착 장비는 그대로 보존된다.
    /// </summary>
    /// <remarks>
    /// 한 트랜잭션으로 묶는 작업(하나라도 실패하면 전부 롤백 — 편성이 반만 반영된 상태를 막는다):
    /// <para>1) player_character SELECT — 계정의 보유 캐릭터 전량을 읽어 요청 목록이 전부 보유 캐릭터인지 확인한다.
    ///     하나라도 없으면 CharacterNotFound로 롤백한다(요청 값 자체의 형식·중복 검증은 서비스가 선처리)</para>
    /// <para>2) player_character UPDATE — 계정의 편성을 먼저 전부 미편성(slot 0)으로 되돌린다.
    ///     이렇게 비우고 다시 세우면 두 캐릭터가 같은 자리를 스쳐 가는 중간 상태가 없어,
    ///     (user_id, slot) 부분 유니크 인덱스를 걸 수 없는 제약에도 자리 중복이 발생하지 않는다</para>
    /// <para>3) player_character UPDATE ×N — 요청 목록대로 각 캐릭터에 자리(1~3)를 부여한다</para>
    /// <para>4) player_character SELECT — 갱신된 보유 캐릭터 전체를 자리 순으로 다시 읽어 응답에 싣는다</para>
    /// </remarks>
    public async Task<ArrangePartyOutcome> SavePartyAsync(long userId, IReadOnlyList<PartyMemberDto> members)
    {
        await using var connection = _dbFactory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var db = _dbFactory.Create(connection);

            // 1) 보유 캐릭터 확인 — 요청 목록에 보유하지 않은 캐릭터가 섞여 있으면 거부한다.
            var owned = (await db.Query("player_character").Where("user_id", userId)
                .GetAsync<PlayerCharacterRow>(transaction)).ToList();

            var ownedIds = owned.Select(r => r.CharacterId).ToHashSet();
            if (members.Any(m => !ownedIds.Contains(m.characterId)))
            {
                await transaction.RollbackAsync();
                return ArrangePartyOutcome.Fail(ArrangePartyStatus.CharacterNotFound);
            }

            // 2) 편성을 전부 비운다(중간 자리 충돌 방지).
            await db.Query("player_character")
                .Where("user_id", userId).Where("slot", "!=", PartySlotUnassigned)
                .UpdateAsync(new { slot = PartySlotUnassigned }, transaction);

            // 3) 스냅샷대로 자리를 다시 부여한다.
            foreach (var member in members)
            {
                await db.Query("player_character")
                    .Where("user_id", userId).Where("character_id", member.characterId)
                    .UpdateAsync(new { slot = member.slot }, transaction);
            }

            // 4) 갱신 결과를 다시 읽어 응답 목록으로 만든다.
            var updated = (await db.Query("player_character").Where("user_id", userId)
                .GetAsync<PlayerCharacterRow>(transaction)).ToList();

            await transaction.CommitAsync();
            return new ArrangePartyOutcome(ArrangePartyStatus.Ok, SortCharactersForResponse(updated));
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

    /// <summary>player_character 행 하나를 캐릭터 DTO로 변환한다(파티 자리 slot 포함).</summary>
    private static CharacterDto ToCharacterDto(PlayerCharacterRow row) => new CharacterDto
    {
        characterId = row.CharacterId,
        classCode = row.ClassCode,
        slot = row.Slot,
        gender = row.Gender,
        level = row.Level,
        exp = row.Exp,
    };

    /// <summary>보유 캐릭터 행을 응답 순서(편성된 자리 1~3 순 → 미편성은 식별자 순)로 정렬해 DTO 목록으로 만든다.</summary>
    private static List<CharacterDto> SortCharactersForResponse(IEnumerable<PlayerCharacterRow> rows)
        => rows
            .OrderBy(r => r.Slot == PartySlotUnassigned ? 1 : 0)
            .ThenBy(r => r.Slot)
            .ThenBy(r => r.CharacterId)
            .Select(ToCharacterDto)
            .ToList();
}
