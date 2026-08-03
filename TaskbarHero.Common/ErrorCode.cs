namespace TaskbarHero.Common
{
    /// <summary>
    /// 서버-클라이언트 공통 에러 코드.
    /// 숫자 값은 클라이언트와의 계약(contract)이므로 변경하지 않는다.
    /// 도메인별 1000번 블록으로 분리하며, 폐기된 값은 결번으로 두고 재사용하지 않는다.
    /// 정본 목록: docs/공통/error-code-정의.md
    /// </summary>
    public enum ErrorCode
    {
        Success = 0,

        // 계정 / 인증 (1000번대)
        UserNotFound = 1001,
        InvalidPassword = 1002,
        DuplicateEmail = 1003,
        InvalidToken = 1004,
        ExpiredToken = 1005,
        InvalidRequest = 1006,

        // 세이브 데이터 (2000번대)
        SaveNotFound = 2001,
        InvalidSaveData = 2002,
        // 2003: 구 SaveVersionMismatch — data_version 제거로 폐기(결번, 재사용 금지)
        PlayerAlreadyExists = 2004,
        InvalidClassCode = 2005,
        InvalidCharacterId = 2006,
        InvalidGender = 2007,
        CannotRemoveLastCharacter = 2008,
        CharacterNotFound = 2009,
        PartySlotOccupied = 2010,

        // 오프라인 보상 정산 (3000번대)
        NoOfflineReward = 3001,
        OfflineRewardAlreadyClaimed = 3002,

        // 인벤토리 / 아이템 / 큐브 (4000번대)
        ItemNotFound = 4001,
        InventoryFull = 4002,
        ItemNotEquippable = 4003,
        MaxEnhanceReached = 4004,
        InsufficientCurrency = 4005,
        InsufficientQuantity = 4006,
        ItemEquipped = 4007,
        InventoryCapacityMax = 4008,
        InvalidInventorySlot = 4009,
        CubeRecipeNotMet = 4010,
        CubeLevelInsufficient = 4011,
        // 4012(구 InventoryRevisionChanged): 가방 페이지 조회의 정합성 검증 장치를 제거하며 폐기(결번, 재사용 금지)

        // 소모품 / 버프 (4020~4029)
        ItemNotConsumable = 4020,
        BuffDurationLimitExceeded = 4021,

        // 성장 (5000번대)
        InvalidGrowthTarget = 5001,
        SkillMaxLevel = 5002,
        InsufficientSkillPoint = 5003,
        SkillClassMismatch = 5004,
        SkillNotActive = 5005,
        SkillNotLearned = 5006,
        ActiveSkillLimitExceeded = 5007,
        RunePrereqNotMet = 5010,
        RuneMaxLevel = 5011,

        // 스테이지 / 전투 결과 (6000번대)
        StageNotFound = 6001,
        StageLocked = 6002,
        StageNotEntered = 6003,
        // 6004: 결번(재사용 금지)

        // 거래소 / 교역선 (7000번대)
        TradeListingNotFound = 7001,
        TradeNotSellable = 7002,
        TradeNotOwner = 7003,
        TradeSelfPurchase = 7004,
        TradeAlreadyClosed = 7005,
        TradePriceOutOfRange = 7006,
        TradeListingLimitExceeded = 7007,
        TradeBusy = 7008,

        // 메일(보상) (8000번대)
        MailNotFound = 8001,
        MailAlreadyClaimed = 8002,
        MailExpired = 8003,

        // 출석부 보상 (9000번대)
        AttendanceAlreadyClaimed = 9001,
        // 9002(구 AttendanceAllClaimed): 30일차 이후 1일차부터 순환하도록 바뀌어 사다리 소진 실패가 없어짐(결번)

        // 마스터 데이터 (10000번대)
        MasterDataNotLoaded = 10001,
        // 10002(구 MasterDataVersionMismatch)·10005(구 InvalidMasterRequest): 마스터 클라 번들 전환으로 폐기(결번)

        // 공통 / 시스템 (11000번대)
        // 특정 도메인에 속하지 않는 서버 내부 오류. 전역 예외 처리기가 미처리 예외를 이 코드로 일반화해 응답한다.
        ServerError = 11001,

        // 가챠(뽑기) (12000번대 — 도메인 4.11이나 11000번대를 공통/시스템이 선점해 12000번대 할당)
        GachaNotFound = 12001,
        GachaPoolEmpty = 12002,
        GachaNotAvailable = 12003,
    }
}
