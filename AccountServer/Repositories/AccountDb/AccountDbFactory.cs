using MySqlConnector;
using SqlKata.Compilers;
using SqlKata.Execution;

namespace AccountServer.Repositories.AccountDb;

/// <summary>
/// 계정 DB(taskbar_hero_account) 접근용 SqlKata <see cref="QueryFactory"/> 생성기.
/// 연결 문자열은 docker-compose.yml 의 MySQL 설정과 맞춘 ConnectionStrings:AccountDb 를 사용한다.
/// 원시 SQL을 조립하지 않고 SqlKata 쿼리 빌더로만 질의한다(프로젝트 규칙).
/// <para>리포지토리는 이 팩토리를 직접 쓰지 않고 <see cref="AccountDbBase"/>를 상속해
/// 커넥션 개시 규약을 물려받는다(GameServer의 GameDbFactory·GameDbBase와 같은 구조).</para>
/// </summary>
public sealed class AccountDbFactory
{
    private readonly string _connectionString;
    private readonly Compiler _compiler = new MySqlCompiler();

    public AccountDbFactory(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("AccountDb")
            ?? throw new InvalidOperationException("ConnectionStrings:AccountDb 설정이 없습니다.");
    }

    /// <summary>
    /// 호출마다 새 MySQL 커넥션을 물린 QueryFactory를 만든다.
    /// QueryFactory.Dispose()가 내부 커넥션을 함께 닫으므로 호출측에서 using으로 감싼다.
    /// </summary>
    public QueryFactory Create() => new(new MySqlConnection(_connectionString), _compiler);
}
