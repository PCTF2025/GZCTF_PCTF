namespace GZCTF.Models.Request.Game;

/// <summary>
/// Cheat behavior information
/// </summary>
public class CheatInfoModel
{
    /// <summary>
    /// Team owning the flag
    /// </summary>
    public ParticipationModel OwnedTeam { get; set; } = null!;

    /// <summary>
    /// Team submitting the flag
    /// </summary>
    public ParticipationModel SubmitTeam { get; set; } = null!;

    /// <summary>
    /// Submission corresponding to this cheating behavior
    /// </summary>
    public Submission Submission { get; set; } = null!;

    /// <summary>
    /// 异常行为类型（FlagShared = 共享 flag，MassSubmission = 短时间大量提交）
    /// </summary>
    public CheatBehavior Behavior { get; set; }

    internal static CheatInfoModel FromCheatInfo(CheatInfo info) =>
        new()
        {
            Submission = info.Submission,
            Behavior = info.Behavior,
            OwnedTeam = ParticipationModel.FromParticipation(info.SourceTeam),
            SubmitTeam = ParticipationModel.FromParticipation(info.SubmitTeam)
        };
}
