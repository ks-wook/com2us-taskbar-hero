using GameServer.Data;
using SqlKata.Execution;
using TaskbarHero.Common.Dto;

namespace GameServer.Repositories;

/// <summary>오프라인 정산 대상 계정의 기준 시각·파밍 스테이지 좌표(game_player 스냅샷).</summary>
public sealed record OfflinePlayerContext(long LastActiveAt, int Act, int Difficulty, int Stage);

/// <summary>오프라인 정산 트랜잭션 결과 상태.</summary>
public enum OfflineClaimStatus
{
    Ok,
    NoPlayer,       // game_player 없음(세이브 미생성)
    AlreadyClaimed, // 잠금 후 재확인 시 경과가 최소 기준 미만이거나 CAS 실패(동시 중복 요청으로 이미 정산됨)
}

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
    public static OfflineClaimOutcome Fail(OfflineClaimStatus status)
        => new(status, 0, false, 0, 0, 0, new List<OfflineCharacterState>(), 0);
}

public interface IOfflineRepository
{
    /// <summary>정산 기준 시각·파밍 스테이지 좌표를 읽는다(계정 없으면 null).</summary>
    Task<OfflinePlayerContext?> GetContextAsync(long userId);

    /// <summary>
    /// 오프라인 보상을 한 트랜잭션으로 적용한다: 잠금 상태의 last_active_at으로 경과를 재계산하고
    /// (최소 기준 미만이면 AlreadyClaimed) last_active_at을 CAS(관측값 조건부)로 now로 리셋해 정산권을 선점한 뒤,
    /// computeReward로 보상을 산출해 골드 적립·전 캐릭터 경험치 지급·레벨 재계산을 수행한다.
    /// CAS가 0행이면(동시 요청이 먼저 정산) 롤백하고 AlreadyClaimed를 반환한다.
    /// 경험치→레벨 계산은 주입된 applyExp 델리게이트(현재 level·exp + 지급 exp → 반영 후 상태)로 처리한다.
    /// </summary>
    Task<OfflineClaimOutcome> ClaimAsync(
        long userId,
        long nowUnix,
        long minRewardSec,
        Func<long, (long effectiveSec, bool capped, long gold, long exp)> computeReward,
        Func<int, long, long, (int newLevel, long newExp)> applyExp);
}

// ── DB 행 매핑용 POCO(제네릭 매핑 전용, dynamic 금지). snake_case→PascalCase는 Dapper 규칙으로 매핑. ──
file sealed class OfflineContextRow
{
    public long LastActiveAt { get; set; }
    public int Act { get; set; }
    public int Difficulty { get; set; }
    public int Stage { get; set; }
}

file sealed class LastActiveRow
{
    public long LastActiveAt { get; set; }
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

/// <summary>오프라인 보상 정산 세이브 접근 계층(taskbar_hero_game). SqlKata 쿼리 빌더 + 제네릭 매핑만 사용한다(dynamic 금지).</summary>
public sealed class OfflineRepository : IOfflineRepository
{
    private const int RowTypeCurrency = 2;
    private const int GoldItemCode = 1;

    private readonly GameDbFactory _dbFactory;

    public OfflineRepository(GameDbFactory dbFactory) => _dbFactory = dbFactory;

    public async Task<OfflinePlayerContext?> GetContextAsync(long userId)
    {
        using var db = _dbFactory.Create();
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

    public async Task<OfflineClaimOutcome> ClaimAsync(
        long userId,
        long nowUnix,
        long minRewardSec,
        Func<long, (long effectiveSec, bool capped, long gold, long exp)> computeReward,
        Func<int, long, long, (int newLevel, long newExp)> applyExp)
    {
        await using var connection = _dbFactory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var db = _dbFactory.Create(connection);

            // 1) 기준 시각 재확인(트랜잭션 내부). 잠금 상태의 경과가 최소 기준 미만이면 이미 정산된 것으로 간주.
            var playerRow = await db.Query("game_player")
                .Select("last_active_at")
                .Where("user_id", userId)
                .FirstOrDefaultAsync<LastActiveRow>(transaction);

            if (playerRow is null)
            {
                await transaction.RollbackAsync();
                return OfflineClaimOutcome.Fail(OfflineClaimStatus.NoPlayer);
            }

            long observed = playerRow.LastActiveAt;
            long elapsed = Math.Max(0, nowUnix - observed);
            if (elapsed < minRewardSec)
            {
                await transaction.RollbackAsync();
                return OfflineClaimOutcome.Fail(OfflineClaimStatus.AlreadyClaimed);
            }

            // 2) 정산권 선점(CAS): last_active_at이 관측값 그대로일 때만 now로 리셋한다.
            //    동시 요청이 먼저 정산했으면 관측값과 달라 0행 → 롤백(AlreadyClaimed). 중복 지급 방지의 핵심 게이트.
            var claimed = await db.Query("game_player")
                .Where("user_id", userId).Where("last_active_at", observed)
                .UpdateAsync(new { last_active_at = nowUnix, updated_at = nowUnix }, transaction);

            if (claimed == 0)
            {
                await transaction.RollbackAsync();
                return OfflineClaimOutcome.Fail(OfflineClaimStatus.AlreadyClaimed);
            }

            // 3) 보상 산출(서버 권위): 상한 적용 후 골드·경험치.
            var (effectiveSec, capped, gold, exp) = computeReward(elapsed);

            // 4) 골드 적립(재화 행 upsert).
            long goldBalance = await UpsertGoldAsync(db, transaction, userId, gold, nowUnix);

            // 5) 경험치 지급(3캐릭터 동일) + 레벨 재계산.
            var charRows = await db.Query("player_character")
                .Select("character_id", "level", "exp")
                .Where("user_id", userId)
                .OrderBy("character_id")
                .GetAsync<CharProgressRow>(transaction);

            var characters = new List<OfflineCharacterState>();
            foreach (var c in charRows)
            {
                var (newLevel, newExp) = applyExp(c.Level, c.Exp, exp);
                await db.Query("player_character")
                    .Where("user_id", userId).Where("character_id", c.CharacterId)
                    .UpdateAsync(new { level = newLevel, exp = newExp }, transaction);

                characters.Add(new OfflineCharacterState
                {
                    characterId = c.CharacterId,
                    level = newLevel,
                    exp = newExp,
                });
            }

            await transaction.CommitAsync();
            return new OfflineClaimOutcome(
                OfflineClaimStatus.Ok, effectiveSec, capped, gold, exp, goldBalance, characters, nowUnix);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>재화(골드) 행을 upsert하고 갱신 후 잔액을 반환한다(없으면 새 재화 행 생성).</summary>
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

        long newBalance = goldRow.Quantity + gold;
        await db.Query("player_item")
            .Where("player_item_id", goldRow.PlayerItemId)
            .UpdateAsync(new { quantity = newBalance }, transaction);
        return newBalance;
    }
}
