using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using GZCTF.Models.Request.Edit;
using MemoryPack;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities.Encoders;

namespace GZCTF.Models.Data;

[MemoryPackable]
public partial class Game
{
    [Key]
    [Required]
    public int Id { get; set; }

    /// <summary>
    /// Game title
    /// </summary>
    [Required]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Token signature public key
    /// </summary>
    [Required]
    [MaxLength(Limits.GameKeyLength)]
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>
    /// Token signature private key
    /// </summary>
    [Required]
    [MaxLength(Limits.GameKeyLength)]
    public string PrivateKey { get; set; } = string.Empty;

    /// <summary>
    /// Whether to hide
    /// </summary>
    [Required]
    public bool Hidden { get; set; }

    /// <summary>
    /// Whether the game is in practice mode (most operations can still be performed after the game ends)
    /// </summary>
    public bool PracticeMode { get; set; } = true;

    /// <summary>
    /// Poster hash
    /// </summary>
    [MaxLength(Limits.FileHashLength)]
    public string? PosterHash { get; set; }

    /// <summary>
    /// Game description
    /// </summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// Detailed introduction of the game
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Teams can join without review
    /// </summary>
    public bool AcceptWithoutReview { get; set; }

    /// <summary>
    /// Whether writeup is required
    /// </summary>
    public bool WriteupRequired { get; set; }

    /// <summary>
    /// Game invitation code
    /// </summary>
    [MaxLength(Limits.InviteTokenLength)]
    public string? InviteCode { get; set; }

    /// <summary>
    /// Limit on the number of team members, 0 means no limit
    /// </summary>
    public int TeamMemberCountLimit { get; set; }

    /// <summary>
    /// Limit on the number of containers a team can have simultaneously
    /// </summary>
    public int ContainerCountLimit { get; set; } = 3;

    /// <summary>
    /// Start time
    /// </summary>
    [Required]
    [JsonPropertyName("start")]
    public DateTimeOffset StartTimeUtc { get; set; } = DateTimeOffset.FromUnixTimeSeconds(0);

    /// <summary>
    /// End time
    /// </summary>
    [Required]
    [JsonPropertyName("end")]
    public DateTimeOffset EndTimeUtc { get; set; } = DateTimeOffset.FromUnixTimeSeconds(0);

    /// <summary>
    /// Writeup submission deadline
    /// </summary>
    [Required]
    public DateTimeOffset WriteupDeadline { get; set; } = DateTimeOffset.FromUnixTimeSeconds(0);

    /// <summary>
    /// Additional notes for writeup
    /// </summary>
    [Required]
    public string WriteupNote { get; set; } = string.Empty;

    [JsonIgnore]
    [Column(nameof(BloodBonus))]
    public long BloodBonusValue { get; set; } = BloodBonus.DefaultValue;

    /// <summary>
    /// Whether challenges are organized and displayed by week
    /// </summary>
    public bool WeekModeEnabled { get; set; }

    /// <summary>
    /// Per-week time window. Each week has an explicit start / end, so gaps
    /// and overlaps between weeks are allowed. Null start means "not configured".
    /// 采用扁平字段存储：GZCTF 配置机制不支持数组与集合类型。
    /// </summary>
    public DateTimeOffset? Week1StartUtc { get; set; }
    public DateTimeOffset? Week1EndUtc { get; set; }
    public DateTimeOffset? Week2StartUtc { get; set; }
    public DateTimeOffset? Week2EndUtc { get; set; }
    public DateTimeOffset? Week3StartUtc { get; set; }
    public DateTimeOffset? Week3EndUtc { get; set; }
    public DateTimeOffset? Week4StartUtc { get; set; }
    public DateTimeOffset? Week4EndUtc { get; set; }
    public DateTimeOffset? Week5StartUtc { get; set; }
    public DateTimeOffset? Week5EndUtc { get; set; }

    /// <summary>
    /// Display name of each week, e.g. "第一周：基础入门". Falls back to
    /// "第 N 周" when null or empty.
    /// </summary>
    [MaxLength(64)]
    public string? Week1Name { get; set; }
    [MaxLength(64)]
    public string? Week2Name { get; set; }
    [MaxLength(64)]
    public string? Week3Name { get; set; }
    [MaxLength(64)]
    public string? Week4Name { get; set; }
    [MaxLength(64)]
    public string? Week5Name { get; set; }

    /// <summary>
    /// Display names of the two non-week buckets. Challenge = 挑战题, Misc = 其他题.
    /// </summary>
    [MaxLength(64)]
    public string? ChallengeBucketName { get; set; }
    [MaxLength(64)]
    public string? MiscBucketName { get; set; }

    /// <summary>
    /// Blood bonus
    /// </summary>
    [NotMapped]
    [Required]
    [MemoryPackIgnore]
    public BloodBonus BloodBonus
    {
        get => BloodBonus.FromValue(BloodBonusValue);
        set => BloodBonusValue = value.Val;
    }

    /// <summary>
    /// Whether the game is active
    /// </summary>
    [NotMapped]
    [JsonIgnore]
    [MemoryPackIgnore]
    public bool IsActive => StartTimeUtc <= DateTimeOffset.Now && DateTimeOffset.Now <= EndTimeUtc;

    /// <summary>
    /// Poster URL
    /// </summary>
    [NotMapped]
    [MemoryPackIgnore]
    public string? PosterUrl => GetPosterUrl(PosterHash);

    /// <summary>
    /// Team hash salt
    /// </summary>
    [NotMapped]
    [MemoryPackIgnore]
    public string TeamHashSalt => $"GZCTF@{PrivateKey}@PK".ToSHA256String();

    internal static string? GetPosterUrl(string? hash) => hash is null ? null : $"/assets/{hash}/poster";

    internal void GenerateKeyPair(byte[]? xorKey)
    {
        SecureRandom sr = new();
        Ed25519KeyPairGenerator kpg = new();
        kpg.Init(new Ed25519KeyGenerationParameters(sr));
        var kp = kpg.GenerateKeyPair();
        var privateKey = (Ed25519PrivateKeyParameters)kp.Private;
        var publicKey = (Ed25519PublicKeyParameters)kp.Public;

        PrivateKey =
            Base64.ToBase64String(xorKey is null
                ? privateKey.GetEncoded()
                : Codec.Xor(privateKey.GetEncoded(), xorKey));

        PublicKey = Base64.ToBase64String(publicKey.GetEncoded());
    }

    internal string Sign(string str, byte[]? xorKey)
    {
        Ed25519PrivateKeyParameters privateKey;
        if (xorKey is null)
            privateKey = new(Codec.Base64.DecodeToBytes(PrivateKey), 0);
        else
            privateKey = new(Codec.Xor(Codec.Base64.DecodeToBytes(PrivateKey), xorKey), 0);

        return CryptoUtils.GenerateSignature(str, privateKey, SignAlgorithm.Ed25519);
    }

    internal Game Update(GameInfoModel model)
    {
        Title = model.Title;
        Content = model.Content;
        Summary = model.Summary;
        Hidden = model.Hidden;
        PracticeMode = model.PracticeMode;
        AcceptWithoutReview = model.AcceptWithoutReview;
        InviteCode = model.InviteCode;
        EndTimeUtc = model.EndTimeUtc;
        StartTimeUtc = model.StartTimeUtc;
        TeamMemberCountLimit = model.TeamMemberCountLimit;
        ContainerCountLimit = model.ContainerCountLimit;
        WriteupNote = model.WriteupNote;
        WriteupRequired = model.WriteupRequired;
        WriteupDeadline = model.WriteupDeadline;
        BloodBonus = BloodBonus.FromValue(model.BloodBonusValue);
        WeekModeEnabled = model.WeekModeEnabled;
        Week1StartUtc = model.Week1StartUtc;
        Week1EndUtc = model.Week1EndUtc;
        Week2StartUtc = model.Week2StartUtc;
        Week2EndUtc = model.Week2EndUtc;
        Week3StartUtc = model.Week3StartUtc;
        Week3EndUtc = model.Week3EndUtc;
        Week4StartUtc = model.Week4StartUtc;
        Week4EndUtc = model.Week4EndUtc;
        Week5StartUtc = model.Week5StartUtc;
        Week5EndUtc = model.Week5EndUtc;
        Week1Name = Trim(model.Week1Name);
        Week2Name = Trim(model.Week2Name);
        Week3Name = Trim(model.Week3Name);
        Week4Name = Trim(model.Week4Name);
        Week5Name = Trim(model.Week5Name);
        ChallengeBucketName = Trim(model.ChallengeBucketName);
        MiscBucketName = Trim(model.MiscBucketName);

        return this;
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// 题目归属分桶：1-5 为周次，<see cref="ChallengeBucket"/> 为挑战题，
    /// <see cref="MiscBucket"/> 为其他题，null 表示未归类。
    /// </summary>
    public const int ChallengeBucket = 6;
    public const int MiscBucket = 7;

    /// <summary>取指定周次（1-5）的时间窗口，未配置时返回 null</summary>
    public (DateTimeOffset? Start, DateTimeOffset? End) GetWeekWindow(int week) =>
        week switch
        {
            1 => (Week1StartUtc, Week1EndUtc),
            2 => (Week2StartUtc, Week2EndUtc),
            3 => (Week3StartUtc, Week3EndUtc),
            4 => (Week4StartUtc, Week4EndUtc),
            5 => (Week5StartUtc, Week5EndUtc),
            _ => (null, null)
        };

    /// <summary>取指定周次的自定义名称，未设置时返回「第 N 周」</summary>
    public string GetWeekName(int week)
    {
        var custom = week switch
        {
            1 => Week1Name,
            2 => Week2Name,
            3 => Week3Name,
            4 => Week4Name,
            5 => Week5Name,
            _ => null
        };

        return string.IsNullOrWhiteSpace(custom) ? $"第 {week} 周" : custom;
    }

    /// <summary>取非周次分桶的名称</summary>
    public string GetBucketName(int bucket) => bucket switch
    {
        ChallengeBucket => string.IsNullOrWhiteSpace(ChallengeBucketName) ? "挑战题" : ChallengeBucketName,
        MiscBucket => string.IsNullOrWhiteSpace(MiscBucketName) ? "其他题" : MiscBucketName,
        _ => GetWeekName(bucket)
    };

    /// <summary>
    /// 判断某题目在当前时刻是否处于可提交状态。
    /// 规则：未归类 / 周次未配置时间窗口 → 始终可提交（不限制）；
    /// 配置了窗口则要求 now 落在 [Start, End] 内（任一端为 null 视为该端不限制）。
    /// </summary>
    public bool IsChallengeOpen(int? challengeWeek, DateTimeOffset now)
    {
        if (!WeekModeEnabled || challengeWeek is null)
            return true;

        // 挑战题 / 其他题两个分桶不受时间窗口约束
        if (challengeWeek is ChallengeBucket or MiscBucket)
            return true;

        var (start, end) = GetWeekWindow(challengeWeek.Value);
        if (start is null && end is null)
            return true;

        if (start is not null && now < start.Value)
            return false;

        if (end is not null && now > end.Value)
            return false;

        return true;
    }

    /// <summary>返回当前时刻所处的周次（1-5），不在任何已配置窗口内时返回 null</summary>
    public int? GetCurrentWeek(DateTimeOffset now)
    {
        if (!WeekModeEnabled)
            return null;

        for (var week = 1; week <= 5; week++)
        {
            var (start, end) = GetWeekWindow(week);
            if (start is null && end is null)
                continue;

            var afterStart = start is null || now >= start.Value;
            var beforeEnd = end is null || now <= end.Value;

            if (afterStart && beforeEnd)
                return week;
        }

        return null;
    }

    #region Db Relationship

    /// <summary>
    /// Game events
    /// </summary>
    [JsonIgnore]
    public List<GameEvent> GameEvents { get; set; } = [];

    /// <summary>
    /// Game notices
    /// </summary>
    [JsonIgnore]
    public List<GameNotice> GameNotices { get; set; } = [];

    /// <summary>
    /// Game submissions
    /// </summary>
    [JsonIgnore]
    public List<Submission> Submissions { get; set; } = [];

    /// <summary>
    /// Game challenges
    /// </summary>
    [JsonIgnore]
    public HashSet<GameChallenge> Challenges { get; set; } = [];

    /// <summary>
    /// Game participations
    /// </summary>
    [JsonIgnore]
    public HashSet<Participation> Participations { get; set; } = [];

    /// <summary>
    /// Game teams
    /// </summary>
    [JsonIgnore]
    public HashSet<Team>? Teams { get; set; }

    /// <summary>
    /// List of divisions for the game
    /// </summary>
    public HashSet<Division>? Divisions { get; set; }

    #endregion Db Relationship
}
