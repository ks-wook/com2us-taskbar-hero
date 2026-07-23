using System.Data.Common;
using GameServer.Data;
using SqlKata.Execution;

namespace GameServer.Repositories;

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

/// <summary>스킬 레벨업 트랜잭션 결과. NewLevel=올린 뒤 레벨, AvailablePoints=갱신 후 사용 가능 스킬 포인트.</summary>
public sealed record SkillLevelUpOutcome(SkillLevelUpStatus Status, int NewLevel, int AvailablePoints)
{
    public static SkillLevelUpOutcome Fail(SkillLevelUpStatus status) => new(status, 0, 0);
}

/// <summary>스킬 초기화 트랜잭션 결과 상태.</summary>
public enum SkillResetStatus
{
    Ok,
    InvalidCharacter,
}

/// <summary>스킬 초기화 트랜잭션 결과. ResetCount=삭제된 스킬 행 수, AvailablePoints=초기화 후 사용 가능 포인트(전액).</summary>
public sealed record SkillResetOutcome(SkillResetStatus Status, int ResetCount, int AvailablePoints)
{
    public static SkillResetOutcome Fail(SkillResetStatus status) => new(status, 0, 0);
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

/// <summary>액티브 스킬 장착 트랜잭션 결과. Equipped=설정 후 장착된 스킬 코드 목록.</summary>
public sealed record SkillEquipOutcome(SkillEquipStatus Status, List<int> Equipped)
{
    public static SkillEquipOutcome Fail(SkillEquipStatus status) => new(status, new List<int>());
}

/// <summary>룬 업그레이드 트랜잭션 결과 상태.</summary>
public enum RuneUpgradeStatus
{
    Ok,
    PrereqNotMet,          // 선행 룬 미해금(레벨 0)
    MaxLevel,              // 이미 최대 레벨
    InsufficientCurrency,  // 골드 부족
}

/// <summary>룬 업그레이드 트랜잭션 결과. NewLevel=올린 뒤 레벨, Cost=차감 골드, GoldBalance=차감 후 잔액.</summary>
public sealed record RuneUpgradeOutcome(RuneUpgradeStatus Status, int NewLevel, long Cost, long GoldBalance)
{
    public static RuneUpgradeOutcome Fail(RuneUpgradeStatus status) => new(status, 0, 0, 0);
}

public interface IGrowthRepository
{
    /// <summary>
    /// 스킬 레벨업을 한 트랜잭션으로 적용한다: 캐릭터 확인 → 대상 스킬 현재 레벨·사용 포인트 집계 →
    /// decide(마스터 검증: 직업·최대 레벨·포인트)로 판정 → player_skill 레벨 +1(없으면 INSERT).
    /// decide는 (classCode, charLevel, curLevel, spentPoints)→(status, availableAfter).
    /// </summary>
    Task<SkillLevelUpOutcome> ApplySkillLevelUpAsync(
        long userId, int characterId, int skillCode,
        Func<int, int, int, int, (SkillLevelUpStatus status, int availableAfter)> decide);

    /// <summary>대상 캐릭터의 player_skill 행을 모두 삭제해 스킬을 초기화한다(무료). totalPoints=(charLevel)→레벨 비례 총량.</summary>
    Task<SkillResetOutcome> ApplySkillResetAsync(long userId, int characterId, Func<int, int> totalPoints);

    /// <summary>
    /// 액티브 스킬 장착 목록을 설정한다(통째 교체): 캐릭터·보유 스킬 조회 → validate(마스터 검증)로 판정 →
    /// player_skill.equipped를 요청 목록에 맞춰 갱신. validate는 (classCode, 보유 스킬 code→level)→status.
    /// </summary>
    Task<SkillEquipOutcome> ApplySkillEquipAsync(
        long userId, int characterId, IReadOnlyList<int> skillCodes,
        Func<int, IReadOnlyDictionary<int, int>, SkillEquipStatus> validate);

    /// <summary>
    /// 룬 업그레이드를 한 트랜잭션으로 적용한다: 현재 레벨·최대 레벨·선행 룬(prereqCode) 확인 →
    /// costOf(현재 레벨)로 골드 비용 산출 → 골드 확인·차감 → player_rune 레벨 +1(없으면 INSERT).
    /// 룬 존재 여부는 호출 전(서비스, 마스터)에서 검증한다.
    /// </summary>
    Task<RuneUpgradeOutcome> ApplyRuneUpgradeAsync(
        long userId, int runeCode, int prereqCode, int maxLevel, Func<int, long> costOf);
}

// ── DB 행 매핑용 POCO(제네릭 매핑 전용, dynamic 금지). snake_case→PascalCase는 Dapper 규칙으로 매핑. ──
file sealed class CharClassLevelRow
{
    public int ClassCode { get; set; }
    public int Level { get; set; }
}

file sealed class SkillCodeLevelRow
{
    public int SkillCode { get; set; }
    public int Level { get; set; }
}

file sealed class ItemIdQtyRow
{
    public long PlayerItemId { get; set; }
    public long Quantity { get; set; }
}

/// <summary>성장(스킬·룬) 세이브 접근 계층(taskbar_hero_game). SqlKata 쿼리 빌더 + 제네릭 매핑만 사용한다(dynamic 금지).</summary>
public sealed class GrowthRepository : IGrowthRepository
{
    private const int RowTypeCurrency = 2;
    private const int GoldItemCode = 1;

    private readonly GameDbFactory _dbFactory;

    public GrowthRepository(GameDbFactory dbFactory) => _dbFactory = dbFactory;

    public async Task<SkillLevelUpOutcome> ApplySkillLevelUpAsync(
        long userId, int characterId, int skillCode,
        Func<int, int, int, int, (SkillLevelUpStatus status, int availableAfter)> decide)
    {
        await using var connection = _dbFactory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var db = _dbFactory.Create(connection);

            // 1) 대상 캐릭터 확인(직업·레벨은 검증에 사용).
            var charRow = await db.Query("player_character")
                .Select("class_code", "level")
                .Where("user_id", userId).Where("character_id", characterId)
                .FirstOrDefaultAsync<CharClassLevelRow>(transaction);
            if (charRow is null)
            {
                await transaction.RollbackAsync();
                return SkillLevelUpOutcome.Fail(SkillLevelUpStatus.InvalidCharacter);
            }

            // 2) 그 캐릭터의 보유 스킬 레벨 집계(대상 현재 레벨 + 사용 포인트 = 레벨 합).
            var skillRows = await db.Query("player_skill")
                .Select("skill_code", "level")
                .Where("user_id", userId).Where("character_id", characterId)
                .GetAsync<SkillCodeLevelRow>(transaction);

            var levels = skillRows.ToDictionary(r => r.SkillCode, r => r.Level);
            int curLevel = levels.GetValueOrDefault(skillCode, 0);
            int spent = levels.Values.Sum();

            // 3) 마스터 검증(직업 소속·최대 레벨·포인트 충족).
            var (status, availableAfter) = decide(charRow.ClassCode, charRow.Level, curLevel, spent);
            if (status != SkillLevelUpStatus.Ok)
            {
                await transaction.RollbackAsync();
                return SkillLevelUpOutcome.Fail(status);
            }

            // 4) 반영: 행이 있으면 레벨 +1, 없으면 INSERT(첫 습득).
            int newLevel = curLevel + 1;
            if (levels.ContainsKey(skillCode))
            {
                await db.Query("player_skill")
                    .Where("user_id", userId).Where("character_id", characterId).Where("skill_code", skillCode)
                    .UpdateAsync(new { level = newLevel }, transaction);
            }
            else
            {
                await db.Query("player_skill").InsertAsync(new
                {
                    user_id = userId,
                    character_id = characterId,
                    skill_code = skillCode,
                    level = newLevel,
                    equipped = 0,
                }, transaction);
            }

            await transaction.CommitAsync();
            return new SkillLevelUpOutcome(SkillLevelUpStatus.Ok, newLevel, availableAfter);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<SkillResetOutcome> ApplySkillResetAsync(long userId, int characterId, Func<int, int> totalPoints)
    {
        await using var connection = _dbFactory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var db = _dbFactory.Create(connection);

            // 1) 대상 캐릭터 확인(레벨은 초기화 후 총 포인트 산출에 사용).
            var charRow = await db.Query("player_character")
                .Select("class_code", "level")
                .Where("user_id", userId).Where("character_id", characterId)
                .FirstOrDefaultAsync<CharClassLevelRow>(transaction);
            if (charRow is null)
            {
                await transaction.RollbackAsync();
                return SkillResetOutcome.Fail(SkillResetStatus.InvalidCharacter);
            }

            // 2) 해당 캐릭터의 스킬 행 전부 삭제(포인트 전량 회수·장착 해제). 재화 변동 없음.
            int resetCount = await db.Query("player_skill")
                .Where("user_id", userId).Where("character_id", characterId)
                .DeleteAsync(transaction);

            await transaction.CommitAsync();
            return new SkillResetOutcome(SkillResetStatus.Ok, resetCount, totalPoints(charRow.Level));
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<SkillEquipOutcome> ApplySkillEquipAsync(
        long userId, int characterId, IReadOnlyList<int> skillCodes,
        Func<int, IReadOnlyDictionary<int, int>, SkillEquipStatus> validate)
    {
        await using var connection = _dbFactory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var db = _dbFactory.Create(connection);

            // 1) 대상 캐릭터 확인.
            var charRow = await db.Query("player_character")
                .Select("class_code", "level")
                .Where("user_id", userId).Where("character_id", characterId)
                .FirstOrDefaultAsync<CharClassLevelRow>(transaction);
            if (charRow is null)
            {
                await transaction.RollbackAsync();
                return SkillEquipOutcome.Fail(SkillEquipStatus.InvalidCharacter);
            }

            // 2) 보유 스킬(code→level) 조회.
            var skillRows = await db.Query("player_skill")
                .Select("skill_code", "level")
                .Where("user_id", userId).Where("character_id", characterId)
                .GetAsync<SkillCodeLevelRow>(transaction);
            var levels = skillRows.ToDictionary(r => r.SkillCode, r => r.Level);

            // 3) 마스터 검증(존재·직업·액티브·습득·한도).
            var status = validate(charRow.ClassCode, levels);
            if (status != SkillEquipStatus.Ok)
            {
                await transaction.RollbackAsync();
                return SkillEquipOutcome.Fail(status);
            }

            // 4) 반영: 전부 해제 후 요청 목록만 장착(통째 교체).
            await db.Query("player_skill")
                .Where("user_id", userId).Where("character_id", characterId)
                .UpdateAsync(new { equipped = 0 }, transaction);

            foreach (var code in skillCodes)
            {
                await db.Query("player_skill")
                    .Where("user_id", userId).Where("character_id", characterId).Where("skill_code", code)
                    .UpdateAsync(new { equipped = 1 }, transaction);
            }

            await transaction.CommitAsync();
            return new SkillEquipOutcome(SkillEquipStatus.Ok, skillCodes.ToList());
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<RuneUpgradeOutcome> ApplyRuneUpgradeAsync(
        long userId, int runeCode, int prereqCode, int maxLevel, Func<int, long> costOf)
    {
        await using var connection = _dbFactory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var db = _dbFactory.Create(connection);

            // 1) 대상 룬 현재 레벨(계정 공용). 없으면 0.
            var curLevel = await db.Query("player_rune")
                .Select("level")
                .Where("user_id", userId).Where("rune_code", runeCode)
                .FirstOrDefaultAsync<int?>(transaction) ?? 0;

            // 2) 최대 레벨 확인.
            if (curLevel >= maxLevel)
            {
                await transaction.RollbackAsync();
                return RuneUpgradeOutcome.Fail(RuneUpgradeStatus.MaxLevel);
            }

            // 3) 선행 룬 해금(레벨 ≥ 1) 확인(루트면 prereqCode=0이라 생략).
            if (prereqCode != 0)
            {
                var prereqLevel = await db.Query("player_rune")
                    .Select("level")
                    .Where("user_id", userId).Where("rune_code", prereqCode)
                    .FirstOrDefaultAsync<int?>(transaction) ?? 0;
                if (prereqLevel < 1)
                {
                    await transaction.RollbackAsync();
                    return RuneUpgradeOutcome.Fail(RuneUpgradeStatus.PrereqNotMet);
                }
            }

            // 4) 골드 비용 산출(서버 권위) + 잔액 확인.
            long cost = costOf(curLevel);
            var goldRow = await db.Query("player_item")
                .Select("player_item_id", "quantity")
                .Where("user_id", userId).Where("row_type", RowTypeCurrency).Where("item_code", GoldItemCode)
                .FirstOrDefaultAsync<ItemIdQtyRow>(transaction);

            long gold = goldRow?.Quantity ?? 0;
            if (gold < cost)
            {
                await transaction.RollbackAsync();
                return RuneUpgradeOutcome.Fail(RuneUpgradeStatus.InsufficientCurrency);
            }

            // 5) 골드 차감(재화 행 UPDATE) + 룬 레벨 +1(없으면 INSERT).
            long newGold = gold - cost;
            if (goldRow is not null)
            {
                await db.Query("player_item").Where("player_item_id", goldRow.PlayerItemId)
                    .UpdateAsync(new { quantity = newGold }, transaction);
            }

            int newLevel = curLevel + 1;
            if (curLevel == 0)
            {
                await db.Query("player_rune").InsertAsync(new
                {
                    user_id = userId,
                    rune_code = runeCode,
                    level = newLevel,
                }, transaction);
            }
            else
            {
                await db.Query("player_rune")
                    .Where("user_id", userId).Where("rune_code", runeCode)
                    .UpdateAsync(new { level = newLevel }, transaction);
            }

            await transaction.CommitAsync();
            return new RuneUpgradeOutcome(RuneUpgradeStatus.Ok, newLevel, cost, newGold);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
