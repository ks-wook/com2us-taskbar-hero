namespace GameServer.Models;

// ConsumableRepository 전용 DB 행 매핑용 POCO(제네릭 매핑 전용, dynamic 금지).
// Dapper.MatchNamesWithUnderscores=true(Program.cs)로 snake_case 컬럼 → PascalCase 프로퍼티 매핑.

class ConsumablePlayerItemRow
{
    public long PlayerItemId { get; set; }
    public int ItemCode { get; set; }
    public int RowType { get; set; }
    public long Quantity { get; set; }
    public int? Slot { get; set; } // 가방 변경분(5.0) 조립용 배치 칸
}

class BuffRow
{
    public int BuffType { get; set; }
    public decimal BuffValue { get; set; } // DECIMAL(5,3) → decimal로 받아 float로 캐스팅
    public long StartedAt { get; set; }
    public long ExpiresAt { get; set; }
}

// ── 마스터 데이터 정의(불변). Repositories/MasterDb 가 적재하고 MasterDbProvider 가 조회한다. ──
/// <summary>
/// 소모품 버프 효과 정의(consumable_master). item_master의 소모품 행(item_type=4)과 1:1이다.
/// BuffValue는 획득량 배율(1.5 = 150%), DurationSec은 지속시간(초, 벽시계 경과 — 오프라인 중에도 소모).
/// </summary>
public sealed record ConsumableDef(int ItemCode, int BuffType, float BuffValue, int DurationSec);

// ── 마스터 적재용 DB 행 매핑 POCO(제네릭 매핑 전용, dynamic 금지). snake_case→PascalCase는 Dapper 규칙으로 매핑.
//    DECIMAL 컬럼은 decimal로 받아 float/double로 캐스팅한다. 적재는 Repositories/MasterDb/MasterDbLoader. ──
class ConsumableMasterRow
{
    public int ItemCode { get; set; }
    public int BuffType { get; set; }
    public decimal BuffValue { get; set; } // DECIMAL(5,3) → decimal로 받아 float로 캐스팅
    public int DurationSec { get; set; }
}
