using GameServer.Util;

namespace GameServer;

/// <summary>
/// 서버 전역 상수 모음. 서비스·스케줄러·리포지토리에 흩어져 있던 <c>private const</c>를 한곳으로 모은 것으로,
/// <b>DB 열거값·마스터 코드·정책 수치</b>가 파일마다 따로 선언돼 값이 갈라지는 것을 막는다
/// (예: <c>row_type</c>·골드 코드·메일 템플릿 코드가 리포지토리 9곳에 중복 선언돼 있었다).
/// <para>시각·기간 계산의 단위(초/분/시/일)는 <see cref="DateTimeUtil"/>이 정본이며 여기서는 그것을 조합해 쓴다.</para>
/// <para>트랜잭션 결과 상태 enum은 <c>Enums.cs</c>에 있다.</para>
/// </summary>
public static class Constants
{
    // ────────────────────────────────────────────────────────────────
    // DB 열거값 — 컬럼에 그대로 들어가는 값이라 마음대로 바꿀 수 없다.
    // ────────────────────────────────────────────────────────────────

    /// <summary><c>player_item.row_type</c> — 한 테이블에 아이템 행과 재화 행이 함께 산다.</summary>
    public static class PlayerItemRow
    {
        /// <summary>1:아이템 행(장비·재료·소모품).</summary>
        public const int Item = 1;

        /// <summary>2:재화 행(골드 등). 아이템 조회·이동 대상이 아니다.</summary>
        public const int Currency = 2;
    }

    /// <summary>재화 식별값.</summary>
    public static class Currency
    {
        /// <summary>골드의 <c>player_item.item_code</c>(재화 행 키).</summary>
        public const int GoldItemCode = 1;

        /// <summary>응답 <c>balance</c>의 <c>currencyType</c> 골드 값.</summary>
        public const int GoldType = 1;
    }

    /// <summary><c>item_master.item_type</c>.</summary>
    public static class ItemType
    {
        /// <summary>1:장비(강화·장착·거래 대상).</summary>
        public const int Equip = 1;

        /// <summary>2:재료(스택, 큐브 제작 소모).</summary>
        public const int Material = 2;

        /// <summary>4:소모품(효과는 consumable_master).</summary>
        public const int Consumable = 4;
    }

    /// <summary><c>equip_slot_master</c> 슬롯 값.</summary>
    public static class EquipSlot
    {
        /// <summary>무기 슬롯. 캐릭터 생성 시 지급하는 기본 장비가 이 슬롯이다.</summary>
        public const int Weapon = 1;
    }

    /// <summary><c>skill_master.skill_type</c>.</summary>
    public static class SkillType
    {
        /// <summary>1:액티브(2:패시브). 장착·기본 습득 대상은 액티브뿐이다.</summary>
        public const int Active = 1;
    }

    /// <summary>메일 첨부·보상의 종류(<c>mail_attachment.reward_type</c>, 보상 정의 공통).</summary>
    public static class RewardType
    {
        /// <summary>1:골드.</summary>
        public const int Gold = 1;

        /// <summary>2:아이템(장비·소모품).</summary>
        public const int Item = 2;

        /// <summary>3:재료.</summary>
        public const int Material = 3;
    }

    /// <summary>MySQL 에러 번호.</summary>
    public static class MySqlError
    {
        /// <summary>1062 중복 키 — 유니크 제약 경합(동시 생성·시즌 개시)을 구분하는 데 쓴다.</summary>
        public const int DuplicateEntry = 1062;
    }

    // ────────────────────────────────────────────────────────────────
    // 마스터 코드
    // ────────────────────────────────────────────────────────────────

    /// <summary><c>mail_master</c> 템플릿 코드. 발급자는 코드만 알고 문구·만료는 템플릿이 확정한다.</summary>
    public static class MailTemplate
    {
        /// <summary>101 신규 가입 지원금 — 계정 초기화 시 1회 발급.</summary>
        public const int NewbieReward = 101;

        /// <summary>201 거래소 판매 대금({0} = 아이템 표시값) — 판매자 수령.</summary>
        public const int TradeSettlement = 201;

        /// <summary>202 거래소 만료 반송({0} = 아이템 표시값) — 등록자에게 되돌려 준다.</summary>
        public const int TradeReturn = 202;

        /// <summary>203 거래소 구매 아이템({0} = 아이템 표시값) — 구매자 수령.</summary>
        public const int TradePurchase = 203;

        /// <summary>301 출석 보상({0} = 출석 일차).</summary>
        public const int Attendance = 301;

        /// <summary>501 보스러시 순위 보상({0} = 시즌 번호 · {1} = 최종 순위).</summary>
        public const int BossRushRankReward = 501;
    }

    /// <summary>마스터에 없는 코드를 만났을 때의 기본 취급(<c>ItemLookup</c> 폴백).</summary>
    public static class MasterFallback
    {
        /// <summary>미정의 코드는 장비(item_type 1)로 본다.</summary>
        public const int UnknownItemType = ItemType.Equip;

        /// <summary>미정의 코드의 스택 상한은 1(합치지 않는다).</summary>
        public const int UnknownStackMax = 1;
    }

    // ────────────────────────────────────────────────────────────────
    // 캐릭터 · 파티 · 스킬
    // ────────────────────────────────────────────────────────────────

    /// <summary>파티 편성(<c>player_character.slot</c>).</summary>
    public static class Party
    {
        /// <summary>파티에 세울 수 있는 인원(전투 참가 인원). 보유 캐릭터 수 상한이 아니다 — 보유는 직업 수만큼 가능하다.</summary>
        public const int MaxSlots = 3;

        /// <summary>"미편성"(보유하지만 파티에 없어 전투에 참가하지 않음) 값. 1~3은 파티 자리다.</summary>
        public const int SlotUnassigned = 0;
    }

    /// <summary>캐릭터 생성 기준값.</summary>
    public static class Character
    {
        /// <summary>갓 생성한 캐릭터의 레벨. 지급 기본 무기의 레벨 제한 상한 판정에도 쓴다.</summary>
        public const int StartingLevel = 1;
    }

    /// <summary>스킬(<c>player_skill</c>) 기준값.</summary>
    public static class Skill
    {
        /// <summary>캐릭터 생성 시 함께 습득시키는 기본 액티브 스킬의 레벨(1레벨 = 스킬 포인트 1을 미리 투자한 상태).</summary>
        public const int StartingLevel = 1;

        /// <summary><c>player_skill.equipped</c>의 "장착됨" 값(1). 기본 스킬은 습득과 동시에 장착한다.</summary>
        public const int Equipped = 1;

        /// <summary>캐릭터당 액티브 장착 한도.</summary>
        public const int MaxActiveEquipped = 2;

        /// <summary>스킬 1레벨당 소모 스킬 포인트(확정).</summary>
        public const int PointPerLevel = 1;
    }

    // ────────────────────────────────────────────────────────────────
    // 콘텐츠 정책 수치
    // ────────────────────────────────────────────────────────────────

    /// <summary>스테이지 좌표계 규모(마스터 데이터 기획서 5.9 · stage-battle 기획서 8장).</summary>
    public static class Stage
    {
        /// <summary>지역(Act) 수.</summary>
        public const int Acts = 5;

        /// <summary>난이도 수(1:Normal 2:Hard).</summary>
        public const int Difficulties = 2;

        /// <summary>한 지역(Act)당 스테이지 수.</summary>
        public const int StagesPerAct = 10;

        /// <summary>각 Act·난이도의 마지막(보스) 스테이지 번호.</summary>
        public const int BossStage = 10;

        /// <summary>전체 스테이지 수(= 진행 시퀀스 상한, 100).</summary>
        public const int TotalStages = Acts * Difficulties * StagesPerAct;
    }

    /// <summary>인벤토리(가방) 용량·페이지.</summary>
    public static class Inventory
    {
        /// <summary>신규 계정 기본 용량(점유 slot 수). 골드로 1칸씩 확장(inventory_expand_master).</summary>
        public const int BaseCapacity = 100;

        /// <summary>페이지 크기 기본값(요청이 0 이하일 때).</summary>
        public const int DefaultPageLimit = 200;

        /// <summary>페이지 크기 상한. 한 요청이 인벤토리 전체를 끌어오지 못하게 막는다.</summary>
        public const int MaxPageLimit = 500;
    }

    /// <summary>오프라인(방치) 보상 산식(offline-reward 기획서 확정값).</summary>
    public static class Offline
    {
        /// <summary>정산에 반영하는 경과의 상한(최대 누적 12시간).</summary>
        public const long CapSec = 12 * DateTimeUtil.SecondsPerHour;

        /// <summary>정산 최소 경과(10분). 미만이면 NoOfflineReward.</summary>
        public const long MinRewardSec = 10 * DateTimeUtil.SecondsPerMinute;

        /// <summary>
        /// 파밍 스테이지의 "클리어 보상"을 이 주기(초)마다 얻는다고 가정해 시간당 산출율을 파생한다.
        /// 기획서 §9 미결이던 stageGoldRate/stageExpRate를 학습용 단순식으로 확정한 것으로, 값만 바꾸면 조정된다.
        /// </summary>
        public const long AssumedClearIntervalSec = 60;

        /// <summary>온라인 대비 효율 50% = ÷2(기획서 확정).</summary>
        public const long EfficiencyDivisor = 2;
    }

    /// <summary>소모품 버프.</summary>
    public static class Consumable
    {
        /// <summary>
        /// 버프 누적 지속시간 상한(24시간). 같은 종류를 반복 사용해 무한히 쌓는 것을 막는다(기획서 §4.2).
        /// 초과하는 요청은 아이템을 차감하지 않고 거부한다.
        /// </summary>
        public const long BuffDurationCapSec = 24 * DateTimeUtil.SecondsPerHour;
    }

    /// <summary>큐브(합성·분해·제작) 경험치 산식.</summary>
    public static class Cube
    {
        /// <summary>합성: 입력 등급 × 이 값 = 획득 큐브 경험치.</summary>
        public const int CombineExpPerGrade = 50;

        /// <summary>분해: 아이템당 등급 × 이 값 × 개수.</summary>
        public const int DismantleExpPerGrade = 20;

        /// <summary>제작: 1회 고정 획득 경험치.</summary>
        public const int CraftExp = 20;
    }

    /// <summary>뽑기(가챠).</summary>
    public static class Gacha
    {
        /// <summary>기록 조회 기본 페이지 크기(뽑기 건수).</summary>
        public const int HistoryDefaultLimit = 20;

        /// <summary>기록 조회 페이지 크기 상한. 과대 응답 방어를 위해 서버가 강제한다(기획서 §5.4).</summary>
        public const int HistoryMaxLimit = 50;

        /// <summary><c>gacha_pity_rule.pity_type</c> 1:소프트 천장(확률 상승).</summary>
        public const int PityTypeSoft = 1;

        /// <summary><c>gacha_pity_rule.pity_type</c> 2:하드 천장(확정).</summary>
        public const int PityTypeHard = 2;
    }

    /// <summary>거래소(trade 기획서 §4·§7).</summary>
    public static class Trade
    {
        /// <summary>거래 수수료 20% — 판매자는 판매가의 80%를 받는다(§4 확정).</summary>
        public const double SellerShare = 0.8;

        /// <summary>계정당 동시 등록(판매중) 한도.</summary>
        public const int ListingLimit = 10;

        /// <summary>등록 유효기간 3일.</summary>
        public const long ListingDurationSeconds = 3 * DateTimeUtil.SecondsPerDay;

        /// <summary>목록 페이지 크기 기본값(§7.2 — 과대 응답 방어).</summary>
        public const int DefaultPageSize = 50;

        /// <summary>목록 페이지 크기 상한.</summary>
        public const int MaxPageSize = 100;

        /// <summary>
        /// 목록 페이징의 OFFSET 상한. OFFSET 은 건너뛸 행을 실제로 세므로, 깊은 페이지 요청이 색인을 통째로
        /// 훑지 않도록 막는다(pageSize 100 기준 100페이지). 상한을 넘는 page 는 거부하지 않고 clamp 한다.
        /// </summary>
        public const int MaxOffset = 10_000;

        /// <summary><c>trade_listing.status</c> 1:판매중.</summary>
        public const int StatusOnSale = 1;

        /// <summary>2:판매 완료.</summary>
        public const int StatusSold = 2;

        /// <summary>3:판매자 취소.</summary>
        public const int StatusCancelled = 3;

        /// <summary>4:기간 만료로 자동 종료(만료 배치). 수동 취소(3)와 구분해 사유를 남긴다.</summary>
        public const int StatusExpired = 4;
    }

    /// <summary>메일(mail 기획서 6.5).</summary>
    public static class Mail
    {
        /// <summary>보관 기간(발급 후 7일 확정) — 이 시간이 지난 메일이 GC 배치의 삭제 대상이다.</summary>
        public const long RetentionSeconds = 7 * DateTimeUtil.SecondsPerDay;
    }

    /// <summary>보스러시(boss-rush 기획서 4장).</summary>
    public static class BossRush
    {
        /// <summary><c>boss_rush_master</c> 단일 행 키. 콘텐츠가 하나라 고정 1이다.</summary>
        public const int ContentId = 1;

        /// <summary>랭킹 목록 기본 페이지 크기(요청이 limit을 생략했을 때).</summary>
        public const int RankDefaultLimit = 50;

        /// <summary>
        /// 리더보드 점수 인코딩 배수 — 하위 7자리를 시즌 상대 초(시즌 7일 = 604,800초 &lt; 10^7)에 내준다.
        /// 이 인코딩은 클리어 시간 상한에 기대지 않는다(제한 시간을 없앤 근거, 기획서 4.3).
        /// </summary>
        public const long ScoreScale = 10_000_000L;
    }

    // ────────────────────────────────────────────────────────────────
    // 인프라 — 배치 · Redis 키 · 인증 · 로깅
    // ────────────────────────────────────────────────────────────────

    /// <summary>주기 배치(<c>PeriodicBatchScheduler</c> 파생)의 기본값. 실제 주기·상한은 appsettings가 덮어쓴다.</summary>
    public static class Batch
    {
        /// <summary>
        /// 다음 주기 대기의 하한(1초). <b>0·음수 대기로 루프가 쉬지 않고 도는 것만</b> 막는 안전장치다.
        /// 크게 잡으면 발화 시각이 코앞인 정상 대기까지 뒤로 밀려 정확도가 깨진다.
        /// </summary>
        public static readonly TimeSpan MinDelay = TimeSpan.FromSeconds(1);

        /// <summary>
        /// 리더 락 TTL의 상한(5분). 1주기 실행 시간의 상한으로 잡는다 — 1주기가 <c>BatchSize</c>로 제한된
        /// 짧은 트랜잭션의 반복이라 수 초 규모이므로 5분은 충분한 여유다.
        /// </summary>
        public static readonly TimeSpan MaxLockTtl = TimeSpan.FromMinutes(5);

        /// <summary>보스러시 시즌 정산 배치(appsettings "BossRushSeasonBatch").</summary>
        public static class BossRushSeason
        {
            /// <summary>
            /// 기본 주기 10분. 이 배치는 <b>정상 경로에서 이 주기로 돌지 않는다</b> — 진행 중 시즌이 있으면 그
            /// 종료 시각까지, 없으면 무기한 자기 때문이다. 남은 쓰임은 ①리더 락 TTL 산정과 ②주기가 실제로 돌지
            /// 못했을 때(리더 락 스킵·예외)의 재시도 간격이다.
            /// </summary>
            public const int DefaultIntervalSeconds = 10 * 60;

            /// <summary>1주기(정산 페이지) 처리 상한. 페이지 단위 트랜잭션으로 쪼개 긴 잠금을 만들지 않는다.</summary>
            public const int DefaultBatchSize = 500;

            /// <summary>
            /// 종료 시각이 이미 지났는데도 정산되지 않은 시즌이 남아 있을 때(다른 인스턴스가 정산 중이라 리더 락을
            /// 놓쳤거나 직전 주기가 실패한 경우)의 재시도 간격. 그 상황에서만 쓰이므로 짧게 잡아도 안전하다.
            /// </summary>
            public static readonly TimeSpan PastDueRetryDelay = TimeSpan.FromMinutes(1);

            /// <summary>종료된 시즌 리더보드에 거는 TTL(7일). 과거 키가 무한히 쌓이지 않게 한다(기획서 4.3).</summary>
            public static readonly TimeSpan ClosedSeasonTtl = TimeSpan.FromDays(7);

            /// <summary>기상 시각을 아는 배치이므로 대기 상한을 사실상 풀어 둔다(주기에 잘려 폴링으로 돌아가지 않게).</summary>
            public static readonly TimeSpan MaxDelay = TimeSpan.FromDays(7);
        }

        /// <summary>메일 GC 배치(appsettings "MailGcBatch").</summary>
        public static class MailGc
        {
            /// <summary>기본 실행 주기 1시간.</summary>
            public const int DefaultIntervalSeconds = (int)DateTimeUtil.SecondsPerHour;

            /// <summary>1회 처리 상한.</summary>
            public const int DefaultBatchSize = 500;
        }

        /// <summary>거래소 만료 배치(appsettings "TradeExpireBatch").</summary>
        public static class TradeExpire
        {
            /// <summary>기본 실행 주기 1시간. 만료 효력은 읽기 경로가 즉시 내므로 이 주기는 <b>반송 지연 상한</b>일 뿐이다.</summary>
            public const int DefaultIntervalSeconds = (int)DateTimeUtil.SecondsPerHour;

            /// <summary>
            /// 1주기 처리 상한. 주기(1시간)보다 넉넉히 잡아, 서버가 한동안 내려가 있다 올라왔을 때 밀린 만료 물량을
            /// 한 주기에 소화하게 한다. 초과분은 다음 주기로 이월된다.
            /// </summary>
            public const int DefaultBatchSize = 1000;
        }
    }

    /// <summary>Redis 키. 배치 리더 락(<c>batch:lock:{BatchKey}</c>)은 <c>BatchLock</c>이 조립한다.</summary>
    public static class RedisKey
    {
        /// <summary>닉네임 캐시 키(시즌·콘텐츠 무관 전역 Hash, TTL 없음).</summary>
        public const string PlayerNickname = "player:nickname";

        /// <summary>현재 보스러시 시즌 메타 캐시 키(Hash).</summary>
        public const string BossRushCurrentSeason = "bossrush:season:current";
    }

    /// <summary>인증(게임 서버 미들웨어).</summary>
    public static class Auth
    {
        /// <summary>인증된 userId를 컨트롤러로 전달하는 <c>HttpContext.Items</c> 키.</summary>
        public const string UserIdItemKey = "userId";
    }

    /// <summary>이벤트 로그(집계용, 로그 이벤트 정의 4장). 구현은 <c>Logging/EventLogger.cs</c>.</summary>
    public static class EventLog
    {
        /// <summary>
        /// 이벤트 로그 전용 로거 카테고리. <c>Program.cs</c>가 이 이름으로 sink를 가른다 —
        /// 바꾸면 필터도 함께 바꿔야 한다.
        /// </summary>
        public const string Category = "TaskbarHero.EventLog";

        /// <summary>
        /// 이벤트 로그 태그(로그 이벤트 정의 4.3). 태그가 곧 적재 테이블(<c>도메인.동작</c> →
        /// <c>도메인_동작_logs</c>)이므로, <b>메서드 이름에서 자동으로 만들지 않고 여기서 상수로 명시</b>한다 —
        /// 리팩터링이 적재 대상을 바꾸는 결합을 만들지 않기 위해서다(4.2).
        /// </summary>
        public static class Tags
        {
            /// <summary>접속 시 세이브 로드 → <c>save_load_logs</c>. 이 체계의 세션 개시 이벤트(DAU·리텐션의 근거).</summary>
            public const string SaveLoad = "save.load";

            /// <summary>최초 캐릭터 생성(계정 세이브 초기화) → <c>player_create_logs</c>.</summary>
            public const string PlayerCreate = "player.create";

            /// <summary>
            /// 스테이지 진입 → <c>stage_enter_logs</c>. 실패(전멸) 보고와 짝지어 실질 난이도를 재는
            /// 분모이고, 보고 없이 사라진 판(이탈)을 세는 기준이기도 하다(5.3).
            /// </summary>
            public const string StageEnter = "stage.enter";

            /// <summary>스테이지 클리어(보상 지급 확정) → <c>stage_clear_logs</c>. 이 체계에서 가장 빈번한 이벤트다.</summary>
            public const string StageClear = "stage.clear";

            /// <summary>스테이지 실패(파티 전멸, 클라이언트 보고) → <c>stage_fail_logs</c>.</summary>
            public const string StageFail = "stage.fail";

            /// <summary>
            /// 캐릭터 레벨 상승 → <c>character_levelup_logs</c>. 스테이지·오프라인이 공통으로 내며
            /// <c>source</c> 필드로 갈린다(5.3).
            /// </summary>
            public const string CharacterLevelUp = "character.levelup";

            /// <summary>
            /// 오프라인(방치) 보상 정산 → <c>offline_claim_logs</c>. 골드 유입은 원장에도 남지만
            /// <b>상한에 걸렸는지는 원장이 모른다</b> — 그 한 컬럼이 이 테이블을 따로 두는 이유다(5.4).
            /// </summary>
            public const string OfflineClaim = "offline.claim";

            /// <summary>
            /// 장비 강화 +1 → <c>item_enhance_logs</c>. <b>재화 부족 거부까지 남긴다</b> —
            /// 어느 단계에서 골드가 막히는지가 강화 곡선을 조정하는 근거다(5.5).
            /// </summary>
            public const string ItemEnhance = "item.enhance";

            /// <summary>
            /// 뽑기 결과 <b>1개당 1행</b> → <c>gacha_pull_item_logs</c>. 요청 단위 값(1연/10연·비용)은
            /// 컬럼으로 두지 않고 같은 <c>pull_id</c>의 행 수와 원장에서 파생한다(5.6).
            /// </summary>
            public const string GachaPullItem = "gacha.pull_item";
        }
    }
}
