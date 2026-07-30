namespace GameServer.MasterData;

/// <summary>
/// 스테이지 좌표 인코딩과 진행 순서(선형 시퀀스) 헬퍼.
///
/// - stage_id = act*1000000 + difficulty*10000 + stage  (마스터 데이터 기획서 5.9)
/// - 진행 시퀀스(max_stage_cleared에 저장): 난이도-바깥 순서.
///   Normal(난이도1)에서 Act1~5(각 10스테이지)을 모두 깨면 Hard(난이도2)가 열린다.
///   sequence = (difficulty-1)*50 + (act-1)*10 + stage  → 1..100
///   Act/난이도 전진 순서(난이도-바깥)는 stage-battle 기획서 8장에서 확정된 채택안이다.
///   최종 스테이지(시퀀스 100) 클리어 후에는 전진처가 없어 그 스테이지에 머문다(기획서 8장 확정).
/// </summary>
public static class StageCoords
{
    public const int Acts = 5;
    public const int Difficulties = 2;
    public const int StagesPerAct = 10;          // 한 지역(Act)당 10 스테이지
    public const int BossStage = 10;              // 각 Act·난이도의 10스테이지가 보스
    public const int TotalStages = Acts * Difficulties * StagesPerAct; // 100

    public static int StageId(int act, int difficulty, int stage)
        => act * 1000000 + difficulty * 10000 + stage;

    public static bool IsValidCoord(int act, int difficulty, int stage)
        => act >= 1 && act <= Acts
        && difficulty >= 1 && difficulty <= Difficulties
        && stage >= 1 && stage <= StagesPerAct;

    /// <summary>진행 시퀀스(1..100). 좌표가 유효하지 않으면 0.</summary>
    public static int Sequence(int act, int difficulty, int stage)
        => IsValidCoord(act, difficulty, stage)
            ? (difficulty - 1) * (Acts * StagesPerAct) + (act - 1) * StagesPerAct + stage
            : 0;

    /// <summary>시퀀스(1..100)를 좌표로 디코딩. 범위 밖이면 false.</summary>
    public static bool TryDecodeSequence(int sequence, out int act, out int difficulty, out int stage)
    {
        act = difficulty = stage = 0;
        if (sequence < 1 || sequence > TotalStages)
        {
            return false;
        }

        var zero = sequence - 1;
        difficulty = zero / (Acts * StagesPerAct) + 1;
        var rem = zero % (Acts * StagesPerAct);
        act = rem / StagesPerAct + 1;
        stage = rem % StagesPerAct + 1;
        return true;
    }
}
