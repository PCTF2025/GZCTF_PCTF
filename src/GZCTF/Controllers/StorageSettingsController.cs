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
    /// Endpoint 归一化：允许只填域名（如 xxx.oss-cn-hangzhou.aliyuncs.com），自动补 https://
    /// </summary>
    private static string NormalizeEndpoint(string? endpoint)
    {
        var value = endpoint?.Trim() ?? string.Empty;

        if (value.Length == 0)
            return string.Empty;

        if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            value = "https://" + value.TrimStart('/');

        return value.TrimEnd('/');
    }

    /// <summary>
    /// 若 Endpoint 填的是「桶域名」（如 mybucket.oss-cn-hangzhou.aliyuncs.com），
    /// 去掉桶前缀，改成区域级 Endpoint，配合路径风格访问，避免请求路径变成 /bucket/bucket
    /// </summary>
    private static string StripBucketPrefix(string endpoint, string bucket)
    {
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(bucket))
            return endpoint;

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            return endpoint;

        var host = uri.Host;
        var labels = host.Split('.');

        // 形如 <bucket>.oss-cn-hangzhou.aliyuncs.com / <bucket>.s3.us-east-1.amazonaws.com
        // 这类「桶域名」需要去掉首段，改成区域级 Endpoint
        var isBucketHost = labels.Length >= 4 &&
                           (labels[1].StartsWith("oss-", StringComparison.OrdinalIgnoreCase) ||
                            labels[1].Equals("oss", StringComparison.OrdinalIgnoreCase) ||
                            labels[1].Equals("s3", StringComparison.OrdinalIgnoreCase));

        // 配置的桶名与域名首段一致的，也按桶域名处理
        if (!isBucketHost && !string.IsNullOrWhiteSpace(bucket) &&
            host.StartsWith(bucket + ".", StringComparison.OrdinalIgnoreCase))
            isBucketHost = true;

        if (!isBucketHost)
            return endpoint;

        var builder = new UriBuilder(uri) { Host = string.Join('.', labels.Skip(1)) };
        return builder.Uri.GetLeftPart(UriPartial.Authority);
    }

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

        model.Endpoint = StripBucketPrefix(NormalizeEndpoint(model.Endpoint), model.Bucket);
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
        config.Endpoint = StripBucketPrefix(NormalizeEndpoint(config.Endpoint), config.Bucket);

        if (string.IsNullOrWhiteSpace(config.Endpoint) || string.IsNullOrWhiteSpace(config.Bucket) ||
            string.IsNullOrWhiteSpace(config.AccessKeyId) || string.IsNullOrWhiteSpace(secret))
            return Ok(new { ok = false, message = "请先补全 Endpoint / Bucket / AccessKeyId / AccessKeySecret" });

        try
        {
            var s3Config = new AmazonS3Config
            {
                ServiceURL = config.Endpoint,
                ForcePathStyle = config.ForcePathStyle,
                Timeout = TimeSpan.FromSeconds(8),
                MaxErrorRetry = 1
            };

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(TimeSpan.FromSeconds(15));

            using var client = new AmazonS3Client(new BasicAWSCredentials(config.AccessKeyId, secret), s3Config);

            // 用列出对象验证：既能验证桶存在，也能验证读权限（GetACL 在部分 OSS 上不被支持）
            await client.ListObjectsV2Async(
                new ListObjectsV2Request { BucketName = config.Bucket, MaxKeys = 1 }, cts.Token);

            return Ok(new { ok = true, message = $"连接成功，桶可访问（Endpoint: {config.Endpoint}）" });
        }
        catch (OperationCanceledException)
        {
            return Ok(new { ok = false, message = "连接超时（15 秒），请检查 Endpoint 或服务器出网情况" });
        }
        catch (AmazonS3Exception e)
        {
            var hint = e.ErrorCode switch
            {
                "NoSuchBucket" => "桶不存在，请检查 Bucket 名称",
                "AccessDenied" or "InvalidAccessKeyId" or "SignatureDoesNotMatch" => "密钥或权限不正确",
                "NoSuchKey" => "Endpoint 与 Bucket 不匹配（请填区域级 Endpoint，如 oss-cn-hangzhou.aliyuncs.com）",
                _ => e.Message
            };
            logger.LogErrorMessage(e, $"对象存储连通性测试失败：{e.ErrorCode}");
            return Ok(new { ok = false, message = hint });
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
