using System.IO;
using PingTunnelVPN.Core;

namespace PingTunnelVPN.App;

/// <summary>
/// Paths to application icons. icon.ico = app and tray when connected;
/// icon-off.ico = tray when disconnected.
/// </summary>
public static class AppIcons
{
    private static string ResourcesDir => Path.Combine(ProcessManager.AppDirectory, "Resources");

    /// <summary>App icon and tray icon when connected.</summary>
    public static string IconPath => Path.Combine(ResourcesDir, "icon.ico");

    /// <summary>Tray icon when disconnected.</summary>
    public static string IconOffPath => Path.Combine(ResourcesDir, "icon-off.ico");
}
