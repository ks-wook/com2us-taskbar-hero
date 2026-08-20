namespace GameServer.Models;

// item_master(아이템 마스터) 정의. 마스터 적재 결과를 담는 불변 모델이며
// 조회는 Repositories/MasterDb/MasterDbProvider 가 제공한다.
// ── 마스터 데이터 정의(불변). Repositories/MasterDb 가 적재하고 MasterDbProvider 가 조회한다. ──
/// <summary>아이템 정의(item_master). 장착 검증(타입·슬롯·클래스·레벨)·드롭/스택·거래 검증에 사용한다.
/// Name은 서버가 만드는 문구(거래 메일 제목·본문 등)에 아이템을 사람이 읽는 이름으로 표기하기 위해 적재한다.</summary>
public sealed record ItemDef(
    int ItemCode, string Name, int ItemType, int Grade, int StackMax, int EquipSlot, int ClassReq, int LevelReq,
    int Sellable, long BasePrice);

// ── 마스터 적재용 DB 행 매핑 POCO(제네릭 매핑 전용, dynamic 금지). snake_case→PascalCase는 Dapper 규칙으로 매핑.
//    DECIMAL 컬럼은 decimal로 받아 float/double로 캐스팅한다. 적재는 Repositories/MasterDb/MasterDbLoader. ──
class ItemMasterRow
{
    public int ItemCode { get; set; }
    public string Name { get; set; } = string.Empty;
    public int ItemType { get; set; }
    public int Grade { get; set; }
    public int StackMax { get; set; }
    public int EquipSlot { get; set; }
    public int ClassReq { get; set; }
    public int LevelReq { get; set; }
    public int Sellable { get; set; }
    public long BasePrice { get; set; }
}
