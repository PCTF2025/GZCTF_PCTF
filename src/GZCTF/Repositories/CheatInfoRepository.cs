using GZCTF.Repositories.Interface;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Repositories;

public class CheatInfoRepository(
    AppDbContext context,
    IParticipationRepository participationRepository) : RepositoryBase(context), ICheatInfoRepository
{
    public async Task<CheatInfo> CreateCheatInfo(Submission submission, GameInstance source,
        CancellationToken token = default)
    {
        var submit =
            await participationRepository.GetParticipation(submission.Team!, submission.Game!, token);

        if (submit is null)
            throw new NullReferenceException(nameof(submit));

        CheatInfo info = new()
        {
            GameId = submission.GameId,
            Submission = submission,
            SubmitTeam = submit,
            SourceTeam = source.Participation
        };

        await Context.AddAsync(info, token);

        return info;
    }

    public Task<CheatInfo[]> GetCheatInfoByGameId(int gameId, CancellationToken token = default) =>
        Context.CheatInfo.IgnoreAutoIncludes().Where(i => i.GameId == gameId)
            .Include(i => i.SourceTeam).ThenInclude(t => t.Team)
            .Include(i => i.SubmitTeam).ThenInclude(t => t.Team)
            .Include(i => i.SubmitTeam).ThenInclude(t => t.Division)
            .Include(i => i.SourceTeam).ThenInclude(t => t.Division)
            .Include(i => i.Submission).ThenInclude(s => s.User)
            .Include(i => i.Submission).ThenInclude(s => s.GameChallenge)
            .AsSplitQuery().ToArrayAsync(token);

    public async Task<CheatInfo?> CreateMassSubmissionAlert(Submission submission, int windowSeconds,
        int threshold, CancellationToken token = default)
    {
        if (submission.Team is null || submission.Game is null || windowSeconds <= 0 || threshold <= 0)
            return null;

        var end = submission.SubmitTimeUtc;
        var start = end.AddSeconds(-windowSeconds);

        var count = await Context.Submissions.AsNoTracking()
            .CountAsync(s => s.GameId == submission.GameId &&
                             s.TeamId == submission.TeamId &&
                             s.SubmitTimeUtc >= start && s.SubmitTimeUtc <= end &&
                             s.Status != AnswerResult.NotFound, token);

        if (count < threshold)
            return null;

        // 同一窗口内只记一次，避免刷屏
        var exists = await Context.CheatInfo.AsNoTracking()
            .Include(i => i.Submission)
            .AnyAsync(i => i.GameId == submission.GameId &&
                           i.SubmitTeamId == submission.TeamId &&
                           i.Behavior == CheatBehavior.MassSubmission &&
                           i.Submission.SubmitTimeUtc >= start && i.Submission.SubmitTimeUtc <= end, token);

        if (exists)
            return null;

        var submit = await participationRepository.GetParticipation(submission.Team, submission.Game, token);

        if (submit is null)
            return null;

        CheatInfo info = new()
        {
            GameId = submission.GameId,
            Submission = submission,
            SubmitTeam = submit,
            // 该行为没有「被抄袭的队伍」，指向自身占位，前端按 Behavior 区分展示
            SourceTeam = submit,
            Behavior = CheatBehavior.MassSubmission
        };

        await Context.AddAsync(info, token);
        await SaveAsync(token);

        return info;
    }
}
