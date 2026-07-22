using GameServer.Data;
using GameServer.MasterData;
using SqlKata.Execution;
using TaskbarHero.Common.Dto;

namespace GameServer.Repositories;

/// <summary>game_player 진행도 스냅샷(스테이지 도메인에서 필요한 필드만).</summary>
public sealed record StageProgressRow(int Act, int Difficulty, int Stage, int MaxStageCleared, int InventoryCapacity);

/// <summary>클리어 트랜잭션 결과 상태.</summary>
public enum ClearStatus
{
    Ok,
    NoPlayer,      // game_player 없음(세이브 미생성)
    NotEntered,    // 현재 진입 스테이지와 요청 불일치
    InventoryFull, // 전리품 적재 중 용량 초과
}

/// <summary>클리어 트랜잭션 결과.</summary>
public sealed record ClearOutcome(
    ClearStatus Status,
    List<CharacterProgressDto> Characters,
    long GoldBalance,
    int Act,
    int Difficulty,
    int Stage,
    int MaxStageCleared)
{
    public static ClearOutcome Fail(ClearStatus status)
        => new(status, new List<CharacterProgressDto>(), 0, 0, 0, 0, 0);
}

public interface IStageRepository
{
    Task<StageProgressRow?> GetProgressAsync(long userId);

    /// <summary>현재 진입 스테이지를 설정한다(game_player.act/difficulty/stage). 갱신 행 수 반환.</summary>
    Task<int> SetCurrentStageAsync(long userId, int act, int difficulty, int stage, long nowUnix);

    /// <summary>
    /// 클리어를 한 트랜잭션으로 적용한다: 진입 스테이지 재검증 → 골드/경험치 지급·전리품 적재 → 진행도 갱신.
    /// 경험치→레벨 계산은 주입된 levelUp 델리게이트(현재 level·exp → 지급 후 상태)로 처리한다.
    /// </summary>
    Task<ClearOutcome> ApplyClearAsync(
        long userId,
        int expectedAct, int expectedDifficulty, int expectedStage,
        long gold, DroppedItem? dropped,
        Func<int, long, (int newLevel, long newExp, bool leveledUp)> levelUp,
        long nowUnix);
}

// ── DB 행 매핑용 POCO(제네릭 매핑 전용, dynamic 금지). snake_case→PascalCase는 Dapper 규칙으로 매핑. ──
file sealed class PlayerProgressRow
{
    public int Act { get; set; }
    public int Difficulty { get; set; }
    public int Stage { get; set; }
    public int MaxStageCleared { get; set; }
    public int InventoryCapacity { get; set; }
}

file sealed class CharProgressRow
{
    public int CharacterId { get; set; }
    public int Level { get; set; }
    public long Exp { get; set; }
}

file sealed class ItemIdQtyRow
{
    public long PlayerItemId { get; set; }
    public long Quantity { get; set; }
}

/// <summary>스테이지 진행/클리어 세이브 접근 계층(taskbar_hero_game). SqlKata 쿼리 빌더 + 제네릭 매핑만 사용한다(dynamic 금지).</summary>
public sealed class StageRepository : IStageRepository
{
    private const int RowTypeItem = 1;
    private const int RowTypeCurrency = 2;
    private const int GoldItemCode = 1;

    private readonly GameDbFactory _dbFactory;

    public StageRepository(GameDbFactory dbFactory) => _dbFactory = dbFactory;

    public async Task<StageProgressRow?> GetProgressAsync(long userId)
    {
        using var db = _dbFactory.Create();
        var row = await db.Query("game_player")
            .Select("act", "difficulty", "stage", "max_stage_cleared", "inventory_capacity")
            .Where("user_id", userId)
            .FirstOrDefaultAsync<PlayerProgressRow>();

        if (row is null)
        {
            return null;
        }

        return new StageProgressRow(row.Act, row.Difficulty, row.Stage, row.MaxStageCleared, row.InventoryCapacity);
    }

    public async Task<int> SetCurrentStageAsync(long userId, int act, int difficulty, int stage, long nowUnix)
    {
        using var db = _dbFactory.Create();
        return await db.Query("game_player")
            .Where("user_id", userId)
            .UpdateAsync(new { act, difficulty, stage, updated_at = nowUnix });
    }

    public async Task<ClearOutcome> ApplyClearAsync(
        long userId,
        int expectedAct, int expectedDifficulty, int expectedStage,
        long gold, DroppedItem? dropped,
        Func<int, long, (int newLevel, long newExp, bool leveledUp)> levelUp,
        long nowUnix)
    {
        await using var connection = _dbFactory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var db = _dbFactory.Create(connection);

            // 1) 현재 진입 스테이지 재검증(트랜잭션 내부에서 원자적으로).
            var player = await db.Query("game_player")
                .Select("act", "difficulty", "stage", "max_stage_cleared", "inventory_capacity")
                .Where("user_id", userId)
                .FirstOrDefaultAsync<PlayerProgressRow>(transaction);

            if (player is null)
            {
                await transaction.RollbackAsync();
                return ClearOutcome.Fail(ClearStatus.NoPlayer);
            }

            int curAct = player.Act;
            int curDiff = player.Difficulty;
            int curStage = player.Stage;
            int maxCleared = player.MaxStageCleared;
            int capacity = player.InventoryCapacity;

            if (curAct != expectedAct || curDiff != expectedDifficulty || curStage != expectedStage)
            {
                await transaction.RollbackAsync();
                return ClearOutcome.Fail(ClearStatus.NotEntered);
            }

            // 2) 골드 지급(재화 행 upsert).
            long goldBalance = await UpsertGoldAsync(db, transaction, userId, gold, nowUnix);

            // 3) 경험치 지급(3캐릭터 동일) + 레벨 재계산.
            var charRows = await db.Query("player_character")
                .Select("character_id", "level", "exp")
                .Where("user_id", userId)
                .OrderBy("character_id")
                .GetAsync<CharProgressRow>(transaction);

            var characters = new List<CharacterProgressDto>();
            foreach (var c in charRows)
            {
                int characterId = c.CharacterId;

                var (newLevel, newExp, leveledUp) = levelUp(c.Level, c.Exp);
                await db.Query("player_character")
                    .Where("user_id", userId).Where("character_id", characterId)
                    .UpdateAsync(new { level = newLevel, exp = newExp }, transaction);

                characters.Add(new CharacterProgressDto
                {
                    characterId = characterId,
                    level = newLevel,
                    exp = newExp,
                    isLevelUp = leveledUp,
                });
            }

            // 4) 전리품 적재(있으면). 용량 초과 시 롤백.
            if (dropped is not null)
            {
                var stored = await StoreDroppedItemAsync(db, transaction, userId, dropped, capacity, nowUnix);
                if (!stored)
                {
                    await transaction.RollbackAsync();
                    return ClearOutcome.Fail(ClearStatus.InventoryFull);
                }
            }

            // 5) 진행도 갱신: 프런티어 클리어면 다음 스테이지로 전진 + max 갱신, 재파밍이면 유지.
            int seq = StageCoords.Sequence(expectedAct, expectedDifficulty, expectedStage);
            bool isFrontier = seq == maxCleared + 1;

            int newAct = curAct, newDiff = curDiff, newStage = curStage, newMax = maxCleared;
            if (isFrontier)
            {
                newMax = seq;
                if (StageCoords.TryDecodeSequence(seq + 1, out var na, out var nd, out var ns))
                {
                    newAct = na;
                    newDiff = nd;
                    newStage = ns;
                }
                // seq == TotalStages(전부 클리어)면 현재 스테이지 유지.

                await db.Query("game_player")
                    .Where("user_id", userId)
                    .UpdateAsync(new
                    {
                        act = newAct,
                        difficulty = newDiff,
                        stage = newStage,
                        max_stage_cleared = newMax,
                        updated_at = nowUnix,
                    }, transaction);
            }
            else
            {
                // 재파밍: 진행도 유지, 갱신 시각만 반영.
                await db.Query("game_player")
                    .Where("user_id", userId)
                    .UpdateAsync(new { updated_at = nowUnix }, transaction);
            }

            await transaction.CommitAsync();
            return new ClearOutcome(ClearStatus.Ok, characters, goldBalance, newAct, newDiff, newStage, newMax);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>재화(골드) 행을 upsert하고 갱신 후 잔액을 반환한다.</summary>
    private static async Task<long> UpsertGoldAsync(
        QueryFactory db, System.Data.Common.DbTransaction transaction, long userId, long gold, long nowUnix)
    {
        var goldRow = await db.Query("player_item")
            .Select("player_item_id", "quantity")
            .Where("user_id", userId).Where("row_type", RowTypeCurrency).Where("item_code", GoldItemCode)
            .FirstOrDefaultAsync<ItemIdQtyRow>(transaction);

        if (goldRow is null)
        {
            await db.Query("player_item").InsertAsync(new
            {
                user_id = userId,
                row_type = RowTypeCurrency,
                item_code = GoldItemCode,
                quantity = gold,
                slot = (int?)null,
                enhance_level = 0,
                acquired_at = nowUnix,
            }, transaction);
            return gold;
        }

        long goldRowId = goldRow.PlayerItemId;
        long newBalance = goldRow.Quantity + gold;
        await db.Query("player_item")
            .Where("player_item_id", goldRowId)
            .UpdateAsync(new { quantity = newBalance }, transaction);
        return newBalance;
    }

    /// <summary>전리품 1개를 인벤토리에 적재한다. 재료는 기존 스택에 합치고, 새 칸이 필요하면 용량을 확인한다.
    /// 용량 초과로 적재 실패하면 false.</summary>
    private static async Task<bool> StoreDroppedItemAsync(
        QueryFactory db, System.Data.Common.DbTransaction transaction, long userId, DroppedItem dropped, int capacity, long nowUnix)
    {
        // 재료(스택 가능): 여유 있는 기존 스택에 합친다(새 칸 불필요).
        if (dropped.StackMax > 1)
        {
            var stackRow = await db.Query("player_item")
                .Select("player_item_id", "quantity")
                .Where("user_id", userId).Where("row_type", RowTypeItem).Where("item_code", dropped.ItemCode)
                .Where("quantity", "<", dropped.StackMax)
                .FirstOrDefaultAsync<ItemIdQtyRow>(transaction);

            if (stackRow is not null)
            {
                long stackRowId = stackRow.PlayerItemId;
                long merged = stackRow.Quantity + dropped.Quantity;
                await db.Query("player_item")
                    .Where("player_item_id", stackRowId)
                    .UpdateAsync(new { quantity = merged }, transaction);
                return true;
            }
        }

        // 새 칸이 필요: 점유 칸을 조회해 빈 칸을 찾는다.
        var slotValues = await db.Query("player_item")
            .Select("slot")
            .Where("user_id", userId).WhereNotNull("slot")
            .GetAsync<int>(transaction);

        var used = new HashSet<int>(slotValues);

        if (used.Count >= capacity)
        {
            return false; // 용량 초과
        }

        int freeSlot = 0;
        while (used.Contains(freeSlot))
        {
            freeSlot++;
        }

        await db.Query("player_item").InsertAsync(new
        {
            user_id = userId,
            row_type = RowTypeItem,
            item_code = dropped.ItemCode,
            quantity = dropped.Quantity,
            slot = freeSlot,
            enhance_level = 0,
            acquired_at = nowUnix,
        }, transaction);
        return true;
    }
}
