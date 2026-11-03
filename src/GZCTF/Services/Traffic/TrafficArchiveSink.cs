using Serilog.Core;
using Serilog.Events;

namespace GZCTF.Services.Traffic;

/// <summary>Copies the actual system log event, preserving its timestamp, content and actor.</summary>
public sealed class TrafficArchiveSink(TrafficArchiveStore store) : ILogEventSink
{
    public void Emit(LogEvent logEvent)
    {
        if (!logEvent.Properties.TryGetValue("TrafficSessions", out var sessions) || sessions is not SequenceValue sequence)
            return;
        string? Property(string key) => logEvent.Properties.GetValueOrDefault(key) is ScalarValue scalar
            ? scalar.Value?.ToString() : null;
        foreach (var value in sequence.Elements.OfType<ScalarValue>())
        {
            if (!Guid.TryParse(value.Value?.ToString(), out var id) || store.Find(id) is not { } session)
                continue;
            store.Queue(id, $"logs/{logEvent.Timestamp.UtcTicks}-{Guid.NewGuid():N}.json", new
            {
                time = logEvent.Timestamp.ToUniversalTime(),
                kind = Property("TrafficKind"),
                level = logEvent.Level.ToString(),
                content = logEvent.RenderMessageWithExceptions(),
                user = Property("TrafficKind") == "container_start" ? session.User :
                    Property("TrafficRequestUser") ?? Property("UserName") ?? "System",
                ip = Property("TrafficKind") == "container_start" ? session.IP :
                    Property("TrafficRequestIP") ?? Property("IP"),
                startedBy = session.User, startedByIP = session.IP,
                status = Property("Status"),
                logger = Property("SourceContext"),
                captureId = id
            });
        }
    }
}
