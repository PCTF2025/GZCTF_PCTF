using System.ComponentModel.DataAnnotations;

namespace GZCTF.Models.Request.Edit;

/// <summary>
/// Basic challenge information (Edit)
/// </summary>
public class ChallengeInfoModel
{
    /// <summary>
    /// Challenge ID
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Challenge title
    /// </summary>
    [Required(ErrorMessageResourceName = nameof(Resources.Program.Model_TitleRequired),
        ErrorMessageResourceType = typeof(Resources.Program))]
    [MinLength(1, ErrorMessageResourceName = nameof(Resources.Program.Model_TitleTooShort),
        ErrorMessageResourceType = typeof(Resources.Program))]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Challenge category
    /// </summary>
    public ChallengeCategory Category { get; set; } = ChallengeCategory.Misc;

    /// <summary>
    /// Challenge type
    /// </summary>
    public ChallengeType Type { get; set; } = ChallengeType.StaticAttachment;

    /// <summary>
    /// Is the challenge enabled
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Challenge score
    /// </summary>
    public int Score { get; set; } = 500;

    /// <summary>
    /// Minimum score
    /// </summary>
    public int MinScore { get; set; }

    /// <summary>
    /// Original score
    /// </summary>
    public int OriginalScore { get; set; } = 500;

    /// <summary>
    /// The deadline of the challenge, null means no deadline
    /// </summary>
    public DateTimeOffset? DeadlineUtc { get; set; }

    /// <summary>
    /// Recruitment week (1-5 for weeks, 6 for extension, null if not assigned)
    /// </summary>
    public int? Week { get; set; }

    /// <summary>
    /// 当前登录用户是否可编辑该题目。
    /// 出题人能看到全部题目，但只有自己创建的（或被授权方向下的）才能修改。
    /// </summary>
    public bool CanEdit { get; set; }

    /// <summary>
    /// 当前登录用户是否可查看该题目（能看即可测题）。
    /// 出题人对自己方向下的他人题目只有查看与测题权限，不能修改。
    /// </summary>
    public bool CanTest { get; set; }

    /// <summary>
    /// 题目创建者用户名，便于出题人辨认归属
    /// </summary>
    public string? AuthorName { get; set; }

    /// <summary>
    /// 外部动态 flag（共用服务器场景，每队 flag 不同）
    /// </summary>
    public bool ExternalDynamicFlag { get; set; }

    internal static ChallengeInfoModel FromChallenge(GameChallenge challenge, string? authorName = null) =>
        new()
        {
            Id = challenge.Id,
            Title = challenge.Title,
            Category = challenge.Category,
            Type = challenge.Type,
            Score = challenge.OriginalScore, // This field should be updated with scoreboard
            MinScore = (int)Math.Floor(challenge.MinScoreRate * challenge.OriginalScore),
            OriginalScore = challenge.OriginalScore,
            IsEnabled = challenge.IsEnabled,
            DeadlineUtc = challenge.DeadlineUtc,
            Week = challenge.Week,
            AuthorName = authorName,
            ExternalDynamicFlag = challenge.ExternalDynamicFlag
        };
}
