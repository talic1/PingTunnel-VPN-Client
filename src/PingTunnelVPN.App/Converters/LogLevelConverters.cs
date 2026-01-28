using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Application = System.Windows.Application;

namespace PingTunnelVPN.App.Converters;

/// <summary>
/// Converts a log level string to background brush for badge.
/// </summary>
public class LogLevelToBadgeBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var level = value?.ToString()?.ToUpperInvariant() ?? "INFO";

        return level switch
        {
            "SUCCESS" => Application.Current.FindResource("Brush.Success.Background"),
            "ERROR" or "ERR" or "FATAL" => Application.Current.FindResource("Brush.Danger.Background"),
            "WARNING" or "WARN" => Application.Current.FindResource("Brush.Warning.Background"),
            _ => Application.Current.FindResource("Brush.Info.Background")
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a log level string to foreground brush for badge text.
/// </summary>
public class LogLevelToBadgeForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var level = value?.ToString()?.ToUpperInvariant() ?? "INFO";

        return level switch
        {
            "SUCCESS" => Application.Current.FindResource("Brush.Success"),
            "ERROR" or "ERR" or "FATAL" => Application.Current.FindResource("Brush.Danger"),
            "WARNING" or "WARN" => Application.Current.FindResource("Brush.Warning"),
            _ => Application.Current.FindResource("Brush.Text.Secondary")
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Parses a log line and extracts the level.
/// Expected format: [timestamp] [LEVEL] message
/// </summary>
public class LogLineToLevelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var line = value?.ToString() ?? "";
        
        // Try to extract level from log line
        // Common formats: 
        // [2024-01-15 10:30:45] [INF] message
        // [10:30:45] [INFO] message
        // 2024-01-15 10:30:45 INF message
        
        if (line.Contains("[INF]") || line.Contains("[INFO]") || line.Contains(" INF "))
            return "INFO";
        if (line.Contains("[WRN]") || line.Contains("[WARN]") || line.Contains("[WARNING]") || line.Contains(" WRN "))
            return "WARNING";
        if (line.Contains("[ERR]") || line.Contains("[ERROR]") || line.Contains("[FATAL]") || line.Contains(" ERR "))
            return "ERROR";
        if (line.Contains("success") || line.Contains("Success") || line.Contains("SUCCESS") || 
            line.Contains("connected") || line.Contains("Connected"))
            return "SUCCESS";

        return "INFO";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Extracts timestamp from a log line.
/// </summary>
public class LogLineToTimestampConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var line = value?.ToString() ?? "";
        
        // Try to extract timestamp in brackets
        var start = line.IndexOf('[');
        var end = line.IndexOf(']');
        
        if (start >= 0 && end > start)
        {
            return line.Substring(start, end - start + 1);
        }
        
        return "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Extracts message from a log line (after level badge).
/// </summary>
public class LogLineToMessageConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var line = value?.ToString() ?? "";
        
        // Find the last ] and return everything after
        var lastBracket = line.LastIndexOf(']');
        if (lastBracket >= 0 && lastBracket < line.Length - 1)
        {
            return line.Substring(lastBracket + 1).Trim();
        }
        
        return line;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
