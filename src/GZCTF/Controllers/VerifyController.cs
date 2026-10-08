using System.Net.Mime;
using GZCTF.Middlewares;
using GZCTF.Models;
using GZCTF.Models.Internal;
using GZCTF.Models.Request.Account;
using GZCTF.Utils;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GZCTF.Controllers;

/// <summary>
/// 学籍信息审核：由各学校管理员审核本校学生的学校 / 年级 / 学号 / 姓名。
/// 审核通过后方可报名主办赛道。
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces(MediaTypeNames.Application.Json)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status403Forbidden)]
public class VerifyController(
    UserManager<UserInfo> userManager,
    AppDbContext dbContext,
    IOptionsSnapshot<SsoConfig> ssoConfig,
    ILogger<VerifyController> logger) : ControllerBase
{
    /// <summary>
    /// 待审核学籍列表
    /// </summary>
    /// <remarks>
    /// 学校管理员只能看到自己负责学校的学生；系统管理员可通过 school 参数筛选任意学校。
    /// </remarks>
    /// <param name="school">按学校短名筛选（可选）</param>
    /// <param name="status">按审核状态筛选（可选，默认待审核）</param>
    /// <param name="count">分页数量</param>
    /// <param name="skip">跳过条数</param>
    /// <param name="hint">按用户名 / 姓名 / 学号模糊搜索</param>
    /// <param name="token"></param>
    [HttpGet("List")]
    [RequireUser]
    [ProducesResponseType(typeof(VerifyListModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? school,
        [FromQuery] VerifyStatus? status,
        [FromQuery] int count = 50,
        [FromQuery] int skip = 0,
        [FromQuery] string? hint = null,
        CancellationToken token = default)
    {
        var viewer = await userManager.GetUserAsync(User);
        if (viewer is null)
            return Unauthorized(new RequestResponse("请先登录"));

        if (!viewer.IsSchoolAdmin && viewer.Role < Role.Admin)
            return Forbidden("你没有学籍审核权限");

        var query = dbContext.Users.AsNoTracking().AsQueryable();

        // 学校过滤：管理员可按参数筛选；学校管理员仅能看到自己负责的学校
        if (viewer.Role >= Role.Admin)
        {
            if (!string.IsNullOrWhiteSpace(school))
                query = query.Where(u => u.School == school);
        }
        else
        {
            var managed = viewer.ManagedSchoolList;
            if (!string.IsNullOrWhiteSpace(school))
            {
                if (!viewer.CanManageSchool(school))
                    return Forbidden($"你无权审核 {school} 的学生");

                query = query.Where(u => u.School == school);
            }
            else
            {
                query = query.Where(u => managed.Contains(u.School));
            }
        }

        var targetStatus = status ?? VerifyStatus.Pending;
        query = query.Where(u => u.VerifyStatus == targetStatus);

        if (!string.IsNullOrWhiteSpace(hint))
        {
            var h = hint.Trim();
            query = query.Where(u =>
                (u.UserName != null && u.UserName.Contains(h)) ||
                u.RealName.Contains(h) ||
                u.StdNumber.Contains(h));
        }

        var total = await query.CountAsync(token);

        var items = await query
            .OrderBy(u => u.RegisterTimeUtc)
            .Skip(Math.Max(0, skip))
            .Take(Math.Clamp(count, 1, 200))
            .Select(u => new VerifyItemModel
            {
                UserId = u.Id,
                UserName = u.UserName,
                Email = u.Email,
                RealName = u.RealName,
                StdNumber = u.StdNumber,
                Grade = u.Grade,
                School = u.School,
                SchoolSource = u.SchoolSource,
                Status = u.VerifyStatus,
                Note = u.VerifyNote,
                RegisterTimeUtc = u.RegisterTimeUtc,
                VerifiedAtUtc = u.VerifiedAtUtc
            })
            .ToListAsync(token);

        // 附上学校名称，便于前端直接展示
        var schoolNames = ClientSsoConfig.ParseSchools(ssoConfig.Value.Schools)
            .ToDictionary(s => s.Slug, s => s.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
            item.SchoolName = schoolNames.GetValueOrDefault(item.School ?? string.Empty) ?? item.School;

        // 学校管理员可在界面上的学校下拉中看到自己负责的学校
        var availableSchools = viewer.Role >= Role.Admin
            ? schoolNames.Select(kv => new VerifySchoolOption(kv.Key, kv.Value)).ToList()
            : viewer.ManagedSchoolList
                .Select(slug => new VerifySchoolOption(slug, schoolNames.GetValueOrDefault(slug) ?? slug))
                .ToList();

        return Ok(new VerifyListModel
        {
            Total = total,
            Items = items,
            AvailableSchools = availableSchools,
            ManageAll = viewer.Role >= Role.Admin
        });
    }

    /// <summary>
    /// 审核通过
    /// </summary>
    /// <param name="model">审核请求</param>
    /// <param name="token"></param>
    [HttpPost("Approve")]
    [RequireUser]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Approve([FromBody] VerifyActionModel model, CancellationToken token = default)
    {
        var (viewer, target, error) = await ResolveTarget(model.UserId, token);
        if (error is not null) return error;

        target!.VerifyStatus = VerifyStatus.Approved;
        target.VerifyNote = model.Note?.Trim() ?? string.Empty;
        target.VerifiedById = viewer!.Id;
        target.VerifiedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(token);

        logger.LogInformation("学籍审核通过：user={User} school={School} by={Viewer}",
            target.UserName, target.School, viewer.UserName);

        return Ok(new RequestResponse($"已通过 {target.RealName}（{target.StdNumber}）的学籍审核", StatusCodes.Status200OK));
    }

    /// <summary>
    /// 审核驳回
    /// </summary>
    /// <param name="model">审核请求，Note 建议填写驳回原因</param>
    /// <param name="token"></param>
    [HttpPost("Reject")]
    [RequireUser]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Reject([FromBody] VerifyActionModel model, CancellationToken token = default)
    {
        var (viewer, target, error) = await ResolveTarget(model.UserId, token);
        if (error is not null) return error;

        target!.VerifyStatus = VerifyStatus.Rejected;
        target.VerifyNote = model.Note?.Trim() ?? string.Empty;
        target.VerifiedById = viewer!.Id;
        target.VerifiedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(token);

        logger.LogInformation("学籍审核驳回：user={User} school={School} by={Viewer} reason={Reason}",
            target.UserName, target.School, viewer.UserName, target.VerifyNote);

        return Ok(new RequestResponse($"已驳回 {target.RealName}（{target.StdNumber}）的学籍审核", StatusCodes.Status200OK));
    }

    /// <summary>
    /// 批量审核通过
    /// </summary>
    /// <param name="model">批量审核请求</param>
    /// <param name="token"></param>
    [HttpPost("BatchApprove")]
    [RequireUser]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> BatchApprove([FromBody] VerifyBatchModel model, CancellationToken token = default)
    {
        var viewer = await userManager.GetUserAsync(User);
        if (viewer is null || (!viewer.IsSchoolAdmin && viewer.Role < Role.Admin))
            return Forbidden("你没有学籍审核权限");

        var ids = model.UserIds?.Distinct().Take(500).ToList() ?? [];
        if (ids.Count == 0)
            return BadRequest(new RequestResponse("请选择要审核的学生"));

        var targets = await dbContext.Users
            .Where(u => ids.Contains(u.Id) && u.VerifyStatus == VerifyStatus.Pending)
            .ToListAsync(token);

        var approved = 0;
        var skipped = 0;

        foreach (var target in targets)
        {
            if (!viewer.CanManageSchool(target.School))
            {
                skipped++;
                continue;
            }

            target.VerifyStatus = VerifyStatus.Approved;
            target.VerifiedById = viewer.Id;
            target.VerifiedAtUtc = DateTimeOffset.UtcNow;
            approved++;
        }

        await dbContext.SaveChangesAsync(token);

        logger.LogInformation("学籍批量审核：approved={Approved} skipped={Skipped} by={Viewer}",
            approved, skipped, viewer.UserName);

        return Ok(new RequestResponse(
            skipped > 0
                ? $"已通过 {approved} 名学生，{skipped} 名因不在你的负责范围内被跳过"
                : $"已通过 {approved} 名学生的学籍审核",
            StatusCodes.Status200OK));
    }

    /// <summary>
    /// 当前用户的审核权限信息
    /// </summary>
    [HttpGet("Permission")]
    [RequireUser]
    [ProducesResponseType(typeof(VerifyPermissionModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> Permission()
    {
        var viewer = await userManager.GetUserAsync(User);
        if (viewer is null)
            return Unauthorized(new RequestResponse("请先登录"));

        var managed = viewer.ManagedSchoolList;
        var schoolNames = ClientSsoConfig.ParseSchools(ssoConfig.Value.Schools)
            .ToDictionary(s => s.Slug, s => s.Name, StringComparer.OrdinalIgnoreCase);

        return Ok(new VerifyPermissionModel
        {
            CanVerify = viewer.IsSchoolAdmin || viewer.Role >= Role.Admin,
            ManageAll = viewer.Role >= Role.Admin,
            Schools = managed
                .Select(slug => new VerifySchoolOption(slug, schoolNames.GetValueOrDefault(slug) ?? slug))
                .ToList()
        });
    }

    /// <summary>
    /// 校验操作者可管理该学生所在学校，并返回目标学生
    /// </summary>
    async Task<(UserInfo? viewer, UserInfo? target, IActionResult? error)> ResolveTarget(
        Guid userId, CancellationToken token)
    {
        var viewer = await userManager.GetUserAsync(User);
        if (viewer is null)
            return (null, null, Unauthorized(new RequestResponse("请先登录")));

        if (!viewer.IsSchoolAdmin && viewer.Role < Role.Admin)
            return (viewer, null, Forbidden("你没有学籍审核权限"));

        var target = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, token);
        if (target is null)
            return (viewer, null, NotFoundUser("学生不存在"));

        if (!viewer.CanManageSchool(target.School))
            return (viewer, null, Forbidden($"你无权审核 {target.School} 的学生"));

        return (viewer, target, null);
    }

    IActionResult Forbidden(string message) =>
        RequestResponse.Result(message, StatusCodes.Status403Forbidden);

    IActionResult NotFoundUser(string message) =>
        RequestResponse.Result(message, StatusCodes.Status404NotFound);
}
