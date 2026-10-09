using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using GZCTF.Middlewares;
using GZCTF.Models.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using GZCTF.Models.Request.Assets;
using GZCTF.Repositories.Interface;
using GZCTF.Storage.Interface;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Localization;
using Microsoft.Net.Http.Headers;

namespace GZCTF.Controllers;

/// <summary>
/// File APIs
/// </summary>
[ApiController]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status403Forbidden)]
public class AssetsController(
    IBlobStorage storage,
    IBlobRepository blobService,
    AppDbContext dbContext,
    UserManager<UserInfo> userManager,
    ILogger<AssetsController> logger,
    IStringLocalizer<Program> localizer) : ControllerBase
{
    /// <summary>
    /// 当前登录用户
    /// </summary>
    private Task<UserInfo?> GetCurrentUserAsync() =>
        User.Identity?.IsAuthenticated ?? false
            ? userManager.GetUserAsync(User)
            : Task.FromResult<UserInfo?>(null);

    private readonly FileExtensionContentTypeProvider _extProvider = new();

    /// <summary>
    /// File retrieval interface
    /// </summary>
    /// <remarks>
    /// Retrieve a file by hash, filename is not matched
    /// </remarks>
    /// <param name="hash">File hash</param>
    /// <param name="filename">Download filename</param>
    /// <param name="token"></param>
    /// <response code="200">File retrieved successfully</response>
    /// <response code="404">File not found</response>
    /// <response code="400">Failed to retrieve file</response>
    [HttpGet("[controller]/{hash:length(64)}/{filename:minlength(1)}")]
    [ResponseCache(Duration = 60 * 60 * 24 * 7)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetFile([RegularExpression("[0-9a-f]{64}")] string hash, string filename,
        CancellationToken token)
    {
        var path = StoragePath.Combine(PathHelper.Uploads, hash[..2], hash[2..4], hash);

        if (!await storage.ExistsAsync(path, token))
        {
            var ip = HttpContext.Connection.RemoteIpAddress;
            logger.Log(StaticLocalizer[nameof(Resources.Program.Assets_FileNotFound), hash[..8], filename], ip,
                TaskStatus.NotFound,
                LogLevel.Warning);
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.File_NotFound)],
                StatusCodes.Status404NotFound));
        }

        if (!_extProvider.TryGetContentType(filename, out var contentType))
            contentType = MediaTypeNames.Application.Octet;

        var blob = await storage.GetBlobAsync(path, token);

        var stream = await storage.OpenReadAsync(path, token);
        var etag = new EntityTagHeaderValue($"\"{hash[8..16]}\"");

        return File(stream, contentType, filename, blob.LastModificationTime, etag);
    }

    /// <summary>
    /// File upload interface
    /// </summary>
    /// <remarks>
    /// Upload one or more files
    /// </remarks>
    /// <param name="files"></param>
    /// <param name="filename">Unified filename</param>
    /// <param name="token"></param>
    /// <response code="200">File(s) uploaded successfully</response>
    /// <response code="400">Failed to upload file(s)</response>
    /// <response code="401">Unauthorized user</response>
    /// <response code="403">Access denied</response>
    [RequireAssetUploader]
    [HttpPost("api/[controller]")]
    [ProducesResponseType(typeof(List<LocalFile>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [RequestFormLimits(ValueLengthLimit = int.MaxValue, MultipartBodyLengthLimit = long.MaxValue)]
    public async Task<IActionResult> Upload(List<IFormFile> files, [FromQuery] string? filename,
        CancellationToken token)
    {
        try
        {
            var currentUser = await GetCurrentUserAsync();
            List<LocalFile> results = [];

            foreach (var file in files.Where(file => file.Length > 0))
            {
                var res = await blobService.CreateOrUpdateBlob(file, filename, token);

                // 记录上传者，供素材库做归属过滤；
                // 同一文件被他人再次上传时不覆盖原归属（保留首个上传者）
                if (currentUser is not null && res.UploadedById is null)
                {
                    res.UploadedById = currentUser.Id;
                    await dbContext.SaveChangesAsync(token);
                }

                logger.SystemLog(
                    StaticLocalizer[nameof(Resources.Program.Assets_UpdateFile), res.Hash[..8],
                        filename ?? file.FileName, file.Length],
                    TaskStatus.Success, LogLevel.Debug);
                results.Add(res);
            }

            return Ok(results);
        }
        catch (Exception ex)
        {
            logger.LogErrorMessage(ex, ex.Message);
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Assets_IOError)]));
        }
    }

    /// <summary>
    /// 文件列表接口（管理端素材库）
    /// </summary>
    /// <remarks>
    /// 分页返回已上传文件的哈希、名称、大小与引用计数，
    /// 供管理端在题目正文中插入内链图片。
    /// </remarks>
    /// <param name="count">每页数量</param>
    /// <param name="skip">跳过数量</param>
    /// <param name="token"></param>
    /// <response code="200">文件列表获取成功</response>
    [RequireAssetUploader]
    [HttpGet("api/[controller]/list")]
    [ProducesResponseType(typeof(FileListModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery][Range(1, 200)] int count = 60,
        [FromQuery][Range(0, int.MaxValue)] int skip = 0,
        CancellationToken token = default)
    {
        var currentUser = await GetCurrentUserAsync();
        var isAdmin = currentUser is not null && currentUser.Role >= Role.Admin;

        LocalFile[] files;
        int total;

        if (isAdmin)
        {
            // 系统管理员可见全部素材
            files = await blobService.GetBlobsByTime(count, skip, token);
            total = await blobService.CountAsync(token);
        }
        else
        {
            if (currentUser is null)
                return Unauthorized(new RequestResponse(localizer[nameof(Resources.Program.Auth_LoginRequired)],
                    StatusCodes.Status401Unauthorized));

            // 其余用户（出题人 / 出题管理员 / 学校负责人）只能看到自己上传的素材
            files = await blobService.GetBlobsByOwner(currentUser.Id, count, skip, token);
            total = await blobService.CountByOwnerAsync(currentUser.Id, token);
        }

        return Ok(new FileListModel
        {
            Total = total,
            Files = files.Select(f => new FileItemModel
            {
                Hash = f.Hash,
                Name = f.Name,
                Size = f.FileSize,
                UploadTimeUtc = f.UploadTimeUtc,
                ReferenceCount = f.ReferenceCount,
                // 直接给出可用的内链，便于前端一键复制
                Url = $"/assets/{f.Hash}/{Uri.EscapeDataString(f.Name)}",
                UploadedById = f.UploadedById
            }).ToArray()
        });
    }

    /// <summary>
    /// File deletion interface
    /// </summary>
    /// <remarks>
    /// Delete a file by hash
    /// </remarks>
    /// <param name="hash"></param>
    /// <param name="token"></param>
    /// <response code="200">File deleted successfully</response>
    /// <response code="400">Failed to delete file</response>
    /// <response code="401">Unauthorized user</response>
    /// <response code="403">Access denied</response>
    [RequireAssetUploader]
    [HttpDelete("api/[controller]/{hash:length(64)}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Delete(string hash, CancellationToken token)
    {
        var currentUser = await GetCurrentUserAsync();
        var isAdmin = currentUser is not null && currentUser.Role >= Role.Admin;

        if (!isAdmin)
        {
            var target = await blobService.GetBlobByHash(hash, token);

            if (target is null)
                return NotFound();

            // 非管理员只能删除自己上传的素材
            if (target.UploadedById != currentUser?.Id)
                return new ObjectResult(new RequestResponse("你只能删除自己上传的素材"))
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };
        }

        var result = await blobService.DeleteBlobByHash(hash, token);

        logger.SystemLog(StaticLocalizer[nameof(Resources.Program.Assets_DeleteFile), hash[..8]], result,
            LogLevel.Information);

        return result switch
        {
            TaskStatus.Success => Ok(),
            TaskStatus.NotFound => NotFound(),
            _ => BadRequest(new RequestResponse(localizer[nameof(Resources.Program.File_DeletionFailed)]))
        };
    }
}
