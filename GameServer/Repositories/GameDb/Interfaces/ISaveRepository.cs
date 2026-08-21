using GameServer.Repositories.GameDb;
using TaskbarHero.Common.Dto;

namespace GameServer.Repositories.GameDb.Interfaces;

public interface ISaveRepository
{
    /// <summary>game_player 1행을 세이브 응답용 DTO로 조회한다(계정 세이브 없으면 null).</summary>
    Task<PlayerDto?> GetPlayerAsync(long userId);

    /// <summary>계정의 보유 캐릭터 목록(편성된 파티 자리 순 → 미편성 순)을 조회한다.</summary>
    Task<List<CharacterDto>> GetCharactersAsync(long userId);

    /// <summary>코어 로드용 재화 목록(player_item의 row_type=2 행)을 조회한다.</summary>
    Task<List<CurrencyDto>> GetCurrenciesAsync(long userId);

    /// <summary>코어 로드용 장착 장비 목록(player_item_equipped 전 행, 최대 18개)을 조회한다.</summary>
    Task<List<EquippedItemDto>> GetEquippedAsync(long userId);

    /// <summary>가방 아이템(row_type=1, 배치된 행) 총 개수를 센다. 페이징 진행률 표시용.</summary>
    Task<int> GetBagItemCountAsync(long userId);

    /// <summary>계정의 전 캐릭터 보유 스킬(레벨·장착 여부)을 조회한다.</summary>
    Task<List<SkillDto>> GetSkillsAsync(long userId);

    /// <summary>계정 공용 룬 목록(코드·레벨)을 조회한다.</summary>
    Task<List<RuneDto>> GetRunesAsync(long userId);

    /// <summary>계정의 큐브 상태(레벨·경험치)를 조회한다(행 없으면 null).</summary>
    Task<CubeDto?> GetCubeAsync(long userId);

    /// <summary>캐릭터 추가 생성 시 식별자·파티 자리 배정과 직업 중복 검사에 쓸 보유 캐릭터 목록(식별자 + 직업 + 파티 자리)을 조회한다.</summary>
    Task<List<CharacterSlot>> GetCharacterSlotsAsync(long userId);

    /// <summary>최초 접속: game_player + 첫 캐릭터(직업·성별, 파티 1번 자리) + 기본 무기(장착 상태) + 기본 액티브 스킬(습득·장착) +
    /// 큐브 + 신규 가입 지원금 메일을 한 트랜잭션으로 초기화한다. welcomeMail·startingEquipment·startingSkillCode가 null이면 그 항목은 건너뛴다.
    /// <para>반환값은 <b>발급된 신규 지원금 메일 id</b>(발급하지 않았으면 0) — 커밋 이후 발급 이벤트 로그가 쓴다.</para></summary>
    Task<long> CreatePlayerWithFirstCharacterAsync(
        long userId, string nickname, int classCode, int gender, int inventoryCapacity, long nowUnix,
        MailDraft? welcomeMail, StartingEquipment? startingEquipment, int? startingSkillCode);

    /// <summary>기존 계정에 캐릭터 1개 추가. 생성 비용(goldCost)을 골드에서 확인·차감하고 지정 파티 자리(slot, 빈 자리 없으면 0)로 삽입하며,
    /// startingEquipment가 있으면 기본 무기를 지급해 장착까지, startingSkillCode가 있으면 기본 액티브 스킬을 습득·장착까지 마치는 한 트랜잭션.</summary>
    Task<AddCharacterOutcome> AddCharacterAsync(
        long userId, int characterId, int classCode, int slot, int gender, long goldCost,
        StartingEquipment? startingEquipment, int? startingSkillCode, long nowUnix);

    /// <summary>클라이언트가 보낸 파티 편성 스냅샷(자리별 캐릭터)을 그대로 저장한다.
    /// 목록에 없는 보유 캐릭터는 미편성(slot 0)이 되는 한 트랜잭션.</summary>
    Task<ArrangePartyOutcome> SavePartyAsync(long userId, IReadOnlyList<PartyMemberDto> members);

    /// <summary>last_active_at 갱신. 갱신된 행 수(0이면 계정 없음) 반환.</summary>
    Task<int> UpdateLastActiveAsync(long userId, long nowUnix);
}
