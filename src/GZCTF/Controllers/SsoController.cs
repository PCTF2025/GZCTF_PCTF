using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using GZCTF.Models;
using GZCTF.Models.Internal;
using GZCTF.Models.Request.Account;
using GZCTF.Services;
using GZCTF.Services.Mail;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
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
    IOptionsSnapshot<GlobalConfig> globalConfig,
    UserManager<UserInfo> userManager,
    SignInManager<UserInfo> signInManager,
    EmailCodeService emailCode,
    IMailSender mailSender,
    IStringLocalizer<Program> localizer,
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

        // 快速登录自动绑定学校：未绑定过学校时写入，并记录来源为 Sso；
        // 已有邀请码绑定的学校不覆盖（邀请码绑定优先级更高，避免误改）
        if (string.IsNullOrWhiteSpace(user.School) || user.SchoolSource != SchoolBindSource.Invite)
        {
            user.School = school.Slug;
            user.SchoolSource = SchoolBindSource.Sso;
        }

        // CAS 模式下如返回的是学号，同时补全学号（用户未填时）
        if (school.Mode == SsoAuthMode.Cas && string.IsNullOrWhiteSpace(user.StdNumber))
            user.StdNumber = identifier;

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
    /// 获取可选学校列表（用于注册时选择学校 / 邀请码绑定）
    /// </summary>
    /// <remarks>
    /// 只返回已启用的学校，且不暴露登录与校验地址等内部信息。
    /// </remarks>
    [HttpGet("schools")]
    [ProducesResponseType(typeof(SchoolOptionModel[]), StatusCodes.Status200OK)]
    public IActionResult GetSchools()
    {
        var inviteCodes = ClientSsoConfig.ParseInviteCodes(ssoConfig.Value.SchoolInviteCodes);

        var schools = ClientSsoConfig.ParseSchools(ssoConfig.Value.Schools)
            .Select(s => new SchoolOptionModel(
                s.Name,
                s.Slug,
                s.Mode == SsoAuthMode.Cas ? "cas" : "email",
                s.EmailSuffixes,
                // 该校在邀请码表中且邀请码非空时，前端应提示邀请码必填
                inviteCodes.Any(pair =>
                    string.Equals(pair.Slug, s.Slug, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(pair.Code))))
            .ToArray();

        return Ok(schools);
    }

    /// <summary>
    /// 发送邮箱验证码（邮箱认证模式）
    /// </summary>
    /// <remarks>
    /// 校验邮箱后缀属于该校后，发送 6 位数字验证码。
    /// 同一邮箱 60 秒内不可重发，每小时最多 8 次。
    /// </remarks>
    /// <param name="model">含学校短名与邮箱</param>
    /// <param name="token"></param>
    [HttpPost("sendCode")]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> SendCode([FromBody] SchoolCodeSendModel model, CancellationToken token = default)
    {
        var school = FindSchool(model.SchoolSlug);

        if (school is null)
            return NotFound(new RequestResponse("未配置的学校，请刷新页面后重试"));

        if (school.Mode != SsoAuthMode.Email)
            return BadRequest(new RequestResponse($"{school.Name} 未启用邮箱验证码登录，请使用统一认证登录"));

        var email = model.Email?.Trim().ToLowerInvariant() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            return BadRequest(new RequestResponse("请输入正确的邮箱地址"));

        if (!EmailCodeService.EmailMatchesSchool(email, school))
            return BadRequest(new RequestResponse($"该邮箱不属于 {school.Name}，请使用学校邮箱"));

        var code = await emailCode.IssueAsync(school.Slug, email, token);

        if (code is null)
            return BadRequest(new RequestResponse("验证码发送过于频繁，请稍后再试"));

        // 必须走发送队列：MailSender 的 SMTP 连接由后台 worker 统一持有，
        // 直接调用 SendMailContent 会因连接未建立而抛出 ServiceNotConnectedException
        var enqueued = mailSender.EnqueueRawMail(
            email,
            null,
            $"{school.Name} 登录验证码",
            BuildCodeMailContent(school, code),
            localizer,
            globalConfig);

        if (!enqueued)
            return BadRequest(new RequestResponse("验证码邮件发送失败，请稍后重试或联系赛事管理员"));

        logger.LogInformation("已发送学校 {School} 的登录验证码至 {Email}", school.Name, email);

        return Ok(new RequestResponse($"验证码已发送至 {email}，10 分钟内有效", StatusCodes.Status200OK));
    }

    /// <summary>
    /// 校验验证码，判断该邮箱是否已注册
    /// </summary>
    /// <remarks>
    /// 前端据此决定「一键注册」还是「一键登录」的按钮文案与后续字段。
    /// 校验通过后验证码不失效，仍需在 loginWithCode 中再次提交。
    /// </remarks>
    /// <param name="model">含学校短名、邮箱与验证码</param>
    /// <param name="token"></param>
    [HttpPost("checkCode")]
    [ProducesResponseType(typeof(SchoolCodeCheckResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> CheckCode([FromBody] SchoolCodeLoginModel model,
        CancellationToken token = default)
    {
        var school = FindSchool(model.SchoolSlug);

        if (school is null)
            return NotFound(new RequestResponse("未配置的学校，请刷新页面后重试"));

        var email = model.Email?.Trim().ToLowerInvariant() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(model.Code))
            return BadRequest(new RequestResponse("请填写验证码"));

        var valid = await VerifyCodeWithoutConsumeAsync(school.Slug, email, model.Code, token);

        if (!valid)
            return BadRequest(new RequestResponse("验证码不正确或已过期，请重新获取"));

        var user = await userManager.FindByEmailAsync(email);
        var exists = user is not null;

        // 已注册用户：若学籍信息缺失，仍需要前端收集姓名 / 学号
        var needProfile = !exists ||
                          string.IsNullOrWhiteSpace(user!.RealName) ||
                          string.IsNullOrWhiteSpace(user.StdNumber);

        return Ok(new SchoolCodeCheckResult
        {
            Registered = exists,
            NeedProfile = needProfile,
            UserName = user?.UserName,
            RealName = user?.RealName,
            StdNumber = user?.StdNumber,
            Grade = user?.Grade
        });
    }

    /// <summary>
    /// 验证码一键注册 / 登录
    /// </summary>
    /// <remarks>
    /// 邮箱未注册时自动创建账号（随机密码，仅验证码登录），并绑定学校；
    /// 已注册时直接登录并补齐学校绑定。首次需提供真实姓名与学号。
    /// </remarks>
    /// <param name="model">含学校短名、邮箱、验证码、真实姓名、学号与年级</param>
    /// <param name="token"></param>
    [HttpPost("loginWithCode")]
    [ProducesResponseType(typeof(SchoolCodeLoginResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> LoginWithCode([FromBody] SchoolCodeLoginModel model,
        CancellationToken token = default)
    {
        var school = FindSchool(model.SchoolSlug);

        if (school is null)
            return NotFound(new RequestResponse("未配置的学校，请刷新页面后重试"));

        if (school.Mode != SsoAuthMode.Email)
            return BadRequest(new RequestResponse($"{school.Name} 未启用邮箱验证码登录，请使用统一认证登录"));

        var email = model.Email?.Trim().ToLowerInvariant() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(email))
            return BadRequest(new RequestResponse("请输入邮箱地址"));

        if (!EmailCodeService.EmailMatchesSchool(email, school))
            return BadRequest(new RequestResponse($"该邮箱不属于 {school.Name}，请使用学校邮箱"));

        if (string.IsNullOrWhiteSpace(model.Code))
            return BadRequest(new RequestResponse("请填写验证码"));

        // 一次性消费验证码
        var valid = await emailCode.VerifyAsync(school.Slug, email, model.Code, token);

        if (!valid)
            return BadRequest(new RequestResponse("验证码不正确或已过期，请重新获取"));

        var user = await userManager.FindByEmailAsync(email);
        var created = false;

        if (user is null)
        {
            // 账号名：优先学号，冲突时回退到邮箱前缀加随机后缀
            // 用户名优先级：用户填写 > 学号 > 邮箱前缀
            var userName = await PickUserNameAsync(model.UserName, model.StdNumber, email);

            user = new UserInfo
            {
                UserName = userName,
                Email = email,
                EmailConfirmed = true,
                Role = Role.User,
                RegisterTimeUtc = DateTimeOffset.UtcNow,
                LastVisitedUtc = DateTimeOffset.UtcNow,
                IP = HttpContext.Connection.RemoteIpAddress ?? System.Net.IPAddress.Any
            };

            // 随机强密码：本流程仅支持验证码登录，用户之后可在个人资料页设置密码
            var password = $"Pctf@{Guid.NewGuid():N}aA1";
            var result = await userManager.CreateAsync(user, password);

            if (!result.Succeeded)
            {
                logger.LogError("验证码注册失败：{Errors}",
                    string.Join("; ", result.Errors.Select(e => e.Description)));
                return BadRequest(new RequestResponse("账号创建失败，请联系赛事管理员"));
            }

            created = true;
        }
        else if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
        }

        // 补齐学籍信息：不覆盖用户已填写的有效值
        if (!string.IsNullOrWhiteSpace(model.RealName))
            user.RealName = model.RealName.Trim();

        if (!string.IsNullOrWhiteSpace(model.StdNumber))
            user.StdNumber = model.StdNumber.Trim();

        if (!string.IsNullOrWhiteSpace(model.Grade))
            user.Grade = model.Grade.Trim();

        // 绑定学校：已通过邀请码绑定的学校不覆盖
        if (user.SchoolSource != SchoolBindSource.Invite)
        {
            user.School = school.Slug;
            user.SchoolSource = SchoolBindSource.Sso;
        }

        user.SubmitForVerification();
        user.LastVisitedUtc = DateTimeOffset.UtcNow;
        user.IP = HttpContext.Connection.RemoteIpAddress ?? System.Net.IPAddress.Any;

        var update = await userManager.UpdateAsync(user);

        if (!update.Succeeded)
        {
            logger.LogError("验证码登录后资料更新失败：{Errors}",
                string.Join("; ", update.Errors.Select(e => e.Description)));
        }

        // 免密码登录：用已验证的邮箱身份直接签发 Cookie
        await signInManager.SignInAsync(user, isPersistent: true);

        logger.LogInformation("学校 {School} 验证码登录成功：user={User} created={Created}",
            school.Name, user.UserName, created);

        return Ok(new SchoolCodeLoginResult
        {
            Created = created,
            UserName = user.UserName,
            School = school.Slug,
            SchoolName = school.Name,
            VerifyStatus = user.VerifyStatus
        });
    }

    /// <summary>
    /// 校验验证码但不消费（供 checkCode 使用）
    /// </summary>
    async Task<bool> VerifyCodeWithoutConsumeAsync(string slug, string email, string code, CancellationToken token)
    {
        // 用一个短时占位副本校验，避免 checkCode 阶段就消耗掉真实验证码
        var valid = await emailCode.VerifyAsync(slug, email, code, token);

        if (!valid)
            return false;

        // 校验成功后把验证码写回，保留给随后的 loginWithCode 消费
        await emailCode.ReissueSilentlyAsync(slug, email, code, token);
        return true;
    }

    /// <summary>
    /// 生成可用的用户名：优先学号，冲突则追加随机后缀
    /// </summary>
    /// <summary>
    /// 解析新账号的用户名：依次尝试用户填写值、学号、邮箱前缀，
    /// 全部被占用时回退「邮箱前缀 + 随机数」。
    /// </summary>
    async Task<string> PickUserNameAsync(string? preferred, string? stdNumber, string email)
    {
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(preferred))
            candidates.Add(preferred.Trim());

        if (!string.IsNullOrWhiteSpace(stdNumber))
            candidates.Add(stdNumber.Trim());

        candidates.Add(email.Split('@')[0]);

        foreach (var candidate in candidates)
        {
            var normalized = new string(candidate.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());

            if (normalized.Length < Limits.MinUserNameLength)
                continue;

            if (normalized.Length > Limits.MaxUserNameLength)
                normalized = normalized[..Limits.MaxUserNameLength];

            if (await userManager.FindByNameAsync(normalized) is null)
                return normalized;
        }

        // 全部冲突：邮箱前缀 + 4 位随机数
        var baseName = new string(email.Split('@')[0].Where(char.IsLetterOrDigit).ToArray());
        if (baseName.Length < Limits.MinUserNameLength)
            baseName = "pctf";

        var maxBase = Math.Max(1, Limits.MaxUserNameLength - 5);

        if (baseName.Length > maxBase)
            baseName = baseName[..maxBase];

        for (var i = 0; i < 20; i++)
        {
            var name = $"{baseName}{Random.Shared.Next(1000, 9999)}";
            if (await userManager.FindByNameAsync(name) is null)
                return name;
        }

        return $"{baseName}{DateTimeOffset.UtcNow.ToUnixTimeSeconds() % 100000}";
    }

    /// <summary>
    /// 构造验证码邮件正文（复用站点风格的 HTML）
    /// </summary>
    string BuildCodeMailContent(ClientSsoSchool school, string code)
    {
        var title = $"{school.Name} 登录验证码";
        var platform = globalConfig.Value.Platform;

        return $"""
                <div style="font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Helvetica,Arial,sans-serif;max-width:560px;margin:0 auto;padding:24px;color:#1f2933;">
                  <h2 style="font-size:18px;margin:0 0 16px;">{title}</h2>
                  <p style="font-size:14px;line-height:1.6;margin:0 0 16px;">
                    你正在通过 <b>{school.Name}</b> 邮箱登录 <b>{platform}</b>。请在页面中输入以下验证码完成验证：
                  </p>
                  <div style="font-size:32px;font-weight:700;letter-spacing:8px;text-align:center;
                              padding:18px;background:#f4f6f8;border-radius:8px;margin:0 0 16px;">
                    {code}
                  </div>
                  <p style="font-size:13px;color:#7b8794;line-height:1.6;margin:0;">
                    验证码 10 分钟内有效，请勿转发给他人。<br/>
                    若这不是你本人的操作，请忽略本邮件。
                  </p>
                </div>
                """;
    }

    /// <summary>
    /// 使用邀请码绑定学校
    /// </summary>
    /// <remarks>
    /// 未使用快速登录的用户，可用管理员下发的邀请码绑定学校。
    /// 邀请码格式为 `学校短名:邀请码`，或直接用全局邀请码。
    /// </remarks>
    /// <param name="model">绑定请求</param>
    /// <param name="token"></param>
    [HttpPost("bind")]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> BindSchool([FromBody] SchoolBindModel model, CancellationToken token)
    {
        if (User.Identity?.IsAuthenticated != true)
            return Unauthorized(new RequestResponse("请先登录"));

        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized(new RequestResponse("请先登录"));

        var school = FindSchool(model.SchoolSlug);
        if (school is null)
            return BadRequest(new RequestResponse("所选学校不存在或未启用"));

        // 邀请码校验：学校级邀请码，未配置则该校不需要邀请码
        var expected = ssoConfig.Value.SchoolInviteCodes is { Length: > 0 }
            ? ClientSsoConfig.ParseInviteCodes(ssoConfig.Value.SchoolInviteCodes)
                .FirstOrDefault(pair => string.Equals(pair.Slug, school.Slug, StringComparison.OrdinalIgnoreCase))
                .Code
            : null;

        if (!string.IsNullOrWhiteSpace(expected) &&
            !string.Equals(expected, model.InviteCode?.Trim(), StringComparison.Ordinal))
        {
            logger.LogWarning("学校邀请码校验失败：school={School} user={User}", school.Slug, user.UserName);
            return BadRequest(new RequestResponse("邀请码不正确，请联系赛事管理员获取"));
        }

        // 已有快速登录绑定的学校时不允许随意更换，避免绕过学校身份校验
        if (user.SchoolSource == SchoolBindSource.Sso &&
            !string.Equals(user.School, school.Slug, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new RequestResponse(
                $"你已通过学校统一认证绑定 {user.School}，如需更换请联系赛事管理员"));
        }

        user.School = school.Slug;
        user.SchoolSource = SchoolBindSource.Invite;

        if (!string.IsNullOrWhiteSpace(model.StdNumber))
            user.StdNumber = model.StdNumber.Trim();

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            var first = result.Errors.FirstOrDefault();
            return BadRequest(new RequestResponse(first?.Description ?? "绑定失败，请稍后重试"));
        }

        logger.LogInformation("邀请码绑定学校成功：school={School} user={User}", school.Slug, user.UserName);

        return Ok(new RequestResponse($"已绑定 {school.Name}", StatusCodes.Status200OK));
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
