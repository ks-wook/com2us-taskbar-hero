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
        public PlayerDto player = new PlayerDto();
        public List<CharacterDto> characters = new List<CharacterDto>();
        public List<CurrencyDto> currencies = new List<CurrencyDto>();
        public List<InventoryItemDto> inventory = new List<InventoryItemDto>();
        public List<SkillDto> skills = new List<SkillDto>();
        public List<RuneDto> runes = new List<RuneDto>();
        public CubeDto cube = new CubeDto();
        public long offlineElapsedSec;
    }
}
