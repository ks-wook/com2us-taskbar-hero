using GameServer.MasterData;
using GameServer.Repositories.GameDb;
using GameServer.Models;

namespace GameServer.Util;

/// <summary>
/// 메일 발급 초안 렌더러(mail 기획서 §6.4 발급 규약). 발급자는 (템플릿, 문구 파라미터, 첨부)만 넘기고,
/// 제목·본문·category·만료 시각은 mail_master 템플릿이 확정한다 — 도메인마다 문구/만료가 어긋나는 것을 막는다.
/// 렌더링 결과(MailDraft)는 발급자의 트랜잭션 안에서 MailRepository.InsertMailAsync로 적재된다.
/// </summary>
public static class MailUtil
{
    /// <summary>
    /// 템플릿의 {0} 자리표시자에 파라미터를 채워 메일 초안을 만든다.
    /// 만료 시각은 템플릿 valid_days 기준 발급 시점 + N일(0이면 무기한 0)로 산출한다.
    /// </summary>
    public static MailDraft Compose(
        MailTemplateDef template, string arg0, long nowUnix, IReadOnlyList<MailAttachment> rewards)
    {
        var title = string.Format(template.TitleFormat, arg0);
        var body = string.Format(template.BodyFormat, arg0);
        var expiresAt = template.ValidDays > 0 ? nowUnix + DateTimeUtil.DaysToSeconds(template.ValidDays) : 0;
        return new MailDraft(template.Category, title, body, expiresAt, rewards) { TemplateCode = template.TemplateCode };
    }

    /// <summary>
    /// 자리표시자가 둘인 템플릿({0}·{1})용 오버로드. 보스러시 시즌 순위 보상(템플릿 501)이
    /// 시즌 번호와 최종 순위를 함께 채우는 데 쓴다(보스러시 기획서 6.4).
    /// </summary>
    public static MailDraft Compose(
        MailTemplateDef template, string arg0, string arg1, long nowUnix, IReadOnlyList<MailAttachment> rewards)
    {
        var title = string.Format(template.TitleFormat, arg0, arg1);
        var body = string.Format(template.BodyFormat, arg0, arg1);
        var expiresAt = template.ValidDays > 0 ? nowUnix + DateTimeUtil.DaysToSeconds(template.ValidDays) : 0;
        return new MailDraft(template.Category, title, body, expiresAt, rewards) { TemplateCode = template.TemplateCode };
    }
}
