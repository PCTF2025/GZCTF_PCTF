using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Serilog;

namespace GZCTF.Middlewares;

/// <summary>
/// Authorization filter for privilege
/// </summary>
/// <param name="privilege"> The privilege required </param>
/// <param name="allowToken"> Whether to allow token authentication </param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequirePrivilegeAttribute(Role privilege, bool allowToken = false, UserPermission? permission = null)
    : Attribute, IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var logger =
            context.HttpContext.RequestServices.GetRequiredService<ILogger<RequirePrivilegeAttribute>>();

        if (allowToken && await ContextHelper.HasValidToken(context.HttpContext))
            return;

        var dbContext = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var localizer =
            context.HttpContext.RequestServices.GetRequiredService<IStringLocalizer<Program>>();
        var diagnosticContext =
            context.HttpContext.RequestServices.GetRequiredService<IDiagnosticContext>();

        var id = context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);

        UserInfo? user = null;

        if (id is not null && context.HttpContext.User.Identity?.IsAuthenticated is true &&
            Guid.TryParse(id, out var guid))
            user = await dbContext.Users.SingleOrDefaultAsync(u => u.Id == guid);

        if (user is null)
        {
            context.Result = RequestResponse.Result(localizer[nameof(Resources.Program.Auth_LoginRequired)],
                StatusCodes.Status401Unauthorized);
            return;
        }

        diagnosticContext.Set("UserId", user.Id);
        diagnosticContext.Set("UserName", user.UserName ?? "Anonymous");

        if (context.HttpContext.Connection.RemoteIpAddress is { } ip)
            diagnosticContext.Set("IP", ip);

        if (DateTimeOffset.UtcNow - user.LastVisitedUtc > TimeSpan.FromSeconds(5))
        {
            user.UpdateByHttpContext(context.HttpContext);
            await dbContext.SaveChangesAsync(); // avoid to update ConcurrencyStamp
        }

        if (Verify(user, privilege) && (permission is null || user.HasPermission(permission.Value)))
            return;

        if (privilege > Role.User)
            logger.Log(
                StaticLocalizer[nameof(Resources.Program.Auth_PathAccessForbidden),
                    context.HttpContext.Request.Path], user,
                TaskStatus.Denied);

        context.Result = RequestResponse.Result(localizer[nameof(Resources.Program.Auth_AccessForbidden)],
            StatusCodes.Status403Forbidden);
    }

    /// <summary>
    /// 是否满足角色等级要求。子类可覆盖以实现更宽松的判定
    /// </summary>
    protected virtual bool Verify(UserInfo user, Role required) => user.Role >= required;
}

/// <summary>
/// User required
/// </summary>
public class RequireUserAttribute() : RequirePrivilegeAttribute(Role.User);

/// <summary>
/// Monitor role required
/// </summary>
public class RequireMonitorAttribute() : RequirePrivilegeAttribute(Role.Monitor);

/// <summary>
/// Admin privilege required
/// </summary>
public class RequireAdminAttribute() : RequirePrivilegeAttribute(Role.Admin);

/// <summary>
/// 要求指定职能权限（出题人 / 出题管理员 / 学校负责人）。
/// 角色等级不足但持有对应权限位时同样放行，因此角色放宽到 User。
/// </summary>
/// <param name="permission">要求的权限位</param>
public class RequirePermissionAttribute(UserPermission permission)
    : RequirePrivilegeAttribute(Role.User, false, permission);

/// <summary>
/// 可进入题目编辑功能（出题人或出题管理员，系统管理员天然放行）
/// </summary>
public class RequireChallengeEditorAttribute()
    : RequirePrivilegeAttribute(Role.User)
{
    protected override bool Verify(UserInfo user, Role required) => user.CanEditChallenge;
}

/// <summary>
/// 可审核学籍（学校负责人，需另行校验具体学校范围）
/// </summary>
public class RequireSchoolManagerAttribute()
    : RequirePrivilegeAttribute(Role.User)
{
    protected override bool Verify(UserInfo user, Role required) =>
        user.Role >= Role.Admin ||
        user.IsSchoolAdmin ||
        user.HasPermission(UserPermission.SchoolManager);
}

/// <summary>
/// 可进入题目编辑控制器（出题人 / 出题管理员 / 系统管理员）。
/// 具体到每个接口的精细化校验在控制器内完成
/// </summary>
public class RequireChallengeEditorOrAdminAttribute()
    : RequirePrivilegeAttribute(Role.User)
{
    protected override bool Verify(UserInfo user, Role required) => user.CanEditChallenge;
}

/// <summary>
/// 可上传素材文件（出题人 / 出题管理员 / 学校负责人 / 系统管理员）
/// </summary>
public class RequireAssetUploaderAttribute()
    : RequirePrivilegeAttribute(Role.User)
{
    protected override bool Verify(UserInfo user, Role required) =>
        user.Role >= Role.Admin || user.CanEditChallenge || user.IsSchoolAdmin;
}

/// <summary>
/// 可查看比赛信息（系统管理员 / 出题人 / 出题管理员 / 学校负责人）。
/// 学校负责人需要进入比赛查看队伍审核与组别，故一并放行只读接口。
/// </summary>
public class RequireGameReviewerAttribute()
    : RequirePrivilegeAttribute(Role.User)
{
    protected override bool Verify(UserInfo user, Role required) =>
        user.Role >= Role.Admin ||
        user.CanEditChallenge ||
        user.IsSchoolAdmin ||
        user.HasPermission(UserPermission.SchoolManager);
}

/// <summary>
/// 可查看报名数据大屏（系统管理员 / 出题人 / 出题管理员 / 学校负责人）
/// </summary>
public class RequireDashboardViewerAttribute()
    : RequirePrivilegeAttribute(Role.User)
{
    protected override bool Verify(UserInfo user, Role required) =>
        user.Role >= Role.Admin ||
        user.CanEditChallenge ||
        user.IsSchoolAdmin ||
        user.HasPermission(UserPermission.SchoolManager);
}

/// <summary>
/// Admin privilege required, but allow token authentication
/// </summary>
public class RequireAdminOrTokenAttribute() : RequirePrivilegeAttribute(Role.Admin, true);
