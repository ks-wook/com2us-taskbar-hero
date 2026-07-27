using System;
using System.Collections.Generic;

namespace TaskbarHero.Common.Dto
{
    // 게임 서버(/api/game) 요청/응답 DTO. 서버-클라이언트 공유 계약.
    // [Serializable] + public camelCase 필드(JsonUtility 호환, 서버는 IncludeFields=true).

    /// <summary>공통 응답 형식 { success, errorCode, message, data }.</summary>
    [Serializable]
    public class ApiResponse
    {
        public bool success;
        public int errorCode;
        public string message;
        // data는 엔드포인트별 페이로드. 서버는 임의 객체를 담고, 클라이언트는 엔드포인트별 구체 타입으로 파싱한다.
        public object data;
    }

    /// <summary>인증만 필요한(추가 데이터 없는) 게임 API 요청 body. { userId, token }
    /// load·update-last-active 등 payload가 없는 인증 엔드포인트에 사용한다.</summary>
    [Serializable]
    public class AuthRequest
    {
        public long userId;
        public string token;
    }

    /// <summary>캐릭터 생성 요청 body(인증). { userId, token, data:{ nickname, classCode } }</summary>
    [Serializable]
    public class CreateCharacterRequest
    {
        public long userId;
        public string token;
        public CreateCharacterData data;
    }

    [Serializable]
    public class CreateCharacterData
    {
        public string nickname;
        public int classCode;
    }

    /// <summary>
    /// 캐릭터 생성 결과(create-character 응답 data). 2·3번 슬롯은 골드를 소모하며 cost·balance로 소모/잔액을 회신한다.
    /// 최초 생성(1번 슬롯, 계정 초기화)은 무료라 cost.amount=0, balance는 빈 목록이다.
    /// 생성 전 안내 비용은 클라이언트가 마스터(character_create_cost) 번들에서 다음 슬롯 값으로 조회한다.
    /// </summary>
    [Serializable]
    public class CreateCharacterResultData
    {
        public long userId;
        public int characterId;
        public int classCode;
        public int level;
        public CurrencyDto cost = new CurrencyDto();                 // 소모 골드(최초 생성은 amount 0)
        public List<CurrencyDto> balance = new List<CurrencyDto>();  // 차감 후 잔액(최초 생성은 빈 목록)
    }

    // ── load 스냅샷 DTO ──

    [Serializable]
    public class PlayerDto
    {
        public string nickname;
        public int act;
        public int stage;
        public int difficulty;
        public int maxStageCleared;
        public int inventoryCapacity;
        public long lastActiveAt;
    }

    [Serializable]
    public class CharacterDto
    {
        public int characterId;
        public int classCode;
        public int level;
        public long exp;
    }

    [Serializable]
    public class CurrencyDto
    {
        public int currencyType;
        public long amount;
    }

    [Serializable]
    public class InventoryItemDto
    {
        // ⚠️ Unity JsonUtility는 Nullable(int?)를 파싱하지 못하므로 non-nullable + 센티넬로 표현한다.
        public long itemId;
        public int slot;                // 가방 칸(0-based). -1 = 슬롯 없음(장착 중)
        public int itemCode;
        public long quantity;
        public int enhanceLevel;
        public int equippedCharacterId; // 0 = 미장착
        public int equippedSlot;        // 0 = 미장착(장착 시 1~6)
    }

    [Serializable]
    public class SkillDto
    {
        public int characterId;
        public int skillCode;
        public int level;
        public int equipped;
    }

    [Serializable]
    public class RuneDto
    {
        public int runeCode;
        public int level;
    }

    [Serializable]
    public class CubeDto
    {
        public int cubeLevel;
        public long cubeExp;
    }

    [Serializable]
    public class LoadDataDto
    {
        // 신규 계정이면 서버가 { isNew:true }만 반환하므로 true, 기존 계정이면 false(미포함→기본값).
        public bool isNew;
        public PlayerDto player = new PlayerDto();
        public List<CharacterDto> characters = new List<CharacterDto>();
        public List<CurrencyDto> currencies = new List<CurrencyDto>();
        public List<InventoryItemDto> inventory = new List<InventoryItemDto>();
        public List<SkillDto> skills = new List<SkillDto>();
        public List<RuneDto> runes = new List<RuneDto>();
        public CubeDto cube = new CubeDto();
        public long offlineElapsedSec;
    }

    /// <summary>세이브 로드 응답 { success, errorCode, message, data(LoadDataDto) }.</summary>
    [Serializable]
    public class LoadResponse
    {
        public bool success;
        public int errorCode;
        public string message;
        public LoadDataDto data = new LoadDataDto();
    }

    // ── 스테이지 진입 / 클리어 (stage-battle 기획서 §5) ──

    /// <summary>스테이지 진입·클리어 공통 요청 데이터. { act, difficulty, stage }</summary>
    [Serializable]
    public class StageActionData
    {
        public int act;
        public int difficulty;
        public int stage;
    }

    /// <summary>스테이지 진입/클리어 요청 body(인증). { userId, token, data:{ act, difficulty, stage } }</summary>
    [Serializable]
    public class StageActionRequest
    {
        public long userId;
        public string token;
        public StageActionData data;
    }

    /// <summary>스테이지 스폰(일반 몬스터 등장 수) 한 항목.</summary>
    [Serializable]
    public class StageSpawnDto
    {
        public int monsterCode;
        public int count;
    }

    /// <summary>스테이지 보스. 보스가 없는 스테이지면 응답 필드가 null.</summary>
    [Serializable]
    public class StageBossDto
    {
        public int monsterCode;
    }

    /// <summary>진입 응답 데이터(5.1).</summary>
    [Serializable]
    public class StageEnterData
    {
        public int act;
        public int difficulty;
        public int stage;
        public int stageId;
        public List<StageSpawnDto> monsters = new List<StageSpawnDto>();
        public StageBossDto boss;        // 보스 없으면 null
        public int backgroundType;
        public long enteredAt;
    }

    /// <summary>스테이지 진입 응답 { success, errorCode, message, data(StageEnterData) }.</summary>
    [Serializable]
    public class StageEnterResponse
    {
        public bool success;
        public int errorCode;
        public string message;
        public StageEnterData data = new StageEnterData();
    }

    /// <summary>클리어된 스테이지 좌표(5.2 cleared).</summary>
    [Serializable]
    public class ClearedStageDto
    {
        public int act;
        public int difficulty;
        public int stage;
    }

    /// <summary>클리어 보상 전리품 한 항목.</summary>
    [Serializable]
    public class RewardItemDto
    {
        public int itemCode;
        public long quantity;
    }

    /// <summary>클리어 보상(골드·경험치·전리품).</summary>
    [Serializable]
    public class StageRewardsDto
    {
        public long gold;
        public long exp;
        public List<RewardItemDto> items = new List<RewardItemDto>();
    }

    /// <summary>클리어 경험치 반영 후 캐릭터 상태(5.2 characters).</summary>
    [Serializable]
    public class CharacterProgressDto
    {
        public int characterId;
        public int level;
        public long exp;
        public bool isLevelUp;
    }

    /// <summary>갱신된 진행도(5.2 progress).</summary>
    [Serializable]
    public class StageProgressDto
    {
        public int act;
        public int difficulty;
        public int stage;
        public int maxStageCleared;
    }

    /// <summary>클리어 응답 데이터(5.2).</summary>
    [Serializable]
    public class StageClearData
    {
        public ClearedStageDto cleared = new ClearedStageDto();
        public StageRewardsDto rewards = new StageRewardsDto();
        public List<CharacterProgressDto> characters = new List<CharacterProgressDto>();
        public List<CurrencyDto> balance = new List<CurrencyDto>();
        public StageProgressDto progress = new StageProgressDto();
    }

    // ── 인벤토리/아이템 액션 (inventory-item-cube 기획서 §5.1·5.2·5.5) ──

    /// <summary>장착 요청 데이터. { characterId, itemId }</summary>
    [Serializable]
    public class EquipData
    {
        public int characterId;
        public long itemId;
    }

    /// <summary>장착 요청 body(인증). { userId, token, data:{ characterId, itemId } }</summary>
    [Serializable]
    public class EquipRequest
    {
        public long userId;
        public string token;
        public EquipData data;
    }

    /// <summary>장착 해제 요청 데이터. { characterId, slot }(slot은 장착 슬롯 equipped_slot)</summary>
    [Serializable]
    public class UnequipData
    {
        public int characterId;
        public int slot;
    }

    /// <summary>장착 해제 요청 body(인증).</summary>
    [Serializable]
    public class UnequipRequest
    {
        public long userId;
        public string token;
        public UnequipData data;
    }

    /// <summary>배치 이동 요청 데이터. { itemId, toSlot }(toSlot은 0-based 인벤토리 칸)</summary>
    [Serializable]
    public class MoveData
    {
        public long itemId;
        public int toSlot;
    }

    /// <summary>배치 이동 요청 body(인증).</summary>
    [Serializable]
    public class MoveRequest
    {
        public long userId;
        public string token;
        public MoveData data;
    }

    /// <summary>슬롯-아이템 참조(장착/이동 결과 항목). { slot, itemId }</summary>
    [Serializable]
    public class SlotItemDto
    {
        public int slot;
        public long itemId;
    }

    /// <summary>장착 결과(5.1). unequipped는 스왑으로 밀려난 기존 장비(없으면 null).</summary>
    [Serializable]
    public class EquipResultData
    {
        public int characterId;
        public SlotItemDto equipped;
        public SlotItemDto unequipped;   // 스왑 없으면 null
    }

    /// <summary>장착 해제 결과(5.2).</summary>
    [Serializable]
    public class UnequipResultData
    {
        public int characterId;
        public int slot;
        public long itemId;
    }

    /// <summary>배치 이동 결과(5.5). swapped는 목표 칸에 있던 아이템(비어 있었으면 null).</summary>
    [Serializable]
    public class MoveResultData
    {
        public SlotItemDto moved;
        public SlotItemDto swapped;      // 목표 칸이 비어 있었으면 null
    }

    /// <summary>인벤토리 용량 확장 결과(5.4). 확장은 1회당 1칸이며, cost=차감 골드·balance=차감 후 잔액.</summary>
    [Serializable]
    public class ExpandResultData
    {
        public int inventoryCapacity;
        public CurrencyDto cost = new CurrencyDto();
        public List<CurrencyDto> balance = new List<CurrencyDto>();
    }

    /// <summary>인벤토리 용량 확장 응답 { success, errorCode, message, data(ExpandResultData) }.</summary>
    [Serializable]
    public class ExpandResponse
    {
        public bool success;
        public int errorCode;
        public string message;
        public ExpandResultData data = new ExpandResultData();
    }

    /// <summary>스테이지 클리어 응답 { success, errorCode, message, data(StageClearData) }.</summary>
    [Serializable]
    public class StageClearResponse
    {
        public bool success;
        public int errorCode;
        public string message;
        public StageClearData data = new StageClearData();
    }

    // ── 성장(직업/스킬/룬) 액션 (growth 기획서 §5) ──

    /// <summary>스킬 레벨업 요청 데이터. { characterId, skillCode }</summary>
    [Serializable]
    public class SkillLevelUpData
    {
        public int characterId;
        public int skillCode;
    }

    /// <summary>스킬 레벨업 요청 body(인증). { userId, token, data:{ characterId, skillCode } }</summary>
    [Serializable]
    public class SkillLevelUpRequest
    {
        public long userId;
        public string token;
        public SkillLevelUpData data;
    }

    /// <summary>스킬 초기화 요청 데이터. { characterId }</summary>
    [Serializable]
    public class SkillResetData
    {
        public int characterId;
    }

    /// <summary>스킬 초기화 요청 body(인증).</summary>
    [Serializable]
    public class SkillResetRequest
    {
        public long userId;
        public string token;
        public SkillResetData data;
    }

    /// <summary>액티브 스킬 장착 요청 데이터. { characterId, skillCodes(0~2) }</summary>
    [Serializable]
    public class SkillEquipData
    {
        public int characterId;
        public List<int> skillCodes = new List<int>();
    }

    /// <summary>액티브 스킬 장착 요청 body(인증).</summary>
    [Serializable]
    public class SkillEquipRequest
    {
        public long userId;
        public string token;
        public SkillEquipData data;
    }

    /// <summary>룬 업그레이드 요청 데이터. { runeCode }</summary>
    [Serializable]
    public class RuneUpgradeData
    {
        public int runeCode;
    }

    /// <summary>룬 업그레이드 요청 body(인증).</summary>
    [Serializable]
    public class RuneUpgradeRequest
    {
        public long userId;
        public string token;
        public RuneUpgradeData data;
    }

    /// <summary>스킬 레벨업 소모 비용(스킬 포인트). { skillPoint }</summary>
    [Serializable]
    public class SkillPointCostDto
    {
        public int skillPoint;
    }

    /// <summary>스킬 레벨업 결과(5.1). skillPoint = 갱신 후 사용 가능 스킬 포인트(레벨 파생값).</summary>
    [Serializable]
    public class SkillLevelUpResultData
    {
        public int characterId;
        public int skillCode;
        public int level;
        public SkillPointCostDto cost = new SkillPointCostDto();
        public int skillPoint;
    }

    /// <summary>스킬 초기화 결과(5.2). resetSkillCount = 초기화된 스킬 수, skillPoint = 초기화 후 사용 가능 포인트(전액).</summary>
    [Serializable]
    public class SkillResetResultData
    {
        public int characterId;
        public int resetSkillCount;
        public int skillPoint;
    }

    /// <summary>액티브 스킬 장착 결과(5.3). equipped = 설정 후 장착된 액티브 스킬 코드 목록.</summary>
    [Serializable]
    public class SkillEquipResultData
    {
        public int characterId;
        public List<int> equipped = new List<int>();
    }

    /// <summary>룬 업그레이드 결과(5.4). cost = 차감 골드, balance = 차감 후 잔액.</summary>
    [Serializable]
    public class RuneUpgradeResultData
    {
        public int runeCode;
        public int level;
        public CurrencyDto cost = new CurrencyDto();
        public List<CurrencyDto> balance = new List<CurrencyDto>();
    }

    // ── 큐브(합성/분해/제작) 액션 (inventory-item-cube 기획서 §5.6·5.7·5.8) ──

    /// <summary>큐브 합성 요청 데이터. { itemIds } — 같은 등급·슬롯·클래스 장비 combine_count개.</summary>
    [Serializable]
    public class CubeCombineData
    {
        public List<long> itemIds = new List<long>();
    }

    /// <summary>큐브 합성 요청 body(인증).</summary>
    [Serializable]
    public class CubeCombineRequest
    {
        public long userId;
        public string token;
        public CubeCombineData data;
    }

    /// <summary>큐브 분해 대상 한 항목. { itemId, count }(장비는 1, 재료는 스택 수량 이하).</summary>
    [Serializable]
    public class CubeDismantleItemDto
    {
        public long itemId;
        public int count;
    }

    /// <summary>큐브 분해 요청 데이터. { items }</summary>
    [Serializable]
    public class CubeDismantleData
    {
        public List<CubeDismantleItemDto> items = new List<CubeDismantleItemDto>();
    }

    /// <summary>큐브 분해 요청 body(인증).</summary>
    [Serializable]
    public class CubeDismantleRequest
    {
        public long userId;
        public string token;
        public CubeDismantleData data;
    }

    /// <summary>큐브 제작 요청 데이터. { recipeCode }</summary>
    [Serializable]
    public class CubeCraftData
    {
        public int recipeCode;
    }

    /// <summary>큐브 제작 요청 body(인증).</summary>
    [Serializable]
    public class CubeCraftRequest
    {
        public long userId;
        public string token;
        public CubeCraftData data;
    }

    /// <summary>아이템 코드+수량 한 항목(제작 소모/획득 목록).</summary>
    [Serializable]
    public class ItemQuantityDto
    {
        public int itemCode;
        public int quantity;
    }

    /// <summary>큐브 합성 결과 아이템. { itemId, itemCode, grade }</summary>
    [Serializable]
    public class CombineResultDto
    {
        public long itemId;
        public int itemCode;
        public int grade;
    }

    /// <summary>큐브 합성 결과(5.6). consumed = 소모된 아이템 id, result = 생성된 상위 등급 아이템, cube = 갱신 후 큐브 상태.</summary>
    [Serializable]
    public class CubeCombineResultData
    {
        public List<long> consumed = new List<long>();
        public CombineResultDto result = new CombineResultDto();
        public CubeDto cube = new CubeDto();
    }

    /// <summary>큐브 분해 결과(5.7). gold = 이번 분해로 획득한 골드, cubeExp = 이번 분해로 획득한 큐브 경험치(증가분).</summary>
    [Serializable]
    public class CubeDismantleResultData
    {
        public long gold;
        public long cubeExp;
    }

    /// <summary>큐브 제작 획득물(5.8).</summary>
    [Serializable]
    public class CubeCraftGainedDto
    {
        public List<ItemQuantityDto> items = new List<ItemQuantityDto>();
    }

    /// <summary>큐브 제작 결과(5.8). consumed = 소모 재료, gained = 제작 결과, cube = 갱신 후 큐브 상태.</summary>
    [Serializable]
    public class CubeCraftResultData
    {
        public List<ItemQuantityDto> consumed = new List<ItemQuantityDto>();
        public CubeCraftGainedDto gained = new CubeCraftGainedDto();
        public CubeDto cube = new CubeDto();
    }

    /// <summary>큐브 합성 응답 { success, errorCode, message, data(CubeCombineResultData) }.</summary>
    [Serializable]
    public class CubeCombineResponse
    {
        public bool success;
        public int errorCode;
        public string message;
        public CubeCombineResultData data = new CubeCombineResultData();
    }

    /// <summary>큐브 분해 응답 { success, errorCode, message, data(CubeDismantleResultData) }.</summary>
    [Serializable]
    public class CubeDismantleResponse
    {
        public bool success;
        public int errorCode;
        public string message;
        public CubeDismantleResultData data = new CubeDismantleResultData();
    }

    /// <summary>큐브 제작 응답 { success, errorCode, message, data(CubeCraftResultData) }.</summary>
    [Serializable]
    public class CubeCraftResponse
    {
        public bool success;
        public int errorCode;
        public string message;
        public CubeCraftResultData data = new CubeCraftResultData();
    }

    // ── 오프라인(방치) 보상 정산 (offline-reward 기획서 §5·§8) ──

    /// <summary>오프라인 보상으로 지급된 골드·경험치(경험치는 3캐릭터 공통).</summary>
    [Serializable]
    public class OfflineRewardAmount
    {
        public long gold;
        public long exp;
    }

    /// <summary>경험치 반영 후 각 캐릭터의 상태(같은 exp를 받아도 시작 레벨이 달라 결과는 캐릭터마다 다를 수 있음).</summary>
    [Serializable]
    public class OfflineCharacterState
    {
        public int characterId; // 캐릭터 슬롯(1~3)
        public int level;
        public long exp;        // 현재 레벨의 잔여 경험치
    }

    /// <summary>
    /// 오프라인 보상 정산 결과(offline-reward 기획서 5.1 성공 응답 data · §8 확정 DTO).
    /// 기획서 §8의 필드 정의를 따르되, 프로젝트 DTO 규약(`[Serializable]`+camelCase 필드, JsonUtility·IncludeFields 공유)에 맞춘다.
    /// </summary>
    [Serializable]
    public class OfflineRewardResult
    {
        public long offlineElapsedSec;                                       // 실제 경과 시간(now - last_active_at), 상한 미적용
        public long effectiveSec;                                            // 상한(12h) 적용 후 보상 산정에 쓴 시간
        public bool capped;                                                  // 12시간 상한 적용 여부
        public OfflineRewardAmount rewards = new OfflineRewardAmount();       // 지급 골드·경험치
        public List<OfflineCharacterState> characters = new List<OfflineCharacterState>(); // 반영 후 3캐릭터 상태
        public long lastActiveAt;                                            // 현재 서버 시각으로 리셋한 기준 시각(Unix ts)
    }

    /// <summary>오프라인 보상 정산 응답 { success, errorCode, message, data(OfflineRewardResult) }.</summary>
    [Serializable]
    public class OfflineClaimResponse
    {
        public bool success;
        public int errorCode;
        public string message;
        public OfflineRewardResult data = new OfflineRewardResult();
    }

    // ── 메일(보상) 수신 (mail 기획서 §5) ──
    // list·claim-all 요청은 추가 데이터가 없으므로 AuthRequest({ userId, token })를 그대로 사용한다.

    /// <summary>메일 첨부 1건. rewardType(1:골드 2:아이템 3:재료), rewardCode(골드는 0, 그 외 item_master 코드), quantity(수량).</summary>
    [Serializable]
    public class MailAttachmentDto
    {
        public int rewardType;
        public int rewardCode;
        public int quantity;
    }

    /// <summary>우편함 메일 1건(5.1 목록 항목). attachments가 비어 있으면 첨부 없는 안내 메일.</summary>
    [Serializable]
    public class MailDto
    {
        public long mailId;
        public int category;   // 1:운영 2:거래 3:출석 4:시스템
        public string title;
        public string body;
        public List<MailAttachmentDto> attachments = new List<MailAttachmentDto>();
        public int isRead;     // 열람 여부(0/1)
        public int claimed;    // 첨부 수령 여부(0/1)
        public long createdAt; // 발급 시각(Unix ts)
        public long expiresAt; // 만료 시각(Unix ts, 0이면 무기한)
    }

    /// <summary>우편함 조회 결과(5.1 응답 data). { mails }</summary>
    [Serializable]
    public class MailListResultData
    {
        public List<MailDto> mails = new List<MailDto>();
    }

    /// <summary>메일 단건 수령 요청 데이터. { mailId }</summary>
    [Serializable]
    public class MailClaimData
    {
        public long mailId;
    }

    /// <summary>메일 단건 수령 요청 body(인증).</summary>
    [Serializable]
    public class MailClaimRequest
    {
        public long userId;
        public string token;
        public MailClaimData data;
    }

    /// <summary>메일 수령으로 지급된 첨부 합계. currencies = 재화(골드), items = 아이템/재료.</summary>
    [Serializable]
    public class MailGainedDto
    {
        public List<CurrencyDto> currencies = new List<CurrencyDto>();
        public List<ItemQuantityDto> items = new List<ItemQuantityDto>();
    }

    /// <summary>메일 단건 수령 결과(5.2 응답 data). gained = 지급 첨부, balance = 지급 후 재화 잔액.</summary>
    [Serializable]
    public class MailClaimResultData
    {
        public long mailId;
        public MailGainedDto gained = new MailGainedDto();
        public List<CurrencyDto> balance = new List<CurrencyDto>();
    }

    /// <summary>메일 일괄 수령 결과(5.3 응답 data). claimedMailIds = 수령된 메일, gained = 첨부 합계, balance = 지급 후 재화 잔액.</summary>
    [Serializable]
    public class MailClaimAllResultData
    {
        public List<long> claimedMailIds = new List<long>();
        public MailGainedDto gained = new MailGainedDto();
        public List<CurrencyDto> balance = new List<CurrencyDto>();
    }

    /// <summary>우편함 조회 응답 { success, errorCode, message, data(MailListResultData) }.</summary>
    [Serializable]
    public class MailListResponse
    {
        public bool success;
        public int errorCode;
        public string message;
        public MailListResultData data = new MailListResultData();
    }

    /// <summary>메일 단건 수령 응답 { success, errorCode, message, data(MailClaimResultData) }.</summary>
    [Serializable]
    public class MailClaimResponse
    {
        public bool success;
        public int errorCode;
        public string message;
        public MailClaimResultData data = new MailClaimResultData();
    }

    /// <summary>메일 일괄 수령 응답 { success, errorCode, message, data(MailClaimAllResultData) }.</summary>
    [Serializable]
    public class MailClaimAllResponse
    {
        public bool success;
        public int errorCode;
        public string message;
        public MailClaimAllResultData data = new MailClaimAllResultData();
    }

    // ── 출석부 보상 (attendance 기획서 §5) ──
    // status·claim 요청은 추가 데이터가 없으므로 AuthRequest({ userId, token })를 그대로 사용한다.

    /// <summary>출석 보상 1건. rewardType(1:골드 2:아이템 3:재료), rewardCode(골드는 0, 그 외 item_master 코드), quantity(수량). 메일 첨부와 동일 enum.</summary>
    [Serializable]
    public class AttendanceRewardDto
    {
        public int rewardType;
        public int rewardCode;
        public int quantity;
    }

    /// <summary>이번달 출석 달력의 일자 1칸(5.1 목록 항목). day = 이달 며칠차(1~31), claimed = 그날 수령 여부.</summary>
    [Serializable]
    public class AttendanceDayDto
    {
        public int day;
        public int rewardType;
        public int rewardCode;
        public int quantity;
        public bool claimed;
    }

    /// <summary>이번달 출석 현황 조회 결과(5.1 응답 data). yearMonth/today는 서버 KST 판정 값.</summary>
    [Serializable]
    public class AttendanceStatusResultData
    {
        public int yearMonth;      // 이번달 YYYYMM(KST)
        public int today;          // 오늘 YYYYMMDD(KST)
        public int todayDay;       // 오늘의 일(day-of-month)
        public bool todayClaimed;  // 오늘자 출석을 이미 수령했는지
        public List<AttendanceDayDto> days = new List<AttendanceDayDto>();
    }

    /// <summary>출석 보상 획득 결과(5.2 응답 data). 보상은 mailId 메일로 발급되며 우편함 수령 시 계정에 반영된다.</summary>
    [Serializable]
    public class AttendanceClaimResultData
    {
        public int attendDate; // 출석 일자 YYYYMMDD(KST)
        public int day;        // 이달 며칠차
        public AttendanceRewardDto reward = new AttendanceRewardDto();
        public long mailId;    // 발급된 보상 메일
    }

    /// <summary>출석 현황 조회 응답 { success, errorCode, message, data(AttendanceStatusResultData) }.</summary>
    [Serializable]
    public class AttendanceStatusResponse
    {
        public bool success;
        public int errorCode;
        public string message;
        public AttendanceStatusResultData data = new AttendanceStatusResultData();
    }

    /// <summary>출석 보상 획득 응답 { success, errorCode, message, data(AttendanceClaimResultData) }.</summary>
    [Serializable]
    public class AttendanceClaimResponse
    {
        public bool success;
        public int errorCode;
        public string message;
        public AttendanceClaimResultData data = new AttendanceClaimResultData();
    }
}
