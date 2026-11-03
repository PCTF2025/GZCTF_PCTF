using GZCTF.Middlewares;
using GZCTF.Models;
using GZCTF.Services.Storage;
using GZCTF.Services.Traffic;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Controllers;

[ApiController]
[Route("api/[controller]")]
[RequireAdmin]
public class TrafficMonitorController(TrafficCaptureCoordinator capture, TrafficArchiveStore archives,
    S3StorageService storage, ILogger<TrafficMonitorController> logger) : ControllerBase
{
    [HttpGet("Status")]
    public async Task<IActionResult> Status(CancellationToken token)
    {
        archives.Refresh();
        var control = await capture.GetControl(token);
        var items = archives.Sessions.Select(s =>
        {
            var report = archives.GetReport(s.Id);
            var pending = Directory.EnumerateFiles(archives.SessionPath(s.Id), "*.ready", SearchOption.AllDirectories).Count();
            var stale = report?.State is "capturing" or "uploading" &&
                report.UpdatedAt < DateTimeOffset.UtcNow.AddSeconds(-30);
            return new
            {
                id = s.Id, pod = s.Pod, challengeId = s.ChallengeId, title = s.Title,
                team = s.Team, users = s.User, ip = s.IP, startedAt = s.StartedAt.ToString("O"),
                node = report?.Node, sizeMB = Math.Round((report?.Bytes ?? 0) / 1048576d, 2),
                state = stale ? "error" : report?.State == "archived" && pending > 0 ? "uploading" :
                    report?.State ?? (archives.IsStopped(s.Id) ? "stopping" : "waiting"),
                error = stale ? "节点采集器超过 30 秒未报告状态，请检查节点服务" : report?.Error,
                keepFiles = report?.Files ?? [],
                logFiles = Directory.EnumerateFiles(archives.SessionPath(s.Id), "*.uploaded", SearchOption.AllDirectories)
                    .Select(p => $"{s.Id:N}/{Path.GetRelativePath(Path.Combine(archives.SessionPath(s.Id), "outbox"), p).Replace('\\', '/')[..^9]}")
                    .ToArray(),
                pendingLogs = pending
            };
        }).ToArray();
        return Ok(new { updatedAt = DateTimeOffset.UtcNow.ToString("O"), enabled = control.Enabled, items });
    }

    [HttpPut("Settings")]
    public async Task<IActionResult> Settings([FromBody] TrafficCaptureSettings model, CancellationToken token)
    {
        var control = await capture.SetControl(model.Enabled, token);
        logger.Log($"靶机流量采集已{(control.Enabled ? "开启（仅新建靶机）" : "关闭（结束当前采集并校验归档）")}",
            HttpContext, TaskStatus.Success);
        return Ok(control);
    }

    [HttpGet("Download")]
    public async Task<IActionResult> Download([FromQuery] string file, CancellationToken token)
    {
        var parts = file.Replace('\\', '/').Split('/');
        if (parts.Length < 2 || parts.Any(p => p is "" or "." or "..") ||
            !Guid.TryParseExact(parts[0], "N", out var id) || archives.Find(id) is null)
            return NotFound(new RequestResponse("文件不存在", StatusCodes.Status404NotFound));
        var url = await storage.GetPresignedUrlAsync($"{S3StorageService.KeyPrefix}/{string.Join('/', parts)}",
            TimeSpan.FromMinutes(30), token);
        return url is null ? NotFound(new RequestResponse("对象存储中未找到文件", StatusCodes.Status404NotFound)) : Redirect(url);
    }
}

public record TrafficCaptureSettings(bool Enabled);
