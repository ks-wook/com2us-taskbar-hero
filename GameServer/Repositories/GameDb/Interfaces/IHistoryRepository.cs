namespace GameServer.Repositories.GameDb.Interfaces;

/// <summary>
/// 히스토리 배치 전용 집계 조회(로그 이벤트 정의 7장). <b>읽기 전용</b>이며, 센 값을 어디에 남길지는
/// 배치가 정한다 — 서버는 <c>logdb</c>에 접속하지 않고 이벤트 로그 1줄만 내보낸다(1장 설계 원칙 4).
/// </summary>
public interface IHistoryRepository
{
    /// <summary>지정 시각 이후에 활동한 계정 수(동시 접속 추정). 하트비트를 로그로 남기지 않고 규모만 센다.</summary>
    Task<int> CountOnlineUsersAsync(long activeSince);

    /// <summary>재화 종류별 유통 총량·보유 계정 수 + 우편함 미수령 골드(부채). 만료된 메일의 첨부는 부채가 아니다.</summary>
    Task<IReadOnlyList<CurrencySupplySnapshot>> GetCurrencySupplyAsync(long nowUnix);

    /// <summary>판매중·미만료 등록의 아이템별 등록 수·최저가·평균가(호가 스냅샷).</summary>
    Task<IReadOnlyList<TradeMarketSnapshot>> GetTradeMarketAsync(long nowUnix);

    /// <summary>아이템 행(재화 제외)의 코드별 총 수량·보유 계정 수.</summary>
    Task<IReadOnlyList<ItemSupplySnapshot>> GetItemSupplyAsync();

    /// <summary>최고 진행 시퀀스별 계정 수(진행도 분포). stage_id 환산은 호출측이 한다.</summary>
    Task<IReadOnlyList<StageProgressSnapshot>> GetStageProgressAsync();

    /// <summary>착용 중인 장비의 (아이템 코드, 슬롯)별 개체 수·계정 수.</summary>
    Task<IReadOnlyList<EquipItemSnapshot>> GetEquipItemAsync();

    /// <summary>파티 자리 세 칸의 직업 조합별 계정 수(빈 자리는 0, 자리 순서를 정렬하지 않는다).</summary>
    Task<IReadOnlyList<PartyCompSnapshot>> GetPartyCompAsync();

    /// <summary>직업별 액티브 스킬 조합(오름차순 쌍)별 캐릭터 수.</summary>
    Task<IReadOnlyList<SkillBuildSnapshot>> GetSkillBuildAsync();

    /// <summary>(직업, 스킬, 레벨)별 캐릭터 수(미습득 level 0 제외).</summary>
    Task<IReadOnlyList<SkillInvestSnapshot>> GetSkillInvestAsync();
}
