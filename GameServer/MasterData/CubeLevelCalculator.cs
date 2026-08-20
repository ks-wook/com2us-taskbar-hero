using GameServer.Repositories.MasterDb;
using GameServer.Models;
namespace GameServer.MasterData;

/// <summary>큐브 경험치 적립 후 상태. NewExp는 "현재 큐브 레벨 내 누적치"다.</summary>
public readonly record struct CubeLevelResult(int NewLevel, long NewExp);

public interface ICubeLevelCalculator
{
    /// <summary>
    /// 큐브 경험치를 적립하고 레벨을 재계산한다. 요구치(<c>cube_master.required_exp</c>)를 넘으면
    /// 차감하며 올리고, 요구치가 0 이하인 레벨(정의 없음 = 최대 레벨)에서 멈춘다.
    /// </summary>
    CubeLevelResult Calculate(int level, long exp, long gain);
}

/// <summary>
/// 큐브 레벨 계산(<c>cube_master</c> 파생). 합성·분해·제작이 <b>같은 계산</b>을 쓰므로 이 클래스가 정본이다 —
/// 예전에는 <c>CubeService.AdvanceCube</c>를 세 연산에 매번 델리게이트로 넘겼다.
/// <para>리포지토리에 생성자 주입되어 <b>트랜잭션 안에서</b> 호출된다(잠금 걸린 큐브 행을 읽은 직후).</para>
/// </summary>
public sealed class CubeLevelCalculator : ICubeLevelCalculator
{
    private readonly MasterDbProvider _masterData;

    /// <summary>큐브 레벨 곡선을 읽을 마스터 데이터를 주입받는다.</summary>
    public CubeLevelCalculator(MasterDbProvider masterData) => _masterData = masterData;

    /// <summary>
    /// 경험치를 더하고 요구치를 넘는 동안 레벨을 올린다. 요구치 0 이하는 "다음 레벨 없음"을 뜻하므로
    /// 그 자리에서 멈춘다 — 최대 레벨 처리와 마스터 공백 방어를 겸한다.
    /// </summary>
    public CubeLevelResult Calculate(int level, long exp, long gain)
    {
        var newLevel = level;
        var newExp = exp + gain;

        while (true)
        {
            var required = _masterData.CubeRequiredExp(newLevel);
            if (required <= 0 || newExp < required)
            {
                break;
            }

            newExp -= required;
            newLevel++;
        }

        return new CubeLevelResult(newLevel, newExp);
    }
}
