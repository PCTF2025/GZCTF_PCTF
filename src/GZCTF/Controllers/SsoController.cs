using System.Text.RegularExpressions;
using System.Xml.Linq;
using GZCTF.Models.Internal;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GZCTF.Controllers;

/// <summary>
/// 外部单点登录（SSO）入口控制器。
/// 登录页右侧的每个入口对应此处一个 provider 短名，实际跳转地址与显示文案
/// 由管理后台的 SsoConfig.Providers 配置项决定。
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
    /// 发起 SSO 登录：重定向到对应身份提供方的登录页
    /// </summary>
    /// <param name="provider">提供方短名，如 oa / mail / cas</param>
    [HttpGet("login/{provider}")]
    public IActionResult Login(string provider)
    {
        var config = ssoConfig.Value;

        var entry = ClientSsoConfig
            .ParseProviders(config.Providers)
            .FirstOrDefault(p => string.Equals(p.Provider, provider, StringComparison.OrdinalIgnoreCase));

        if (entry is null || string.IsNullOrWhiteSpace(entry.Link))
            return NotFound(new RequestResponse($"未配置的登录方式：{provider}"));

        // 相对路径直接跳转（保留前端路由），绝对地址则附加回调参数
        var callbackUrl = Url.Action(nameof(Callback), "Sso", new { provider },
            Request.Scheme, Request.Host.Value);

        var target = entry.Link.Contains("://")
            ? $"{entry.Link}{(entry.Link.Contains('?') ? '&' : '?')}redirect_uri={Uri.EscapeDataString(callbackUrl ?? string.Empty)}"
            : entry.Link;

        return Redirect(target);
    }

    /// <summary>
    /// SSO 回调：接收票据并完成登录
    /// </summary>
    /// <param name="provider">提供方短名</param>
    /// <param name="ticket">CAS 票据</param>
    /// <param name="code">OAuth code（与 ticket 二选一）</param>
    /// <param name="state">OAuth state</param>
    [HttpGet("callback")]
    public async Task<IActionResult> Callback(
        [FromQuery] string? provider,
        [FromQuery] string? ticket,
        [FromQuery] string? code,
        [FromQuery] string? state,
        CancellationToken token)
    {
        var config = ssoConfig.Value;

        var entry = ClientSsoConfig
            .ParseProviders(config.Providers)
            .FirstOrDefault(p => string.Equals(p.Provider, provider, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
            return BadRequest(new RequestResponse("未配置的登录方式"));

        // 取用户标识：CAS 用 ticket 校验，OAuth 用 code 换 token
        string? identifier = null;

        if (!string.IsNullOrWhiteSpace(ticket))
            identifier = await ValidateCasTicketAsync(entry, ticket, token);
        else if (!string.IsNullOrWhiteSpace(code))
            identifier = await ExchangeOAuthCodeAsync(entry, code, token);

        if (string.IsNullOrWhiteSpace(identifier))
        {
            logger.LogWarning("SSO 认证失败：provider={Provider} 未获取到用户标识", provider);
            return BadRequest(new RequestResponse("SSO 认证失败，未获取到用户信息"));
        }

        // 优先按学号匹配，其次按邮箱匹配
        var user = await FindUserAsync(identifier, token);

        if (user is null)
        {
            // 未绑定账号：带上学号跳回登录页引导绑定 / 注册
            var frontendUrl =
                $"{Request.Scheme}://{Request.Host}/account/login?sso_bind=1&std={Uri.EscapeDataString(identifier)}";
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

        logger.LogInformation("SSO 登录成功：provider={Provider} user={UserName} id={Identifier}",
            provider, user.UserName, identifier);

        return Redirect($"{Request.Scheme}://{Request.Host}{config.RedirectPath}");
    }

    /// <summary>
    /// 校验 CAS ticket，返回学号
    /// </summary>
    async Task<string?> ValidateCasTicketAsync(ClientSsoProvider entry, string ticket, CancellationToken token)
    {
        var callbackUrl = Url.Action(nameof(Callback), "Sso", new { provider = entry.Provider },
            Request.Scheme, Request.Host.Value);

        var validateUrl =
            $"{entry.ValidateUrl}?service={Uri.EscapeDataString(callbackUrl ?? string.Empty)}&ticket={Uri.EscapeDataString(ticket)}";

        try
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var responseXml = await httpClient.GetStringAsync(validateUrl, token);
            return ParseCasResponse(responseXml);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "CAS ticket 校验失败：{Ticket}", ticket);
            return null;
        }
    }

    /// <summary>
    /// 用 OAuth code 换取用户标识
    /// </summary>
    async Task<string?> ExchangeOAuthCodeAsync(ClientSsoProvider entry, string code, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(entry.TokenUrl))
            return null;

        try
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var payload = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["client_id"] = entry.ClientId,
                ["client_secret"] = entry.ClientSecret,
                ["redirect_uri"] = Url.Action(nameof(Callback), "Sso", new { provider = entry.Provider },
                    Request.Scheme, Request.Host.Value) ?? string.Empty
            });

            var response = await httpClient.PostAsync(entry.TokenUrl, payload, token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("OAuth 换取 token 失败：{Status}", response.StatusCode);
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(token);

            // 容忍常见返回结构：直接返回用户标识或包在 JSON 字段中
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            var root = doc.RootElement;

            foreach (var key in new[] { "user", "username", "userName", "sub", "email", "id" })
                if (root.TryGetProperty(key, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String)
                    return value.GetString();

            logger.LogWarning("OAuth 响应中未找到用户标识字段");
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OAuth code 换取用户标识异常");
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
    async Task<UserInfo?> FindUserAsync(string identifier, CancellationToken token)
    {
        var byStd = await userManager.Users.FirstOrDefaultAsync(
            u => u.StdNumber != null && u.StdNumber == identifier, token);

        if (byStd is not null)
            return byStd;

        return await userManager.FindByNameAsync(identifier)
               ?? await userManager.FindByEmailAsync(identifier);
    }
}
