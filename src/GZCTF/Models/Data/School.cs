using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using GZCTF.Models.Internal;

namespace GZCTF.Models.Data;

/// <summary>
/// 学校（参赛单位）。
///
/// 用途：
///   1. 学校统一身份认证（登录）—— 每所学校可独立配置 CAS / 邮箱认证方式；
///   2. 选手归属 —— 注册或绑定后记录学生所在学校，用于学籍审核与报名分组；
///   3. 报名分组 —— 按年级把本校学生划分到「新生组」「老生组」，
///      每组可指定主办赛道下的一个赛区，报名时自动按年级分配赛区。
/// </summary>
public class School
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// 学校名称，如「中国人民警察大学」
    /// </summary>
    [Required]
    [MaxLength(Limits.MaxSchoolNameLength)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 学校标识（即学校名称），全局唯一，用于 /api/account/sso/login/{slug}
    /// </summary>
    [Required]
    [MaxLength(Limits.MaxSchoolSlugLength)]
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// 认证模式：cas 走学校统一身份认证跳转，email 走站内邮箱验证码
    /// </summary>
    public SchoolAuthType AuthType { get; set; } = SchoolAuthType.Email;

    /// <summary>
    /// CAS 登录地址（AuthType = Cas 时必填）
    /// </summary>
    [MaxLength(Limits.MaxUrlLength)]
    public string? CasLoginUrl { get; set; }

    /// <summary>
    /// CAS 票据校验地址（AuthType = Cas 时必填）
    /// </summary>
    [MaxLength(Limits.MaxUrlLength)]
    public string? CasValidateUrl { get; set; }

    /// <summary>
    /// 邮箱后缀，多个以逗号分隔，如 cppu.edu.cn,stu.cppu.edu.cn
    /// 用于判定邮箱是否归属本校（AuthType = Email 时必填）
    /// </summary>
    [MaxLength(Limits.MaxEmailDomainListLength)]
    public string EmailDomains { get; set; } = string.Empty;

    /// <summary>
    /// 学校图标 URL，可为空
    /// </summary>
    [MaxLength(Limits.MaxUrlLength)]
    public string? IconUrl { get; set; }

    /// <summary>
    /// 报名邀请码。非快速登录的用户需要输入邀请码才能绑定本校；
    /// 留空表示免邀请码绑定。
    /// 允许标准 UUID（36 位带连字符），故使用独立的长度上限。
    /// </summary>
    [MaxLength(Limits.SchoolInviteCodeLength)]
    public string? InviteCode { get; set; }

    /// <summary>
    /// 是否启用。停用后不出现在登录页与绑定列表，但不影响已绑定用户的归属。
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 展示排序，数值小的靠前
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// 划分为「新生」的年级，CSV 存储，如「大一,大二」。
    /// 未列入本字段也未列入 <see cref="SeniorGrades"/> 的年级不参与自动分组。
    /// </summary>
    [MaxLength(Limits.MaxGradeListLength)]
    public string FreshmanGrades { get; set; } = string.Empty;

    /// <summary>
    /// 划分为「老生」的年级，CSV 存储，如「大三,大四,研一,研二,研三,博士」。
    /// </summary>
    [MaxLength(Limits.MaxGradeListLength)]
    public string SeniorGrades { get; set; } = string.Empty;

    /// <summary>
    /// 新生组对应的赛区 Id。为空表示不自动分配。
    /// </summary>
    public int? FreshmanDivisionId { get; set; }

    /// <summary>
    /// 老生组对应的赛区 Id。为空表示不自动分配。
    /// </summary>
    public int? SeniorDivisionId { get; set; }

    #region Db Relationship

    /// <summary>
    /// 新生组赛区
    /// </summary>
    [JsonIgnore]
    public Division? FreshmanDivision { get; set; }

    /// <summary>
    /// 老生组赛区
    /// </summary>
    [JsonIgnore]
    public Division? SeniorDivision { get; set; }

    #endregion

    #region Not Mapped

    /// <summary>
    /// 邮箱后缀列表
    /// </summary>
    [NotMapped]
    [JsonIgnore]
    public string[] EmailDomainList =>
        string.IsNullOrWhiteSpace(EmailDomains)
            ? []
            : EmailDomains.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// 新生年级列表
    /// </summary>
    [NotMapped]
    [JsonIgnore]
    public string[] FreshmanGradeList => SplitGrades(FreshmanGrades);

    /// <summary>
    /// 老生年级列表
    /// </summary>
    [NotMapped]
    [JsonIgnore]
    public string[] SeniorGradeList => SplitGrades(SeniorGrades);

    private static string[] SplitGrades(string raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? []
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// 判定邮箱是否归属本校
    /// </summary>
    public bool MatchesEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        var at = email.LastIndexOf('@');
        if (at < 0 || at == email.Length - 1)
            return false;

        var domain = email[(at + 1)..].Trim().ToLowerInvariant();
        return EmailDomainList.Any(d => string.Equals(d, domain, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 按年级取应属的赛区 Id。年级不属于任何一组时返回 null。
    /// </summary>
    public int? ResolveDivisionId(string? grade)
    {
        if (string.IsNullOrWhiteSpace(grade))
            return null;

        var g = grade.Trim();

        if (FreshmanGradeList.Contains(g, StringComparer.OrdinalIgnoreCase))
            return FreshmanDivisionId;

        if (SeniorGradeList.Contains(g, StringComparer.OrdinalIgnoreCase))
            return SeniorDivisionId;

        return null;
    }

    /// <summary>
    /// 按年级返回分组名称，用于界面展示
    /// </summary>
    public string? ResolveGroupName(string? grade)
    {
        if (string.IsNullOrWhiteSpace(grade))
            return null;

        var g = grade.Trim();

        if (FreshmanGradeList.Contains(g, StringComparer.OrdinalIgnoreCase))
            return "新生组";

        if (SeniorGradeList.Contains(g, StringComparer.OrdinalIgnoreCase))
            return "老生组";

        return null;
    }

    #endregion

    /// <summary>
    /// 转换为登录页 / 绑定用的精简模型
    /// </summary>
    public ClientSsoSchool ToClientModel() => new()
    {
        Name = Name,
        Slug = Slug,
        Mode = AuthType switch
        {
            SchoolAuthType.Cas => SsoAuthMode.Cas,
            SchoolAuthType.InviteOnly => SsoAuthMode.Invite,
            _ => SsoAuthMode.Email
        },
        LoginUrl = CasLoginUrl ?? string.Empty,
        ValidateUrl = CasValidateUrl ?? string.Empty,
        Icon = IconUrl,
        EmailSuffixes = [.. EmailDomainList]
    };

    /// <summary>
    /// 从旧的配置模型构造实体（用于把 CSV 配置迁移进数据库）
    /// </summary>
    public static School FromClientModel(ClientSsoSchool model) => new()
    {
        Name = model.Name,
        Slug = model.Slug,
        AuthType = model.Mode == SsoAuthMode.Cas ? SchoolAuthType.Cas : SchoolAuthType.Email,
        CasLoginUrl = model.LoginUrl,
        CasValidateUrl = model.ValidateUrl,
        IconUrl = model.Icon,
        EmailDomains = string.Join(',', model.EmailSuffixes),
        Enabled = true
    };
}
