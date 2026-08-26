namespace GameServer.Repositories.MasterDb.Interfaces;

/// <summary>
/// 마스터 DB(정적 데이터) 적재기. 기동 시 1회 호출되어 필요한 테이블 전부를 읽고 스냅샷으로 돌려준다.
/// <para>조회는 <c>MasterDbProvider</c>가 담당한다 — 이 인터페이스를 사이에 두어 <b>조회 계층이 DB를 모르게</b>
/// 한다(적재원을 MySQL에서 JSON 번들로 바꿔도 조회부는 그대로다).</para>
/// <para>실패는 흡수하지 않고 예외로 올린다 — 마스터가 없으면 게임이 성립하지 않으므로,
/// 축소 운전 판단(<c>IsLoaded</c>)은 호출측인 Provider가 한다.</para>
/// </summary>
public interface IMasterDbLoader
{
    /// <summary>커넥션 하나로 마스터 테이블 전부를 읽어 스냅샷을 만든다.</summary>
    Task<MasterDbSnapshot> LoadAllAsync();
}
