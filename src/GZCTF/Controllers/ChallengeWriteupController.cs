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
    /// 队伍视角：本队已解出的题目及 WP 提交状态
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

        // 本队已解出的题目
        var solved = await dbContext.FirstSolves.AsNoTracking()
            .Where(f => f.ParticipationId == participation.Id)
            .Select(f => f.ChallengeId)
            .ToListAsync(token);

        var solvedSet = solved.ToHashSet();

        var challenges = await dbContext.GameChallenges.AsNoTracking()
            .Where(c => c.GameId == gameId && c.IsEnabled)
            .OrderBy(c => c.Category).ThenBy(c => c.Title)
            .Select(c => new { c.Id, c.Title, c.Category, c.Week })
            .ToListAsync(token);

        var writeups = await dbContext.ChallengeWriteups.AsNoTracking()
            .Where(w => w.ParticipationId == participation.Id)
            .Include(w => w.File)
            .ToDictionaryAsync(w => w.ChallengeId, token);

        var items = challenges
            .Where(c => solvedSet.Contains(c.Id))
            .Select(c =>
            {
                writeups.TryGetValue(c.Id, out var wp);
                return new ChallengeWriteupItemModel
                {
                    ChallengeId = c.Id,
                    Title = c.Title,
                    Category = c.Category.ToString(),
                    IsChallengeBucket = c.Week == Game.ChallengeBucket,
                    Solved = true,
                    Submitted = wp is not null,
                    SubmitTimeUtc = wp?.SubmitTimeUtc,
                    AwardedScore = wp?.AwardedScore ?? 0,
                    ReviewNote = wp?.ReviewNote,
                    FileName = wp?.File?.Name
                };
            })
            .OrderByDescending(i => i.IsChallengeBucket)
            .ThenBy(i => i.Category)
            .ThenBy(i => i.Title)
            .ToList();

        return Ok(new ChallengeWriteupListModel { Items = items });
    }

    /// <summary>
    /// 上传某道题的解题思路（PDF）
    /// </summary>
    /// <remarks>仅可对已解出的题目提交；重复提交会覆盖旧文件。</remarks>
    /// <param name="gameId">比赛 Id</param>
    /// <param name="challengeId">题目 Id</param>
    /// <param name="file">PDF 文件</param>
    /// <param name="token"></param>
    [HttpPost("Games/{gameId:int}/Challenges/{challengeId:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Upload(int gameId, int challengeId, IFormFile file,
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

        var challenge = await dbContext.GameChallenges.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == challengeId && c.GameId == gameId, token);

        if (challenge is null)
            return NotFound(new RequestResponse("题目不存在", StatusCodes.Status404NotFound));

        var solved = await dbContext.FirstSolves.AsNoTracking()
            .AnyAsync(f => f.ParticipationId == participation.Id && f.ChallengeId == challengeId, token);

        if (!solved)
            return BadRequest(new RequestResponse("只有解出该题后才能提交解题思路"));

        var writeup = await dbContext.ChallengeWriteups
            .Include(w => w.File)
            .FirstOrDefaultAsync(w => w.ParticipationId == participation.Id && w.ChallengeId == challengeId,
                token);

        var oldFile = writeup?.File;

        if (writeup is null)
        {
            writeup = new ChallengeWriteup
            {
                GameId = gameId,
                ParticipationId = participation.Id,
                ChallengeId = challengeId
            };
            dbContext.ChallengeWriteups.Add(writeup);
        }

        writeup.File = await blobRepository.CreateOrUpdateBlob(file,
            $"WP-{gameId}-{participation.TeamId}-{challengeId}-{DateTimeOffset.UtcNow:yyyyMMdd-HH.mm.ss}Z.pdf",
            token);
        writeup.SubmitTimeUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(token);

        // 旧文件不再被引用，清理
        if (oldFile is not null)
            await blobRepository.DeleteBlob(oldFile, token);

        logger.Log(
            $"提交解题思路：game={gameId} challenge={challengeId} team={participation.Team?.Name}",
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
                ChallengeId = w.ChallengeId,
                ChallengeTitle = w.Challenge.Title,
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
