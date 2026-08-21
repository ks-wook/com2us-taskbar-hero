using SqlKata.Execution;

namespace AccountServer.Repositories.AccountDb;

/// <summary>
/// MySQL 계정 DB(AccountDb) 계층의 공통 기반 — <b>커넥션 개시 규약</b>을 한곳에 모은다.
/// <para>연결 문자열은 <see cref="AccountDbFactory"/>가 계속 보유하고(싱글턴 1개) 이 클래스는 그것을 주입받아 쓴다.
/// 상속으로 물려주는 것은 연결이 아니라 <b>규약</b>이다 — 파생 리포지토리는 팩토리를 필드로 들고 있지 않고
/// <see cref="Db"/>만 호출하므로, 커넥션을 어떻게 얻는지가 계층 전체에서 한 줄로 통일된다.</para>
/// <para><b>트랜잭션 헬퍼가 없는 이유</b>: 계정 계층의 쓰기는 모두 단일 문장(users INSERT,
/// user_auth_token UPDATE/INSERT/DELETE)이라 감쌀 트랜잭션 본문이 없다. GameServer의
/// <c>GameDbBase.TransactionAsync</c>/<c>TxResult</c>는 "검증 실패 후 정상 반환에서 롤백"하는 경로가
/// 120곳 있어서 존재하는 장치이므로, 여기서는 쓰이지 않을 장치를 미리 두지 않는다.
/// 다중 문장 경로가 생기면 그때 같은 형태로 추가한다.</para>
/// </summary>
public abstract class AccountDbBase
{
    private readonly AccountDbFactory _dbFactory;

    /// <summary>계정 DB 커넥션 팩토리를 주입받는다.</summary>
    protected AccountDbBase(AccountDbFactory dbFactory) => _dbFactory = dbFactory;

    /// <summary>
    /// 단발 질의용 QueryFactory. 호출측이 <c>using</c>으로 감싸면 커넥션도 함께 닫힌다.
    /// </summary>
    protected QueryFactory Db() => _dbFactory.Create();
}
