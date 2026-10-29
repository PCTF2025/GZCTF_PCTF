using System.ComponentModel.DataAnnotations;

namespace GZCTF.Models.Request.Game;

/// <summary>
/// 队伍视角：可提交 WP 的题目与已提交状态
/// </summary>
public class ChallengeWriteupListModel
{
    public List<ChallengeWriteupItemModel> Items { get; set; } = [];
}

public class ChallengeWriteupItemModel
{
    public int ChallengeId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    /// <summary>是否挑战题（Week = 挑战题分桶）</summary>
    public bool IsChallengeBucket { get; set; }

    /// <summary>是否已解出（只有解出的题可提交 WP）</summary>
    public bool Solved { get; set; }

    /// <summary>是否已提交 WP</summary>
    public bool Submitted { get; set; }

    public DateTimeOffset? SubmitTimeUtc { get; set; }

    /// <summary>已获得的加分</summary>
    public int AwardedScore { get; set; }

    public string? ReviewNote { get; set; }

    /// <summary>文件名（用于下载）</summary>
    public string? FileName { get; set; }
}

/// <summary>
/// 审核视角：某场比赛的全部 WP 提交
/// </summary>
public class ChallengeWriteupReviewListModel
{
    public List<ChallengeWriteupReviewItemModel> Items { get; set; } = [];
}

public class ChallengeWriteupReviewItemModel
{
    public int Id { get; set; }

    public int TeamId { get; set; }

    public string TeamName { get; set; } = string.Empty;

    public int ChallengeId { get; set; }

    public string ChallengeTitle { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public DateTimeOffset SubmitTimeUtc { get; set; }

    public int AwardedScore { get; set; }

    public DateTimeOffset? AwardedAtUtc { get; set; }

    public string? ReviewNote { get; set; }

    public string? FileName { get; set; }
}

/// <summary>
/// 加分请求
/// </summary>
public class ChallengeWriteupAwardModel
{
    /// <summary>加分数值（计入排行榜总分）</summary>
    [Range(0, 100000)]
    public int Score { get; set; }

    /// <summary>审核备注</summary>
    [MaxLength(512)]
    public string? Note { get; set; }
}
