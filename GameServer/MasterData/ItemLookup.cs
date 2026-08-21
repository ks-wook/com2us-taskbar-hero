using GameServer.Repositories.MasterDb;
using GameServer.Models;
namespace GameServer.MasterData;

/// <summary>
/// 가방 적재 규칙에 필요한 아이템 최소 속성. StackMax는 <b>항상 1 이상</b>으로 보정된 값이다 —
/// 0이 흘러들면 스택 병합이 무한 루프나 0칸 적재로 이어진다.
/// </summary>
public readonly record struct ItemStacking(int ItemType, int StackMax);

/// <summary>거래 검증에 필요한 아이템 속성(마스터 파생). Sellable 0이면 판매 불가, BasePrice 0이면 거래 불가.</summary>
public sealed record ItemAttributes(int ItemType, int StackMax, int Sellable, long BasePrice);

public interface IItemLookup
{
    /// <summary>적재 규칙용 속성. 마스터에 없는 코드는 장비처럼(타입 1·스택 1) 취급한다.</summary>
    ItemStacking Stacking(int itemCode);

    /// <summary>거래 검증용 전체 속성. 마스터에 없는 코드는 null.</summary>
    ItemAttributes? Attributes(int itemCode);
}

/// <summary>
/// 아이템 속성 조회(<c>item_master</c> 파생). 메일 수령·가챠 지급·거래 등록/취소/만료가 모두 이 한 곳을 쓴다 —
/// 예전에는 세 서비스가 각자 조회 헬퍼를 들고 있어 <b>StackMax 보정이 서로 달랐다</b>(가챠만
/// <c>Math.Max(stackMax, 1)</c> 가드가 있었다).
/// <para>리포지토리에 생성자 주입되어 트랜잭션 안에서 호출된다.</para>
/// </summary>
public sealed class ItemLookup : IItemLookup
{
    private readonly MasterDbProvider _masterData;

    /// <summary>아이템 정의를 읽을 마스터 데이터를 주입받는다.</summary>
    public ItemLookup(MasterDbProvider masterData) => _masterData = masterData;

    /// <summary>적재 규칙용 속성을 돌려준다. StackMax는 1 미만이면 1로 올린다(0칸 적재 방지).</summary>
    public ItemStacking Stacking(int itemCode)
    {
        var def = _masterData.GetItem(itemCode);
        return def is null
            ? new ItemStacking(Constants.MasterFallback.UnknownItemType, Constants.MasterFallback.UnknownStackMax)
            : new ItemStacking(def.ItemType, Math.Max(def.StackMax, 1));
    }

    /// <summary>거래 검증용 전체 속성을 돌려준다. 정의가 없으면 null(호출측이 거래 불가로 처리).</summary>
    public ItemAttributes? Attributes(int itemCode)
    {
        var def = _masterData.GetItem(itemCode);
        return def is null
            ? null
            : new ItemAttributes(def.ItemType, Math.Max(def.StackMax, 1), def.Sellable, def.BasePrice);
    }
}
