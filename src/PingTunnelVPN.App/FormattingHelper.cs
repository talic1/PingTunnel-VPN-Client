namespace PingTunnelVPN.App;

/// <summary>
/// Shared formatting helpers for bytes and speed display.
/// </summary>
public static class FormattingHelper
{
    /// <summary>Format bytes as "X.XX KB", "X.XX MB", etc. (1024-based).</summary>
    public static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }

    /// <summary>Format bytes per second as "X.XX B/s", "X.XX KB/s", etc. (1024-based).</summary>
    public static string FormatSpeed(double bytesPerSecond)
    {
        string[] sizes = { "B/s", "KB/s", "MB/s", "GB/s" };
        int order = 0;
        double size = bytesPerSecond;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }

    /// <summary>Compact speed format for tight UI: "1.2K/s", "56M/s".</summary>
    public static string FormatSpeedCompact(double bytesPerSecond)
    {
        string[] sizes = { "B/s", "K/s", "M/s", "G/s" };
        int order = 0;
        double size = bytesPerSecond;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        var format = size >= 100 ? "0" : size >= 10 ? "0.#" : "0.##";
        return $"{size.ToString(format)}{sizes[order]}";
    }
}
