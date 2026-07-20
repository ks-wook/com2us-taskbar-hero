using MySqlConnector;
using SqlKata.Compilers;
using SqlKata.Execution;

namespace GameServer.Data;

/// <summary>
/// 세이브 DB(taskbar_hero_game) 접근용 SqlKata QueryFactory 생성기.
/// 원시 SQL을 조립하지 않고 SqlKata 쿼리 빌더로만 질의한다(프로젝트 규칙).
/// 트랜잭션이 필요한 경우 CreateConnection()으로 커넥션을 직접 열어 Create(conn)에 넘긴다.
/// </summary>
public sealed class GameDbFactory
{
    private readonly string _connectionString;
    private readonly Compiler _compiler = new MySqlCompiler();

    public GameDbFactory(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("GameDb")
            ?? throw new InvalidOperationException("ConnectionStrings:GameDb 설정이 없습니다.");
    }

    /// <summary>새 커넥션을 물린 QueryFactory. using으로 감싸면 커넥션도 함께 닫힌다.</summary>
    public QueryFactory Create() => new(new MySqlConnection(_connectionString), _compiler);

    /// <summary>트랜잭션 제어용 커넥션. 호출측이 열고 닫는다.</summary>
    public MySqlConnection CreateConnection() => new(_connectionString);

    /// <summary>이미 연 커넥션 위에 QueryFactory를 만든다(트랜잭션 공유용).</summary>
    public QueryFactory Create(MySqlConnection connection) => new(connection, _compiler);
}
