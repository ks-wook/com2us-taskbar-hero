using GameServer.MasterData;
using GameServer.Repositories.GameDb;
using GameServer.Repositories.GameDb.Interfaces;
using MySqlConnector;
using TaskbarHero.Common;
using TaskbarHero.Common.Dto;
using ZLogger;
using GameServer.Repositories.MasterDb;
using GameServer.Models;
using GameServer.Services.Interfaces;
using GameServer.Logging;
using GameServer.Util;

namespace GameServer.Services;

/// <summary>세이브 API 처리 결과. 성공 메시지는 엔드포인트별로 다르므로 함께 담는다.</summary>
public readonly record struct SaveResult(ErrorCode ErrorCode, string SuccessMessage, object? Data);

public sealed class SaveService : ISaveService
{
    private readonly ISaveRepository _saveRepository;
    private readonly IConsumableRepository _consumableRepository;
    private readonly MasterDbProvider _masterData;
    private readonly ILogger<SaveService> _logger;
    private readonly IEventLogger _eventLogger;

    /// <summary>의존성(세이브·소모품 버프 리포지토리, 마스터 데이터, 운영 로거, 이벤트 로거)을 주입받는다.</summary>
    public SaveService(
        ISaveRepository saveRepository,
        IConsumableRepository consumableRepository,
        MasterDbProvider masterData,
        ILogger<SaveService> logger,
        IEventLogger eventLogger)
    {
        _saveRepository = saveRepository;
        _consumableRepository = consumableRepository;
        _masterData = masterData;
        _logger = logger;
        _eventLogger = eventLogger;
    }

    /// <summary>
    /// 코어 세이브 스냅샷을 로드한다. game_player가 없으면 신규 계정({ isNew:true })으로 응답한다.
    /// **크기가 고정된 데이터만** 담으며, 무한히 커질 수 있는 가방 아이템은 포함하지 않는다
    /// (창고 UI를 열 때 <see cref="IInventoryService.GetPageAsync"/>로 지연 로딩).
    /// </summary>
    /// <remarks>
    /// 반환 항목(세이브 데이터 기획서 5.1의 표와 1:1로 대응한다. 항목을 늘리거나 줄이면 그 표도 함께 고친다):
    /// <para><c>player</c> — game_player 1행: 닉네임·진행 좌표(act/stage/difficulty)·최고 클리어·인벤 용량·마지막 활동 시각</para>
    /// <para><c>characters</c> — player_character(보유 캐릭터, ≤직업 수): 캐릭터별 직업·파티 자리(slot, 0=미편성)·성별·레벨·경험치</para>
    /// <para><c>currencies</c> — player_item의 재화 행(row_type=2): 재화 종류별 보유량(골드 포함)</para>
    /// <para><c>equipped</c> — player_item_equipped(≤ 보유 캐릭터 수 × 6슬롯): 장착 장비. 미편성 캐릭터도 장비를
    ///   그대로 착용한 채 대기하므로 함께 내려간다. 캐릭터 스탯 계산의 입력이라 가방 로딩을 기다리지 않도록 코어에 넣는다</para>
    /// <para><c>skills</c> — player_skill 중 레벨 1 이상인 행: 캐릭터별 스킬 코드·레벨·액티브 장착 여부
    ///   (초기화로 레벨 0이 된 행은 미습득이라 제외한다)</para>
    /// <para><c>runes</c> — player_rune: 계정 공용 룬 코드·레벨</para>
    /// <para><c>cube</c> — player_cube 1행: 큐브 레벨·경험치(행이 없으면 레벨 1·경험치 0)</para>
    /// <para><c>activeBuffs</c> — player_buff 중 <c>expires_at &gt; now</c>인 행: 활성 획득량 버프의 종류·배율·시작/만료 시각.
    ///   계정당 버프 종류 수만큼이라 크기가 고정이므로 코어에 담는다(만료 판정은 항상 서버가 한다)</para>
    /// <para><c>inventoryTotal</c> — 가방 아이템 총 행 수(페이징 진행률·용량 UI 표시용)</para>
    /// <para><c>offlineElapsedSec</c> — 현재 서버 시각 − last_active_at(오프라인 보상 계산 입력값)</para>
    /// </remarks>
    public async Task<SaveResult> LoadAsync(long userId)
    {
        var player = await _saveRepository.GetPlayerAsync(userId);
        if (player is null)
        {
            // 세션 개시 이벤트(로그 이벤트 정의 5.2). 아직 세이브가 없으므로 경과 시간은 0이다.
            _eventLogger.Action(Constants.EventLog.Tags.SaveLoad, userId, new SaveLoadEvent(IsNew: true, OfflineElapsedSec: 0));
            return new SaveResult(ErrorCode.Success, "New player", new { isNew = true });
        }

        var characters = await _saveRepository.GetCharactersAsync(userId);
        var currencies = await _saveRepository.GetCurrenciesAsync(userId);
        var equipped = await _saveRepository.GetEquippedAsync(userId);
        var skills = await _saveRepository.GetSkillsAsync(userId);
        var runes = await _saveRepository.GetRunesAsync(userId);
        var cube = await _saveRepository.GetCubeAsync(userId) ?? new CubeDto { cubeLevel = 1, cubeExp = 0 };
        var inventoryTotal = await _saveRepository.GetBagItemCountAsync(userId);

        var now = DateTimeUtil.NowUnixSeconds();
        var offlineElapsed = DateTimeUtil.ElapsedSeconds(player.lastActiveAt, now);

        // 활성 버프만 담는다(만료 행은 남아 있어도 제외 — 오프라인 정산이 소급 참조할 뿐이다).
        var activeBuffs = await _consumableRepository.GetActiveBuffsAsync(userId, now);

        var data = new LoadDataDto
        {
            player = player,
            characters = characters,
            currencies = currencies,
            equipped = equipped,
            skills = skills,
            runes = runes,
            cube = cube,
            activeBuffs = activeBuffs
                .Select(b => new ActiveBuffDto
                {
                    buffType = b.BuffType,
                    buffValue = b.BuffValue,
                    startedAt = b.StartedAt,
                    expiresAt = b.ExpiresAt,
                })
                .ToList(),
            inventoryTotal = inventoryTotal,
            offlineElapsedSec = offlineElapsed,
        };

        // 세션 개시 이벤트(로그 이벤트 정의 5.2). 계정 로그를 남기지 않으므로 접속·리텐션 분석이 전부 이 행에서 나온다.
        _eventLogger.Action(
            Constants.EventLog.Tags.SaveLoad, userId, new SaveLoadEvent(IsNew: false, OfflineElapsedSec: offlineElapsed));

        return new SaveResult(ErrorCode.Success, "Load successful", data);
    }

    /// <summary>
    /// 캐릭터를 생성한다. 마스터 로드·직업 코드·성별 값 유효성을 확인하고, 계정이 없으면 game_player와 첫 캐릭터를
    /// 초기화하며, 기존 계정이면 <b>직업 중복만</b> 검사한 뒤 추가한다(보유 수 상한을 따로 두지 않는다 —
    /// 직업 중복이 불가하므로 보유 상한은 자연히 직업 수가 된다).
    /// 생성 비용은 마스터 character_create_cost의 <b>생성 순번(보유 수 + 1)</b> 값(현재 정액)이며 최초 생성은 무료다.
    /// 파티 자리는 빈 자리가 있으면 가장 앞자리에 자동 편성하고, 파티가 이미 3명이면 미편성(slot 0)으로 보유만 한다.
    /// 성별(1:남 2:여)은 생성 시 확정되며 이후 변경 수단이 없다(외형 전용, 스탯 무관).
    /// 생성되는 캐릭터는 그 직업의 <b>가장 등급 낮은 기본 무기</b>를 지급받아 무기 슬롯에 장착한 상태로 시작하며,
    /// 그 직업의 <b>첫 번째 액티브 스킬</b>도 레벨 1로 습득·장착한 상태로 시작한다
    /// (맨손·무스킬로 전투에 나가지 않게 하기 위함 — 지급·습득은 캐릭터 삽입과 같은 트랜잭션에서 처리한다).
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

        // 생성과 함께 지급·장착할 직업 기본 무기(정의가 없으면 null → 맨손 생성).
        var startingWeapon = ResolveStartingWeapon(classCode);

        // 생성과 함께 습득·장착시킬 직업 기본 액티브 스킬(정의가 없으면 null → 스킬 없이 생성).
        var startingSkillCode = ResolveStartingSkillCode(classCode);

        // 최초 생성: game_player 초기화 + 1번 슬롯.
        if (player is null)
        {
            if (string.IsNullOrWhiteSpace(nickname))
            {
                return new SaveResult(ErrorCode.InvalidRequest, string.Empty, null);
            }

            var nowUnix = DateTimeUtil.NowUnixSeconds();
            var welcomeMail = ComposeNewbieRewardMail(nickname.Trim(), nowUnix);

            CreatePlayerOutcome created;
            try
            {
                created = await _saveRepository.CreatePlayerWithFirstCharacterAsync(
                    userId, nickname.Trim(), classCode, gender, Constants.Inventory.BaseCapacity,
                    nowUnix, welcomeMail, startingWeapon, startingSkillCode);
            }
            catch (MySqlException ex) when (ex.Number == Constants.MySqlError.DuplicateEntry)
            {
                // 동시 초기화 경합.
                _logger.ZLogWarning($"계정 초기화 경합 감지: user {userId:@UserId}");
                return new SaveResult(ErrorCode.InvalidSaveData, string.Empty, null);
            }

            _logger.ZLogInformation($"캐릭터 생성 성공(신규 계정): userId {userId:@UserId}, characterId {1:@CharacterId}, classCode {classCode:@ClassCode}, gender {gender:@Gender}, startingWeapon {startingWeapon?.ItemCode ?? 0:@StartingWeapon}, startingSkill {startingSkillCode ?? 0:@StartingSkill}");

            // 최초 생성만 이벤트로 남긴다(로그 이벤트 정의 5.2) — 답하는 질문이 첫 직업 선호와
            // 가입 → 플레이 전환이라, 두 번째 이후의 캐릭터 추가는 이 축에 들어가지 않는다.
            // 커밋이 끝난 뒤 방출한다(롤백된 사실을 로그에 남기지 않는다, 4.2).
            _eventLogger.Action(Constants.EventLog.Tags.PlayerCreate, userId, new PlayerCreateEvent(classCode, gender));

            // 신규 지원금 메일 발급(5.8). 계정 생애에 한 번뿐이라 이 트랜잭션이 곧 유일한 발급 지점이다.
            if (welcomeMail is not null && created.WelcomeMailId > 0)
            {
                _eventLogger.MailIssued(userId, created.WelcomeMailId, welcomeMail, MailSource.Newbie);
            }

            // 아이템 원장(6.2). 기본 무기는 가방을 거치지 않고 곧바로 장착된 상태로 생기지만, 계정 보유량이
            // 늘어난 것은 같으므로 원장에 남긴다 — 남기지 않으면 이후 이 개체의 강화·거래·분해 행이
            // 유입 없는 유출로 보인다. ref_id는 생성 순번(character_id)이다.
            EmitStartingWeapon(userId, 1, startingWeapon, created.StartingWeaponItemId);

            // 최초 생성은 계정 초기화라 무료이며 파티 1번 자리에 편성된다.
            return SuccessCharacter(userId, 1, classCode, 1, gender, 0, null);
        }

        // 기존 계정: 직업 중복만 검사한다(보유 수 상한 없음 — 직업 중복 불가가 곧 상한).
        var owned = await _saveRepository.GetCharacterSlotsAsync(userId);
        if (owned.Any(s => s.ClassCode == classCode))
        {
            return new SaveResult(ErrorCode.InvalidCharacterId, string.Empty, null);
        }

        var newCharacterId = NextCharacterId(owned);
        var newSlot = FirstFreePartySlot(owned);
        // 생성 비용은 "몇 번째로 만드는 캐릭터인가"(보유 수 + 1)로 찾는 마스터 명시값(현재 정액).
        // 골드 확인·차감·캐릭터 삽입은 리포지토리 트랜잭션에서 원자적으로 처리한다.
        var cost = _masterData.CharacterCreateCost(owned.Count + 1);

        var outcome = await _saveRepository.AddCharacterAsync(
            userId, newCharacterId, classCode, newSlot, gender, cost,
            startingWeapon, startingSkillCode, DateTimeUtil.NowUnixSeconds());
        switch (outcome.Status)
        {
            case AddCharacterStatus.InsufficientCurrency:
                return new SaveResult(ErrorCode.InsufficientCurrency, string.Empty, null);
            case AddCharacterStatus.DuplicateConflict:
                // 식별자/직업 유니크 경합(동시 생성).
                return new SaveResult(ErrorCode.InvalidCharacterId, string.Empty, null);
        }

        _logger.ZLogInformation($"캐릭터 생성 성공: userId {userId:@UserId}, characterId {newCharacterId:@CharacterId}, classCode {classCode:@ClassCode}, slot {newSlot:@Slot}, gender {gender:@Gender}, cost {outcome.Cost:@Cost}, startingWeapon {startingWeapon?.ItemCode ?? 0:@StartingWeapon}, startingSkill {startingSkillCode ?? 0:@StartingSkill}");

        // 재화 원장(6.1). ref_id가 생성 순번(character_id)이라 "몇 번째 캐릭터를 언제 샀나"가 이 값으로 나온다.
        // 최초 생성은 무료라 이 행이 없다(그쪽은 player.create가 답한다).
        if (outcome.Cost > 0)
        {
            _eventLogger.CurrencySpent(
                userId, outcome.Cost, outcome.GoldBalance, CurrencySource.CharacterCreate, newCharacterId);
        }

        // 아이템 원장(6.2) — 최초 생성과 같은 사유로 남긴다(ref_id = 생성 순번).
        EmitStartingWeapon(userId, newCharacterId, startingWeapon, outcome.StartingWeaponItemId);

        return SuccessCharacter(userId, newCharacterId, classCode, newSlot, gender, outcome.Cost, outcome.GoldBalance);
    }

    /// <summary>
    /// 캐릭터 생성과 함께 지급된 직업 기본 무기를 아이템 원장에 남긴다(<c>item.flow</c>, 6.2).
    /// 지급 정의가 없어 맨손으로 생성됐으면(<paramref name="weapon"/>이 null) 아무것도 내지 않는다.
    /// </summary>
    /// <param name="characterId">생성 순번. 이 이벤트의 <c>ref_id</c>다.</param>
    /// <param name="itemId">지급된 개체 id(장비라 개체가 유일하다).</param>
    private void EmitStartingWeapon(long userId, int characterId, StartingEquipment? weapon, long itemId)
    {
        if (weapon is null || itemId <= 0)
        {
            return;
        }

        _eventLogger.Action(
            Constants.EventLog.Tags.ItemFlow, userId,
            new ItemFlowEvent(
                weapon.ItemCode, Constants.ItemType.Equip, _masterData.GetItem(weapon.ItemCode)?.Grade ?? 0,
                itemId, 1, ItemFlowReason.CharacterCreateWeapon, characterId));
    }

    /// <summary>
    /// 클라이언트가 보낸 <b>파티 편성 스냅샷</b>(저장 후의 파티 전체)을 저장한다. 편성 UI의 "저장"이 곧 1회 호출이며,
    /// 목록에 없는 보유 캐릭터는 자동으로 미편성이 되므로 추가·추방·교체·자리 바꾸기가 이 하나로 처리된다.
    /// 성장·장비는 건드리지 않고 파티 자리만 바꾸며, 골드도 소모하지 않는다(비용은 캐릭터 생성 시에만 발생).
    /// 요청 목록의 형식(인원 수·자리 범위·자리 중복·캐릭터 중복)을 먼저 검증하고,
    /// 보유 여부 확인과 실제 저장은 리포지토리 트랜잭션이 수행한다.
    /// </summary>
    public async Task<SaveResult> ArrangePartyAsync(long userId, IReadOnlyList<PartyMemberDto>? members)
    {
        var invalid = ValidatePartySnapshot(members);
        if (invalid is not null)
        {
            return new SaveResult(invalid.Value, string.Empty, null);
        }

        var outcome = await _saveRepository.SavePartyAsync(userId, members!);
        if (outcome.Status == ArrangePartyStatus.CharacterNotFound)
        {
            return new SaveResult(ErrorCode.CharacterNotFound, string.Empty, null);
        }

        _logger.ZLogInformation($"파티 편성 저장: userId {userId:@UserId}, memberCount {members!.Count:@MemberCount}");
        return new SaveResult(ErrorCode.Success, "Party arranged", new ArrangePartyResultData { characters = outcome.Characters });
    }

    /// <summary>
    /// 파티 편성 스냅샷 요청의 형식을 검증한다. 위반이면 그 에러 코드를, 정상이면 null을 반환한다.
    /// 검사 항목: 빈 목록(파티는 최소 1명), 정원 초과(3명), 자리 범위(1~3), 자리 중복, 같은 캐릭터 중복 지정.
    /// 보유 여부는 DB를 봐야 하므로 여기서 검사하지 않는다(리포지토리 트랜잭션에서 확인).
    /// </summary>
    private static ErrorCode? ValidatePartySnapshot(IReadOnlyList<PartyMemberDto>? members)
    {
        // 파티가 비면 전투를 시작할 수 없다.
        if (members is null || members.Count == 0)
        {
            return ErrorCode.CannotRemoveLastCharacter;
        }

        if (members.Count > Constants.Party.MaxSlots)
        {
            return ErrorCode.PartySlotOccupied;
        }

        var usedSlots = new HashSet<int>();
        var usedCharacters = new HashSet<int>();
        foreach (var member in members)
        {
            // 미편성(0)은 "목록에 담지 않는 것"으로 표현하므로 자리 값은 1~3만 허용한다.
            if (member.slot < 1 || member.slot > Constants.Party.MaxSlots || !usedSlots.Add(member.slot))
            {
                return ErrorCode.PartySlotOccupied;
            }

            // 한 캐릭터를 두 자리에 세울 수 없다.
            if (!usedCharacters.Add(member.characterId))
            {
                return ErrorCode.InvalidCharacterId;
            }
        }

        return null;
    }

    /// <summary>접속 시각(last_active_at)을 현재로 갱신한다(heartbeat). 계정 세이브가 없으면 SaveNotFound.</summary>
    public async Task<SaveResult> UpdateLastActiveAsync(long userId)
    {
        var now = DateTimeUtil.NowUnixSeconds();
        var affected = await _saveRepository.UpdateLastActiveAsync(userId, now);
        if (affected == 0)
        {
            // 아직 계정 세이브(game_player)가 없음.
            return new SaveResult(ErrorCode.SaveNotFound, string.Empty, null);
        }

        return new SaveResult(ErrorCode.Success, "Heartbeat OK", new { lastActiveAt = now });
    }

    /// <summary>캐릭터 생성 성공 응답(userId·characterId·classCode·배정 파티 자리·gender·초기 레벨 1 + 소모 골드·잔액)을 만든다.
    /// 무료 생성(최초 캐릭터)이면 goldBalance=null로 넘겨 cost 0·빈 잔액으로 회신한다.</summary>
    private static SaveResult SuccessCharacter(long userId, int characterId, int classCode, int slot, int gender, long cost, long? goldBalance)
    {
        var data = new CreateCharacterResultData
        {
            userId = userId,
            characterId = characterId,
            classCode = classCode,
            slot = slot,
            gender = gender,
            level = 1,
            cost = new CurrencyDto { currencyType = Constants.Currency.GoldType, amount = cost },
            balance = goldBalance.HasValue
                ? new List<CurrencyDto>
                { new CurrencyDto { currencyType = Constants.Currency.GoldType, amount = goldBalance.Value } }
                : new List<CurrencyDto>(),
        };
        return new SaveResult(ErrorCode.Success, "Character created", data);
    }

    /// <summary>
    /// 신규 가입 지원금 메일의 초안을 만든다(마스터 newbie_reward_master 첨부 + mail_master 101 문구).
    /// 계정 초기화 트랜잭션 안에서 함께 적재되므로 계정당 정확히 1회만 발급된다.
    /// 지급 정의(첨부)가 비어 있으면 발급하지 않고 null을 반환하며, 문구 템플릿이 없으면
    /// 마스터 결함이므로 Error 로그를 남기고 계정 생성 자체는 그대로 진행한다(지원금만 누락).
    /// </summary>
    private MailDraft? ComposeNewbieRewardMail(string nickname, long nowUnix)
    {
        var rewards = _masterData.NewbieRewards;
        if (rewards.Count == 0)
        {
            return null; // 지급 정의 없음 = 지원금 미운영
        }

        var template = _masterData.GetMailTemplate(Constants.MailTemplate.NewbieReward);
        if (template is null)
        {
            _logger.ZLogError($"신규 가입 지원금 메일 템플릿 미정의: templateCode {Constants.MailTemplate.NewbieReward:@TemplateCode} — mail_master 확인 필요");
            return null;
        }

        var attachments = rewards
            .Select(r => new MailAttachment(r.RewardType, r.RewardCode, r.Quantity))
            .ToList();

        return MailUtil.Compose(template, nickname, nowUnix, attachments);
    }

    /// <summary>
    /// 생성하는 캐릭터에게 지급·장착할 <b>직업 기본 무기</b>를 마스터에서 확정한다(등급이 가장 낮은 그 직업 전용 무기).
    /// 리포지토리에 넘길 지급 정의(아이템 코드 + 장착 슬롯)로 변환하며, 마스터에 그 직업의 무기가 없으면
    /// 지급을 건너뛰도록 null을 반환한다(캐릭터 생성 자체는 막지 않는다 — 맨손으로 생성된다).
    /// 마스터 결함은 운영에서 찾아낼 수 있게 Error 로그로 남긴다.
    /// </summary>
    private StartingEquipment? ResolveStartingWeapon(int classCode)
    {
        var weapon = _masterData.StartingWeapon(classCode);
        if (weapon is null)
        {
            _logger.ZLogError($"직업 기본 무기 미정의: classCode {classCode:@ClassCode} — item_master의 무기(equip_slot=1) 확인 필요");
            return null;
        }

        return new StartingEquipment(weapon.ItemCode, weapon.EquipSlot);
    }

    /// <summary>
    /// 생성하는 캐릭터가 <b>기본으로 습득·장착한 채 시작할 액티브 스킬</b>의 코드를 마스터에서 확정한다
    /// (그 직업 액티브 스킬 중 skill_code가 가장 작은 첫 스킬). 마스터에 그 직업의 액티브 스킬이 없으면
    /// 습득을 건너뛰도록 null을 반환한다(캐릭터 생성 자체는 막지 않는다 — 스킬 없이 생성된다).
    /// 마스터 결함은 운영에서 찾아낼 수 있게 Error 로그로 남긴다.
    /// </summary>
    private int? ResolveStartingSkillCode(int classCode)
    {
        var skill = _masterData.StartingSkill(classCode);
        if (skill is null)
        {
            _logger.ZLogError($"직업 기본 액티브 스킬 미정의: classCode {classCode:@ClassCode} — skill_master의 액티브 스킬(skill_type=1) 확인 필요");
            return null;
        }

        return skill.SkillCode;
    }

    /// <summary>성별 값이 정의된 범위(1:남 2:여)인지 검사한다. 그 외 값은 InvalidGender로 거절한다.</summary>
    private static bool IsValidGender(int gender) =>
        gender == (int)CharacterGender.Male || gender == (int)CharacterGender.Female;

    /// <summary>새 캐릭터에 배정할 고유 식별자(사용되지 않은 가장 작은 번호). 캐릭터 삭제가 없으므로 사실상 보유 수 + 1이지만,
    /// 빈 번호를 찾는 방식이라 향후 삭제가 생겨도 식별자가 겹치지 않는다.</summary>
    private static int NextCharacterId(IReadOnlyCollection<CharacterSlot> owned)
    {
        var used = owned.Select(c => c.CharacterId).ToHashSet();
        var id = 1;
        while (used.Contains(id))
        {
            id++;
        }

        return id;
    }

    /// <summary>새 캐릭터를 자동 편성할 빈 파티 자리(1~3 중 사용되지 않은 가장 작은 번호). 파티가 가득 차 있으면 0(미편성).</summary>
    private static int FirstFreePartySlot(IReadOnlyCollection<CharacterSlot> owned)
    {
        var used = owned.Select(c => c.Slot).ToHashSet();
        for (var slot = 1; slot <= Constants.Party.MaxSlots; slot++)
        {
            if (!used.Contains(slot))
            {
                return slot;
            }
        }

        return Constants.Party.SlotUnassigned;
    }
}
