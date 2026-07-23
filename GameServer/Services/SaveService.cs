using GameServer.MasterData;
using GameServer.Repositories;
using MySqlConnector;
using TaskbarHero.Common;
using TaskbarHero.Common.Dto;

namespace GameServer.Services;

/// <summary>세이브 API 처리 결과. 성공 메시지는 엔드포인트별로 다르므로 함께 담는다.</summary>
public readonly record struct SaveResult(ErrorCode ErrorCode, string SuccessMessage, object? Data);

public interface ISaveService
{
    Task<SaveResult> LoadAsync(long userId);
    Task<SaveResult> CreateCharacterAsync(long userId, string? nickname, int classCode);
    Task<SaveResult> UpdateLastActiveAsync(long userId);
}

public sealed class SaveService : ISaveService
{
    private const int MaxCharacterSlots = 3;
    private const int MySqlDuplicateEntry = 1062;
    private const int GoldCurrencyType = 1;

    private readonly ISaveRepository _saveRepository;
    private readonly MasterDataProvider _masterData;
    private readonly ILogger<SaveService> _logger;

    /// <summary>의존성(세이브 리포지토리·마스터 데이터·로거)을 주입받는다.</summary>
    public SaveService(ISaveRepository saveRepository, MasterDataProvider masterData, ILogger<SaveService> logger)
    {
        _saveRepository = saveRepository;
        _masterData = masterData;
        _logger = logger;
    }

    /// <summary>
    /// 계정 세이브 스냅샷을 로드한다. game_player가 없으면 신규 계정({ isNew:true })으로 응답하고,
    /// 있으면 캐릭터·인벤토리(재화 포함)·스킬·룬·큐브를 모아 오프라인 경과 시간과 함께 반환한다.
    /// </summary>
    public async Task<SaveResult> LoadAsync(long userId)
    {
        var player = await _saveRepository.GetPlayerAsync(userId);
        if (player is null)
        {
            return new SaveResult(ErrorCode.Success, "New player", new { isNew = true });
        }

        var characters = await _saveRepository.GetCharactersAsync(userId);
        var (currencies, inventory) = await _saveRepository.GetInventoryAsync(userId);
        var skills = await _saveRepository.GetSkillsAsync(userId);
        var runes = await _saveRepository.GetRunesAsync(userId);
        var cube = await _saveRepository.GetCubeAsync(userId) ?? new CubeDto { cubeLevel = 1, cubeExp = 0 };

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var offlineElapsed = Math.Max(0, now - player.lastActiveAt);

        var data = new LoadDataDto
        {
            player = player,
            characters = characters,
            currencies = currencies,
            inventory = inventory,
            skills = skills,
            runes = runes,
            cube = cube,
            offlineElapsedSec = offlineElapsed,
        };

        return new SaveResult(ErrorCode.Success, "Load successful", data);
    }

    /// <summary>
    /// 캐릭터를 생성한다. 마스터 로드·직업 코드 유효성을 확인하고, 계정이 없으면 game_player와 1번 슬롯을
    /// 초기화하며, 기존 계정이면 슬롯 여유(최대 3)·직업 중복을 검사한 뒤 빈 슬롯에 추가한다.
    /// 동시 초기화·중복 생성 경합은 UNIQUE 위반을 잡아 에러 코드로 변환한다.
    /// </summary>
    public async Task<SaveResult> CreateCharacterAsync(long userId, string? nickname, int classCode)
    {
        // 마스터 미로드 시 직업 검증 불가.
        if (!_masterData.IsLoaded)
        {
            return new SaveResult(ErrorCode.MasterDataNotLoaded, string.Empty, null);
        }

        // 존재하지 않는 직업 코드.
        if (!_masterData.IsValidClass(classCode))
        {
            return new SaveResult(ErrorCode.InvalidClassCode, string.Empty, null);
        }

        var player = await _saveRepository.GetPlayerAsync(userId);

        // 최초 생성: game_player 초기화 + 1번 슬롯.
        if (player is null)
        {
            if (string.IsNullOrWhiteSpace(nickname))
            {
                return new SaveResult(ErrorCode.InvalidRequest, string.Empty, null);
            }

            try
            {
                await _saveRepository.CreatePlayerWithFirstCharacterAsync(
                    userId, nickname.Trim(), classCode, MasterDataProvider.BaseInventoryCapacity,
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            }
            catch (MySqlException ex) when (ex.Number == MySqlDuplicateEntry)
            {
                // 동시 초기화 경합.
                _logger.LogWarning("계정 초기화 경합 감지: user {UserId}", userId);
                return new SaveResult(ErrorCode.InvalidSaveData, string.Empty, null);
            }

            _logger.LogInformation(
                "캐릭터 생성 성공(신규 계정): userId {UserId}, characterId {CharacterId}, classCode {ClassCode}",
                userId, 1, classCode);
            // 최초 생성(1번 슬롯)은 계정 초기화라 무료.
            return SuccessCharacter(userId, 1, classCode, 0, null);
        }

        // 기존 계정: 슬롯 여유·직업 중복 검사 후 추가.
        var slots = await _saveRepository.GetCharacterSlotsAsync(userId);
        if (slots.Count >= MaxCharacterSlots)
        {
            return new SaveResult(ErrorCode.PlayerAlreadyExists, string.Empty, null);
        }

        if (slots.Any(s => s.ClassCode == classCode))
        {
            return new SaveResult(ErrorCode.InvalidCharacterId, string.Empty, null);
        }

        var newSlot = FirstFreeSlot(slots);
        // 2·3번 슬롯 추가 생성 비용(마스터 명시값). 골드 확인·차감·캐릭터 삽입은 리포지토리 트랜잭션에서 원자적으로 처리.
        var cost = _masterData.CharacterCreateCost(newSlot);

        var outcome = await _saveRepository.AddCharacterAsync(userId, newSlot, classCode, cost);
        switch (outcome.Status)
        {
            case AddCharacterStatus.InsufficientCurrency:
                return new SaveResult(ErrorCode.InsufficientCurrency, string.Empty, null);
            case AddCharacterStatus.DuplicateConflict:
                // 슬롯/직업 유니크 경합(동시 생성).
                return new SaveResult(ErrorCode.InvalidCharacterId, string.Empty, null);
        }

        _logger.LogInformation(
            "캐릭터 생성 성공: userId {UserId}, characterId {CharacterId}, classCode {ClassCode}, cost {Cost}",
            userId, newSlot, classCode, outcome.Cost);
        return SuccessCharacter(userId, newSlot, classCode, outcome.Cost, outcome.GoldBalance);
    }

    /// <summary>접속 시각(last_active_at)을 현재로 갱신한다(heartbeat). 계정 세이브가 없으면 SaveNotFound.</summary>
    public async Task<SaveResult> UpdateLastActiveAsync(long userId)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var affected = await _saveRepository.UpdateLastActiveAsync(userId, now);
        if (affected == 0)
        {
            // 아직 계정 세이브(game_player)가 없음.
            return new SaveResult(ErrorCode.SaveNotFound, string.Empty, null);
        }

        return new SaveResult(ErrorCode.Success, "Heartbeat OK", new { lastActiveAt = now });
    }

    /// <summary>캐릭터 생성 성공 응답(userId·characterId·classCode·초기 레벨 1 + 소모 골드·잔액)을 만든다.
    /// 무료 생성(최초 1번 슬롯)이면 goldBalance=null로 넘겨 cost 0·빈 잔액으로 회신한다.</summary>
    private static SaveResult SuccessCharacter(long userId, int characterId, int classCode, long cost, long? goldBalance)
    {
        var data = new CreateCharacterResultData
        {
            userId = userId,
            characterId = characterId,
            classCode = classCode,
            level = 1,
            cost = new CurrencyDto { currencyType = GoldCurrencyType, amount = cost },
            balance = goldBalance.HasValue
                ? new List<CurrencyDto> { new CurrencyDto { currencyType = GoldCurrencyType, amount = goldBalance.Value } }
                : new List<CurrencyDto>(),
        };
        return new SaveResult(ErrorCode.Success, "Character created", data);
    }

    /// <summary>1~3 슬롯 중 사용되지 않은 가장 작은 번호.</summary>
    private static int FirstFreeSlot(IReadOnlyCollection<CharacterSlot> slots)
    {
        var used = slots.Select(s => s.CharacterId).ToHashSet();
        for (var slot = 1; slot <= MaxCharacterSlots; slot++)
        {
            if (!used.Contains(slot))
            {
                return slot;
            }
        }

        // 슬롯이 가득 찬 경우는 호출 전에 걸러지므로 도달하지 않는다.
        throw new InvalidOperationException("빈 캐릭터 슬롯이 없습니다.");
    }
}
