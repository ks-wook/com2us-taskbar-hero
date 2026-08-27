using System.Data.Common;
using GameServer.MasterData;
using GameServer.Models;
using GameServer.Repositories.GameDb.Interfaces;
using SqlKata.Execution;
using TaskbarHero.Common;
using TaskbarHero.Common.Dto;

namespace GameServer.Repositories.GameDb;

/// <summary>game_player 진행도 스냅샷(스테이지 도메인에서 필요한 필드만).</summary>
public sealed record StageProgressRow(int Act, int Difficulty, int Stage, int MaxStageCleared, int InventoryCapacity);

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

    /// <summary>
    /// 이번 클리어가 <b>최고 도달 스테이지를 밀어 올린</b> 첫 클리어인지(재파밍이면 false).
    /// 스테이지별 이탈 지점을 세려면 첫 클리어와 반복 파밍을 갈라야 해서 이벤트 로그에 함께 남긴다(5.3).
    /// </summary>
    public bool IsFirstClear { get; init; }

    /// <summary>
    /// 이번 경험치 지급으로 레벨이 오른 캐릭터들. 성장 곡선 실측용 이벤트 로그(character.levelup)의 원본이며,
    /// 아무도 오르지 않았으면 비어 있다.
    /// </summary>
    public IReadOnlyList<CharacterLevelUp> LevelUps { get; init; } = Array.Empty<CharacterLevelUp>();

    public static ClearOutcome Fail(ClearStatus status)
        => new(status, new List<CharacterProgressDto>(), 0, 0, 0, 1.0m, 1.0m, 0, 0, 0, 0);
}

/// <summary>스테이지 진행/클리어 세이브 접근 계층(taskbar_hero_game). SqlKata 쿼리 빌더 + 제네릭 매핑만 사용한다(dynamic 금지).</summary>
public sealed class StageRepository : GameDbBase, IStageRepository
{
    private readonly ILevelUpCalculator _levelUp;

    /// <summary>세이브 DB 커넥션 팩토리(기반 클래스로 전달)와 레벨업 계산기를 주입받는다.</summary>
    public StageRepository(GameDbFactory dbFactory, ILevelUpCalculator levelUp) : base(dbFactory)
        => _levelUp = levelUp;

    /// <summary>
    /// game_player에서 스테이지 도메인용 진행도 스냅샷(현재 진입 좌표·최고 클리어 시퀀스·인벤토리 용량)을
    /// 단건 조회한다. 트랜잭션 없이 자체 커넥션으로 읽으며, 계정 세이브가 없으면 null.
    /// </summary>
    public async Task<StageProgressRow?> GetProgressAsync(long userId)
    {
        using var db = Db();
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
        using var db = Db();
        return await db.Query("game_player")
            .Where("user_id", userId)
            .UpdateAsync(new { act, difficulty, stage, updated_at = nowUnix });
    }

    /// <summary>
    /// 클리어 판정·보상 지급·진행도 전진을 단일 커넥션의 단일 트랜잭션으로 적용한다.
    /// 검증 실패(NoPlayer·NotEntered)는 <see cref="TxResult{T}.Rollback"/>으로 되돌린 Fail 상태를 반환하고,
    /// 예외는 <see cref="GameDbBase"/>가 롤백 후 전파한다.
    /// </summary>
    /// <remarks>
    /// 한 트랜잭션으로 묶는 작업(하나라도 실패하면 전부 롤백 — 보상만 들어가고 진행도가 안 오르는 부분 반영을 막는다):
    /// <para>1) game_player SELECT — 현재 진입 좌표·max_stage_cleared·inventory_capacity 확보 후 요청 좌표와 일치 검증(불일치 → NotEntered)</para>
    /// <para>2) player_buff SELECT — 활성(<c>expires_at &gt; now</c>) 획득량 버프 배율을 읽어 골드·경험치 지급액 확정(내림).
    ///    <b>지급과 같은 트랜잭션에서 판정</b>해 배율 판정과 지급이 갈라지지 않게 한다(소모품/버프 기획서 6.2)</para>
    /// <para>3) player_item(재화 행) 원자 가산 — 클리어 보상 골드 적립, 갱신 후 잔액 산출</para>
    /// <para>4) player_character SELECT + 캐릭터별 UPDATE — <b>파티에 편성된(slot≠0)</b> 캐릭터에만 동일 경험치 지급 후 levelUp 델리게이트로 레벨 재계산</para>
    /// <para>5) player_item 전리품 적재 — 스택 가능하면 기존 스택 병합, 아니면 빈 칸에 INSERT
    ///    (용량 초과면 롤백하지 않고 전리품만 폐기 → LootStored=false, 골드·경험치·진행도는 그대로 반영)</para>
    /// <para>6) game_player 진행도 UPDATE — 프런티어 클리어면 max_stage_cleared 갱신 + 다음 스테이지로 전진, 재파밍이면 updated_at만 갱신</para>
    /// ⚠️ 원자성은 보장하지만 game_player 행에 잠금(FOR UPDATE 등)을 걸지 않으므로, 동일 userId의 동시 요청은
    ///    1)의 검증을 함께 통과할 수 있다(중복 전리품 지급·경험치 lost update·슬롯 유니크 충돌). 백로그 과제.
    ///    <b>골드는 여기서 빠진다</b> — 3)이 <c>quantity = quantity + N</c> 원자 가산이라 읽은 값을 덮어쓰지 않는다.
    /// </remarks>
    public async Task<ClearOutcome> ApplyClearAsync(
        long userId,
        int expectedAct, int expectedDifficulty, int expectedStage,
        long baseGold, long baseExp, DroppedItem? dropped,
        long nowUnix)
        => await TransactionAsync<ClearOutcome>(async (db, transaction) =>
        {
            // 1) 현재 진입 스테이지 재검증(트랜잭션 내부에서 원자적으로).
            var player = await db.Query("game_player")
                .Select("act", "difficulty", "stage", "max_stage_cleared", "inventory_capacity")
                .Where("user_id", userId)
                .FirstOrDefaultAsync<PlayerProgressRow>(transaction);

            if (player is null)
            {
                return TxResult<ClearOutcome>.Rollback(ClearOutcome.Fail(ClearStatus.NoPlayer));
            }

            int curAct = player.Act;
            int curDiff = player.Difficulty;
            int curStage = player.Stage;
            int maxCleared = player.MaxStageCleared;
            int capacity = player.InventoryCapacity;

            if (curAct != expectedAct || curDiff != expectedDifficulty || curStage != expectedStage)
            {
                return TxResult<ClearOutcome>.Rollback(ClearOutcome.Fail(ClearStatus.NotEntered));
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

            // 3) 골드 지급(재화 행 원자 가산).
            long goldBalance = await CreditCurrencyAsync(
                db, transaction, userId, Constants.Currency.GoldItemCode, gold);

            // 4) 경험치 지급(파티 편성 캐릭터 동일) + 레벨 재계산.
            //    미편성(slot=0) 캐릭터는 전투에 나가지 않았으므로 경험치를 받지 않는다(세이브 데이터 기획서 5.5).
            //    **잠금 조회**로 읽는다 — 레벨·경험치가 계산의 입력이라 최신값이어야 하고, 잠금 없는 조회는
            //    트랜잭션이 처음 읽은 시점의 스냅샷을 계속 보기 때문이다. slot 순으로 잠가 순서를 고정한다.
            var charRows = await db.SelectAsync<CharProgressRow>(
                """
                SELECT character_id, level, exp, class_code FROM player_character
                 WHERE user_id = @userId AND slot <> @unassigned ORDER BY slot FOR UPDATE
                """,
                new { userId, unassigned = Constants.Party.SlotUnassigned }, transaction);

            var characters = new List<CharacterProgressDto>();
            var levelUps = new List<CharacterLevelUp>();
            foreach (var c in charRows)
            {
                int characterId = c.CharacterId;

                var (newLevel, newExp, leveledUp) = _levelUp.Calculate(c.Level, c.Exp, exp);

                // 읽은 레벨·경험치가 그대로일 때만 쓴다. 이 조건이 없으면 같은 계정의 클리어 둘이 겹칠 때
                // 양쪽이 같은 값을 읽고 각자 계산한 값을 덮어써 한쪽 경험치가 사라진다.
                // 지급액이 0이면 바뀔 값이 없으므로 갱신 자체를 건너뛴다(경합으로 오인하지 않게).
                if (newLevel != c.Level || newExp != c.Exp)
                {
                    var applied = await db.Query("player_character")
                        .Where("user_id", userId).Where("character_id", characterId)
                        .Where("level", c.Level).Where("exp", c.Exp)
                        .UpdateAsync(new { level = newLevel, exp = newExp }, transaction);
                    if (applied == 0)
                    {
                        throw new ConcurrencyConflictException("캐릭터 경험치");
                    }
                }

                characters.Add(new CharacterProgressDto
                {
                    characterId = characterId,
                    level = newLevel,
                    exp = newExp,
                    isLevelUp = leveledUp,
                });

                // 오른 캐릭터만 담는다 — 이벤트 로그는 "레벨이 올랐다"는 사건 자체가 1행이다(5.3).
                if (leveledUp)
                {
                    levelUps.Add(new CharacterLevelUp(characterId, c.ClassCode, c.Level, newLevel));
                }
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

                // 관측한 max_stage_cleared 그대로일 때만 전진한다. 이 조건이 없으면 같은 프런티어 스테이지의
                // 클리어 둘이 겹칠 때 양쪽이 모두 "내가 프런티어다"라고 판단해 한 스테이지로 두 번 전진하고,
                // 보상과 전리품도 두 번 나간다.
                var advanced = await db.Query("game_player")
                    .Where("user_id", userId)
                    .Where("max_stage_cleared", maxCleared)
                    .UpdateAsync(new
                    {
                        act = newAct,
                        difficulty = newDiff,
                        stage = newStage,
                        max_stage_cleared = newMax,
                        updated_at = nowUnix,
                    }, transaction);
                if (advanced == 0)
                {
                    throw new ConcurrencyConflictException("스테이지 진행도");
                }
            }
            else
            {
                // 재파밍: 진행도 유지, 갱신 시각만 반영.
                await db.Query("game_player")
                    .Where("user_id", userId)
                    .UpdateAsync(new { updated_at = nowUnix }, transaction);
            }

            return TxResult<ClearOutcome>.Commit(new ClearOutcome(
                ClearStatus.Ok, characters, goldBalance,
                gold, exp, goldMultiplier, expMultiplier,
                newAct, newDiff, newStage, newMax)
            {
                IsFirstClear = isFrontier,
                LevelUps = levelUps,
                Delta = delta,
                LootStored = lootStored,
            });
        });

    /// <summary>활성 버프 배율 목록에서 지정 종류의 획득량 배율을 얻는다. 해당 종류의 활성 버프가 없으면 1.0(배율 없음).</summary>
    private static decimal MultiplierOf(IReadOnlyDictionary<int, decimal> multipliers, BuffType buffType)
        => multipliers.TryGetValue((int)buffType, out var value) ? value : 1.0m;

    /// <summary>기본 보상에 획득량 배율을 곱해 지급액을 확정한다(기획서 6.2: 정수 내림). 배율이 1.0이면 원값 그대로.</summary>
    private static long ApplyMultiplier(long baseAmount, decimal multiplier)
        => multiplier == 1.0m ? baseAmount : (long)decimal.Floor(baseAmount * multiplier);

    /// <summary>전리품 1개를 인벤토리에 적재한다. 재료는 기존 스택에 합치고, 새 칸이 필요하면 용량을 확인한다.
    /// 용량 초과로 적재 실패하면 false.</summary>
    private static async Task<bool> StoreDroppedItemAsync(
        QueryFactory db, System.Data.Common.DbTransaction transaction, long userId, DroppedItem dropped, int capacity, long nowUnix,
        InventoryDeltaDto delta)
    {
        // 재료(스택 가능): 여유 있는 기존 스택에 합친다(새 칸 불필요).
        if (dropped.StackMax > 1)
        {
            // **잠금 조회** — 합칠 스택의 수량이 계산의 입력이라 최신값이어야 한다.
            var stackRow = (await db.SelectAsync<ItemIdQtySlotRow>(
                """
                SELECT player_item_id, quantity, slot FROM player_item
                 WHERE user_id = @userId AND row_type = @rowType AND item_code = @itemCode
                   AND quantity < @stackMax
                 ORDER BY player_item_id LIMIT 1 FOR UPDATE
                """,
                new { userId, rowType = Constants.PlayerItemRow.Item, itemCode = dropped.ItemCode, stackMax = dropped.StackMax },
                transaction)).FirstOrDefault();

            if (stackRow is not null)
            {
                long stackRowId = stackRow.PlayerItemId;
                long merged = stackRow.Quantity + dropped.Quantity;

                // 읽은 수량 그대로일 때만 합친다(같은 스택에 두 요청이 동시에 합치면 한쪽이 사라진다).
                var mergedRows = await db.Query("player_item")
                    .Where("player_item_id", stackRowId).Where("quantity", stackRow.Quantity)
                    .UpdateAsync(new { quantity = merged }, transaction);
                if (mergedRows == 0)
                {
                    throw new ConcurrencyConflictException("아이템 스택");
                }
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
            row_type = Constants.PlayerItemRow.Item,
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

    /// <summary>
    /// 재화 행(<c>player_item</c>, <c>row_type=2</c>)의 <c>player_item_id</c>를 찾는다(없으면 null).
    /// 잔액이 아니라 <b>행 번호</b>만 읽으므로 이 조회는 낡지 않는다 — 재화 행은 세이브를 만들 때 함께 만들어지고
    /// 이후 지워지지 않아 번호가 고정이다(<c>SaveRepository.CreatePlayerWithFirstCharacterAsync</c>).
    /// </summary>
    private static async Task<long?> FindCurrencyRowIdAsync(
        QueryFactory db, DbTransaction tx, long userId, int currencyCode)
        => await db.Query("player_item").Select("player_item_id")
            .Where("user_id", userId)
            .Where("row_type", Constants.PlayerItemRow.Currency)
            .Where("item_code", currencyCode)
            .FirstOrDefaultAsync<long?>(tx);

    /// <summary>갱신 직후 잔액을 기본키로 읽는다. 자기 트랜잭션이 그 행에 X락을 쥔 상태라 방금 확정한 값이 보인다.</summary>
    private static async Task<long> ReadCurrencyBalanceAsync(QueryFactory db, DbTransaction tx, long rowId)
        => await db.Query("player_item").Select("quantity")
            .Where("player_item_id", rowId)
            .FirstOrDefaultAsync<long?>(tx) ?? 0;

    /// <summary>
    /// 재화를 <paramref name="amount"/>만큼 적립하고 적립 후 잔액을 돌려준다.
    /// <para><b>읽어서 계산한 값을 쓰지 않는다.</b> <c>quantity = quantity + @amount</c>로 DB가 직접 계산하게 해,
    /// 같은 계정에 지급 둘이 동시에 들어와도 한쪽 적립이 다른 쪽에 덮이지 않게 한다. 갱신은 그 계정의 아이템 행까지
    /// 훑어 잠그지 않도록 <b>기본키</b>로 건다.</para>
    /// <para><b>재화 행이 없으면 만들지 않고 예외로 알린다.</b> 재화 행은 세이브를 만들 때 잔액 0으로 함께 생성하고
    /// 어디서도 지우지 않으므로, 여기서 없다는 것은 그 규칙이 깨졌다는 뜻이다. 이때 새로 만들면 잔액 0인 계정에
    /// 지급 둘이 동시에 들어올 때 양쪽이 모두 "행 없음"을 보고 INSERT해 재화 행이 둘로 갈라지고, 그 뒤로는 조회가
    /// 한 행만 읽어 나머지 잔액이 보이지 않게 된다.</para>
    /// </summary>
    private static async Task<long> CreditCurrencyAsync(
        QueryFactory db, DbTransaction tx, long userId, int currencyCode, long amount)
    {
        var rowId = await FindCurrencyRowIdAsync(db, tx, userId, currencyCode);

        // 지급액 0(보상이 아이템뿐인 경로)은 갱신할 것이 없다.
        if (amount <= 0)
        {
            return rowId is null ? 0 : await ReadCurrencyBalanceAsync(db, tx, rowId.Value);
        }

        if (rowId is null)
        {
            throw new CurrencyRowMissingException(userId, currencyCode);
        }

        await db.StatementAsync(
            """
            UPDATE player_item SET quantity = quantity + @amount WHERE player_item_id = @rowId
            """,
            new { rowId, amount }, tx);

        return await ReadCurrencyBalanceAsync(db, tx, rowId.Value);
    }
}
