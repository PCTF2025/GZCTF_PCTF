using GZCTF.Models.Request.Shared;

namespace GZCTF.Models.Request.Game;

/// <summary>
/// Challenge detailed information
/// </summary>
public class ChallengeDetailModel
{
    /// <summary>
    /// Challenge ID
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Challenge title
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Challenge content
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Challenge category
    /// </summary>
    public ChallengeCategory Category { get; set; } = ChallengeCategory.Misc;

    /// <summary>
    /// Challenge hints
    /// </summary>
    public List<string>? Hints { get; set; }

    /// <summary>
    /// Current score of the challenge
    /// </summary>
    public int Score { get; set; }

    /// <summary>
    /// Challenge type
    /// </summary>
    public ChallengeType Type { get; set; } = ChallengeType.StaticAttachment;

    /// <summary>
    /// Flag context
    /// </summary>
    public ClientFlagContext Context { get; set; } = null!;

    /// <summary>
    /// Maximum number of attempts allowed (0 = no limit)
    /// </summary>
    public int Limit { get; set; }

    /// <summary>
    /// Current attempt count
    /// </summary>
    public int Attempts { get; set; }

    /// <summary>
    /// Deadline of the challenge, null means no deadline
    /// </summary>
    public DateTimeOffset? Deadline { get; set; }

    /// <summary>
    /// Recruitment week (1-5 for weeks, 6 for extension, null if not assigned)
    /// </summary>
    public int? Week { get; set; }

    internal static ChallengeDetailModel FromInstance(GameInstance gameInstance, int attemptCount,
        ChallengeInfo? scoreboardChallenge = null) =>
        new()
        {
            Id = gameInstance.Challenge.Id,
            Content = gameInstance.Challenge.Content,
            Hints = gameInstance.Challenge.Hints,
            Score = scoreboardChallenge?.Score ?? gameInstance.Challenge.CurrentScore,
            Category = gameInstance.Challenge.Category,
            Title = gameInstance.Challenge.Title,
            Type = gameInstance.Challenge.Type,
            Limit = gameInstance.Challenge.SubmissionLimit,
            Deadline = gameInstance.Challenge.DeadlineUtc,
            Week = gameInstance.Challenge.Week,
            Attempts = attemptCount,
            Context = new()
            {
                InstanceEntry = gameInstance.Container?.Entry,
                CloseTime = gameInstance.Container?.ExpectStopAt,
                // 单题多靶机：装载了多容器时下发给前端，前端按靶机列表展示各入口；
                // 单靶机题目此字段为 null，前端保持原有单一入口渲染。
                InstanceEntries = gameInstance.IsMultiContainer
                    ? gameInstance.Containers
                        .OrderBy(c => c.GroupOrder)
                        .Select(c => new ContainerEntryModel
                        {
                            Name = c.GroupName,
                            Entry = c.Entry,
                            CloseTime = c.ExpectStopAt,
                            Status = c.Status
                        })
                        .ToList()
                    : null,
                // 靶机拓扑：只读图形，连线仅作题面说明，平台不施加网络隔离
                InstanceTopology = BuildTopology(gameInstance.Challenge, gameInstance),
                Url = gameInstance.AttachmentUrl,
                FileSize = gameInstance.Attachment?.FileSize
            }
        };

    /// <summary>
    /// 组装只读拓扑视图。
    ///
    /// 节点按创建顺序与实例容器对齐（InstanceIndex），前端借此把图形与入口卡片关联起来；
    /// 未配置拓扑的题目返回 null。
    /// </summary>
    private static TopologyGraphModel? BuildTopology(GameChallenge challenge, GameInstance gameInstance)
    {
        if (challenge.TopologyNodes.Count == 0)
            return null;

        var containers = gameInstance.Containers
            .OrderBy(c => c.GroupOrder)
            .ToList();

        var nodes = new List<TopologyNodeModel>();
        var used = new HashSet<int>();

        foreach (var n in challenge.TopologyNodes)
        {
            // 节点顺序即创建顺序：入口在最前，其余按画布纵向位置排列。
            // 这里按名称与已创建的容器对齐，便于前端把图形与入口卡片关联起来。
            var index = -1;

            for (var i = 0; i < containers.Count; i++)
            {
                if (used.Contains(i))
                    continue;

                if (string.Equals(containers[i].GroupName, n.Name, StringComparison.Ordinal))
                {
                    index = i;
                    break;
                }
            }

            if (index >= 0)
                used.Add(index);

            nodes.Add(new TopologyNodeModel
            {
                Id = n.Id,
                Name = n.Name,
                ExposePort = n.ExposePort,
                IsEntry = n.IsEntry,
                InstanceIndex = index,
                X = n.X,
                Y = n.Y
            });
        }

        return new TopologyGraphModel
        {
            Nodes = nodes,
            Links = challenge.TopologyEdges
                .Select(e => new TopologyLinkModel
                {
                    Id = e.Id,
                    From = e.From,
                    To = e.To,
                    Label = e.Label
                })
                .ToList()
        };
    }
}
