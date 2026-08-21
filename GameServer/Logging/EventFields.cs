namespace GameServer.Logging;

/// <summary>
/// 이벤트 고유 필드를 담는 타입의 표식([로그 이벤트 정의](../../docs/공통/로그-이벤트-정의.md) 4.2).
/// <b>이벤트마다 전용 record</b>를 두어 컬럼과 1:1로 맞춘다 — 익명 객체를 쓰면 컬럼이 조용히 어긋난다.
/// <para>프로퍼티 이름은 <c>PascalCase</c>로 쓰고 직렬화 시 <c>snake_case</c>로 바뀐다. 그 이름이 곧
/// 적재 테이블의 컬럼 이름이므로, 필드를 바꾸면 정의 문서(5~7장)·테이블 DDL·fluentd 매핑을 함께 고친다(9.4).</para>
/// </summary>
public interface IEventFields
{
}

// ── 5.2 세션 / 캐릭터 ──

/// <summary>
/// <c>save.load</c> — 접속 시 세이브 로드. <b>이 체계에서 세션 개시를 뜻하는 유일한 이벤트</b>라
/// DAU·재방문율·시간대별 접속 분포가 전부 여기서 나온다(계정 로그를 남기지 않으므로, 5.1).
/// </summary>
/// <param name="IsNew">세이브가 없는 신규 계정인지(가입 → 실제 플레이 전환율의 분모).</param>
/// <param name="OfflineElapsedSec">직전 활동 이후 경과 초. 이탈 후 복귀 간격 분포의 입력이다. 신규는 0.</param>
public sealed record SaveLoadEvent(bool IsNew, long OfflineElapsedSec) : IEventFields;

/// <summary>
/// <c>player.create</c> — <b>최초</b> 캐릭터 생성(계정 세이브 초기화). 두 번째 이후의 캐릭터 추가는
/// 남기지 않는다 — 이 이벤트가 답하는 질문이 <b>첫 직업 선호</b>와 <b>가입 → 플레이 전환</b>이기 때문이다
/// (지금 보유한 직업은 스냅샷이 답하므로 첫 선택만 액션 로그로 남긴다).
/// </summary>
/// <param name="ClassCode">선택한 직업(class_master).</param>
/// <param name="Gender">선택한 성별(1:남 2:여). 외형 전용이라 스탯과 무관하다.</param>
public sealed record PlayerCreateEvent(int ClassCode, int Gender) : IEventFields;

// ── 5.3 스테이지 / 전투 ──

/// <summary>
/// <c>stage.enter</c> — 스테이지 진입. 이 이벤트 하나만으로는 답하는 질문이 없고,
/// <c>stage.clear</c>·<c>stage.fail</c>과 짝지어야 뜻이 생긴다 — <b>실질 난이도</b>(fail / enter)의 분모이며,
/// <c>enter − clear − fail</c>이 보고 없이 사라진 판, 즉 <b>이탈</b>이다.
/// <para>진입 <b>거부</b>도 남기는 유일한 스테이지 이벤트다(<c>error_code</c>) — 도달하지 못한 스테이지로의
/// 진입 시도가 반복되면 잠금 UI나 진행 곡선을 손봐야 한다는 신호이기 때문이다(4.1의 선별 기준).</para>
/// </summary>
public sealed record StageEnterEvent(int StageId, int Act, int Difficulty, int Stage) : IEventFields;

/// <summary>
/// <c>stage.clear</c> — 클리어 보상 지급 확정. <b>이 체계에서 가장 빈번한 이벤트</b>다(방치형이라 한 판이
/// 수십 초로 끝난다, 10장). 스테이지별 이탈 지점과 재화 유입량이 여기서 나온다.
/// </summary>
/// <param name="Gold">실제 지급액(획득량 버프 배율이 적용된 뒤의 값). 마스터 기본값이 아니다.</param>
/// <param name="Exp">실제 지급 경험치(같은 이유로 배율 적용 후).</param>
/// <param name="IsFirstClear">최고 도달을 밀어 올린 첫 클리어인지. 반복 파밍과 갈라야 이탈 지점이 보인다.</param>
/// <param name="MaxStageCleared">클리어 반영 후의 최고 진행도(되돌릴 수 없는 단조 증가 상태, 5장 기준 2).</param>
public sealed record StageClearEvent(
    int StageId, int Act, int Difficulty, int Stage,
    long Gold, long Exp, bool IsFirstClear, int MaxStageCleared) : IEventFields;

/// <summary>
/// <c>stage.fail</c> — 파티 전멸(클라이언트 보고). 전투가 클라이언트 권위라 서버는 전멸을 스스로 알 수 없고,
/// 이 보고가 있어야 실질 난이도를 추정이 아닌 <b>측정</b>으로 얻는다.
/// <para>세 수치가 <b>리밸런싱 대상</b>을 고른다 — 실패율만 보면 "조금 모자라 진 스테이지"와 "손도 못 대는 벽"이
/// 같아 보인다.</para>
/// </summary>
/// <param name="ElapsedMs">진입~전멸까지 걸린 시간. 즉사와 접전을 가른다.</param>
/// <param name="RemainingMonsterCount">전멸 시점의 잔여 적 수. 0에 가까울수록 소폭 하향으로 넘길 수 있는 패배다.</param>
/// <param name="ReachedBoss">보스전까지 갔는지. 잡몹이 벽인지 보스가 벽인지 가른다.</param>
public sealed record StageFailEvent(
    int StageId, int Act, int Difficulty, int Stage,
    int ElapsedMs, int RemainingMonsterCount, bool ReachedBoss) : IEventFields;

/// <summary>
/// <c>character.levelup</c> — 캐릭터 레벨 상승. <b>기획 성장 곡선과 실측의 괴리</b>를 재는 이벤트다.
/// <para>상태 분포(지금 몇 레벨인가)는 스냅샷이 답하지만 이 이벤트는 예외로 액션 로그에 남긴다 —
/// 레벨은 <b>되돌릴 수 없는 단조 증가 상태</b>라 액션의 누적이 곧 상태여서 집계 편향이 없다(5장 기준 2).</para>
/// </summary>
/// <param name="Source">레벨을 올린 경로(<c>stage</c> 또는 <c>offline</c>). 두 경로의 기여 비중을 가른다.</param>
public sealed record CharacterLevelUpEvent(
    long CharacterId, int ClassCode, int FromLevel, int ToLevel, string Source) : IEventFields;

// ── 5.5 아이템 강화 ──

/// <summary>
/// <c>item.enhance</c> — 장비 강화 +1. <b>강화 단계 분포</b>(유저가 어디서 멈추나)와 어떤 등급·부위에 투자가
/// 몰리는지, 그리고 골드 유출에서 강화가 차지하는 비중을 답한다.
/// <para><b>거부(재화 부족)도 같은 필드로 남긴다</b> — 그때 <see cref="ToLevel"/>·<see cref="Cost"/>는
/// *시도한* 값이다. 어느 단계에서 골드가 막히는지가 곧 강화 비용 곡선을 조정할 자리를 가리킨다(5.5).</para>
/// </summary>
/// <param name="ItemId">강화 대상 보유 아이템(<c>player_item_id</c>). 같은 아이템의 강화 이력을 잇는 축이다.</param>
/// <param name="Grade">아이템 등급. 등급별로 투자가 어디에 몰리는지 가른다.</param>
/// <param name="Cost">소모한(또는 소모하려 한) 재화량.</param>
public sealed record ItemEnhanceEvent(
    long ItemId, int ItemCode, int Grade, int FromLevel, int ToLevel, long Cost) : IEventFields;

// ── 5.6 가챠 ──

/// <summary>
/// <c>gacha.pull_item</c> — 뽑기 결과 <b>1개당 1행</b>(10연이면 10행). <b>등급 실측 분포가 기획 확률과 맞는가</b>를
/// 검증하는 표본이고, 천장 발동률·배너별 소비량도 여기서 나온다.
/// <para><b>요청 단위 값은 컬럼으로 두지 않는다</b> — 1연/10연은 같은 <see cref="PullId"/>의 행 수(1 또는 10)로,
/// 비용은 원장(<c>spend</c>/<c>gacha_pull</c>, <c>ref_id</c> = pull_id)으로 파생한다. 같은 사실을 두 곳에
/// 적지 않기 위해서다(5.6).</para>
/// <para>지급된 아이템 자체는 <c>item_flow_logs</c>에도 남는다 — <b>등급 분포는 이쪽, 아이템 유통량은 그쪽</b>이다.</para>
/// </summary>
/// <param name="PullId">뽑기 요청 1건의 식별자. 10연 10행이 이 값으로 묶인다.</param>
/// <param name="Seq">그 요청 안의 회차(1부터). 10연 보장은 마지막 회차에 걸린다.</param>
/// <param name="IsPity">하드 천장이 발동해 등급이 보정된 결과인지.</param>
/// <param name="IsGuaranteed">10연 보장으로 마지막 회차가 대체된 결과인지.</param>
public sealed record GachaPullItemEvent(
    long PullId, int Seq, int GachaCode, int ItemCode, int Grade,
    bool IsPity, bool IsGuaranteed) : IEventFields;

// ── 5.9 출석부 ──

/// <summary>
/// <c>attendance.claim</c> — 오늘자 출석 보상 획득. <b>일차별 이탈</b>(며칠째에서 끊기나)과 출석 재화 유입을 답한다.
/// <para><c>mail.issue</c>(<c>source='attendance'</c>)와 사건이 겹치지만 이 테이블을 따로 둔다 —
/// 이 도메인의 질문이 일차별 이탈이고, 그 축인 <see cref="Day"/>가 메일 로그에는 없기 때문이다(5.9).</para>
/// <para>재화는 이 시점에 지급되지 않는다. <see cref="MailId"/>의 <b>수령 시점</b>에 원장으로 잡히므로,
/// 이 행과 원장 사이의 시차가 곧 발급 → 수령 지연이다.</para>
/// </summary>
/// <param name="AttendDate">획득한 날짜(서버 KST 기준, <c>YYYY-MM-DD</c>).</param>
/// <param name="Day">회차 안의 출석 일차(1~30). 이탈 분포의 축이다.</param>
public sealed record AttendanceClaimEvent(
    string AttendDate, int Day, int RewardType, int RewardCode, long Quantity, long MailId) : IEventFields;

// ── 5.8 메일 ──

/// <summary>
/// <c>mail.issue</c> — 서버가 메일을 발급하는 <b>모든 지점</b>.
/// <para><b>메일은 이 게임의 재화 지급 관문이다</b> — 출석·거래 대금·순위 보상·신규 지원금이 전부 메일을
/// 거치므로, <b>발급(이 테이블) − 수령(원장의 mail_claim 행)</b>의 차액이 곧 <b>우편함에 떠 있는 부채</b>,
/// 즉 아직 경제에 풀리지 않은 재화다. 발급 → 수령 지연도 두 시각의 차로 나온다(5.8).</para>
/// </summary>
/// <param name="TemplateCode">문구를 확정한 <c>mail_master</c> 템플릿. 어떤 템플릿이 얼마나 나갔는지를 센다.</param>
/// <param name="Category">메일 분류(템플릿이 정한 값).</param>
/// <param name="Source"><see cref="MailSource"/>의 값. 어느 도메인이 발급했는지를 가른다.</param>
/// <param name="Gold">첨부된 골드 총액(없으면 0).</param>
/// <param name="ItemCount">
/// 첨부 아이템 <b>건수만</b> 담는다 — 품목별 상세는 수령 시 <c>item_flow_logs</c>가 담으므로
/// 여기에 목록을 넣으면 같은 사실이 두 곳에 생긴다(5.8).
/// </param>
public sealed record MailIssueEvent(
    long MailId, int TemplateCode, int Category, string Source, long Gold, int ItemCount) : IEventFields;

/// <summary>
/// <see cref="MailIssueEvent.Source"/>에 들어가는 <b>고정 집합</b>(5.8). 발급 주체를 가르는 집계 축이라
/// 새 발급 지점을 만들면 여기에 값을 추가하고 정의 문서의 목록도 함께 고친다.
/// </summary>
public static class MailSource
{
    /// <summary>신규 가입 지원금(계정 초기화 시 1회).</summary>
    public const string Newbie = "newbie";

    /// <summary>출석 보상.</summary>
    public const string Attendance = "attendance";

    /// <summary>거래소 구매 아이템(구매자에게).</summary>
    public const string TradeBuyItem = "trade_buy_item";

    /// <summary>거래소 판매 대금(판매자에게, 수수료 차감 후).</summary>
    public const string TradeSellProceeds = "trade_sell_proceeds";

    /// <summary>거래소 만료 반송(판매자에게 아이템 되돌림).</summary>
    public const string TradeExpire = "trade_expire";

    /// <summary>보스러시 시즌 순위 보상(1~3위).</summary>
    public const string BossRushRank = "bossrush_rank";
}

// ── 5.7 거래소 / 교역선 ──

/// <summary>
/// <c>trade.register</c> — 판매 등록(에스크로). <b>아이템별 시세 분포</b>(<c>price / base_price</c>)와 공급량을 답한다.
/// <para><b>가격 범위 거부도 남긴다</b> — 유저가 부르려던 가격이 허용 범위 밖이라는 사실이 반복되면
/// 그 범위가 실제 시세와 맞지 않는다는 뜻이다. 그때 <see cref="ListingId"/>는 0이다(등록이 만들어지지 않았다).</para>
/// <para><c>price_ratio</c> 같은 파생 값은 컬럼으로 두지 않는다 — <c>price / base_price</c>로 언제든 계산된다.</para>
/// </summary>
/// <param name="BasePrice">마스터 기준가. 시세 배율의 분모다.</param>
public sealed record TradeRegisterEvent(
    long ListingId, int ItemCode, int Grade, int EnhanceLevel, long Price, long BasePrice) : IEventFields;

/// <summary>
/// <c>trade.close</c> — 등록이 <b>어떻게든 끝났을 때</b>(구매·취소·만료). 미체결률·체결가·체결까지 걸린 시간·
/// 수수료 소각량을 답한다.
/// <para><b>결말 셋을 한 테이블에 담는다</b> — 필드가 거의 같고, 핵심 질문인 미체결률이 세 결말의 비율이라
/// <c>GROUP BY outcome</c> 한 줄로 나온다. 테이블을 셋으로 나누면 그 질문마다 <c>UNION</c>이 필요하다(5.7).</para>
/// <para><b>uid는 언제나 판매자(등록자)</b>다 — 등록과 결말을 같은 계정 축으로 잇기 위해서이고,
/// 구매자는 <see cref="BuyerUid"/>로 따로 담는다.</para>
/// </summary>
/// <param name="Outcome"><see cref="TradeCloseOutcome"/>의 값(<c>buy</c>/<c>cancel</c>/<c>expire</c>).</param>
/// <param name="Fee">구매 성사 시 소각되는 수수료(판매가의 20%). 구매가 아니면 0이다 — 경제에서 사라지는 골드다.</param>
/// <param name="SellerProceeds">판매자 수령액(가격 − 수수료). 구매가 아니면 0이다.</param>
/// <param name="BuyerUid">구매자. 구매가 아니면 null(필드 자체가 빠진다).</param>
/// <param name="ListedSec">등록 → 종결까지 걸린 초. <b>유동성 지표</b>다 — 길어지면 그 품목은 공급 과잉이다.</param>
/// <param name="MailId">그 결말로 발급된 메일(구매=구매 아이템, 만료=반송). 취소는 인벤토리로 돌아가 메일이 없어 null이다.</param>
public sealed record TradeCloseEvent(
    long ListingId, int ItemCode, int Grade, string Outcome,
    long Price, long Fee, long SellerProceeds, long? BuyerUid, long ListedSec, long? MailId) : IEventFields;

/// <summary>
/// <see cref="TradeCloseEvent.Outcome"/>에 들어가는 값. 컬럼에 그대로 적재되고 미체결률 집계의 그룹 축이라
/// 오타가 나면 결말 하나가 통째로 빠진다.
/// </summary>
public static class TradeCloseOutcome
{
    /// <summary>구매 성사.</summary>
    public const string Buy = "buy";

    /// <summary>판매자가 직접 내림.</summary>
    public const string Cancel = "cancel";

    /// <summary>판매 기간(3일)이 지나 배치가 닫음.</summary>
    public const string Expire = "expire";
}

// ── 5.4 오프라인 보상 ──

/// <summary>
/// <c>offline.claim</c> — 오프라인(방치) 보상 정산. <b>12시간 상한에 걸리는 비율</b>이 상한을 조정할지 판단하는
/// 근거이고, 전체 재화 유입에서 오프라인이 차지하는 비중을 재는 자리이기도 하다.
/// <para>골드 유입 자체는 원장(<c>currency_flow_logs</c>)에도 남지만 <b>상한에 걸렸는지는 원장이 모른다</b> —
/// <see cref="IsCapped"/> 한 컬럼이 이 테이블을 따로 두는 이유다(5.4).</para>
/// </summary>
/// <param name="ElapsedSec">실제 경과 시간(마지막 활동 이후). 상한을 적용하기 전의 값이다.</param>
/// <param name="EffectiveSec">상한을 적용한 뒤 실제로 보상 계산에 쓰인 시간.</param>
/// <param name="IsCapped">상한에 걸려 잘렸는지. 이 비율이 높으면 상한이 유저 행동과 맞지 않는다는 뜻이다.</param>
public sealed record OfflineClaimEvent(
    long ElapsedSec, long EffectiveSec, bool IsCapped, long Gold, long Exp) : IEventFields;

/// <summary>
/// <see cref="CharacterLevelUpEvent.Source"/>에 들어가는 값. 컬럼에 그대로 적재되는 문자열이라
/// 오타가 나면 집계에서 조용히 빠지므로 상수로 고정한다.
/// </summary>
public static class LevelUpSource
{
    /// <summary>스테이지 클리어 보상으로 오른 레벨.</summary>
    public const string Stage = "stage";

    /// <summary>오프라인(방치) 정산으로 오른 레벨.</summary>
    public const string Offline = "offline";
}
