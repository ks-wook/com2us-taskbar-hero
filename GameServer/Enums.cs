namespace GameServer;

// 리포지토리 트랜잭션·조회 결과 상태 enum 모음.
// 각 enum과 짝이 되는 *Outcome 레코드는 해당 리포지토리 파일(Repositories/)에,
// I*Repository 인터페이스는 Repositories/Interfaces/에 있다.

// ── 출석(AttendanceRepository) ──

public enum AttendanceClaimStatus
{
    Ok,
    NoPlayer,       // player_attendance 행 없음 = 계정 세이브 미생성(캐릭터 생성 시 함께 만든다)
    AlreadyClaimed, // 오늘자 출석을 이미 수령(동시 요청 경합 포함)
    RewardNotFound, // 산출된 일차의 보상이 attendance_master에 없거나 사다리가 비어 있음(마스터 결함)
}

// ── 보스러시(BossRushRepository) ──

public enum BossRushEnterStatus
{
    Ok,
    NoPlayer,       // game_player 행 없음 = 계정 세이브 미생성
    Locked,         // 해금 조건 미달(max_stage_cleared < unlock_stage_sequence)
    SeasonClosed,   // 진행 중 시즌 없음(정산 중)
}

public enum BossRushClearStatus
{
    Ok,
    RunNotFound,      // runId 없음 또는 타인 런
    AlreadyFinished,  // 이미 종결된 런(중복 보고·동시 요청의 패자·만료 판정에 걸린 런)
}

// ── 소모품(ConsumableRepository) ──

public enum ConsumableUseStatus
{
    Ok,
    ItemNotFound,          // 대상 행이 계정에 없음(또는 재화 행)
    NotConsumable,         // item_type이 소모품(4)이 아님
    InsufficientQuantity,  // 보유 수량 0
    MasterNotDefined,      // consumable_master에 효과 정의가 없음
    DurationLimitExceeded, // 누적 지속시간 상한 초과
}

// ── 큐브(CubeRepository) ──

public enum CombineStatus
{
    Ok,
    ItemNotFound,   // 입력 아이템 일부가 계정에 없음(또는 재화 행)
    ItemEquipped,   // 입력 중 장착 중인 아이템
    RecipeNotMet,   // 등급/슬롯/클래스 불일치·개수·등급 상한 등 조건 미충족
}

public enum DismantleStatus
{
    Ok,
    ItemNotFound,         // 대상 아이템 없음(또는 재화 행)
    ItemEquipped,         // 장착 중이라 분해 불가
    InsufficientQuantity, // 요청 수량이 보유 수량 초과
}

public enum CraftStatus
{
    Ok,
    CubeLevelInsufficient, // 큐브 레벨이 요구치 미만
    InsufficientCurrency,  // 비용 골드 부족
    RecipeNotMet,          // 소모 재료 부족
    InventoryFull,         // 결과 아이템 적재 용량 부족
}

// ── 뽑기(GachaRepository) ──

/// <summary>뽑기 처리 결과 상태(가챠 기획서 §6.5·6.7).</summary>
public enum GachaPullStatus
{
    Ok,
    InsufficientCurrency, // 비용 재화 부족(차감 전 검증이라 상태 변화 없음)
    PoolEmpty,            // 추첨된 등급 슬롯에 후보가 없음(마스터 결함) → 전체 롤백
    InventoryFull,        // 지급 아이템을 적재할 빈 칸 부족 → 전체 롤백(골드도 돌아온다)
}

// ── 성장(GrowthRepository) ──

/// <summary>스킬 레벨업 트랜잭션 결과 상태.</summary>
public enum SkillLevelUpStatus
{
    Ok,
    InvalidCharacter,   // player_character 슬롯 없음
    SkillNotFound,      // 존재하지 않는 스킬 코드
    ClassMismatch,      // 대상 캐릭터 직업 소속이 아닌 스킬
    MaxLevel,           // 이미 최대 레벨
    InsufficientPoint,  // 사용 가능 스킬 포인트 부족
}

/// <summary>스킬 초기화 트랜잭션 결과 상태.</summary>
public enum SkillResetStatus
{
    Ok,
    InvalidCharacter,
}

/// <summary>액티브 스킬 장착 트랜잭션 결과 상태.</summary>
public enum SkillEquipStatus
{
    Ok,
    InvalidCharacter,
    SkillNotFound,   // 존재하지 않는 스킬 코드
    ClassMismatch,   // 대상 캐릭터 직업 소속 아님
    NotActive,       // 패시브를 장착 시도
    NotLearned,      // 레벨 0(미습득) 스킬을 장착 시도
    LimitExceeded,   // 액티브 장착 한도(2개) 초과
}

/// <summary>룬 업그레이드 트랜잭션 결과 상태.</summary>
public enum RuneUpgradeStatus
{
    Ok,
    PrereqNotMet,          // 선행 룬 미해금(레벨 0)
    MaxLevel,              // 이미 최대 레벨
    InsufficientCurrency,  // 골드 부족
}

// ── 인벤토리(InventoryRepository) ──

/// <summary>장착 트랜잭션 결과 상태.</summary>
public enum EquipStatus
{
    Ok,
    InvalidCharacter, // player_character 슬롯 없음
    ItemNotFound,     // 인벤토리에 해당 아이템 없음
    ItemEquipped,     // 이미 어딘가에 장착 중
    NotEquippable,    // 장비 아님·슬롯/클래스/레벨 부적합
    InventoryFull,    // 스왑으로 밀려난 기존 장비를 되돌릴 빈 칸이 없음(방어적, 통상 발생하지 않음)
}

/// <summary>장착 해제 트랜잭션 결과 상태.</summary>
public enum UnequipStatus
{
    Ok,
    InvalidCharacter, // player_character 슬롯 없음
    NotEquipped,      // 해당 캐릭터-슬롯에 장착된 장비 없음
    InventoryFull,    // 가방에 되돌릴 빈 칸이 없음
}

/// <summary>배치 이동 트랜잭션 결과 상태.</summary>
public enum MoveStatus
{
    Ok,
    ItemNotFound, // 대상 아이템이 인벤토리에 없음(또는 재화 행)
    InvalidSlot,  // toSlot이 용량 범위 밖
}

/// <summary>강화 판정(마스터) 결과 상태. 리포지토리가 델리게이트로 받아 트랜잭션 안에서 사용한다.</summary>
public enum EnhancePlanStatus
{
    Ok,
    NotEquippable, // 장비가 아님(재료·소모품·재화 등)
    MaxReached,    // 다음 강화 단계가 enhance_master에 없음
}

/// <summary>장비 강화 트랜잭션 결과 상태.</summary>
public enum EnhanceStatus
{
    Ok,
    ItemNotFound,         // 계정 소유 아이템이 아님
    NotEquippable,        // 장비가 아님
    MaxEnhanceReached,    // 최대 강화 단계 도달
    InsufficientCurrency, // 비용 재화 부족
}

/// <summary>인벤토리 용량 확장 트랜잭션 결과 상태.</summary>
public enum ExpandStatus
{
    Ok,
    NoPlayer,             // game_player 없음(세이브 미생성)
    CapacityMax,          // 이미 상한이라 더 확장 불가
    InsufficientCurrency, // 골드 부족
}

/// <summary>인벤토리 조회 결과 상태.</summary>
public enum InventoryPageStatus
{
    Ok,
    NoPlayer, // game_player 없음(세이브 미생성)
}

// ── 메일(MailRepository) ──

public enum MailClaimStatus
{
    Ok,
    MailNotFound,       // 메일 없음 또는 타인 메일(존재 노출 안 함)
    MailAlreadyClaimed, // 이미 수령(동시 요청 경합 포함)
    MailExpired,        // 만료되어 수령 불가
    InventoryFull,      // 첨부 아이템 적재 용량 부족
}

// ── 오프라인 보상(OfflineRepository) ──

/// <summary>오프라인 정산 트랜잭션 결과 상태.</summary>
public enum OfflineClaimStatus
{
    Ok,
    NoPlayer,       // game_player 없음(세이브 미생성)
    AlreadyClaimed, // 잠금 후 재확인 시 경과가 최소 기준 미만이거나 CAS 실패(동시 중복 요청으로 이미 정산됨)
}

// ── 세이브(SaveRepository) ──

/// <summary>캐릭터 추가 생성 트랜잭션 결과 상태.</summary>
public enum AddCharacterStatus
{
    Ok,
    InsufficientCurrency, // 생성 비용 골드 부족
    DuplicateConflict,    // 식별자/직업 유니크 경합(동시 생성)
}

/// <summary>파티 편성 저장 트랜잭션 결과 상태.</summary>
public enum ArrangePartyStatus
{
    Ok,
    CharacterNotFound, // 편성 목록에 계정이 보유하지 않은 캐릭터가 있음
}

// ── 스테이지(StageRepository) ──

/// <summary>클리어 트랜잭션 결과 상태.</summary>
public enum ClearStatus
{
    Ok,
    NoPlayer,      // game_player 없음(세이브 미생성)
    NotEntered,    // 현재 진입 스테이지와 요청 불일치
}

// ── 거래소(TradeRepository) ──

public enum TradeRegisterStatus
{
    Ok,
    ItemNotFound,        // 인벤토리에 없음(타인 아이템·이미 등록 중 포함)
    ItemEquipped,        // 장착 중
    NotSellable,         // item_master.sellable=0
    PriceOutOfRange,     // 기준가 ±20% 밖
    ListingLimitExceeded, // 계정 동시 등록 한도 초과
}

public enum TradeCloseStatus
{
    Ok,
    ListingNotFound, // 등록 없음
    AlreadyClosed,   // 이미 판매/취소(동시 경합 포함)
    NotOwner,        // 본인 등록 아님(취소)
    SelfPurchase,    // 자기 등록 구매
    InsufficientGold,
    InventoryFull,
}
