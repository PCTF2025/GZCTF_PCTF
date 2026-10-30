using System.ComponentModel.DataAnnotations;
using GZCTF.Models;
using GZCTF.Repositories.Interface;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Controllers;

/// <summary>
/// 内部接口：供外部服务（如 Minecraft 服务器插件）按队伍兑换动态 flag。
///
/// 只服务于「外部动态 flag」题目：这类题目不下发容器，所有队伍共用一台常开服务器，
/// 由插件拿选手提供的队伍 Token 向平台换取该队伍的专属 flag。
///
/// 鉴权使用固定密钥（配置项 InternalApiKey，或环境变量 GZCTF_INTERNAL_API_KEY），
/// 未配置该密钥时接口始终拒绝访问。
/// </summary>
[ApiController]
[Route("api/internal")]
public class InternalApiController(
    IGameInstanceRepository instanceRepository,
    IConfiguration configuration,
    ILogger<InternalApiController> logger) : ControllerBase
{
    private const string KeyHeader = "X-Internal-Key";

    private string? ConfiguredKey =>
        configuration["InternalApiKey"] ?? Environment.GetEnvironmentVariable("GZCTF_INTERNAL_API_KEY");

    /// <summary>
    /// 兑换队伍专属 flag
    /// </summary>
    /// <remarks>
    /// 调用方需在请求头 <c>X-Internal-Key</c> 中携带内部密钥。
    /// 首次兑换会为该队伍生成并持久化 flag，之后重复调用返回同一个值。
    /// </remarks>
    /// <param name="model">题目 ID 与队伍 Token</param>
    /// <param name="token"></param>
    /// <response code="200">成功返回 flag</response>
    /// <response code="403">密钥错误或未配置</response>
    /// <response code="404">题目未开启外部动态 flag，或队伍/实例不存在</response>
    [HttpPost("flag")]
    [ProducesResponseType(typeof(InternalFlagResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFlag([FromBody] InternalFlagRequest model, CancellationToken token)
    {
        var key = ConfiguredKey;

        if (string.IsNullOrEmpty(key))
        {
            logger.LogWarning("内部接口未配置 InternalApiKey，拒绝兑换请求");
            return StatusCode(StatusCodes.Status403Forbidden, new RequestResponse("内部接口未启用"));
        }

        if (!Request.Headers.TryGetValue(KeyHeader, out var provided) || provided != key)
            return StatusCode(StatusCodes.Status403Forbidden, new RequestResponse("内部密钥错误"));

        var flag = await instanceRepository.EnsureExternalFlag(model.GameId, model.ChallengeId,
            model.TeamToken, token);

        if (flag is null)
            return NotFound(new RequestResponse("题目未开启外部动态 flag，或队伍不存在",
                StatusCodes.Status404NotFound));

        return Ok(new InternalFlagResponse { Flag = flag });
    }
}

/// <summary>
/// 外部兑换 flag 请求
/// </summary>
public class InternalFlagRequest
{
    /// <summary>比赛 ID</summary>
    [Range(1, int.MaxValue)]
    public int GameId { get; set; }

    /// <summary>题目 ID</summary>
    [Range(1, int.MaxValue)]
    public int ChallengeId { get; set; }

    /// <summary>队伍 Token（比赛详情页可见）</summary>
    [Required]
    [MaxLength(64)]
    public string TeamToken { get; set; } = string.Empty;
}

/// <summary>
/// 外部兑换 flag 响应
/// </summary>
public class InternalFlagResponse
{
    public string Flag { get; set; } = string.Empty;
}
