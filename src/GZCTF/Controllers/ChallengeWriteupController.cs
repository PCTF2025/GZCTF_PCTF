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
    /// 队伍视角：WP 提交状态（第 1-4 周各一份 + 挑战题逐题一份，含历史记录）
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

        // 周次报告：已配置开始时间的周（1-4），每周一份，覆盖该周所有题目
        var weeks = Enumerable.Range(1, 4)
            .Where(w => game.GetWeekWindow(w).Start is not null)
            .ToArray();

        var writeups = await dbContext.ChallengeWriteups.AsNoTracking()
            .Where(w => w.ParticipationId == participation.Id)
            .Include(w => w.File)
            .Include(w => w.Challenge)
            .OrderByDescending(w => w.SubmitTimeUtc)
            .ToListAsync(token);

        var items = new List<ChallengeWriteupItemModel>();

        // 第 1-4 周：每周一份（取最新提交）
        foreach (var week in weeks)
        {
            var wp = writeups.FirstOrDefault(w => w.Week == week && w.ChallengeId is null);
            items.Add(new ChallengeWriteupItemModel
            {
                Id = wp?.Id,
                Week = week,
                IsChallengeBucket = false,
                Solved = true,
                Submitted = wp is not null,
                SubmitTimeUtc = wp?.SubmitTimeUtc,
                AwardedScore = wp?.AwardedScore ?? 0,
                ReviewNote = wp?.ReviewNote,
                FileName = wp?.File?.Name
            });
        }

        // 挑战题：解出一道交一份（逐题提交）
        var solved = await dbContext.FirstSolves.AsNoTracking()
            .Where(f => f.ParticipationId == participation.Id)
            .Select(f => f.ChallengeId)
            .ToListAsync(token);
        var solvedSet = solved.ToHashSet();

        var challenges = await dbContext.GameChallenges.AsNoTracking()
            .Where(c => c.GameId == gameId && c.IsEnabled && c.Week == Game.ChallengeBucket)
            .OrderBy(c => c.Category).ThenBy(c => c.Title)
            .Select(c => new { c.Id, c.Title, c.Category })
            .ToListAsync(token);

        foreach (var challenge in challenges.Where(c => solvedSet.Contains(c.Id)))
        {
            var wp = writeups.FirstOrDefault(w => w.ChallengeId == challenge.Id);
            items.Add(new ChallengeWriteupItemModel
            {
                Id = wp?.Id,
                Week = Game.ChallengeBucket,
                ChallengeId = challenge.Id,
                IsChallengeBucket = true,
                Title = challenge.Title,
                Category = challenge.Category.ToString(),
                Solved = true,
                Submitted = wp is not null,
                SubmitTimeUtc = wp?.SubmitTimeUtc,
                AwardedScore = wp?.AwardedScore ?? 0,
                ReviewNote = wp?.ReviewNote,
                FileName = wp?.File?.Name
            });
        }

        // 历史按题提交记录（迁移前的数据，题目可能非挑战题）
        foreach (var wp in writeups.Where(w => w.ChallengeId is not null &&
                                               w.Challenge?.Week != Game.ChallengeBucket))
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
    /// 支持的上传格式：PDF 与 Word
    /// </summary>
    private static bool IsAllowedDocument(string fileName, string? contentType)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (ext is not (".pdf" or ".doc" or ".docx"))
            return false;

        if (string.IsNullOrWhiteSpace(contentType))
            return true;

        return contentType is "application/pdf"
            or "application/msword"
            or "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
            or "application/octet-stream";
    }

    /// <summary>
    /// 上传某一周的解题报告（覆盖该周所有题目）
    /// </summary>
    /// <remarks>同一按钮可反复使用：同一周重复提交会覆盖旧文件，不同周互不影响。</remarks>
    /// <param name="gameId">比赛 Id</param>
    /// <param name="week">周次（1-4）</param>
    /// <param name="file">PDF 或 Word 文件</param>
    /// <param name="token"></param>
    [HttpPost("Games/{gameId:int}/Weeks/{week:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UploadWeek(int gameId, int week, IFormFile file,
        CancellationToken token)
    {
        var validation = ValidateFile(file);
        if (validation is not null)
            return BadRequest(new RequestResponse(validation));

        var user = await userManager.GetUserAsync(User);

        if (user is null)
            return Unauthorized(new RequestResponse("请先登录"));

        var participation = await GetMyParticipation(gameId, user, token);

        if (participation is null)
            return NotFound(new RequestResponse("你尚未报名该场比赛", StatusCodes.Status404NotFound));

        var game = await dbContext.Games.AsNoTracking().FirstOrDefaultAsync(g => g.Id == gameId, token);

        if (game is null)
            return NotFound(new RequestResponse("比赛不存在", StatusCodes.Status404NotFound));

        if (week < 1 || week > 4 || game.GetWeekWindow(week).Start is null)
            return BadRequest(new RequestResponse("该周次未开启，无法提交"));

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

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        writeup.File = await blobRepository.CreateOrUpdateBlob(file,
            $"WP-{gameId}-{participation.TeamId}-W{week}-{DateTimeOffset.UtcNow:yyyyMMdd-HH.mm.ss}Z{ext}",
            token);
        writeup.SubmitTimeUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(token);

        if (oldFile is not null)
            await blobRepository.DeleteBlob(oldFile, token);

        logger.Log(
            $"提交周报告 WP：game={gameId} week={week} team={participation.Team?.Name}",
            user, TaskStatus.Success, LogLevel.Information);

        return Ok();
    }

    /// <summary>
    /// 上传某道挑战题的解题报告（逐题一份）
    /// </summary>
    /// <param name="gameId">比赛 Id</param>
    /// <param name="challengeId">题目 Id</param>
    /// <param name="file">PDF 或 Word 文件</param>
    /// <param name="token"></param>
    [HttpPost("Games/{gameId:int}/Challenges/{challengeId:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UploadChallenge(int gameId, int challengeId, IFormFile file,
        CancellationToken token)
    {
        var validation = ValidateFile(file);
        if (validation is not null)
            return BadRequest(new RequestResponse(validation));

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

        if (challenge.Week != Game.ChallengeBucket)
            return BadRequest(new RequestResponse("只有挑战题需要逐题提交报告"));

        var solved = await dbContext.FirstSolves.AsNoTracking()
            .AnyAsync(f => f.ParticipationId == participation.Id && f.ChallengeId == challengeId, token);

        if (!solved)
            return BadRequest(new RequestResponse("只有解出该题后才能提交解题报告"));

        var writeup = await dbContext.ChallengeWriteups
            .Include(w => w.File)
            .FirstOrDefaultAsync(w => w.ParticipationId == participation.Id &&
                                      w.ChallengeId == challengeId, token);

        var oldFile = writeup?.File;

        if (writeup is null)
        {
            writeup = new ChallengeWriteup
            {
                GameId = gameId,
                ParticipationId = participation.Id,
                ChallengeId = challengeId,
                Week = Game.ChallengeBucket
            };
            dbContext.ChallengeWriteups.Add(writeup);
        }

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        writeup.File = await blobRepository.CreateOrUpdateBlob(file,
            $"WP-{gameId}-{participation.TeamId}-C{challengeId}-{DateTimeOffset.UtcNow:yyyyMMdd-HH.mm.ss}Z{ext}",
            token);
        writeup.SubmitTimeUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(token);

        if (oldFile is not null)
            await blobRepository.DeleteBlob(oldFile, token);

        logger.Log(
            $"提交挑战题 WP：game={gameId} challenge={challengeId} team={participation.Team?.Name}",
            user, TaskStatus.Success, LogLevel.Information);

        return Ok();
    }

    private string? ValidateFile(IFormFile file)
    {
        if (file.Length == 0)
            return "文件为空，请重新选择";

        if (file.Length > MaxFileSize)
            return "文件超过 20MB 限制";

        if (!IsAllowedDocument(file.FileName, file.ContentType))
            return "仅支持 PDF 或 Word（doc/docx）文件";

        return null;
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
        var isSchoolReviewer = user.IsSchoolAdmin || user.HasPermission(UserPermission.SchoolManager);
        var canReview = user.Role >= Role.Admin || isSchoolReviewer ||
                        (user.CanEditChallenge &&
                         (writeup.Challenge is null || user.HasManagedCategory(writeup.Challenge.Category)));

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
    [RequireWriteupReviewer]
    [ProducesResponseType(typeof(ChallengeWriteupReviewListModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> Review(int gameId, CancellationToken token)
    {
        var user = await userManager.GetUserAsync(User);

        if (user is null)
            return Unauthorized(new RequestResponse("请先登录"));

        var query = dbContext.ChallengeWriteups.AsNoTracking()
            .Where(w => w.GameId == gameId);

        // 管理员与学校学籍审核可见全部；出题人可见自己方向 + 按周提交的报告（跨方向，无法按方向拆分）
        var seesAll = user.Role >= Role.Admin || user.IsSchoolAdmin ||
                      user.HasPermission(UserPermission.SchoolManager);

        if (!seesAll)
        {
            var categories = user.ManagedCategoryList;
            query = query.Where(w => w.ChallengeId == null || categories.Contains(w.Challenge!.Category));
        }

        var rows = await query
            .Include(w => w.File)
            .Include(w => w.Challenge)
            .Include(w => w.Participation).ThenInclude(p => p.Team)
            .Include(w => w.Participation).ThenInclude(p => p.Division)
            .Include(w => w.Participation).ThenInclude(p => p.Members).ThenInclude(m => m.User)
            .OrderByDescending(w => w.AwardedAtUtc == null)
            .ThenBy(w => w.SubmitTimeUtc)
            .ToListAsync(token);

        var items = rows.Select(w => new ChallengeWriteupReviewItemModel
        {
            Id = w.Id,
            TeamId = w.Participation.TeamId,
            TeamName = w.Participation.Team.Name,
            Week = w.Week,
            // 只有「挑战题分桶」的逐题提交才算挑战题；历史按题记录按周归类
            IsChallengeBucket = w.ChallengeId is not null &&
                                w.Challenge is not null &&
                                w.Challenge.Week == Game.ChallengeBucket,
            School = string.Join("、", w.Participation.Members
                .Select(m => m.User.School)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct()
                .OrderBy(s => s)),
            Division = w.Participation.Division?.Name,
            ChallengeId = w.ChallengeId,
            ChallengeTitle = w.Challenge?.Title ?? string.Empty,
            Category = w.Challenge?.Category.ToString() ?? string.Empty,
            SubmitTimeUtc = w.SubmitTimeUtc,
            AwardedScore = w.AwardedScore,
            AwardedAtUtc = w.AwardedAtUtc,
            ReviewNote = w.ReviewNote,
            FileName = w.File?.Name
        }).ToList();

        return Ok(new ChallengeWriteupReviewListModel { Items = items });
    }

    /// <summary>
    /// 审核加分
    /// </summary>
    /// <param name="id">WP 记录 Id</param>
    /// <param name="model">加分值与备注</param>
    /// <param name="token"></param>
    [HttpPost("{id:int}/Award")]
    [RequireWriteupReviewer]
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

        // 加分权限：管理员不限；按周报告（无题目）任意出题人可加分；按题需具备该方向
        var canAward = user.Role >= Role.Admin ||
                       (user.CanEditChallenge &&
                        (writeup.Challenge is null || user.HasManagedCategory(writeup.Challenge.Category)));

        if (!canAward)
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
