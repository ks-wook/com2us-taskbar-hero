using GameServer.Services;

namespace GameServer.Services.Interfaces;

public interface IStageService
{
    Task<SaveResult> EnterAsync(long userId, int act, int difficulty, int stage);
    Task<SaveResult> ClearAsync(long userId, int act, int difficulty, int stage);
    Task<SaveResult> FailAsync(
        long userId, int act, int difficulty, int stage,
        int elapsedMs, int remainingMonsterCount, bool reachedBoss);
}
