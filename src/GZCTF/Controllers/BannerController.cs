using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using GZCTF.Middlewares;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Controllers;

/// <summary>
/// 首页 Banner 轮播 API（公开读取 + 管理员维护）
/// </summary>
/// <remarks>
/// GZCTF 的配置机制不支持数组类型，故 Banner 列表以 JSON 形式
/// 直接存放在 Configs 表中（键：home.banners）。
/// </remarks>
[ApiController]
[Route("api/[controller]")]
public class BannerController(AppDbContext db) : ControllerBase
{
    private const string BannerConfigKey = "home.banners";
    private const string BannerIntervalKey = "home.banner.interval";
    private const int DefaultIntervalMs = 5000;
    private const int MinIntervalMs = 1000;
    private const int MaxIntervalMs = 60000;

    /// <summary>
    /// 获取首页启用中的 Banner 列表与自动播放间隔（公开接口）
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(HomeBannerConfigModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBanners(CancellationToken token)
    {
        var list = await LoadBanners(token);
        var interval = await LoadInterval(token);

        return Ok(new HomeBannerConfigModel
        {
            IntervalMs = interval,
            Banners = list.Where(b => b.Enabled).OrderBy(b => b.SortOrder).ToList()
        });
    }

    /// <summary>
    /// 管理员读取全部 Banner（含未启用）
    /// </summary>
    [HttpGet("Admin")]
    [RequireAdmin]
    [ProducesResponseType(typeof(HomeBannerConfigModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetBannersForAdmin(CancellationToken token)
    {
        var list = await LoadBanners(token);
        var interval = await LoadInterval(token);

        return Ok(new HomeBannerConfigModel
        {
            IntervalMs = interval,
            Banners = list.OrderBy(b => b.SortOrder).ToList()
        });
    }

    /// <summary>
    /// 管理员保存 Banner 配置
    /// </summary>
    [HttpPut("Admin")]
    [RequireAdmin]
    [ProducesResponseType(typeof(HomeBannerConfigModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SaveBanners([FromBody] HomeBannerConfigModel model, CancellationToken token)
    {
        var interval = Math.Clamp(model.IntervalMs, MinIntervalMs, MaxIntervalMs);

        // 清理：丢弃图片与链接均为空、或标题也为空的无意义条目
        var cleaned = (model.Banners ?? [])
            .Where(b => !string.IsNullOrWhiteSpace(b.ImageUrl) || !string.IsNullOrWhiteSpace(b.LinkUrl))
            .Select((b, i) => new HomeBannerModel
            {
                Id = string.IsNullOrWhiteSpace(b.Id) ? Guid.NewGuid().ToString() : b.Id,
                Title = string.IsNullOrWhiteSpace(b.Title) ? null : b.Title.Trim(),
                ImageUrl = string.IsNullOrWhiteSpace(b.ImageUrl) ? null : b.ImageUrl.Trim(),
                LinkUrl = string.IsNullOrWhiteSpace(b.LinkUrl) ? null : b.LinkUrl.Trim(),
                Enabled = b.Enabled,
                SortOrder = i
            })
            .ToList();

        await SaveConfig(BannerConfigKey, JsonSerializer.Serialize(cleaned), token);
        await SaveConfig(BannerIntervalKey, interval.ToString(), token);
        await db.SaveChangesAsync(token);

        return Ok(new HomeBannerConfigModel { IntervalMs = interval, Banners = cleaned });
    }

    async Task<List<HomeBannerModel>> LoadBanners(CancellationToken token)
    {
        var config = await db.Configs.FirstOrDefaultAsync(c => c.ConfigKey == BannerConfigKey, token);
        if (string.IsNullOrEmpty(config?.Value))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<HomeBannerModel>>(config.Value) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    async Task<int> LoadInterval(CancellationToken token)
    {
        var config = await db.Configs.FirstOrDefaultAsync(c => c.ConfigKey == BannerIntervalKey, token);
        return int.TryParse(config?.Value, out var interval)
            ? Math.Clamp(interval, MinIntervalMs, MaxIntervalMs)
            : DefaultIntervalMs;
    }

    async Task SaveConfig(string key, string value, CancellationToken token)
    {
        var config = await db.Configs.FirstOrDefaultAsync(c => c.ConfigKey == key, token);
        if (config is null)
            db.Configs.Add(new Config(key, value));
        else
            config.Value = value;

        await Task.CompletedTask;
    }
}

/// <summary>
/// 首页 Banner 配置：自动播放间隔 + Banner 列表
/// </summary>
public class HomeBannerConfigModel
{
    /// <summary>自动播放间隔（毫秒）</summary>
    [Range(MinInterval, MaxInterval)]
    public int IntervalMs { get; set; } = DefaultInterval;

    public List<HomeBannerModel>? Banners { get; set; }

    internal const int DefaultInterval = 5000;
    internal const int MinInterval = 1000;
    internal const int MaxInterval = 60000;
}

/// <summary>
/// 单条首页 Banner
/// </summary>
public class HomeBannerModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>标题（可为空，仅用于管理端识别）</summary>
    public string? Title { get; set; }

    /// <summary>图片地址</summary>
    public string? ImageUrl { get; set; }

    /// <summary>点击跳转地址</summary>
    public string? LinkUrl { get; set; }

    /// <summary>是否启用</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>排序权重，越小越靠前</summary>
    public int SortOrder { get; set; }
}
