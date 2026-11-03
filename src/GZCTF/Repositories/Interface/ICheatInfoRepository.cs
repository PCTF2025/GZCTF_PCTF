namespace GZCTF.Repositories.Interface;

public interface ICheatInfoRepository : IRepository
{
    /// <summary>
    /// Create a new cheat info record
    /// </summary>
    /// <param name="submission">The submission that triggered the cheat info</param>
    /// <param name="source">The instance where the flag was pointed</param>
    /// <param name="token"></param>
    /// <returns></returns>
    public Task<CheatInfo> CreateCheatInfo(Submission submission, GameInstance source,
        CancellationToken token = default);

    /// <summary>
    /// 检测短时间大量提交（疑似自动化工具），命中则记录异常行为
    /// </summary>
    /// <param name="submission">触发检测的提交</param>
    /// <param name="windowSeconds">统计窗口（秒）</param>
    /// <param name="threshold">窗口内提交次数阈值</param>
    /// <param name="token"></param>
    /// <returns>命中时返回记录，否则返回 null</returns>
    public Task<CheatInfo?> CreateMassSubmissionAlert(Submission submission, int windowSeconds,
        int threshold, CancellationToken token = default);

    /// <summary>
    /// Get all cheat info records for a specific game by its ID
    /// </summary>
    /// <param name="gameId">The game ID</param>
    /// <param name="token"></param>
    /// <returns></returns>
    public Task<CheatInfo[]> GetCheatInfoByGameId(int gameId, CancellationToken token = default);
}
