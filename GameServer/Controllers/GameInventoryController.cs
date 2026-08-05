using GameServer.Services;
using Microsoft.AspNetCore.Mvc;
using TaskbarHero.Common.Dto;

namespace GameServer.Controllers;

/// <summary>
/// 인벤토리 API(inventory-item-cube 기획서 §5.1·5.2·5.5). 인증 필요.
/// 가방 아이템 페이지 조회(세이브 데이터 기획서 §5.2)와 장착·해제·강화·배치 이동·용량 확장을 제공하며,
/// 큐브·상자는 범위 밖이다.
/// </summary>
[ApiController]
[Route("api/game/inventory")]
public sealed class GameInventoryController(IInventoryService inventoryService) : GameApiControllerBase
{
    /// <summary>가방 아이템 페이지 조회(slot 커서 페이징). POST /api/game/inventory/list</summary>
    [HttpPost("list")]
    public async Task<IActionResult> List([FromBody] InventoryListRequest request)
    {
        var data = request?.data ?? new InventoryListData();
        var result = await inventoryService.GetPageAsync(AuthenticatedUserId(), data.cursor, data.limit);
        return ApiResult(result.ErrorCode, result.SuccessMessage, result.Data);
    }

    /// <summary>장비 장착. POST /api/game/inventory/equip</summary>
    [HttpPost("equip")]
    public async Task<IActionResult> Equip([FromBody] EquipRequest request)
    {
        var data = request?.data ?? new EquipData();
        var result = await inventoryService.EquipAsync(AuthenticatedUserId(), data.characterId, data.itemId);
        return ApiResult(result.ErrorCode, result.SuccessMessage, result.Data);
    }

    /// <summary>장비 장착 해제. POST /api/game/inventory/unequip</summary>
    [HttpPost("unequip")]
    public async Task<IActionResult> Unequip([FromBody] UnequipRequest request)
    {
        var data = request?.data ?? new UnequipData();
        var result = await inventoryService.UnequipAsync(AuthenticatedUserId(), data.characterId, data.slot);
        return ApiResult(result.ErrorCode, result.SuccessMessage, result.Data);
    }

    /// <summary>인벤토리 배치 이동/교환. POST /api/game/inventory/move</summary>
    [HttpPost("move")]
    public async Task<IActionResult> Move([FromBody] MoveRequest request)
    {
        var data = request?.data ?? new MoveData();
        var result = await inventoryService.MoveAsync(AuthenticatedUserId(), data.itemId, data.toSlot);
        return ApiResult(result.ErrorCode, result.SuccessMessage, result.Data);
    }

    /// <summary>장비 강화 단계 +1(재화 소모). POST /api/game/inventory/enhance</summary>
    [HttpPost("enhance")]
    public async Task<IActionResult> Enhance([FromBody] EnhanceRequest request)
    {
        var data = request?.data ?? new EnhanceData();
        var result = await inventoryService.EnhanceAsync(AuthenticatedUserId(), data.itemId);
        return ApiResult(result.ErrorCode, result.SuccessMessage, result.Data);
    }

    /// <summary>인벤토리 용량 1칸 확장(골드 소모). POST /api/game/inventory/expand</summary>
    [HttpPost("expand")]
    public async Task<IActionResult> Expand([FromBody] AuthRequest request)
    {
        var result = await inventoryService.ExpandAsync(AuthenticatedUserId());
        return ApiResult(result.ErrorCode, result.SuccessMessage, result.Data);
    }
}
