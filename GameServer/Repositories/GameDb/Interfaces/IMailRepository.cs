using GameServer.Repositories.GameDb;

namespace GameServer.Repositories.GameDb.Interfaces;

public interface IMailRepository
{
    /// <summary>
    /// 우편함 전체(첨부 포함)를 조회하고, 미열람 메일을 읽음 처리한다(조회 = 열람, mail 기획서 §8 확정).
    /// 반환 스냅샷의 IsRead는 조회 시점 값이므로 클라이언트가 신규 메일 표시에 쓸 수 있다.
    /// </summary>
    Task<IReadOnlyList<MailSummary>> GetMailboxAndMarkReadAsync(long userId);

    /// <summary>
    /// 단건 수령을 한 트랜잭션으로 적용한다: 소유·미수령·미만료 검증 → 조건부 갱신(claimed 0→1)으로 수령권 선점 →
    /// 첨부 지급(골드 적립·아이템 적재). 적재 규칙 판정에 필요한 아이템 속성은 주입된 IItemLookup이 제공한다.
    /// </summary>
    Task<MailClaimOutcome> ApplyClaimAsync(
        long userId, long mailId, long nowUnix);

    /// <summary>
    /// 일괄 수령을 한 트랜잭션으로 적용한다: 미수령·미만료 메일 전건을 6.1과 같은 규칙으로 수령한다.
    /// 용량 초과 시 전체 롤백(InventoryFull) — 부분 수령하지 않는다(mail 기획서 §8 확정).
    /// </summary>
    Task<MailClaimAllOutcome> ApplyClaimAllAsync(
        long userId, long nowUnix);

    /// <summary>
    /// 보관 기한이 지난(발급 시각 &lt; createdBefore) 메일을 최대 limit건 삭제한다(GC 배치 전용, mail 기획서 §6.5).
    /// 열람·수령 여부와 무관하며, 첨부(player_mail_reward)는 FK CASCADE로 함께 삭제된다. 삭제된 메일 수를 반환한다.
    /// </summary>
    Task<int> DeleteRetentionExpiredAsync(long createdBefore, int limit);
}
