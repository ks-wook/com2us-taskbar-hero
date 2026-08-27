using System.Data.Common;
using GameServer.Models;
using GameServer.Repositories.GameDb.Interfaces;
using SqlKata.Execution;

namespace GameServer.Repositories.GameDb;

/// <summary>스킬 레벨업 트랜잭션 결과. NewLevel=올린 뒤 레벨, AvailablePoints=갱신 후 사용 가능 스킬 포인트.</summary>
public sealed record SkillLevelUpOutcome(SkillLevelUpStatus Status, int NewLevel, int AvailablePoints)
{
    public static SkillLevelUpOutcome Fail(SkillLevelUpStatus status) => new(status, 0, 0);
}

/// <summary>스킬 초기화 트랜잭션 결과. ResetCount=레벨 0으로 되돌린 스킬 수, AvailablePoints=초기화 후 사용 가능 포인트(전액).</summary>
public sealed record SkillResetOutcome(SkillResetStatus Status, int ResetCount, int AvailablePoints)
{
    public static SkillResetOutcome Fail(SkillResetStatus status) => new(status, 0, 0);
}

/// <summary>액티브 스킬 장착 트랜잭션 결과. Equipped=설정 후 장착된 스킬 코드 목록.</summary>
public sealed record SkillEquipOutcome(SkillEquipStatus Status, List<int> Equipped)
{
    public static SkillEquipOutcome Fail(SkillEquipStatus status) => new(status, new List<int>());
}

/// <summary>룬 업그레이드 트랜잭션 결과. NewLevel=올린 뒤 레벨, Cost=차감 골드, GoldBalance=차감 후 잔액.</summary>
public sealed record RuneUpgradeOutcome(RuneUpgradeStatus Status, int NewLevel, long Cost, long GoldBalance)
{
    public static RuneUpgradeOutcome Fail(RuneUpgradeStatus status) => new(status, 0, 0, 0);
}

/// <summary>성장(스킬·룬) 세이브 접근 계층(taskbar_hero_game). SqlKata 쿼리 빌더 + 제네릭 매핑만 사용한다(dynamic 금지).</summary>
public sealed class GrowthRepository : GameDbBase, IGrowthRepository
{
    /// <summary>세이브 DB 커넥션 팩토리를 기반 클래스로 전달한다.</summary>
    public GrowthRepository(GameDbFactory dbFactory) : base(dbFactory) { }

    /// <summary>
    /// 스킬 1레벨 상승을 단일 커넥션의 단일 트랜잭션으로 적용한다.
    /// 검증 실패(InvalidCharacter·SkillNotFound·ClassMismatch·MaxLevel·InsufficientPoint)는 즉시 롤백 후
    /// Fail 상태로 반환하고, 예외는 롤백 후 전파한다.
    /// </summary>
    /// <remarks>
    /// 한 트랜잭션으로 묶는 작업(하나라도 실패하면 전부 롤백 — 포인트 계산의 근거가 된 스냅샷과 반영이 어긋나지 않게 한다):
    /// <para>1) player_character SELECT — 대상 캐릭터 존재 확인 및 직업·레벨 확보(검증 입력)</para>
    /// <para>2) player_skill SELECT — 해당 캐릭터의 보유 스킬 레벨 집계(대상 스킬 현재 레벨 + 사용한 총 포인트)</para>
    /// <para>3) decide 델리게이트 — 마스터 검증(스킬 존재·직업 소속·최대 레벨·잔여 포인트)과 반영 후 잔여 포인트 산출(DB 접근 없음)</para>
    /// <para>4) player_skill UPDATE 또는 INSERT — 기존 행(초기화로 레벨 0이 된 행 포함)이면 레벨 +1, 행 자체가 없으면 레벨 1·미장착으로 새 행 생성</para>
    /// </remarks>
    public async Task<SkillLevelUpOutcome> ApplySkillLevelUpAsync(
        long userId, int characterId, int skillCode,
        Func<int, int, int, int, (SkillLevelUpStatus status, int availableAfter)> decide)
        => await TransactionAsync<SkillLevelUpOutcome>(async (db, transaction) =>
        {
            // 1) 대상 캐릭터 확인(직업·레벨은 검증에 사용).
            var charRow = await db.Query("player_character")
                .Select("class_code", "level")
                .Where("user_id", userId).Where("character_id", characterId)
                .FirstOrDefaultAsync<CharClassLevelRow>(transaction);
            if (charRow is null)
            {
                return TxResult<SkillLevelUpOutcome>.Rollback(SkillLevelUpOutcome.Fail(SkillLevelUpStatus.InvalidCharacter));
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
                return TxResult<SkillLevelUpOutcome>.Rollback(SkillLevelUpOutcome.Fail(status));
            }

            // 4) 반영: 행이 있으면 레벨 +1, 없으면 INSERT(첫 습득).
            int newLevel = curLevel + 1;
            if (levels.ContainsKey(skillCode))
            {
                // 읽은 레벨 그대로일 때만 올린다 — 조건이 없으면 동시 요청 둘이 같은 레벨을 읽고 같은 값을 써,
                // 스킬 포인트는 두 번 쓰이는데 레벨은 한 번만 오른다.
                var raised = await db.Query("player_skill")
                    .Where("user_id", userId).Where("character_id", characterId).Where("skill_code", skillCode)
                    .Where("level", curLevel)
                    .UpdateAsync(new { level = newLevel }, transaction);
                if (raised == 0)
                {
                    throw new ConcurrencyConflictException("스킬 레벨");
                }
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

            return TxResult<SkillLevelUpOutcome>.Commit(new SkillLevelUpOutcome(SkillLevelUpStatus.Ok, newLevel, availableAfter));
        });

    /// <summary>
    /// 대상 캐릭터의 스킬을 전부 초기화(무료)하는 작업을 단일 커넥션의 단일 트랜잭션으로 적용한다.
    /// 캐릭터가 없으면 롤백 후 InvalidCharacter를 반환하고, 예외는 롤백 후 전파한다.
    /// </summary>
    /// <remarks>
    /// 한 트랜잭션으로 묶는 작업(재화 변동은 없다):
    /// <para>1) player_character SELECT — 대상 캐릭터 존재 확인 및 레벨 확보(회수 후 총 포인트 산출 입력)</para>
    /// <para>2) player_skill UPDATE — 레벨 1 이상인 스킬 행을 level=0·equipped=0으로 갱신(포인트 전액 회수 + 장착 해제). 행은 삭제하지 않고 남기며, 레벨 0 행은 미습득으로 보아 세이브 조회에서 제외한다(SaveRepository.GetSkillsAsync)</para>
    /// <para>3) totalPoints 델리게이트 — 캐릭터 레벨 기준 총 스킬 포인트 산출(DB 접근 없음)</para>
    /// </remarks>
    public async Task<SkillResetOutcome> ApplySkillResetAsync(long userId, int characterId, Func<int, int> totalPoints)
        => await TransactionAsync<SkillResetOutcome>(async (db, transaction) =>
        {
            // 1) 대상 캐릭터 확인(레벨은 초기화 후 총 포인트 산출에 사용).
            var charRow = await db.Query("player_character")
                .Select("class_code", "level")
                .Where("user_id", userId).Where("character_id", characterId)
                .FirstOrDefaultAsync<CharClassLevelRow>(transaction);
            if (charRow is null)
            {
                return TxResult<SkillResetOutcome>.Rollback(SkillResetOutcome.Fail(SkillResetStatus.InvalidCharacter));
            }

            // 2) 해당 캐릭터의 투자된 스킬 행을 레벨 0·미장착으로 되돌린다(행 삭제 없음, 포인트 전량 회수). 재화 변동 없음.
            int resetCount = await db.Query("player_skill")
                .Where("user_id", userId).Where("character_id", characterId).Where("level", ">", 0)
                .UpdateAsync(new { level = 0, equipped = 0 }, transaction);

            return TxResult<SkillResetOutcome>.Commit(new SkillResetOutcome(SkillResetStatus.Ok, resetCount, totalPoints(charRow.Level)));
        });

    /// <summary>
    /// 액티브 스킬 장착 목록을 통째로 교체하는 작업을 단일 커넥션의 단일 트랜잭션으로 적용한다.
    /// 검증 실패(InvalidCharacter·SkillNotFound·ClassMismatch·NotActive·NotLearned·LimitExceeded)는 즉시 롤백 후
    /// Fail 상태로 반환하고, 예외는 롤백 후 전파한다.
    /// </summary>
    /// <remarks>
    /// 한 트랜잭션으로 묶는 작업(전량 해제와 재장착을 함께 커밋해 "아무것도 장착되지 않은" 중간 상태가 보이지 않게 한다):
    /// <para>1) player_character SELECT — 대상 캐릭터 존재 확인 및 직업 확보(검증 입력)</para>
    /// <para>2) player_skill SELECT — 보유 스킬 code→level 사전 확보</para>
    /// <para>3) validate 델리게이트 — 마스터 검증(스킬 존재·직업 소속·액티브 여부·습득 여부·장착 한도)(DB 접근 없음)</para>
    /// <para>4) player_skill UPDATE — 해당 캐릭터의 equipped를 전부 0으로 해제</para>
    /// <para>5) player_skill UPDATE(요청 코드별) — 요청 목록의 스킬만 equipped=1로 재설정</para>
    /// </remarks>
    public async Task<SkillEquipOutcome> ApplySkillEquipAsync(
        long userId, int characterId, IReadOnlyList<int> skillCodes,
        Func<int, IReadOnlyDictionary<int, int>, SkillEquipStatus> validate)
        => await TransactionAsync<SkillEquipOutcome>(async (db, transaction) =>
        {
            // 1) 대상 캐릭터 확인.
            var charRow = await db.Query("player_character")
                .Select("class_code", "level")
                .Where("user_id", userId).Where("character_id", characterId)
                .FirstOrDefaultAsync<CharClassLevelRow>(transaction);
            if (charRow is null)
            {
                return TxResult<SkillEquipOutcome>.Rollback(SkillEquipOutcome.Fail(SkillEquipStatus.InvalidCharacter));
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
                return TxResult<SkillEquipOutcome>.Rollback(SkillEquipOutcome.Fail(status));
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

            return TxResult<SkillEquipOutcome>.Commit(new SkillEquipOutcome(SkillEquipStatus.Ok, skillCodes.ToList()));
        });

    /// <summary>
    /// 룬 1레벨 업그레이드(골드 소모)를 단일 커넥션의 단일 트랜잭션으로 적용한다.
    /// 검증 실패(MaxLevel·PrereqNotMet·InsufficientCurrency)는 즉시 롤백 후 Fail 상태로 반환하고, 예외는 롤백 후 전파한다.
    /// 룬 존재 여부는 호출 전(서비스·마스터)에서 검증한다.
    /// </summary>
    /// <remarks>
    /// 한 트랜잭션으로 묶는 작업(하나라도 실패하면 전부 롤백 — 골드만 차감되고 레벨이 안 오르는 상태를 막는다):
    /// <para>1) player_rune SELECT — 대상 룬 현재 레벨(계정 공용, 행이 없으면 0) 확인 후 최대 레벨 도달 검사</para>
    /// <para>2) player_rune SELECT — 선행 룬(prereqCode≠0일 때) 해금 여부 확인(레벨 &lt; 1 → PrereqNotMet)</para>
    /// <para>3) costOf 델리게이트 — 현재 레벨 기준 골드 비용 산출(서버 권위, DB 접근 없음)</para>
    /// <para>4) player_item(재화 행) UPDATE — 잔액이 비용 이상일 때만 깎는 원자 갱신. 0행이면 잔액 부족
    ///     (→ InsufficientCurrency)이며 아무것도 바뀌지 않는다</para>
    /// <para>5) player_rune UPDATE 또는 INSERT — 기존 행이면 레벨 +1, 첫 해금이면 레벨 1로 새 행 생성</para>
    /// </remarks>
    public async Task<RuneUpgradeOutcome> ApplyRuneUpgradeAsync(
        long userId, int runeCode, int prereqCode, int maxLevel, Func<int, long> costOf)
        => await TransactionAsync<RuneUpgradeOutcome>(async (db, transaction) =>
        {
            // 1) 대상 룬 현재 레벨(계정 공용). 없으면 0.
            var curLevel = await db.Query("player_rune")
                .Select("level")
                .Where("user_id", userId).Where("rune_code", runeCode)
                .FirstOrDefaultAsync<int?>(transaction) ?? 0;

            // 2) 최대 레벨 확인.
            if (curLevel >= maxLevel)
            {
                return TxResult<RuneUpgradeOutcome>.Rollback(RuneUpgradeOutcome.Fail(RuneUpgradeStatus.MaxLevel));
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
                    return TxResult<RuneUpgradeOutcome>.Rollback(RuneUpgradeOutcome.Fail(RuneUpgradeStatus.PrereqNotMet));
                }
            }

            // 4) 골드 비용 산출(서버 권위) + 차감. 잔액 확인이 차감 문장의 조건이라 확인과 차감 사이가 벌어지지 않는다.
            long cost = costOf(curLevel);
            long? debited = await TryDebitCurrencyAsync(
                db, transaction, userId, Constants.Currency.GoldItemCode, cost);
            if (debited is null)
            {
                return TxResult<RuneUpgradeOutcome>.Rollback(RuneUpgradeOutcome.Fail(RuneUpgradeStatus.InsufficientCurrency));
            }

            long newGold = debited.Value;

            // 5) 룬 레벨 +1(없으면 INSERT).
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
                // 읽은 레벨 그대로일 때만 올린다(스킬 레벨과 같은 이유 — 골드만 두 번 빠지는 것을 막는다).
                var raised = await db.Query("player_rune")
                    .Where("user_id", userId).Where("rune_code", runeCode).Where("level", curLevel)
                    .UpdateAsync(new { level = newLevel }, transaction);
                if (raised == 0)
                {
                    throw new ConcurrencyConflictException("룬 레벨");
                }
            }

            return TxResult<RuneUpgradeOutcome>.Commit(new RuneUpgradeOutcome(RuneUpgradeStatus.Ok, newLevel, cost, newGold));
        });

    /// <summary>
    /// 재화 행(<c>player_item</c>, <c>row_type=2</c>)의 <c>player_item_id</c>를 찾는다(없으면 null).
    /// 잔액이 아니라 <b>행 번호</b>만 읽으므로 이 조회는 낡지 않는다 — 재화 행은 세이브를 만들 때 함께 만들어지고
    /// 이후 지워지지 않아 번호가 고정이다(<c>SaveRepository.CreatePlayerWithFirstCharacterAsync</c>).
    /// </summary>
    private static async Task<long?> FindCurrencyRowIdAsync(
        QueryFactory db, DbTransaction tx, long userId, int currencyCode)
        => await db.Query("player_item").Select("player_item_id")
            .Where("user_id", userId)
            .Where("row_type", Constants.PlayerItemRow.Currency)
            .Where("item_code", currencyCode)
            .FirstOrDefaultAsync<long?>(tx);

    /// <summary>갱신 직후 잔액을 기본키로 읽는다. 자기 트랜잭션이 그 행에 X락을 쥔 상태라 방금 확정한 값이 보인다.</summary>
    private static async Task<long> ReadCurrencyBalanceAsync(QueryFactory db, DbTransaction tx, long rowId)
        => await db.Query("player_item").Select("quantity")
            .Where("player_item_id", rowId)
            .FirstOrDefaultAsync<long?>(tx) ?? 0;

    /// <summary>
    /// 재화를 <paramref name="amount"/>만큼 차감한다. 잔액이 부족하면 <b>아무것도 바꾸지 않고</b> null을 돌려주고,
    /// 성공하면 차감 후 잔액을 돌려준다. 재화 행 자체가 없으면 <see cref="CurrencyRowMissingException"/>을 던진다.
    /// <para><b>읽어서 계산한 값을 쓰지 않는다.</b> 잠금 없는 <c>SELECT</c>는 스냅샷 읽기라 동시에 도는 다른
    /// 트랜잭션의 변경을 보지 못한다 — 같은 계정의 요청 둘이 겹치면 양쪽이 같은 잔액을 읽고 각자 계산한 값을
    /// 덮어써 한쪽 차감이 사라진다. <c>quantity = quantity - @amount</c>로 DB가 직접 계산하게 하면 그 갱신은
    /// 최신 커밋값을 읽고 그 행을 잠그므로 겹칠 틈이 없고, 잔액 검사도 같은 문장의 <c>WHERE</c>가 겸한다.</para>
    /// <para><b>갱신은 기본키로 건다.</b> <c>WHERE user_id = ? AND row_type = 2 AND item_code = ?</c>로 바로 갱신하면
    /// 이 조건에 맞는 인덱스가 없어 그 계정의 아이템 행까지 훑으며 잠그고, 잠그는 순서가 엇갈리면 교착이 난다.</para>
    /// <para>상대값 갱신은 SqlKata 빌더로 표현되지 않아(증감폭을 <c>int</c>로만 받는다) 파라미터를 바인딩한
    /// 문장을 쓴다. 문자열을 조립하지는 않는다.</para>
    /// </summary>
    private static async Task<long?> TryDebitCurrencyAsync(
        QueryFactory db, DbTransaction tx, long userId, int currencyCode, long amount)
    {
        var rowId = await FindCurrencyRowIdAsync(db, tx, userId, currencyCode);

        // 비용 0(무료 경로)은 갱신할 것이 없다. 응답에 담을 현재 잔액만 돌려준다.
        if (amount <= 0)
        {
            return rowId is null ? 0 : await ReadCurrencyBalanceAsync(db, tx, rowId.Value);
        }

        // 재화 행이 없으면 잔액 부족과 같은 값으로 돌려주지 않는다 — 서버 결함이 사용자 실수로 응답되고
        // 기록도 남지 않는다. 정상 경로로는 발생하지 않는 상황이라 예외로 알린다.
        if (rowId is null)
        {
            throw new CurrencyRowMissingException(userId, currencyCode);
        }

        var affected = await db.StatementAsync(
            """
            UPDATE player_item SET quantity = quantity - @amount
             WHERE player_item_id = @rowId AND quantity >= @amount
            """,
            new { rowId, amount }, tx);

        // 0행 = 잔액 부족. 조건이 갱신 문장 안에 있으므로 부족하면 아무것도 바뀌지 않는다.
        return affected == 0 ? null : await ReadCurrencyBalanceAsync(db, tx, rowId.Value);
    }
}
