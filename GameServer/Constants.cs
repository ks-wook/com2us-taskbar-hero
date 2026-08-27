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
        /// <summary>1062 중복 키 — 유니크 제약 경합(동시 생성·시즌 개시·가방 칸 선점)을 구분하는 데 쓴다.</summary>
        public const int DuplicateEntry = 1062;

        /// <summary>
        /// 1213 교착 — 두 트랜잭션이 서로가 쥔 행을 기다려 InnoDB가 한쪽을 되돌린 경우.
        /// 되돌려진 쪽은 다시 실행하면 되므로 <see cref="Db.MaxTransactionAttempts"/> 재시도 대상이다.
        /// </summary>
        public const int Deadlock = 1213;

        /// <summary>1205 잠금 대기 시간 초과 — 교착과 같은 성격(경합)이라 같은 재시도 대상이다.</summary>
        public const int LockWaitTimeout = 1205;
    }

    /// <summary>세이브 DB 트랜잭션 운영 값.</summary>
    public static class Db
    {
        /// <summary>
        /// 경합으로 되돌아간 트랜잭션을 <b>처음 실행을 포함해</b> 최대 몇 번까지 시도할지.
        /// <para>같은 트랜잭션 안에서 다시 읽어 봐야 소용이 없어서 트랜잭션째 다시 실행한다 — InnoDB 기본
        /// 격리 수준(REPEATABLE READ)에서 잠금 없는 조회는 트랜잭션이 처음 읽은 시점의 스냅샷을 계속 보므로,
        /// 그 사이 다른 요청이 커밋한 결과가 보이지 않는다. 새 트랜잭션이라야 스냅샷도 새로 잡힌다.</para>
        /// <para>3인 이유는 경합이 같은 계정의 요청끼리만 일어나 동시 요청 수가 애초에 작기 때문이다.
        /// 여기서도 실패하면 재시도로 풀 문제가 아니므로 예외를 그대로 올린다.</para>
        /// </summary>
        public const int MaxTransactionAttempts = 3;
    }

    // ────────────────────────────────────────────────────────────────
    // 마스터 코드
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>mail_master.category</c>(= 발급된 <c>player_mail.category</c>). 템플릿 코드가
    /// <c>category × 100 + 순번</c> 규약이라 이 값이 곧 템플릿의 백 자리다.
    /// </summary>
    public static class MailCategory
    {
        /// <summary>
        /// 1:운영. 지금 이 분류로 발급되는 메일은 <see cref="MailTemplate.NewbieReward"/>(신규 가입 지원금)뿐이라,
        /// <b>아이템 원장이 수령 시점에 유입 사유를 <c>newbie_grant</c>로 가르는 기준</b>으로 쓴다(6.2).
        /// <c>player_mail</c>에는 템플릿 코드가 남지 않고(문구·category가 이미 스냅샷이다) 분류만 남기 때문이다.
        /// 운영 지급 등 다른 1xx 템플릿을 추가하면 그 기준을 함께 손봐야 한다.
        /// </summary>
        public const int Operation = 1;

        /// <summary>2:거래(거래소 구매 아이템·판매 대금·만료 반송).</summary>
        public const int Trade = 2;

        /// <summary>3:출석.</summary>
        public const int Attendance = 3;

        /// <summary>4:시스템.</summary>
        public const int System = 4;

        /// <summary>5:랭킹(보스러시 시즌 순위 보상).</summary>
        public const int Rank = 5;
    }

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

    /// <summary>플레이어 계정(<c>game_player</c>) 기준값.</summary>
    public static class Player
    {
        /// <summary>
        /// 닉네임 최대 길이. 클라이언트 입력 UI가 이 길이까지만 보내므로 더 긴 값은 조작이거나 클라 버그다.
        /// <c>game_player.nickname</c>은 VARCHAR(50)이라 이 검사가 DB 길이 오류보다 앞에서 걸러 준다.
        /// 계정 서버의 회원가입 닉네임 검증(<c>AccountServer</c> <c>Constants.Auth.MaxNicknameLength</c>)과 같은 값이어야 한다.
        /// </summary>
        public const int NicknameMaxLength = 12;
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

        /// <summary>
        /// 리더보드 멤버(user_id) 문자열의 고정 자릿수. <b>동점자 순서를 MySQL과 일치시키기 위한 값</b>이다 —
        /// Redis는 점수가 같으면 멤버를 <b>사전순</b>으로 비교하는데, 자릿수가 다르면 "10" &lt; "9"가 되어
        /// user_id 오름차순(MySQL의 동점 기준)과 어긋난다. 0으로 채워 자릿수를 맞추면 사전순 = 숫자순이 된다.
        /// <c>user_id</c>가 BIGINT이므로 최대 자릿수(19)에 여유 1을 더한 20으로 둔다.
        /// </summary>
        public const int RankMemberDigits = 20;

        /// <summary>
        /// 랭킹 캐시 워밍업(관리 API)이 <c>boss_rush_record</c>를 읽는 페이지 크기. 한 번에 다 읽지 않고
        /// 쪼개는 이유는 시즌 등재 인원이 늘어도 메모리 사용량이 이 크기에 묶이기 때문이다.
        /// </summary>
        public const int RankWarmupPageSize = 500;

        /// <summary>
        /// 리더보드 적재 완료 마커(<see cref="RedisKey.BossRushLeaderboardReadyFormat"/>)에 넣는 값.
        /// 키의 존재 자체가 신호라 값은 무엇이든 되지만, 비워 두면 Redis에서 눈으로 확인하기 어려워 1을 쓴다.
        /// </summary>
        public const string RankReadyMarkerValue = "1";

        /// <summary>
        /// 리더보드 자동 재적재 락의 수명(2분). 재적재 도중 프로세스가 죽어 락을 풀지 못해도 이 시간이 지나면
        /// 다른 요청이 다시 시도할 수 있다. 시즌 등재 인원이 늘어 재적재가 이보다 오래 걸리면 두 인스턴스가
        /// 겹칠 수 있으나, 적재는 member 단위 덮어쓰기(ZADD)라 겹쳐도 결과가 같다.
        /// </summary>
        public static readonly TimeSpan RankRebuildLockTtl = TimeSpan.FromMinutes(2);

        /// <summary>
        /// 랭킹 캐시 워밍업 결과 상태값(관리 API 응답 <c>status</c>). 외부 스크립트가 이 문자열로
        /// 성공/재적재 여부를 판정하므로 <b>값을 바꾸면 스크립트도 함께 고쳐야 한다</b>
        /// (<c>server_up_with_docker.py</c>).
        /// </summary>
        public static class RankWarmupStatus
        {
            /// <summary>진행 중 시즌이 없어 적재할 대상이 없다(정상, 재적재 0건).</summary>
            public const string NoSeason = "no-season";

            /// <summary>적재 완료 마커가 있어(= 전량 적재된 리더보드) 재구축을 건너뛰었다(정상).</summary>
            public const string AlreadyWarm = "already-warm";

            /// <summary>MySQL에서 읽어 리더보드를 재구축했다.</summary>
            public const string Restored = "restored";

            /// <summary>Redis에 접근할 수 없어 적재하지 못했다.</summary>
            public const string CacheUnavailable = "cache-unavailable";

            /// <summary>적재 중 예외가 나 중단됐다(서비스가 잡아 이 값으로 바꾼다). 위 값과 함께 실패로 본다.</summary>
            public const string Failed = "failed";
        }

        /// <summary>
        /// 이벤트 로그가 라운드 기록을 펼쳐 담는 컬럼 수(<c>round1_ms</c> ~ <c>round5_ms</c>). 라운드 수가
        /// 5로 고정이라 자식 테이블 대신 컬럼으로 둔 구조이며(로그 이벤트 정의 5.10), 마스터의 라운드 수가
        /// 바뀌면 로그 스키마(<c>bossrush_clear_logs</c>)도 함께 고쳐야 한다.
        /// </summary>
        public const int RoundLogColumns = 5;
    }

    // ────────────────────────────────────────────────────────────────
    // 인프라 — Redis 키 · 인증 · 로깅
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Redis 키. 인증 토큰·보스러시 랭킹 캐시에 쓴다(배치 리더 락은 없앴다 — BatchServer가 1대다).
    /// 남은 락은 랭킹 캐시 재적재용 하나뿐이며, 그것은 <b>게임 API가 N대로 뜨기 때문</b>에 필요하다.
    /// </summary>
    public static class RedisKey
    {
        /// <summary>닉네임 캐시 키(시즌·콘텐츠 무관 전역 Hash, TTL 없음).</summary>
        public const string PlayerNickname = "player:nickname";

        /// <summary>현재 보스러시 시즌 메타 캐시 키(Hash).</summary>
        public const string BossRushCurrentSeason = "bossrush:season:current";

        /// <summary>시즌 리더보드 키(Sorted Set). <c>{0}</c> = seasonId.</summary>
        public const string BossRushLeaderboardFormat = "rank:bossrush:{0}";

        /// <summary>
        /// 리더보드 적재 완료 마커 키(String). <c>{0}</c> = seasonId.
        /// <para><b>리더보드 키가 있다는 것만으로는 캐시를 믿을 수 없다</b> — 클리어 보고의 ZADD가 키를
        /// 새로 만들 수 있어, Redis를 껐다 켠 뒤 누군가 기록을 갱신하면 <b>그 한 명만 든 리더보드</b>가
        /// 생긴다. 그 상태는 비어 있는 것보다 위험하다(틀린 순위표가 정상으로 보인다). 그래서 워밍업이
        /// 시즌 기록을 <b>전량</b> 적재했을 때만 이 마커를 세우고, 조회는 마커가 있을 때만 캐시를 쓴다.</para>
        /// </summary>
        public const string BossRushLeaderboardReadyFormat = "rank:bossrush:{0}:ready";

        /// <summary>
        /// 리더보드 자동 재적재 락 키(String, SET NX). <c>{0}</c> = seasonId.
        /// <para>게임 API는 scale-out으로 N대가 뜨므로, 마커 부재를 동시에 감지한 인스턴스들이 저마다
        /// 재적재를 시작하면 같은 ZADD가 중복으로 돈다. 이 키를 잡은 하나만 재적재한다
        /// (BatchServer가 락을 쓰지 않는 것은 인스턴스 1대 고정이라서지, 게임 API에는 그 전제가 없다).</para>
        /// </summary>
        public const string BossRushLeaderboardRebuildLockFormat = "rank:bossrush:{0}:rebuilding";
    }

    /// <summary>인증(게임 서버 미들웨어).</summary>
    public static class Auth
    {
        /// <summary>인증된 userId를 컨트롤러로 전달하는 <c>HttpContext.Items</c> 키.</summary>
        public const string UserIdItemKey = "userId";
    }

    /// <summary>
    /// 운영·개발용 관리 API(<c>/api/admin/**</c>). 게임 인증 미들웨어는 <c>/api/game</c>만 검사하므로
    /// 이 경로는 <b>아래 키 헤더로 스스로 보호한다</b> — 키가 설정돼 있지 않으면 엔드포인트 자체가
    /// 404로 닫힌다(설정하지 않은 서버에 무인증 관리 API가 열려 있는 상태를 만들지 않는다).
    /// </summary>
    public static class Admin
    {
        /// <summary>관리 API 인증 헤더 이름.</summary>
        public const string ApiKeyHeader = "X-Admin-Key";

        /// <summary>관리 API 키를 읽는 설정 경로(appsettings · 환경변수 <c>Admin__ApiKey</c>).</summary>
        public const string ApiKeyConfigPath = "Admin:ApiKey";
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

            /// <summary>판매 등록(에스크로) → <c>trade_register_logs</c>. 가격 범위 거부까지 남긴다(5.7).</summary>
            public const string TradeRegister = "trade.register";

            /// <summary>
            /// 등록이 <b>어떻게든 끝날 때</b>(구매·취소·만료) → <c>trade_close_logs</c>.
            /// 결말 셋을 <c>outcome</c> 한 컬럼으로 갈라 한 테이블에 담는다 — 핵심 질문인 미체결률이
            /// 세 결말의 비율이라 <c>GROUP BY outcome</c> 한 줄로 나오기 때문이다(5.7).
            /// </summary>
            public const string TradeClose = "trade.close";

            /// <summary>
            /// 서버가 메일을 발급하는 <b>모든 지점</b> → <c>mail_issue_logs</c>. 메일이 이 게임의 재화 지급
            /// 관문이라, 발급 − 수령의 차액이 곧 <b>아직 경제에 풀리지 않은 재화</b>다(5.8).
            /// </summary>
            public const string MailIssue = "mail.issue";

            /// <summary>
            /// 오늘자 출석 보상 획득 → <c>attendance_claim_logs</c>. <c>mail.issue</c>와 겹치지만
            /// 이 도메인의 질문인 <b>일차별 이탈</b>의 축(<c>day</c>)이 메일 로그에는 없어 따로 둔다(5.9).
            /// </summary>
            public const string AttendanceClaim = "attendance.claim";

            /// <summary>도전 개시 → <c>bossrush_enter_logs</c>. clear와 짝지어 <b>완주율</b>을 낸다(5.10).</summary>
            public const string BossRushEnter = "bossrush.enter";

            /// <summary>클리어 보고 → <c>bossrush_clear_logs</c>. 라운드별 소요가 컬럼 5개로 펼쳐진다(5.10).</summary>
            public const string BossRushClear = "bossrush.clear";

            /// <summary>
            /// 주기 배치 1회 종료 → <c>batch_run_logs</c>. 배치 종류를 테이블이 아니라 <c>batch_key</c>로
            /// 구분한다 — 컬럼 구조가 배치마다 같기 때문이다(5.11).
            /// </summary>
            public const string BatchRun = "batch.run";

            /// <summary>마스터 데이터 적재 완료 → <c>master_load_logs</c>. 배포 후 마스터가 기대대로 올라갔나(5.11).</summary>
            public const string MasterLoad = "master.load";

            /// <summary>서버 기동·종료 → <c>server_lifecycle_logs</c>. 대시보드의 <b>배포 시점 표시</b>로 쓴다(5.11).</summary>
            public const string ServerLifecycle = "server.lifecycle";

            /// <summary>전역 예외 처리기가 미처리 예외를 잡음 → <c>api_error_logs</c>(5.11).</summary>
            public const string ApiError = "api.error";

            /// <summary>
            /// 재화가 움직인 <b>모든 지점</b> → <c>currency_flow_logs</c>(6.1). 도메인 로그와 겹치지만,
            /// "골드가 전체적으로 늘고 있나 줄고 있나"는 이 스트림이 아니면 매번 도메인을 UNION 해야 답한다.
            /// </summary>
            public const string CurrencyFlow = "currency.flow";

            /// <summary>
            /// 아이템이 움직인 <b>모든 지점</b> → <c>item_flow_logs</c>(6.2). 큐브·소모품처럼 도메인 테이블이
            /// 아예 없는 기능은 이 행이 유일한 기록이다.
            /// </summary>
            public const string ItemFlow = "item.flow";

            /// <summary>
            /// 히스토리(주기 스냅샷) 태그 9종(7장). 액션 로그가 <b>변화</b>를 담는 데 반해 이쪽은
            /// <b>총량과 현재 상태</b>를 담으며, 적재 테이블이 자연 키 PK라 같은 주기를 다시 세면 덮어쓴다.
            /// </summary>
            public static class History
            {
                /// <summary>5분 주기 동시 접속 → <c>online_user_history</c>. 하트비트를 로그로 남기지 않고 규모만 센다.</summary>
                public const string OnlineUser = "history.online_user";

                /// <summary>1시간 주기 재화 유통 총량 + 우편함 부채 → <c>currency_supply_history</c>.</summary>
                public const string CurrencySupply = "history.currency_supply";

                /// <summary>1시간 주기 아이템별 호가 → <c>trade_market_history</c>. 체결이 없어도 시세를 본다.</summary>
                public const string TradeMarket = "history.trade_market";

                /// <summary>1일 주기 아이템 유통량 → <c>item_supply_history</c>.</summary>
                public const string ItemSupply = "history.item_supply";

                /// <summary>1일 주기 진행도 분포 → <c>stage_progress_history</c>.</summary>
                public const string StageProgress = "history.stage_progress";

                /// <summary>1일 주기 착용 장비 분포 → <c>equip_item_history</c>. item_supply와 나누면 착용률이 나온다.</summary>
                public const string EquipItem = "history.equip_item";

                /// <summary>1일 주기 파티 조합 분포 → <c>party_comp_history</c>.</summary>
                public const string PartyComp = "history.party_comp";

                /// <summary>1일 주기 액티브 스킬 조합 → <c>skill_build_history</c>.</summary>
                public const string SkillBuild = "history.skill_build";

                /// <summary>1일 주기 스킬 투자 분포 → <c>skill_invest_history</c>.</summary>
                public const string SkillInvest = "history.skill_invest";
            }
        }
    }
}
