using PingTunnelVPN.App.Models;
using Serilog.Core;
using Serilog.Events;

namespace PingTunnelVPN.App.Logging;

/// <summary>
/// Serilog sink that forwards log events to the UI log hub.
/// </summary>
public sealed class UiLogSink : ILogEventSink
{
    public void Emit(LogEvent logEvent)
    {
        try
        {
            var entry = LogEntry.FromLogEvent(logEvent);
            LogEventHub.Publish(entry);
        }
        catch
        {
            // Never let logging failures crash the app.
        }
    }
}
