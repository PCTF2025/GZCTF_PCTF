using System.ComponentModel.DataAnnotations;

namespace GZCTF.Models.Request.Account;

/// <summary>
/// 学校选项（用于注册 / 报名时的学校选择）
/// </summary>
/// <param name="Name">学校名称</param>
/// <param name="Slug">学校短名，绑定与展示用</param>
/// <param name="Mode">认证模式：cas / email</param>
/// <param name="EmailSuffixes">允许的邮箱后缀，邮箱模式下用于前端提示</param>
/// <param name="InviteCodeRequired">该校绑定是否需要邀请码（供前端提示必填）</param>
public record SchoolOptionModel(
    string Name,
    string Slug,
    string Mode,
    List<string> EmailSuffixes,
    bool InviteCodeRequired);

/// <summary>
/// 使用邀请码绑定学校的请求
/// </summary>
public class SchoolBindModel
{
    /// <summary>
    /// 目标学校短名
    /// </summary>
    [Required(ErrorMessageResourceName = nameof(Resources.Program.Model_ContentRequired),
        ErrorMessageResourceType = typeof(Resources.Program))]
    [MaxLength(Limits.MaxUserDataLength)]
    public string SchoolSlug { get; set; } = string.Empty;

    /// <summary>
    /// 学校邀请码（该校未配置邀请码时可不填）
    /// </summary>
    [MaxLength(Limits.MaxUserDataLength)]
    public string? InviteCode { get; set; }

    /// <summary>
    /// 学号（可选，填写后一并更新到个人资料）
    /// </summary>
    [MaxLength(Limits.MaxStdNumberLength)]
    public string? StdNumber { get; set; }
}
