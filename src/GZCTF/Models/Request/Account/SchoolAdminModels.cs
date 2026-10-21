using System.ComponentModel.DataAnnotations;
using GZCTF.Models.Data;

namespace GZCTF.Models.Request.Account;

/// <summary>
/// 管理端：学校完整信息（含登录配置、邀请码、报名分组）
/// </summary>
public class SchoolDetailModel
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string AuthType { get; set; } = "email";

    public string? CasLoginUrl { get; set; }

    public string? CasValidateUrl { get; set; }

    public string EmailDomains { get; set; } = string.Empty;

    public string? IconUrl { get; set; }

    public string? InviteCode { get; set; }

    public bool Enabled { get; set; }

    public int Order { get; set; }

    public string FreshmanGrades { get; set; } = string.Empty;

    public string SeniorGrades { get; set; } = string.Empty;

    public int? FreshmanDivisionId { get; set; }

    public int? SeniorDivisionId { get; set; }

    /// <summary>
    /// 已绑定本校的学生数
    /// </summary>
    public int UserCount { get; set; }

    /// <summary>
    /// 报名邀请码是否已设置
    /// </summary>
    public bool HasInviteCode { get; set; }

    public static SchoolDetailModel FromSchool(School s, int userCount) => new()
    {
        Id = s.Id,
        Name = s.Name,
        Slug = s.Slug,
        AuthType = s.AuthType switch
        {
            SchoolAuthType.Cas => "cas",
            SchoolAuthType.InviteOnly => "invite",
            _ => "email"
        },
        CasLoginUrl = s.CasLoginUrl,
        CasValidateUrl = s.CasValidateUrl,
        EmailDomains = s.EmailDomains,
        IconUrl = s.IconUrl,
        InviteCode = s.InviteCode,
        HasInviteCode = !string.IsNullOrWhiteSpace(s.InviteCode),
        Enabled = s.Enabled,
        Order = s.Order,
        FreshmanGrades = s.FreshmanGrades,
        SeniorGrades = s.SeniorGrades,
        FreshmanDivisionId = s.FreshmanDivisionId,
        SeniorDivisionId = s.SeniorDivisionId,
        UserCount = userCount
    };
}

/// <summary>
/// 管理端：新增 / 编辑学校
/// </summary>
public class SchoolEditModel
{
    [Required(ErrorMessageResourceName = nameof(Resources.Program.Model_ContentRequired),
        ErrorMessageResourceType = typeof(Resources.Program))]
    [MaxLength(Limits.MaxSchoolNameLength)]
    public string Name { get; set; } = string.Empty;

    /// <summary>认证模式：cas / email</summary>
    public string AuthType { get; set; } = "email";

    [MaxLength(Limits.MaxUrlLength)]
    public string? CasLoginUrl { get; set; }

    [MaxLength(Limits.MaxUrlLength)]
    public string? CasValidateUrl { get; set; }

    [MaxLength(Limits.MaxEmailDomainListLength)]
    public string? EmailDomains { get; set; }

    [MaxLength(Limits.MaxUrlLength)]
    public string? IconUrl { get; set; }

    [MaxLength(Limits.SchoolInviteCodeLength)]
    public string? InviteCode { get; set; }

    public bool Enabled { get; set; } = true;

    public int Order { get; set; }

    [MaxLength(Limits.MaxGradeListLength)]
    public string? FreshmanGrades { get; set; }

    [MaxLength(Limits.MaxGradeListLength)]
    public string? SeniorGrades { get; set; }

    public int? FreshmanDivisionId { get; set; }

    public int? SeniorDivisionId { get; set; }
}

/// <summary>
/// 管理端：学校设置页聚合数据
/// </summary>
public class SchoolSettingsModel
{
    /// <summary>学校列表</summary>
    public SchoolDetailModel[] Schools { get; set; } = [];

    /// <summary>可选赛区（跨赛事汇总，供分组下拉选择）</summary>
    public SchoolDivisionOptionModel[] Divisions { get; set; } = [];

    /// <summary>可选年级（与注册表单保持一致）</summary>
    public string[] Grades { get; set; } = [];
}

/// <summary>
/// 赛区选项（含所属赛事名称，便于分组下拉区分）
/// </summary>
public class SchoolDivisionOptionModel
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int GameId { get; set; }

    public string GameTitle { get; set; } = string.Empty;
}

/// <summary>
/// 管理端：批量设置报名邀请码
/// </summary>
public class SchoolInviteBatchModel
{
    /// <summary>学校标识（即学校名称）</summary>
    [Required]
    public string Slug { get; set; } = string.Empty;

    /// <summary>邀请码，留空表示该校免邀请码绑定</summary>
    [MaxLength(Limits.SchoolInviteCodeLength)]
    public string? InviteCode { get; set; }
}

/// <summary>
/// 分组解析结果
/// </summary>
public class SchoolResolveResult
{
    public bool Resolved { get; set; }

    public string? SchoolName { get; set; }

    /// <summary>新生组 / 老生组</summary>
    public string? GroupName { get; set; }

    public string? Grade { get; set; }

    public int? DivisionId { get; set; }

    public string? Reason { get; set; }
}

/// <summary>
/// 报名数据大屏：各校报名人数汇总
/// </summary>
public class SchoolStatsModel
{
    /// <summary>报名总人数（已绑定学校的学生）</summary>
    public int Total { get; set; }

    /// <summary>学籍审核通过总人数</summary>
    public int ApprovedTotal { get; set; }

    /// <summary>数据生成时间（UTC）</summary>
    public DateTimeOffset GeneratedAtUtc { get; set; }

    /// <summary>各校明细，按报名人数降序</summary>
    public List<SchoolStatItemModel> Schools { get; set; } = [];
}

/// <summary>
/// 单个学校的报名统计
/// </summary>
public class SchoolStatItemModel
{
    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    /// <summary>报名人数（绑定该校的学生总数）</summary>
    public int UserCount { get; set; }

    /// <summary>学籍审核通过人数</summary>
    public int ApprovedCount { get; set; }

    /// <summary>待审核人数</summary>
    public int PendingCount { get; set; }

    /// <summary>是否启用</summary>
    public bool Enabled { get; set; }
}
