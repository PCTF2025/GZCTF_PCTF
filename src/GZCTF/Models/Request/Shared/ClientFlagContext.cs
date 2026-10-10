using GZCTF.Utils;

namespace GZCTF.Models.Request.Shared;

public class ClientFlagContext
{
    /// <summary>
    /// Close time of the challenge instance
    /// </summary>
    public DateTimeOffset? CloseTime { get; set; }

    /// <summary>
    /// Connection method of the challenge instance
    /// </summary>
    public string? InstanceEntry { get; set; }

    /// <summary>
    /// 单题多靶机模式下各靶机的独立入口。
    /// 单靶机题目与历史数据为空，前端按 InstanceEntry 渲染原有单一入口。
    /// </summary>
    public List<ContainerEntryModel>? InstanceEntries { get; set; }

    /// <summary>
    /// Attachment URL
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// Attachment file size
    /// </summary>
    public long? FileSize { get; set; }
}

/// <summary>
/// 单个靶机的连接信息
/// </summary>
public class ContainerEntryModel
{
    /// <summary>
    /// 靶机名称（如 Web、数据库）；未命名时前端按序号展示
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 靶机连接地址
    /// </summary>
    public string? Entry { get; set; }

    /// <summary>
    /// 该靶机的关闭时间
    /// </summary>
    public DateTimeOffset? CloseTime { get; set; }

    /// <summary>
    /// 靶机状态
    /// </summary>
    public ContainerStatus Status { get; set; }
}
