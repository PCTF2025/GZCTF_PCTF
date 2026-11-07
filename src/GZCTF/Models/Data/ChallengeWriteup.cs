using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GZCTF.Models.Data;

/// <summary>
/// 解题思路（WP）提交与人工加分记录。
/// 按周提交：每支队伍每周一份，可重复提交覆盖；管理员/出题人审核后手动加分。
/// </summary>
public class ChallengeWriteup
{
    [Key]
    public int Id { get; set; }

    public int GameId { get; set; }

    [ForeignKey(nameof(GameId))]
    public Game Game { get; set; } = null!;

    public int ParticipationId { get; set; }

    [ForeignKey(nameof(ParticipationId))]
    public Participation Participation { get; set; } = null!;

    /// <summary>
    /// 提交所属周次（1-5）。旧数据按题提交，保留 ChallengeId 作为历史记录。
    /// </summary>
    public int Week { get; set; }

    /// <summary>
    /// 历史按题提交时的题目 Id；按周提交后为 null
    /// </summary>
    public int? ChallengeId { get; set; }

    [ForeignKey(nameof(ChallengeId))]
    public GameChallenge? Challenge { get; set; }

    /// <summary>上传的 WP 文件（PDF）</summary>
    public int? FileId { get; set; }

    [ForeignKey(nameof(FileId))]
    public LocalFile? File { get; set; }

    /// <summary>提交时间</summary>
    public DateTimeOffset SubmitTimeUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>人工加分（计入排行榜总分）</summary>
    public int AwardedScore { get; set; }

    /// <summary>加分操作人</summary>
    public Guid? AwardedById { get; set; }

    [ForeignKey(nameof(AwardedById))]
    public UserInfo? AwardedBy { get; set; }

    /// <summary>加分时间</summary>
    public DateTimeOffset? AwardedAtUtc { get; set; }

    /// <summary>审核备注</summary>
    [MaxLength(512)]
    public string? ReviewNote { get; set; }
}
