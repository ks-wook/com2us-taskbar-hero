using GameServer.MasterData;
using GameServer.Repositories;
using MySqlConnector;
using TaskbarHero.Common;
using TaskbarHero.Common.Dto;
using ZLogger;

namespace GameServer.Services;

/// <summary>세이브 API 처리 결과. 성공 메시지는 엔드포인트별로 다르므로 함께 담는다.</summary>
public readonly record struct SaveResult(ErrorCode ErrorCode, string SuccessMessage, object? Data);

public interface ISaveService
{
    Task<SaveResult> LoadAsync(long userId);
    Task<SaveResult> CreateCharacterAsync(long userId, string? nickname, int classCode, int gender);
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
    /// 코어 세이브 스냅샷을 로드한다. game_player가 없으면 신규 계정({ isNew:true })으로 응답한다.
    /// **크기가 고정된 데이터만** 담으며, 무한히 커질 수 있는 가방 아이템은 포함하지 않는다
    /// (창고 UI를 열 때 <see cref="IInventoryService.GetPageAsync"/>로 지연 로딩).
    /// </summary>
    /// <remarks>
    /// 반환 항목(세이브 데이터 기획서 5.1의 표와 1:1로 대응한다. 항목을 늘리거나 줄이면 그 표도 함께 고친다):
    /// <para><c>player</c> — game_player 1행: 닉네임·진행 좌표(act/stage/difficulty)·최고 클리어·인벤 용량·마지막 활동 시각</para>
    /// <para><c>characters</c> — player_character(≤3행): 캐릭터 슬롯별 직업·성별·레벨·경험치</para>
    /// <para><c>currencies</c> — player_item의 재화 행(row_type=2): 재화 종류별 보유량(골드 포함)</para>
    /// <para><c>equipped</c> — player_item_equipped(≤18행 = 3캐릭터 × 6슬롯): 장착 장비. 캐릭터 스탯 계산의
    ///   입력이라 가방 로딩을 기다리지 않도록 코어에 넣는다</para>
    /// <para><c>skills</c> — player_skill: 캐릭터별 스킬 코드·레벨·액티브 장착 여부</para>
    /// <para><c>runes</c> — player_rune: 계정 공용 룬 코드·레벨</para>
    /// <para><c>cube</c> — player_cube 1행: 큐브 레벨·경험치(행이 없으면 레벨 1·경험치 0)</para>
    /// <para><c>inventoryTotal</c> — 가방 아이템 총 행 수(페이징 진행률·용량 UI 표시용)</para>
    /// <para><c>offlineElapsedSec</c> — 현재 서버 시각 − last_active_at(오프라인 보상 계산 입력값)</para>
    /// </remarks>
    public async Task<SaveResult> LoadAsync(long userId)
    {
        var player = await _saveRepository.GetPlayerAsync(userId);
        if (player is null)
        {
            return new SaveResult(ErrorCode.Success, "New player", new { isNew = true });
        }

        var characters = await _saveRepository.GetCharactersAsync(userId);
        var currencies = await _saveRepository.GetCurrenciesAsync(userId);
        var equipped = await _saveRepository.GetEquippedAsync(userId);
        var skills = await _saveRepository.GetSkillsAsync(userId);
        var runes = await _saveRepository.GetRunesAsync(userId);
        var cube = await _saveRepository.GetCubeAsync(userId) ?? new CubeDto { cubeLevel = 1, cubeExp = 0 };
        var inventoryTotal = await _saveRepository.GetBagItemCountAsync(userId);

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var offlineElapsed = Math.Max(0, now - player.lastActiveAt);

        var data = new LoadDataDto
        {
            player = player,
            characters = characters,
            currencies = currencies,
            equipped = equipped,
            skills = skills,
            runes = runes,
            cube = cube,
            inventoryTotal = inventoryTotal,
            offlineElapsedSec = offlineElapsed,
        };

        return new SaveResult(ErrorCode.Success, "Load successful", data);
    }

    /// <summary>
    /// 캐릭터를 생성한다. 마스터 로드·직업 코드·성별 값 유효성을 확인하고, 계정이 없으면 game_player와 1번 슬롯을
    /// 초기화하며, 기존 계정이면 슬롯 여유(최대 3)·직업 중복을 검사한 뒤 빈 슬롯에 추가한다.
    /// 성별(1:남 2:여)은 생성 시 확정되며 이후 변경 수단이 없다(외형 전용, 스탯 무관).
    /// 동시 초기화·중복 생성 경합은 UNIQUE 위반을 잡아 에러 코드로 변환한다.
    /// </summary>
    public async Task<SaveResult> CreateCharacterAsync(long userId, string? nickname, int classCode, int gender)
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

        // 정의되지 않은 성별 값(1:남 2:여 외).
        if (!IsValidGender(gender))
        {
            return new SaveResult(ErrorCode.InvalidGender, string.Empty, null);
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
                    userId, nickname.Trim(), classCode, gender, MasterDataProvider.BaseInventoryCapacity,
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            }
            catch (MySqlException ex) when (ex.Number == MySqlDuplicateEntry)
            {
                // 동시 초기화 경합.
                _logger.ZLogWarning($"계정 초기화 경합 감지: user {userId:@UserId}");
                return new SaveResult(ErrorCode.InvalidSaveData, string.Empty, null);
            }

            _logger.ZLogInformation($"캐릭터 생성 성공(신규 계정): userId {userId:@UserId}, characterId {1:@CharacterId}, classCode {classCode:@ClassCode}, gender {gender:@Gender}");
            // 최초 생성(1번 슬롯)은 계정 초기화라 무료.
            return SuccessCharacter(userId, 1, classCode, gender, 0, null);
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

        var outcome = await _saveRepository.AddCharacterAsync(userId, newSlot, classCode, gender, cost);
        switch (outcome.Status)
        {
            case AddCharacterStatus.InsufficientCurrency:
                return new SaveResult(ErrorCode.InsufficientCurrency, string.Empty, null);
            case AddCharacterStatus.DuplicateConflict:
                // 슬롯/직업 유니크 경합(동시 생성).
                return new SaveResult(ErrorCode.InvalidCharacterId, string.Empty, null);
        }

        _logger.ZLogInformation($"캐릭터 생성 성공: userId {userId:@UserId}, characterId {newSlot:@CharacterId}, classCode {classCode:@ClassCode}, gender {gender:@Gender}, cost {outcome.Cost:@Cost}");
        return SuccessCharacter(userId, newSlot, classCode, gender, outcome.Cost, outcome.GoldBalance);
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

    /// <summary>캐릭터 생성 성공 응답(userId·characterId·classCode·gender·초기 레벨 1 + 소모 골드·잔액)을 만든다.
    /// 무료 생성(최초 1번 슬롯)이면 goldBalance=null로 넘겨 cost 0·빈 잔액으로 회신한다.</summary>
    private static SaveResult SuccessCharacter(long userId, int characterId, int classCode, int gender, long cost, long? goldBalance)
    {
        var data = new CreateCharacterResultData
        {
            userId = userId,
            characterId = characterId,
            classCode = classCode,
            gender = gender,
            level = 1,
            cost = new CurrencyDto { currencyType = GoldCurrencyType, amount = cost },
            balance = goldBalance.HasValue
                ? new List<CurrencyDto> { new CurrencyDto { currencyType = GoldCurrencyType, amount = goldBalance.Value } }
                : new List<CurrencyDto>(),
        };
        return new SaveResult(ErrorCode.Success, "Character created", data);
    }

    /// <summary>성별 값이 정의된 범위(1:남 2:여)인지 검사한다. 그 외 값은 InvalidGender로 거절한다.</summary>
    private static bool IsValidGender(int gender) =>
        gender == (int)CharacterGender.Male || gender == (int)CharacterGender.Female;

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
