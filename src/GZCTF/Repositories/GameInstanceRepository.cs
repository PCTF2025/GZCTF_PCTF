using GZCTF.Models.Internal;
using GZCTF.Repositories.Interface;
using GZCTF.Utils;
using GZCTF.Services.Container.Manager;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace GZCTF.Repositories;

public class GameInstanceRepository(
    AppDbContext context,
    IContainerManager service,
    ICheatInfoRepository cheatInfoRepository,
    IContainerRepository containerRepository,
    IGameEventRepository gameEventRepository,
    IOptionsSnapshot<ContainerPolicy> containerPolicy,
    ILogger<GameInstanceRepository> logger,
    IStringLocalizer<Program> localizer) : RepositoryBase(context), IGameInstanceRepository
{
    public async Task<GameInstance?> GetInstance(Participation part, int challengeId, CancellationToken token = default)
    {
        await using var transaction = await Context.Database.BeginTransactionAsync(token);

        var instance = await Context.GameInstances
            .Include(i => i.FlagContext)
            .Where(e => e.ChallengeId == challengeId && e.Participation == part)
            .SingleOrDefaultAsync(token);

        if (instance is null)
        {
            logger.SystemLog(
                StaticLocalizer[nameof(Resources.Program.InstanceRepository_NoInstance), part.Id, challengeId],
                TaskStatus.NotFound,
                LogLevel.Warning);
            await transaction.RollbackAsync(token);
            return null;
        }

        var challenge = instance.Challenge;

        if (!challenge.IsEnabled)
        {
            await transaction.RollbackAsync(token);
            return null;
        }

        if (instance.IsLoaded)
        {
            await transaction.RollbackAsync(token);
            // 已加载过的实例同样需要装载容器组，否则多靶机详情会退化成单入口
            await LoadContainersAsync(instance, token);
            return instance;
        }

        try
        {
            switch (instance.Challenge.Type)
            {
                // dynamic flag dispatch
                case ChallengeType.DynamicContainer:
                    instance.FlagContext = new()
                    {
                        Challenge = challenge,
                        Flag = challenge.GenerateDynamicFlag(part),
                        IsOccupied = true
                    };
                    break;
                case ChallengeType.DynamicAttachment:
                    var flags = await Context.FlagContexts
                        .Where(e => e.Challenge == challenge && !e.IsOccupied)
                        .ToListAsync(token);

                    if (flags.Count == 0)
                    {
                        logger.SystemLog(
                            StaticLocalizer[nameof(Resources.Program.InstanceRepository_DynamicFlagsNotEnough),
                                challenge.Title,
                                challenge.Id], TaskStatus.Failed,
                            LogLevel.Warning);
                        return null;
                    }

                    var pos = Random.Shared.Next(flags.Count);
                    flags[pos].IsOccupied = true;

                    instance.FlagId = flags[pos].Id;
                    break;
            }

            // instance.FlagContext is null by default
            // static flag does not need to be dispatched

            instance.IsLoaded = true;
            await SaveAsync(token);
            await transaction.CommitAsync(token);
        }
        catch
        {
            logger.SystemLog(
                StaticLocalizer[nameof(Resources.Program.InstanceRepository_GetInstanceFailed), part.Team.Name,
                    challenge.Title,
                    challenge.Id],
                TaskStatus.Failed, LogLevel.Warning);
            await transaction.RollbackAsync(token);
            return null;
        }

        await LoadContainersAsync(instance, token);

        return instance;
    }

    /// <summary>
    /// 装载实例的容器集合（单题多靶机）。
    ///
    /// Containers 是 NotMapped 的辅助属性，需要显式查询：
    /// 若主容器带有 GroupId，则取出同组全部容器并按顺序排列；
    /// 否则（单靶机题目与历史数据）仅使用主容器。
    /// </summary>
    private async Task LoadContainersAsync(GameInstance instance, CancellationToken token = default)
    {
        if (instance.Container is null)
        {
            instance.Containers = [];
            return;
        }

        var groupId = instance.Container.GroupId;

        if (groupId is null)
        {
            instance.Containers = [instance.Container];
            return;
        }

        var group = await Context.Containers
            .Where(c => c.GroupId == groupId &&
                        c.Status != ContainerStatus.Destroyed)
            .OrderBy(c => c.GroupOrder)
            .ToListAsync(token);

        // 主容器可能因状态变化未出现在结果中，仍需保证列表非空
        instance.Containers = group.Count > 0 ? group : [instance.Container];
    }

    public Task<GameInstance?> GetInstanceForSubmission(Participation team, int challengeId,
        CancellationToken token = default)
        => Context.GameInstances.IgnoreAutoIncludes()
            .Include(i => i.Challenge)
            .Where(i => i.ParticipationId == team.Id && i.ChallengeId == challengeId)
            .SingleOrDefaultAsync(token);

    public async Task<TaskResult<Container>> CreateContainer(GameInstance gameInstance, Team team, UserInfo user,
        Game game, CancellationToken token = default)
    {
        if (string.IsNullOrEmpty(gameInstance.Challenge.ContainerImage) ||
            gameInstance.Challenge.ExposePort is null)
        {
            logger.SystemLog(
                StaticLocalizer[nameof(Resources.Program.InstanceRepository_ContainerCreationFailed),
                    gameInstance.Challenge.Title],
                TaskStatus.Denied, LogLevel.Warning);
            return new TaskResult<Container>(TaskStatus.Failed);
        }

        // containerLimit == 0 means unlimited
        if (game.ContainerCountLimit > 0)
        {
            if (containerPolicy.Value.AutoDestroyOnLimitReached)
            {
                var running = await Context.GameInstances
                    .Where(i => i.Participation == gameInstance.Participation && i.Container != null)
                    .OrderBy(i => i.Container!.StartedAt).ToListAsync(token);

                var first = running.FirstOrDefault();
                if (running.Count >= game.ContainerCountLimit && first is not null)
                {
                    logger.Log(
                        StaticLocalizer[nameof(Resources.Program.InstanceRepository_ContainerAutoDestroy),
                            team.Name, first.Challenge.Title,
                            first.Container!.LogId],
                        user, TaskStatus.Success);
                    await containerRepository.DestroyContainer(running.First().Container!, token);
                }
            }
            else
            {
                var count = await Context.GameInstances.CountAsync(
                    i => i.Participation == gameInstance.Participation &&
                         i.Container != null, token);

                if (count >= game.ContainerCountLimit)
                    return new TaskResult<Container>(TaskStatus.Denied);
            }
        }

        if (gameInstance.Container is not null)
            return new TaskResult<Container>(TaskStatus.Success, gameInstance.Container);

        await Context.Entry(gameInstance).Reference(e => e.FlagContext).LoadAsync(token);

        var challenge = gameInstance.Challenge;

        // 单题多靶机：主镜像 + 其余靶机依次创建，同一实例下共享一个 GroupId。
        // 优先采用可视化拓扑编排；未配置拓扑时回退到「主镜像 + 附加靶机列表」的星形结构，
        // 两者都未配置时行为与原来完全一致（只创建主容器）。
        var nodes = BuildNodeList(challenge);
        var groupId = Guid.NewGuid();
        var container = await CreateOneAsync(
            BuildConfig(challenge, gameInstance, team, user, nodes[0].Image, nodes[0].ExposePort,
                nodes[0].EnableTrafficCapture, game.IsActive), groupId, nodes[0].Name, 0, token);

        if (container is null)
        {
            logger.SystemLog(
                StaticLocalizer[nameof(Resources.Program.InstanceRepository_ContainerCreationFailed),
                    gameInstance.Challenge.Title],
                TaskStatus.Failed, LogLevel.Warning);
            return new TaskResult<Container>(TaskStatus.Failed);
        }

        // 附加靶机创建失败时回滚已创建的容器，避免选手拿到残缺的多靶机实例
        var created = new List<Container> { container };
        var order = 1;


        foreach (var extra in nodes.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(extra.Image) || extra.ExposePort <= 0)
                continue;

            var extraContainer = await CreateOneAsync(
                BuildConfig(challenge, gameInstance, team, user, extra.Image, extra.ExposePort,
                    extra.EnableTrafficCapture, game.IsActive), groupId, extra.Name, order, token);

            if (extraContainer is null)
            {
                logger.SystemLog(
                    string.Format(
                        StaticLocalizer[nameof(Resources.Program.InstanceRepository_ContainerCreationFailed)],
                        $"{challenge.Title}（附加靶机 {extra.Name ?? extra.Image}）"),
                    TaskStatus.Failed, LogLevel.Warning);

                foreach (var done in created)
                    await containerRepository.DestroyContainer(done, token);

                // 附加靶机若已进入变更追踪但未落库，回滚时一并剥离，避免残留脏实体
                foreach (var done in created)
                    Context.Entry(done).State = EntityState.Detached;

                return new TaskResult<Container>(TaskStatus.Failed);
            }

            created.Add(extraContainer);
            order++;
        }

        // 附加靶机先落库拿到主键，避免只存在于内存中的容器导致列表丢失
        if (created.Count > 1)
            await Context.SaveChangesAsync(token);

        gameInstance.Containers = created;
        gameInstance.Container = container;
        gameInstance.LastContainerOperation = DateTimeOffset.UtcNow;

        await gameEventRepository.AddEvent(
            new()
            {
                Type = EventType.ContainerStart,
                GameId = gameInstance.Challenge.GameId,
                TeamId = gameInstance.Participation.TeamId,
                UserId = user.Id,
                Values = [gameInstance.Challenge.Id.ToString(), gameInstance.Challenge.Title]
            }, token);

        logger.Log(
            StaticLocalizer[nameof(Resources.Program.InstanceRepository_ContainerCreated), team.Name,
                gameInstance.Challenge.Title,
                container.LogId], user,
            TaskStatus.Success);

        return new TaskResult<Container>(TaskStatus.Success, gameInstance.Container);
    }

    /// <summary>
    /// 把题目的拓扑编排解析成有序的靶机节点列表（首项为主靶机）。
    ///
    /// 三种来源按优先级回退：
    /// 1. TopologyNodes 非空 —— 采用可视化拓扑，入口节点排在最前，其余按画布纵向位置排序；
    /// 2. ContainerImage 非空 —— 采用「主镜像 + ExtraContainerImages」的星形结构；
    /// 3. 兜底 —— 仅主镜像（历史行为）。
    ///
    /// 拓扑中的连线仅用于题面展示，不参与容器创建。
    /// </summary>
    private static List<ExtraContainerImage> BuildNodeList(GameChallenge challenge)
    {
        var nodes = challenge.TopologyNodes;

        if (nodes.Count > 0)
        {
            var ordered = nodes
                .OrderByDescending(n => n.IsEntry)
                .ThenBy(n => n.Y)
                .ThenBy(n => n.X)
                .Select(n => new ExtraContainerImage
                {
                    Name = n.Name,
                    Image = n.Image,
                    ExposePort = n.ExposePort,
                    EnableTrafficCapture = n.EnableTrafficCapture
                })
                .ToList();

            if (ordered.Count > 0)
                return ordered;
        }

        var fallback = new List<ExtraContainerImage>
        {
            new()
            {
                Name = null,
                Image = challenge.ContainerImage ?? string.Empty,
                ExposePort = challenge.ExposePort ?? 0,
                EnableTrafficCapture = challenge.EnableTrafficCapture
            }
        };

        fallback.AddRange(challenge.ExtraContainerImages);

        return fallback;
    }

    /// <summary>
    /// 按题目配置组装一个容器的创建参数
    /// </summary>
    private ContainerConfig BuildConfig(GameChallenge challenge, GameInstance gameInstance, Team team,
        UserInfo user, string? image, int? exposePort, bool trafficCapture, bool gameActive) =>
        new()
        {
            TeamId = team.Id.ToString(),
            UserId = user.Id,
            ChallengeId = gameInstance.ChallengeId,
            GameId = challenge.GameId,
            Flag = gameInstance.FlagContext?.Flag, // static challenge has no specific flag
            Image = image ?? string.Empty,
            CPUCount = challenge.CPUCount ?? 1,
            MemoryLimit = challenge.MemoryLimit ?? 64,
            StorageLimit = challenge.StorageLimit ?? 256,
            NetworkMode = challenge.NetworkMode ?? NetworkMode.Open,
            EnableTrafficCapture = trafficCapture && gameActive,
            ExposedPort = exposePort ??
                          throw new ArgumentException(
                              localizer[nameof(Resources.Program.InstanceRepository_InvalidPort)])
        };

    /// <summary>
    /// 创建单个容器，并写入靶机组信息（groupId / 名称 / 顺序）
    /// </summary>
    private async Task<Container?> CreateOneAsync(ContainerConfig config, Guid groupId, string? groupName,
        int groupOrder, CancellationToken token)
    {
        var container = await service.CreateContainerAsync(config, token);

        if (container is null)
            return null;

        // 按配置更新预期停止时间
        container.ExpectStopAt = container.StartedAt.AddMinutes(containerPolicy.Value.DefaultLifetime);
        container.GroupId = groupId;
        container.GroupName = string.IsNullOrWhiteSpace(groupName) ? null : groupName.Trim();
        container.GroupOrder = groupOrder;

        // 主容器通过 gameInstance.Container 建立外键关系后由外层统一保存；
        // 附加靶机不挂在任何被追踪的导航属性上，必须显式加入变更追踪，否则会被丢弃。
        if (groupOrder > 0)
            Context.Containers.Add(container);

        return container;
    }

    public async Task DestroyAllContainers(GameChallenge challenge, CancellationToken token = default)
    {
        // 主容器：沿用既有查询
        var containers = await Context.GameInstances
            .Include(i => i.Container)
            .Where(i => i.Challenge == challenge && i.ContainerId != null)
            .Select(i => i.Container)
            .ToArrayAsync(token);

        // 附加靶机：同一实例下共享 GroupId 的其余容器
        var groupIds = await Context.GameInstances
            .Where(i => i.Challenge == challenge && i.Container != null && i.Container.GroupId != null)
            .Select(i => i.Container!.GroupId!.Value)
            .Distinct()
            .ToArrayAsync(token);

        List<Container> extras = [];

        if (groupIds.Length > 0)
        {
            extras = await Context.Containers
                .Where(c => c.GroupId != null && groupIds.Contains(c.GroupId.Value) && c.Status != ContainerStatus.Destroyed)
                .ToListAsync(token);
        }

        // 去重后统一销毁，避免主容器被重复处理
        var targets = containers
            .Where(c => c is not null)
            .Select(c => c!)
            .Concat(extras)
            .GroupBy(c => c.Id)
            .Select(g => g.First());

        foreach (var container in targets)
            await containerRepository.DestroyContainer(container, token);
    }

    public async Task<CheatCheckInfo> CheckCheat(Submission submission, CancellationToken token = default)
    {
        CheatCheckInfo checkInfo = new();

        var instance = await Context.GameInstances
            .Include(i => i.Participation)
            .ThenInclude(i => i.Team)
            .Include(i => i.FlagContext)
            .Where(i => i.ChallengeId == submission.ChallengeId &&
                        i.ParticipationId != submission.ParticipationId &&
                        i.FlagContext != null && i.FlagContext.Flag == submission.Answer)
            .FirstOrDefaultAsync(token);

        if (instance is null)
            return checkInfo;

        var updateSub = await Context.Submissions.Where(s => s.Id == submission.Id).SingleAsync(token);

        var cheatInfo = await cheatInfoRepository.CreateCheatInfo(updateSub, instance, token);

        checkInfo = CheatCheckInfo.FromCheatInfo(cheatInfo);

        updateSub.Status = AnswerResult.CheatDetected;

        await SaveAsync(token);

        return checkInfo;
    }

    public async Task<string?> EnsureExternalFlag(int gameId, int challengeId, string teamToken,
        CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(teamToken))
            return null;

        var challenge = await Context.GameChallenges.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == challengeId && c.GameId == gameId, token);

        if (challenge is null || !challenge.ExternalDynamicFlag)
            return null;

        var part = await Context.Participations.AsNoTracking()
            .Include(p => p.Game)
            .SingleOrDefaultAsync(p => p.GameId == gameId && p.Token == teamToken, token);

        if (part is null)
            return null;

        var instance = await Context.GameInstances
            .Include(i => i.FlagContext)
            .SingleOrDefaultAsync(i => i.ParticipationId == part.Id && i.ChallengeId == challengeId, token);

        // 正常报名流程会为每道题预建实例；异常数据下兜底补一条
        var isNewInstance = instance is null;

        if (isNewInstance)
            instance = new GameInstance { ParticipationId = part.Id, ChallengeId = challengeId };

        if (instance!.FlagContext is not null)
            return instance.FlagContext.Flag;

        var flag = new FlagContext { Flag = challenge.GenerateDynamicFlag(part) };
        instance.FlagContext = flag;
        Context.FlagContexts.Add(flag);

        if (isNewInstance)
            Context.GameInstances.Add(instance);

        await Context.SaveChangesAsync(token);

        return flag.Flag;
    }

    public async Task<VerifyResult> VerifyAnswer(Submission submission, CancellationToken token = default)
    {
        await using var transaction = await Context.Database.BeginTransactionAsync(token);

        try
        {
            var instance = await Context.GameInstances.IgnoreAutoIncludes()
                .Include(i => i.FlagContext)
                .SingleOrDefaultAsync(i => i.ChallengeId == submission.ChallengeId &&
                                           i.ParticipationId == submission.ParticipationId, token);

            if (instance is null)
            {
                submission.Status = AnswerResult.NotFound;
                await transaction.RollbackAsync(token);
                return new(SubmissionType.Unaccepted, AnswerResult.NotFound);
            }

            var updateSub = await Context.Submissions
                .IgnoreAutoIncludes()
                .SingleAsync(s => s.Id == submission.Id, token);

            var challenge = await Context.GameChallenges
                .AsNoTracking()
                .Select(c => new { c.Id, c.Type, c.DisableBloodBonus, c.DeadlineUtc, c.ExternalDynamicFlag })
                .SingleAsync(c => c.Id == submission.ChallengeId, token);

            // 外部动态 flag：共用服务器题目没有容器，按队伍生成并落库后走动态分支校验
            if (instance.FlagContext is null && challenge.ExternalDynamicFlag)
            {
                var part = await Context.Participations.AsNoTracking()
                    .Include(p => p.Game)
                    .SingleAsync(p => p.Id == submission.ParticipationId, token);

                var chal = await Context.GameChallenges.AsNoTracking()
                    .SingleAsync(c => c.Id == submission.ChallengeId, token);

                var externalFlag = new FlagContext { Flag = chal.GenerateDynamicFlag(part) };
                instance.FlagContext = externalFlag;
                Context.FlagContexts.Add(externalFlag);
                await Context.SaveChangesAsync(token);
            }

            if (instance.FlagContext is null && challenge.Type.IsStatic())
            {
                updateSub.Status = await Context.FlagContexts.AsNoTracking()
                    .AnyAsync(
                        f => f.ChallengeId == submission.ChallengeId && f.Flag == submission.Answer,
                        token)
                    ? AnswerResult.Accepted
                    : AnswerResult.WrongAnswer;
            }
            else
            {
                updateSub.Status = instance.FlagContext?.Flag == submission.Answer
                    ? AnswerResult.Accepted
                    : AnswerResult.WrongAnswer;
            }

            if (updateSub.Status != AnswerResult.Accepted)
            {
                await SaveAsync(token);
                await transaction.CommitAsync(token);
                return new(SubmissionType.Unaccepted, updateSub.Status);
            }

            // Acquire a PostgresSQL advisory lock to prevent race conditions:
            // This lock ensures that only one concurrent submission for the same participation/challenge pair
            // can proceed past this point, preventing duplicate FirstSolve entries if multiple submissions
            // are processed at the same time. Without this, two submissions could both pass the check and
            // insert duplicate records.
            await Context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0}, {1})",
                [updateSub.ParticipationId, updateSub.ChallengeId],
                cancellationToken: token);

            var alreadySolved = await Context.FirstSolves
                .AnyAsync(fs => fs.ParticipationId == submission.ParticipationId &&
                                fs.ChallengeId == submission.ChallengeId, token);

            if (alreadySolved)
            {
                await SaveAsync(token);
                await transaction.CommitAsync(token);
                return new(SubmissionType.Normal, updateSub.Status);
            }

            var participation = await Context.Participations
                .IgnoreAutoIncludes().AsNoTracking()
                .Include(p => p.Division)
                .ThenInclude(d => d!.ChallengeConfigs)
                .SingleAsync(p => p.Id == submission.ParticipationId, token);

            var time = await Context.Games.IgnoreAutoIncludes().AsNoTracking()
                .Where(g => g.Id == participation.GameId)
                .Select(g => new { g.StartTimeUtc, g.EndTimeUtc })
                .SingleAsync(token);

            // Check if submission is within game time window
            var withinGameWindow = updateSub.SubmitTimeUtc >= time.StartTimeUtc &&
                                   updateSub.SubmitTimeUtc < time.EndTimeUtc;

            // Check if submission is within challenge deadline (if deadline is set)
            var withinDeadline = !challenge.DeadlineUtc.HasValue ||
                                 updateSub.SubmitTimeUtc <= challenge.DeadlineUtc.Value;

            // Blood bonus is only awarded if submission is within both game window and deadline
            var hasBloodPermission = withinGameWindow && withinDeadline && !challenge.DisableBloodBonus &&
                                     HasPermission(participation.Division, GamePermission.GetBlood,
                                         submission.ChallengeId);

            var submissionType = SubmissionType.Normal;
            if (hasBloodPermission)
            {
                var bloodEligibleCount = await CountBloodEligibleSolves(submission.ChallengeId, time.StartTimeUtc,
                    time.EndTimeUtc, token);

                submissionType = bloodEligibleCount switch
                {
                    0 => SubmissionType.FirstBlood,
                    1 => SubmissionType.SecondBlood,
                    2 => SubmissionType.ThirdBlood,
                    _ => SubmissionType.Normal
                };
            }

            Context.FirstSolves.Add(new FirstSolve
            {
                ParticipationId = submission.ParticipationId,
                ChallengeId = submission.ChallengeId,
                SubmissionId = submission.Id
            });

            await SaveAsync(token);
            await transaction.CommitAsync(token);

            return new(submissionType, updateSub.Status);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred during answer verification for submission {SubmissionId}.",
                submission.Id);
            await transaction.RollbackAsync(token);
            throw;
        }
    }

    private Task<int> CountBloodEligibleSolves(int challengeId, DateTimeOffset start, DateTimeOffset end,
        CancellationToken token)
    {
        // First, get blood-eligible participation IDs for the challenge
        var eligibleParticipationIds =
            from participation in Context.Participations.AsNoTracking()
            join config in Context.Set<DivisionChallengeConfig>().AsNoTracking()
                    .Where(c => c.ChallengeId == challengeId)
                on participation.DivisionId equals config.DivisionId into configJoin
            from cfg in configJoin.DefaultIfEmpty()
            join division in Context.Divisions.AsNoTracking()
                on participation.DivisionId equals division.Id into divisionJoin
            from div in divisionJoin.DefaultIfEmpty()
            where participation.Status == ParticipationStatus.Accepted
                  && (cfg != null
                      ? cfg.Permissions.HasFlag(GamePermission.GetBlood)
                      : div == null || div.DefaultPermissions.HasFlag(GamePermission.GetBlood))
            select participation.Id;

        // Now, count FirstSolves for the challenge with eligible participations and time window
        return (
            from fs in Context.FirstSolves.AsNoTracking()
            join submission in Context.Submissions.AsNoTracking() on fs.SubmissionId equals submission.Id
            where fs.ChallengeId == challengeId
                  && eligibleParticipationIds.Contains(fs.ParticipationId)
                  && submission.SubmitTimeUtc >= start
                  && submission.SubmitTimeUtc < end
            orderby submission.SubmitTimeUtc
            select fs.ParticipationId
        ).Take(4).CountAsync(token);
    }

    private static bool HasPermission(Division? division, GamePermission permission, int challengeId)
    {
        if (division is null)
            return true;

        var specific = division.ChallengeConfigs.FirstOrDefault(c => c.ChallengeId == challengeId);
        var permissions = specific?.Permissions ?? division.DefaultPermissions;
        return permissions.HasFlag(permission);
    }
}
