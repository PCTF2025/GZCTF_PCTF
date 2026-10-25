using GZCTF.Middlewares;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Request.Account;
using GZCTF.Services.Cache;
using GZCTF.Utils;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Controllers;

/// <summary>
/// 队伍组别调整。
///
/// 学校负责人（学籍审核人）可以在「队伍审核」中修改本校学生的队伍组别
/// （新生组 / 老生组等），但只能看到并修改包含本校学生的队伍，
/// 且只能改组别，不能改动审核状态。系统管理员不受此限制。
/// </summary>
[ApiController]
[Route("api/[controller]")]
[RequireSchoolManager]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status403Forbidden)]
public class TeamDivisionController(
    AppDbContext dbContext,
    UserManager<UserInfo> userManager,
    CacheHelper cacheHelper,
    ILogger<TeamDivisionController> logger) : ControllerBase
{
    /// <summary>
    /// 可调整组别的比赛列表
    /// </summary>
    /// <remarks>
    /// 只返回包含本校学生队伍的比赛；管理员返回全部比赛。
    /// </remarks>
    [HttpGet("Games")]
    [ProducesResponseType(typeof(TeamDivisionGameModel[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> Games(CancellationToken token)
    {
        var viewer = await userManager.GetUserAsync(User);

        if (viewer is null)
            return Unauthorized(new RequestResponse("请先登录"));

        var query = dbContext.Participations.AsNoTracking().AsQueryable();

        if (viewer.Role < Role.Admin)
        {
            var managed = viewer.ManagedSchoolList.ToArray();

            if (managed.Length == 0)
                return Ok(Array.Empty<TeamDivisionGameModel>());

            query = query.Where(p => p.Members.Any(m => m.User.School != null && managed.Contains(m.User.School)));
        }

        var games = await query
            .Select(p => new TeamDivisionGameModel { Id = p.GameId, Title = p.Game.Title })
            .Distinct()
            .OrderBy(g => g.Id)
            .ToListAsync(token);

        return Ok(games);
    }

    /// <summary>
    /// 某场比赛的队伍与组别列表
    /// </summary>
    /// <remarks>
    /// 学校负责人只会拿到包含本校学生的队伍；管理员拿到全部。
    /// </remarks>
    /// <param name="gameId">比赛 Id</param>
    /// <param name="token"></param>
    [HttpGet("Games/{gameId:int}")]
    [ProducesResponseType(typeof(TeamDivisionListModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(int gameId, CancellationToken token)
    {
        var viewer = await userManager.GetUserAsync(User);

        if (viewer is null)
            return Unauthorized(new RequestResponse("请先登录"));

        var gameExists = await dbContext.Games.AnyAsync(g => g.Id == gameId, token);

        if (!gameExists)
            return NotFound(new RequestResponse("比赛不存在", StatusCodes.Status404NotFound));

        var divisions = await dbContext.Divisions.AsNoTracking()
            .Where(d => d.GameId == gameId)
            .OrderBy(d => d.Id)
            .Select(d => new TeamDivisionOptionModel { Id = d.Id, Name = d.Name })
            .ToListAsync(token);

        var query = dbContext.Participations.AsNoTracking().Where(p => p.GameId == gameId);

        if (viewer.Role < Role.Admin)
        {
            var managed = viewer.ManagedSchoolList.ToArray();

            if (managed.Length == 0)
                return Ok(new TeamDivisionListModel { Divisions = divisions });

            // 只要队伍里有一名本校学生，该队伍即出现在列表中
            query = query.Where(p => p.Members.Any(m => m.User.School != null && managed.Contains(m.User.School)));
        }

        var items = await query
            .OrderBy(p => p.Team.Name)
            .Select(p => new TeamDivisionItemModel
            {
                ParticipationId = p.Id,
                TeamName = p.Team.Name,
                Status = p.Status.ToString(),
                DivisionId = p.DivisionId,
                DivisionName = p.Division != null ? p.Division.Name : null,
                Members = p.Members
                    .Select(m => new TeamDivisionMemberModel
                    {
                        UserName = m.User.UserName ?? string.Empty,
                        RealName = m.User.RealName,
                        School = m.User.School,
                        Grade = m.User.Grade
                    })
                    .ToList()
            })
            .ToListAsync(token);

        return Ok(new TeamDivisionListModel { Divisions = divisions, Items = items });
    }

    /// <summary>
    /// 修改队伍组别
    /// </summary>
    /// <remarks>
    /// 学校负责人只能修改包含本校学生的队伍，且只能改组别。
    /// </remarks>
    /// <param name="id">报名 Id</param>
    /// <param name="model">目标组别</param>
    /// <param name="token"></param>
    [HttpPut("Participation/{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateDivision(int id, [FromBody] TeamDivisionUpdateModel model,
        CancellationToken token)
    {
        var viewer = await userManager.GetUserAsync(User);

        if (viewer is null)
            return Unauthorized(new RequestResponse("请先登录"));

        var participation = await dbContext.Participations
            .Include(p => p.Members).ThenInclude(m => m.User)
            .FirstOrDefaultAsync(p => p.Id == id, token);

        if (participation is null)
            return NotFound(new RequestResponse("报名不存在", StatusCodes.Status404NotFound));

        if (viewer.Role < Role.Admin)
        {
            var managed = viewer.ManagedSchoolList;

            // 必须至少有一名本校学生在该队伍中
            var belongs = participation.Members.Any(m =>
                !string.IsNullOrWhiteSpace(m.User.School) &&
                managed.Any(s => string.Equals(s, m.User.School, StringComparison.OrdinalIgnoreCase)));

            if (!belongs)
                return StatusCode(StatusCodes.Status403Forbidden,
                    new RequestResponse("该队伍中没有你负责学校的学生"));
        }

        if (model.DivisionId is { } divisionId)
        {
            var valid = await dbContext.Divisions
                .AnyAsync(d => d.Id == divisionId && d.GameId == participation.GameId, token);

            if (!valid)
                return BadRequest(new RequestResponse("所选组别不属于该场比赛"));
        }

        participation.DivisionId = model.DivisionId;
        await dbContext.SaveChangesAsync(token);

        // 组别影响排行榜分组，需要刷新缓存
        await cacheHelper.FlushScoreboardCache(participation.GameId, token);

        logger.SystemLog(
            $"调整队伍组别：participation={participation.Id} team={participation.TeamId} division={(model.DivisionId?.ToString() ?? "(未分组)")}",
            TaskStatus.Success, LogLevel.Information);

        return Ok();
    }
}
