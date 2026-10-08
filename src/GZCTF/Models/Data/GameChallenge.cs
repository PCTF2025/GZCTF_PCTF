using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GZCTF.Models.Request.Edit;

namespace GZCTF.Models.Data;

public class GameChallenge : Challenge
{
    /// <summary>
    /// Whether to record traffic
    /// </summary>
    public bool EnableTrafficCapture { get; set; }

    /// <summary>
    /// Whether to disable blood bonus
    /// </summary>
    public bool DisableBloodBonus { get; set; }

    /// <summary>
    /// 题目归属分桶：1-5 表示第 N 周，6 表示挑战题，7 表示其他题；
    /// null 表示未归类（不受周次时间窗口限制）。
    /// </summary>
    [Range(1, 7)]
    public int? Week { get; set; }

    /// <summary>
    /// Initial score
    /// </summary>
    [Required]
    public int OriginalScore { get; set; } = 1000;

    /// <summary>
    /// Minimum score rate
    /// </summary>
    [Required]
    [Range(0, 1)]
    public double MinScoreRate { get; set; } = 0.25;

    /// <summary>
    /// Difficulty coefficient
    /// </summary>
    [Required]
    public double Difficulty { get; set; } = 5;

    /// <summary>
    /// Current score of the challenge
    /// </summary>
    [NotMapped]
    public int CurrentScore => CalculateChallengeScore(
        OriginalScore,
        MinScoreRate,
        Difficulty,
        FirstSolves?.Count ?? 0);


    internal static int CalculateChallengeScore(int originalScore, double minScoreRate, double difficulty,
        int acceptedCount)
    {
        if (acceptedCount <= 1)
            return originalScore;

        return (int)Math.Floor(
            originalScore *
            (minScoreRate + (1.0 - minScoreRate) * Math.Exp((1 - acceptedCount) / difficulty)));
    }

    internal void Update(ChallengeUpdateModel model)
    {
        Title = model.Title ?? Title;
        Content = model.Content ?? Content;
        Category = model.Category ?? Category;
        // Week 为 null 表示保持原值，0 表示清空归属，1~7 为有效分桶
        if (model.Week is not null)
            Week = model.Week is >= 1 and <= 7 ? model.Week : null;
        Hints = model.Hints ?? Hints;
        CPUCount = model.CPUCount ?? CPUCount;
        MemoryLimit = model.MemoryLimit ?? MemoryLimit;
        StorageLimit = model.StorageLimit ?? StorageLimit;
        ContainerImage = model.ContainerImage?.Trim() ?? ContainerImage;
        ExposePort = model.ExposePort ?? ExposePort;
        NetworkMode = model.NetworkMode ?? NetworkMode;
        OriginalScore = model.OriginalScore ?? OriginalScore;
        MinScoreRate = model.MinScoreRate ?? MinScoreRate;
        Difficulty = model.Difficulty ?? Difficulty;
        FileName = model.FileName ?? FileName;
        DisableBloodBonus = model.DisableBloodBonus ?? DisableBloodBonus;
        SubmissionLimit = model.SubmissionLimit ?? SubmissionLimit;

        // isEnabled should be updated alone
        IsEnabled = model.IsEnabled ?? IsEnabled;

        // only set DeadlineUtc to null when pass DateTimeOffset.MinValue (but not null)
        if (model.DeadlineUtc is { } time)
            DeadlineUtc = time.ToUnixTimeSeconds() == 0 ? null : time;

        // only set FlagTemplate to null when pass an empty string (but not null)
        if (model.FlagTemplate is { } template)
            FlagTemplate = string.IsNullOrWhiteSpace(template) ? null : template;

        // Container only
        EnableTrafficCapture = Type.IsContainer() && (model.EnableTrafficCapture ?? EnableTrafficCapture);
    }

    #region Db Relationship

    /// <summary>
    /// Submissions
    /// </summary>
    public List<Submission> Submissions { get; set; } = [];

    /// <summary>
    /// Challenge instances
    /// </summary>
    public List<GameInstance> Instances { get; set; } = [];

    /// <summary>
    /// Teams that activated the challenge
    /// </summary>
    public HashSet<Participation> Teams { get; set; } = [];

    /// <summary>
    /// Configurations for divisions
    /// </summary>
    public HashSet<DivisionChallengeConfig> DivisionConfigs { get; set; } = [];

    /// <summary>
    /// First solves recorded for this challenge.
    /// </summary>
    public List<FirstSolve>? FirstSolves { get; set; } = [];

    /// <summary>
    /// Game ID
    /// </summary>
    public int GameId { get; set; }

    /// <summary>
    /// Game object
    /// </summary>
    public Game Game { get; set; } = null!;

    #endregion Db Relationship
}
