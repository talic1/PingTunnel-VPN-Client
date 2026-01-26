using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Serilog.Events;

namespace PingTunnelVPN.App.Models;

/// <summary>
/// Represents a structured log entry for display in the UI.
/// </summary>
public class LogEntry : INotifyPropertyChanged
{
    private static readonly Regex LogPattern = new(
        @"^\[?(\d{4}-\d{2}-\d{2}\s+)?(\d{2}:\d{2}:\d{2})(?:\.\d+)?\]?\s*\[?(INF|WRN|ERR|DBG|VRB|FTL|INFO|WARN|WARNING|ERROR|DEBUG|VERBOSE|FATAL|SUCCESS)\]?\s*(.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public DateTime Timestamp { get; set; }
    public string TimeString { get; set; } = string.Empty;
    public string Level { get; set; } = "INFO";
    public string Message { get; set; } = string.Empty;
    public string RawLine { get; set; } = string.Empty;

    /// <summary>
    /// Parses a raw log line into a structured LogEntry.
    /// </summary>
    public static LogEntry Parse(string line)
    {
        var entry = new LogEntry
        {
            RawLine = line,
            Timestamp = DateTime.Now
        };

        if (string.IsNullOrWhiteSpace(line))
        {
            entry.Message = line;
            return entry;
        }

        var match = LogPattern.Match(line);
        if (match.Success)
        {
            entry.TimeString = match.Groups[2].Value;
            entry.Level = NormalizeLevel(match.Groups[3].Value);
            entry.Message = match.Groups[4].Value.Trim();
        }
        else
        {
            // Try to infer level from content
            entry.TimeString = DateTime.Now.ToString("HH:mm:ss");
            entry.Level = InferLevel(line);
            entry.Message = line;
        }

        return entry;
    }

    /// <summary>
    /// Creates a structured LogEntry from a Serilog LogEvent.
    /// </summary>
    public static LogEntry FromLogEvent(LogEvent logEvent)
    {
        var timestamp = logEvent.Timestamp.LocalDateTime;
        var level = NormalizeSerilogLevel(logEvent.Level);
        var message = logEvent.RenderMessage();

        if (logEvent.Exception != null)
        {
            message = $"{message} | {logEvent.Exception.Message}";
        }

        var rawLine = $"{logEvent.Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{ShortLevel(logEvent.Level)}] {message}";
        if (logEvent.Exception != null)
        {
            rawLine = $"{rawLine}{Environment.NewLine}{logEvent.Exception}";
        }

        return new LogEntry
        {
            RawLine = rawLine,
            Timestamp = timestamp,
            TimeString = timestamp.ToString("HH:mm:ss"),
            Level = level,
            Message = message
        };
    }

    private static string NormalizeLevel(string level)
    {
        return level.ToUpperInvariant() switch
        {
            "INF" or "INFO" => "INFO",
            "WRN" or "WARN" or "WARNING" => "WARNING",
            "ERR" or "ERROR" => "ERROR",
            "FTL" or "FATAL" => "ERROR",
            "DBG" or "DEBUG" => "DEBUG",
            "VRB" or "VERBOSE" => "DEBUG",
            "SUCCESS" => "SUCCESS",
            _ => "INFO"
        };
    }

    private static string NormalizeSerilogLevel(LogEventLevel level)
    {
        return level switch
        {
            LogEventLevel.Verbose => "DEBUG",
            LogEventLevel.Debug => "DEBUG",
            LogEventLevel.Information => "INFO",
            LogEventLevel.Warning => "WARNING",
            LogEventLevel.Error => "ERROR",
            LogEventLevel.Fatal => "ERROR",
            _ => "INFO"
        };
    }

    private static string ShortLevel(LogEventLevel level)
    {
        return level switch
        {
            LogEventLevel.Verbose => "VRB",
            LogEventLevel.Debug => "DBG",
            LogEventLevel.Information => "INF",
            LogEventLevel.Warning => "WRN",
            LogEventLevel.Error => "ERR",
            LogEventLevel.Fatal => "FTL",
            _ => "INF"
        };
    }

    private static string InferLevel(string line)
    {
        var lower = line.ToLowerInvariant();
        
        if (lower.Contains("error") || lower.Contains("failed") || lower.Contains("exception"))
            return "ERROR";
        if (lower.Contains("warning") || lower.Contains("warn"))
            return "WARNING";
        if (lower.Contains("success") || lower.Contains("connected") || lower.Contains("completed"))
            return "SUCCESS";
        
        return "INFO";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
