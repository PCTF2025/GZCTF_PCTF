namespace GZCTF.Models.Request.Assets;

/// <summary>
/// 素材库文件条目
/// </summary>
public class FileItemModel
{
    /// <summary>
    /// 文件哈希（内链标识）
    /// </summary>
    public string Hash { get; set; } = string.Empty;

    /// <summary>
    /// 文件名
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 文件大小（字节）
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// 上传时间
    /// </summary>
    public DateTimeOffset UploadTimeUtc { get; set; }

    /// <summary>
    /// 引用计数
    /// </summary>
    public uint ReferenceCount { get; set; }

    /// <summary>
    /// 可直接用于 Markdown / HTML 的内链地址
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// 上传者 Id（非管理员仅能看到自己上传的素材）
    /// </summary>
    public Guid? UploadedById { get; set; }
}

/// <summary>
/// 素材库文件列表
/// </summary>
public class FileListModel
{
    /// <summary>
    /// 文件总数
    /// </summary>
    public int Total { get; set; }

    /// <summary>
    /// 当前页文件
    /// </summary>
    public FileItemModel[] Files { get; set; } = [];
}
