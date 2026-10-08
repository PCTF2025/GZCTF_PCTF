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
    /// Duration, in days, for each configured week
    /// </summary>
    [Range(1, 365)]
    public int Week1DurationDays { get; set; } = 7;
    [Range(1, 365)]
    public int Week2DurationDays { get; set; } = 7;
    [Range(1, 365)]
    public int Week3DurationDays { get; set; } = 7;
    [Range(1, 365)]
    public int Week4DurationDays { get; set; } = 7;
    [Range(1, 365)]
    public int Week5DurationDays { get; set; } = 7;

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
            Week1DurationDays = game.Week1DurationDays,
            Week2DurationDays = game.Week2DurationDays,
            Week3DurationDays = game.Week3DurationDays,
            Week4DurationDays = game.Week4DurationDays,
            Week5DurationDays = game.Week5DurationDays
        };
}
