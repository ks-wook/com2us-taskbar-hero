using MySqlConnector;
using SqlKata.Compilers;
using SqlKata.Execution;

namespace GameServer.Data;

/// <summary>
/// 마스터 데이터 DB(taskbar_hero_master) 접근용 QueryFactory 생성기.
/// 마스터 원천은 master-data-schema.sql(= 이 DB)이며, 서버 기동 시 인메모리로 적재한다(마스터 데이터 기획서 8장).
/// </summary>
public sealed class MasterDbFactory
{
    private readonly string _connectionString;
    private readonly Compiler _compiler = new MySqlCompiler();

    public MasterDbFactory(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("MasterDb")
            ?? throw new InvalidOperationException("ConnectionStrings:MasterDb 설정이 없습니다.");
    }

    public QueryFactory Create() => new(new MySqlConnection(_connectionString), _compiler);
}
