using System.Security.Cryptography;
using System.Text;
using GZCTF.Models;
using GZCTF.Services.Storage;
using GZCTF.Services.Traffic;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Controllers;

/// <summary>Node collector API. The internal key is never placed in challenge Pods.</summary>
[ApiController]
[Route("api/internal/traffic")]
public class TrafficCollectorController(IConfiguration configuration, TrafficCaptureCoordinator capture,
    TrafficArchiveStore archives, S3StorageService storage) : ControllerBase
{
    bool Authorized()
    {
        var key = configuration["InternalApiKey"] ?? Environment.GetEnvironmentVariable("GZCTF_INTERNAL_API_KEY");
        return !string.IsNullOrEmpty(key) && Request.Headers.TryGetValue("X-Internal-Key", out var provided) &&
            CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(provided.ToString()));
    }

    [HttpGet("control")]
    public async Task<IActionResult> Control(CancellationToken token)
    {
        if (!Authorized()) return StatusCode(403, new RequestResponse("内部密钥错误或未配置"));
        archives.Refresh();
        var control = await capture.GetControl(token);
        return Ok(new
        {
            control.Enabled, control.Epoch,
            sessions = archives.Sessions.Where(s => archives.GetReport(s.Id)?.State is not ("archived" or "stopped"))
                .Select(s => new { session = s, stopped = archives.IsStopped(s.Id) })
        });
    }

    [HttpPut("sessions/{id:guid}/status")]
    public IActionResult Report(Guid id, [FromBody] TrafficCaptureReport report)
    {
        if (!Authorized()) return StatusCode(403, new RequestResponse("内部密钥错误或未配置"));
        archives.Refresh();
        if (archives.Find(id) is null) return NotFound();
        var prefix = $"{id:N}/";
        if (report.Bytes < 0 || report.Files.Any(f => !f.StartsWith(prefix, StringComparison.Ordinal) || f.Contains("..")))
            return BadRequest();
        archives.Report(id, report);
        return Ok();
    }

    [HttpPost("sessions/{id:guid}/upload")]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue)]
    public async Task<IActionResult> Upload(Guid id, [FromForm] IFormFile file,
        [FromForm] string sha256, CancellationToken token)
    {
        if (!Authorized()) return StatusCode(403, new RequestResponse("内部密钥错误或未配置"));
        archives.Refresh();
        if (archives.Find(id) is null) return NotFound();
        var name = file.FileName;
        if (file.Length < 24 || !System.Text.RegularExpressions.Regex.IsMatch(name, "^traffic-[0-9a-f]{32}\\.pcap$") ||
            !System.Text.RegularExpressions.Regex.IsMatch(sha256, "^[0-9a-f]{64}$"))
            return BadRequest(new RequestResponse("非法抓包文件或 SHA-256"));
        await using var stream = file.OpenReadStream();
        var actualHash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, token));
        if (actualHash != sha256) return BadRequest(new RequestResponse("传输文件 SHA-256 不匹配"));
        stream.Position = 0;
        var receipt = await storage.UploadVerifiedAsync(stream, $"{S3StorageService.KeyPrefix}/{id:N}/{name}",
            "application/vnd.tcpdump.pcap", token);
        return receipt is null
            ? StatusCode(503, new RequestResponse("上传或回读校验失败；请保留本地文件并重试"))
            : Ok(new { verified = true, receipt.Key, receipt.Size, receipt.Sha256 });
    }
}
