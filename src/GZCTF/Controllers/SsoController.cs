using System.Text.RegularExpressions;
using System.Xml.Linq;
using GZCTF.Models.Internal;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GZCTF.Controllers;

/// <summary>
/// 学校统一身份认证（SSO）入口控制器。
/// 支持多所学校，每所学校可选 CAS 或邮箱后缀两种认证模式，
/// 学校列表由管理后台的 SsoConfig.Schools 配置项维护。
/// </summary>
[ApiController]
[Route("api/account/[controller]")]
public class SsoController(
    IOptionsSnapshot<SsoConfig> ssoConfig,
    UserManager<UserInfo> userManager,
    SignInManager<UserInfo> signInManager,
    ILogger<SsoController> logger) : ControllerBase
{
    /// <summary>
    /// 发起学校 SSO 登录
    /// </summary>
    /// <remarks>
    /// CAS 模式：重定向到该学校的统一认证登录页。
    /// 邮箱模式：直接返回引导信息，前端就地切换到邮箱登录表单。
    /// </remarks>
    /// <param name="slug">学校短名</param>
    [HttpGet("login/{slug}")]
    public IActionResult Login(string slug)
    {
        var school = FindSchool(slug);

        if (school is null)
            return NotFound(new RequestResponse($"未配置的学校：{slug}"));

        // 邮箱模式不跳转外部，交由前端处理
        if (school.Mode == SsoAuthMode.Email)
            return Ok(new RequestResponse($"请使用 {school.Name} 邮箱登录", StatusCodes.Status200OK));

        if (string.IsNullOrWhiteSpace(school.LoginUrl))
            return BadRequest(new RequestResponse($"{school.Name} 未配置统一认证登录地址"));

        var callbackUrl = Url.Action(nameof(Callback), "Sso", new { slug = school.Slug },
            Request.Scheme, Request.Host.Value);

        var target = $"{school.LoginUrl}{(school.LoginUrl.Contains('?') ? '&' : '?')}" +
                     $"service={Uri.EscapeDataString(callbackUrl ?? string.Empty)}";

        return Redirect(target);
    }

    /// <summary>
    /// CAS 回调：校验票据并完成登录
    /// </summary>
    /// <param name="slug">学校短名</param>
    /// <param name="ticket">CAS 票据</param>
    [HttpGet("callback")]
    public async Task<IActionResult> Callback(
        [FromQuery] string? slug,
        [FromQuery] string? ticket,
        CancellationToken token)
    {
        var school = FindSchool(slug);

        if (school is null)
            return BadRequest(new RequestResponse("未配置的学校"));

        if (school.Mode != SsoAuthMode.Cas)
            return BadRequest(new RequestResponse($"{school.Name} 未启用统一认证登录"));

        if (string.IsNullOrWhiteSpace(ticket))
            return BadRequest(new RequestResponse("缺少认证票据（ticket）"));

        var identifier = await ValidateCasTicketAsync(school, ticket, token);

        if (string.IsNullOrWhiteSpace(identifier))
        {
            logger.LogWarning("CAS 认证失败：school={School} 未获取到用户标识", school.Name);
            return BadRequest(new RequestResponse("认证失败，未获取到用户信息"));
        }

        var user = await FindUserAsync(identifier, school, token);

        if (user is null)
        {
            // 未绑定账号：带学号跳回登录页引导绑定或注册
            var frontendUrl =
                $"{Request.Scheme}://{Request.Host}/account/login?sso_bind=1&school={Uri.EscapeDataString(school.Slug)}&std={Uri.EscapeDataString(identifier)}";
            return Redirect(frontendUrl);
        }

        user.LastSignedInUtc = DateTimeOffset.UtcNow;
        user.LastVisitedUtc = DateTimeOffset.UtcNow;
        user.UpdateByHttpContext(HttpContext);

        await signInManager.SignOutAsync();
        await signInManager.SignInAsync(user, true);

        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            var first = updateResult.Errors.FirstOrDefault();
            return BadRequest(new RequestResponse(first?.Description ?? "用户操作失败"));
        }

        logger.LogInformation("学校 SSO 登录成功：school={School} user={UserName} id={Identifier}",
            school.Name, user.UserName, identifier);

        return Redirect($"{Request.Scheme}://{Request.Host}{ssoConfig.Value.RedirectPath}");
    }

    /// <summary>
    /// 校验邮箱 / 账号是否属于指定学校（邮箱模式使用）
    /// </summary>
    /// <param name="slug">学校短名</param>
    /// <param name="account">邮箱或学号</param>
    [HttpGet("verify")]
    public IActionResult Verify([FromQuery] string? slug, [FromQuery] string? account)
    {
        var school = FindSchool(slug);

        if (school is null)
            return NotFound(new RequestResponse($"未配置的学校：{slug}"));

        if (string.IsNullOrWhiteSpace(account))
            return BadRequest(new RequestResponse("请输入邮箱或学号"));

        var ok = IsEmailBelongsToSchool(account, school);

        return ok
            ? Ok(new RequestResponse($"{school.Name} 邮箱校验通过", StatusCodes.Status200OK))
            : BadRequest(new RequestResponse($"该邮箱不属于 {school.Name}，请检查后重试"));
    }

    /// <summary>
    /// 判断邮箱是否归属该校。
    /// 传入纯学号（不含 @）时无法判断域归属，交由后续账密校验处理，此处放行。
    /// </summary>
    internal static bool IsEmailBelongsToSchool(string account, ClientSsoSchool school)
    {
        if (school.EmailSuffixes.Count == 0)
            return true; // 未限制后缀时不做归属校验

        var value = account.Trim();

        // 纯学号：不含域信息，放行由登录流程校验
        if (!value.Contains('@'))
            return true;

        var domain = value[(value.IndexOf('@') + 1)..];

        return school.EmailSuffixes.Any(suffix =>
            domain.Equals(suffix, StringComparison.OrdinalIgnoreCase) ||
            domain.EndsWith($".{suffix}", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 校验 CAS ticket，返回学号
    /// </summary>
    async Task<string?> ValidateCasTicketAsync(ClientSsoSchool school, string ticket, CancellationToken token)
    {
        var callbackUrl = Url.Action(nameof(Callback), "Sso", new { slug = school.Slug },
            Request.Scheme, Request.Host.Value);

        var validateUrl =
            $"{school.ValidateUrl}?service={Uri.EscapeDataString(callbackUrl ?? string.Empty)}&ticket={Uri.EscapeDataString(ticket)}";

        try
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var responseXml = await httpClient.GetStringAsync(validateUrl, token);
            return ParseCasResponse(responseXml);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "CAS ticket 校验失败：school={School} ticket={Ticket}", school.Name, ticket);
            return null;
        }
    }

    /// <summary>
    /// 解析 CAS 2.0 serviceValidate XML，提取用户名（学号）
    /// </summary>
    static string? ParseCasResponse(string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            var ns = XNamespace.Get("http://www.yale.edu/tp/cas");
            return doc.Descendants(ns + "user").FirstOrDefault()?.Value?.Trim();
        }
        catch
        {
            var match = Regex.Match(xml, @"<cas:user>([^<]+)</cas:user>", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.Trim() : null;
        }
    }

    /// <summary>
    /// 按学号或邮箱查找用户
    /// </summary>
    async Task<UserInfo?> FindUserAsync(string identifier, ClientSsoSchool school, CancellationToken token)
    {
        var byStd = await userManager.Users.FirstOrDefaultAsync(
            u => u.StdNumber != null && u.StdNumber == identifier, token);

        if (byStd is not null)
            return byStd;

        // CAS 返回的可能是纯学号，尝试拼成学校邮箱再查
        foreach (var suffix in school.EmailSuffixes)
        {
            var byMail = await userManager.FindByEmailAsync($"{identifier}@{suffix}");
            if (byMail is not null)
                return byMail;
        }

        return await userManager.FindByNameAsync(identifier)
               ?? await userManager.FindByEmailAsync(identifier);
    }

    /// <summary>
    /// 按短名查找已启用学校
    /// </summary>
    ClientSsoSchool? FindSchool(string? slug) =>
        string.IsNullOrWhiteSpace(slug)
            ? null
            : ClientSsoConfig.ParseSchools(ssoConfig.Value.Schools)
                .FirstOrDefault(s => string.Equals(s.Slug, slug, StringComparison.OrdinalIgnoreCase));
}
