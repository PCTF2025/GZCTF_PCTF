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

    /// <summary>
    /// 提交者真实姓名
    /// </summary>
    public string? RealName { get; set; }

    /// <summary>
    /// 提交者学号
    /// </summary>
    public string? StdNumber { get; set; }

    /// <summary>
    /// 提供 flag 一方的队员（真实姓名+学号），用于溯源
    ///
    /// 平台只记录 flag 所属队伍，不记录具体是谁启动的容器，
    /// 因此这里列出该队全部队员；单队伍单人时即为提供者本人。
    /// </summary>
    public string? SourceMembers { get; set; }

    internal static CheatInfoModel FromCheatInfo(CheatInfo info) =>
        new()
        {
            Submission = info.Submission,
            Behavior = info.Behavior,
            RealName = info.Submission.User?.RealName,
            StdNumber = info.Submission.User?.StdNumber,
            SourceMembers = FormatMembers(info.SourceTeam),
            OwnedTeam = ParticipationModel.FromParticipation(info.SourceTeam),
            SubmitTeam = ParticipationModel.FromParticipation(info.SubmitTeam)
        };

    /// <summary>
    /// 把队伍成员格式化为「姓名(学号)」列表
    /// </summary>
    private static string? FormatMembers(Participation participation)
    {
        var members = participation.Members
            .Select(m => m.User)
            .Where(u => u is not null)
            .Select(u =>
            {
                var name = string.IsNullOrWhiteSpace(u.RealName) ? u.UserName : u.RealName;
                return string.IsNullOrWhiteSpace(u.StdNumber) ? name : $"{name}({u.StdNumber})";
            })
            .Distinct()
            .OrderBy(x => x)
            .ToArray();

        return members.Length == 0 ? null : string.Join("、", members);
    }
}
