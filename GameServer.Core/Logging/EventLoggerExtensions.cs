using GameServer.Models;
using GameServer.Repositories.GameDb;
using TaskbarHero.Common.Dto;

namespace GameServer.Logging;

/// <summary>
/// 이번 지급으로 <b>새로 생긴 장비 행</b>의 개체 id를 아이템 코드별로 꺼내 쓰는 소모용 목록(6.2).
/// <para>장비는 <c>stack_max=1</c>이라 지급 때마다 반드시 새 행이 생기고 <b>스택 병합이 일어나지 않는다</b> —
/// 그래서 가방 변경분(<c>upserted</c>)에 담긴 그 코드의 행이 곧 이번에 생긴 개체이고, 원장이 개체 단위로
/// <c>item_id</c>를 채울 수 있다. 스택 아이템(재료·소모품)은 병합된 기존 행이 섞이므로 쓰지 않는다.</para>
/// </summary>
public sealed class GrantedItemIds
{
    private readonly Dictionary<int, Queue<long>> _byItemCode;

    /// <summary>가방 변경분에서 코드별 행 id를 담긴 순서대로 모은다.</summary>
    public GrantedItemIds(InventoryDeltaDto delta)
    {
        _byItemCode = new Dictionary<int, Queue<long>>();
        foreach (var row in delta.upserted)
        {
            if (!_byItemCode.TryGetValue(row.itemCode, out var queue))
            {
                queue = new Queue<long>();
                _byItemCode[row.itemCode] = queue;
            }

            queue.Enqueue(row.itemId);
        }
    }

    /// <summary>id가 하나도 없는 빈 목록(장비를 지급하지 않는 경로용).</summary>
    public static GrantedItemIds Empty { get; } = new(new InventoryDeltaDto());

    /// <summary>
    /// 그 코드의 다음 개체 id를 하나 꺼낸다. 더 없으면 null —
    /// 원장 행이 <c>item_id</c> 없이 나갈 뿐, 유통량 집계는 그대로 맞는다.
    /// </summary>
    public long? Next(int itemCode)
        => _byItemCode.TryGetValue(itemCode, out var queue) && queue.Count > 0 ? queue.Dequeue() : null;
}

/// <summary>
/// 두 곳 이상에서 같은 모양으로 방출되는 이벤트의 호출 헬퍼.
/// 서비스마다 같은 루프를 다시 쓰면 필드 순서·<c>source</c> 문자열이 갈라질 수 있어 여기로 모은다.
/// </summary>
public static class EventLoggerExtensions
{
    /// <summary>
    /// 이번 지급으로 오른 레벨을 <b>캐릭터 1명당 1행</b>으로 방출한다(<c>character.levelup</c>, 5.3).
    /// 스테이지 클리어와 오프라인 정산이 같은 테이블에 쓰고 <paramref name="source"/>로만 갈리므로,
    /// 두 경로가 이 메서드를 공유한다. 아무도 오르지 않았으면 아무것도 내지 않는다.
    /// </summary>
    /// <param name="source"><see cref="LevelUpSource"/>의 상수만 넘긴다.</param>
    public static void CharacterLevelUps(
        this IEventLogger eventLogger, long userId, IReadOnlyList<CharacterLevelUp> levelUps, string source)
    {
        foreach (var levelUp in levelUps)
        {
            eventLogger.Action(
                Constants.EventLog.Tags.CharacterLevelUp, userId,
                new CharacterLevelUpEvent(
                    levelUp.CharacterId, levelUp.ClassCode, levelUp.FromLevel, levelUp.ToLevel, source));
        }
    }

    /// <summary>
    /// 재화 유입 1행을 방출한다(<c>currency.flow</c>, 6.1).
    /// </summary>
    /// <param name="balanceAfter">적립 후 잔액.</param>
    /// <param name="source"><see cref="CurrencySource"/>의 상수만 넘긴다.</param>
    /// <param name="refId">source가 해석을 정하는 식별자.</param>
    public static void CurrencyGained(
        this IEventLogger eventLogger, long userId, long amount, long balanceAfter, string source, long refId)
        => eventLogger.Action(
            Constants.EventLog.Tags.CurrencyFlow, userId,
            new CurrencyFlowEvent(
                Constants.Currency.GoldItemCode, CurrencyDirection.Gain, amount, balanceAfter, source, refId));

    /// <summary>재화 유출 1행을 방출한다(<c>currency.flow</c>, 6.1).</summary>
    /// <param name="balanceAfter">차감 후 잔액.</param>
    public static void CurrencySpent(
        this IEventLogger eventLogger, long userId, long amount, long balanceAfter, string source, long refId)
        => eventLogger.Action(
            Constants.EventLog.Tags.CurrencyFlow, userId,
            new CurrencyFlowEvent(
                Constants.Currency.GoldItemCode, CurrencyDirection.Spend, amount, balanceAfter, source, refId));

    /// <summary>
    /// 재화 소각 1행을 방출한다(<c>currency.flow</c>, 6.1). <b>잔액을 담지 않는다</b> —
    /// 소각은 어느 계정의 잔액도 바꾸지 않는 정산용 행이기 때문이다.
    /// </summary>
    /// <param name="userId">소각을 <b>부담한</b> 계정(거래 수수료는 판매자).</param>
    public static void CurrencyBurned(
        this IEventLogger eventLogger, long userId, long amount, string source, long refId)
        => eventLogger.Action(
            Constants.EventLog.Tags.CurrencyFlow, userId,
            new CurrencyFlowEvent(
                Constants.Currency.GoldItemCode, CurrencyDirection.Burn, amount, null, source, refId));

    /// <summary>
    /// 메일 1건의 발급을 방출한다(<c>mail.issue</c>, 5.8). 발급 지점이 도메인마다 흩어져 있어
    /// (신규 지원금·출석·거래 대금·구매 아이템·만료 반송·순위 보상) 여기로 모은다 — 그러지 않으면
    /// 지점마다 <c>gold</c>·<c>item_count</c> 집계 방식이 갈릴 수 있다.
    /// <para>골드와 아이템 건수는 <b>초안의 첨부에서 파생</b>한다. 첨부 품목 상세는 담지 않는다 —
    /// 그건 수령 시 <c>item_flow_logs</c>의 몫이다.</para>
    /// </summary>
    /// <param name="userId">메일을 <b>받는</b> 계정. 발급을 유발한 요청자와 다를 수 있다(거래 대금·구매 아이템).</param>
    /// <param name="source"><see cref="MailSource"/>의 상수만 넘긴다.</param>
    public static void MailIssued(
        this IEventLogger eventLogger, long userId, long mailId, MailDraft draft, string source)
    {
        long gold = 0;
        var itemCount = 0;
        foreach (var reward in draft.Rewards)
        {
            if (reward.RewardType == Constants.RewardType.Gold)
            {
                gold += reward.Quantity;
            }
            else
            {
                itemCount++;
            }
        }

        eventLogger.Action(
            Constants.EventLog.Tags.MailIssue, userId,
            new MailIssueEvent(mailId, draft.TemplateCode, draft.Category, source, gold, itemCount));
    }

    /// <summary>
    /// 아이템 유입을 원장에 남긴다(<c>item.flow</c>, 6.2).
    /// <para><b>장비는 개체마다 1행</b>(<c>delta=+1</c>, <c>item_id</c> 채움)으로 쪼개고, 스택 아이템(재료·소모품)은
    /// 합계 1행으로 낸다 — 장비만 개체가 유일해 지급 뒤의 강화·거래·분해까지 같은 축으로 이어지기 때문이다.</para>
    /// </summary>
    /// <param name="def">마스터 정의. 미정의 코드면 null을 넘기고, 그때 type·grade는 0으로 나간다.</param>
    /// <param name="quantity">이번에 들어온 수량(양수).</param>
    /// <param name="granted">장비 개체 id를 꺼낼 목록. 없으면 <see cref="GrantedItemIds.Empty"/>.</param>
    /// <param name="reason"><see cref="ItemFlowReason"/>의 상수만 넘긴다.</param>
    /// <param name="refId">reason이 해석을 정하는 식별자.</param>
    public static void ItemGained(
        this IEventLogger eventLogger, long userId, int itemCode, ItemDef? def, long quantity,
        GrantedItemIds granted, string reason, long refId)
        => EmitItemFlow(eventLogger, userId, itemCode, def, quantity, granted, null, reason, refId);

    /// <summary>
    /// 아이템 유출을 원장에 남긴다(<c>item.flow</c>, 6.2). <paramref name="quantity"/>는 절대값이고
    /// 부호는 이 메서드가 붙인다.
    /// <para>거래소 등록(에스크로 반출)처럼 <b>소실이 아니라 이동</b>인 경로도 이 자리를 쓴다 —
    /// 원장이 기록하는 것은 계정 보유량의 증감이고, 이동 여부는 <paramref name="reason"/>이 가른다.</para>
    /// </summary>
    /// <param name="itemId">
    /// 사라진 개체의 id(장비처럼 개체가 유일할 때만). 스택 아이템은 null을 넘긴다.
    /// </param>
    public static void ItemRemoved(
        this IEventLogger eventLogger, long userId, int itemCode, ItemDef? def, long quantity,
        long? itemId, string reason, long refId)
        => EmitItemFlow(eventLogger, userId, itemCode, def, -quantity, GrantedItemIds.Empty, itemId, reason, refId);

    /// <summary>
    /// 아이템 원장 행을 실제로 방출한다. 장비(개체 유일)는 <paramref name="signedQuantity"/>의 절대 수량만큼
    /// ±1 행으로 쪼개고, 스택 아이템은 합계 1행으로 낸다.
    /// </summary>
    /// <param name="signedQuantity">부호 있는 변동량(+유입 / −유출).</param>
    /// <param name="granted">유입 시 장비 개체 id를 꺼낼 목록(유출은 <paramref name="fixedItemId"/>를 쓴다).</param>
    /// <param name="fixedItemId">유출 시 사라진 개체의 id. 유입 경로는 null.</param>
    private static void EmitItemFlow(
        IEventLogger eventLogger, long userId, int itemCode, ItemDef? def, long signedQuantity,
        GrantedItemIds granted, long? fixedItemId, string reason, long refId)
    {
        if (signedQuantity == 0)
        {
            return; // 움직이지 않은 사실은 원장에 남길 것이 없다.
        }

        int itemType = def?.ItemType ?? 0;
        int grade = def?.Grade ?? 0;
        int sign = signedQuantity > 0 ? 1 : -1;

        // 재화 행은 이 원장에 오지 않는다(6.1의 몫) — 장비만 개체 단위로 쪼갠다.
        if (itemType != Constants.ItemType.Equip)
        {
            eventLogger.Action(
                Constants.EventLog.Tags.ItemFlow, userId,
                new ItemFlowEvent(itemCode, itemType, grade, null, (int)signedQuantity, reason, refId));
            return;
        }

        long count = Math.Abs(signedQuantity);
        for (long i = 0; i < count; i++)
        {
            long? itemId = sign > 0 ? granted.Next(itemCode) : (i == 0 ? fixedItemId : null);
            eventLogger.Action(
                Constants.EventLog.Tags.ItemFlow, userId,
                new ItemFlowEvent(itemCode, itemType, grade, itemId, sign, reason, refId));
        }
    }
}
