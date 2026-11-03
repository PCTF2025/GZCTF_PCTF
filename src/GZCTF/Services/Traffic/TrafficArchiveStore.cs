using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using GZCTF.Models.Internal;
using Serilog.Context;

namespace GZCTF.Services.Traffic;

public record TrafficCaptureControl(bool Enabled, string Epoch);
public record TrafficCaptureSession(Guid Id, string Epoch, string Pod, int? GameId, int ChallengeId,
    string Title, string TeamId, string Team, Guid UserId, string User, string? IP, DateTimeOffset StartedAt);
public record TrafficCaptureReport(string Node, string State, long Bytes, string[] Files, string? Error = null,
    DateTimeOffset? UpdatedAt = null);
public record TrafficSubmissionContext(Guid[] Sessions, string User, string? IP);

/// <summary>
/// Durable archive outbox. Manifests and immutable log objects survive application restarts.
/// Mount Root on persistent shared storage when running more than one platform replica.
/// </summary>
public sealed class TrafficArchiveStore
{
    public string Root { get; }
    public Channel<bool> Notifications { get; } = Channel.CreateBounded<bool>(1);
    readonly ConcurrentDictionary<Guid, TrafficCaptureSession> _sessions = new();
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public TrafficArchiveStore(IConfiguration configuration)
    {
        Root = Path.GetFullPath(configuration["TrafficCapture:ArchiveRoot"] ??
            Path.Combine(PathHelper.Base, "traffic-archive"));
        Directory.CreateDirectory(Root);
        Refresh();
    }

    public void Refresh()
    {
        foreach (var file in Directory.EnumerateFiles(Root, "session.json", SearchOption.AllDirectories))
        {
            var session = JsonSerializer.Deserialize<TrafficCaptureSession>(File.ReadAllText(file), Json);
            if (session is not null)
                _sessions.TryAdd(session.Id, session);
        }
    }

    public TrafficCaptureSession[] Sessions => _sessions.Values.OrderByDescending(s => s.StartedAt).ToArray();
    public TrafficCaptureSession? Find(Guid id) => _sessions.GetValueOrDefault(id);
    public TrafficCaptureSession? FindPod(string pod)
    {
        Refresh();
        return _sessions.Values.FirstOrDefault(s => s.Pod == pod);
    }
    public string SessionPath(Guid id) => Path.Combine(Root, id.ToString("N"));
    public bool IsStopped(Guid id) => File.Exists(Path.Combine(SessionPath(id), "stopped"));

    public void Register(TrafficCaptureSession session)
    {
        WriteJson(Path.Combine(SessionPath(session.Id), "session.json"), session);
        _sessions[session.Id] = session;
        Queue(session.Id, "metadata.json", session);
    }

    public void Stop(Guid id) => WriteJson(Path.Combine(SessionPath(id), "stopped"), DateTimeOffset.UtcNow);

    public void Report(Guid id, TrafficCaptureReport report) =>
        WriteJson(Path.Combine(SessionPath(id), "status.json"), report with { UpdatedAt = DateTimeOffset.UtcNow });

    public TrafficCaptureReport? GetReport(Guid id)
    {
        var path = Path.Combine(SessionPath(id), "status.json");
        return File.Exists(path) ? JsonSerializer.Deserialize<TrafficCaptureReport>(File.ReadAllText(path), Json) : null;
    }

    public void Queue(Guid sessionId, string name, object value)
    {
        WriteJson(Path.Combine(SessionPath(sessionId), "outbox", name + ".ready"), value);
        Notifications.Writer.TryWrite(true);
    }

    public IDisposable LogScope(IEnumerable<Guid> ids, string kind) =>
        new CaptureLogScope(ids.ToArray(), kind);

    public IDisposable ContainerLogScope(IEnumerable<Models.Data.Container> containers, string kind) =>
        LogScope(containers.Select(c => FindPod(c.ContainerId)?.Id).OfType<Guid>(), kind);

    public IDisposable SubmissionLogScope(Submission submission, string kind = "flag_result")
    {
        var path = SubmissionPath(submission.Id);
        var request = File.Exists(path)
            ? JsonSerializer.Deserialize<TrafficSubmissionContext>(File.ReadAllText(path), Json)
            : null;
        return new CaptureLogScope(request?.Sessions ?? [], kind, request);
    }

    public void RememberSubmission(Submission submission, string? ip, string[]? currentPods = null)
    {
        // Bind to the latest launch (including all nodes of that launch). Never attach a later
        // submission to an older launch merely because that older launch was captured.
        Refresh();
        var related = Sessions.Where(s => s.GameId == submission.GameId &&
            s.ChallengeId == submission.ChallengeId && s.TeamId == submission.TeamId.ToString() &&
            s.StartedAt <= submission.SubmitTimeUtc).ToArray();
        var active = currentPods is { Length: > 0 }
            ? related.Where(s => currentPods.Contains(s.Pod)).ToArray()
            : related.Where(s => !IsStopped(s.Id)).ToArray();
        if (currentPods is { Length: > 0 } && active.Length == 0)
            return;
        var ids = active.Length > 0 ? active.Select(s => s.Id).ToArray() : related.Take(1).Select(s => s.Id).ToArray();
        if (ids.Length == 0)
            return;
        WriteJson(SubmissionPath(submission.Id), new TrafficSubmissionContext(ids, submission.UserName, ip));
    }

    string SubmissionPath(int id) => Path.Combine(Root, "submissions", $"{id}.json");

    internal static void WriteJson(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + $".{Guid.NewGuid():N}.tmp";
        using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, value, Json);
            stream.Flush(true);
        }
        File.Move(temp, path, true);
    }

    sealed class CaptureLogScope : IDisposable
    {
        readonly IDisposable[] _scopes;
        public CaptureLogScope(Guid[] ids, string kind, TrafficSubmissionContext? request = null)
        {
            _scopes = [LogContext.PushProperty("TrafficSessions", ids.Select(id => id.ToString("N")).ToArray(), true),
                LogContext.PushProperty("TrafficKind", kind),
                LogContext.PushProperty("TrafficRequestUser", request?.User),
                LogContext.PushProperty("TrafficRequestIP", request?.IP)];
        }
        public void Dispose()
        {
            foreach (var scope in _scopes.Reverse())
                scope.Dispose();
        }
    }
}
