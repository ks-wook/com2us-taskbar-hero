using GameServer.Data;
using MySqlConnector;
using SqlKata.Execution;

namespace GameServer.Repositories;

public enum ConsumableUseStatus
{
    Ok,
    ItemNotFound,          // 대상 행이 계정에 없음(또는 재화 행)
    NotConsumable,         // item_type이 소모품(4)이 아님
    InsufficientQuantity,  // 보유 수량 0
    MasterNotDefined,      // consumable_master에 효과 정의가 없음
    DurationLimitExceeded, // 누적 지속시간 상한 초과
}

/// <summary>버프 1건(활성/만료 무관). BuffValue는 획득량 배율, 시각은 Unix ts(초).</summary>
public sealed record PlayerBuffRow(int BuffType, float BuffValue, long StartedAt, long ExpiresAt);

/// <summary>
/// 사용 대상 소모품의 마스터 판정 결과(서비스 델리게이트 반환). 리포지토리는 DB에서 읽은 item_code만 넘기고,
/// 마스터 조회(소모품 여부·배율·지속시간)와 상한 검사는 서비스가 수행한다.
/// </summary>
public sealed record ConsumableDecision(ConsumableUseStatus Status, int BuffType, float BuffValue, int DurationSec)
{
    public static ConsumableDecision Reject(ConsumableUseStatus status) => new(status, 0, 0f, 0);
    public static ConsumableDecision Accept(int buffType, float buffValue, int durationSec)
        => new(ConsumableUseStatus.Ok, buffType, buffValue, durationSec);
}

/// <summary>
/// 소모품 사용 트랜잭션 결과. Ok면 차감 후 남은 수량과 갱신된 버프·계정 활성 버프 전체를 담는다.
/// </summary>
public sealed record ConsumableUseOutcome(
    ConsumableUseStatus Status,
    int ItemCode,
    long RemainingQuantity,
    PlayerBuffRow? Buff,
    IReadOnlyList<PlayerBuffRow> ActiveBuffs)
{
    public static ConsumableUseOutcome Fail(ConsumableUseStatus status)
        => new(status, 0, 0, null, Array.Empty<PlayerBuffRow>());
}

public interface IConsumableRepository
{
    /// <summary>
    /// 소모품 1개 사용을 한 트랜잭션으로 적용한다: 대상 행 소유·수량 확인 → decide(마스터 검증: 소모품 여부·배율·
    /// 지속시간·누적 상한) → 아이템 1개 차감(0이면 행 삭제) + player_buff upsert. plan은
    /// (기존 버프, 지속시간) → (새 started_at, 새 expires_at, 상한 초과 여부)를 산출한다.
    /// </summary>
    Task<ConsumableUseOutcome> ApplyUseAsync(
        long userId, long itemId,
        Func<int, ConsumableDecision> decide,
        Func<PlayerBuffRow?, int, (long startedAt, long expiresAt, bool overLimit)> plan,
        long nowUnix);

    /// <summary>계정의 활성 버프(expires_at > now)를 조회한다. 코어 세이브 로드의 activeBuffs 항목이 사용한다.</summary>
    Task<List<PlayerBuffRow>> GetActiveBuffsAsync(long userId, long nowUnix);
}

// ── DB 행 매핑용 POCO(제네릭 매핑 전용, dynamic 금지). snake_case→PascalCase는 Dapper 규칙으로 매핑. ──
file sealed class PlayerItemRow
{
    public long PlayerItemId { get; set; }
    public int ItemCode { get; set; }
    public int RowType { get; set; }
    public long Quantity { get; set; }
}

file sealed class BuffRow
{
    public int BuffType { get; set; }
    public decimal BuffValue { get; set; } // DECIMAL(5,3) → decimal로 받아 float로 캐스팅
    public long StartedAt { get; set; }
    public long ExpiresAt { get; set; }
}

/// <summary>행 매핑 POCO → 공개 레코드 변환. 파일 로컬 타입은 공개 멤버 시그니처에 못 쓰므로 별도 파일 로컬 클래스에 둔다.</summary>
file static class BuffRowMapper
{
    /// <summary>DB 행 → 버프 레코드. DECIMAL(5,3)인 buff_value를 float로 캐스팅한다. 행이 없으면 null.</summary>
    public static PlayerBuffRow? ToBuff(this BuffRow? row)
        => row is null ? null : new PlayerBuffRow(row.BuffType, (float)row.BuffValue, row.StartedAt, row.ExpiresAt);
}

/// <summary>
/// 소모품 사용·버프 조회 세이브 접근 계층(taskbar_hero_game). SqlKata 쿼리 빌더 + 제네릭 매핑만 사용한다(dynamic 금지).
/// </summary>
public sealed class ConsumableRepository : IConsumableRepository
{
    private const int RowTypeItem = 1;
    private const int ItemTypeConsumable = 4;

    private readonly GameDbFactory _dbFactory;

    /// <summary>세이브 DB 커넥션 팩토리를 주입받는다.</summary>
    public ConsumableRepository(GameDbFactory dbFactory) => _dbFactory = dbFactory;

    /// <summary>
    /// 소모품 사용(아이템 1개 차감 → 버프 부여·연장)을 단일 커넥션의 단일 트랜잭션으로 적용한다.
    /// 검증 실패는 즉시 롤백 후 Fail 상태로 반환하고, 예외는 롤백 후 전파한다.
    /// </summary>
    /// <remarks>
    /// 한 트랜잭션으로 묶는 작업(하나라도 실패하면 전부 롤백 — 아이템만 사라지거나 버프만 생기는 상태를 막는다):
    /// <para>1) player_item SELECT — 대상 행 소유·아이템 행 여부·수량 확인</para>
    /// <para>2) decide 델리게이트 — 마스터 검증(item_type=4·consumable_master 정의)과 배율·지속시간 산출(DB 접근 없음)</para>
    /// <para>3) player_buff SELECT — 같은 종류의 기존 버프 조회(연장 기준)</para>
    /// <para>4) plan 델리게이트 — 새 started_at·expires_at 산출 및 누적 상한 검사(초과면 <b>차감 전에</b> 거부)</para>
    /// <para>5) player_item 조건부 UPDATE/DELETE — 수량 1 차감(0이 되면 행 삭제 = 가방 칸 반납)</para>
    /// <para>6) player_buff INSERT/UPDATE — 버프 부여 또는 연장</para>
    /// <para>7) player_buff SELECT — 갱신 후 계정 활성 버프 전체(응답 activeBuffs)</para>
    /// <para><b>이중 차감 방지</b>는 5단계의 <b>조건부 갱신</b>이 담당한다 — 조회 시점 수량을 WHERE에 넣어(CAS)
    /// 영향 행이 0이면 다른 요청이 먼저 소모한 것으로 보고 롤백한다. 계정당 단일 세션 정책이라 경합 자체가 제한적이며,
    /// 행 잠금(FOR UPDATE)은 다른 리포지토리와 동일하게 쓰지 않는다.</para>
    /// </remarks>
    public async Task<ConsumableUseOutcome> ApplyUseAsync(
        long userId, long itemId,
        Func<int, ConsumableDecision> decide,
        Func<PlayerBuffRow?, int, (long startedAt, long expiresAt, bool overLimit)> plan,
        long nowUnix)
    {
        await using var connection = _dbFactory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var db = _dbFactory.Create(connection);

            // 1) 대상 아이템 행 조회(소유·타입·수량).
            var item = await db.Query("player_item")
                .Select("player_item_id", "item_code", "row_type", "quantity")
                .Where("user_id", userId).Where("player_item_id", itemId)
                .FirstOrDefaultAsync<PlayerItemRow>(transaction);

            if (item is null || item.RowType != RowTypeItem)
            {
                await transaction.RollbackAsync();
                return ConsumableUseOutcome.Fail(ConsumableUseStatus.ItemNotFound);
            }

            if (item.Quantity < 1)
            {
                await transaction.RollbackAsync();
                return ConsumableUseOutcome.Fail(ConsumableUseStatus.InsufficientQuantity);
            }

            // 2) 마스터 검증(소모품 여부·효과 정의) — 서비스 델리게이트.
            var decision = decide(item.ItemCode);
            if (decision.Status != ConsumableUseStatus.Ok)
            {
                await transaction.RollbackAsync();
                return ConsumableUseOutcome.Fail(decision.Status);
            }

            // 3) 같은 종류의 기존 버프를 조회한다(있으면 연장 기준, 없으면 신규 부여).
            var prev = await db.Query("player_buff")
                .Select("buff_type", "buff_value", "started_at", "expires_at")
                .Where("user_id", userId).Where("buff_type", decision.BuffType)
                .FirstOrDefaultAsync<BuffRow>(transaction);
            var prevBuff = prev.ToBuff();

            // 4) 새 구간 산출 + 누적 상한 검사. 초과면 아이템을 차감하지 않는다.
            var (startedAt, expiresAt, overLimit) = plan(prevBuff, decision.DurationSec);
            if (overLimit)
            {
                await transaction.RollbackAsync();
                return ConsumableUseOutcome.Fail(ConsumableUseStatus.DurationLimitExceeded);
            }

            // 5) 아이템 1개 차감(0이면 행 삭제 → 가방 칸 반납).
            //    조회 시점 수량을 WHERE에 넣는 조건부 갱신(CAS)이라, 그 사이 다른 요청이 소모했으면 영향 행이 0이 되어
            //    이중 차감 없이 거부된다.
            var remaining = item.Quantity - 1;
            var affected = remaining <= 0
                ? await db.Query("player_item")
                    .Where("player_item_id", itemId).Where("user_id", userId).Where("quantity", item.Quantity)
                    .DeleteAsync(transaction)
                : await db.Query("player_item")
                    .Where("player_item_id", itemId).Where("user_id", userId).Where("quantity", item.Quantity)
                    .UpdateAsync(new { quantity = remaining }, transaction);

            if (affected == 0)
            {
                await transaction.RollbackAsync();
                return ConsumableUseOutcome.Fail(ConsumableUseStatus.ItemNotFound);
            }

            // 6) 버프 부여(신규) 또는 연장(기존 행 갱신).
            if (prevBuff is null)
            {
                await db.Query("player_buff").InsertAsync(new
                {
                    user_id = userId,
                    buff_type = decision.BuffType,
                    buff_value = (decimal)decision.BuffValue,
                    started_at = startedAt,
                    expires_at = expiresAt,
                }, transaction);
            }
            else
            {
                await db.Query("player_buff")
                    .Where("user_id", userId).Where("buff_type", decision.BuffType)
                    .UpdateAsync(new
                    {
                        buff_value = (decimal)decision.BuffValue,
                        started_at = startedAt,
                        expires_at = expiresAt,
                    }, transaction);
            }

            // 7) 갱신 후 계정 활성 버프 전체(응답용). 커밋 전 같은 트랜잭션에서 읽어 방금 반영분을 포함시킨다.
            var activeRows = await db.Query("player_buff")
                .Select("buff_type", "buff_value", "started_at", "expires_at")
                .Where("user_id", userId).Where("expires_at", ">", nowUnix)
                .OrderBy("buff_type")
                .GetAsync<BuffRow>(transaction);

            await transaction.CommitAsync();

            var buff = new PlayerBuffRow(decision.BuffType, decision.BuffValue, startedAt, expiresAt);
            var active = activeRows.Select(r => r.ToBuff()!).ToList();
            return new ConsumableUseOutcome(ConsumableUseStatus.Ok, item.ItemCode, remaining, buff, active);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>계정의 활성 버프(expires_at > now)를 buff_type 순으로 조회한다. 만료 행은 제외한다.</summary>
    public async Task<List<PlayerBuffRow>> GetActiveBuffsAsync(long userId, long nowUnix)
    {
        using var db = _dbFactory.Create();
        var rows = await db.Query("player_buff")
            .Select("buff_type", "buff_value", "started_at", "expires_at")
            .Where("user_id", userId).Where("expires_at", ">", nowUnix)
            .OrderBy("buff_type")
            .GetAsync<BuffRow>();

        return rows.Select(r => r.ToBuff()!).ToList();
    }
}
