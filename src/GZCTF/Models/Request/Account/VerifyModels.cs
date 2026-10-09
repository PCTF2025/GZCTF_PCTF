using System.ComponentModel.DataAnnotations;

namespace GZCTF.Models.Request.Account;

/// <summary>
/// 学校选项（审核界面筛选用）
/// </summary>
/// <param name="Slug">学校短名</param>
/// <param name="Name">学校名称</param>
public record VerifySchoolOption(string Slug, string Name);

/// <summary>
/// 待审核学生条目
/// </summary>
public class VerifyItemModel
{
    public Guid UserId { get; set; }
    public string? UserName { get; set; }
    public string? Email { get; set; }
    /// <summary>真实姓名</summary>
    public string? RealName { get; set; }
    /// <summary>学号</summary>
    public string? StdNumber { get; set; }
    /// <summary>年级</summary>
    public string? Grade { get; set; }
    /// <summary>学校短名</summary>
    public string? School { get; set; }
    /// <summary>学校名称</summary>
    public string? SchoolName { get; set; }
    /// <summary>学校绑定来源</summary>
    public SchoolBindSource SchoolSource { get; set; }
    /// <summary>审核状态</summary>
    public VerifyStatus Status { get; set; }
    /// <summary>审核备注</summary>
    public string? Note { get; set; }
    /// <summary>注册时间</summary>
    public DateTimeOffset RegisterTimeUtc { get; set; }
    /// <summary>审核时间</summary>
    public DateTimeOffset? VerifiedAtUtc { get; set; }
}

/// <summary>
/// 学籍审核列表
/// </summary>
public class VerifyListModel
{
    /// <summary>符合条件的总数</summary>
    public int Total { get; set; }
    /// <summary>当前页数据</summary>
    public List<VerifyItemModel> Items { get; set; } = [];
    /// <summary>当前审核人可筛选的学校</summary>
    public List<VerifySchoolOption> AvailableSchools { get; set; } = [];
    /// <summary>是否为系统管理员（可审核全部学校）</summary>
    public bool ManageAll { get; set; }
}

/// <summary>
/// 审核操作请求
/// </summary>
public class VerifyActionModel
{
    /// <summary>目标学生 UserId</summary>
    [Required]
    public Guid UserId { get; set; }

    /// <summary>审核备注 / 驳回原因</summary>
    [MaxLength(Limits.MaxUserDataLength)]
    public string? Note { get; set; }
}

/// <summary>
/// 批量审核请求
/// </summary>
public class VerifyBatchModel
{
    /// <summary>目标学生 UserId 列表</summary>
    [Required]
    public List<Guid>? UserIds { get; set; }
}

/// <summary>
/// 当前用户的审核权限
/// </summary>
public class VerifyPermissionModel
{
    /// <summary>是否具备审核权限</summary>
    public bool CanVerify { get; set; }
    /// <summary>是否可审核全部学校</summary>
    public bool ManageAll { get; set; }
    /// <summary>可审核的学校列表</summary>
    public List<VerifySchoolOption> Schools { get; set; } = [];
}

/// <summary>
/// 发送邮箱验证码请求
/// </summary>
public class SchoolCodeSendModel
{
    /// <summary>学校短名</summary>
    [Required]
    [MaxLength(Limits.MaxUserDataLength)]
    public string SchoolSlug { get; set; } = string.Empty;

    /// <summary>学校邮箱</summary>
    [Required]
    [EmailAddress]
    [MaxLength(Limits.MaxEmailLength)]
    public string Email { get; set; } = string.Empty;
}

/// <summary>
/// 验证码校验 / 一键登录请求
/// </summary>
public class SchoolCodeLoginModel
{
    /// <summary>学校短名</summary>
    [Required]
    [MaxLength(Limits.MaxUserDataLength)]
    public string SchoolSlug { get; set; } = string.Empty;

    /// <summary>学校邮箱</summary>
    [Required]
    [EmailAddress]
    [MaxLength(Limits.MaxEmailLength)]
    public string Email { get; set; } = string.Empty;

    /// <summary>6 位数字验证码</summary>
    [Required]
    [MaxLength(16)]
    public string Code { get; set; } = string.Empty;

    /// <summary>真实姓名（首次注册时必填）</summary>
    [MaxLength(Limits.MaxUserDataLength)]
    public string? RealName { get; set; }

    /// <summary>学号（首次注册时必填）</summary>
    [MaxLength(Limits.MaxStdNumberLength)]
    public string? StdNumber { get; set; }

    /// <summary>年级</summary>
    [MaxLength(Limits.MaxUserDataLength)]
    public string? Grade { get; set; }
}

/// <summary>
/// 验证码校验结果
/// </summary>
public class SchoolCodeCheckResult
{
    /// <summary>该邮箱是否已注册</summary>
    public bool Registered { get; set; }

    /// <summary>是否仍需补充学籍信息（姓名 / 学号）</summary>
    public bool NeedProfile { get; set; }

    /// <summary>已注册用户的用户名</summary>
    public string? UserName { get; set; }

    /// <summary>已填写的真实姓名</summary>
    public string? RealName { get; set; }

    /// <summary>已填写的学号</summary>
    public string? StdNumber { get; set; }

    /// <summary>已填写的年级</summary>
    public string? Grade { get; set; }
}

/// <summary>
/// 一键注册 / 登录结果
/// </summary>
public class SchoolCodeLoginResult
{
    /// <summary>本次是否新创建账号</summary>
    public bool Created { get; set; }

    /// <summary>登录后的用户名</summary>
    public string? UserName { get; set; }

    /// <summary>绑定的学校短名</summary>
    public string? School { get; set; }

    /// <summary>绑定的学校名称</summary>
    public string? SchoolName { get; set; }

    /// <summary>学籍审核状态</summary>
    public VerifyStatus VerifyStatus { get; set; }
}
