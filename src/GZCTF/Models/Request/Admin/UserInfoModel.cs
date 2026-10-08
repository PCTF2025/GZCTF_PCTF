using System.Net;

namespace GZCTF.Models.Request.Admin;

/// <summary>
/// User information (Admin)
/// </summary>
public class UserInfoModel
{
    /// <summary>
    /// User ID
    /// </summary>
    public Guid? Id { get; set; }

    /// <summary>
    /// Username
    /// </summary>
    public string? UserName { get; set; }

    /// <summary>
    /// Real name
    /// </summary>
    public string? RealName { get; set; }

    /// <summary>
    /// Student number
    /// </summary>
    public string? StdNumber { get; set; }

    /// <summary>
    /// Contact phone number
    /// </summary>
    public string? Phone { get; set; }

    /// <summary>
    /// Bio
    /// </summary>
    public string? Bio { get; set; }

    /// <summary>
    /// Registration time
    /// </summary>
    public DateTimeOffset RegisterTimeUtc { get; set; }

    /// <summary>
    /// Last visit time
    /// </summary>
    public DateTimeOffset LastVisitedUtc { get; set; }

    /// <summary>
    /// Last visit IP
    /// </summary>
    public IPAddress IP { get; set; } = IPAddress.Any;

    /// <summary>
    /// Email
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Avatar URL
    /// </summary>
    public string? Avatar { get; set; }

    /// <summary>
    /// User role
    /// </summary>
    public Role? Role { get; set; }

    /// <summary>
    /// Is email confirmed (can log in)
    /// </summary>
    public bool? EmailConfirmed { get; set; }

    /// <summary>
    /// 学校短名
    /// </summary>
    public string? School { get; set; }

    /// <summary>
    /// 学校绑定来源
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
    /// 该用户负责审核的学校短名（逗号分隔），非空即为学校管理员
    /// </summary>
    public string? ManagedSchools { get; set; }

    internal static UserInfoModel FromUserInfo(UserInfo user) =>
        new()
        {
            Id = user.Id,
            IP = user.IP,
            Bio = user.Bio,
            Role = user.Role,
            Email = user.Email,
            Phone = user.PhoneNumber,
            Avatar = user.AvatarUrl,
            RealName = user.RealName,
            UserName = user.UserName,
            StdNumber = user.StdNumber,
            LastVisitedUtc = user.LastVisitedUtc,
            RegisterTimeUtc = user.RegisterTimeUtc,
            EmailConfirmed = user.EmailConfirmed,
            School = string.IsNullOrWhiteSpace(user.School) ? null : user.School,
            SchoolSource = user.SchoolSource,
            Grade = string.IsNullOrWhiteSpace(user.Grade) ? null : user.Grade,
            VerifyStatus = user.VerifyStatus,
            VerifyNote = string.IsNullOrWhiteSpace(user.VerifyNote) ? null : user.VerifyNote,
            ManagedSchools = string.IsNullOrWhiteSpace(user.ManagedSchools) ? null : user.ManagedSchools
        };
}
