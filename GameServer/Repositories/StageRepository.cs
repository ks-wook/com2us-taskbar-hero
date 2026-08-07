using GameServer.Data;
using GameServer.MasterData;
using SqlKata.Execution;
using TaskbarHero.Common;
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
}

/// <summary>
/// 클리어 트랜잭션 결과. GrantedGold·GrantedExp는 <b>활성 획득량 버프 배율을 적용한 최종 지급액</b>이며
/// (배율 판정은 지급과 같은 트랜잭션에서 수행), GoldMultiplier·ExpMultiplier는 그때 적용된 배율(버프 없으면 1.0)이다.
/// </summary>
public sealed record ClearOutcome(
    ClearStatus Status,
    List<CharacterProgressDto> Characters,
    long GoldBalance,
    long GrantedGold,
    long GrantedExp,
    decimal GoldMultiplier,
    decimal ExpMultiplier,
    int Act,
    int Difficulty,
    int Stage,
    int MaxStageCleared)
{
    /// <summary>가방 변경분(5.0). 전리품 적재로 생긴·병합된 행이 담긴다(드롭이 없으면 비어 있다).</summary>
    public InventoryDeltaDto Delta { get; init; } = new InventoryDeltaDto();

    /// <summary>
    /// 추첨된 전리품을 실제로 적재했는지 여부. 드롭이 없었거나 인벤토리 용량이 부족해 폐기한 경우 false다
    /// (용량 부족은 클리어를 거부하지 않고 골드·경험치만 지급한다 — stage-battle 기획서 6.3).
    /// </summary>
    public bool LootStored { get; init; }

    public static ClearOutcome Fail(ClearStatus status)
        => new(status, new List<CharacterProgressDto>(), 0, 0, 0, 1.0m, 1.0m, 0, 0, 0, 0);
}

public interface IStageRepository
{
    Task<StageProgressRow?> GetProgressAsync(long userId);

    /// <summary>현재 진입 스테이지를 설정한다(game_player.act/difficulty/stage). 갱신 행 수 반환.</summary>
    Task<int> SetCurrentStageAsync(long userId, int act, int difficulty, int stage, long nowUnix);

    /// <summary>
    /// 클리어를 한 트랜잭션으로 적용한다: 진입 스테이지 재검증 → 활성 획득량 버프 배율 판정 →
    /// 골드/경험치 지급·전리품 적재 → 진행도 갱신. baseGold·baseExp는 마스터의 기본 보상이며 배율은 이 안에서 곱한다.
    /// 경험치→레벨 계산은 주입된 levelUp 델리게이트(현재 level·exp·배율 적용된 지급 경험치 → 지급 후 상태)로 처리한다.
    /// 인벤토리 용량이 부족하면 전리품만 폐기하고(<see cref="ClearOutcome.LootStored"/>=false) 클리어는 성공시킨다.
    /// </summary>
    Task<ClearOutcome> ApplyClearAsync(
        long userId,
        int expectedAct, int expectedDifficulty, int expectedStage,
        long baseGold, long baseExp, DroppedItem? dropped,
        Func<int, long, long, (int newLevel, long newExp, bool leveledUp)> levelUp,
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

/// <summary>가방 변경분(5.0) 조립에 배치 칸이 필요한 스택 병합 조회용.</summary>
file sealed class ItemIdQtySlotRow
{
    public long PlayerItemId { get; set; }
    public long Quantity { get; set; }
    public int? Slot { get; set; }
}

file sealed class BuffMultiplierRow
{
    public int BuffType { get; set; }
    public decimal BuffValue { get; set; } // DECIMAL(5,3) → decimal로 받아 그대로 곱한다(부동소수 오차 없이 내림).
}

/// <summary>스테이지 진행/클리어 세이브 접근 계층(taskbar_hero_game). SqlKata 쿼리 빌더 + 제네릭 매핑만 사용한다(dynamic 금지).</summary>
public sealed class StageRepository : IStageRepository
{
    private const int RowTypeItem = 1;
    private const int RowTypeCurrency = 2;
    private const int GoldItemCode = 1;

    /// <summary>player_character.slot의 "미편성"(파티에 없어 전투에 참가하지 않음) 값.</summary>
    private const int PartySlotUnassigned = 0;

    private readonly GameDbFactory _dbFactory;

    /// <summary>세이브 DB 커넥션 팩토리를 주입받는다.</summary>
    public StageRepository(GameDbFactory dbFactory) => _dbFactory = dbFactory;

    /// <summary>
    /// game_player에서 스테이지 도메인용 진행도 스냅샷(현재 진입 좌표·최고 클리어 시퀀스·인벤토리 용량)을
    /// 단건 조회한다. 트랜잭션 없이 자체 커넥션으로 읽으며, 계정 세이브가 없으면 null.
    /// </summary>
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

    /// <summary>
    /// 현재 진입 스테이지 좌표(act/difficulty/stage)와 updated_at을 game_player에 기록한다(단일 UPDATE).
    /// 쓰기가 한 문장이라 그 자체로 원자적이므로 별도 트랜잭션을 열지 않는다. 갱신된 행 수(0이면 계정 없음)를 반환한다.
    /// </summary>
    public async Task<int> SetCurrentStageAsync(long userId, int act, int difficulty, int stage, long nowUnix)
    {
        using var db = _dbFactory.Create();
        return await db.Query("game_player")
            .Where("user_id", userId)
            .UpdateAsync(new { act, difficulty, stage, updated_at = nowUnix });
    }

    /// <summary>
    /// 클리어 판정·보상 지급·진행도 전진을 단일 커넥션의 단일 트랜잭션으로 적용한다.
    /// 검증 실패(NoPlayer·NotEntered)는 즉시 롤백 후 Fail 상태로 반환하고, 예외는 롤백 후 전파한다.
    /// </summary>
    /// <remarks>
    /// 한 트랜잭션으로 묶는 작업(하나라도 실패하면 전부 롤백 — 보상만 들어가고 진행도가 안 오르는 부분 반영을 막는다):
    /// <para>1) game_player SELECT — 현재 진입 좌표·max_stage_cleared·inventory_capacity 확보 후 요청 좌표와 일치 검증(불일치 → NotEntered)</para>
    /// <para>2) player_buff SELECT — 활성(<c>expires_at &gt; now</c>) 획득량 버프 배율을 읽어 골드·경험치 지급액 확정(내림).
    ///    <b>지급과 같은 트랜잭션에서 판정</b>해 배율 판정과 지급이 갈라지지 않게 한다(소모품/버프 기획서 6.2)</para>
    /// <para>3) player_item(재화 행) upsert — 클리어 보상 골드 적립, 갱신 후 잔액 산출</para>
    /// <para>4) player_character SELECT + 캐릭터별 UPDATE — <b>파티에 편성된(slot≠0)</b> 캐릭터에만 동일 경험치 지급 후 levelUp 델리게이트로 레벨 재계산</para>
    /// <para>5) player_item 전리품 적재 — 스택 가능하면 기존 스택 병합, 아니면 빈 칸에 INSERT
    ///    (용량 초과면 롤백하지 않고 전리품만 폐기 → LootStored=false, 골드·경험치·진행도는 그대로 반영)</para>
    /// <para>6) game_player 진행도 UPDATE — 프런티어 클리어면 max_stage_cleared 갱신 + 다음 스테이지로 전진, 재파밍이면 updated_at만 갱신</para>
    /// ⚠️ 원자성은 보장하지만 game_player 행에 잠금(FOR UPDATE 등)을 걸지 않으므로, 동일 userId의 동시 요청은
    ///    1)의 검증을 함께 통과할 수 있다(중복 전리품 지급·골드/경험치 lost update·슬롯 유니크 충돌). 백로그 과제.
    /// </remarks>
    public async Task<ClearOutcome> ApplyClearAsync(
        long userId,
        int expectedAct, int expectedDifficulty, int expectedStage,
        long baseGold, long baseExp, DroppedItem? dropped,
        Func<int, long, long, (int newLevel, long newExp, bool leveledUp)> levelUp,
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

            // 2) 활성 획득량 버프 배율 판정. 지급과 같은 트랜잭션에서 읽어 배율 판정과 지급이 갈라지지 않게 한다.
            //    트랜잭션 시작 시각(nowUnix) 기준 단일 판정이며, 만료 행(expires_at <= now)은 제외한다.
            var buffRows = await db.Query("player_buff")
                .Select("buff_type", "buff_value")
                .Where("user_id", userId).Where("expires_at", ">", nowUnix)
                .GetAsync<BuffMultiplierRow>(transaction);

            // PK가 (user_id, buff_type)이라 종류별 1행뿐이므로 키 충돌이 없다.
            var multipliers = buffRows.ToDictionary(r => r.BuffType, r => r.BuffValue);
            decimal goldMultiplier = MultiplierOf(multipliers, BuffType.GoldGain);
            decimal expMultiplier = MultiplierOf(multipliers, BuffType.ExpGain);
            long gold = ApplyMultiplier(baseGold, goldMultiplier);
            long exp = ApplyMultiplier(baseExp, expMultiplier);

            // 3) 골드 지급(재화 행 upsert).
            long goldBalance = await UpsertGoldAsync(db, transaction, userId, gold, nowUnix);

            // 4) 경험치 지급(파티 편성 캐릭터 동일) + 레벨 재계산.
            //    미편성(slot=0) 캐릭터는 전투에 나가지 않았으므로 경험치를 받지 않는다(세이브 데이터 기획서 5.5).
            var charRows = await db.Query("player_character")
                .Select("character_id", "level", "exp")
                .Where("user_id", userId).Where("slot", "!=", PartySlotUnassigned)
                .OrderBy("slot")
                .GetAsync<CharProgressRow>(transaction);

            var characters = new List<CharacterProgressDto>();
            foreach (var c in charRows)
            {
                int characterId = c.CharacterId;

                var (newLevel, newExp, leveledUp) = levelUp(c.Level, c.Exp, exp);
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

            // 5) 전리품 적재(있으면). 적재 결과는 가방 변경분(5.0)에 담긴다.
            //    용량이 부족하면 클리어를 거부하지 않고 전리품만 폐기한다(골드·경험치는 그대로 지급, 기획서 6.3).
            //    적재 실패는 빈 칸 판정 단계에서 결정되므로 이 시점까지 player_item에 쓰기가 없다(부분 반영 없음).
            var delta = new InventoryDeltaDto();
            bool lootStored = false;
            if (dropped is not null)
            {
                lootStored = await StoreDroppedItemAsync(db, transaction, userId, dropped, capacity, nowUnix, delta);
            }

            // 6) 진행도 갱신: 프런티어 클리어면 다음 스테이지로 전진 + max 갱신, 재파밍이면 유지.
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
            return new ClearOutcome(
                ClearStatus.Ok, characters, goldBalance,
                gold, exp, goldMultiplier, expMultiplier,
                newAct, newDiff, newStage, newMax)
            {
                Delta = delta,
                LootStored = lootStored,
            };
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>활성 버프 배율 목록에서 지정 종류의 획득량 배율을 얻는다. 해당 종류의 활성 버프가 없으면 1.0(배율 없음).</summary>
    private static decimal MultiplierOf(IReadOnlyDictionary<int, decimal> multipliers, BuffType buffType)
        => multipliers.TryGetValue((int)buffType, out var value) ? value : 1.0m;

    /// <summary>기본 보상에 획득량 배율을 곱해 지급액을 확정한다(기획서 6.2: 정수 내림). 배율이 1.0이면 원값 그대로.</summary>
    private static long ApplyMultiplier(long baseAmount, decimal multiplier)
        => multiplier == 1.0m ? baseAmount : (long)decimal.Floor(baseAmount * multiplier);

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
        QueryFactory db, System.Data.Common.DbTransaction transaction, long userId, DroppedItem dropped, int capacity, long nowUnix,
        InventoryDeltaDto delta)
    {
        // 재료(스택 가능): 여유 있는 기존 스택에 합친다(새 칸 불필요).
        if (dropped.StackMax > 1)
        {
            var stackRow = await db.Query("player_item")
                .Select("player_item_id", "quantity", "slot")
                .Where("user_id", userId).Where("row_type", RowTypeItem).Where("item_code", dropped.ItemCode)
                .Where("quantity", "<", dropped.StackMax)
                .FirstOrDefaultAsync<ItemIdQtySlotRow>(transaction);

            if (stackRow is not null)
            {
                long stackRowId = stackRow.PlayerItemId;
                long merged = stackRow.Quantity + dropped.Quantity;
                await db.Query("player_item")
                    .Where("player_item_id", stackRowId)
                    .UpdateAsync(new { quantity = merged }, transaction);
                delta.upserted.Add(new InventoryItemDto
                {
                    itemId = stackRowId,
                    slot = stackRow.Slot ?? 0,
                    itemCode = dropped.ItemCode,
                    quantity = merged,
                    enhanceLevel = 0,
                });
                return true;
            }
        }

        // 새 칸이 필요: 점유 칸을 조회해 빈 칸을 찾는다.
        var used = await InventorySlotAllocator.LoadUsedSlotsAsync(db, transaction, userId);
        if (!InventorySlotAllocator.TryFirstFree(used, capacity, out int freeSlot))
        {
            return false; // 용량 초과
        }

        long newItemId = await db.Query("player_item").InsertGetIdAsync<long>(new
        {
            user_id = userId,
            row_type = RowTypeItem,
            item_code = dropped.ItemCode,
            quantity = dropped.Quantity,
            slot = freeSlot,
            enhance_level = 0,
            acquired_at = nowUnix,
        }, transaction);
        delta.upserted.Add(new InventoryItemDto
        {
            itemId = newItemId,
            slot = freeSlot,
            itemCode = dropped.ItemCode,
            quantity = dropped.Quantity,
            enhanceLevel = 0,
        });
        return true;
    }
}
