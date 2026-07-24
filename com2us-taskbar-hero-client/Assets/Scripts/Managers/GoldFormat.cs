namespace TaskbarHero.Client.Managers
{
    /// <summary>
    /// 골드 수치를 UI에서 강조 표기하기 위한 공용 포매터. 모달(<see cref="ModalManager"/>) 등에서
    /// 골드 금액을 표시할 때는 이 헬퍼로 <b>노란색·볼드 리치텍스트</b>로 감싸 강조한다.
    /// Unity UI <c>Text</c>는 기본적으로 리치텍스트(<c>&lt;color&gt;</c>·<c>&lt;b&gt;</c>)를 지원하므로
    /// 반환 문자열을 그대로 메시지에 끼워 넣으면 된다.
    /// </summary>
    public static class GoldFormat
    {
        /// <summary>골드 강조 색(노란색). 클리어 보상 골드 색과 동일 계열.</summary>
        private const string GoldColorHex = "FFD94D";

        /// <summary>골드 금액을 천단위 구분(N0) + 노란색 + 볼드 리치텍스트로 감싼 문자열로 반환한다.
        /// 예: <c>1200</c> → <c>&lt;color=#FFD94D&gt;&lt;b&gt;1,200&lt;/b&gt;&lt;/color&gt;</c>.</summary>
        public static string Highlight(long amount)
        {
            return $"<color=#{GoldColorHex}><b>{amount:N0}</b></color>";
        }
    }
}
