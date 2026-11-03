using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using GZCTF.Middlewares;
using GZCTF.Models;
using GZCTF.Models.Internal;
using GZCTF.Services.Config;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Controllers;

/// <summary>
/// 对象存储（OSS / S3 兼容）配置
///
/// 安全约定：
/// - AccessKeySecret 只写不读，GET 永远不返回明文，只返回「是否已配置」；
/// - 保存时若前端传空或掩码，保留原有密钥；
/// - 仅系统管理员可访问。
/// </summary>
[ApiController]
[Route("api/[controller]")]
[RequireAdmin]
public class StorageSettingsController(
    AppDbContext context,
    IConfigService configService,
    ILogger<StorageSettingsController> logger) : ControllerBase
{
    private const string SecretMask = "******";

    private const string SecretKey = "StorageConfig:AccessKeySecret";

    /// <summary>
    /// 读取对象存储配置（密钥不回传）
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken token)
    {
        var config = await LoadAsync(token);
        var hasSecret = !string.IsNullOrWhiteSpace(await GetSecretAsync(token));

        return Ok(new
        {
            endpoint = config.Endpoint,
            bucket = config.Bucket,
            accessKeyId = config.AccessKeyId,
            region = config.Region,
            forcePathStyle = config.ForcePathStyle,
            hasSecret
        });
    }

    /// <summary>
    /// 保存对象存储配置
    /// </summary>
    /// <remarks>accessKeySecret 留空或传 ****** 表示不修改已有密钥</remarks>
    [HttpPut]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update([FromBody] StorageConfig model, CancellationToken token)
    {
        var current = await LoadAsync(token);

        // 未填密钥则沿用原值，避免前端「看不见就覆盖成空」
        if (string.IsNullOrWhiteSpace(model.AccessKeySecret) || model.AccessKeySecret == SecretMask)
            model.AccessKeySecret = await GetSecretAsync(token);

        model.Endpoint = model.Endpoint?.Trim() ?? string.Empty;
        model.Bucket = model.Bucket?.Trim() ?? string.Empty;
        model.AccessKeyId = model.AccessKeyId?.Trim() ?? string.Empty;
        model.Region = model.Region?.Trim() ?? string.Empty;

        await configService.SaveConfig(model, token);
        configService.ReloadConfig();

        logger.SystemLog(
            $"对象存储配置已更新：endpoint={model.Endpoint} bucket={model.Bucket} " +
            $"accessKeyId={model.AccessKeyId} secret={(string.IsNullOrWhiteSpace(model.AccessKeySecret) ? "空" : "已设置")}" +
            (current.Endpoint == model.Endpoint ? "" : $"（原 endpoint={current.Endpoint}）"),
            TaskStatus.Success, LogLevel.Information);

        return Ok();
    }

    /// <summary>
    /// 测试连通性（只验证凭据与桶是否可访问）
    /// </summary>
    [HttpPost("Test")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Test(CancellationToken token)
    {
        var config = await LoadAsync(token);
        var secret = await GetSecretAsync(token);

        if (string.IsNullOrWhiteSpace(config.Endpoint) || string.IsNullOrWhiteSpace(config.Bucket) ||
            string.IsNullOrWhiteSpace(config.AccessKeyId) || string.IsNullOrWhiteSpace(secret))
            return Ok(new { ok = false, message = "请先补全 Endpoint / Bucket / AccessKeyId / AccessKeySecret" });

        try
        {
            var s3Config = new AmazonS3Config { ServiceURL = config.Endpoint, ForcePathStyle = config.ForcePathStyle };
            if (!string.IsNullOrWhiteSpace(config.Region))
            {
                s3Config.ServiceURL = config.Endpoint;
            }

            using var client = new AmazonS3Client(new BasicAWSCredentials(config.AccessKeyId, secret), s3Config);
            await client.GetACLAsync(new GetACLRequest { BucketName = config.Bucket }, token);
            return Ok(new { ok = true, message = "连接成功，桶可访问" });
        }
        catch (Exception e)
        {
            logger.LogErrorMessage(e, "对象存储连通性测试失败");
            return Ok(new { ok = false, message = e.Message });
        }
    }

    private async Task<StorageConfig> LoadAsync(CancellationToken token)
    {
        var dict = await context.Configs.AsNoTracking()
            .Where(c => c.ConfigKey.StartsWith("StorageConfig:"))
            .ToDictionaryAsync(c => c.ConfigKey, c => c.Value, token);

        string Get(string name) => dict.TryGetValue($"StorageConfig:{name}", out var v) ? v ?? string.Empty : string.Empty;

        return new StorageConfig
        {
            Endpoint = Get(nameof(StorageConfig.Endpoint)),
            Bucket = Get(nameof(StorageConfig.Bucket)),
            AccessKeyId = Get(nameof(StorageConfig.AccessKeyId)),
            AccessKeySecret = Get(nameof(StorageConfig.AccessKeySecret)),
            Region = Get(nameof(StorageConfig.Region)),
            ForcePathStyle = !dict.TryGetValue("StorageConfig:ForcePathStyle", out var fps) ||
                             !bool.TryParse(fps, out var parsed) || parsed
        };
    }

    private async Task<string> GetSecretAsync(CancellationToken token) =>
        await context.Configs.AsNoTracking()
            .Where(c => c.ConfigKey == SecretKey)
            .Select(c => c.Value)
            .FirstOrDefaultAsync(token) ?? string.Empty;
}
