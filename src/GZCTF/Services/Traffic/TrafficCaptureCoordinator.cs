using System.Text.Json;
using GZCTF.Models;
using GZCTF.Models.Internal;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Services.Traffic;

public sealed class TrafficCaptureCoordinator(AppDbContext context, TrafficArchiveStore store,
    IHttpContextAccessor http)
{
    const string ControlKey = "TrafficCapture:Control";

    public async Task<TrafficCaptureControl> GetControl(CancellationToken token = default)
    {
        var value = await context.Configs.AsNoTracking().Where(c => c.ConfigKey == ControlKey)
            .Select(c => c.Value).SingleOrDefaultAsync(token);
        return value is null ? new(false, "initial") : JsonSerializer.Deserialize<TrafficCaptureControl>(value)!;
    }

    public async Task<TrafficCaptureControl> SetControl(bool enabled, CancellationToken token)
    {
        // Serialize concurrent settings writes across replicas. Keep the epoch on idempotent PUTs.
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        await context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(74672391)", token);
        var current = await GetControl(token);
        if (current.Enabled == enabled)
            return current;
        var control = new TrafficCaptureControl(enabled, Guid.NewGuid().ToString("N"));
        var config = await context.Configs.SingleOrDefaultAsync(c => c.ConfigKey == ControlKey, token);
        if (config is null)
            context.Configs.Add(new Models.Data.Config(ControlKey, JsonSerializer.Serialize(control)));
        else
            config.Value = JsonSerializer.Serialize(control);
        await context.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        // Persist per-session stop markers too: a rapid off/on must still close the old captures.
        if (!enabled)
        {
            store.Refresh();
            foreach (var session in store.Sessions.Where(s => s.Epoch == current.Epoch))
                store.Stop(session.Id);
        }
        return control;
    }

    public async Task<TrafficCaptureSession?> Begin(ContainerConfig config, string pod, CancellationToken token)
    {
        var control = await GetControl(token);
        if (!control.Enabled)
            return null;
        var user = await context.Users.AsNoTracking().Where(u => u.Id == config.UserId)
            .Select(u => u.UserName).SingleOrDefaultAsync(token) ?? config.UserId.ToString();
        var title = config.GameId.HasValue
            ? await context.GameChallenges.AsNoTracking().Where(c => c.Id == config.ChallengeId)
                .Select(c => c.Title).SingleOrDefaultAsync(token)
            : await context.ExerciseChallenges.AsNoTracking().Where(c => c.Id == config.ChallengeId)
                .Select(c => c.Title).SingleOrDefaultAsync(token);
        var team = int.TryParse(config.TeamId, out var teamId)
            ? await context.Teams.AsNoTracking().Where(t => t.Id == teamId).Select(t => t.Name).SingleOrDefaultAsync(token)
            : null;
        var session = new TrafficCaptureSession(Guid.NewGuid(), control.Epoch, pod, config.GameId,
            config.ChallengeId, title ?? config.ChallengeId.ToString(), config.TeamId, team ?? config.TeamId,
            config.UserId, user, http.HttpContext?.Connection.RemoteIpAddress?.ToString(), DateTimeOffset.UtcNow);
        store.Register(session);
        return session;
    }
}
