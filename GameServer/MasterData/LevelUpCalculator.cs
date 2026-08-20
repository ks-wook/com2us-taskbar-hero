using GameServer.Repositories.MasterDb;
using GameServer.Models;
namespace GameServer.MasterData;

/// <summary>경험치 지급 후 캐릭터 상태. NewExp는 "현재 레벨 내 누적치"다(레벨업 시 요구치를 차감한 잔여).</summary>
public readonly record struct LevelUpResult(int NewLevel, long NewExp, bool LeveledUp);

public interface ILevelUpCalculator
{
    /// <summary>
    /// 지급 경험치를 반영하고 레벨을 재계산한다. 요구치(<c>level_master</c>)를 넘으면 차감하며 올리고
    /// 최대 레벨에서 멈춘다(초과분은 그 레벨 내 누적치로 이월).
    /// </summary>
    LevelUpResult Calculate(int level, long exp, long rewardExp);
}

/// <summary>
/// 레벨업 계산(<c>level_master</c> 파생). 스테이지 클리어·오프라인 정산이 <b>같은 계산</b>을 써야 하므로
/// 이 클래스가 유일한 정본이다 — 예전에는 두 서비스가 각자 <c>ApplyExp</c>를 들고 있어 규칙이 두 벌이었다.
/// <para>리포지토리에 생성자 주입되어 <b>트랜잭션 안에서 호출</b>된다(잠금 걸린 캐릭터 행을 읽은 직후).
/// 그래서 델리게이트를 인자로 넘기지 않아도 트랜잭션 경계가 유지된다.</para>
/// </summary>
public sealed class LevelUpCalculator : ILevelUpCalculator
{
    private readonly MasterDbProvider _masterData;

    /// <summary>레벨 곡선을 읽을 마스터 데이터를 주입받는다.</summary>
    public LevelUpCalculator(MasterDbProvider masterData) => _masterData = masterData;

    /// <summary>
    /// 경험치를 더하고 요구치를 넘는 동안 레벨을 올린다. 요구치가 0 이하면(정의 없음) 그 자리에서 멈춘다 —
    /// 마스터가 비어 있을 때 무한 루프에 빠지지 않게 하는 방어다.
    /// </summary>
    public LevelUpResult Calculate(int level, long exp, long rewardExp)
    {
        var newLevel = level;
        var newExp = exp + rewardExp;

        while (newLevel < _masterData.MaxLevel)
        {
            var required = _masterData.LevelRequiredExp(newLevel);
            if (required <= 0 || newExp < required)
            {
                break;
            }

            newExp -= required;
            newLevel++;
        }

        return new LevelUpResult(newLevel, newExp, newLevel > level);
    }
}
