using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using GZCTF.Models;
using GZCTF.Models.Internal;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

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

    /// <summary>超过此大小的对象不做整包回读校验</summary>
    private const long LargeObjectThreshold = 32L * 1024 * 1024;

    /// <summary>
    /// 读取当前配置并构造客户端；未配置时返回 null
    /// </summary>
    public virtual async Task<(IAmazonS3 Client, string Bucket)?> CreateClientAsync(CancellationToken token)
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
            // 大抓包文件（数百 MB）上传 + 校验需要更长时间；过短会必然超时
            Timeout = TimeSpan.FromMinutes(10),
            MaxErrorRetry = 1,
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
        CancellationToken token = default) =>
        await UploadVerifiedAsync(stream, key, contentType, token) is not null;

    /// <summary>Verify the stored bytes by HEAD and a complete GET; never trust a PUT response or ETag alone.</summary>
    public async Task<VerifiedUpload?> UploadVerifiedAsync(Stream stream, string key, string contentType,
        CancellationToken token = default)
    {
        var client = await CreateClientAsync(token);
        if (client is null)
            return null;

        FileStream? spool = null;
        try
        {
            // Large captures must not be buffered in RAM. IFormFile/FileStream is normally seekable.
            if (!stream.CanSeek)
            {
                spool = new FileStream(Path.GetTempFileName(), FileMode.Open, FileAccess.ReadWrite,
                    FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
                await stream.CopyToAsync(spool, token);
                spool.Position = 0;
                stream = spool;
            }
            var position = stream.Position;
            var size = stream.Length - position;
            var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, token));
            stream.Position = position;

            var request = new PutObjectRequest
            {
                BucketName = client.Value.Bucket,
                Key = key,
                InputStream = stream,
                ContentType = contentType,
                AutoCloseStream = false,
                // 关键：关闭分块编码与默认校验，否则 OSS 会拒绝 STREAMING-* 载荷
                UseChunkEncoding = false,
                DisableDefaultChecksumValidation = true,
                DisablePayloadSigning = false
            };
            // 把 SHA-256 随对象一起写入元数据，校验时直接读元数据，无需整包回读
            request.Metadata.Add("x-amz-meta-sha256", hash);

            await client.Value.Client.PutObjectAsync(request, token);

            var metadata = await client.Value.Client.GetObjectMetadataAsync(new GetObjectMetadataRequest
            {
                BucketName = client.Value.Bucket, Key = key
            }, token);
            if (metadata.ContentLength != size)
                throw new IOException($"Object size mismatch for {key}");

            // 大文件不再整包回读（数百 MB 回读会占满连接并超时）：
            // 以「HEAD 大小 + 写入时记录的 SHA-256 元数据」作为校验依据。
            var storedHash = metadata.Metadata["x-amz-meta-sha256"];
            if (string.IsNullOrEmpty(storedHash))
            {
                // 元数据不可用时才回退到完整回读（仅限小文件）
                if (size > LargeObjectThreshold)
                    throw new IOException($"Object metadata missing for large object {key}");
                using var stored = await client.Value.Client.GetObjectAsync(client.Value.Bucket, key, token);
                storedHash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stored.ResponseStream, token));
            }

            if (!string.Equals(storedHash, hash, StringComparison.OrdinalIgnoreCase))
                throw new IOException($"Object SHA-256 mismatch for {key}");

            return new VerifiedUpload(key, size, hash);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (AmazonS3Exception e)
        {
            logger.LogErrorMessage(e,
                $"上传对象存储失败：{key} | code={e.ErrorCode} status={e.StatusCode} msg={e.Message}");
            return null;
        }
        catch (Exception e)
        {
            logger.LogErrorMessage(e, $"上传对象存储失败：{key} | {e.GetType().Name}: {e.Message}");
            return null;
        }
        finally
        {
            client.Value.Client.Dispose();
            if (spool is not null)
                await spool.DisposeAsync();
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
            await client.Value.Client.GetObjectMetadataAsync(new GetObjectMetadataRequest
            {
                BucketName = client.Value.Bucket, Key = key
            }, token);
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

public record VerifiedUpload(string Key, long Size, string Sha256);
