using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using GZCTF.Extensions;
using GZCTF.Services.Cache;
using MemoryPack;
using Microsoft.Extensions.Options;
using OpenTelemetry.Exporter;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Utilities.Encoders;
using Serilog.Sinks.Grafana.Loki;

namespace GZCTF.Models.Internal;

/// <summary>
/// Ignore when saving automatically
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class AutoSaveIgnoreAttribute : Attribute;

/// <summary>
/// Update cache when this property changes
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public sealed class CacheFlushAttribute(string cacheKey) : Attribute
{
    public string CacheKey { get; } = cacheKey;
}

/// <summary>
/// Account policy
/// </summary>
public class AccountPolicy
{
    /// <summary>
    /// Allow user registration
    /// </summary>
    public bool AllowRegister { get; set; } = true;

    /// <summary>
    /// Activate account upon registration
    /// </summary>
    public bool ActiveOnRegister { get; set; } = true;

    /// <summary>
    /// Use captcha verification
    /// </summary>
    [CacheFlush(CacheKey.CaptchaConfig)]
    public bool UseCaptcha { get; set; }

    /// <summary>
    /// Email confirmation required for registration, email change, and password recovery
    /// </summary>
    public bool EmailConfirmationRequired { get; set; }

    /// <summary>
    /// Email domain list, separated by commas
    /// </summary>
    public string EmailDomainList { get; set; } = string.Empty;
}

/// <summary>
/// Container policy
/// </summary>
public class ContainerPolicy
{
    /// <summary>
    /// Automatically destroy the oldest container when the limit is reached
    /// </summary>
    public bool AutoDestroyOnLimitReached { get; set; }

    /// <summary>
    /// User container limit, used to limit the number of exercise containers
    /// </summary>
    public int MaxExerciseContainerCountPerUser { get; set; } = 1;

    /// <summary>
    /// Default container lifetime in minutes
    /// </summary>
    [CacheFlush(CacheKey.ClientConfig)]
    [Range(1, 7200, ErrorMessageResourceName = nameof(Resources.Program.Model_OutOfRange),
        ErrorMessageResourceType = typeof(Resources.Program))]
    public int DefaultLifetime { get; set; } = 120;

    /// <summary>
    /// Extension duration for each renewal in minutes
    /// </summary>
    [CacheFlush(CacheKey.ClientConfig)]
    [Range(1, 7200, ErrorMessageResourceName = nameof(Resources.Program.Model_OutOfRange),
        ErrorMessageResourceType = typeof(Resources.Program))]
    public int ExtensionDuration { get; set; } = 120;

    /// <summary>
    /// Renewal window before container stops in minutes
    /// </summary>
    [CacheFlush(CacheKey.ClientConfig)]
    [Range(1, 360, ErrorMessageResourceName = nameof(Resources.Program.Model_OutOfRange),
        ErrorMessageResourceType = typeof(Resources.Program))]
    public int RenewalWindow { get; set; } = 10;
}

public class X25519KeyPair
{
    /// <summary>
    /// Public key
    /// </summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>
    /// Private key
    /// </summary>
    public string PrivateKey { get; set; } = string.Empty;

    public void RegenerateKeys(byte[] xorKey)
    {
        var kp = CryptoUtils.GenerateX25519KeyPair();
        var privateKey = (X25519PrivateKeyParameters)kp.Private;
        var publicKey = (X25519PublicKeyParameters)kp.Public;
        var privateKeyBytes = Codec.Xor(privateKey.GetEncoded(), xorKey);
        PublicKey = Base64.ToBase64String(publicKey.GetEncoded());
        PrivateKey = Base64.ToBase64String(privateKeyBytes);
    }

    public string? Decrypt(string data, byte[] xorKey)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(xorKey);

        try
        {
            var encryptedData = Base64.Decode(data);
            var privateKeyBytes = Codec.Xor(Base64.Decode(PrivateKey), xorKey);
            var privateKey = new X25519PrivateKeyParameters(privateKeyBytes);

            return Encoding.UTF8.GetString(CryptoUtils.DecryptData(encryptedData, privateKey));
        }
        catch
        {
            // If decryption fails, return null
            return null;
        }
    }
}

public class Ed25519KeyPair
{
    /// <summary>
    /// Public key
    /// </summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>
    /// Private key
    /// </summary>
    public string PrivateKey { get; set; } = string.Empty;

    public void RegenerateKeys(byte[] xorKey)
    {
        var kp = CryptoUtils.GenerateEd25519KeyPair();
        var privateKey = (Ed25519PrivateKeyParameters)kp.Private;
        var publicKey = (Ed25519PublicKeyParameters)kp.Public;
        var privateKeyBytes = Codec.Xor(privateKey.GetEncoded(), xorKey);
        PublicKey = Base64.ToBase64String(publicKey.GetEncoded());
        PrivateKey = Base64.ToBase64String(privateKeyBytes);
    }

    public string Sign(string data, byte[] xorKey, bool useUrlSafeBase64 = false)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(xorKey);

        var privateKeyBytes = Codec.Xor(Base64.Decode(PrivateKey), xorKey);
        var privateKey = new Ed25519PrivateKeyParameters(privateKeyBytes);
        return CryptoUtils.GenerateSignature(data, privateKey, SignAlgorithm.Ed25519, useUrlSafeBase64);
    }

    public bool Verify(string data, string signature, bool useUrlSafeBase64 = false)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(signature);

        try
        {
            var publicKey = new Ed25519PublicKeyParameters(Base64.Decode(PublicKey));
            return CryptoUtils.VerifySignature(data, signature, publicKey, SignAlgorithm.Ed25519, useUrlSafeBase64);
        }
        catch
        {
            // If verification fails, return false
            return false;
        }
    }
}

/// <summary>
/// A context for signature operations, including signing and verifying
/// </summary>
public record SignatureContext(Ed25519KeyPair EncryptedKeyPair, byte[] XorKey)
{
    public string Sign(string data, bool urlSafe = true) =>
        EncryptedKeyPair.Sign(data, XorKey, urlSafe);

    public bool Verify(string data, string signature, bool urlSafe = true) =>
        EncryptedKeyPair.Verify(data, signature, urlSafe);
}

/// <summary>
/// Configs controlled by the backend
/// </summary>
public class ManagedConfig
{
    /// <summary>
    /// Api encryption configuration
    /// </summary>
    public X25519KeyPair ApiEncryption { get; set; } = new();

    /// <summary>
    /// Api token configuration
    /// </summary>
    public Ed25519KeyPair ApiToken { get; set; } = new();
}

/// <summary>
/// Global settings
/// </summary>
public class GlobalConfig
{
    /// <summary>
    /// Default site description
    /// </summary>
    public const string DefaultDescription = "GZ::CTF is an open source CTF platform";

    /// <summary>
    /// Platform prefix name
    /// </summary>
    [CacheFlush(CacheKey.Index)]
    [CacheFlush(CacheKey.ClientConfig)]
    public string Title { get; set; } = "GZ";

    /// <summary>
    /// Platform slogan
    /// </summary>
    [CacheFlush(CacheKey.ClientConfig)]
    public string Slogan { get; set; } = "Hack for fun not for profit";

    /// <summary>
    /// Site description information
    /// </summary>
    [CacheFlush(CacheKey.Index)]
    public string? Description { get; set; } = DefaultDescription;

    /// <summary>
    /// Footer information
    /// </summary>
    [CacheFlush(CacheKey.ClientConfig)]
    public string? FooterInfo { get; set; }

    /// <summary>
    /// Custom theme color
    /// </summary>
    [CacheFlush(CacheKey.ClientConfig)]
    public string? CustomTheme { get; set; }

    /// <summary>
    /// Use asymmetric encryption for API requests
    /// </summary>
    [CacheFlush(CacheKey.ClientConfig)]
    public bool ApiEncryption { get; set; }

    /// <summary>
    /// Platform logo hash
    /// </summary>
    [AutoSaveIgnore]
    public string? LogoHash { get; set; }

    /// <summary>
    /// Platform favicon hash
    /// </summary>
    [AutoSaveIgnore]
    public string? FaviconHash { get; set; }

    [JsonIgnore]
    public string? LogoUrl => string.IsNullOrEmpty(LogoHash) ? null : $"/assets/{LogoHash}/logo";

    /// <summary>
    /// Platform name, used for email and homepage rendering
    /// </summary>
    [JsonIgnore]
    public string Platform => string.IsNullOrEmpty(Title) ? "GZ::CTF" : $"{Title}::CTF";
}

/// <summary>
/// Client configuration
/// </summary>
[MemoryPackable]
public partial class ClientConfig
{
    /// <summary>
    /// Platform prefix name
    /// </summary>
    public string Title { get; set; } = "GZ";

    /// <summary>
    /// Platform slogan
    /// </summary>
    public string Slogan { get; set; } = "Hack for fun not for profit";

    /// <summary>
    /// Footer information
    /// </summary>
    public string? FooterInfo { get; set; }

    /// <summary>
    /// Custom theme color
    /// </summary>
    public string? CustomTheme { get; set; }

    /// <summary>
    /// The public key used for API requests
    /// </summary>
    public string? ApiPublicKey { get; set; }

    /// <summary>
    /// Platform logo URL
    /// </summary>
    public string? LogoUrl { get; set; }

    /// <summary>
    /// Container port mapping type
    /// </summary>
    public ContainerPortMappingType PortMapping { get; set; } = ContainerPortMappingType.Default;

    /// <summary>
    /// Default container lifetime in minutes
    /// </summary>
    public int DefaultLifetime { get; set; } = 120;

    /// <summary>
    /// Extension duration for each renewal in minutes
    /// </summary>
    public int ExtensionDuration { get; set; } = 120;

    /// <summary>
    /// Renewal window before container stops in minutes
    /// </summary>
    public int RenewalWindow { get; set; } = 10;

    /// <summary>
    /// 登录页外部登录（SSO）区配置，未启用时为 null
    /// </summary>
    public ClientSsoConfig? Sso { get; set; }

    [JsonIgnore]
    public DateTimeOffset UpdateTimeUtc { get; set; } = DateTimeOffset.UtcNow;

    public static ClientConfig FromServiceProvider(IServiceProvider serviceProvider) =>
        FromConfigs(
            serviceProvider.GetRequiredService<IOptionsSnapshot<GlobalConfig>>().Value,
            serviceProvider.GetRequiredService<IOptionsSnapshot<ContainerPolicy>>().Value,
            serviceProvider.GetRequiredService<IOptionsSnapshot<ContainerProvider>>().Value,
            serviceProvider.GetRequiredService<IOptionsSnapshot<ManagedConfig>>().Value,
            serviceProvider.GetRequiredService<IOptionsSnapshot<SsoConfig>>().Value);

    private static ClientConfig FromConfigs(GlobalConfig globalConfig, ContainerPolicy containerPolicy,
        ContainerProvider containerProvider, ManagedConfig managedConfig, SsoConfig ssoConfig) =>
        new()
        {
            Title = globalConfig.Title,
            Slogan = globalConfig.Slogan,
            FooterInfo = globalConfig.FooterInfo,
            CustomTheme = globalConfig.CustomTheme,
            LogoUrl = globalConfig.LogoUrl,
            ApiPublicKey = globalConfig.ApiEncryption ? managedConfig.ApiEncryption.PublicKey : null,
            PortMapping = containerProvider.PortMappingType,
            DefaultLifetime = containerPolicy.DefaultLifetime,
            ExtensionDuration = containerPolicy.ExtensionDuration,
            RenewalWindow = containerPolicy.RenewalWindow,
            Sso = ClientSsoConfig.FromConfig(ssoConfig)
        };
}

/// <summary>
/// 下发给前端登录页的外部登录（SSO）区配置
/// </summary>
[MemoryPackable]
public partial class ClientSsoConfig
{
    /// <summary>区域标题</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>区域说明</summary>
    public string? Description { get; set; }

    /// <summary>登录入口列表</summary>
    public List<ClientSsoProvider> Providers { get; set; } = [];

    /// <summary>由服务端配置解析而来；已禁用或无有效入口时返回 null</summary>
    public static ClientSsoConfig? FromConfig(SsoConfig config)
    {
        if (!config.Enabled)
            return null;

        var providers = ParseProviders(config.Providers);
        if (providers.Count == 0)
            return null;

        return new ClientSsoConfig
        {
            Title = config.Title,
            Description = config.Description,
            Providers = providers
        };
    }

    /// <summary>
    /// 解析 CSV 形式的入口列表，每行字段以 | 分隔：
    /// 标题|短名|链接|图标URL|新窗口(0/1)|校验地址|换token地址|clientId|clientSecret
    /// </summary>
    internal static List<ClientSsoProvider> ParseProviders(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        var result = new List<ClientSsoProvider>();

        foreach (var line in raw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('|', StringSplitOptions.TrimEntries);
            if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0]))
                continue;

            var title = parts[0];
            var provider = Field(parts, 1);

            // 短名留空时用标题兜底，保证每个入口都有可路由的标识
            if (string.IsNullOrWhiteSpace(provider))
                provider = title;

            result.Add(new ClientSsoProvider
            {
                Title = title,
                Provider = provider,
                Link = Field(parts, 2),
                Icon = NullIfEmpty(Field(parts, 3)),
                NewWindow = Field(parts, 4) is "1" or "true" or "True",
                ValidateUrl = Field(parts, 5),
                TokenUrl = Field(parts, 6),
                ClientId = Field(parts, 7),
                ClientSecret = Field(parts, 8)
            });
        }

        return result;
    }

    static string Field(string[] parts, int index) =>
        parts.Length > index ? parts[index] : string.Empty;

    static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}

/// <summary>
/// 单个外部登录入口
/// </summary>
[MemoryPackable]
public partial class ClientSsoProvider
{
    /// <summary>按钮显示文案</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>提供方短名，用于路由 /api/account/sso/login/{provider}</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>登录跳转地址（站内相对路径或外部绝对地址）</summary>
    public string Link { get; set; } = string.Empty;

    /// <summary>图标 URL，可为空</summary>
    public string? Icon { get; set; }

    /// <summary>是否在新窗口打开</summary>
    public bool NewWindow { get; set; }

    /// <summary>CAS serviceValidate 地址，仅 CAS 流程需要</summary>
    [JsonIgnore]
    public string ValidateUrl { get; set; } = string.Empty;

    /// <summary>OAuth 换 token 地址，仅 OAuth 流程需要</summary>
    [JsonIgnore]
    public string TokenUrl { get; set; } = string.Empty;

    [JsonIgnore]
    public string ClientId { get; set; } = string.Empty;

    [JsonIgnore]
    public string ClientSecret { get; set; } = string.Empty;
}

#region Mail Config

public class SmtpConfig
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 587;
    public bool BypassCertVerify { get; set; }
}

public class EmailConfig
{
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? SenderAddress { get; set; } = string.Empty;
    public string? SenderName { get; set; } = string.Empty;
    public SmtpConfig? Smtp { get; set; } = new();
}

#endregion

#region SSO Config

/// <summary>
/// 外部单点登录（SSO）配置。
/// 右侧登录区展示若干可自定义的登录入口（OA / 学校邮箱等）。
/// 注：GZCTF 配置机制不支持数组类型，故列表项以 CSV 字符串存储。
/// </summary>
public class SsoConfig
{
    /// <summary>是否在登录页展示外部登录区（关闭后登录页只保留左侧账号密码登录）</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>右侧区域标题，例如「统一身份认证」</summary>
    public string Title { get; set; } = "统一身份认证登录";

    /// <summary>右侧区域副标题说明（可为空）</summary>
    public string? Description { get; set; } = "使用学校统一认证或校园邮箱登录";

    /// <summary>
    /// 登录入口列表，每项一行，字段以 | 分隔：
    /// 标题|短名|链接|图标URL|新窗口(0/1)|校验地址|换token地址|clientId|clientSecret
    /// 后四项可留空。示例：
    /// OA 统一认证|oa|/api/account/sso/login/oa||0|https://sso.example.edu/cas/serviceValidate|||
    /// 学校邮箱登录|mail|https://mail.example.edu/login||1||||
    /// </summary>
    public string Providers { get; set; } = string.Empty;

    /// <summary>是否允许新用户通过 SSO 首次登录时自动注册账号</summary>
    public bool AllowAutoRegister { get; set; } = true;

    /// <summary>SSO 登录成功后跳转路径</summary>
    public string RedirectPath { get; set; } = "/";
}

#endregion

#region Container Provider

[JsonConverter(typeof(JsonStringEnumConverter<ContainerProviderType>))]
public enum ContainerProviderType
{
    Docker,
    Kubernetes
}

[JsonConverter(typeof(JsonStringEnumConverter<ContainerPortMappingType>))]
public enum ContainerPortMappingType
{
    /// Use default to map the container port to a random port on the host
    Default,

    /// Use platform proxy to map the container tcp to wss
    PlatformProxy
}

public class ContainerProvider
{
    public ContainerProviderType Type { get; set; } = ContainerProviderType.Docker;
    public ContainerPortMappingType PortMappingType { get; set; } = ContainerPortMappingType.Default;
    public bool EnableTrafficCapture { get; set; }
    public string PublicEntry { get; set; } = string.Empty;
    public KubernetesConfig? KubernetesConfig { get; set; }
    public DockerConfig? DockerConfig { get; set; }
}

public class DockerConfig
{
    public string Uri { get; set; } = string.Empty;
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string? ChallengeNetwork { get; set; }
}

public class KubernetesConfig
{
    public string Namespace { get; set; } = "gzctf-challenges";
    public string KubeConfig { get; set; } = "kube-config.yaml";
    public string[]? AllowCidr { get; set; }
    public string[]? Dns { get; set; }
}

public class RegistrySet<T> : Dictionary<string, T>
    where T : class
{
    public T? GetForImage(string image)
    {
        if (string.IsNullOrWhiteSpace(image))
            return null;

        image = image.Contains("://") ? image : $"https://{image}";

        if (!Uri.TryCreate(image, UriKind.Absolute, out var uri) || uri.HostNameType == UriHostNameType.Unknown)
            return null;

        return TryGetValue(uri.Authority, out var cfg) ? cfg :
            TryGetValue(uri.Host, out var cfgHost) ? cfgHost : null;
    }
}

public class RegistryConfig
{
    public string? ServerAddress { get; set; }
    public string? UserName { get; set; }
    public string? Password { get; set; }

    public bool Valid => !string.IsNullOrEmpty(UserName) &&
                         !string.IsNullOrEmpty(Password);
}

#endregion

#region Captcha Provider

[JsonConverter(typeof(JsonStringEnumConverter<CaptchaProvider>))]
public enum CaptchaProvider
{
    None,
    HashPow,
    CloudflareTurnstile
}

public class HashPowConfig
{
    // How many leading zeros the hash should have
    private int _difficulty = 18;

    public int Difficulty
    {
        set => _difficulty = value;
        get => _difficulty = Math.Clamp(_difficulty, 8, 48);
    }
}

public class CaptchaConfig
{
    public CaptchaProvider Provider { get; set; }
    public string? SecretKey { get; set; }
    public string? SiteKey { get; set; }
    public HashPowConfig HashPow { get; set; } = new();
}

#endregion

#region Telemetry

public class TelemetryConfig
{
    public PrometheusConfig Prometheus { get; set; } = new();
    public OpenTelemetryConfig OpenTelemetry { get; set; } = new();
    public AzureMonitorConfig AzureMonitor { get; set; } = new();
    public ConsoleConfig Console { get; set; } = new();

    [JsonIgnore]
    public bool Enable => Prometheus.Enable || OpenTelemetry.Enable || AzureMonitor.Enable || Console.Enable;
}

public class PrometheusConfig
{
    public bool Enable { get; set; }
    public bool TotalNameSuffixForCounters { get; set; }
}

public class OpenTelemetryConfig
{
    public bool Enable { get; set; }
    public OtlpExportProtocol Protocol { get; set; }
    public string? EndpointUri { get; set; }
}

public class AzureMonitorConfig
{
    public bool Enable { get; set; }
    public string? ConnectionString { get; set; }
}

public class ConsoleConfig
{
    public bool Enable { get; set; }
}

#endregion

public class GrafanaLokiOptions
{
    public bool Enable { get; set; }
    public string? EndpointUri { get; set; }
    public LokiLabel[]? Labels { get; set; }
    public string[]? PropertiesAsLabels { get; set; }
    public LokiCredentials? Credentials { get; set; }
    public string? Tenant { get; set; }
    public LogLevel? MinimumLevel { get; set; }
}

public class ForwardedOptions : ForwardedHeadersOptions
{
    // For historical configuration compatibility as we accept string
    public new List<string>? KnownIPNetworks { get; set; }
    public new List<string>? KnownProxies { get; set; }

    // Old properties for compatibility
    public new List<string>? KnownNetworks { get; set; }
    public List<string>? TrustedNetworks { get; set; }
    public List<string>? TrustedProxies { get; set; }

    public void ToForwardedHeadersOptions(ForwardedHeadersOptions options)
    {
        // assign the same value to the base class via reflection
        var type = typeof(ForwardedHeadersOptions);
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        foreach (var property in properties)
        {
            // skip the properties that are not being set directly
            // .NET 10 update: `KnownNetworks` is obsolete, needs to be skipped
            if (property.Name is nameof(KnownIPNetworks) or nameof(KnownProxies) or "KnownNetworks")
                continue;

            property.SetValue(options, property.GetValue(this));
        }

        // Handle KnownIPNetworks
        Action<string> addNetwork = networkString =>
        {
            // split the network into address and prefix length
            var parts = networkString.Split('/');
            if (parts.Length == 2 &&
                IPAddress.TryParse(parts[0], out var prefix) &&
                int.TryParse(parts[1], out var prefixLength))
                options.KnownIPNetworks.Add(new IPNetwork(prefix, prefixLength));
        };

        KnownIPNetworks?.ForEach(addNetwork);
        KnownNetworks?.ForEach(addNetwork);
        TrustedNetworks?.ForEach(addNetwork);

        // Handle KnownProxies
        Action<string> addProxies = proxy =>
            Array.ForEach(proxy.ResolveIP(), ip => options.KnownProxies.Add(ip));

        KnownProxies?.ForEach(addProxies);
        TrustedProxies?.ForEach(addProxies);
    }
}
