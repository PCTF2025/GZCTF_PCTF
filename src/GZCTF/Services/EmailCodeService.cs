using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Distributed;

namespace GZCTF.Services;

/// <summary>
/// 邮箱验证码服务：用于学校邮箱统一认证的一键注册 / 登录。
/// 验证码存放于分布式缓存（Redis），键按「学校 + 邮箱」隔离，一次性消费。
/// </summary>
public class EmailCodeService(IDistributedCache cache, ILogger<EmailCodeService> logger)
{
    /// <summary>验证码有效期</summary>
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);

    /// <summary>同一邮箱的最小重发间隔</summary>
    private static readonly TimeSpan ResendInterval = TimeSpan.FromSeconds(60);

    /// <summary>同一邮箱每小时最多发送次数</summary>
    private const int MaxSendPerHour = 8;

    private static string CodeKey(string slug, string email) =>
        $"pctf:sso:code:{slug}:{email.ToLowerInvariant()}";

    private static string CooldownKey(string slug, string email) =>
        $"pctf:sso:code:cd:{slug}:{email.ToLowerInvariant()}";

    private static string QuotaKey(string slug, string email) =>
        $"pctf:sso:code:q:{slug}:{email.ToLowerInvariant()}";

    /// <summary>
    /// 生成 6 位数字验证码并写入缓存。
    /// 返回 null 表示触发重发限流，此时不发送邮件。
    /// </summary>
    public async Task<string?> IssueAsync(string slug, string email, CancellationToken token = default)
    {
        email = email.ToLowerInvariant();

        // 重发冷却
        var cooldown = await cache.GetStringAsync(CooldownKey(slug, email), token);
        if (!string.IsNullOrEmpty(cooldown))
            return null;

        // 小时配额
        var quotaRaw = await cache.GetStringAsync(QuotaKey(slug, email), token);
        var quota = int.TryParse(quotaRaw, out var q) ? q : 0;
        if (quota >= MaxSendPerHour)
            return null;

        // 6 位数字，用密码学安全随机数
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

        await cache.SetStringAsync(CodeKey(slug, email), code, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CodeLifetime
        }, token);

        await cache.SetStringAsync(CooldownKey(slug, email), "1", new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ResendInterval
        }, token);

        await cache.SetStringAsync(QuotaKey(slug, email), (quota + 1).ToString(), new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
        }, token);

        logger.LogInformation("已为 {Email} 生成学校 {Slug} 的登录验证码", email, slug);

        return code;
    }

    /// <summary>
    /// 校验验证码；成功后立即失效（一次性）。
    /// </summary>
    public async Task<bool> VerifyAsync(string slug, string email, string code, CancellationToken token = default)
    {
        email = email.ToLowerInvariant();

        var stored = await cache.GetStringAsync(CodeKey(slug, email), token);
        if (string.IsNullOrEmpty(stored))
            return false;

        // 定长比较，避免时序侧信道
        var ok = CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(stored),
            System.Text.Encoding.UTF8.GetBytes(code.Trim()));

        if (!ok)
            return false;

        await cache.RemoveAsync(CodeKey(slug, email), token);
        return true;
    }

    /// <summary>
    /// 在不消耗验证码的前提下写回（供 checkCode 两步流程使用）
    /// </summary>
    public async Task ReissueSilentlyAsync(string slug, string email, string code, CancellationToken token = default)
    {
        await cache.SetStringAsync(CodeKey(slug, email.ToLowerInvariant()), code.Trim(),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CodeLifetime }, token);
    }

    /// <summary>
    /// 剩余冷却秒数，用于前端倒计时提示；0 表示可立即重发。
    /// </summary>
    public async Task<int> CooldownRemainingAsync(string slug, string email, CancellationToken token = default)
    {
        var raw = await cache.GetStringAsync(CooldownKey(slug, email.ToLowerInvariant()), token);
        return string.IsNullOrEmpty(raw) ? 0 : (int)ResendInterval.TotalSeconds;
    }

    /// <summary>
    /// 校验邮箱后缀是否属于该校
    /// </summary>
    public static bool EmailMatchesSchool(string email, Models.Internal.ClientSsoSchool school)
    {
        if (school.EmailSuffixes.Count == 0)
            return true;

        var at = email.LastIndexOf('@');
        if (at < 0)
            return false;

        var domain = email[(at + 1)..].ToLowerInvariant();

        return school.EmailSuffixes.Any(suffix =>
            domain == suffix.ToLowerInvariant() ||
            domain.EndsWith("." + suffix.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase));
    }
}
