using GZCTF.Models.Internal;

namespace GZCTF.Models.Request.Admin;

/// <summary>
/// Global configuration update
/// </summary>
public class ConfigEditModel
{
    /// <summary>
    /// User policy
    /// </summary>
    public AccountPolicy? AccountPolicy { get; set; }

    /// <summary>
    /// Global configuration
    /// </summary>
    public GlobalConfig? GlobalConfig { get; set; }

    /// <summary>
    /// Game policy
    /// </summary>
    public ContainerPolicy? ContainerPolicy { get; set; }

    /// <summary>
    /// 外部单点登录（登录页右侧区域）配置
    /// </summary>
    public SsoConfig? SsoConfig { get; set; }

    /// <summary>
    /// 首页赛道入口绑定配置
    /// </summary>
    public TrackConfig? TrackConfig { get; set; }
}
