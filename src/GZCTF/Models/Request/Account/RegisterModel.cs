using System.ComponentModel.DataAnnotations;
using GZCTF.Services;

namespace GZCTF.Models.Request.Account;

/// <summary>
/// Account registration
/// </summary>
public class RegisterModel : ModelWithCaptcha
{
    /// <summary>
    /// Username
    /// </summary>
    [Required(ErrorMessageResourceName = nameof(Resources.Program.Model_UserNameRequired),
        ErrorMessageResourceType = typeof(Resources.Program))]
    [MinLength(Limits.MinUserNameLength, ErrorMessageResourceName = nameof(Resources.Program.Model_UserNameTooShort),
        ErrorMessageResourceType = typeof(Resources.Program))]
    [MaxLength(Limits.MaxUserNameLength, ErrorMessageResourceName = nameof(Resources.Program.Model_UserNameTooLong),
        ErrorMessageResourceType = typeof(Resources.Program))]
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// Password
    /// </summary>
    [Required(ErrorMessageResourceName = nameof(Resources.Program.Model_PasswordRequired),
        ErrorMessageResourceType = typeof(Resources.Program))]
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Email
    /// </summary>
    [Required(ErrorMessageResourceName = nameof(Resources.Program.Model_EmailRequired),
        ErrorMessageResourceType = typeof(Resources.Program))]
    [EmailAddress(ErrorMessageResourceName = nameof(Resources.Program.Model_EmailMalformed),
        ErrorMessageResourceType = typeof(Resources.Program))]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// 学校短名（可选）。来自学校邮箱认证（快速登录）入口，
    /// 注册时直接绑定该学校，无需邀请码。
    /// </summary>
    [MaxLength(Limits.MaxUserDataLength)]
    public string? SchoolSlug { get; set; }

    /// <summary>
    /// 真实姓名（主办赛道报名必需，注册时填写可免二次提交）
    /// </summary>
    [MaxLength(Limits.MaxUserDataLength)]
    public string? RealName { get; set; }

    /// <summary>
    /// 学号
    /// </summary>
    [MaxLength(Limits.MaxStdNumberLength)]
    public string? StdNumber { get; set; }

    /// <summary>
    /// 年级（大一 / 大二 / 大三 / 大四 / 研一 / 研二 / 研三）
    /// </summary>
    [MaxLength(Limits.MaxUserDataLength)]
    public string? Grade { get; set; }
}
