using GZCTF.Middlewares;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Request.Account;
using GZCTF.Utils;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace GZCTF.Controllers;

/// <summary>
/// 学校管理接口（管理端专用）
///
/// 学校是参赛单位，承载三件事：
///   1. 登录方式（CAS 统一认证 / 邮箱验证码）与报名邀请码；
///   2. 学生的学校归属（注册或绑定后写入 UserInfo.School）；
///   3. 主办赛道的报名分组：每所学校把年级划分为「新生组」「老生组」，
///      每组绑定一个赛区，报名时按学生年级自动分配。
///
/// 除只读选项接口外，全部要求系统管理员权限。
/// </summary>
[ApiController]
[Route("api/[controller]")]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status403Forbidden)]
public class SchoolController(
    AppDbContext dbContext,
    UserManager<UserInfo> userManager,
    ILogger<SchoolController> logger,
    IStringLocalizer<Program> localizer) : ControllerBase
{
    /// <summary>
    /// 报名数据大屏：各校报名人数统计
    /// </summary>
    [RequireAdmin]
    [HttpGet("Stats")]
    [ProducesResponseType(typeof(SchoolStatsModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStats(CancellationToken token)
    {
        // 一次分组查询拿到各校报名数 / 通过数 / 待审数
        var stats = await dbContext.Users
            .Where(u => u.School != null && u.School != string.Empty)
            .GroupBy(u => u.School!)
            .Select(g => new
            {
                Slug = g.Key,
                Total = g.Count(),
                Approved = g.Count(u => u.VerifyStatus == VerifyStatus.Approved),
                Pending = g.Count(u => u.VerifyStatus == VerifyStatus.Pending)
            })
            .ToListAsync(token);

        var bySlug = stats.ToDictionary(s => s.Slug, s => s, StringComparer.OrdinalIgnoreCase);

        // 列出全部学校（含已停用的，如「社会」组），保证大屏总数与真实报名数一致；
        // 未启用但有报名的学校会带 Enabled=false，由前端标注。
        var schools = await dbContext.Schools.AsNoTracking()
            .Select(s => new { s.Name, s.Slug, s.Enabled })
            .ToListAsync(token);

        var items = schools
            .Select(s =>
            {
                bySlug.TryGetValue(s.Slug, out var st);
                return new SchoolStatItemModel
                {
                    Name = s.Name,
                    Slug = s.Slug,
                    UserCount = st?.Total ?? 0,
                    ApprovedCount = st?.Approved ?? 0,
                    PendingCount = st?.Pending ?? 0,
                    Enabled = s.Enabled
                };
            })
            .OrderByDescending(i => i.UserCount)
            .ThenBy(i => i.Name)
            .ToList();

        return Ok(new SchoolStatsModel
        {
            Total = items.Sum(i => i.UserCount),
            ApprovedTotal = items.Sum(i => i.ApprovedCount),
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Schools = items
        });
    }

    /// <summary>
    /// 学校设置页聚合数据（学校列表 + 可选赛区 + 年级选项）
    /// </summary>
    [RequireAdmin]
    [HttpGet("settings")]
    [ProducesResponseType(typeof(SchoolSettingsModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSettings(CancellationToken token)
    {
        var schools = await dbContext.Schools
            .OrderBy(s => s.Order).ThenBy(s => s.Id)
            .ToArrayAsync(token);

        // 统计各校已绑定学生数
        var slugs = schools.Select(s => s.Slug).ToArray();
        var counts = await dbContext.Users
            .Where(u => u.School != null && slugs.Contains(u.School))
            .GroupBy(u => u.School!)
            .Select(g => new { Slug = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Slug, g => g.Count, token);

        var divisions = await dbContext.Divisions
            .Include(d => d.Game)
            .OrderBy(d => d.GameId).ThenBy(d => d.Id)
            .Select(d => new SchoolDivisionOptionModel
            {
                Id = d.Id,
                Name = d.Name,
                GameId = d.GameId,
                GameTitle = d.Game != null ? d.Game.Title : string.Empty
            })
            .ToArrayAsync(token);

        return Ok(new SchoolSettingsModel
        {
            Schools = [.. schools.Select(s => SchoolDetailModel.FromSchool(s, counts.GetValueOrDefault(s.Slug)))],
            Divisions = divisions,
            Grades = GradeOptions
        });
    }

    /// <summary>
    /// 新增学校
    /// </summary>
    [RequireAdmin]
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] SchoolEditModel model, CancellationToken token)
    {
        var name = model.Name.Trim();

        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new RequestResponse("学校名称不能为空"));

        if (await dbContext.Schools.AnyAsync(s => s.Name == name, token))
            return BadRequest(new RequestResponse($"学校「{name}」已存在"));

        // 短名不再由管理员填写：统一以学校全称作为标识，保证各处一致。
        var slug = NormalizeSlug(null, name);

        if (string.IsNullOrWhiteSpace(slug))
            return BadRequest(new RequestResponse("无法生成有效的学校标识，请检查学校名称"));

        if (await dbContext.Schools.AnyAsync(s => s.Slug == slug, token))
            return BadRequest(new RequestResponse($"学校「{name}」的标识已被占用"));

        var school = new School
        {
            Name = name,
            Slug = slug,
            AuthType = ParseAuthType(model.AuthType),
            CasLoginUrl = model.CasLoginUrl?.Trim(),
            CasValidateUrl = model.CasValidateUrl?.Trim(),
            EmailDomains = CleanRequired(model.EmailDomains),
            IconUrl = model.IconUrl?.Trim(),
            InviteCode = Clean(model.InviteCode),
            Enabled = model.Enabled,
            Order = model.Order,
            FreshmanGrades = CleanGrades(model.FreshmanGrades),
            SeniorGrades = CleanGrades(model.SeniorGrades),
            FreshmanDivisionId = model.FreshmanDivisionId,
            SeniorDivisionId = model.SeniorDivisionId
        };

        dbContext.Schools.Add(school);
        await dbContext.SaveChangesAsync(token);

        logger.SystemLog($"新增学校：{school.Name}（{school.Slug}）", TaskStatus.Success, LogLevel.Information);

        return Ok(new { school.Id, school.Slug });
    }

    /// <summary>
    /// 编辑学校
    /// </summary>
    [RequireAdmin]
    [HttpPut("{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int id, [FromBody] SchoolEditModel model, CancellationToken token)
    {
        var school = await dbContext.Schools.FirstOrDefaultAsync(s => s.Id == id, token);

        if (school is null)
            return NotFound(new RequestResponse("学校不存在", StatusCodes.Status404NotFound));

        var newName = model.Name.Trim();

        if (string.IsNullOrWhiteSpace(newName))
            return BadRequest(new RequestResponse("学校名称不能为空"));

        if (await dbContext.Schools.AnyAsync(s => s.Name == newName && s.Id != id, token))
            return BadRequest(new RequestResponse($"学校「{newName}」已存在"));

        // 短名跟随学校全称自动同步，管理员无需（也无法）单独指定。
        var newSlug = NormalizeSlug(null, newName);

        if (string.IsNullOrWhiteSpace(newSlug))
            return BadRequest(new RequestResponse("无法生成有效的学校标识，请检查学校名称"));

        if (newSlug != school.Slug &&
            await dbContext.Schools.AnyAsync(s => s.Slug == newSlug && s.Id != id, token))
            return BadRequest(new RequestResponse($"学校「{newName}」的标识已被占用"));

        school.Slug = newSlug;
        school.Name = newName;
        school.AuthType = ParseAuthType(model.AuthType);
        school.CasLoginUrl = model.CasLoginUrl?.Trim();
        school.CasValidateUrl = model.CasValidateUrl?.Trim();
        school.EmailDomains = CleanRequired(model.EmailDomains);
        school.IconUrl = model.IconUrl?.Trim();
        school.InviteCode = Clean(model.InviteCode);
        school.Enabled = model.Enabled;
        school.Order = model.Order;
        school.FreshmanGrades = CleanGrades(model.FreshmanGrades);
        school.SeniorGrades = CleanGrades(model.SeniorGrades);
        school.FreshmanDivisionId = model.FreshmanDivisionId;
        school.SeniorDivisionId = model.SeniorDivisionId;

        await dbContext.SaveChangesAsync(token);

        logger.SystemLog($"更新学校：{school.Name}（{school.Slug}）", TaskStatus.Success, LogLevel.Information);

        return Ok();
    }

    /// <summary>
    /// 删除学校
    /// </summary>
    /// <remarks>
    /// 已有学生绑定该校时不允许直接删除，避免产生孤儿归属。
    /// </remarks>
    [RequireAdmin]
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Delete(int id, CancellationToken token)
    {
        var school = await dbContext.Schools.FirstOrDefaultAsync(s => s.Id == id, token);

        if (school is null)
            return NotFound(new RequestResponse("学校不存在", StatusCodes.Status404NotFound));

        var bound = await dbContext.Users.CountAsync(u => u.School == school.Slug, token);

        if (bound > 0)
            return BadRequest(new RequestResponse(
                $"该校已有 {bound} 名学生绑定，请先停用或转移学生归属后再删除"));

        dbContext.Schools.Remove(school);
        await dbContext.SaveChangesAsync(token);

        logger.SystemLog($"删除学校：{school.Name}（{school.Slug}）", TaskStatus.Success, LogLevel.Information);

        return Ok();
    }

    /// <summary>
    /// 批量设置各校报名邀请码
    /// </summary>
    [RequireAdmin]
    [HttpPost("inviteCodes")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetInviteCodes([FromBody] SchoolInviteBatchModel[] models, CancellationToken token)
    {
        var schools = await dbContext.Schools.ToArrayAsync(token);
        var updated = 0;

        foreach (var model in models)
        {
            var school = schools.FirstOrDefault(s => s.Slug == model.Slug);

            if (school is null)
                continue;

            school.InviteCode = Clean(model.InviteCode);
            updated++;
        }

        await dbContext.SaveChangesAsync(token);

        logger.SystemLog($"批量更新 {updated} 所学校的报名邀请码", TaskStatus.Success, LogLevel.Information);

        return Ok(new { updated });
    }

    /// <summary>
    /// 按学生年级与所属学校解析应报名的赛区
    /// </summary>
    /// <remarks>
    /// 供报名流程内部调用，也便于管理端预览分组规则。
    /// 普通学员可查询自己的分组（用于报名时自动匹配赛区），
    /// 管理员可传 slug / userId 预览任意组合。
    /// </remarks>
    [RequireUser]
    [HttpGet("resolve")]
    [ProducesResponseType(typeof(SchoolResolveResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Resolve(
        [FromQuery] string? slug,
        [FromQuery] string? userId,
        CancellationToken token)
    {
        var current = await userManager.GetUserAsync(User);
        var isAdmin = current is not null && current.Role >= Role.Admin;

        UserInfo? user;

        if (isAdmin)
        {
            // 管理员可指定 userId 预览；不传则查自己
            user = Guid.TryParse(userId, out var uid)
                ? await userManager.FindByIdAsync(uid.ToString())
                : current;
        }
        else
        {
            // 普通学员只能查自己的分组
            user = current;
        }

        var school = await dbContext.Schools
            .FirstOrDefaultAsync(s => s.Slug == (slug ?? user!.School), token);

        if (school is null)
            return Ok(new SchoolResolveResult { Resolved = false, Reason = "未找到学校" });

        var grade = user?.Grade;
        var group = school.ResolveGroupName(grade);
        var divisionId = school.ResolveDivisionId(grade);

        return Ok(new SchoolResolveResult
        {
            Resolved = divisionId.HasValue,
            SchoolName = school.Name,
            GroupName = group,
            Grade = grade,
            DivisionId = divisionId,
            Reason = divisionId.HasValue ? null : "该年级未配置报名分组"
        });
    }

    #region Helpers

    /// <summary>
    /// 与注册表单保持一致的年级选项
    /// </summary>
    private static readonly string[] GradeOptions =
    [
        "大一", "大二", "大三", "大四", "大五",
        "研一", "研二", "研三", "博士", "教职工", "其他"
    ];

    private static SchoolAuthType ParseAuthType(string? raw) =>
        raw?.Trim().ToLowerInvariant() switch
        {
            "cas" => SchoolAuthType.Cas,
            "invite" or "inviteonly" or "code" => SchoolAuthType.InviteOnly,
            _ => SchoolAuthType.Email
        };

    private static string? Clean(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();

    /// <summary>
    /// 规范化「非空列」的字符串字段：数据库列声明为 NOT NULL，空值须落成空串
    /// </summary>
    private static string CleanRequired(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? string.Empty : raw.Trim();

    /// <summary>
    /// 年级列表规范化：统一以逗号连接并去重
    /// </summary>
    private static string CleanGrades(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var grades = raw
            .Split([',', '，', ';', '；', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return string.Join(',', grades);
    }

    /// <summary>
    /// 生成学校标识：由学校名称推导（保留字母数字与中文）
    /// </summary>
    internal static string NormalizeSlug(string? preferred, string name)
    {
        var source = string.IsNullOrWhiteSpace(preferred) ? name : preferred;

        var chars = source
            .ToLowerInvariant()
            .Where(c => char.IsLetterOrDigit(c) || c is '-' or '_')
            .ToArray();

        var slug = new string(chars).Trim('-', '_');

        if (slug.Length > Limits.MaxSchoolSlugLength)
            slug = slug[..Limits.MaxSchoolSlugLength];

        return slug;
    }

    #endregion
}
