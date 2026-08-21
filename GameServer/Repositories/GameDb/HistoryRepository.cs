using GameServer.Repositories.GameDb.Interfaces;
using SqlKata.Execution;

namespace GameServer.Repositories.GameDb;

/// <summary>재화 유통 스냅샷 1종(로그 이벤트 정의 7장 <c>history.currency_supply</c>).</summary>
/// <param name="TotalAmount">계정들이 실제로 들고 있는 총량(우편함에 떠 있는 몫은 제외).</param>
/// <param name="HolderCount">잔액이 1 이상인 계정 수.</param>
/// <param name="MailPendingAmount">아직 수령하지 않은 메일 첨부 합계 — <b>경제에 풀리지 않은 부채</b>다.</param>
public sealed record CurrencySupplySnapshot(
    int CurrencyCode, long TotalAmount, int HolderCount, long MailPendingAmount);

/// <summary>아이템별 호가 스냅샷(<c>history.trade_market</c>). 판매중이면서 아직 만료되지 않은 등록만 센다.</summary>
public sealed record TradeMarketSnapshot(int ItemCode, int ListingCount, long MinPrice, long AvgPrice);

/// <summary>아이템별 유통량 스냅샷(<c>history.item_supply</c>). 가방·장착·재화 행 중 <b>아이템 행</b>만 센다.</summary>
public sealed record ItemSupplySnapshot(int ItemCode, long TotalCount, int HolderCount);

/// <summary>
/// 진행도 분포 스냅샷(<c>history.stage_progress</c>). <paramref name="Sequence"/>는 게임 DB가 들고 있는
/// <b>진행 시퀀스</b>(<c>game_player.max_stage_cleared</c>, 1~100)이며 stage_id로의 환산은 배치가 한다 —
/// 좌표 규약(<see cref="MasterData.StageCoords"/>)은 리포지토리가 아니라 마스터 계층의 지식이다.
/// </summary>
/// <param name="Sequence">그 계정의 최고 진행 시퀀스. 0은 아직 한 판도 깨지 못한 계정이다.</param>
public sealed record StageProgressSnapshot(int Sequence, int UserCount);

/// <summary>착용 장비 분포 스냅샷(<c>history.equip_item</c>).</summary>
/// <param name="EquippedCount">착용 중인 개체 수(계정 하나가 여러 캐릭터에 같은 코드를 끼면 그만큼 센다).</param>
/// <param name="HolderCount">그 아이템을 착용 중인 계정 수.</param>
public sealed record EquipItemSnapshot(int ItemCode, int EquipSlot, int EquippedCount, int HolderCount);

/// <summary>파티 조합 스냅샷(<c>history.party_comp</c>). 빈 자리는 0이며 <b>자리 순서를 정렬하지 않는다</b>(8.7).</summary>
public sealed record PartyCompSnapshot(
    int Slot1ClassCode, int Slot2ClassCode, int Slot3ClassCode, int UserCount);

/// <summary>액티브 스킬 조합 스냅샷(<c>history.skill_build</c>). 1개만 장착한 캐릭터는 두 번째가 0이다.</summary>
public sealed record SkillBuildSnapshot(
    int ClassCode, int ActiveSkillCode1, int ActiveSkillCode2, int CharacterCount);

/// <summary>스킬 투자 분포 스냅샷(<c>history.skill_invest</c>). 미습득(level 0)은 세지 않는다.</summary>
public sealed record SkillInvestSnapshot(int ClassCode, int SkillCode, int Level, int CharacterCount);

/// <summary>
/// 히스토리 배치 전용 집계 조회 계층(taskbar_hero_game). 액션 로그가 담지 못하는 <b>총량과 현재 상태</b>를
/// 세는 읽기 전용 쿼리만 모은다(로그 이벤트 정의 7장).
/// <para><b>쓰기가 없다.</b> 배치는 여기서 센 값을 이벤트 로그 1줄로 내보낼 뿐이고, 적재는 fluentd가 한다 —
/// 서버는 <c>logdb</c>에 접속하지 않는다(1장 설계 원칙 4).</para>
/// <para><b>집계 함수는 <c>SelectRaw</c>로 쓴다.</b> SqlKata 4의 <c>AsCount</c>·<c>AsSum</c> 계열은 SELECT 절을
/// 통째로 대체해 <c>GROUP BY</c>와 함께 쓸 수 없다. 여기 들어가는 문자열은 <b>컬럼과 상수만으로 된 고정 식</b>이며
/// 호출자 입력이 섞이지 않는다 — 조건·값은 모두 빌더의 <c>Where</c>가 파라미터로 넘긴다.</para>
/// </summary>
public sealed class HistoryRepository : GameDbBase, IHistoryRepository
{
    /// <summary>세이브 DB 커넥션 팩토리를 기반 클래스로 전달한다.</summary>
    public HistoryRepository(GameDbFactory dbFactory) : base(dbFactory) { }

    /// <summary>
    /// <paramref name="activeSince"/> 이후에 활동한 계정 수를 센다(동시 접속 추정).
    /// <para>하트비트(<c>update-last-active</c>)를 액션 로그로 남기면 그것 하나가 전체 볼륨을 넘지만,
    /// 같은 주기에 <b>수만 세면 1행</b>이고 필요한 답(동접)은 그대로 나온다(7장).</para>
    /// </summary>
    public async Task<int> CountOnlineUsersAsync(long activeSince)
    {
        using var db = Db();
        return await db.Query("game_player").Where("last_active_at", ">=", activeSince).CountAsync<int>();
    }

    /// <summary>
    /// 재화 종류별 유통 총량·보유 계정 수와, 우편함에 미수령으로 떠 있는 골드(부채)를 함께 조회한다.
    /// <para>부채는 <b>골드 행에만 붙인다</b> — 메일 첨부의 골드는 <c>reward_type=1</c> 하나로 표현되어
    /// 재화 코드를 따로 갖지 않기 때문이다(첨부 스키마 <c>reward_code=0</c>).</para>
    /// </summary>
    /// <param name="nowUnix">만료 판정 기준 시각. 만료된 메일의 첨부는 수령할 수 없으므로 부채가 아니다.</param>
    public async Task<IReadOnlyList<CurrencySupplySnapshot>> GetCurrencySupplyAsync(long nowUnix)
    {
        using var db = Db();

        var totals = (await db.Query("player_item")
            .Where("row_type", Constants.PlayerItemRow.Currency)
            .Where("quantity", ">", 0)
            .Select("item_code")
            .SelectRaw("SUM(quantity) AS total_amount")
            .SelectRaw("COUNT(*) AS holder_count")
            .GroupBy("item_code")
            .GetAsync<CurrencyTotalRow>()).ToList();

        var pending = await db.Query("player_mail_reward AS r")
            .Join("player_mail AS m", "m.mail_id", "r.mail_id")
            .Where("r.reward_type", Constants.RewardType.Gold)
            .Where("m.claimed", 0)
            .Where(q => q.Where("m.expires_at", 0).OrWhere("m.expires_at", ">=", nowUnix))
            .SelectRaw("COALESCE(SUM(r.quantity), 0) AS pending_amount")
            .FirstOrDefaultAsync<MailPendingRow>() ?? new MailPendingRow();

        return totals
            .Select(t => new CurrencySupplySnapshot(
                t.ItemCode, t.TotalAmount, t.HolderCount,
                t.ItemCode == Constants.Currency.GoldItemCode ? pending.PendingAmount : 0))
            .ToList();
    }

    /// <summary>
    /// 판매중이면서 아직 만료되지 않은 등록을 아이템별로 묶어 등록 수·최저가·평균가를 조회한다.
    /// <b>체결이 없어도 호가가 어떻게 움직이는지</b>가 이 스냅샷의 값어치다(7장).
    /// </summary>
    public async Task<IReadOnlyList<TradeMarketSnapshot>> GetTradeMarketAsync(long nowUnix)
    {
        using var db = Db();

        var rows = await db.Query("trade_listing")
            .Where("status", Constants.Trade.StatusOnSale)
            .Where("expires_at", ">", nowUnix)
            .Select("item_code")
            .SelectRaw("COUNT(*) AS listing_count")
            .SelectRaw("MIN(price) AS min_price")
            // 적재 컬럼이 bigint라 평균은 반올림해 정수로 맞춘다(소수점을 남기면 out_sql에서 잘린다).
            .SelectRaw("CAST(ROUND(AVG(price)) AS SIGNED) AS avg_price")
            .GroupBy("item_code")
            .GetAsync<TradeMarketRow>();

        return rows
            .Select(r => new TradeMarketSnapshot(r.ItemCode, r.ListingCount, r.MinPrice, r.AvgPrice))
            .ToList();
    }

    /// <summary>
    /// 아이템 행(재화 제외)을 코드별로 묶어 총 수량과 보유 계정 수를 조회한다.
    /// <c>equip_item_history</c>와 나누면 <b>착용률</b>(가지고는 있는데 안 쓰는 아이템)이 나온다.
    /// </summary>
    public async Task<IReadOnlyList<ItemSupplySnapshot>> GetItemSupplyAsync()
    {
        using var db = Db();

        var rows = await db.Query("player_item")
            .Where("row_type", Constants.PlayerItemRow.Item)
            .Select("item_code")
            .SelectRaw("SUM(quantity) AS total_count")
            .SelectRaw("COUNT(DISTINCT user_id) AS holder_count")
            .GroupBy("item_code")
            .GetAsync<ItemSupplyRow>();

        return rows
            .Select(r => new ItemSupplySnapshot(r.ItemCode, r.TotalCount, r.HolderCount))
            .ToList();
    }

    /// <summary>
    /// 계정을 최고 진행 시퀀스별로 묶어 센다. <b>유저들이 지금 어디에 몰려 있나</b>가 이 분포다.
    /// 시퀀스 → stage_id 환산은 좌표 규약을 아는 배치가 한다.
    /// </summary>
    public async Task<IReadOnlyList<StageProgressSnapshot>> GetStageProgressAsync()
    {
        using var db = Db();

        var rows = await db.Query("game_player")
            .Select("max_stage_cleared")
            .SelectRaw("COUNT(*) AS user_count")
            .GroupBy("max_stage_cleared")
            .GetAsync<StageProgressRowAgg>();

        return rows
            .Select(r => new StageProgressSnapshot(r.MaxStageCleared, r.UserCount))
            .ToList();
    }

    /// <summary>
    /// 착용 중인 장비를 (아이템 코드, 장착 슬롯)별로 묶어 개체 수와 계정 수를 조회한다.
    /// <c>player_item_equipped</c>는 <b>행이 있으면 곧 착용 중</b>이라 조건절이 필요 없다.
    /// </summary>
    public async Task<IReadOnlyList<EquipItemSnapshot>> GetEquipItemAsync()
    {
        using var db = Db();

        var rows = await db.Query("player_item_equipped")
            .Select("item_code", "equipped_slot")
            .SelectRaw("COUNT(*) AS equipped_count")
            .SelectRaw("COUNT(DISTINCT user_id) AS holder_count")
            .GroupBy("item_code", "equipped_slot")
            .GetAsync<EquipItemRow>();

        return rows
            .Select(r => new EquipItemSnapshot(r.ItemCode, r.EquippedSlot, r.EquippedCount, r.HolderCount))
            .ToList();
    }

    /// <summary>
    /// 계정별 파티 편성을 자리 세 칸으로 펼친 뒤 같은 조합끼리 센다.
    /// <para><b>펼치기는 DB가, 조합 집계는 메모리가 한다</b> — 펼친 결과가 계정당 1행이라 이 단계에서 이미
    /// 행 수가 계정 수로 줄고, 조합 자체는 직업 순열이라 상한이 수십 가지다. 파생 테이블을 한 겹 더 쌓는
    /// 대신 계정 수만큼의 행을 받아 묶는 편이 읽기 쉽다.</para>
    /// <para>미편성(<c>slot=0</c>) 캐릭터는 제외하며, 채워지지 않은 자리는 0으로 남는다.</para>
    /// </summary>
    public async Task<IReadOnlyList<PartyCompSnapshot>> GetPartyCompAsync()
    {
        using var db = Db();

        var rows = await db.Query("player_character")
            .Where("slot", ">", Constants.Party.SlotUnassigned)
            .Where("slot", "<=", Constants.Party.MaxSlots)
            .Select("user_id")
            .SelectRaw("MAX(CASE WHEN slot = 1 THEN class_code ELSE 0 END) AS slot1_class_code")
            .SelectRaw("MAX(CASE WHEN slot = 2 THEN class_code ELSE 0 END) AS slot2_class_code")
            .SelectRaw("MAX(CASE WHEN slot = 3 THEN class_code ELSE 0 END) AS slot3_class_code")
            .GroupBy("user_id")
            .GetAsync<PartyPivotRow>();

        return rows
            .GroupBy(r => (r.Slot1ClassCode, r.Slot2ClassCode, r.Slot3ClassCode))
            .Select(g => new PartyCompSnapshot(g.Key.Item1, g.Key.Item2, g.Key.Item3, g.Count()))
            .ToList();
    }

    /// <summary>
    /// 캐릭터별 장착 액티브 스킬(최대 2개)을 오름차순 쌍으로 펼친 뒤 같은 조합끼리 센다.
    /// <para>장착 한도가 2개라 <c>MIN</c>·<c>MAX</c> 두 값이 곧 그 쌍이고, 1개만 장착한 캐릭터는
    /// 둘이 같은 값이 되므로 두 번째를 0으로 접는다 — <c>(코드, 코드)</c>로 두면 "같은 스킬을 두 번 장착"으로
    /// 읽히기 때문이다.</para>
    /// </summary>
    public async Task<IReadOnlyList<SkillBuildSnapshot>> GetSkillBuildAsync()
    {
        using var db = Db();

        var rows = await db.Query("player_skill AS s")
            .Join("player_character AS c",
                j => j.On("c.user_id", "s.user_id").On("c.character_id", "s.character_id"))
            .Where("s.equipped", 1)
            .Select("c.class_code")
            .SelectRaw("MIN(s.skill_code) AS active_skill_code_1")
            .SelectRaw("MAX(s.skill_code) AS active_skill_code_2")
            .SelectRaw("COUNT(*) AS equipped_count")
            .GroupBy("s.user_id", "s.character_id", "c.class_code")
            .GetAsync<SkillBuildPivotRow>();

        return rows
            .Select(r => (
                r.ClassCode,
                First: r.ActiveSkillCode1,
                Second: r.EquippedCount >= 2 ? r.ActiveSkillCode2 : 0))
            .GroupBy(r => r)
            .Select(g => new SkillBuildSnapshot(g.Key.ClassCode, g.Key.First, g.Key.Second, g.Count()))
            .ToList();
    }

    /// <summary>
    /// 습득한 스킬을 (직업, 스킬, 레벨)별로 묶어 캐릭터 수를 조회한다.
    /// <b>아무도 안 올리는 스킬은 밸런스 문제</b>라는 판단의 근거다.
    /// <para>레벨 0 행은 미습득이므로(스킬 초기화가 행 삭제가 아니라 <c>level=0</c> 갱신이다) 제외한다.</para>
    /// </summary>
    public async Task<IReadOnlyList<SkillInvestSnapshot>> GetSkillInvestAsync()
    {
        using var db = Db();

        var rows = await db.Query("player_skill AS s")
            .Join("player_character AS c",
                j => j.On("c.user_id", "s.user_id").On("c.character_id", "s.character_id"))
            .Where("s.level", ">", 0)
            .Select("c.class_code", "s.skill_code", "s.level")
            .SelectRaw("COUNT(*) AS character_count")
            .GroupBy("c.class_code", "s.skill_code", "s.level")
            .GetAsync<SkillInvestRow>();

        return rows
            .Select(r => new SkillInvestSnapshot(r.ClassCode, r.SkillCode, r.Level, r.CharacterCount))
            .ToList();
    }
}

// ── DB 행 매핑 POCO(제네릭 매핑 전용, dynamic 금지). snake_case → PascalCase는 Dapper 규칙으로 매핑한다. ──

file sealed class CurrencyTotalRow
{
    public int ItemCode { get; set; }
    public long TotalAmount { get; set; }
    public int HolderCount { get; set; }
}

file sealed class MailPendingRow
{
    public long PendingAmount { get; set; }
}

file sealed class TradeMarketRow
{
    public int ItemCode { get; set; }
    public int ListingCount { get; set; }
    public long MinPrice { get; set; }
    public long AvgPrice { get; set; }
}

file sealed class ItemSupplyRow
{
    public int ItemCode { get; set; }
    public long TotalCount { get; set; }
    public int HolderCount { get; set; }
}

file sealed class StageProgressRowAgg
{
    public int MaxStageCleared { get; set; }
    public int UserCount { get; set; }
}

file sealed class EquipItemRow
{
    public int ItemCode { get; set; }
    public int EquippedSlot { get; set; }
    public int EquippedCount { get; set; }
    public int HolderCount { get; set; }
}

file sealed class PartyPivotRow
{
    public long UserId { get; set; }
    public int Slot1ClassCode { get; set; }
    public int Slot2ClassCode { get; set; }
    public int Slot3ClassCode { get; set; }
}

file sealed class SkillBuildPivotRow
{
    public int ClassCode { get; set; }
    public int ActiveSkillCode1 { get; set; }
    public int ActiveSkillCode2 { get; set; }
    public int EquippedCount { get; set; }
}

file sealed class SkillInvestRow
{
    public int ClassCode { get; set; }
    public int SkillCode { get; set; }
    public int Level { get; set; }
    public int CharacterCount { get; set; }
}
