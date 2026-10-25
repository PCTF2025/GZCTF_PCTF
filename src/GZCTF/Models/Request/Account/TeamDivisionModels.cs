namespace GZCTF.Models.Request.Account;

/// <summary>
/// 可供当前用户调整组别的比赛（其队伍中含本校学生）
/// </summary>
public class TeamDivisionGameModel
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;
}

/// <summary>
/// 队伍组别管理：列表（含可选组别）
/// </summary>
public class TeamDivisionListModel
{
    /// <summary>该比赛可选的组别</summary>
    public List<TeamDivisionOptionModel> Divisions { get; set; } = [];

    /// <summary>队伍列表（学校负责人仅能看到本校本队）</summary>
    public List<TeamDivisionItemModel> Items { get; set; } = [];
}

/// <summary>
/// 组别选项
/// </summary>
public class TeamDivisionOptionModel
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// 单支队伍及其成员
/// </summary>
public class TeamDivisionItemModel
{
    public int ParticipationId { get; set; }

    public string TeamName { get; set; } = string.Empty;

    /// <summary>报名审核状态</summary>
    public string Status { get; set; } = string.Empty;

    public int? DivisionId { get; set; }

    public string? DivisionName { get; set; }

    /// <summary>队伍成员</summary>
    public List<TeamDivisionMemberModel> Members { get; set; } = [];
}

/// <summary>
/// 队伍成员（用于确认是否属于本校）
/// </summary>
public class TeamDivisionMemberModel
{
    public string UserName { get; set; } = string.Empty;

    public string? RealName { get; set; }

    public string? School { get; set; }

    public string? Grade { get; set; }
}

/// <summary>
/// 修改队伍组别
/// </summary>
public class TeamDivisionUpdateModel
{
    /// <summary>目标组别，传 null 表示不分组</summary>
    public int? DivisionId { get; set; }
}
