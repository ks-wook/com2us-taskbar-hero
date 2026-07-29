using MySqlConnector;
using SqlKata.Execution;

namespace GameServer.Repositories;

/// <summary>
/// 인벤토리 변경 카운터(<c>game_player.inventory_revision</c>) 갱신 헬퍼.
/// 가방 아이템은 페이지로 나눠 조회하므로(<c>/api/game/inventory/list</c>), 페이지 사이에 인벤토리가 바뀌면
/// 같은 아이템을 두 번 받거나 놓친 찢어진 스냅샷이 만들어진다. 이를 감지하려고 인벤토리를 바꾸는
/// 트랜잭션이 커밋 전에 이 카운터를 올린다(세이브 데이터 기획서 4장·5.2).
/// </summary>
public static class InventoryRevision
{
    /// <summary>
    /// 계정의 인벤토리 변경 카운터를 1 올린다. <c>player_item</c>·<c>player_item_equipped</c>를 변경한
    /// 트랜잭션이 **커밋 전에 같은 트랜잭션으로** 호출해야 한다(별도 트랜잭션으로 미루면 페이지 조회가
    /// 변경을 놓친다). 계정 세이브 행이 없으면 갱신 행이 0이며, 그 경우 인벤토리도 없으므로 무시해도 된다.
    /// </summary>
    public static Task<int> BumpAsync(QueryFactory db, MySqlTransaction transaction, long userId)
        => db.Query("game_player").Where("user_id", userId).IncrementAsync("inventory_revision", 1, transaction);
}
