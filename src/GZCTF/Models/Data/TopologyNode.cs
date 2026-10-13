namespace GZCTF.Models.Data;

/// <summary>
/// 靶机拓扑节点。
///
/// 用于单题多靶机模式的「编排 + 题面说明」：
/// 每个节点对应一个容器，节点之间的连线描述它们如何互相访问。
/// 拓扑本身不下发任何网络隔离策略，连线只作为题面提示展示给选手。
/// </summary>
public class TopologyNode
{
    /// <summary>
    /// 节点标识，由前端生成，用于连线两端引用
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 靶机名称，用于在实例列表中区分各入口
    /// （如「Web」「数据库」「Node-1」）。留空时按序号展示。
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 镜像名与 tag
    /// </summary>
    public string Image { get; set; } = string.Empty;

    /// <summary>
    /// 容器暴露端口
    /// </summary>
    public int ExposePort { get; set; } = 80;

    /// <summary>
    /// 是否把入口地址下发给选手。
    /// 未勾选的节点仍会创建，但选手只能通过题面描述的其他靶机间接访问。
    /// </summary>
    public bool IsEntry { get; set; }

    /// <summary>
    /// 是否启用流量捕获
    /// </summary>
    public bool EnableTrafficCapture { get; set; }

    /// <summary>
    /// 画布横坐标（仅用于编辑器还原布局）
    /// </summary>
    public double X { get; set; }

    /// <summary>
    /// 画布纵坐标
    /// </summary>
    public double Y { get; set; }
}
