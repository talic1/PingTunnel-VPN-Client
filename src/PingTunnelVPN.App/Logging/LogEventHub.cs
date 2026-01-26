using System.Collections.Concurrent;
using PingTunnelVPN.App.Models;

namespace PingTunnelVPN.App.Logging;

/// <summary>
/// In-process log broadcaster for UI consumers.
/// </summary>
public static class LogEventHub
{
    private const int MaxBacklog = 500;
    private static readonly ConcurrentQueue<LogEntry> Backlog = new();

    public static event Action<LogEntry>? LogEntryReceived;

    public static void Publish(LogEntry entry)
    {
        Backlog.Enqueue(entry);
        while (Backlog.Count > MaxBacklog && Backlog.TryDequeue(out _))
        {
        }

        LogEntryReceived?.Invoke(entry);
    }

    public static IReadOnlyList<LogEntry> SnapshotBacklog()
    {
        return Backlog.ToArray();
    }
}
