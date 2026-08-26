namespace GameServer.Models;

// GachaRepository 전용 DB 행 매핑용 POCO(제네릭 매핑 전용, dynamic 금지).
// Dapper.MatchNamesWithUnderscores=true(Program.cs)로 snake_case 컬럼 → PascalCase 프로퍼티 매핑.

class GachaCounterRow
{
    public int Grade { get; set; }
    public int PityCount { get; set; }
}

class GachaPullRow
{
    public long PullId { get; set; }
    public int GachaCode { get; set; }
    public int PullType { get; set; }
    public int CostCurrencyCode { get; set; }
    public long CostAmount { get; set; }
    public long PulledAt { get; set; }
}

class GachaPullItemRow
{
    public long PullId { get; set; }
    public int Seq { get; set; }
    public int ItemCode { get; set; }
    public int Grade { get; set; }
    public int Quantity { get; set; }
    public int PityApplied { get; set; }
    public int Guaranteed { get; set; }
}

// ── 마스터 데이터 정의(불변). Repositories/MasterDb 가 적재하고 MasterDbProvider 가 조회한다. ──
/// <summary>가챠 등급 슬롯의 지급 후보 1건(gacha_item_pool). Quantity는 1회 지급 수량.</summary>
public sealed record GachaPoolEntry(int ItemCode, int Quantity);
/// <summary>
/// 가챠 천장 규칙 1건(gacha_pity_rule). PityType 1:소프트(확률 가산) 2:하드(확정 지급).
/// Threshold는 <b>이번 뽑기의 회차 번호</b>(player_gacha_counter.pity_count + 1)와 비교한다 —
/// 누적 미획득 횟수와 직접 비교하면 한 회차 늦게 발동한다(가챠 기획서 §4.1·6.3).
/// <para><see cref="ProbStep"/>은 소프트 전용으로 <b>발동 후 회차 1번당 올릴 확률(%p, 0~1)</b>이다.
/// 하드 규칙은 0이다.</para>
/// </summary>
public sealed record GachaPityRule(int Grade, int PityType, int Threshold, double ProbStep);
/// <summary>
/// 가챠(뽑기) 배너 정의(gacha_master + 자식 3종). 한 행이 하나의 배너다.
/// <para><b>노출 조건</b>: IsActive=1 AND (OpenAt=0 or now&gt;=OpenAt) AND (CloseAt=0 or now&lt;CloseAt).
/// 판정은 서버 시각 기준이며 배너 조회·뽑기가 같은 조건을 쓴다(기획서 §6.1).</para>
/// <para><b>PickupItemCode</b>는 픽업 대상 <i>선언</i>이며 추첨식에 들어가지 않는다. 픽업 배너는 최고 등급 슬롯의
/// 후보를 그 아이템 1종으로 두므로, 균등 추첨이 그대로 확정을 만든다(기획서 §4.1). 0이면 상시 배너.</para>
/// </summary>
public sealed record GachaBannerDef(
    int GachaCode, string Name, int IsActive, long OpenAt, long CloseAt, int SortOrder,
    int CostCurrencyCode, long CostSingle, long CostMulti, int MultiCount, int MultiGuaranteedGrade,
    int PickupItemCode,
    IReadOnlyDictionary<int, int> GradeWeights,
    IReadOnlyDictionary<int, List<GachaPoolEntry>> PoolByGrade,
    IReadOnlyList<GachaPityRule> PityRules)
{
    /// <summary>지정 시각에 이 배너가 열려 있는지(노출 조건, 기획서 §6.1).</summary>
    public bool IsOpenAt(long nowUnix)
        => IsActive == 1
           && (OpenAt == 0 || nowUnix >= OpenAt)
           && (CloseAt == 0 || nowUnix < CloseAt);

    /// <summary>천장 규칙이 걸린 등급 목록(소프트·하드가 같은 등급에 있으면 한 번만). 오름차순.</summary>
    public IReadOnlyList<int> PityGrades => PityRules.Select(r => r.Grade).Distinct().OrderBy(g => g).ToList();

    /// <summary>등급의 하드 천장 발동 회차. 하드 규칙이 없으면 0(클라이언트 게이지 표시용).</summary>
    public int HardThreshold(int grade)
        => PityRules.FirstOrDefault(r => r.Grade == grade && r.PityType == Constants.Gacha.PityTypeHard)?.Threshold ?? 0;
}
/// <summary>가챠 1회 추첨 결과(서버 RNG 확정). PityApplied는 하드 천장으로 등급이 확정된 회차임을 뜻한다.</summary>
public sealed record GachaRoll(int Grade, int ItemCode, int Quantity, bool PityApplied, bool Guaranteed);

// ── 마스터 적재용 DB 행 매핑 POCO(제네릭 매핑 전용, dynamic 금지). snake_case→PascalCase는 Dapper 규칙으로 매핑.
//    DECIMAL 컬럼은 decimal로 받아 float/double로 캐스팅한다. 적재는 Repositories/MasterDb/MasterDbLoader. ──
class GachaMasterRow
{
    public int GachaCode { get; set; }
    public string Name { get; set; } = string.Empty;
    public int IsActive { get; set; }
    public long OpenAt { get; set; }
    public long CloseAt { get; set; }
    public int SortOrder { get; set; }
    public int CostCurrencyCode { get; set; }
    public long CostSingle { get; set; }
    public long CostMulti { get; set; }
    public int MultiCount { get; set; }
    public int MultiGuaranteedGrade { get; set; }
    public int PickupItemCode { get; set; }
}
class GachaGradeWeightRow
{
    public int GachaCode { get; set; }
    public int Grade { get; set; }
    public int Weight { get; set; }
}
class GachaItemPoolRow
{
    public int GachaCode { get; set; }
    public int Grade { get; set; }
    public int ItemCode { get; set; }
    public int Quantity { get; set; }
}
class GachaPityRuleRow
{
    public int GachaCode { get; set; }
    public int Grade { get; set; }
    public int PityType { get; set; }
    public int Threshold { get; set; }
    public decimal ProbStep { get; set; }   // DECIMAL 컬럼 → decimal 로 받아 double 로 캐스팅
}
