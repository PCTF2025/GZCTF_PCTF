using System.ComponentModel.DataAnnotations;

namespace GZCTF.Models.Request.Admin;

/// <summary>
/// User information modification (Admin)
/// </summary>
public class AdminUserInfoModel
{
    /// <summary>
    /// Username
    /// </summary>
    [MinLength(Limits.MinUserNameLength, ErrorMessageResourceName = nameof(Resources.Program.Model_UserNameTooShort),
        ErrorMessageResourceType = typeof(Resources.Program))]
    [MaxLength(Limits.MaxUserNameLength, ErrorMessageResourceName = nameof(Resources.Program.Model_UserNameTooLong),
        ErrorMessageResourceType = typeof(Resources.Program))]
    public string? UserName { get; set; }

    /// <summary>
    /// Email
    /// </summary>
    [EmailAddress(ErrorMessageResourceName = nameof(Resources.Program.Model_EmailMalformed),
        ErrorMessageResourceType = typeof(Resources.Program))]
    public string? Email { get; set; }

    /// <summary>
    /// Signature
    /// </summary>
    [MaxLength(Limits.MaxUserDataLength, ErrorMessageResourceName = nameof(Resources.Program.Model_BioTooLong),
        ErrorMessageResourceType = typeof(Resources.Program))]
    public string? Bio { get; set; }

    /// <summary>
    /// Phone number
    /// </summary>
    [Phone(ErrorMessageResourceName = nameof(Resources.Program.Model_MalformedPhoneNumber),
        ErrorMessageResourceType = typeof(Resources.Program))]
    public string? Phone { get; set; }

    /// <summary>
    /// Real name
    /// </summary>
    [MaxLength(Limits.MaxUserDataLength, ErrorMessageResourceName = nameof(Resources.Program.Model_RealNameTooLong),
        ErrorMessageResourceType = typeof(Resources.Program))]
    public string? RealName { get; set; }

    /// <summary>
    /// Student number
    /// </summary>
    [MaxLength(Limits.MaxStdNumberLength, ErrorMessageResourceName = nameof(Resources.Program.Model_StdNumberTooLong),
        ErrorMessageResourceType = typeof(Resources.Program))]
    public string? StdNumber { get; set; }

    /// <summary>
    /// Is email confirmed (can log in)
    /// </summary>
    public bool? EmailConfirmed { get; set; }

    /// <summary>
    /// User role
    /// </summary>
    public Role? Role { get; set; }

    /// <summary>
    /// 年级
    /// </summary>
    [MaxLength(Limits.MaxUserDataLength)]
    public string? Grade { get; set; }

    /// <summary>
    /// 所属学校短名。传空串表示清除归属，传 null 表示不修改。
    /// 由平台管理员在用户管理界面调整，仅限 Role.Admin 调用。
    /// </summary>
    [MaxLength(Limits.MaxUserDataLength)]
    public string? School { get; set; }

    /// <summary>
    /// 该用户负责审核的学校短名（逗号分隔）。
    /// 设置后该用户即成为这些学校的学校负责人。
    /// </summary>
    [MaxLength(Limits.MaxUserDataLength)]
    public string? ManagedSchools { get; set; }

    /// <summary>
    /// 附加职能权限位（出题人 / 出题管理员 / 学校负责人），可同时赋予多个。
    /// 传 null 表示不修改。
    /// </summary>
    public UserPermission? Permissions { get; set; }

    /// <summary>
    /// 作为「出题管理员」被授权管理的题目方向（ChallengeCategory 名称，逗号分隔）。
    /// </summary>
    [MaxLength(Limits.MaxUserDataLength)]
    public string? ManagedCategories { get; set; }
}
