namespace TaskbarHero.Common
{
    /// <summary>
    /// 가챠 뽑기 상품 종류(가챠 기획서 §4.3). player_gacha_pull.pull_type 컬럼과 API 응답의 pullType이 같은 값이며,
    /// 서버-클라이언트 공유 계약이므로 <b>숫자 값을 변경하지 않는다</b>.
    /// <para>어떤 상품으로 뽑았는지를 나타내는 값이고 <b>행 수와 무관</b>하다 — 실제 뽑은 횟수는
    /// player_gacha_pull_item의 자식 행 수(1연 1행 / 10연 10행)로 확인한다.</para>
    /// </summary>
    public enum GachaPullType
    {
        /// <summary>1연(cost_single 소모, 1회 추첨).</summary>
        Single = 1,

        /// <summary>10연(cost_multi 소모, multi_count회 연속 추첨 + 보장 등급 대체).</summary>
        Multi = 2,
    }
}
