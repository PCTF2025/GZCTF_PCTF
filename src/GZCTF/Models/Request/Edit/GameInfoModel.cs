using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace GZCTF.Models.Request.Edit;

/// <summary>
/// Game information (Edit)
/// </summary>
public class GameInfoModel
{
    /// <summary>
    /// Game ID
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Game title
    /// </summary>
    [Required]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Is hidden
    /// </summary>
    public bool Hidden { get; set; }

    /// <summary>
    /// Game summary
    /// </summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// Game detailed description
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Accept teams without review
    /// </summary>
    public bool AcceptWithoutReview { get; set; }

    /// <summary>
    /// Is writeup required
    /// </summary>
    public bool WriteupRequired { get; set; }

    /// <summary>
    /// Game invitation code
    /// </summary>
    [MaxLength(Limits.InviteTokenLength,
        ErrorMessageResourceName = nameof(Resources.Program.Model_InvitationCodeTooLong),
        ErrorMessageResourceType = typeof(Resources.Program))]
    public string? InviteCode { get; set; }

    /// <summary>
    /// Team member count limit, 0 means no limit
    /// </summary>
    public int TeamMemberCountLimit { get; set; }

    /// <summary>
    /// Container count limit per team
    /// </summary>
    public int ContainerCountLimit { get; set; } = 3;

    /// <summary>
    /// Game poster URL
    /// </summary>
    [JsonPropertyName("poster")]
    public string? PosterUrl { get; set; } = string.Empty;

    /// <summary>
    /// Game public key
    /// </summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>
    /// Is the game in practice mode (accessible even after the game ends)
    /// </summary>
    public bool PracticeMode { get; set; } = true;

    /// <summary>
    /// Start time
    /// </summary>
    [Required]
    [JsonPropertyName("start")]
    public DateTimeOffset StartTimeUtc { get; set; } = DateTimeOffset.FromUnixTimeSeconds(0);

    /// <summary>
    /// End time
    /// </summary>
    [Required]
    [JsonPropertyName("end")]
    public DateTimeOffset EndTimeUtc { get; set; } = DateTimeOffset.FromUnixTimeSeconds(0);

    /// <summary>
    /// Writeup submission deadline
    /// </summary>
    public DateTimeOffset WriteupDeadline { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Writeup additional notes
    /// </summary>
    public string WriteupNote { get; set; } = string.Empty;

    /// <summary>
    /// Blood bonus points
    /// </summary>
    [JsonPropertyName("bloodBonus")]
    public long BloodBonusValue { get; set; } = BloodBonus.DefaultValue;

    /// <summary>
    /// Whether challenges are organized and displayed by week
    /// </summary>
    public bool WeekModeEnabled { get; set; }

    /// <summary>
    /// 每周的起止时间（UTC）。任一端留空表示该端不限制；两端都留空表示该周未配置。
    /// </summary>
    public DateTimeOffset? Week1StartUtc { get; set; }
    public DateTimeOffset? Week1EndUtc { get; set; }
    public DateTimeOffset? Week2StartUtc { get; set; }
    public DateTimeOffset? Week2EndUtc { get; set; }
    public DateTimeOffset? Week3StartUtc { get; set; }
    public DateTimeOffset? Week3EndUtc { get; set; }
    public DateTimeOffset? Week4StartUtc { get; set; }
    public DateTimeOffset? Week4EndUtc { get; set; }
    public DateTimeOffset? Week5StartUtc { get; set; }
    public DateTimeOffset? Week5EndUtc { get; set; }

    /// <summary>每周自定义名称，留空显示「第 N 周」</summary>
    [MaxLength(64)]
    public string? Week1Name { get; set; }
    [MaxLength(64)]
    public string? Week2Name { get; set; }
    [MaxLength(64)]
    public string? Week3Name { get; set; }
    [MaxLength(64)]
    public string? Week4Name { get; set; }
    [MaxLength(64)]
    public string? Week5Name { get; set; }

    /// <summary>挑战题分组名称，留空显示「挑战题」</summary>
    [MaxLength(64)]
    public string? ChallengeBucketName { get; set; }

    /// <summary>其他题分组名称，留空显示「其他题」</summary>
    [MaxLength(64)]
    public string? MiscBucketName { get; set; }

    internal static GameInfoModel FromGame(Data.Game game) =>
        new()
        {
            Id = game.Id,
            Title = game.Title,
            Summary = game.Summary,
            Content = game.Content,
            Hidden = game.Hidden,
            PracticeMode = game.PracticeMode,
            PosterUrl = game.PosterUrl,
            InviteCode = game.InviteCode,
            PublicKey = game.PublicKey,
            AcceptWithoutReview = game.AcceptWithoutReview,
            TeamMemberCountLimit = game.TeamMemberCountLimit,
            ContainerCountLimit = game.ContainerCountLimit,
            StartTimeUtc = game.StartTimeUtc,
            EndTimeUtc = game.EndTimeUtc,
            WriteupDeadline = game.WriteupDeadline,
            WriteupNote = game.WriteupNote,
            WriteupRequired = game.WriteupRequired,
            BloodBonusValue = game.BloodBonus.Val,
            WeekModeEnabled = game.WeekModeEnabled,
            Week1StartUtc = game.Week1StartUtc,
            Week1EndUtc = game.Week1EndUtc,
            Week2StartUtc = game.Week2StartUtc,
            Week2EndUtc = game.Week2EndUtc,
            Week3StartUtc = game.Week3StartUtc,
            Week3EndUtc = game.Week3EndUtc,
            Week4StartUtc = game.Week4StartUtc,
            Week4EndUtc = game.Week4EndUtc,
            Week5StartUtc = game.Week5StartUtc,
            Week5EndUtc = game.Week5EndUtc,
            Week1Name = game.Week1Name,
            Week2Name = game.Week2Name,
            Week3Name = game.Week3Name,
            Week4Name = game.Week4Name,
            Week5Name = game.Week5Name,
            ChallengeBucketName = game.ChallengeBucketName,
            MiscBucketName = game.MiscBucketName
        };
}
