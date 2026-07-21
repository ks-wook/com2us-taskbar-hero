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
        public long itemId;
        public int? slot;
        public int itemCode;
        public long quantity;
        public int enhanceLevel;
        public int? equippedCharacterId;
        public int? equippedSlot;
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
}
