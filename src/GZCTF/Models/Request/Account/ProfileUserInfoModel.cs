namespace GZCTF.Models.Request.Account;

/// <summary>
/// Basic account information
/// </summary>
public class ProfileUserInfoModel
{
    /// <summary>
    /// User ID
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// User role
    /// </summary>
    public Role Role { get; set; }

    /// <summary>
    /// Username
    /// </summary>
    public string? UserName { get; set; }

    /// <summary>
    /// Email
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Bio
    /// </summary>
    public string? Bio { get; set; }

    /// <summary>
    /// Phone number
    /// </summary>
    public string? Phone { get; set; }

    /// <summary>
    /// Real name
    /// </summary>
    public string? RealName { get; set; }

    /// <summary>
    /// Student ID
    /// </summary>
    public string? StdNumber { get; set; }

    /// <summary>
    /// 已绑定的学校（未绑定时为 null）
    /// </summary>
    public string? School { get; set; }

    /// <summary>
    /// 学校绑定来源：None / Sso / Invite
    /// </summary>
    public SchoolBindSource SchoolSource { get; set; }

    /// <summary>
    /// 年级
    /// </summary>
    public string? Grade { get; set; }

    /// <summary>
    /// 学籍审核状态
    /// </summary>
    public VerifyStatus VerifyStatus { get; set; }

    /// <summary>
    /// 审核备注（驳回原因）
    /// </summary>
    public string? VerifyNote { get; set; }

    /// <summary>
    /// 该用户是否为学校管理员（负责至少一所学校）
    /// </summary>
    public bool IsSchoolAdmin { get; set; }

    /// <summary>
    /// 该用户负责审核的学校列表
    /// </summary>
    public List<string> ManagedSchools { get; set; } = [];

    /// <summary>
    /// 附加职能权限位（出题人 / 出题管理员 / 学校负责人）
    /// </summary>
    public UserPermission Permissions { get; set; }

    /// <summary>
    /// 是否可进入题目编辑功能（出题人或出题管理员）
    /// </summary>
    public bool CanEditChallenge { get; set; }

    /// <summary>
    /// 是否为出题人
    /// </summary>
    public bool IsChallengeAuthor { get; set; }

    /// <summary>
    /// 是否为出题管理员
    /// </summary>
    public bool IsChallengeManager { get; set; }

    /// <summary>
    /// 被指定可出题的方向（逗号分隔），空表示不限制
    /// </summary>
    public string? ManagedCategories { get; set; }

    /// <summary>
    /// Avatar URL
    /// </summary>
    public string? Avatar { get; set; }

    internal static ProfileUserInfoModel FromUserInfo(UserInfo user) =>
        new()
        {
            UserId = user.Id,
            Bio = user.Bio,
            Email = user.Email,
            UserName = user.UserName,
            RealName = user.RealName,
            Phone = user.PhoneNumber,
            Avatar = user.AvatarUrl,
            StdNumber = user.StdNumber,
            School = string.IsNullOrWhiteSpace(user.School) ? null : user.School,
            SchoolSource = user.SchoolSource,
            Grade = string.IsNullOrWhiteSpace(user.Grade) ? null : user.Grade,
            VerifyStatus = user.VerifyStatus,
            VerifyNote = string.IsNullOrWhiteSpace(user.VerifyNote) ? null : user.VerifyNote,
            IsSchoolAdmin = user.IsSchoolAdmin,
            ManagedSchools = user.ManagedSchoolList.ToList(),
            Permissions = user.Permissions,
            IsChallengeAuthor = user.IsChallengeAuthor,
            IsChallengeManager = user.IsChallengeManager,
            CanEditChallenge = user.CanEditChallenge,
            ManagedCategories = string.IsNullOrWhiteSpace(user.ManagedCategories)
                ? null
                : user.ManagedCategories,
            Role = user.Role
        };
}
