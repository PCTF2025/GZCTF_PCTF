using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using GZCTF.Extensions;
using GZCTF.Middlewares;
using GZCTF.Models.Request.Edit;
using GZCTF.Models.Request.Game;
using GZCTF.Models.Request.Info;
using GZCTF.Repositories.Interface;
using GZCTF.Services.Cache;
using GZCTF.Services.Container.Manager;
using GZCTF.Services.Transfer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using NSwag.Annotations;

namespace GZCTF.Controllers;

/// <summary>
/// Data Modification APIs
/// </summary>
[RequireChallengeEditorOrAdmin]
[ApiController]
[Route("api/[controller]")]
[Produces(MediaTypeNames.Application.Json)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status403Forbidden)]
public class EditController(
    CacheHelper cacheHelper,
    UserManager<UserInfo> userManager,
    ILogger<EditController> logger,
    IPostRepository postRepository,
    IContainerRepository containerRepository,
    IGameChallengeRepository challengeRepository,
    IGameInstanceRepository instanceRepository,
    IGameNoticeRepository gameNoticeRepository,
    IGameRepository gameRepository,
    IContainerManager containerService,
    IBlobRepository blobService,
    GameExportService exportService,
    GameImportService importService,
    IDivisionRepository divisionRepository,
    AppDbContext dbContext,
    IStringLocalizer<Program> localizer) : Controller
{
    /// <summary>
    /// 当前登录用户（带权限判定所需的全部字段）
    /// </summary>
    private async Task<UserInfo?> GetCurrentUserAsync(CancellationToken token)
    {
        if (!User.Identity?.IsAuthenticated ?? true)
            return null;

        return await userManager.GetUserAsync(User);
    }

    /// <summary>
    /// 校验「新增题目」权限与出题方向。
    /// 出题人 / 出题管理员 / 系统管理员可新增；
    /// 若被指定了出题方向，则只能把题目建在授权方向内。
    /// </summary>
    private async Task<IActionResult?> EnsureCanCreateChallengeAsync(ChallengeCategory category,
        CancellationToken token)
    {
        var user = await GetCurrentUserAsync(token);

        if (user is null)
            return Unauthorized(new RequestResponse(localizer[nameof(Resources.Program.Auth_LoginRequired)],
                StatusCodes.Status401Unauthorized));

        if (!user.IsChallengeAuthor && !user.IsChallengeManager)
            return new ObjectResult(new RequestResponse("你没有出题权限，无法新增题目"))
            {
                StatusCode = StatusCodes.Status403Forbidden
            };

        if (!user.CanAuthorInCategory(category))
        {
            var allowed = string.Join("、", user.ManagedCategoryList.Select(c => c.ToString()));
            return new ObjectResult(new RequestResponse($"你只能在被指定的方向出题，当前授权方向：{allowed}"))
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }

        return null;
    }

    /// <summary>
    /// 校验对指定题目的管理权限。
    /// 系统管理员 / 被授权方向的出题管理员 / 题目创建者本人可操作。
    /// </summary>
    private async Task<IActionResult?> EnsureCanManageChallengeAsync(GameChallenge? challenge,
        CancellationToken token)
    {
        if (challenge is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_ChallengeNotFound)],
                StatusCodes.Status404NotFound));

        var user = await GetCurrentUserAsync(token);

        if (user is null)
            return Unauthorized(new RequestResponse(localizer[nameof(Resources.Program.Auth_LoginRequired)],
                StatusCodes.Status401Unauthorized));

        if (user.CanManageChallenge(challenge.Category, challenge.AuthorId))
            return null;

        return new ObjectResult(new RequestResponse("你只能管理自己创建的题目，或被授权方向下的题目"))
        {
            StatusCode = StatusCodes.Status403Forbidden
        };
    }

    /// <summary>
    /// 校验是否可查看该题目。
    /// 比 <see cref="EnsureCanManageChallengeAsync"/> 宽松：被授权方向的出题人
    /// 可交叉查看他人题目（只读 + 可测题），但不能修改。
    /// </summary>
    private async Task<IActionResult?> EnsureCanViewChallengeAsync(GameChallenge? challenge,
        CancellationToken token)
    {
        if (challenge is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_ChallengeNotFound)],
                StatusCodes.Status404NotFound));

        var user = await GetCurrentUserAsync(token);

        if (user is null)
            return Unauthorized(new RequestResponse(localizer[nameof(Resources.Program.Auth_LoginRequired)],
                StatusCodes.Status401Unauthorized));

        if (user.CanViewChallenge(challenge.Category, challenge.AuthorId))
            return null;

        return new ObjectResult(new RequestResponse("你只能查看自己创建的题目，或被授权方向下的题目"))
        {
            StatusCode = StatusCodes.Status403Forbidden
        };
    }

    /// <summary>
    /// 校验「把题目改到目标方向」是否被允许。
    /// 防止出题人把题目迁到未被授权的新方向。
    /// </summary>
    private async Task<IActionResult?> EnsureCanMoveToCategoryAsync(ChallengeCategory target,
        CancellationToken token)
    {
        var user = await GetCurrentUserAsync(token);

        if (user is null || user.CanAuthorInCategory(target))
            return null;

        var allowed = string.Join("、", user.ManagedCategoryList.Select(c => c.ToString()));
        return new ObjectResult(new RequestResponse($"你只能在被指定的方向出题，当前授权方向：{allowed}"))
        {
            StatusCode = StatusCodes.Status403Forbidden
        };
    }

    /// <summary>
    /// Add Post
    /// </summary>
    /// <remarks>
    /// Adding a post requires administrator privileges
    /// </remarks>
    /// <param name="model"></param>
    /// <param name="token"></param>
    /// <response code="200">Successfully added post</response>
    [HttpPost("Posts")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    [RequireAdmin]
    public async Task<IActionResult> AddPost([FromBody] PostEditModel model, CancellationToken token)
    {
        var user = await userManager.GetUserAsync(User);
        var res = await postRepository.CreatePost(new Post().Update(model, user!), token);
        return Ok(res.Id);
    }

    /// <summary>
    /// Update Post
    /// </summary>
    /// <remarks>
    /// Updating a post requires administrator privileges
    /// </remarks>
    /// <param name="id">Post ID</param>
    /// <param name="token"></param>
    /// <param name="model"></param>
    /// <response code="200">Successfully updated post</response>
    /// <response code="404">Post not found</response>
    [HttpPut("Posts/{id}")]
    [ProducesResponseType(typeof(PostDetailModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [RequireAdmin]
    public async Task<IActionResult> UpdatePost(string id, [FromBody] PostEditModel model, CancellationToken token)
    {
        var post = await postRepository.GetPostById(id, token);

        if (post is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Post_NotFound)],
                StatusCodes.Status404NotFound));

        var user = await userManager.GetUserAsync(User);

        await postRepository.UpdatePost(post.Update(model, user!), token);

        return Ok(PostDetailModel.FromPost(post));
    }

    /// <summary>
    /// Delete Post
    /// </summary>
    /// <remarks>
    /// Deleting a post requires administrator privileges
    /// </remarks>
    /// <param name="id">Post ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully deleted post</response>
    /// <response code="404">Post not found</response>
    [HttpDelete("Posts/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [RequireAdmin]
    public async Task<IActionResult> DeletePost(string id, CancellationToken token)
    {
        var post = await postRepository.GetPostById(id, token);

        if (post is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Post_NotFound)],
                StatusCodes.Status404NotFound));

        await postRepository.RemovePost(post, token);

        return Ok();
    }

    /// <summary>
    /// Add Game
    /// </summary>
    /// <remarks>
    /// Adding a game requires administrator privileges
    /// </remarks>
    /// <param name="model"></param>
    /// <param name="token"></param>
    /// <response code="200">Successfully added game</response>
    [HttpPost("Games")]
    [ProducesResponseType(typeof(GameInfoModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [RequireAdmin]
    public async Task<IActionResult> AddGame([FromBody] GameInfoModel model, CancellationToken token)
    {
        var game = await gameRepository.CreateGame(new Game().Update(model), token);

        if (game is null)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_CreationFailed)]));

        await cacheHelper.FlushRecentGamesCache(token);

        return Ok(GameInfoModel.FromGame(game));
    }

    /// <summary>
    /// Get Game List
    /// </summary>
    /// <remarks>
    /// Retrieving the game list requires administrator privileges
    /// </remarks>
    /// <param name="count"></param>
    /// <param name="skip"></param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved game list</response>
    [HttpGet("Games")]
    [ProducesResponseType(typeof(ArrayResponse<GameInfoModel>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetGames([FromQuery][Range(0, 100)] int count, [FromQuery] int skip,
        CancellationToken token) =>
        Ok((await gameRepository.GetGames(count, skip, token))
            .Select(GameInfoModel.FromGame)
            .ToResponse(await gameRepository.CountAsync(token)));

    /// <summary>
    /// Get Game
    /// </summary>
    /// <remarks>
    /// Retrieving a game requires administrator privileges
    /// </remarks>
    /// <param name="id"></param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved game</response>
    [HttpGet("Games/{id:int}")]
    [ProducesResponseType(typeof(GameInfoModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetGame([FromRoute] int id, CancellationToken token)
    {
        var game = await gameRepository.GetGameById(id, token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        return Ok(GameInfoModel.FromGame(game));
    }

    /// <summary>
    /// Get Game Hash Salt
    /// </summary>
    /// <remarks>
    /// Retrieving the game hash salt requires administrator privileges
    /// </remarks>
    /// <param name="id"></param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved game hash salt</response>
    [OpenApiIgnore]
    [HttpGet("Games/{id:int}/HashSalt")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetHashSalt([FromRoute] int id, CancellationToken token)
    {
        var game = await gameRepository.GetGameById(id, token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        return Ok(game.TeamHashSalt);
    }

    /// <summary>
    /// Update Game
    /// </summary>
    /// <remarks>
    /// Updating a game requires administrator privileges
    /// </remarks>
    /// <param name="id"></param>
    /// <param name="model"></param>
    /// <param name="token"></param>
    /// <response code="200">Successfully updated game</response>
    [HttpPut("Games/{id:int}")]
    [ProducesResponseType(typeof(GameInfoModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [RequireAdmin]
    public async Task<IActionResult> UpdateGame([FromRoute] int id, [FromBody] GameInfoModel model,
        CancellationToken token)
    {
        var game = await gameRepository.GetGameById(id, token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        game.Update(model);
        await gameRepository.UpdateGame(game, token);

        return Ok(GameInfoModel.FromGame(game));
    }

    /// <summary>
    /// Delete Game
    /// </summary>
    /// <remarks>
    /// Deleting a game requires administrator privileges
    /// </remarks>
    /// <param name="id"></param>
    /// <param name="token"></param>
    /// <response code="200">Successfully deleted game</response>
    [HttpDelete("Games/{id:int}")]
    [ProducesResponseType(typeof(GameInfoModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [RequireAdmin]
    public async Task<IActionResult> DeleteGame([FromRoute] int id, CancellationToken token)
    {
        var game = await gameRepository.GetGameById(id, token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        return await gameRepository.DeleteGame(game, token) switch
        {
            TaskStatus.Success => Ok(),
            _ => BadRequest(
                new RequestResponse(localizer[nameof(Resources.Program.Game_DeletionFailed)]))
        };
    }

    /// <summary>
    /// Delete All WriteUps
    /// </summary>
    /// <remarks>
    /// Deleting all WriteUps for a game requires administrator privileges
    /// </remarks>
    /// <param name="id"></param>
    /// <param name="token"></param>
    /// <response code="200">Successfully deleted game WriteUps</response>
    [HttpDelete("Games/{id:int}/WriteUps")]
    [ProducesResponseType(typeof(GameInfoModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [RequireAdmin]
    public async Task<IActionResult> DeleteGameWriteUps([FromRoute] int id, CancellationToken token)
    {
        var game = await gameRepository.GetGameById(id, token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        await gameRepository.DeleteAllWriteUps(game, token);

        return Ok();
    }

    /// <summary>
    /// Update Game Poster
    /// </summary>
    /// <remarks>
    /// Use this endpoint to update the game poster; administrator privileges required
    /// </remarks>
    /// <response code="200">Game poster URL</response>
    /// <response code="400">Invalid request</response>
    /// <response code="401">Unauthorized user</response>
    [HttpPut("Games/{id:int}/Poster")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [RequireAdmin]
    public async Task<IActionResult> UpdateGamePoster([FromRoute] int id, IFormFile file, CancellationToken token)
    {
        switch (file.Length)
        {
            case 0:
                return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.File_SizeZero)]));
            case > 3 * 1024 * 1024:
                return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.File_SizeTooLarge)]));
        }

        var game = await gameRepository.GetGameById(id, token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        var poster = await blobService.CreateOrUpdateImage(file, "poster", 0, token);

        if (poster is null)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.File_CreationFailed)]));

        game.PosterHash = poster.Hash;
        await gameRepository.UpdateGame(game, token);

        return Ok(poster.Url());
    }

    /// <summary>
    /// Add Game Notice
    /// </summary>
    /// <remarks>
    /// Adding a game notice requires administrator privileges
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="model">Notice content</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully added game notice</response>
    [HttpPost("Games/{id:int}/Notices")]
    [ProducesResponseType(typeof(GameNotice), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [RequireAdmin]
    public async Task<IActionResult> AddGameNotice([FromRoute] int id, [FromBody] GameNoticeModel model,
        CancellationToken token)
    {
        var game = await gameRepository.GetGameById(id, token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        var res = await gameNoticeRepository.AddNotice(
            new()
            {
                Values = [model.Content],
                GameId = game.Id,
                Type = NoticeType.Normal,
                PublishTimeUtc = DateTimeOffset.UtcNow
            }, token);

        return Ok(res);
    }

    /// <summary>
    /// Get Game Notices
    /// </summary>
    /// <remarks>
    /// Retrieving game notices requires administrator privileges
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved game notices</response>
    [HttpGet("Games/{id:int}/Notices")]
    [ProducesResponseType(typeof(GameNotice[]), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetGameNotices([FromRoute] int id, CancellationToken token = default)
    {
        var game = await gameRepository.GetGameById(id, token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        return Ok(await gameNoticeRepository.GetNormalNotices(id, token));
    }

    /// <summary>
    /// Update Game Notice
    /// </summary>
    /// <remarks>
    /// Updating a game notice requires administrator privileges
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="noticeId">Notice ID</param>
    /// <param name="model">Notice content</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully updated notice</response>
    [HttpPut("Games/{id:int}/Notices/{noticeId:int}")]
    [ProducesResponseType(typeof(GameNotice), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [RequireAdmin]
    public async Task<IActionResult> UpdateGameNotice([FromRoute] int id, [FromRoute] int noticeId,
        [FromBody] GameNoticeModel model, CancellationToken token = default)
    {
        var notice = await gameNoticeRepository.GetNoticeById(id, noticeId, token);

        if (notice is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Notification_NotFound)],
                StatusCodes.Status404NotFound));

        if (notice.Type != NoticeType.Normal)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Notification_SystemNotEditable)]));

        notice.Values = [model.Content];
        return Ok(await gameNoticeRepository.UpdateNotice(notice, token));
    }

    /// <summary>
    /// Delete Game Notice
    /// </summary>
    /// <remarks>
    /// Deleting a game notice requires administrator privileges
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="noticeId">Post ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully deleted post</response>
    /// <response code="404">Post not found</response>
    [HttpDelete("Games/{id:int}/Notices/{noticeId:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [RequireAdmin]
    public async Task<IActionResult> DeleteGameNotice([FromRoute] int id, [FromRoute] int noticeId,
        CancellationToken token)
    {
        var notice = await gameNoticeRepository.GetNoticeById(id, noticeId, token);

        if (notice is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Notification_SystemNotEditable)],
                StatusCodes.Status404NotFound));

        if (notice.Type != NoticeType.Normal)
            return BadRequest(
                new RequestResponse(localizer[nameof(Resources.Program.Notification_SystemNotDeletable)]));

        await gameNoticeRepository.RemoveNotice(notice, token);

        return Ok();
    }


    /// <summary>
    /// Create Division
    /// </summary>
    /// <remarks>
    /// Add a new division for a game; requires administrator privileges
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="model">Division information</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully created division</response>
    [HttpPost("Games/{id:int}/Divisions")]
    [ProducesResponseType(typeof(Division), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [RequireAdmin]
    public async Task<IActionResult> CreateDivision([FromRoute] int id, [FromBody] DivisionCreateModel model,
        CancellationToken token)
    {
        var game = await gameRepository.GetGameById(id, token);
        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        var division = await divisionRepository.CreateDivision(game, model, token);

        return Ok(division);
    }

    /// <summary>
    /// Get Divisions
    /// </summary>
    /// <remarks>
    /// Retrieve all divisions for a game; requires administrator privileges
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved divisions</response>
    [HttpGet("Games/{id:int}/Divisions")]
    [ProducesResponseType(typeof(Division[]), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDivisions([FromRoute] int id, CancellationToken token)
    {
        var game = await gameRepository.GetGameById(id, token);
        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        var divisions = await divisionRepository.GetDivisions(id, token);
        return Ok(divisions);
    }

    /// <summary>
    /// Update Division
    /// </summary>
    /// <remarks>
    /// Update a division for a game; requires administrator privileges
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="divisionId">Division ID</param>
    /// <param name="model">Division information</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully updated division</response>
    [HttpPut("Games/{id:int}/Divisions/{divisionId:int}")]
    [ProducesResponseType(typeof(Division), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [RequireAdmin]
    public async Task<IActionResult> UpdateDivision([FromRoute] int id, [FromRoute] int divisionId,
        [FromBody] DivisionEditModel model, CancellationToken token)
    {
        var division = await divisionRepository.GetDivision(id, divisionId, token);
        if (division is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Division_NotFound)],
                StatusCodes.Status404NotFound));

        await divisionRepository.UpdateDivision(division, model, token);

        return Ok(division);
    }

    /// <summary>
    /// Delete Division
    /// </summary>
    /// <remarks>
    /// Delete a division for a game; requires administrator privileges
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="divisionId">Division ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully deleted division</response>
    [HttpDelete("Games/{id:int}/Divisions/{divisionId:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [RequireAdmin]
    public async Task<IActionResult> DeleteDivision([FromRoute] int id, [FromRoute] int divisionId,
        CancellationToken token)
    {
        var division = await divisionRepository.GetDivision(id, divisionId, token);
        if (division is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Division_NotFound)],
                StatusCodes.Status404NotFound));

        await divisionRepository.RemoveDivision(division, token);

        return Ok();
    }

    /// <summary>
    /// Add Game Challenge
    /// </summary>
    /// <remarks>
    /// Adding a game challenge requires administrator privileges
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="model"></param>
    /// <param name="token"></param>
    /// <response code="200">Successfully added game challenge</response>
    [HttpPost("Games/{id:int}/Challenges")]
    [ProducesResponseType(typeof(ChallengeEditDetailModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddGameChallenge([FromRoute] int id, [FromBody] ChallengeInfoModel model,
        CancellationToken token)
    {
        if (await EnsureCanCreateChallengeAsync(model.Category, token) is { } denied)
            return denied;

        var game = await gameRepository.GetGameById(id, token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        var author = await GetCurrentUserAsync(token);

        var res = await challengeRepository.CreateChallenge(game,
            new GameChallenge
            {
                Title = model.Title,
                Type = model.Type,
                Category = model.Category,
                // 记录出题人，供「只能修改自己新增的题目」判定
                AuthorId = author?.Id
            }, token);

        return Ok(ChallengeEditDetailModel.FromChallenge(res));
    }

    /// <summary>
    /// Get All Game Challenges
    /// </summary>
    /// <remarks>
    /// Retrieving all game challenges requires administrator privileges
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved game challenges</response>
    [HttpGet("Games/{id:int}/Challenges")]
    [ProducesResponseType(typeof(ChallengeInfoModel[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetGameChallenges([FromRoute] int id, CancellationToken token)
    {
        var challenges = (await challengeRepository.GetChallenges(id, token)).ToList();

        // 出题人可以看到全部题目（便于了解全局与避免撞题），
        // 但只有自己能管理的题目才标记为可编辑，其余在编辑页只读。
        var currentUser = await GetCurrentUserAsync(token);

        // 作者名映射：AuthorId -> UserName
        var authorIds = challenges
            .Where(c => c.AuthorId.HasValue)
            .Select(c => c.AuthorId!.Value)
            .Distinct()
            .ToList();

        var authorNames = new Dictionary<Guid, string?>();

        if (authorIds.Count > 0)
        {
            var authors = await dbContext.Users
                .Where(u => authorIds.Contains(u.Id))
                .Select(u => new KeyValuePair<Guid, string?>(u.Id, u.UserName))
                .ToListAsync(token);

            foreach (var (uid, name) in authors)
                authorNames[uid] = name;
        }

        var scoreboard = await gameRepository.TryGetScoreboard(id, token);

        var result = challenges.Select(c =>
        {
            var model = ChallengeInfoModel.FromChallenge(c,
                c.AuthorId.HasValue && authorNames.TryGetValue(c.AuthorId.Value, out var name) ? name : null);

            model.CanEdit = currentUser is not null &&
                            currentUser.CanManageChallenge(c.Category, c.AuthorId);

            // 能看/能测：同方向的题目可交叉查看与测题，但不可修改
            model.CanTest = currentUser is not null &&
                            currentUser.CanViewChallenge(c.Category, c.AuthorId);

            if (scoreboard is not null && scoreboard.ChallengeMap.TryGetValue(c.Id, out var challengeInfo))
                model.Score = challengeInfo.Score;
            return model;
        });

        return Ok(result);
    }

    /// <summary>
    /// Flush Scoreboard Cache
    /// </summary>
    /// <param name="id">Game ID</param>
    /// <param name="token"></param>
    /// <response code="200"></response>
    [HttpPost("Games/{id:int}/Scoreboard/Flush")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [RequireAdmin]
    public async Task<IActionResult> FlushScoreboardCache([FromRoute] int id, CancellationToken token)
    {
        await cacheHelper.FlushScoreboardCache(id, token);
        return Ok();
    }

    /// <summary>
    /// Get Game Challenge
    /// </summary>
    /// <remarks>
    /// Retrieving a game challenge requires administrator privileges
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="cId">Challenge ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved game challenge</response>
    [HttpGet("Games/{id:int}/Challenges/{cId:int}")]
    [ProducesResponseType(typeof(ChallengeEditDetailModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetGameChallenge([FromRoute] int id, [FromRoute] int cId, CancellationToken token)
    {
        var challenge = await challengeRepository.GetChallenge(id, cId, token);

        // 查看权限：本人题目 / 被授权方向 / 管理员。
        // 同方向他人题目可查看与测题，但只是只读视角。
        if (await EnsureCanViewChallengeAsync(challenge, token) is { } denied)
            return denied;

        var user = await GetCurrentUserAsync(token);

        // 只有能管理该题目的人才加载并看到 flag 明文；
        // 交叉查看者不返回 flag，避免泄露他人题目的答案。
        var canManage = user is not null &&
                        user.CanManageChallenge(challenge.Category, challenge.AuthorId);

        // Do not load flags for dynamic containers
        if (canManage && challenge.Type != ChallengeType.DynamicContainer)
            await challengeRepository.LoadFlags(challenge, token);

        var result = ChallengeEditDetailModel.FromChallenge(challenge);

        result.CanTest = true; // 能进到这里即可测题
        result.CanEdit = canManage;

        // 交叉查看时需要知道题目归属
        if (challenge.AuthorId.HasValue)
            result.AuthorName = await dbContext.Users
                .Where(u => u.Id == challenge.AuthorId.Value)
                .Select(u => u.UserName)
                .FirstOrDefaultAsync(token);

        if (!canManage)
        {
            result.Flags = [];
            result.FlagTemplate = null;
        }

        var scoreboard = await gameRepository.TryGetScoreboard(id, token);

        if (scoreboard is not null && scoreboard.ChallengeMap.TryGetValue(cId, out var challengeInfo))
            result.AcceptedCount = challengeInfo.SolvedCount;

        return Ok(result);
    }

    /// <summary>
    /// Update Game Challenge Information
    /// </summary>
    /// <remarks>
    /// Updating a game challenge, requires administrator privileges. Flags are not affected; use Flag-related APIs to modify
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="cId">Challenge ID</param>
    /// <param name="model">Challenge information</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully updated game challenge</response>
    [HttpPut("Games/{id:int}/Challenges/{cId:int}")]
    [ProducesResponseType(typeof(ChallengeEditDetailModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateGameChallenge([FromRoute] int id, [FromRoute] int cId,
        [FromBody] ChallengeUpdateModel model, CancellationToken token)
    {
        await using var transaction = await challengeRepository.BeginTransactionAsync(token);

        var game = await gameRepository.GetGameById(id, token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        var res = await challengeRepository.GetChallenge(id, cId, token);

        if (await EnsureCanManageChallengeAsync(res, token) is { } denied)
            return denied;

        // NOTE: IsEnabled can only be updated outside the edit page
        if (model.IsEnabled is true && !res.IsEnabled && res.Type != ChallengeType.DynamicContainer)
        {
            await challengeRepository.LoadFlags(res, token);

            if (res.Flags.Count == 0)
                return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Challenge_NoFlag)]));
        }

        if (model.EnableTrafficCapture is true && !res.Type.IsContainer())
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Challenge_CaptureNotAllowed)]));

        if (model.FileName is not null && string.IsNullOrWhiteSpace(model.FileName))
            return BadRequest(
                new RequestResponse(localizer[nameof(Resources.Program.Challenge_DynamicAssetsNotNullable)]));

        var hintUpdated = model.IsHintUpdated(res.Hints?.GetSetHashCode());

        if (!string.IsNullOrWhiteSpace(model.FlagTemplate) && res.Type == ChallengeType.DynamicContainer &&
            !model.IsValidFlagTemplate())
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Challenge_FlagTooTrivial)]));

        // 改方向时校验新方向是否在授权范围内
        if (model.Category is { } targetCategory && targetCategory != res.Category &&
            await EnsureCanMoveToCategoryAsync(targetCategory, token) is { } moveDenied)
            return moveDenied;

        res.Update(model);

        switch (model.IsEnabled)
        {
            case true:
                {
                    // Will also update IsEnabled
                    await challengeRepository.EnsureInstances(res, game, token);

                    if (game.IsActive)
                        await gameNoticeRepository.AddNotice(
                            new() { Game = game, Type = NoticeType.NewChallenge, Values = [res.Title] }, token);
                    break;
                }
            case false when res.Type.IsContainer():
                await instanceRepository.DestroyAllContainers(res, token);
                break;
            case null:
                // do nothing
                break;
        }

        if (game.IsActive && res.IsEnabled && hintUpdated)
            await gameNoticeRepository.AddNotice(
                new() { Game = game, Type = NoticeType.NewHint, Values = [res.Title] },
                token);

        await challengeRepository.SaveAsync(token);

        await transaction.CommitAsync(token);

        // Always flush scoreboard
        await cacheHelper.FlushScoreboardCache(game.Id, token);

        return Ok(ChallengeEditDetailModel.FromChallenge(res));
    }

    /// <summary>
    /// Test Game Challenge Container
    /// </summary>
    /// <remarks>
    /// Testing a game challenge container requires administrator privileges
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="cId">Challenge ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully started game challenge container</response>
    [HttpPost("Games/{id:int}/Challenges/{cId:int}/Container")]
    [ProducesResponseType(typeof(ContainerInfoModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateTestContainer([FromRoute] int id, [FromRoute] int cId,
        CancellationToken token)
    {
        var challenge = await challengeRepository.GetChallenge(id, cId, token);

        if (await EnsureCanViewChallengeAsync(challenge, token) is { } denied)
            return denied;

        if (!challenge.Type.IsContainer())
            return BadRequest(
                new RequestResponse(localizer[nameof(Resources.Program.Game_ContainerCreationNotAllowed)]));

        if (challenge.ContainerImage is null || challenge.ExposePort is null)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Container_ConfigError)]));

        var user = await userManager.GetUserAsync(User);

        var container = await containerService.CreateContainerAsync(
            new()
            {
                TeamId = "admin",
                UserId = user!.Id,
                ChallengeId = challenge.Id,
                GameId = challenge.GameId,
                Flag = challenge.Type.IsDynamic() ? challenge.GenerateTestFlag() : null,
                Image = challenge.ContainerImage,
                CPUCount = challenge.CPUCount ?? 1,
                MemoryLimit = challenge.MemoryLimit ?? 64,
                StorageLimit = challenge.StorageLimit ?? 256,
                NetworkMode = challenge.NetworkMode ?? NetworkMode.Open,
                ExposedPort = challenge.ExposePort.Value,
            }, token);

        if (container is null)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Container_CreationFailed)]));

        challenge.TestContainer = container;
        await challengeRepository.SaveAsync(token);

        logger.Log(
            StaticLocalizer[nameof(Resources.Program.Container_TestContainerCreated), container.LogId],
            user,
            TaskStatus.Success);

        return Ok(ContainerInfoModel.FromContainer(container));
    }

    /// <summary>
    /// Destroy Test Game Challenge Container
    /// </summary>
    /// <remarks>
    /// Destroying a test game challenge container requires administrator privileges
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="cId">Challenge ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully destroyed game challenge container</response>
    [HttpDelete("Games/{id:int}/Challenges/{cId:int}/Container")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DestroyTestContainer([FromRoute] int id, [FromRoute] int cId,
        CancellationToken token)
    {
        var challenge = await challengeRepository.GetChallenge(id, cId, token);

        if (await EnsureCanViewChallengeAsync(challenge, token) is { } denied)
            return denied;

        if (challenge.TestContainer is null)
            return Ok();

        await containerRepository.DestroyContainer(challenge.TestContainer, token);

        return Ok();
    }

    /// <summary>
    /// Delete Game Challenge
    /// </summary>
    /// <remarks>
    /// Deleting a game challenge requires administrator privileges
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="cId">Challenge ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully deleted game challenge</response>
    [HttpDelete("Games/{id:int}/Challenges/{cId:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveGameChallenge([FromRoute] int id, [FromRoute] int cId,
        CancellationToken token)
    {
        var res = await challengeRepository.GetChallenge(id, cId, token);

        if (await EnsureCanManageChallengeAsync(res, token) is { } denied)
            return denied;

        await challengeRepository.RemoveChallenge(res, true, token);

        // Always flush scoreboard
        await cacheHelper.FlushScoreboardCache(id, token);

        return Ok();
    }

    /// <summary>
    /// Update Game Challenge Attachment
    /// </summary>
    /// <remarks>
    /// Updating a game challenge attachment requires administrator privileges; only for non-dynamic attachment challenges
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="cId">Challenge ID</param>
    /// <param name="model"></param>
    /// <param name="token"></param>
    /// <response code="200">Successfully updated game challenge</response>
    [HttpPost("Games/{id:int}/Challenges/{cId:int}/Attachment")]
    [ProducesResponseType(typeof(int), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAttachment([FromRoute] int id, [FromRoute] int cId,
        [FromBody] AttachmentCreateModel model, CancellationToken token)
    {
        var challenge = await challengeRepository.GetChallenge(id, cId, token);

        if (await EnsureCanManageChallengeAsync(challenge, token) is { } denied)
            return denied;

        if (challenge.Type == ChallengeType.DynamicAttachment)
            return BadRequest(
                new RequestResponse(localizer[nameof(Resources.Program.Challenge_UseAssetsApiForDynamic)]));

        await challengeRepository.UpdateAttachment(challenge, model, token);

        return Ok();
    }

    /// <summary>
    /// Add Game Challenge Flag
    /// </summary>
    /// <remarks>
    /// Adding a game challenge flag requires administrator privileges
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="cId">Challenge ID</param>
    /// <param name="models"></param>
    /// <param name="token"></param>
    /// <response code="200">Successfully added game challenge flags</response>
    [HttpPost("Games/{id:int}/Challenges/{cId:int}/Flags")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddFlags([FromRoute] int id, [FromRoute] int cId,
        [FromBody] FlagCreateModel[] models, CancellationToken token)
    {
        var challenge = await challengeRepository.GetChallenge(id, cId, token);

        if (await EnsureCanManageChallengeAsync(challenge, token) is { } denied)
            return denied;

        await challengeRepository.AddFlags(challenge, models, token);

        return Ok();
    }

    /// <summary>
    /// Delete Game Challenge Flag
    /// </summary>
    /// <remarks>
    /// Deleting a game challenge flag requires administrator privileges
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="cId">Challenge ID</param>
    /// <param name="fId">Flag ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully deleted game challenge flag</response>
    [HttpDelete("Games/{id:int}/Challenges/{cId:int}/Flags/{fId:int}")]
    [ProducesResponseType(typeof(TaskStatus), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveFlag([FromRoute] int id, [FromRoute] int cId, [FromRoute] int fId,
        CancellationToken token)
    {
        var challenge = await challengeRepository.GetChallenge(id, cId, token);

        if (await EnsureCanManageChallengeAsync(challenge, token) is { } denied)
            return denied;

        return Ok(await challengeRepository.RemoveFlag(challenge, fId, token));
    }


    /// <summary>
    /// Export game package
    /// </summary>
    /// <remarks>
    /// Export game with all challenges, divisions, and attachments as a ZIP file; requires Admin permission
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully exported game package</response>
    /// <response code="400">Invalid operation</response>
    /// <response code="404">Game not found</response>
    /// <response code="500">Internal server error during export</response>
    [HttpPost("Games/{id:int}/Export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status500InternalServerError)]
    [RequireAdmin]
    public async Task<IActionResult> ExportGame([FromRoute] int id, CancellationToken token = default)
    {
        try
        {
            var result = await exportService.ExportGameAsync(id, token);

            if (result is null)
            {
                logger.SystemLog(
                    StaticLocalizer[nameof(Resources.Program.Game_NotFound)],
                    TaskStatus.NotFound,
                    LogLevel.Warning);
                return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                    StatusCodes.Status404NotFound));
            }

            var fileName = $"{result.Game.Title}-export-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.zip";

            logger.SystemLog(
                StaticLocalizer[nameof(Resources.Program.Game_Exported), result.Game.Title, fileName]);

            var fileStream = new FileStream(
                result.ZipFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.DeleteOnClose | FileOptions.SequentialScan | FileOptions.Asynchronous);

            return File(fileStream, "application/zip", fileName, enableRangeProcessing: true);
        }
        catch (Exception ex)
        {
            logger.SystemLog(
                StaticLocalizer[nameof(Resources.Program.Game_ExportFailed), id],
                TaskStatus.Failed,
                LogLevel.Error);
            logger.LogError(ex, "Failed to export game {GameId}", id);
            return RequestResponse.Result(localizer[nameof(Resources.Program.Error_InternalServerError)],
                StatusCodes.Status500InternalServerError);
        }
    }

    private static readonly string[] AllowedImportContentTypes = ["application/zip", "application/x-zip-compressed"];

    /// <summary>
    /// Import game package
    /// </summary>
    /// <remarks>
    /// Import game from a ZIP package; requires Admin permission
    /// </remarks>
    /// <param name="file">Game package ZIP file</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully imported game, returns game ID</response>
    /// <response code="400">Invalid package or import failed</response>
    /// <response code="500">Internal server error during import</response>
    [RequireAdmin]
    [HttpPost("Games/Import")]
    [ProducesResponseType(typeof(int), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status500InternalServerError)]
    [RequestFormLimits(ValueLengthLimit = int.MaxValue, MultipartBodyLengthLimit = long.MaxValue)]
    public async Task<IActionResult> ImportGame(IFormFile file, CancellationToken token = default)
    {
        switch (file.Length)
        {
            case 0:
                logger.SystemLog(
                    StaticLocalizer[nameof(Resources.Program.File_SizeZero)],
                    TaskStatus.Failed,
                    LogLevel.Warning);
                return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.File_SizeZero)]));
            case > 512 * 1024 * 1024:
                // 512MB limit
                logger.SystemLog(
                    StaticLocalizer[nameof(Resources.Program.File_SizeTooLarge), file.FileName],
                    TaskStatus.Failed,
                    LogLevel.Warning);
                return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.File_SizeTooLarge)]));
        }

        if (!file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
            !AllowedImportContentTypes.Contains(file.ContentType))
        {
            logger.SystemLog(
                StaticLocalizer[nameof(Resources.Program.File_TypeNotSupported), file.FileName],
                TaskStatus.Failed,
                LogLevel.Warning);
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.File_TypeNotSupported)]));
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var gameId = await importService.ImportGameAsync(stream, token);

            if (gameId is null)
            {
                logger.SystemLog(
                    StaticLocalizer[nameof(Resources.Program.Game_ImportFailed), file.FileName],
                    TaskStatus.Failed,
                    LogLevel.Warning);
                return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_ImportFailed)]));
            }

            await cacheHelper.FlushRecentGamesCache(token);

            logger.SystemLog(
                StaticLocalizer[nameof(Resources.Program.Game_Imported), gameId, file.FileName]);

            return Ok(gameId);
        }
        catch (InvalidOperationException)
        {
            logger.SystemLog(
                StaticLocalizer[nameof(Resources.Program.Game_ImportInvalidPackage), file.FileName],
                TaskStatus.Failed,
                LogLevel.Warning);
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_ImportInvalidPackage)]));
        }
        catch (Exception ex)
        {
            logger.SystemLog(
                StaticLocalizer[nameof(Resources.Program.Game_ImportFailed), file.FileName],
                TaskStatus.Failed,
                LogLevel.Error);
            logger.LogError(ex, "Failed to import game from file {FileName}: {ErrorMessage}", file.FileName,
                ex.Message);
            return RequestResponse.Result(localizer[nameof(Resources.Program.Error_InternalServerError)],
                StatusCodes.Status500InternalServerError);
        }
    }
}
