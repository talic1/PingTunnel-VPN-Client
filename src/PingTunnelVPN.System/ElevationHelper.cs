using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using Serilog;

namespace PingTunnelVPN.Platform;

/// <summary>
/// Helper methods for checking and requesting elevated (administrator) privileges.
/// </summary>
public static class ElevationHelper
{
    /// <summary>
    /// Checks if the current process is running with administrator privileges.
    /// </summary>
    public static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to check elevation status");
            return false;
        }
    }

    /// <summary>
    /// Restarts the current application with administrator privileges.
    /// </summary>
    /// <param name="exitCurrentProcess">If true, exits the current process after starting elevated.</param>
    /// <returns>True if the elevated process was started successfully.</returns>
    public static bool RestartElevated(bool exitCurrentProcess = true)
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
            {
                Log.Error("Could not determine executable path");
                return false;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                Verb = "runas", // Request elevation
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory
            };

            // Pass command line arguments if any
            var args = Environment.GetCommandLineArgs();
            if (args.Length > 1)
            {
                startInfo.Arguments = string.Join(" ", args.Skip(1).Select(a => 
                    a.Contains(' ') ? $"\"{a}\"" : a));
            }

            var process = Process.Start(startInfo);
            if (process != null)
            {
                Log.Information("Started elevated process with PID {Pid}", process.Id);
                
                if (exitCurrentProcess)
                {
                    Environment.Exit(0);
                }
                
                return true;
            }

            return false;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) // Cancelled by user
        {
            Log.Information("User cancelled elevation request");
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to restart with elevation");
            return false;
        }
    }

    /// <summary>
    /// Ensures the application is running elevated, prompting for elevation if needed.
    /// </summary>
    /// <returns>True if running elevated or elevation was granted.</returns>
    public static bool EnsureElevated()
    {
        if (IsElevated())
        {
            return true;
        }

        Log.Information("Application requires elevation, requesting...");
        return RestartElevated();
    }

    /// <summary>
    /// Gets the Windows version information.
    /// </summary>
    public static string GetWindowsVersion()
    {
        try
        {
            var version = Environment.OSVersion.Version;
            return $"Windows {version.Major}.{version.Minor}.{version.Build}";
        }
        catch
        {
            return "Windows (unknown version)";
        }
    }

    /// <summary>
    /// Checks if the current Windows version is supported (Windows 10+).
    /// </summary>
    public static bool IsWindowsVersionSupported()
    {
        try
        {
            var version = Environment.OSVersion.Version;
            // Windows 10 is version 10.0
            return version.Major >= 10;
        }
        catch
        {
            return false;
        }
    }
}
