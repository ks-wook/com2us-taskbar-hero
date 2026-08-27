using System.Data.Common;
using GameServer.MasterData;
using GameServer.Models;
using GameServer.Repositories.GameDb.Interfaces;
using SqlKata.Execution;
using TaskbarHero.Common.Dto;
using GameServer.Util;

namespace GameServer.Repositories.GameDb;

/// <summary>오프라인 정산 대상 계정의 기준 시각·파밍 스테이지 좌표(game_player 스냅샷).</summary>
public sealed record OfflinePlayerContext(long LastActiveAt, int Act, int Difficulty, int Stage);

/// <summary>오프라인 정산 트랜잭션 결과.</summary>
public sealed record OfflineClaimOutcome(
    OfflineClaimStatus Status,
    long EffectiveSec,
    bool Capped,
    long Gold,
    long Exp,
    long GoldBalance,
    List<OfflineCharacterState> Characters,
    long LastActiveAt)
{
    /// <summary>
    /// 이번 정산 경험치로 레벨이 오른 캐릭터들. 스테이지 클리어와 같은 <c>character.levelup</c> 이벤트로 나가며
    /// <c>source</c>만 <c>offline</c>으로 갈린다(로그 이벤트 정의 5.3).
    /// </summary>
    public IReadOnlyList<CharacterLevelUp> LevelUps { get; init; } = Array.Empty<CharacterLevelUp>();

    public static OfflineClaimOutcome Fail(OfflineClaimStatus status)
        => new(status, 0, false, 0, 0, 0, new List<OfflineCharacterState>(), 0);
}

/// <summary>오프라인 보상 정산 세이브 접근 계층(taskbar_hero_game). SqlKata 쿼리 빌더 + 제네릭 매핑만 사용한다(dynamic 금지).</summary>
public sealed class OfflineRepository : GameDbBase, IOfflineRepository
{
    private readonly ILevelUpCalculator _levelUp;

    /// <summary>세이브 DB 커넥션 팩토리(기반 클래스로 전달)와 레벨업 계산기를 주입받는다.</summary>
    public OfflineRepository(GameDbFactory dbFactory, ILevelUpCalculator levelUp) : base(dbFactory)
        => _levelUp = levelUp;

    /// <summary>
    /// game_player에서 정산 기준 시각(last_active_at)과 파밍 스테이지 좌표를 단건 조회한다.
    /// 트랜잭션 없이 자체 커넥션으로 읽는 사전 조회이며(실제 정산 판정은 ClaimAsync가 트랜잭션 내부에서 재확인),
    /// 계정 세이브가 없으면 null.
    /// </summary>
    public async Task<OfflinePlayerContext?> GetContextAsync(long userId)
    {
        using var db = Db();
        var row = await db.Query("game_player")
            .Select("last_active_at", "act", "difficulty", "stage")
            .Where("user_id", userId)
            .FirstOrDefaultAsync<OfflineContextRow>();

        if (row is null)
        {
            return null;
        }

        return new OfflinePlayerContext(row.LastActiveAt, row.Act, row.Difficulty, row.Stage);
    }

    /// <summary>
    /// 오프라인(방치) 보상 정산을 단일 커넥션의 단일 트랜잭션으로 적용한다.
    /// 계정이 없으면 NoPlayer, 경과가 최소 기준 미만이거나 CAS 선점에 실패하면 AlreadyClaimed를 롤백 후 반환하며,
    /// 예외는 롤백 후 전파한다.
    /// </summary>
    /// <remarks>
    /// 한 트랜잭션으로 묶는 작업(하나라도 실패하면 전부 롤백 — 기준 시각만 리셋되고 보상이 안 들어가는 상태를 막는다):
    /// <para>1) game_player SELECT — 트랜잭션 내부에서 last_active_at을 재확인해 경과 시간 재계산(최소 기준 미만 → AlreadyClaimed)</para>
    /// <para>2) game_player CAS UPDATE — last_active_at이 1)에서 관측한 값 그대로일 때만 now로 리셋해 정산권을 선점.
    ///     0행이면 동시 요청이 먼저 정산한 것이므로 롤백(AlreadyClaimed). 중복 지급을 막는 핵심 게이트다.</para>
    /// <para>3) computeReward 델리게이트 — 경과 시간에 상한을 적용해 골드·경험치 산출(서버 권위, DB 접근 없음)</para>
    /// <para>4) player_item(재화 행) 원자 가산 — 정산 골드 적립, 갱신 후 잔액 산출</para>
    /// <para>5) player_character SELECT + 캐릭터별 UPDATE — <b>파티에 편성된(slot≠0)</b> 캐릭터에만 동일 경험치 지급 후 applyExp 델리게이트로 레벨 재계산</para>
    /// </remarks>
    public async Task<OfflineClaimOutcome> ClaimAsync(
        long userId,
        long nowUnix,
        long minRewardSec,
        Func<long, (long effectiveSec, bool capped, long gold, long exp)> computeReward)
        => await TransactionAsync<OfflineClaimOutcome>(async (db, transaction) =>
        {
            // 1) 기준 시각 재확인(트랜잭션 내부). 잠금 상태의 경과가 최소 기준 미만이면 이미 정산된 것으로 간주.
            var playerRow = await db.Query("game_player")
                .Select("last_active_at")
                .Where("user_id", userId)
                .FirstOrDefaultAsync<LastActiveRow>(transaction);

            if (playerRow is null)
            {
                return TxResult<OfflineClaimOutcome>.Rollback(OfflineClaimOutcome.Fail(OfflineClaimStatus.NoPlayer));
            }

            long observed = playerRow.LastActiveAt;
            long elapsed = DateTimeUtil.ElapsedSeconds(observed, nowUnix);
            if (elapsed < minRewardSec)
            {
                return TxResult<OfflineClaimOutcome>.Rollback(OfflineClaimOutcome.Fail(OfflineClaimStatus.AlreadyClaimed));
            }

            // 2) 정산권 선점(CAS): last_active_at이 관측값 그대로일 때만 now로 리셋한다.
            //    동시 요청이 먼저 정산했으면 관측값과 달라 0행 → 롤백(AlreadyClaimed). 중복 지급 방지의 핵심 게이트.
            var claimed = await db.Query("game_player")
                .Where("user_id", userId).Where("last_active_at", observed)
                .UpdateAsync(new { last_active_at = nowUnix, updated_at = nowUnix }, transaction);

            if (claimed == 0)
            {
                return TxResult<OfflineClaimOutcome>.Rollback(OfflineClaimOutcome.Fail(OfflineClaimStatus.AlreadyClaimed));
            }

            // 3) 보상 산출(서버 권위): 상한 적용 후 골드·경험치.
            var (effectiveSec, capped, gold, exp) = computeReward(elapsed);

            // 4) 골드 적립(재화 행 원자 가산).
            long goldBalance = await CreditCurrencyAsync(
                db, transaction, userId, Constants.Currency.GoldItemCode, gold);

            // 5) 경험치 지급(파티 편성 캐릭터 동일) + 레벨 재계산.
            //    미편성(slot=0) 캐릭터는 방치 전투에 참가하지 않았으므로 경험치를 받지 않는다(세이브 데이터 기획서 5.5).
            //    **잠금 조회**로 읽는다(스테이지 클리어와 같은 이유).
            var charRows = await db.SelectAsync<CharProgressRow>(
                """
                SELECT character_id, level, exp, class_code FROM player_character
                 WHERE user_id = @userId AND slot <> @unassigned ORDER BY slot FOR UPDATE
                """,
                new { userId, unassigned = Constants.Party.SlotUnassigned }, transaction);

            var characters = new List<OfflineCharacterState>();
            var levelUps = new List<CharacterLevelUp>();
            foreach (var c in charRows)
            {
                var (newLevel, newExp, leveledUp) = _levelUp.Calculate(c.Level, c.Exp, exp);

                // 읽은 레벨·경험치가 그대로일 때만 쓴다(스테이지 클리어와 같은 이유).
                // 지급액이 0이면 바뀔 값이 없으므로 갱신 자체를 건너뛴다.
                if (newLevel != c.Level || newExp != c.Exp)
                {
                    var applied = await db.Query("player_character")
                        .Where("user_id", userId).Where("character_id", c.CharacterId)
                        .Where("level", c.Level).Where("exp", c.Exp)
                        .UpdateAsync(new { level = newLevel, exp = newExp }, transaction);
                    if (applied == 0)
                    {
                        throw new ConcurrencyConflictException("캐릭터 경험치");
                    }
                }

                characters.Add(new OfflineCharacterState
                {
                    characterId = c.CharacterId,
                    level = newLevel,
                    exp = newExp,
                });

                // 오른 캐릭터만 담는다 — 이벤트 로그는 "레벨이 올랐다"는 사건 자체가 1행이다(5.3).
                if (leveledUp)
                {
                    levelUps.Add(new CharacterLevelUp(c.CharacterId, c.ClassCode, c.Level, newLevel));
                }
            }

            return TxResult<OfflineClaimOutcome>.Commit(new OfflineClaimOutcome(
                OfflineClaimStatus.Ok, effectiveSec, capped, gold, exp, goldBalance, characters, nowUnix)
            {
                LevelUps = levelUps,
            });
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
    /// 재화를 <paramref name="amount"/>만큼 적립하고 적립 후 잔액을 돌려준다.
    /// <para><b>읽어서 계산한 값을 쓰지 않는다.</b> <c>quantity = quantity + @amount</c>로 DB가 직접 계산하게 해,
    /// 같은 계정에 지급 둘이 동시에 들어와도 한쪽 적립이 다른 쪽에 덮이지 않게 한다. 갱신은 그 계정의 아이템 행까지
    /// 훑어 잠그지 않도록 <b>기본키</b>로 건다.</para>
    /// <para><b>재화 행이 없으면 만들지 않고 예외로 알린다.</b> 재화 행은 세이브를 만들 때 잔액 0으로 함께 생성하고
    /// 어디서도 지우지 않으므로, 여기서 없다는 것은 그 규칙이 깨졌다는 뜻이다. 이때 새로 만들면 잔액 0인 계정에
    /// 지급 둘이 동시에 들어올 때 양쪽이 모두 "행 없음"을 보고 INSERT해 재화 행이 둘로 갈라지고, 그 뒤로는 조회가
    /// 한 행만 읽어 나머지 잔액이 보이지 않게 된다.</para>
    /// </summary>
    private static async Task<long> CreditCurrencyAsync(
        QueryFactory db, DbTransaction tx, long userId, int currencyCode, long amount)
    {
        var rowId = await FindCurrencyRowIdAsync(db, tx, userId, currencyCode);

        // 지급액 0(보상이 아이템뿐인 경로)은 갱신할 것이 없다.
        if (amount <= 0)
        {
            return rowId is null ? 0 : await ReadCurrencyBalanceAsync(db, tx, rowId.Value);
        }

        if (rowId is null)
        {
            throw new InvalidOperationException(
                $"재화 행이 없어 적립하지 못했습니다(userId {userId}, currencyCode {currencyCode}). " +
                "세이브 생성 시 만들어져 있어야 하는 행입니다.");
        }

        await db.StatementAsync(
            """
            UPDATE player_item SET quantity = quantity + @amount WHERE player_item_id = @rowId
            """,
            new { rowId, amount }, tx);

        return await ReadCurrencyBalanceAsync(db, tx, rowId.Value);
    }
}
