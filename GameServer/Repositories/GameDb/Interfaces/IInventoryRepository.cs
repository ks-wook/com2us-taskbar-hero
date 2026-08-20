using GameServer.Repositories.GameDb;

namespace GameServer.Repositories.GameDb.Interfaces;

public interface IInventoryRepository
{
    /// <summary>
    /// 그 계정의 가방 아이템을 <b>slot 커서 keyset 페이징</b>으로 조회한다(<c>slot &gt; cursor</c>, slot 오름차순,
    /// 최대 limit개) — 다음 페이지 존재 판정을 위해 한 건 더 읽어 돌려주므로 호출측이 잘라 쓴다.
    /// 총 점유 칸 수(<c>Total</c>)를 함께 반환한다.
    /// </summary>
    Task<InventoryBagOutcome> GetPageAsync(long userId, int cursor, int limit);

    /// <summary>
    /// 장착을 한 트랜잭션으로 적용한다: 캐릭터·아이템 존재/미장착 확인 → validate(마스터 검증)로 장착 가능 여부·대상 슬롯 판정
    /// → 같은 슬롯 기존 장비 해제(스왑) → 장착 행 INSERT. validate는 (itemCode, classCode, level)→(ok, slot).
    /// </summary>
    Task<EquipOutcome> ApplyEquipAsync(
        long userId, int characterId, long itemId,
        Func<int, int, int, (bool ok, int slot)> validate);

    /// <summary>지정 캐릭터-장착 슬롯의 장비를 해제(장착 행 DELETE)한다.</summary>
    Task<UnequipOutcome> ApplyUnequipAsync(long userId, int characterId, int slot);

    /// <summary>아이템을 목표 칸으로 이동한다. 목표 칸이 차 있으면 두 칸을 교환(swap)하며, 한 트랜잭션으로 처리한다.</summary>
    Task<MoveOutcome> ApplyMoveAsync(long userId, long itemId, int toSlot);

    /// <summary>
    /// 장비 강화를 한 트랜잭션으로 적용한다: 아이템 소유 확인 → plan(마스터 검증)으로 강화 가능 여부·비용 판정
    /// → 비용 재화 확인·차감 → enhance_level += 1(장착 중이면 장착 행의 강화 단계도 함께 갱신).
    /// plan은 (itemCode, 현재 강화 단계)→(status, cost, currencyCode).
    /// </summary>
    Task<EnhanceOutcome> ApplyEnhanceAsync(
        long userId, long itemId,
        Func<int, int, (EnhancePlanStatus status, long cost, int currencyCode)> plan);

    /// <summary>
    /// 인벤토리 용량을 1칸 확장한다: 현재 용량으로 planOne(비용·가능 여부)을 산출 → 골드 확인·차감 → inventory_capacity += 1.
    /// planOne은 (currentCapacity)→(ok, cost). 한 트랜잭션으로 처리하며 실패 시 전체 롤백한다.
    /// </summary>
    Task<ExpandOutcome> ApplyExpandAsync(long userId, Func<int, (bool ok, long cost)> planOne, long nowUnix);
}
