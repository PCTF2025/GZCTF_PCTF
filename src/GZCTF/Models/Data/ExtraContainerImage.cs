namespace GZCTF.Models.Data;

/// <summary>
/// 附加靶机镜像。
///
/// 用于单题多靶机模式：除主容器外，题目可再配套若干容器，
/// 启动实例时全部创建，选手获得多个独立入口。
/// </summary>
public class ExtraContainerImage
{
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
    /// 是否启用流量捕获
    /// </summary>
    public bool EnableTrafficCapture { get; set; }
}
