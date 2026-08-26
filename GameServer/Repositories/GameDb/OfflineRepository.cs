using GameServer.MasterData;
using GameServer.Models;
using GameServer.Repositories.GameDb.Interfaces;
using SqlKata.Execution;
using TaskbarHero.Common.Dto;
using GameServer.Util;

namespace GameServer.Repositories.GameDb;

/// <summary>오프라인 정산 대상 계정의 기준 시각·파밍 스테이지 좌표(game_player 스냅샷).</summary>
public sealed record OfflinePlayerContext(long LastActiveAt, int Act, int Difficulty, int Stage);

/// <summary>오프라인 정산 트랜잭션 결과.</summary>
public sealed record OfflineClaimOutcome(
    OfflineClaimStatus Status,
    long EffectiveSec,
    bool Capped,
    long Gold,
    long Exp,
    long GoldBalance,
    List<OfflineCharacterState> Characters,
    long LastActiveAt)
{
    /// <summary>
    /// 이번 정산 경험치로 레벨이 오른 캐릭터들. 스테이지 클리어와 같은 <c>character.levelup</c> 이벤트로 나가며
    /// <c>source</c>만 <c>offline</c>으로 갈린다(로그 이벤트 정의 5.3).
    /// </summary>
    public IReadOnlyList<CharacterLevelUp> LevelUps { get; init; } = Array.Empty<CharacterLevelUp>();

    public static OfflineClaimOutcome Fail(OfflineClaimStatus status)
        => new(status, 0, false, 0, 0, 0, new List<OfflineCharacterState>(), 0);
}

/// <summary>오프라인 보상 정산 세이브 접근 계층(taskbar_hero_game). SqlKata 쿼리 빌더 + 제네릭 매핑만 사용한다(dynamic 금지).</summary>
public sealed class OfflineRepository : GameDbBase, IOfflineRepository
{
    private readonly ILevelUpCalculator _levelUp;

    /// <summary>세이브 DB 커넥션 팩토리(기반 클래스로 전달)와 레벨업 계산기를 주입받는다.</summary>
    public OfflineRepository(GameDbFactory dbFactory, ILevelUpCalculator levelUp) : base(dbFactory)
        => _levelUp = levelUp;

    /// <summary>
    /// game_player에서 정산 기준 시각(last_active_at)과 파밍 스테이지 좌표를 단건 조회한다.
    /// 트랜잭션 없이 자체 커넥션으로 읽는 사전 조회이며(실제 정산 판정은 ClaimAsync가 트랜잭션 내부에서 재확인),
    /// 계정 세이브가 없으면 null.
    /// </summary>
    public async Task<OfflinePlayerContext?> GetContextAsync(long userId)
    {
        using var db = Db();
        var row = await db.Query("game_player")
            .Select("last_active_at", "act", "difficulty", "stage")
            .Where("user_id", userId)
            .FirstOrDefaultAsync<OfflineContextRow>();

        if (row is null)
        {
            return null;
        }

        return new OfflinePlayerContext(row.LastActiveAt, row.Act, row.Difficulty, row.Stage);
    }

    /// <summary>
    /// 오프라인(방치) 보상 정산을 단일 커넥션의 단일 트랜잭션으로 적용한다.
    /// 계정이 없으면 NoPlayer, 경과가 최소 기준 미만이거나 CAS 선점에 실패하면 AlreadyClaimed를 롤백 후 반환하며,
    /// 예외는 롤백 후 전파한다.
    /// </summary>
    /// <remarks>
    /// 한 트랜잭션으로 묶는 작업(하나라도 실패하면 전부 롤백 — 기준 시각만 리셋되고 보상이 안 들어가는 상태를 막는다):
    /// <para>1) game_player SELECT — 트랜잭션 내부에서 last_active_at을 재확인해 경과 시간 재계산(최소 기준 미만 → AlreadyClaimed)</para>
    /// <para>2) game_player CAS UPDATE — last_active_at이 1)에서 관측한 값 그대로일 때만 now로 리셋해 정산권을 선점.
    ///     0행이면 동시 요청이 먼저 정산한 것이므로 롤백(AlreadyClaimed). 중복 지급을 막는 핵심 게이트다.</para>
    /// <para>3) computeReward 델리게이트 — 경과 시간에 상한을 적용해 골드·경험치 산출(서버 권위, DB 접근 없음)</para>
    /// <para>4) player_item(재화 행) upsert — 정산 골드 적립, 갱신 후 잔액 산출</para>
    /// <para>5) player_character SELECT + 캐릭터별 UPDATE — <b>파티에 편성된(slot≠0)</b> 캐릭터에만 동일 경험치 지급 후 applyExp 델리게이트로 레벨 재계산</para>
    /// </remarks>
    public async Task<OfflineClaimOutcome> ClaimAsync(
        long userId,
        long nowUnix,
        long minRewardSec,
        Func<long, (long effectiveSec, bool capped, long gold, long exp)> computeReward)
        => await TransactionAsync<OfflineClaimOutcome>(async (db, transaction) =>
        {
            // 1) 기준 시각 재확인(트랜잭션 내부). 잠금 상태의 경과가 최소 기준 미만이면 이미 정산된 것으로 간주.
            var playerRow = await db.Query("game_player")
                .Select("last_active_at")
                .Where("user_id", userId)
                .FirstOrDefaultAsync<LastActiveRow>(transaction);

            if (playerRow is null)
            {
                return TxResult<OfflineClaimOutcome>.Rollback(OfflineClaimOutcome.Fail(OfflineClaimStatus.NoPlayer));
            }

            long observed = playerRow.LastActiveAt;
            long elapsed = DateTimeUtil.ElapsedSeconds(observed, nowUnix);
            if (elapsed < minRewardSec)
            {
                return TxResult<OfflineClaimOutcome>.Rollback(OfflineClaimOutcome.Fail(OfflineClaimStatus.AlreadyClaimed));
            }

            // 2) 정산권 선점(CAS): last_active_at이 관측값 그대로일 때만 now로 리셋한다.
            //    동시 요청이 먼저 정산했으면 관측값과 달라 0행 → 롤백(AlreadyClaimed). 중복 지급 방지의 핵심 게이트.
            var claimed = await db.Query("game_player")
                .Where("user_id", userId).Where("last_active_at", observed)
                .UpdateAsync(new { last_active_at = nowUnix, updated_at = nowUnix }, transaction);

            if (claimed == 0)
            {
                return TxResult<OfflineClaimOutcome>.Rollback(OfflineClaimOutcome.Fail(OfflineClaimStatus.AlreadyClaimed));
            }

            // 3) 보상 산출(서버 권위): 상한 적용 후 골드·경험치.
            var (effectiveSec, capped, gold, exp) = computeReward(elapsed);

            // 4) 골드 적립(재화 행 upsert).
            long goldBalance = await UpsertGoldAsync(db, transaction, userId, gold, nowUnix);

            // 5) 경험치 지급(파티 편성 캐릭터 동일) + 레벨 재계산.
            //    미편성(slot=0) 캐릭터는 방치 전투에 참가하지 않았으므로 경험치를 받지 않는다(세이브 데이터 기획서 5.5).
            var charRows = await db.Query("player_character")
                .Select("character_id", "level", "exp", "class_code")
                .Where("user_id", userId).Where("slot", "!=", Constants.Party.SlotUnassigned)
                .OrderBy("slot")
                .GetAsync<CharProgressRow>(transaction);

            var characters = new List<OfflineCharacterState>();
            var levelUps = new List<CharacterLevelUp>();
            foreach (var c in charRows)
            {
                var (newLevel, newExp, leveledUp) = _levelUp.Calculate(c.Level, c.Exp, exp);
                await db.Query("player_character")
                    .Where("user_id", userId).Where("character_id", c.CharacterId)
                    .UpdateAsync(new { level = newLevel, exp = newExp }, transaction);

                characters.Add(new OfflineCharacterState
                {
                    characterId = c.CharacterId,
                    level = newLevel,
                    exp = newExp,
                });

                // 오른 캐릭터만 담는다 — 이벤트 로그는 "레벨이 올랐다"는 사건 자체가 1행이다(5.3).
                if (leveledUp)
                {
                    levelUps.Add(new CharacterLevelUp(c.CharacterId, c.ClassCode, c.Level, newLevel));
                }
            }

            return TxResult<OfflineClaimOutcome>.Commit(new OfflineClaimOutcome(
                OfflineClaimStatus.Ok, effectiveSec, capped, gold, exp, goldBalance, characters, nowUnix)
            {
                LevelUps = levelUps,
            });
        });

    /// <summary>재화(골드) 행을 upsert하고 갱신 후 잔액을 반환한다(없으면 새 재화 행 생성).</summary>
    private static async Task<long> UpsertGoldAsync(
        QueryFactory db, System.Data.Common.DbTransaction transaction, long userId, long gold, long nowUnix)
    {
        var goldRow = await db.Query("player_item")
            .Select("player_item_id", "quantity")
            .Where("user_id", userId)
            .Where("row_type", Constants.PlayerItemRow.Currency)
            .Where("item_code", Constants.Currency.GoldItemCode)
            .FirstOrDefaultAsync<ItemIdQtyRow>(transaction);

        if (goldRow is null)
        {
            await db.Query("player_item").InsertAsync(new
            {
                user_id = userId,
                row_type = Constants.PlayerItemRow.Currency,
                item_code = Constants.Currency.GoldItemCode,
                quantity = gold,
                slot = (int?)null,
                enhance_level = 0,
                acquired_at = nowUnix,
            }, transaction);
            return gold;
        }

        long newBalance = goldRow.Quantity + gold;
        await db.Query("player_item")
            .Where("player_item_id", goldRow.PlayerItemId)
            .UpdateAsync(new { quantity = newBalance }, transaction);
        return newBalance;
    }
}
