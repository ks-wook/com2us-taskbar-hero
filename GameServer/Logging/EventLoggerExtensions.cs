using GameServer.Models;

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
}
