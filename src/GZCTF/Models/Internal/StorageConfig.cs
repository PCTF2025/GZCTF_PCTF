namespace GZCTF.Models.Internal;

/// <summary>
/// 对象存储（S3 兼容 / OSS）配置
///
/// 目前仅做配置保存与连通性测试，实际上传切换在后续版本接入。
/// AccessKeySecret 只写不读：接口永不回传明文，日志也会打码。
/// </summary>
public class StorageConfig
{
    /// <summary>OSS 站点（Endpoint），如 https://oss-cn-hangzhou.aliyuncs.com</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>存储桶名称</summary>
    public string Bucket { get; set; } = string.Empty;

    /// <summary>AccessKeyId</summary>
    public string AccessKeyId { get; set; } = string.Empty;

    /// <summary>AccessKeySecret（只写，不返回）</summary>
    public string AccessKeySecret { get; set; } = string.Empty;

    /// <summary>区域，可留空（自定义 Endpoint 时通常不需要）</summary>
    public string Region { get; set; } = string.Empty;

    /// <summary>是否使用路径风格（多数第三方 OSS 需要开启）</summary>
    public bool ForcePathStyle { get; set; } = true;
}
