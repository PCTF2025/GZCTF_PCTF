using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace GZCTF.Models.Request.Game;

/// <summary>
/// Detailed game information, including detailed introduction and current team registration status
/// </summary>
public class DetailedGameInfoModel
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Game title
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Game description
    /// </summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// Detailed introduction of the game
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Whether the game is hidden
    /// </summary>
    public bool Hidden { get; set; }

    /// <summary>
    /// List of participation divisions
    /// </summary>
    public HashSet<DivisionInfo>? Divisions { get; set; }

    /// <summary>
    /// Whether an invitation code is required
    /// </summary>
    public bool InviteCodeRequired { get; set; }

    /// <summary>
    /// Whether writeup submission is required
    /// </summary>
    public bool WriteupRequired { get; set; }

    /// <summary>
    /// Game poster URL
    /// </summary>
    [JsonPropertyName("poster")]
    public string? PosterUrl { get; set; } = string.Empty;

    /// <summary>
    /// Team member count limit
    /// </summary>
    [JsonPropertyName("limit")]
    public int TeamMemberCountLimit { get; set; }

    /// <summary>
    /// Number of teams registered for participation
    /// </summary>
    public int TeamCount { get; set; }

    /// <summary>
    /// Current registered division
    /// </summary>
    public int? Division { get; set; }

    /// <summary>
    /// Team name for participation
    /// </summary>
    public string? TeamName { get; set; }

    /// <summary>
    /// Whether the game is in practice mode (can still be accessed after the game ends)
    /// </summary>
    public bool PracticeMode { get; set; } = true;

    /// <summary>
    /// Team participation status
    /// </summary>
    [JsonPropertyName("status")]
    public ParticipationStatus Status { get; set; } = ParticipationStatus.Unsubmitted;

    /// <summary>
    /// Start time
    /// </summary>
    [JsonPropertyName("start")]
    public DateTimeOffset StartTimeUtc { get; set; } = DateTimeOffset.FromUnixTimeSeconds(0);

    /// <summary>
    /// End time
    /// </summary>
    [JsonPropertyName("end")]
    public DateTimeOffset EndTimeUtc { get; set; } = DateTimeOffset.FromUnixTimeSeconds(0);

    /// <summary>
    /// Whether challenges are organized and displayed by week
    /// </summary>
    public bool WeekModeEnabled { get; set; }

    /// <summary>
    /// 周次与分组配置（含各自时间窗口与名称），供答题页渲染筛选与倒计时
    /// </summary>
    public List<WeekBucketInfo> WeekBuckets { get; set; } = [];

    public DetailedGameInfoModel WithParticipation(Participation? part, int teamCount)
    {
        TeamCount = teamCount;
        Status = part?.Status ?? ParticipationStatus.Unsubmitted;
        TeamName = part?.Team.Name;
        Division = part?.DivisionId;
        return this;
    }

    internal static DetailedGameInfoModel FromGame(Data.Game game) =>
        new()
        {
            Id = game.Id,
            Title = game.Title,
            Hidden = game.Hidden,
            Summary = game.Summary,
            Content = game.Content,
            PracticeMode = game.PracticeMode,
            WeekModeEnabled = game.WeekModeEnabled,
            WeekBuckets = BuildWeekBuckets(game),
            Divisions =
                game.Divisions?.Select(d => new DivisionInfo
                {
                    Id = d.Id,
                    Name = d.Name,
                    InviteCodeRequired = !string.IsNullOrWhiteSpace(d.InviteCode)
                }).ToHashSet(),
            InviteCodeRequired = !string.IsNullOrWhiteSpace(game.InviteCode),
            WriteupRequired = game.WriteupRequired,
            PosterUrl = game.PosterUrl,
            StartTimeUtc = game.StartTimeUtc,
            EndTimeUtc = game.EndTimeUtc,
            TeamMemberCountLimit = game.TeamMemberCountLimit
        };

    /// <summary>
    /// 汇总 1-5 周 + 挑战题 + 其他题共 7 个分桶；周次桶带时间窗口，
    /// 两个非周次桶不带时间（始终可做）。
    /// </summary>
    private static List<WeekBucketInfo> BuildWeekBuckets(Data.Game game)
    {
        var now = DateTimeOffset.UtcNow;
        var list = new List<WeekBucketInfo>();

        for (var week = 1; week <= 5; week++)
        {
            var (start, end) = game.GetWeekWindow(week);
            if (start is null && end is null)
                continue;

            list.Add(new WeekBucketInfo
            {
                Key = week,
                Name = game.GetWeekName(week),
                StartUtc = start,
                EndUtc = end,
                IsOpen = game.IsChallengeOpen(week, now)
            });
        }

        list.Add(new WeekBucketInfo
        {
            Key = Data.Game.ChallengeBucket,
            Name = game.GetBucketName(Data.Game.ChallengeBucket),
            IsOpen = true
        });

        list.Add(new WeekBucketInfo
        {
            Key = Data.Game.MiscBucket,
            Name = game.GetBucketName(Data.Game.MiscBucket),
            IsOpen = true
        });

        return list;
    }
}

public class DivisionInfo
{
    /// <summary>
    /// Division ID
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Division name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Is the division invite code required
    /// </summary>
    public bool InviteCodeRequired { get; set; }

}

/// <summary>
/// 单个分桶（某周 / 挑战题 / 其他题）的下发信息
/// </summary>
public class WeekBucketInfo
{
    /// <summary>1-5 为周次，6 为挑战题，7 为其他题</summary>
    public int Key { get; set; }

    /// <summary>显示名称</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>开始时间（UTC），仅周次桶有值</summary>
    public DateTimeOffset? StartUtc { get; set; }

    /// <summary>结束时间（UTC），仅周次桶有值</summary>
    public DateTimeOffset? EndUtc { get; set; }

    /// <summary>当前时刻是否可提交</summary>
    public bool IsOpen { get; set; }
}
