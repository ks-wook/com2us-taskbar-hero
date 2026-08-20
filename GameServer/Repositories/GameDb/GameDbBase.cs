using MySqlConnector;
using SqlKata.Execution;

namespace GameServer.Repositories.GameDb;

/// <summary>
/// 트랜잭션 본문의 결말 — 커밋할지 롤백할지를 <b>예외가 아니라 반환값으로</b> 말한다.
/// <para>이 계층의 롤백은 대부분 예외가 아니라 <b>검증 실패 후 정상 반환</b>이다(중복 획득·잔액 부족·경합 패배).
/// 그래서 "정상 반환이면 커밋"하는 흔한 래퍼를 쓰면 검증 실패가 커밋되고, 흐름 제어용 예외를 던지는 것도 답이 아니다.
/// 커밋 여부를 값에 실어 보내면 <see cref="GameDbBase.TransactionAsync"/>가 그대로 따른다.</para>
/// </summary>
public readonly record struct TxResult<T>(bool ShouldCommit, T Value)
{
    /// <summary>변경을 확정한다.</summary>
    public static TxResult<T> Commit(T value) => new(true, value);

    /// <summary>변경을 버리고 <paramref name="value"/>를 결과로 돌려준다(검증 실패 경로).</summary>
    public static TxResult<T> Rollback(T value) => new(false, value);
}

/// <summary>
/// MySQL 세이브 DB(GameDb) 계층의 공통 기반 — <b>커넥션 개시와 트랜잭션 커밋/롤백 규약</b>을 한곳에 모은다.
/// <para>연결 문자열은 <see cref="GameDbFactory"/>가 계속 보유하고(싱글턴 1개) 이 클래스는 그것을 주입받아 쓴다.
/// 상속으로 물려주는 것은 연결이 아니라 <b>규약</b>이다 — Redis 계층의
/// <see cref="MemoryDb.MemoryDbBase"/>가 연결을 주입받고 실패 정책만 물려주는 것과 같은 구조다.</para>
/// <para>실익은 줄 수가 아니라 <b>롤백을 빼먹을 수 없어지는 것</b>이다. 조기 반환 지점마다 사람이
/// <c>RollbackAsync()</c>를 기억해야 했고, 그 지점이 계층 전체에 120곳이었다.</para>
/// </summary>
public abstract class GameDbBase
{
    private readonly GameDbFactory _dbFactory;

    /// <summary>세이브 DB 커넥션 팩토리를 주입받는다.</summary>
    protected GameDbBase(GameDbFactory dbFactory) => _dbFactory = dbFactory;

    /// <summary>
    /// 트랜잭션 없는 단발 조회용 QueryFactory. 호출측이 <c>using</c>으로 감싸면 커넥션도 함께 닫힌다.
    /// </summary>
    protected QueryFactory Db() => _dbFactory.Create();

    /// <summary>
    /// 커넥션과 트랜잭션을 열고 <paramref name="body"/>가 돌려준 <see cref="TxResult{T}"/>에 따라 커밋 또는 롤백한다.
    /// 예외는 롤백 후 그대로 전파한다(서비스 계층이 서버 결함으로 처리).
    /// <para><paramref name="body"/>에 <c>QueryFactory</c>와 <c>MySqlTransaction</c>을 함께 넘기는 이유는
    /// <c>MailRepository.InsertMailAsync(db, transaction, ...)</c>처럼 같은 트랜잭션을 공유하는
    /// 리포지토리 간 static 헬퍼가 그대로 동작해야 하기 때문이다. 트랜잭션을 구체 타입으로 넘기므로
    /// <c>SELECT ... FOR UPDATE</c> 행 잠금이 필요한 경로는 <c>transaction.Connection</c>으로 커넥션을 얻는다.</para>
    /// </summary>
    protected async Task<T> TransactionAsync<T>(Func<QueryFactory, MySqlTransaction, Task<TxResult<T>>> body)
    {
        await using var connection = _dbFactory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var result = await body(_dbFactory.Create(connection), transaction);

            if (result.ShouldCommit)
            {
                await transaction.CommitAsync();
            }
            else
            {
                await transaction.RollbackAsync();
            }

            return result.Value;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// 돌려줄 값이 없는 트랜잭션(계정 생성처럼 성공 아니면 예외인 경로). 정상 완료면 커밋, 예외면 롤백 후 전파한다.
    /// <para>반환값이 없으면 검증 실패를 값으로 돌려줄 경로 자체가 없으므로(실패가 곧 예외다)
    /// 커밋 여부를 따로 말할 필요가 없다.</para>
    /// </summary>
    protected async Task TransactionAsync(Func<QueryFactory, MySqlTransaction, Task> body)
    {
        await using var connection = _dbFactory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            await body(_dbFactory.Create(connection), transaction);
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
