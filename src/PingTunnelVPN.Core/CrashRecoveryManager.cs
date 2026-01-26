using System.Text.Json;
using PingTunnelVPN.Platform;
using Serilog;

namespace PingTunnelVPN.Core;

/// <summary>
/// Manages crash recovery by tracking system state changes and restoring on unclean shutdown.
/// </summary>
public class CrashRecoveryManager
{
    private static readonly string StateFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PingTunnelVPN",
        "state.json");

    /// <summary>
    /// State information saved before making system changes.
    /// </summary>
    public class RecoveryState
    {
        public bool IsConnected { get; set; }
        public DateTime Timestamp { get; set; }
        public List<RouteEntry> AddedRoutes { get; set; } = new();
        public Dictionary<string, string[]> OriginalDnsSettings { get; set; } = new();
        public string? OriginalDefaultGateway { get; set; }
        public int? OriginalDefaultInterfaceIndex { get; set; }
    }

    public class RouteEntry
    {
        public string Destination { get; set; } = string.Empty;
        public int PrefixLength { get; set; }
        public string Gateway { get; set; } = string.Empty;
        public int InterfaceIndex { get; set; }
        public int Metric { get; set; }
    }

    /// <summary>
    /// Checks if recovery is needed (unclean shutdown while connected).
    /// </summary>
    public bool NeedsRecovery()
    {
        if (!File.Exists(StateFilePath))
            return false;

        try
        {
            var json = File.ReadAllText(StateFilePath);
            var state = JsonSerializer.Deserialize<RecoveryState>(json);
            return state?.IsConnected == true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to read recovery state file");
            return false;
        }
    }

    /// <summary>
    /// Performs recovery by restoring system settings to pre-connection state.
    /// </summary>
    public void PerformRecovery()
    {
        RecoveryState? state = null;

        try
        {
            var json = File.ReadAllText(StateFilePath);
            state = JsonSerializer.Deserialize<RecoveryState>(json);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to deserialize recovery state");
            ClearState();
            return;
        }

        if (state == null)
        {
            ClearState();
            return;
        }

        Log.Information("Starting crash recovery from state saved at {Timestamp}", state.Timestamp);

        // Restore routes (use shell to avoid P/Invoke TypeLoadException)
        try
        {
            var routeManager = new RouteManager();
            foreach (var route in state.AddedRoutes)
            {
                try
                {
                    routeManager.DeleteRouteViaShell(route.Destination, route.PrefixLength, route.Gateway);
                    Log.Information("Removed route: {Destination}/{Prefix}", route.Destination, route.PrefixLength);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to remove route {Destination}/{Prefix}", route.Destination, route.PrefixLength);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to restore routes during recovery");
        }

        // Restore DNS settings
        try
        {
            var dnsManager = new DnsManager();
            foreach (var (adapterName, dnsServers) in state.OriginalDnsSettings)
            {
                try
                {
                    if (dnsServers.Length == 0)
                    {
                        dnsManager.RestoreToDhcp(adapterName);
                    }
                    else
                    {
                        dnsManager.SetDnsServers(adapterName, dnsServers);
                    }
                    Log.Information("Restored DNS for adapter: {Adapter}", adapterName);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to restore DNS for adapter {Adapter}", adapterName);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to restore DNS settings during recovery");
        }

        // Kill any orphaned processes
        try
        {
            ProcessManager.KillOrphanedProcesses();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to kill orphaned processes");
        }

        ClearState();
        Log.Information("Crash recovery completed");
    }

    /// <summary>
    /// Saves the current state before making system changes.
    /// </summary>
    public void SaveState(RecoveryState state)
    {
        try
        {
            var directory = Path.GetDirectoryName(StateFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            state.Timestamp = DateTime.UtcNow;
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(StateFilePath, json);
            Log.Debug("Saved recovery state");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save recovery state");
        }
    }

    /// <summary>
    /// Clears the saved state (called after clean disconnect).
    /// </summary>
    public void ClearState()
    {
        try
        {
            if (File.Exists(StateFilePath))
            {
                File.Delete(StateFilePath);
                Log.Debug("Cleared recovery state");
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to clear recovery state file");
        }
    }
}
