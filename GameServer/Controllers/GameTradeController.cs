using GameServer.Services;
using Microsoft.AspNetCore.Mvc;
using TaskbarHero.Common.Dto;

namespace GameServer.Controllers;

/// <summary>거래소/교역선 API(trade 기획서 §5). 인증 필요.</summary>
[ApiController]
[Route("api/game/trade")]
public sealed class GameTradeController(ITradeService tradeService) : GameApiControllerBase
{
    /// <summary>판매중 등록 목록 조회(가격 오름차순). data.mine=false면 본인 등록 제외(구매 대상),
    /// true면 본인 등록만(취소 대상). POST /api/game/trade/list</summary>
    [HttpPost("list")]
    public async Task<IActionResult> List([FromBody] TradeListRequest request)
    {
        var data = request?.data ?? new TradeListData();
        var result = await tradeService.ListAsync(
            AuthenticatedUserId(), data.itemCode, data.mine, data.page, data.pageSize);
        return ApiResult(result.ErrorCode, result.SuccessMessage, result.Data);
    }

    /// <summary>인벤토리 아이템을 거래소에 판매 등록(에스크로). POST /api/game/trade/register</summary>
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] TradeRegisterRequest request)
    {
        var data = request?.data ?? new TradeRegisterData();
        var result = await tradeService.RegisterAsync(AuthenticatedUserId(), data.itemId, data.price);
        return ApiResult(result.ErrorCode, result.SuccessMessage, result.Data);
    }

    /// <summary>등록 구매(골드 차감 → 구매 아이템은 구매자 우편함, 판매 대금은 판매자 우편함).
    /// POST /api/game/trade/buy</summary>
    [HttpPost("buy")]
    public async Task<IActionResult> Buy([FromBody] TradeBuyRequest request)
    {
        var data = request?.data ?? new TradeListingData();
        var result = await tradeService.BuyAsync(AuthenticatedUserId(), data.listingId);
        return ApiResult(result.ErrorCode, result.SuccessMessage, result.Data);
    }

    /// <summary>본인 등록 판매 취소(아이템 인벤토리 복귀). POST /api/game/trade/cancel</summary>
    [HttpPost("cancel")]
    public async Task<IActionResult> Cancel([FromBody] TradeCancelRequest request)
    {
        var data = request?.data ?? new TradeListingData();
        var result = await tradeService.CancelAsync(AuthenticatedUserId(), data.listingId);
        return ApiResult(result.ErrorCode, result.SuccessMessage, result.Data);
    }
}
