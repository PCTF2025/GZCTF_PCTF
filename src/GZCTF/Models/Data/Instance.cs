using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace GZCTF.Models.Data;

public class Instance
{
    /// <summary>
    /// Whether the challenge is loaded
    /// </summary>
    public bool IsLoaded { get; set; }

    /// <summary>
    /// Concurrency token
    /// </summary>
    [JsonIgnore]
    [Timestamp]
    public uint ConcurrencyToken { get; set; }

    /// <summary>
    /// Last container operation time to ensure operations are not too frequent
    /// </summary>
    public DateTimeOffset LastContainerOperation { get; set; } = DateTimeOffset.MinValue;

    [NotMapped]
    public bool IsContainerOperationTooFrequent =>
        DateTimeOffset.UtcNow - LastContainerOperation < TimeSpan.FromSeconds(10);

    #region Db Relationship

    public int? FlagId { get; set; }

    /// <summary>
    /// Flag context object
    /// </summary>
    public FlagContext? FlagContext { get; set; }

    public Guid? ContainerId { get; set; }

    /// <summary>
    /// Container object
    /// </summary>
    public Container? Container { get; set; }

    /// <summary>
    /// 本实例的全部容器（单题多靶机模式，按 GroupOrder 排序）。
    ///
    /// 非 EF 导航属性：由仓储在需要时按 GroupId 装载并填充，
    /// 避免为一个可选特性改动既有的 one-to-one 映射。
    /// 单靶机题目与历史数据只有 Container 一个容器。
    /// </summary>
    [NotMapped]
    [JsonIgnore]
    public List<Container> Containers { get; set; } = [];

    /// <summary>
    /// 实例是否为多靶机
    /// </summary>
    [NotMapped]
    [JsonIgnore]
    public bool IsMultiContainer => Containers.Count > 1;


    #endregion
}
