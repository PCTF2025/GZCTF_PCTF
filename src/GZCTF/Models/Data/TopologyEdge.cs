namespace GZCTF.Models.Data;

/// <summary>
/// 靶机拓扑连线。
///
/// 描述两个靶机节点之间应该如何互相访问，仅作为题面说明展示，
/// 不会生成任何实际的网络策略。
/// </summary>
public class TopologyEdge
{
    /// <summary>
    /// 连线标识，由前端生成
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
    /// 连线说明，用于标注协议或访问方式
    /// （如 HTTP、SQL、Redis）。留空时画布上不显示文字。
    /// </summary>
    public string? Label { get; set; }
}
