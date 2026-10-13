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
    /// 靶机拓扑（仅用于题面展示的只读图形）。
    ///
    /// 连线只描述靶机之间应当如何互相访问，平台不施加任何网络隔离。
    /// 未使用可视化编排的题目为 null。
    /// </summary>
    public TopologyGraphModel? InstanceTopology { get; set; }

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

/// <summary>
/// 题目靶机拓扑（只读展示）
/// </summary>
public class TopologyGraphModel
{
    /// <summary>
    /// 拓扑节点，Id 与连线两端对应
    /// </summary>
    public List<TopologyNodeModel> Nodes { get; set; } = [];

    /// <summary>
    /// 拓扑连线
    /// </summary>
    public List<TopologyLinkModel> Links { get; set; } = [];
}

/// <summary>
/// 拓扑节点（下发给选手的只读视图）
/// </summary>
public class TopologyNodeModel
{
    /// <summary>
    /// 节点标识
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 靶机名称
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 暴露端口
    /// </summary>
    public int ExposePort { get; set; }

    /// <summary>
    /// 是否为入口靶机（地址会下发给选手）
    /// </summary>
    public bool IsEntry { get; set; }

    /// <summary>
    /// 该节点在实例列表中的序号，用于与 InstanceEntries 对齐；-1 表示未创建
    /// </summary>
    public int InstanceIndex { get; set; } = -1;

    /// <summary>
    /// 画布横坐标
    /// </summary>
    public double X { get; set; }

    /// <summary>
    /// 画布纵坐标
    /// </summary>
    public double Y { get; set; }
}

/// <summary>
/// 拓扑连线（下发给选手的只读视图）
/// </summary>
public class TopologyLinkModel
{
    /// <summary>
    /// 连线标识
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 起点节点 Id
    /// </summary>
    public string From { get; set; } = string.Empty;

    /// <summary>
    /// 终点节点 Id
    /// </summary>
    public string To { get; set; } = string.Empty;

    /// <summary>
    /// 连线说明（协议或访问方式）
    /// </summary>
    public string? Label { get; set; }
}
