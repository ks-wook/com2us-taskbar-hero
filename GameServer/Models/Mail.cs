namespace GameServer.Models;

// MailRepository 전용 DB 행 매핑용 POCO(제네릭 매핑 전용, dynamic 금지).
// Dapper.MatchNamesWithUnderscores=true(Program.cs)로 snake_case 컬럼 → PascalCase 프로퍼티 매핑.

class MailRow
{
    public long MailId { get; set; }
    public int Category { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public int IsRead { get; set; }
    public int Claimed { get; set; }
    public long CreatedAt { get; set; }
    public long ExpiresAt { get; set; }
}

// 일괄 수령이 선점 대상 메일을 훑을 때 쓰는 최소 컬럼(수령권 갱신 + 원장 유입 사유 판정).
class MailIdCategoryRow
{
    public long MailId { get; set; }
    public int Category { get; set; }
}

class MailRewardRow
{
    public long MailId { get; set; }
    public int Seq { get; set; }
    public int RewardType { get; set; }
    public int RewardCode { get; set; }
    public long Quantity { get; set; }
    public int EnhanceLevel { get; set; }
}

// ── 마스터 데이터 정의(불변). Repositories/MasterDb 가 적재하고 MasterDbProvider 가 조회한다. ──
/// <summary>메일 발급 문구 템플릿(mail_master). 발급 메일의 category·제목/본문 형식·만료 일수를 확정한다(mail 기획서 §4·§6.4).</summary>
public sealed record MailTemplateDef(int TemplateCode, int Category, string TitleFormat, string BodyFormat, int ValidDays);
/// <summary>신규 가입 지원금 첨부 1건(newbie_reward_master). 계정 초기화 시 발급하는 환영 메일에 그대로 담긴다.
/// RewardType 1:골드 2:아이템 3:재료(메일 첨부·출석 보상과 동일 enum), 골드는 RewardCode 0.</summary>
public sealed record NewbieRewardDef(int Seq, int RewardType, int RewardCode, long Quantity);

// ── 마스터 적재용 DB 행 매핑 POCO(제네릭 매핑 전용, dynamic 금지). snake_case→PascalCase는 Dapper 규칙으로 매핑.
//    DECIMAL 컬럼은 decimal로 받아 float/double로 캐스팅한다. 적재는 Repositories/MasterDb/MasterDbLoader. ──
class NewbieRewardRow
{
    public int Seq { get; set; }
    public int RewardType { get; set; }
    public int RewardCode { get; set; }
    public long Quantity { get; set; }
}
class MailMasterRow
{
    public int MailTemplateCode { get; set; }
    public int Category { get; set; }
    public string TitleFormat { get; set; } = string.Empty;
    public string BodyFormat { get; set; } = string.Empty;
    public int ValidDays { get; set; }
}
