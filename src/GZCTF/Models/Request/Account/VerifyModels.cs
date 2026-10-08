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
