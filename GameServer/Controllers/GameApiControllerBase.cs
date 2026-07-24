using GameServer.Auth;
using Microsoft.AspNetCore.Mvc;
using TaskbarHero.Common;
using TaskbarHero.Common.Dto;

namespace GameServer.Controllers;

/// <summary>
/// 게임 컨트롤러 공통 베이스. HTTP 액션 메서드 외의 보조 로직(인증 userId 추출, 공통 응답 변환,
/// ErrorCode → HTTP 상태/메시지 매핑)을 여기에 두고, 각 컨트롤러가 이를 상속해 사용한다
/// (프로젝트 규칙: 컨트롤러는 HTTP 액션만 포함).
/// </summary>
public abstract class GameApiControllerBase : ControllerBase
{
    /// <summary>인증 미들웨어가 검증·주입한 userId(HttpContext.Items).</summary>
    protected long AuthenticatedUserId()
        => (long)HttpContext.Items[GameAuthMiddleware.UserIdItemKey]!;

    /// <summary>ErrorCode·성공 메시지·데이터를 공통 응답 형식 { success, errorCode, message, data } + HTTP 상태로 변환한다.</summary>
    protected IActionResult ApiResult(ErrorCode errorCode, string successMessage, object? data = null)
    {
        var success = errorCode == ErrorCode.Success;
        var response = new ApiResponse
        {
            success = success,
            errorCode = (int)errorCode,
            message = success ? successMessage : ErrorMessage(errorCode),
            data = data,
        };

        return StatusCode(HttpStatus(errorCode), response);
    }

    private static int HttpStatus(ErrorCode code) => code switch
    {
        ErrorCode.Success => StatusCodes.Status200OK,
        // 오프라인 정산: 경과 부족은 오류가 아닌 정상 응답(200, success=false), 동시 중복은 409(기획서 5.1).
        ErrorCode.NoOfflineReward => StatusCodes.Status200OK,
        ErrorCode.OfflineRewardAlreadyClaimed => StatusCodes.Status409Conflict,
        ErrorCode.PlayerAlreadyExists => StatusCodes.Status409Conflict,
        ErrorCode.MasterDataNotLoaded => StatusCodes.Status503ServiceUnavailable,
        ErrorCode.SaveNotFound => StatusCodes.Status404NotFound,
        ErrorCode.StageNotFound => StatusCodes.Status404NotFound,
        ErrorCode.ItemNotFound => StatusCodes.Status404NotFound,
        // 인증 실패 계열(미들웨어가 대부분 선처리하나 방어적으로 매핑).
        ErrorCode.InvalidToken => StatusCodes.Status401Unauthorized,
        ErrorCode.ExpiredToken => StatusCodes.Status401Unauthorized,
        // 나머지 검증 실패는 400.
        _ => StatusCodes.Status400BadRequest,
    };

    private static string ErrorMessage(ErrorCode code) => code switch
    {
        ErrorCode.InvalidRequest => "Invalid request",
        ErrorCode.InvalidSaveData => "Invalid save data",
        ErrorCode.PlayerAlreadyExists => "Player already exists",
        ErrorCode.InvalidClassCode => "Invalid class code",
        ErrorCode.InvalidCharacterId => "Invalid character id",
        ErrorCode.SaveNotFound => "Save not found",
        ErrorCode.NoOfflineReward => "No offline reward",
        ErrorCode.OfflineRewardAlreadyClaimed => "Offline reward already claimed",
        ErrorCode.StageNotFound => "Stage not found",
        ErrorCode.StageLocked => "Stage locked",
        ErrorCode.StageNotEntered => "Stage not entered",
        ErrorCode.StageClearTooFast => "Stage cleared too fast",
        ErrorCode.InventoryFull => "Inventory full",
        ErrorCode.ItemNotFound => "Item not found",
        ErrorCode.ItemNotEquippable => "Item not equippable",
        ErrorCode.ItemEquipped => "Item already equipped",
        ErrorCode.InvalidInventorySlot => "Invalid inventory slot",
        ErrorCode.InsufficientCurrency => "Insufficient currency",
        ErrorCode.InventoryCapacityMax => "Inventory capacity max",
        // 성장(스킬·룬)
        ErrorCode.InvalidGrowthTarget => "Invalid growth target",
        ErrorCode.SkillMaxLevel => "Skill max level",
        ErrorCode.InsufficientSkillPoint => "Insufficient skill point",
        ErrorCode.SkillClassMismatch => "Skill class mismatch",
        ErrorCode.SkillNotActive => "Skill not active",
        ErrorCode.SkillNotLearned => "Skill not learned",
        ErrorCode.ActiveSkillLimitExceeded => "Active skill limit exceeded",
        ErrorCode.RunePrereqNotMet => "Rune prerequisite not met",
        ErrorCode.RuneMaxLevel => "Rune max level",
        ErrorCode.MasterDataNotLoaded => "Master data not loaded",
        ErrorCode.InvalidToken => "Invalid token",
        ErrorCode.ExpiredToken => "Expired token",
        _ => code.ToString(),
    };
}
