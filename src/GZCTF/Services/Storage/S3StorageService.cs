using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using GZCTF.Models;
using GZCTF.Models.Internal;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Services.Storage;

/// <summary>
/// 对象存储读写（S3 兼容 / 阿里云 OSS）
///
/// 配置来自后台「对象存储」页面，未配置时所有方法返回未启用。
/// Endpoint 会自动归一化（补协议、去桶前缀、阿里云强制虚拟主机风格）。
/// </summary>
public class S3StorageService(AppDbContext context, ILogger<S3StorageService> logger)
{
    public const string KeyPrefix = "traffic";

    /// <summary>
    /// 读取当前配置并构造客户端；未配置时返回 null
    /// </summary>
    public async Task<(IAmazonS3 Client, string Bucket)?> CreateClientAsync(CancellationToken token)
    {
        var dict = await context.Configs.AsNoTracking()
            .Where(c => c.ConfigKey.StartsWith("StorageConfig:"))
            .ToDictionaryAsync(c => c.ConfigKey, c => c.Value, token);

        string Get(string name) => dict.TryGetValue($"StorageConfig:{name}", out var v) ? v ?? string.Empty : string.Empty;

        var endpoint = Get("Endpoint");
        var bucket = Get("Bucket");
        var accessKey = Get("AccessKeyId");
        var secret = Get("AccessKeySecret");

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(bucket) ||
            string.IsNullOrWhiteSpace(accessKey) || string.IsNullOrWhiteSpace(secret))
            return null;

        var forcePathStyle = dict.TryGetValue("StorageConfig:ForcePathStyle", out var fps) &&
                             bool.TryParse(fps, out var parsed) && parsed;

        endpoint = Normalize(endpoint, bucket);

        // 阿里云 OSS 只接受虚拟主机风格
        if (endpoint.Contains(".aliyuncs.com", StringComparison.OrdinalIgnoreCase))
            forcePathStyle = false;

        var config = new AmazonS3Config
        {
            ServiceURL = endpoint,
            ForcePathStyle = forcePathStyle,
            Timeout = TimeSpan.FromSeconds(30),
            MaxErrorRetry = 2,
            // 阿里云 OSS 不支持 AWS 的分块校验 trailer
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
        };

        return (new AmazonS3Client(new BasicAWSCredentials(accessKey, secret), config), bucket);
    }

    /// <summary>
    /// 上传文件
    /// </summary>
    public async Task<bool> UploadAsync(Stream stream, string key, string contentType,
        CancellationToken token = default)
    {
        var client = await CreateClientAsync(token);
        if (client is null)
            return false;

        try
        {
            // 先读入内存以拿到确定长度：OSS 对 chunked 上传支持不佳
            await using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, token);
            buffer.Position = 0;

            var request = new PutObjectRequest
            {
                BucketName = client.Value.Bucket,
                Key = key,
                InputStream = buffer,
                ContentType = contentType,
                AutoCloseStream = false,
                // 关键：关闭分块编码与默认校验，否则 OSS 会拒绝 STREAMING-* 载荷
                UseChunkEncoding = false,
                DisableDefaultChecksumValidation = true,
                DisablePayloadSigning = false
            };

            await client.Value.Client.PutObjectAsync(request, token);

            return true;
        }
        catch (AmazonS3Exception e)
        {
            logger.LogErrorMessage(e,
                $"上传对象存储失败：{key} | code={e.ErrorCode} status={e.StatusCode} msg={e.Message}");
            return false;
        }
        catch (Exception e)
        {
            logger.LogErrorMessage(e, $"上传对象存储失败：{key} | {e.GetType().Name}: {e.Message}");
            return false;
        }
        finally
        {
            client.Value.Client.Dispose();
        }
    }

    /// <summary>
    /// 生成带签名的下载地址；对象不存在返回 null
    /// </summary>
    public async Task<string?> GetPresignedUrlAsync(string key, TimeSpan lifetime, CancellationToken token = default)
    {
        var client = await CreateClientAsync(token);
        if (client is null)
            return null;

        try
        {
            var url = await client.Value.Client.GetPreSignedURLAsync(new GetPreSignedUrlRequest
            {
                BucketName = client.Value.Bucket,
                Key = key,
                Verb = HttpVerb.GET,
                Expires = DateTime.UtcNow.Add(lifetime)
            });
            return url;
        }
        catch (Exception e)
        {
            logger.LogErrorMessage(e, $"生成对象存储下载地址失败：{key}");
            return null;
        }
        finally
        {
            client.Value.Client.Dispose();
        }
    }

    private static string Normalize(string endpoint, string bucket)
    {
        var value = endpoint.Trim();

        if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            value = "https://" + value.TrimStart('/');

        value = value.TrimEnd('/');

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return value;

        var labels = uri.Host.Split('.');
        var isBucketHost = labels.Length >= 4 &&
                           (labels[1].StartsWith("oss-", StringComparison.OrdinalIgnoreCase) ||
                            labels[1].Equals("s3", StringComparison.OrdinalIgnoreCase));

        if (isBucketHost)
        {
            value = new UriBuilder(uri) { Host = string.Join('.', labels.Skip(1)) }
                .Uri.GetLeftPart(UriPartial.Authority);
        }

        return value;
    }
}
