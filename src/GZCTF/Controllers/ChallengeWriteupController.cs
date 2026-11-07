using GZCTF.Middlewares;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Request.Game;
using GZCTF.Repositories.Interface;
using GZCTF.Services.Cache;
using GZCTF.Storage.Interface;
using GZCTF.Utils;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace GZCTF.Controllers;

/// <summary>
/// 挑战题解题思路（WP）提交与人工加分。
///
/// 队伍在「挑战题 WP」页面上传自己已解出题目的思路 PDF；
/// 系统管理员或该方向的出题人/出题管理员审核后手动加分，
/// 加分计入排行榜总分。排行榜在计算时会自动带上这部分分数。
/// </summary>
[ApiController]
[Route("api/[controller]")]
[RequireUser]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status403Forbidden)]
public class ChallengeWriteupController(
    AppDbContext dbContext,
    UserManager<UserInfo> userManager,
    IBlobRepository blobRepository,
    IBlobStorage storage,
    CacheHelper cacheHelper,
    ILogger<ChallengeWriteupController> logger) : ControllerBase
{
    private const long MaxFileSize = 20 * 1024 * 1024;

    /// <summary>
    /// 当前用户在该场比赛中所属的报名（含队伍）
    /// </summary>
    private async Task<Participation?> GetMyParticipation(int gameId, UserInfo user, CancellationToken token) =>
        await dbContext.Participations
            .Include(p => p.Team)
            .FirstOrDefaultAsync(p => p.GameId == gameId &&
                                      p.Status == ParticipationStatus.Accepted &&
                                      p.Members.Any(m => m.UserId == user.Id), token);

    /// <summary>
    /// 队伍视角：按周提交的 WP 状态（含历史按题提交记录）
    /// </summary>
    /// <param name="gameId">比赛 Id</param>
    /// <param name="token"></param>
    [HttpGet("Games/{gameId:int}")]
    [ProducesResponseType(typeof(ChallengeWriteupListModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(int gameId, CancellationToken token)
    {
        var user = await userManager.GetUserAsync(User);

        if (user is null)
            return Unauthorized(new RequestResponse("请先登录"));

        var participation = await GetMyParticipation(gameId, user, token);

        if (participation is null)
            return NotFound(new RequestResponse("你尚未报名该场比赛", StatusCodes.Status404NotFound));

        var game = await dbContext.Games.AsNoTracking().FirstOrDefaultAsync(g => g.Id == gameId, token);

        if (game is null)
            return NotFound(new RequestResponse("比赛不存在", StatusCodes.Status404NotFound));

        // 可提交的周次：已配置开始时间的周（1-5）
        var weeks = Enumerable.Range(1, 5)
            .Where(w => game.GetWeekWindow(w).Start is not null)
            .ToArray();

        var writeups = await dbContext.ChallengeWriteups.AsNoTracking()
            .Where(w => w.ParticipationId == participation.Id)
            .Include(w => w.File)
            .Include(w => w.Challenge)
            .OrderByDescending(w => w.SubmitTimeUtc)
            .ToListAsync(token);

        var items = new List<ChallengeWriteupItemModel>();

        // 每周一条：取该周最新提交
        foreach (var week in weeks)
        {
            var wp = writeups.FirstOrDefault(w => w.Week == week && w.ChallengeId is null);
            items.Add(new ChallengeWriteupItemModel
            {
                Id = wp?.Id,
                Week = week,
                Solved = true,
                Submitted = wp is not null,
                SubmitTimeUtc = wp?.SubmitTimeUtc,
                AwardedScore = wp?.AwardedScore ?? 0,
                ReviewNote = wp?.ReviewNote,
                FileName = wp?.File?.Name
            });
        }

        // 历史按题提交记录（迁移前已有数据）
        foreach (var wp in writeups.Where(w => w.ChallengeId is not null))
        {
            items.Add(new ChallengeWriteupItemModel
            {
                Id = wp.Id,
                Week = wp.Week,
                ChallengeId = wp.ChallengeId,
                Legacy = true,
                Title = wp.Challenge?.Title ?? string.Empty,
                Category = wp.Challenge?.Category.ToString() ?? string.Empty,
                Solved = true,
                Submitted = true,
                SubmitTimeUtc = wp.SubmitTimeUtc,
                AwardedScore = wp.AwardedScore,
                ReviewNote = wp.ReviewNote,
                FileName = wp.File?.Name
            });
        }

        return Ok(new ChallengeWriteupListModel { Items = items, Weeks = weeks });
    }

    /// <summary>
    /// 按周上传解题思路（PDF）
    /// </summary>
    /// <remarks>同一按钮可反复使用：同一周重复提交会覆盖旧文件，不同周互不影响。</remarks>
    /// <param name="gameId">比赛 Id</param>
    /// <param name="week">周次（1-5）</param>
    /// <param name="file">PDF 文件</param>
    /// <param name="token"></param>
    [HttpPost("Games/{gameId:int}/Weeks/{week:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Upload(int gameId, int week, IFormFile file,
        CancellationToken token)
    {
        if (file.Length == 0)
            return BadRequest(new RequestResponse("文件为空，请重新选择"));

        if (file.Length > MaxFileSize)
            return BadRequest(new RequestResponse("文件超过 20MB 限制"));

        if (file.ContentType != "application/pdf" || Path.GetExtension(file.FileName) != ".pdf")
            return BadRequest(new RequestResponse("仅支持上传 PDF 文件"));

        var user = await userManager.GetUserAsync(User);

        if (user is null)
            return Unauthorized(new RequestResponse("请先登录"));

        var participation = await GetMyParticipation(gameId, user, token);

        if (participation is null)
            return NotFound(new RequestResponse("你尚未报名该场比赛", StatusCodes.Status404NotFound));

        var game = await dbContext.Games.AsNoTracking().FirstOrDefaultAsync(g => g.Id == gameId, token);

        if (game is null)
            return NotFound(new RequestResponse("比赛不存在", StatusCodes.Status404NotFound));

        if (week < 1 || week > 5 || game.GetWeekWindow(week).Start is null)
            return BadRequest(new RequestResponse("该周次未开启，无法提交"));

        // 本周已有记录则覆盖（同一按钮可多次使用）
        var writeup = await dbContext.ChallengeWriteups
            .Include(w => w.File)
            .FirstOrDefaultAsync(w => w.ParticipationId == participation.Id &&
                                      w.Week == week && w.ChallengeId == null, token);

        var oldFile = writeup?.File;

        if (writeup is null)
        {
            writeup = new ChallengeWriteup
            {
                GameId = gameId,
                ParticipationId = participation.Id,
                Week = week
            };
            dbContext.ChallengeWriteups.Add(writeup);
        }

        writeup.File = await blobRepository.CreateOrUpdateBlob(file,
            $"WP-{gameId}-{participation.TeamId}-W{week}-{DateTimeOffset.UtcNow:yyyyMMdd-HH.mm.ss}Z.pdf",
            token);
        writeup.SubmitTimeUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(token);

        if (oldFile is not null)
            await blobRepository.DeleteBlob(oldFile, token);

        logger.Log(
            $"提交解题思路：game={gameId} week={week} team={participation.Team?.Name}",
            user, TaskStatus.Success, LogLevel.Information);

        return Ok();
    }

    /// <summary>
    /// 下载解题思路文件
    /// </summary>
    /// <remarks>队伍成员可下载本队的；管理员与该项目方向的出题人可下载任意。</remarks>
    /// <param name="id">WP 记录 Id</param>
    /// <param name="token"></param>
    [HttpGet("{id:int}/File")]
    public async Task<IActionResult> Download(int id, CancellationToken token)
    {
        var user = await userManager.GetUserAsync(User);

        if (user is null)
            return Unauthorized(new RequestResponse("请先登录"));

        var writeup = await dbContext.ChallengeWriteups.AsNoTracking()
            .Include(w => w.File)
            .Include(w => w.Challenge)
            .Include(w => w.Participation).ThenInclude(p => p.Members)
            .FirstOrDefaultAsync(w => w.Id == id, token);

        if (writeup?.File is null)
            return NotFound(new RequestResponse("文件不存在", StatusCodes.Status404NotFound));

        var isMember = writeup.Participation.Members.Any(m => m.UserId == user.Id);
        var canReview = user.Role >= Role.Admin ||
                        (user.CanEditChallenge && user.HasManagedCategory(writeup.Challenge.Category));

        if (!isMember && !canReview)
            return StatusCode(StatusCodes.Status403Forbidden, new RequestResponse("无权下载该文件"));

        var file = writeup.File;
        var path = StoragePath.Combine(PathHelper.Uploads, file.Hash[..2], file.Hash[2..4], file.Hash);

        if (!await storage.ExistsAsync(path, token))
            return NotFound(new RequestResponse("文件不存在", StatusCodes.Status404NotFound));

        var stream = await storage.OpenReadAsync(path, token);
        return File(stream, "application/pdf", file.Name);
    }

    /// <summary>
    /// 审核视角：某场比赛的全部 WP 提交
    /// </summary>
    /// <remarks>
    /// 系统管理员可见全部；出题人/出题管理员仅可见自己授权方向下的题目。
    /// </remarks>
    /// <param name="gameId">比赛 Id</param>
    /// <param name="token"></param>
    [HttpGet("Games/{gameId:int}/Review")]
    [RequireChallengeEditorOrAdmin]
    [ProducesResponseType(typeof(ChallengeWriteupReviewListModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> Review(int gameId, CancellationToken token)
    {
        var user = await userManager.GetUserAsync(User);

        if (user is null)
            return Unauthorized(new RequestResponse("请先登录"));

        var query = dbContext.ChallengeWriteups.AsNoTracking()
            .Where(w => w.GameId == gameId);

        if (user.Role < Role.Admin)
        {
            var categories = user.ManagedCategoryList;

            if (categories.Count == 0)
                return Ok(new ChallengeWriteupReviewListModel());

            query = query.Where(w => categories.Contains(w.Challenge.Category));
        }

        var items = await query
            .OrderByDescending(w => w.AwardedAtUtc == null)
            .ThenBy(w => w.SubmitTimeUtc)
            .Select(w => new ChallengeWriteupReviewItemModel
            {
                Id = w.Id,
                TeamId = w.Participation.TeamId,
                TeamName = w.Participation.Team.Name,
                Week = w.Week,
                ChallengeId = w.ChallengeId,
                ChallengeTitle = w.Challenge != null ? w.Challenge.Title : string.Empty,
                Category = w.Challenge.Category.ToString(),
                SubmitTimeUtc = w.SubmitTimeUtc,
                AwardedScore = w.AwardedScore,
                AwardedAtUtc = w.AwardedAtUtc,
                ReviewNote = w.ReviewNote,
                FileName = w.File != null ? w.File.Name : null
            })
            .ToListAsync(token);

        return Ok(new ChallengeWriteupReviewListModel { Items = items });
    }

    /// <summary>
    /// 审核加分
    /// </summary>
    /// <param name="id">WP 记录 Id</param>
    /// <param name="model">加分值与备注</param>
    /// <param name="token"></param>
    [HttpPost("{id:int}/Award")]
    [RequireChallengeEditorOrAdmin]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Award(int id, [FromBody] ChallengeWriteupAwardModel model,
        CancellationToken token)
    {
        var user = await userManager.GetUserAsync(User);

        if (user is null)
            return Unauthorized(new RequestResponse("请先登录"));

        var writeup = await dbContext.ChallengeWriteups
            .Include(w => w.Challenge)
            .FirstOrDefaultAsync(w => w.Id == id, token);

        if (writeup is null)
            return NotFound(new RequestResponse("记录不存在", StatusCodes.Status404NotFound));

        if (user.Role < Role.Admin && !user.HasManagedCategory(writeup.Challenge.Category))
            return StatusCode(StatusCodes.Status403Forbidden,
                new RequestResponse("你只能为自己授权方向下的题目加分"));

        writeup.AwardedScore = model.Score;
        writeup.ReviewNote = string.IsNullOrWhiteSpace(model.Note) ? null : model.Note.Trim();
        writeup.AwardedById = user.Id;
        writeup.AwardedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(token);

        // 加分影响排行榜，刷新缓存
        await cacheHelper.FlushScoreboardCache(writeup.GameId, token);

        logger.Log(
            $"解题思路加分：writeup={writeup.Id} challenge={writeup.ChallengeId} score={model.Score}",
            user, TaskStatus.Success, LogLevel.Information);

        return Ok();
    }
}
