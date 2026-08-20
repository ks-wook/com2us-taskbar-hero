using GameServer.Repositories;

namespace GameServer.Repositories.Interfaces;

/// <summary>거래 아이템의 마스터 정보(판매 가능 여부·기준가·타입·스택). 서비스가 마스터에서 조회해 주입한다.</summary>
public interface ITradeRepository
{
    /// <summary>
    /// 해당 itemCode(0이면 전체)의 판매중 등록 <b>한 페이지</b>를 가격 오름차순·listing_id 보조 정렬로 조회한다.
    /// <b>뷰어 필터와 페이징을 모두 쿼리에서 처리</b>하므로(<c>WHERE → ORDER BY → LIMIT</c>) 페이지 크기가 정확하다.
    /// <paramref name="limit"/>에 <b>페이지 크기 + 1</b>을 넘기면 호출측이 hasMore를 판정할 수 있다(trade 기획서 §7.3).
    /// <para><b>만료 시각이 지난 등록은 제외한다</b>(<c>expires_at &gt; nowUnix</c>) — 만료는 배치를 기다리지 않고
    /// 읽는 순간 효력을 갖는다(trade 기획서 §7.6).</para>
    /// </summary>
    Task<IReadOnlyList<TradeListingSnapshot>> GetActiveListingPageAsync(
        int itemCode, long viewerUserId, bool mine, int offset, int limit, long nowUnix);

    /// <summary>등록 스냅샷 1건(판매중 + 미만료). 없거나 이미 닫혔거나 만료 시각이 지났으면 null.</summary>
    Task<TradeListingSnapshot?> GetListingAsync(long listingId, long nowUnix);

    /// <summary>판매 등록(에스크로): 한도·아이템·가격 검증 → player_item 제거 → trade_listing 생성을 한 트랜잭션으로 적용한다.</summary>
    Task<TradeRegisterOutcome> ApplyRegisterAsync(
        long userId, long itemId, long price,
        int listingLimit, long nowUnix, long expiresAt);

    /// <summary>
    /// 구매: 조건부 갱신 선점 → 골드 차감 → <b>구매 아이템 메일 발급(구매자)</b> → 판매 대금 메일 발급(판매자)을
    /// 한 트랜잭션으로 적용한다. 아이템은 인벤토리에 직접 넣지 않고 우편함으로 보낸다.
    /// </summary>
    Task<TradeBuyOutcome> ApplyBuyAsync(
        long buyerUserId, long listingId,
        Func<TradeListingSnapshot, MailDraft> composeItemMail,
        Func<TradeListingSnapshot, MailDraft> composeSettlementMail, long nowUnix);

    /// <summary>판매 취소: 본인·판매중 확인 → 조건부 갱신 선점 → 아이템 인벤토리 복원을 한 트랜잭션으로 적용한다.</summary>
    Task<TradeCancelOutcome> ApplyCancelAsync(
        long userId, long listingId, long nowUnix);

    /// <summary>만료 배치 대상(판매중 + 만료 시각 경과) listing_id를 오름차순 최대 limit건 조회한다.</summary>
    Task<IReadOnlyList<long>> GetExpiredListingIdsAsync(long nowUnix, int limit);

    /// <summary>만료 처리: 조건부 갱신으로 취소 확정 → 아이템을 판매자에게 메일로 반송한다. 이미 닫혔으면 null.</summary>
    Task<TradeListingSnapshot?> ApplyExpireAsync(
        long listingId, Func<TradeListingSnapshot, MailDraft> composeReturnMail, long nowUnix);
}
