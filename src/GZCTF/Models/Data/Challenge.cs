using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace GZCTF.Models.Data;

public class Challenge
{
    [Key]
    [Required]
    public int Id { get; set; }

    /// <summary>
    /// Challenge title
    /// </summary>
    [Required(ErrorMessageResourceName = nameof(Resources.Program.Model_TitleRequired),
        ErrorMessageResourceType = typeof(Resources.Program))]
    [MinLength(1, ErrorMessageResourceName = nameof(Resources.Program.Model_TitleTooShort),
        ErrorMessageResourceType = typeof(Resources.Program))]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Challenge content
    /// </summary>
    [Required(ErrorMessageResourceName = nameof(Resources.Program.Model_ContentRequired),
        ErrorMessageResourceType = typeof(Resources.Program))]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 题目创建者（出题人）。用于判定「出题人只能改自己的题目」
    /// </summary>
    public Guid? AuthorId { get; set; }

    /// <summary>
    /// Challenge category
    /// </summary>
    [Required]
    [JsonConverter(typeof(JsonStringEnumConverter<ChallengeCategory>))]
    public ChallengeCategory Category { get; set; } = ChallengeCategory.Misc;

    /// <summary>
    /// Challenge type, cannot be changed after creation
    /// </summary>
    [Required]
    [JsonConverter(typeof(JsonStringEnumConverter<ChallengeType>))]
    public ChallengeType Type { get; set; } = ChallengeType.StaticAttachment;

    /// <summary>
    /// Challenge hints
    /// </summary>
    public List<string>? Hints { get; set; }

    /// <summary>
    /// Whether the challenge is enabled
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// The deadline of the challenge, null means no deadline
    /// </summary>
    public DateTimeOffset? DeadlineUtc { get; set; }

    /// <summary>
    /// Maximum number of submissions allowed per team (0 = no limit)
    /// </summary>
    [Required]
    public int SubmissionLimit { get; set; }

    /// <summary>
    /// Image name and tag
    /// </summary>
    public string? ContainerImage { get; set; } = string.Empty;

    /// <summary>
    /// 附加靶机镜像。
    ///
    /// 单题多靶机模式：一道题可以同时启动多个互相配合的容器
    /// （例如 Web 前端 + 数据库、多节点集群等），
    /// 选手启动实例时会一次性创建全部容器，各自有独立入口。
    ///
    /// 主镜像仍保存在 ContainerImage 中，本字段只存放附加靶机，
    /// 未使用该模式时为空列表，与原有行为完全一致。
    /// </summary>
    public List<ExtraContainerImage> ExtraContainerImages { get; set; } = [];

    /// <summary>
    /// 靶机拓扑节点。
    ///
    /// 单题多靶机模式的进阶形态：以「节点 + 连线」描述靶机编排，
    /// 一道题可以同时启动多个互相配合的容器，连线用于在题面中说明
    /// 它们之间应当如何互相访问。
    ///
    /// 拓扑仅作编排与题面说明用途，不会下发任何网络隔离策略。
    /// 当本字段为空时，回退到 ContainerImage + ExtraContainerImages 的星形拓扑，
    /// 未使用该模式时与原有行为完全一致。
    /// </summary>
    public List<TopologyNode> TopologyNodes { get; set; } = [];

    /// <summary>
    /// 靶机拓扑连线，仅引用 TopologyNodes 中的节点 Id
    /// </summary>
    public List<TopologyEdge> TopologyEdges { get; set; } = [];

    /// <summary>
    /// 拓扑节点数量上限
    /// </summary>
    public const int MaxTopologyNodes = 8;

    /// <summary>
    /// 规范化拓扑：剔除空镜像节点、裁剪名称、修补悬空连线、按上限截断。
    ///
    /// 至少保留一个入口节点：若没有任何节点被标记为入口，
    /// 则将排在最前的节点提升为入口，保证题目始终有可访问地址。
    /// </summary>
    public static (List<TopologyNode> Nodes, List<TopologyEdge> Edges) NormalizeTopology(
        IEnumerable<TopologyNode>? nodes, IEnumerable<TopologyEdge>? edges)
    {
        var list = nodes is null
            ? []
            : nodes
                .Where(n => !string.IsNullOrWhiteSpace(n.Image) && !string.IsNullOrWhiteSpace(n.Id))
                .Take(MaxTopologyNodes)
                .Select(n => new TopologyNode
                {
                    Id = n.Id.Trim(),
                    Name = string.IsNullOrWhiteSpace(n.Name) ? null : n.Name.Trim(),
                    Image = n.Image.Trim(),
                    ExposePort = n.ExposePort > 0 ? n.ExposePort : 80,
                    IsEntry = n.IsEntry,
                    EnableTrafficCapture = n.EnableTrafficCapture,
                    X = double.IsFinite(n.X) ? n.X : 0,
                    Y = double.IsFinite(n.Y) ? n.Y : 0
                })
                .ToList();

        if (list.Count > 0 && list.All(n => !n.IsEntry))
            list[0].IsEntry = true;

        var ids = list.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);

        var links = edges is null
            ? []
            : edges
                .Where(e => !string.IsNullOrWhiteSpace(e.From) && !string.IsNullOrWhiteSpace(e.To))
                .Where(e => ids.Contains(e.From.Trim()) && ids.Contains(e.To.Trim()))
                .Where(e => !string.Equals(e.From.Trim(), e.To.Trim(), StringComparison.Ordinal))
                .Select(e => new TopologyEdge
                {
                    Id = string.IsNullOrWhiteSpace(e.Id)
                        ? $"{e.From.Trim()}--{e.To.Trim()}"
                        : e.Id.Trim(),
                    From = e.From.Trim(),
                    To = e.To.Trim(),
                    Label = string.IsNullOrWhiteSpace(e.Label) ? null : e.Label.Trim()
                })
                // 去重：同一对节点的重复连线只保留一条
                .GroupBy(e => string.CompareOrdinal(e.From, e.To) <= 0
                    ? $"{e.From}|{e.To}"
                    : $"{e.To}|{e.From}")
                .Select(g => g.First())
                .ToList();

        return (list, links);
    }

    /// <summary>
    /// 附加靶机数量上限
    /// </summary>
    public const int MaxExtraContainerImages = 5;

    /// <summary>
    /// 规范化附加靶机列表：剔除空镜像、裁剪名称、按上限截断
    /// </summary>
    public static List<ExtraContainerImage> NormalizeExtraImages(IEnumerable<ExtraContainerImage>? images) =>
        images is null
            ? []
            : images
                .Where(i => !string.IsNullOrWhiteSpace(i.Image))
                .Take(MaxExtraContainerImages)
                .Select(i => new ExtraContainerImage
                {
                    Name = string.IsNullOrWhiteSpace(i.Name) ? null : i.Name.Trim(),
                    Image = i.Image.Trim(),
                    ExposePort = i.ExposePort > 0 ? i.ExposePort : 80,
                    EnableTrafficCapture = i.EnableTrafficCapture
                })
                .ToList();

    /// <summary>
    /// Memory limit (MB)
    /// </summary>
    public int? MemoryLimit { get; set; } = 64;

    /// <summary>
    /// Storage limit (MB)
    /// </summary>
    public int? StorageLimit { get; set; } = 256;

    /// <summary>
    /// CPU limit (0.1 CPUs)
    /// </summary>
    public int? CPUCount { get; set; } = 1;

    /// <summary>
    /// Container exposed port
    /// </summary>
    public int? ExposePort { get; set; } = 80;

    /// <summary>
    /// Container network mode
    /// </summary>
    public NetworkMode? NetworkMode { get; set; } = Utils.NetworkMode.Open;

    /// <summary>
    /// Download file name, used only for dynamic attachment unified file name
    /// </summary>
    public string? FileName { get; set; } = "attachment";

    /// <summary>
    /// Concurrency token
    /// </summary>
    [JsonIgnore]
    [Timestamp]
    public uint ConcurrencyToken { get; set; }

    /// <summary>
    /// Flag template, used to generate flags based on token and challenge, game information
    /// </summary>
    [MaxLength(Limits.MaxFlagTemplateLength)]
    public string? FlagTemplate { get; set; }

    /// <summary>
    /// Generate dynamic flag for the participant
    /// </summary>
    /// <param name="part"></param>
    /// <returns></returns>
    internal string GenerateDynamicFlag(Participation part)
    {
        if (string.IsNullOrEmpty(FlagTemplate))
            return $"flag{Guid.NewGuid():B}";

        var generator = new DynamicFlagGenerator(FlagTemplate);
        return generator.GenerateWithTeamHash(() =>
        {
            var salt = $"{part.Game.TeamHashSalt}::{Id}".ToSHA256String();
            var hash = $"{salt}::{part.Token}".ToSHA256String();
            return hash[12..24];
        });
    }

    /// <summary>
    /// Directly generate dynamic flag
    /// </summary>
    internal string GenerateDynamicFlag()
    {
        if (string.IsNullOrEmpty(FlagTemplate))
            return $"flag{Guid.NewGuid():B}";

        var generator = new DynamicFlagGenerator(FlagTemplate);

        // Always use TestTeamHash for [TEAM_HASH] in parameterless version
        return generator.GenerateWithTeamHash(() => "TestTeamHash");
    }

    /// <summary>
    /// Generate test flag for admin to check the challenge
    /// </summary>
    internal string GenerateTestFlag()
    {
        if (string.IsNullOrEmpty(FlagTemplate))
            return "flag{GZCTF_dynamic_flag_test}";

        var generator = new DynamicFlagGenerator(FlagTemplate);
        return generator.GenerateTestFlag();
    }

    #region Db Relationship

    /// <summary>
    /// Challenge attachment ID
    /// </summary>
    public int? AttachmentId { get; set; }

    /// <summary>
    /// Challenge attachment (dynamic attachments are stored in FlagContext)
    /// </summary>
    public Attachment? Attachment { get; set; }

    /// <summary>
    /// Test container ID
    /// </summary>
    public Guid? TestContainerId { get; set; }

    /// <summary>
    /// Test container
    /// </summary>
    public Container? TestContainer { get; set; }

    /// <summary>
    /// List of flags for the challenge
    /// </summary>
    public List<FlagContext> Flags { get; set; } = [];

    #endregion
}
