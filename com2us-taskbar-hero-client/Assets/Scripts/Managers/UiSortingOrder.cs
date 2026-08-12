namespace TaskbarHero.Client.Managers
{
    /// <summary>
    /// 화면 오버레이 캔버스의 정렬 순서(sortingOrder)를 한곳에 모아 둔 계층 지도.
    /// 흩어진 매직 넘버 때문에 "전투 연출이 인벤토리를 가린다" 같은 겹침 사고가 나므로,
    /// 새 오버레이를 만들 때는 아래 띠 중 하나를 골라 쓴다.
    ///
    /// <list type="table">
    /// <item><term>0~29 게임 화면</term><description>전투 오버레이(1~10)·HUD(15)·스테이지 진행 바(20) — 항상 맨 아래</description></item>
    /// <item><term>30~99 전투 연출</term><description>입장 배너·보스 경고·클리어/패배 연출.
    ///   <b>기능 패널보다 아래</b>라 인벤토리·출석부 등을 열어 둔 동안 가리지 않는다.</description></item>
    /// <item><term>100~119 기능 패널</term><description>인벤토리·메일·거래소·출석부·스테이지·편성(100),
    ///   스킬·룬·오프라인 보상(110), 큐브(112) — 패널에서 파생된 패널일수록 위</description></item>
    /// <item><term>120~199 패널 위 연출</term><description>패널에서 띄우는 보상 획득 연출 등</description></item>
    /// <item><term>200~299 시스템 메뉴</term><description>ESC 메뉴·캐릭터 선택</description></item>
    /// <item><term>500+ 최상단</term><description>공용 모달·아이템 상세 팝업·로딩 오버레이(500), 개발 콘솔(1000)</description></item>
    /// </list>
    /// </summary>
    public static class UiSortingOrder
    {
        /// <summary>상시 HUD(하단 아이콘 줄). 씬에 구워진 전투 오버레이 캔버스들(적 HP바 1 · 전투 이펙트 2 ·
        /// 전투 UI <c>SkillUICanvas</c> 10)보다 <b>위</b>여야 한다 — 같은 10이면 draw 순서가 계층 순서에
        /// 좌우돼 하단 바 프레임이 전투 쪽 그림에 가려질 수 있다. 스테이지 진행 바(20)보다는 아래로 둔다.</summary>
        public const int Hud = 15;

        /// <summary>스테이지 진행 바(HUD 위, 전투 연출 아래).</summary>
        public const int StageProgress = 20;

        // ── 전투 연출(기능 패널 아래) ──

        /// <summary>스테이지 입장 배너.</summary>
        public const int BattleEnterBanner = 40;

        /// <summary>보스 등장 경고 배너.</summary>
        public const int BattleBossWarning = 50;

        /// <summary>스테이지 클리어·패배 연출.</summary>
        public const int BattleResult = 60;

        // ── 기능 패널 ──

        /// <summary>기본 기능 패널(인벤토리·메일·거래소·출석부 등).</summary>
        public const int Panel = 100;

        /// <summary>패널에서 파생되는 패널(스킬·룬·오프라인 보상).</summary>
        public const int PanelChild = 110;

        /// <summary>패널 위에서 띄우는 보상 획득 연출(우편함·거래소 수령 등).
        /// 연 패널(≤112)보다는 위, 공용 모달(500)보다는 아래.</summary>
        public const int RewardOverPanel = 120;

        /// <summary>
        /// <b>끌고 있는 아이템 아이콘</b>. 가방(100)에서 집은 아이콘이 큐브(112) 같은 다른 패널 뒤로
        /// 숨으면 어디에 놓는지 보이지 않으므로, 모든 기능 패널 위에 그린다.
        /// 드래그 중에만 적용하고 놓으면 되돌린다.
        /// </summary>
        public const int DraggedItem = 130;

        /// <summary>ESC 메뉴·캐릭터 선택 같은 시스템 메뉴.</summary>
        public const int SystemMenu = 200;

        /// <summary>공용 모달·아이템 상세 팝업·로딩 오버레이.</summary>
        public const int Topmost = 500;
    }
}
