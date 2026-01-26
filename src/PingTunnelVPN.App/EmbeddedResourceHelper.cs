using System.IO;
using System.Linq;
using System.Reflection;
using PingTunnelVPN.Core;
using Serilog;

namespace PingTunnelVPN.App;

internal static class EmbeddedResourceHelper
{
    private static readonly string[] ResourceFiles =
    {
        "pingtunnel.exe",
        "tun2socks.exe",
        "wintun.dll",
        "icon.ico",
        "icon-off.ico"
    };

    public static void EnsureResourcesPresent()
    {
        try
        {
            var resourcesDir = ProcessManager.ResourcesDirectory;
            Directory.CreateDirectory(resourcesDir);

            foreach (var fileName in ResourceFiles)
            {
                var targetPath = Path.Combine(resourcesDir, fileName);
                if (File.Exists(targetPath))
                    continue;

                if (TryExtractEmbeddedResource(fileName, targetPath))
                {
                    Log.Information("Extracted embedded resource: {File}", fileName);
                }
                else
                {
                    Log.Debug("Embedded resource not found for: {File}", fileName);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to ensure embedded resources");
        }
    }

    private static bool TryExtractEmbeddedResource(string fileName, string targetPath)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith($".Resources.{fileName}", StringComparison.OrdinalIgnoreCase));

        if (resourceName == null)
            return false;

        using var resourceStream = assembly.GetManifestResourceStream(resourceName);
        if (resourceStream == null)
            return false;

        using var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
        resourceStream.CopyTo(fileStream);
        return true;
    }
}
