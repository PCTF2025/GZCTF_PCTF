using GZCTF.Models.Internal;

namespace GZCTF.Repositories.Interface;

public interface IGameInstanceRepository : IRepository
{
    /// <summary>
    /// Get or create a game instance for a team and challenge
    /// </summary>
    /// <param name="team">Team</param>
    /// <param name="challengeId">Challenge id</param>
    /// <param name="token"></param>
    /// <returns></returns>
    public Task<GameInstance?> GetInstance(Participation team, int challengeId, CancellationToken token = default);

    /// <summary>
    /// Get a game instance for submission, with minimal includes
    /// </summary>
    /// <param name="team">Team</param>
    /// <param name="challengeId">Challenge id</param>
    /// <param name="token"></param>
    /// <returns></returns>
    public Task<GameInstance?> GetInstanceForSubmission(Participation team, int challengeId,
        CancellationToken token = default);

    /// <summary>
    /// Verify the answer of a submission
    /// </summary>
    /// <param name="submission">Submission</param>
    /// <param name="token"></param>
    /// <returns></returns>
    public Task<VerifyResult> VerifyAnswer(Submission submission, CancellationToken token = default);

    /// <summary>
    /// 取得（必要时生成并落库）某队伍在「外部动态 flag」题目上专属的 flag。
    ///
    /// 共用一台常开服务器的题目无法下发容器，但仍需要每队不同的 flag：
    /// 这里按 participation 确定性生成并写入 instance.FlagContext，
    /// 后续校验与外部插件兑换都读取同一个值。
    /// </summary>
    /// <returns>flag 内容；题目未开启外部动态 flag 或队伍不存在时返回 null</returns>
    public Task<string?> EnsureExternalFlag(int gameId, int challengeId, string teamToken,
        CancellationToken token = default);

    /// <summary>
    /// Check if the submission is a cheat
    /// </summary>
    /// <param name="submission">Submission</param>
    /// <param name="token"></param>
    /// <returns></returns>
    public Task<CheatCheckInfo> CheckCheat(Submission submission, CancellationToken token = default);

    /// <summary>
    /// Create a container for a game instance
    /// </summary>
    /// <param name="team">Team info</param>
    /// <param name="game">Game</param>
    /// <param name="user">User</param>
    /// <param name="gameInstance">Instance</param>
    /// <param name="token"></param>
    /// <returns></returns>
    public Task<TaskResult<Container>> CreateContainer(GameInstance gameInstance, Team team, UserInfo user,
        Game game, CancellationToken token = default);

    /// <summary>
    /// Destroy all containers of a challenge
    /// </summary>
    /// <param name="challenge">Challenge</param>
    /// <param name="token"></param>
    /// <returns></returns>
    public Task DestroyAllContainers(GameChallenge challenge, CancellationToken token = default);
}
