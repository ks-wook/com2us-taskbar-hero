using System.Data.Common;
using GameServer.Data;
using SqlKata.Execution;

namespace GameServer.Repositories;

// ── 우편함 조회 ──

/// <summary>메일 첨부 1건(원장 스냅샷). RewardType 1:골드 2:아이템 3:재료, RewardCode는 골드면 0.</summary>
/// <summary>메일 첨부 1건. EnhanceLevel은 장비의 강화 단계로, 거래소 구매·만료 반송처럼
/// 강화 상태를 그대로 옮겨야 하는 발급 경로에서 사용한다(골드·재료는 0).</summary>
public sealed record MailAttachment(int RewardType, int RewardCode, long Quantity, int EnhanceLevel = 0);

/// <summary>우편함 메일 1건 + 첨부 목록(조회 시점 스냅샷).</summary>
public sealed record MailSummary(
    long MailId, int Category, string Title, string Body,
    int IsRead, int Claimed, long CreatedAt, long ExpiresAt,
    IReadOnlyList<MailAttachment> Attachments);

// ── 발급(issue) ──

/// <summary>
/// 발급할 메일 1건의 초안(mail 기획서 §6.4 발급 규약). 제목·본문·category·만료는 mail_master 템플릿이 확정한 값이며
/// (발급자가 임의 문자열을 만들지 않음), player_mail에 스냅샷으로 저장된다. Rewards = 첨부(0~N건, seq 순).
/// </summary>
public sealed record MailDraft(
    int Category, string Title, string Body, long ExpiresAt, IReadOnlyList<MailAttachment> Rewards);

// ── 수령(claim) ──

public enum MailClaimStatus
{
    Ok,
    MailNotFound,       // 메일 없음 또는 타인 메일(존재 노출 안 함)
    MailAlreadyClaimed, // 이미 수령(동시 요청 경합 포함)
    MailExpired,        // 만료되어 수령 불가
    InventoryFull,      // 첨부 아이템 적재 용량 부족
}

/// <summary>단건 수령 트랜잭션 결과. Gold = 지급 골드 합, Items = 지급 아이템(코드별 합산), GoldBalance = 지급 후 잔액.</summary>
public sealed record MailClaimOutcome(
    MailClaimStatus Status, long Gold, IReadOnlyList<MailAttachment> Items, long GoldBalance)
{
    public static MailClaimOutcome Fail(MailClaimStatus status)
        => new(status, 0, Array.Empty<MailAttachment>(), 0);
}

/// <summary>일괄 수령 트랜잭션 결과. ClaimedMailIds = 이번에 수령된 메일, Gold·Items = 첨부 합계, GoldBalance = 지급 후 잔액.</summary>
public sealed record MailClaimAllOutcome(
    MailClaimStatus Status, IReadOnlyList<long> ClaimedMailIds, long Gold,
    IReadOnlyList<MailAttachment> Items, long GoldBalance)
{
    public static MailClaimAllOutcome Fail(MailClaimStatus status)
        => new(status, Array.Empty<long>(), 0, Array.Empty<MailAttachment>(), 0);
}

public interface IMailRepository
{
    /// <summary>
    /// 우편함 전체(첨부 포함)를 조회하고, 미열람 메일을 읽음 처리한다(조회 = 열람, mail 기획서 §8 확정).
    /// 반환 스냅샷의 IsRead는 조회 시점 값이므로 클라이언트가 신규 메일 표시에 쓸 수 있다.
    /// </summary>
    Task<IReadOnlyList<MailSummary>> GetMailboxAndMarkReadAsync(long userId);

    /// <summary>
    /// 단건 수령을 한 트랜잭션으로 적용한다: 소유·미수령·미만료 검증 → 조건부 갱신(claimed 0→1)으로 수령권 선점 →
    /// 첨부 지급(골드 적립·아이템 적재). itemLookup은 item_code → (itemType, stackMax) 마스터 조회 델리게이트.
    /// </summary>
    Task<MailClaimOutcome> ApplyClaimAsync(
        long userId, long mailId, Func<int, (int itemType, int stackMax)> itemLookup, long nowUnix);

    /// <summary>
    /// 일괄 수령을 한 트랜잭션으로 적용한다: 미수령·미만료 메일 전건을 6.1과 같은 규칙으로 수령한다.
    /// 용량 초과 시 전체 롤백(InventoryFull) — 부분 수령하지 않는다(mail 기획서 §8 확정).
    /// </summary>
    Task<MailClaimAllOutcome> ApplyClaimAllAsync(
        long userId, Func<int, (int itemType, int stackMax)> itemLookup, long nowUnix);

    /// <summary>
    /// 보관 기한이 지난(발급 시각 &lt; createdBefore) 메일을 최대 limit건 삭제한다(GC 배치 전용, mail 기획서 §6.5).
    /// 열람·수령 여부와 무관하며, 첨부(player_mail_reward)는 FK CASCADE로 함께 삭제된다. 삭제된 메일 수를 반환한다.
    /// </summary>
    Task<int> DeleteRetentionExpiredAsync(long createdBefore, int limit);
}

// ── DB 행 매핑용 POCO(제네릭 매핑 전용, dynamic 금지). snake_case→PascalCase는 Dapper 규칙으로 매핑. ──
file sealed class MailRow
{
    public long MailId { get; set; }
    public int Category { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public int IsRead { get; set; }
    public int Claimed { get; set; }
    public long CreatedAt { get; set; }
    public long ExpiresAt { get; set; }
}

file sealed class MailRewardRow
{
    public long MailId { get; set; }
    public int Seq { get; set; }
    public int RewardType { get; set; }
    public int RewardCode { get; set; }
    public long Quantity { get; set; }
    public int EnhanceLevel { get; set; }
}

file sealed class ItemIdQtyRow
{
    public long PlayerItemId { get; set; }
    public long Quantity { get; set; }
}

/// <summary>메일(우편함) 세이브 접근 계층(taskbar_hero_game). SqlKata 쿼리 빌더 + 제네릭 매핑만 사용한다(dynamic 금지).</summary>
public sealed class MailRepository : IMailRepository
{
    private const int RowTypeItem = 1;
    private const int RowTypeCurrency = 2;
    private const int GoldItemCode = 1;

    private const int RewardTypeGold = 1;

    private readonly GameDbFactory _dbFactory;

    /// <summary>세이브 DB 커넥션 팩토리를 주입받는다.</summary>
    public MailRepository(GameDbFactory dbFactory) => _dbFactory = dbFactory;

    /// <summary>
    /// 우편함 메일 전건과 첨부를 조회해 스냅샷으로 반환하고, 같은 트랜잭션에서 미열람(is_read=0) 메일을 읽음 처리한다.
    /// 만료·수령 완료 메일도 목록에 포함한다(정리 배치 미도입, 상태는 클라이언트가 표시).
    /// </summary>
    public async Task<IReadOnlyList<MailSummary>> GetMailboxAndMarkReadAsync(long userId)
    {
        await using var connection = _dbFactory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var db = _dbFactory.Create(connection);

            var mails = (await db.Query("player_mail")
                .Select("mail_id", "category", "title", "body", "is_read", "claimed", "created_at", "expires_at")
                .Where("user_id", userId)
                .OrderByDesc("mail_id")
                .GetAsync<MailRow>(transaction)).ToList();

            var rewardsByMail = await LoadRewardsAsync(db, transaction, mails.Select(m => m.MailId).ToList());

            // 조회 = 열람(기획서 §8 확정): 미열람 메일을 읽음 처리. 반환 스냅샷은 조회 시점 값 유지.
            if (mails.Any(m => m.IsRead == 0))
            {
                await db.Query("player_mail")
                    .Where("user_id", userId).Where("is_read", 0)
                    .UpdateAsync(new { is_read = 1 }, transaction);
            }

            await transaction.CommitAsync();

            return mails.Select(m => new MailSummary(
                m.MailId, m.Category, m.Title, m.Body, m.IsRead, m.Claimed, m.CreatedAt, m.ExpiresAt,
                rewardsByMail.TryGetValue(m.MailId, out var list) ? list : Array.Empty<MailAttachment>())).ToList();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// 메일 단건 수령(첨부 지급 + 수령 플래그 갱신)을 단일 커넥션의 단일 트랜잭션으로 적용한다.
    /// 검증 실패(MailNotFound·MailAlreadyClaimed·MailExpired·InventoryFull)는 즉시 롤백 후 Fail 상태로 반환하고,
    /// 예외는 롤백 후 전파한다.
    /// </summary>
    /// <remarks>
    /// 한 트랜잭션으로 묶는 작업(하나라도 실패하면 전부 롤백 — 수령 표시만 되고 지급이 안 되는 상태를 막는다):
    /// <para>1) player_mail SELECT — 존재·소유 확인(타인 메일은 MailNotFound로 취급해 존재 비노출)</para>
    /// <para>2) claimed·expires_at 검증 — 이미 수령이면 MailAlreadyClaimed, 만료면 MailExpired</para>
    /// <para>3) player_mail 조건부 갱신(claimed=0일 때만 1로) — 동시 요청 직렬화, 0행이면 경합 패배(MailAlreadyClaimed)</para>
    /// <para>4) player_mail_reward SELECT — 첨부 원장 로드(클라이언트 입력 없음, 서버 권위)</para>
    /// <para>5) player_item — 골드 적립(재화 행 upsert) + 아이템/재료 적재(스택 병합·빈 칸, 부족 시 InventoryFull 롤백)</para>
    /// </remarks>
    public async Task<MailClaimOutcome> ApplyClaimAsync(
        long userId, long mailId, Func<int, (int itemType, int stackMax)> itemLookup, long nowUnix)
    {
        await using var connection = _dbFactory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var db = _dbFactory.Create(connection);

            // 1) 존재·소유 확인. 타인 메일은 MailNotFound(존재 노출 안 함).
            var mail = await db.Query("player_mail")
                .Select("mail_id", "category", "title", "body", "is_read", "claimed", "created_at", "expires_at")
                .Where("mail_id", mailId).Where("user_id", userId)
                .FirstOrDefaultAsync<MailRow>(transaction);
            if (mail is null)
            {
                await transaction.RollbackAsync();
                return MailClaimOutcome.Fail(MailClaimStatus.MailNotFound);
            }

            // 2) 미수령·미만료 검증.
            if (mail.Claimed == 1)
            {
                await transaction.RollbackAsync();
                return MailClaimOutcome.Fail(MailClaimStatus.MailAlreadyClaimed);
            }

            if (mail.ExpiresAt != 0 && nowUnix > mail.ExpiresAt)
            {
                await transaction.RollbackAsync();
                return MailClaimOutcome.Fail(MailClaimStatus.MailExpired);
            }

            // 3) 수령권 선점(조건부 갱신): claimed=0일 때만 1로 전이. 0행이면 동시 요청이 먼저 수령.
            var claimed = await db.Query("player_mail")
                .Where("mail_id", mailId).Where("user_id", userId).Where("claimed", 0)
                .UpdateAsync(new { claimed = 1, claimed_at = nowUnix, is_read = 1 }, transaction);
            if (claimed == 0)
            {
                await transaction.RollbackAsync();
                return MailClaimOutcome.Fail(MailClaimStatus.MailAlreadyClaimed);
            }

            // 4) 첨부 원장 로드 + 5) 지급.
            var rewards = await LoadRewardsForMailAsync(db, transaction, mailId);
            var grant = await GrantAttachmentsAsync(db, transaction, userId, rewards, itemLookup, nowUnix);
            if (!grant.stored)
            {
                await transaction.RollbackAsync();
                return MailClaimOutcome.Fail(MailClaimStatus.InventoryFull);
            }

            await transaction.CommitAsync();
            return new MailClaimOutcome(MailClaimStatus.Ok, grant.gold, grant.items, grant.goldBalance);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// 일괄 수령을 단일 커넥션의 단일 트랜잭션으로 적용한다. 미수령·미만료 메일 전건을 조건부 갱신으로 선점한 뒤
    /// 첨부를 합산 지급한다. 아이템 적재가 용량을 넘으면 전체 롤백 후 InventoryFull을 반환한다(부분 수령 없음).
    /// 수령 대상이 없으면 빈 목록으로 성공한다. 예외는 롤백 후 전파한다.
    /// </summary>
    /// <remarks>
    /// 한 트랜잭션으로 묶는 작업(하나라도 실패하면 전부 롤백):
    /// <para>1) player_mail SELECT — 미수령(claimed=0)·미만료 메일 목록 확보</para>
    /// <para>2) 메일별 조건부 갱신(claimed 0→1) — 동시 단건 수령과 경합하면 그 메일만 제외</para>
    /// <para>3) player_mail_reward SELECT — 선점한 메일들의 첨부 일괄 로드</para>
    /// <para>4) player_item — 골드 합계 적립 + 아이템/재료 적재(부족 시 전체 롤백 → InventoryFull)</para>
    /// </remarks>
    public async Task<MailClaimAllOutcome> ApplyClaimAllAsync(
        long userId, Func<int, (int itemType, int stackMax)> itemLookup, long nowUnix)
    {
        await using var connection = _dbFactory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var db = _dbFactory.Create(connection);

            // 1) 수령 가능(미수령·미만료) 메일.
            var candidates = (await db.Query("player_mail")
                .Select("mail_id")
                .Where("user_id", userId).Where("claimed", 0)
                .Where(q => q.Where("expires_at", 0).OrWhere("expires_at", ">=", nowUnix))
                .OrderBy("mail_id")
                .GetAsync<long>(transaction)).ToList();

            // 2) 메일별 수령권 선점. 동시 단건 수령이 먼저면 해당 메일만 제외.
            var claimedIds = new List<long>();
            foreach (var mailId in candidates)
            {
                var claimed = await db.Query("player_mail")
                    .Where("mail_id", mailId).Where("user_id", userId).Where("claimed", 0)
                    .UpdateAsync(new { claimed = 1, claimed_at = nowUnix, is_read = 1 }, transaction);
                if (claimed > 0)
                {
                    claimedIds.Add(mailId);
                }
            }

            // 3) 첨부 일괄 로드 + 4) 지급. 수령 대상이 없어도 잔액은 회신한다.
            var rewards = claimedIds.Count > 0
                ? (await LoadRewardsAsync(db, transaction, claimedIds)).Values.SelectMany(r => r).ToList()
                : new List<MailAttachment>();
            var grant = await GrantAttachmentsAsync(db, transaction, userId, rewards, itemLookup, nowUnix);
            if (!grant.stored)
            {
                await transaction.RollbackAsync();
                return MailClaimAllOutcome.Fail(MailClaimStatus.InventoryFull);
            }

            await transaction.CommitAsync();
            return new MailClaimAllOutcome(MailClaimStatus.Ok, claimedIds, grant.gold, grant.items, grant.goldBalance);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// 보관 기한이 지난 메일을 mail_id 오름차순으로 최대 limit건 삭제한다(GC 배치 전용).
    /// 대상 mail_id를 먼저 조회한 뒤 PK 목록으로 삭제해 1회 처리량을 상한 안에 묶는다(밀린 분량은 다음 주기 이월).
    /// 첨부(player_mail_reward)는 FK CASCADE로 함께 삭제되므로 별도 DELETE가 없다.
    /// <para><b>미수령 무기한 메일(<c>expires_at=0 AND claimed=0</c>)은 삭제하지 않는다.</b> 거래소 구매 아이템처럼
    /// 만료를 두지 않기로 한 메일을 보관 기한으로 지워 버리면 무기한 발급이 무의미해진다. 수령을 마친 뒤에는
    /// 보관 기한이 지나면 함께 정리된다.</para>
    /// </summary>
    public async Task<int> DeleteRetentionExpiredAsync(long createdBefore, int limit)
    {
        await using var connection = _dbFactory.CreateConnection();
        await connection.OpenAsync();

        var db = _dbFactory.Create(connection);

        var targets = (await db.Query("player_mail")
            .Select("mail_id")
            .Where("created_at", "<", createdBefore)
            .Where(q => q.Where("expires_at", "<>", 0).OrWhere("claimed", 1))
            .OrderBy("mail_id")
            .Limit(limit)
            .GetAsync<long>()).ToList();

        if (targets.Count == 0)
        {
            return 0;
        }

        return await db.Query("player_mail").WhereIn("mail_id", targets).DeleteAsync();
    }

    // ── 발급 헬퍼(발급자 트랜잭션 공용) ──

    /// <summary>
    /// 메일 1건(player_mail + 첨부 player_mail_reward)을 **발급자의 트랜잭션 안에서** 삽입하고 mail_id를 반환한다
    /// (mail 기획서 §6.4 — 렌더링은 발급자 측 MailComposer, 적재는 리포지토리). 출석 획득·거래소 대금 등
    /// 도메인 트랜잭션이 자신의 상태 변경과 메일 발급을 원자적으로 묶을 때 호출한다.
    /// </summary>
    public static async Task<long> InsertMailAsync(
        QueryFactory db, DbTransaction tx, long userId, MailDraft draft, long nowUnix)
    {
        var mailId = await db.Query("player_mail").InsertGetIdAsync<long>(new
        {
            user_id = userId,
            category = draft.Category,
            title = draft.Title,
            body = draft.Body,
            is_read = 0,
            claimed = 0,
            created_at = nowUnix,
            expires_at = draft.ExpiresAt,
            claimed_at = 0,
        }, tx);

        var seq = 1;
        foreach (var reward in draft.Rewards)
        {
            await db.Query("player_mail_reward").InsertAsync(new
            {
                mail_id = mailId,
                seq = seq++,
                reward_type = reward.RewardType,
                reward_code = reward.RewardCode,
                quantity = reward.Quantity,
                enhance_level = reward.EnhanceLevel,
            }, tx);
        }

        return mailId;
    }

    // ── 헬퍼 ──

    /// <summary>지정 메일들의 첨부를 seq 순으로 로드해 메일별로 묶는다.</summary>
    private static async Task<Dictionary<long, IReadOnlyList<MailAttachment>>> LoadRewardsAsync(
        QueryFactory db, DbTransaction tx, IReadOnlyList<long> mailIds)
    {
        if (mailIds.Count == 0)
        {
            return new Dictionary<long, IReadOnlyList<MailAttachment>>();
        }

        var rows = await db.Query("player_mail_reward")
            .Select("mail_id", "seq", "reward_type", "reward_code", "quantity", "enhance_level")
            .WhereIn("mail_id", mailIds)
            .OrderBy("mail_id", "seq")
            .GetAsync<MailRewardRow>(tx);

        return rows.GroupBy(r => r.MailId).ToDictionary(
            g => g.Key,
            g => (IReadOnlyList<MailAttachment>)g
                .Select(r => new MailAttachment(r.RewardType, r.RewardCode, r.Quantity, r.EnhanceLevel)).ToList());
    }

    /// <summary>메일 1건의 첨부를 seq 순으로 로드한다.</summary>
    private static async Task<IReadOnlyList<MailAttachment>> LoadRewardsForMailAsync(
        QueryFactory db, DbTransaction tx, long mailId)
    {
        var rows = await db.Query("player_mail_reward")
            .Select("mail_id", "seq", "reward_type", "reward_code", "quantity", "enhance_level")
            .Where("mail_id", mailId)
            .OrderBy("seq")
            .GetAsync<MailRewardRow>(tx);
        return rows.Select(r => new MailAttachment(r.RewardType, r.RewardCode, r.Quantity, r.EnhanceLevel)).ToList();
    }

    /// <summary>
    /// 첨부 목록을 지급한다: 골드(reward_type=1)는 합산해 재화 행에 적립하고, 아이템/재료(2·3)는 스택 병합·빈 칸 규칙으로
    /// 적재한다. 적재 실패(용량 부족) 시 stored=false를 반환한다(호출측 롤백). items는 코드별 합산된 지급 내역.
    /// </summary>
    private static async Task<(bool stored, long gold, IReadOnlyList<MailAttachment> items, long goldBalance)>
        GrantAttachmentsAsync(
            QueryFactory db, DbTransaction tx, long userId,
            IReadOnlyList<MailAttachment> rewards, Func<int, (int itemType, int stackMax)> itemLookup, long nowUnix)
    {
        long gold = rewards.Where(r => r.RewardType == RewardTypeGold).Sum(r => r.Quantity);

        // 아이템/재료는 코드별로 합산해 적재(같은 코드 첨부가 여러 건이어도 스택 병합이 한 번에 이뤄진다).
        var itemGroups = rewards.Where(r => r.RewardType != RewardTypeGold)
            .GroupBy(r => new { r.RewardType, r.RewardCode, r.EnhanceLevel })
            .Select(g => new MailAttachment(
                g.Key.RewardType, g.Key.RewardCode, g.Sum(r => r.Quantity), g.Key.EnhanceLevel))
            .ToList();

        int capacity = await LoadCapacityAsync(db, tx, userId);
        var used = await LoadUsedSlotsAsync(db, tx, userId);
        foreach (var item in itemGroups)
        {
            // 적재 규칙은 스택 상한(stack_max)만으로 결정되므로 itemType은 쓰지 않는다(소모품·재료 모두 스택 병합 대상).
            var (_, stackMax) = itemLookup(item.RewardCode);
            bool stored = await StoreItemAsync(
                db, tx, userId, item.RewardCode, item.Quantity, stackMax,
                item.EnhanceLevel, capacity, used, nowUnix);
            if (!stored)
            {
                return (false, 0, Array.Empty<MailAttachment>(), 0);
            }
        }

        long balance = await CreditGoldAsync(db, tx, userId, gold, nowUnix);
        return (true, gold, itemGroups, balance);
    }

    /// <summary>인벤토리 용량(game_player.inventory_capacity). 계정 세이브가 없으면 0.</summary>
    private static async Task<int> LoadCapacityAsync(QueryFactory db, DbTransaction tx, long userId)
        => await db.Query("game_player").Select("inventory_capacity").Where("user_id", userId)
            .FirstOrDefaultAsync<int?>(tx) ?? 0;

    /// <summary>현재 점유 중인 인벤토리 칸(slot) 집합(slot이 NULL이 아닌 행).</summary>
    private static async Task<HashSet<int>> LoadUsedSlotsAsync(QueryFactory db, DbTransaction tx, long userId)
    {
        var slots = await db.Query("player_item").Select("slot")
            .Where("user_id", userId).WhereNotNull("slot").GetAsync<int>(tx);
        return slots.ToHashSet();
    }

    /// <summary>used 집합에서 [0, capacity) 범위의 가장 작은 빈 칸. 빈 칸이 없으면 -1.</summary>
    private static int FirstFreeSlot(HashSet<int> used, int capacity)
    {
        for (var i = 0; i < capacity; i++)
        {
            if (!used.Contains(i))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>골드(재화 행)를 upsert로 적립하고 적립 후 잔액을 반환한다(0 지급이면 현재 잔액만 조회).</summary>
    private static async Task<long> CreditGoldAsync(QueryFactory db, DbTransaction tx, long userId, long amount, long nowUnix)
    {
        var goldRow = await db.Query("player_item").Select("player_item_id", "quantity")
            .Where("user_id", userId).Where("row_type", RowTypeCurrency).Where("item_code", GoldItemCode)
            .FirstOrDefaultAsync<ItemIdQtyRow>(tx);

        if (amount <= 0)
        {
            return goldRow?.Quantity ?? 0;
        }

        if (goldRow is null)
        {
            await db.Query("player_item").InsertAsync(new
            {
                user_id = userId,
                row_type = RowTypeCurrency,
                item_code = GoldItemCode,
                quantity = amount,
                slot = (int?)null,
                enhance_level = 0,
                acquired_at = nowUnix,
            }, tx);
            return amount;
        }

        await db.Query("player_item").Where("player_item_id", goldRow.PlayerItemId)
            .UpdateAsync(new { quantity = goldRow.Quantity + amount }, tx);
        return goldRow.Quantity + amount;
    }

    /// <summary>
    /// 첨부 아이템을 적재한다. 스택형(stack_max &gt; 1인 재료·소모품)이면 기존 스택의 여유부터 채운 뒤 남으면 새 행,
    /// 비스택(stack_max = 1인 장비)이면 개당 1행씩 새 칸에 넣는다. 새 칸이 용량을 넘어 부족하면 false(호출측 롤백).
    /// </summary>
    private static async Task<bool> StoreItemAsync(
        QueryFactory db, DbTransaction tx, long userId, int itemCode, long quantity,
        int stackMax, int enhanceLevel, int capacity, HashSet<int> used, long nowUnix)
    {
        long remaining = quantity;

        // 스택형(재료·소모품): 기존 스택의 여유부터 채운다(새 칸 불필요).
        if (stackMax > 1 && enhanceLevel == 0)
        {
            var stacks = await db.Query("player_item").Select("player_item_id", "quantity")
                .Where("user_id", userId).Where("row_type", RowTypeItem).Where("item_code", itemCode)
                .Where("enhance_level", 0)
                .Where("quantity", "<", stackMax)
                .GetAsync<ItemIdQtyRow>(tx);

            foreach (var stack in stacks)
            {
                if (remaining <= 0)
                {
                    break;
                }

                long room = stackMax - stack.Quantity;
                long add = Math.Min(room, remaining);
                await db.Query("player_item").Where("player_item_id", stack.PlayerItemId)
                    .UpdateAsync(new { quantity = stack.Quantity + add }, tx);
                remaining -= add;
            }
        }

        // 남은 수량은 새 행으로. 스택형은 stackMax씩 묶고, 장비는 stackMax가 1이라 자연히 1개당 1행이 된다.
        int perRow = Math.Max(stackMax, 1);
        while (remaining > 0)
        {
            int slot = FirstFreeSlot(used, capacity);
            if (slot < 0)
            {
                return false; // 빈 칸 없음(용량 초과)
            }

            long put = Math.Min(perRow, remaining);
            await db.Query("player_item").InsertAsync(new
            {
                user_id = userId,
                row_type = RowTypeItem,
                item_code = itemCode,
                quantity = put,
                slot = slot,
                enhance_level = enhanceLevel,
                acquired_at = nowUnix,
            }, tx);
            used.Add(slot);
            remaining -= put;
        }

        return true;
    }
}
