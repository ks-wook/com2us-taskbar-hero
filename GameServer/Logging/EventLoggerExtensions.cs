using GameServer.Models;
using GameServer.Repositories.GameDb;

namespace GameServer.Logging;

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
}
