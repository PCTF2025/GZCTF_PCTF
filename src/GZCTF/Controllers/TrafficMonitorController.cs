using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using GZCTF.Middlewares;
using GZCTF.Models;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Controllers;

/// <summary>
/// 靶机流量监控（外部采集脚本提供数据）
///
/// 采集由服务器上的独立服务完成（按 pod 抓包、已解出的保留），
/// 这里只负责把状态快照读出来展示，并提供 pcap 下载。
/// </summary>
[ApiController]
[Route("api/[controller]")]
[RequireGameReviewer]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status403Forbidden)]
public class TrafficMonitorController(
    ILogger<TrafficMonitorController> logger,
    Services.Storage.S3StorageService s3,
    IConfiguration configuration) : ControllerBase
{
    private static readonly string StatusPath =
        Environment.GetEnvironmentVariable("TRAFFIC_MONITOR_STATUS") ?? "/var/lib/gzctf-traffic/.status.json";

    private static readonly string FilesRoot =
        Environment.GetEnvironmentVariable("TRAFFIC_MONITOR_FILES") ?? "/var/lib/gzctf-traffic";

    /// <summary>
    /// 当前抓包状态
    /// </summary>
    [HttpGet("Status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Status()
    {
        if (!System.IO.File.Exists(StatusPath))
            return Ok(new { updatedAt = (string?)null, items = Array.Empty<object>() });

        try
        {
            var json = System.IO.File.ReadAllText(StatusPath);
            using var doc = JsonDocument.Parse(json);
            return Content(json, "application/json");
        }
        catch (Exception e)
        {
            logger.LogErrorMessage(e, "读取流量监控状态失败");
            return Ok(new { updatedAt = (string?)null, items = Array.Empty<object>() });
        }
    }

    /// <summary>
    /// 下载已保留的 pcap
    /// </summary>
    /// <param name="file">相对路径（由状态快照给出）</param>
    [HttpGet("Download")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download([FromQuery] string file)
    {
        if (string.IsNullOrWhiteSpace(file))
            return NotFound(new RequestResponse("文件不存在", StatusCodes.Status404NotFound));

        // 防目录穿越：拼接后必须仍在 FilesRoot 内
        var root = Path.GetFullPath(FilesRoot);
        var full = Path.GetFullPath(Path.Combine(root, file));

        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            return NotFound(new RequestResponse("文件不存在", StatusCodes.Status404NotFound));

        // 本地有就直接给（缓存，最快也最稳）
        if (System.IO.File.Exists(full))
        {
            var stream = System.IO.File.OpenRead(full);
            return File(stream, "application/vnd.tcpdump.pcap", Path.GetFileName(full));
        }

        // 本地已清理则回落到对象存储的签名地址
        var objectKey = $"{Services.Storage.S3StorageService.KeyPrefix}/{file.Replace('\\', '/')}";
        var url = await s3.GetPresignedUrlAsync(objectKey, TimeSpan.FromMinutes(30));

        if (!string.IsNullOrEmpty(url))
            return Redirect(url);

        return NotFound(new RequestResponse("文件不存在（本地与对象存储均未找到）", StatusCodes.Status404NotFound));
    }
}
