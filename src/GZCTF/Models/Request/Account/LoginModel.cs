using System.ComponentModel.DataAnnotations;
using GZCTF.Services;

namespace GZCTF.Models.Request.Account;

/// <summary>
/// Login
/// </summary>
public class LoginModel : ModelWithCaptcha
{
    /// <summary>
    /// Username or email
    /// </summary>
    [Required]
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// Password
    /// </summary>
    [Required]
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// 学校短名（可选）。来自学校邮箱认证（快速登录）入口，
    /// 登录成功后平台据此自动为该账号绑定学校。
    /// </summary>
    [MaxLength(Limits.MaxUserDataLength)]
    public string? SchoolSlug { get; set; }
}
