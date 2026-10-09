using System.ComponentModel.DataAnnotations;

namespace GZCTF.Models.Request.Game;

public class GameJoinModel
{
    /// <summary>
    /// Team ID for participation.
    /// 个人赛可直接留空（0 或 null），后端会为该用户自动创建一个个人队后报名。
    /// </summary>
    public int? TeamId { get; set; }

    /// <summary>
    /// Division for participation
    /// </summary>
    public int? DivisionId { get; set; }

    /// <summary>
    /// Invitation code for participation
    /// </summary>
    public string? InviteCode { get; set; }
}
