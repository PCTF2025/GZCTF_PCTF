using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Net;
using GZCTF.Models.Request.Account;
using GZCTF.Models.Request.Admin;
using MemoryPack;
using Microsoft.AspNetCore.Identity;

namespace GZCTF.Models.Data;

[MemoryPackable]
public partial class UserInfo : IdentityUser<Guid>
{
    /// <summary>
    /// Override Guid to use Ulid
    /// </summary>
    [PersonalData]
    public override Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>
    /// User role
    /// </summary>
    [ProtectedPersonalData]
    public Role Role { get; set; } = Role.User;

    /// <summary>
    /// User's recent IP address
    /// </summary>
    [IPAddressFormatter]
    public IPAddress IP { get; set; } = IPAddress.Any;

    /// <summary>
    /// User's last sign-in time
    /// </summary>
    public DateTimeOffset LastSignedInUtc { get; set; } = DateTimeOffset.FromUnixTimeSeconds(0);

    /// <summary>
    /// User's last visit time
    /// </summary>
    public DateTimeOffset LastVisitedUtc { get; set; } = DateTimeOffset.FromUnixTimeSeconds(0);

    /// <summary>
    /// User registration time
    /// </summary>
    public DateTimeOffset RegisterTimeUtc { get; set; } = DateTimeOffset.FromUnixTimeSeconds(0);

    /// <summary>
    /// User bio
    /// </summary>
    [MaxLength(Limits.MaxUserDataLength)]
    public string Bio { get; set; } = string.Empty;

    /// <summary>
    /// Real name
    /// </summary>
    [MaxLength(Limits.MaxUserDataLength)]
    [ProtectedPersonalData]
    public string RealName { get; set; } = string.Empty;

    /// <summary>
    /// Student ID
    /// </summary>
    [MaxLength(Limits.MaxStdNumberLength)]
    [ProtectedPersonalData]
    public string StdNumber { get; set; } = string.Empty;

    /// <summary>
    /// 已绑定的学校短名（对应 SsoConfig.Schools 中的 slug）
    /// 学校统一身份认证（快速登录）时自动写入；普通注册时通过邀请码绑定。
    /// </summary>
    [MaxLength(Limits.MaxUserDataLength)]
    public string School { get; set; } = string.Empty;

    /// <summary>
    /// 学校绑定来源：None = 未绑定；Sso = 快速登录自动绑定；Invite = 邀请码绑定
    /// </summary>
    public SchoolBindSource SchoolSource { get; set; } = SchoolBindSource.None;

    /// <summary>
    /// 年级（注册时选择，如 大一 / 大二 / 研一）
    /// </summary>
    [MaxLength(Limits.MaxUserDataLength)]
    public string Grade { get; set; } = string.Empty;

    /// <summary>
    /// 学籍信息审核状态。主办赛道报名要求审核通过。
    /// </summary>
    public VerifyStatus VerifyStatus { get; set; } = VerifyStatus.None;

    /// <summary>
    /// 审核备注（驳回原因等），由审核人填写
    /// </summary>
    [MaxLength(Limits.MaxUserDataLength)]
    public string VerifyNote { get; set; } = string.Empty;

    /// <summary>
    /// 审核人 UserId，用于追溯
    /// </summary>
    public Guid? VerifiedById { get; set; }

    /// <summary>
    /// 审核时间
    /// </summary>
    public DateTimeOffset? VerifiedAtUtc { get; set; }

    /// <summary>
    /// 该用户作为「学校管理员」负责的学校短名（多个以逗号分隔）。
    /// 非空时，该用户可审核这些学校学生的学籍信息。
    /// 与 Role 正交：不改变用户的角色权限，仅授予对应学校的审核权。
    /// </summary>
    [MaxLength(Limits.MaxUserDataLength)]
    public string ManagedSchools { get; set; } = string.Empty;

    /// <summary>
    /// 附加职能权限位（出题人 / 出题管理员 / 学校负责人）。
    /// 与 Role 正交，一个用户可以同时拥有多个权限。
    /// </summary>
    public UserPermission Permissions { get; set; } = UserPermission.None;

    /// <summary>
    /// 作为「出题管理员」被授权管理的题目方向（ChallengeCategory，逗号分隔）。
    /// 空表示未被授权任何方向；系统管理员不受此限制。
    /// </summary>
    [MaxLength(Limits.MaxUserDataLength)]
    public string ManagedCategories { get; set; } = string.Empty;

    /// <summary>
    /// Hide in exercise scoreboard
    /// </summary>
    public bool ExerciseVisible { get; set; } = true;

    [NotMapped]
    [MemoryPackIgnore]
    public string? AvatarUrl => AvatarHash is null ? null : $"/assets/{AvatarHash}/avatar";

    /// <summary>
    /// 该用户是否被指定为学校负责人（持有权限位且负责至少一所学校）。
    /// 兼容旧数据：仅设置了 ManagedSchools 而未打权限位时同样视为负责人。
    /// </summary>
    [NotMapped]
    [MemoryPackIgnore]
    public bool IsSchoolAdmin =>
        !string.IsNullOrWhiteSpace(ManagedSchools) &&
        (HasPermission(UserPermission.SchoolManager) || Permissions == UserPermission.None);

    /// <summary>
    /// 该用户负责审核的学校短名集合
    /// </summary>
    [NotMapped]
    [MemoryPackIgnore]
    public IReadOnlyList<string> ManagedSchoolList =>
        string.IsNullOrWhiteSpace(ManagedSchools)
            ? []
            : ManagedSchools
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

    /// <summary>
    /// 是否可审核指定学校的学生（系统管理员可审核任意学校）
    /// </summary>
    public bool CanManageSchool(string? schoolSlug) =>
        Role >= Role.Admin ||
        (HasPermission(UserPermission.SchoolManager) &&
         !string.IsNullOrWhiteSpace(schoolSlug) &&
         ManagedSchoolList.Any(s => string.Equals(s, schoolSlug, StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// 是否拥有指定权限位（系统管理员默认拥有全部权限）
    /// </summary>
    public bool HasPermission(UserPermission permission) =>
        Role >= Role.Admin || (Permissions & permission) == permission;

    /// <summary>
    /// 是否为出题人（可新增题目，并可管理自己创建的题目）
    /// </summary>
    [NotMapped]
    [MemoryPackIgnore]
    public bool IsChallengeAuthor => Role >= Role.Admin || HasPermission(UserPermission.ChallengeAuthor);

    /// <summary>
    /// 是否为出题管理员（可管理被授权方向下的全部题目）
    /// </summary>
    [NotMapped]
    [MemoryPackIgnore]
    public bool IsChallengeManager => Role >= Role.Admin || HasPermission(UserPermission.ChallengeManager);

    /// <summary>
    /// 是否可进入题目编辑功能（出题人或出题管理员）
    /// </summary>
    [NotMapped]
    [MemoryPackIgnore]
    public bool CanEditChallenge => Role >= Role.Admin || IsChallengeAuthor || IsChallengeManager;

    /// <summary>
    /// 被授权管理的题目方向集合
    /// </summary>
    [NotMapped]
    [MemoryPackIgnore]
    public IReadOnlyList<ChallengeCategory> ManagedCategoryList =>
        string.IsNullOrWhiteSpace(ManagedCategories)
            ? []
            : ManagedCategories
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(v => Enum.TryParse<ChallengeCategory>(v, true, out var c) ? (ChallengeCategory?)c : null)
                .Where(c => c.HasValue)
                .Select(c => c!.Value)
                .ToList();

    /// <summary>
    /// 是否可管理指定题目。
    /// 系统管理员放行全部；出题管理员放行被授权方向；
    /// 出题人放行自己创建的题目。
    /// </summary>
    /// <param name="category">题目方向</param>
    /// <param name="authorId">题目创建者 Id</param>
    public bool CanManageChallenge(ChallengeCategory category, Guid? authorId)
    {
        if (Role >= Role.Admin)
            return true;

        if (IsChallengeManager && ManagedCategoryList.Contains(category))
            return true;

        return IsChallengeAuthor && authorId.HasValue && authorId.Value == Id;
    }

    /// <summary>
    /// Update user's last visit time and IP address via HTTP request
    /// </summary>
    /// <param name="context"></param>
    public void UpdateByHttpContext(HttpContext context)
    {
        LastVisitedUtc = DateTimeOffset.UtcNow;

        var remoteAddress = context.Connection.RemoteIpAddress;

        if (remoteAddress is null)
            return;

        IP = remoteAddress;
    }

    internal void UpdateUserInfo(AdminUserInfoModel model)
    {
        // use SetUserNameAsync and SetEmailAsync to update UserName and Email
        Bio = model.Bio ?? Bio;
        Role = model.Role ?? Role;
        StdNumber = model.StdNumber ?? StdNumber;
        RealName = model.RealName ?? RealName;
        PhoneNumber = model.Phone ?? PhoneNumber;
        EmailConfirmed = model.EmailConfirmed ?? EmailConfirmed;
        Grade = model.Grade ?? Grade;
        ManagedSchools = model.ManagedSchools?.Trim() ?? ManagedSchools;
        Permissions = model.Permissions ?? Permissions;
        ManagedCategories = model.ManagedCategories?.Trim() ?? ManagedCategories;
    }

    /// <summary>
    /// Update user info by UserCreateModel
    /// for batch creation
    /// </summary>
    /// <param name="model"></param>
    internal void UpdateUserInfo(UserCreateModel model)
    {
        UserName = model.UserName;
        Email = model.Email;
        StdNumber = model.StdNumber ?? StdNumber;
        RealName = model.RealName ?? RealName;
        PhoneNumber = model.Phone ?? PhoneNumber;
    }

    internal void UpdateUserInfo(ProfileUpdateModel model)
    {
        // use SetUserNameAsync to update UserName
        Bio = model.Bio ?? Bio;
        PhoneNumber = model.Phone ?? PhoneNumber;

        // 学籍关键信息变更时，需重新走审核流程：
        // 已通过 / 已驳回的审核结果不能沿用到新填写的信息上。
        var profileChanged =
            (model.RealName is not null && model.RealName != RealName) ||
            (model.StdNumber is not null && model.StdNumber != StdNumber) ||
            (model.Grade is not null && model.Grade != Grade);

        RealName = model.RealName ?? RealName;
        StdNumber = model.StdNumber ?? StdNumber;
        Grade = model.Grade ?? Grade;

        if (profileChanged && VerifyStatus is VerifyStatus.Approved or VerifyStatus.Rejected)
        {
            VerifyStatus = VerifyStatus.None;
            VerifyNote = string.Empty;
            VerifiedById = null;
            VerifiedAtUtc = null;
        }
    }

    /// <summary>
    /// 学籍信息是否填写完整：学校 + 真实姓名 + 学号 + 年级
    /// </summary>
    [NotMapped]
    [MemoryPackIgnore]
    public bool HasCompleteProfile =>
        !string.IsNullOrWhiteSpace(School) &&
        !string.IsNullOrWhiteSpace(RealName) &&
        !string.IsNullOrWhiteSpace(StdNumber) &&
        !string.IsNullOrWhiteSpace(Grade);

    /// <summary>
    /// 提交学籍审核：信息完整且当前为空 / 被驳回时进入待审核
    /// </summary>
    internal void SubmitForVerification()
    {
        if (!HasCompleteProfile)
        {
            VerifyStatus = VerifyStatus.None;
            return;
        }

        if (VerifyStatus is VerifyStatus.None or VerifyStatus.Rejected)
        {
            VerifyStatus = VerifyStatus.Pending;
            VerifyNote = string.Empty;
        }
    }

    #region Db Relationship

    /// <summary>
    /// Avatar hash
    /// </summary>
    [MaxLength(Limits.FileHashLength)]
    public string? AvatarHash { get; set; }

    /// <summary>
    /// Personal submission records
    /// </summary>
    [MemoryPackIgnore]
    public List<Submission> Submissions { get; set; } = [];

    /// <summary>
    /// Participated teams
    /// </summary>
    [MemoryPackIgnore]
    public List<Team> Teams { get; set; } = [];

    #endregion
}
