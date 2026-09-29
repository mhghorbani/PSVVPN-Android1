namespace NPViera.Diagnostics;

public sealed record DiagnosticEvent(DateTimeOffset Timestamp, string Stage, bool Success, string? Code = null);

public sealed class DiagnosticTimeline
{
    readonly object sync = new();
    readonly List<DiagnosticEvent> events = new();

    public void Add(string stage, bool success = true, string? code = null)
    {
        lock (sync) events.Add(new(DateTimeOffset.UtcNow, stage, success, code));
    }

    public IReadOnlyList<DiagnosticEvent> Snapshot()
    {
        lock (sync) return events.ToArray();
    }
}
