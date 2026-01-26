using System.Management;
using System.Net.NetworkInformation;
using Serilog;

namespace PingTunnelVPN.Platform;

/// <summary>
/// Manages Windows DNS server configuration via WMI.
/// </summary>
public class DnsManager
{
    private readonly Dictionary<string, string[]> _originalDnsSettings = new();
    private readonly object _lock = new();

    /// <summary>
    /// Gets the DNS servers configured for an adapter.
    /// </summary>
    public string[] GetDnsServers(string adapterName)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = TRUE");

            foreach (ManagementObject obj in searcher.Get())
            {
                var description = obj["Description"]?.ToString();
                if (description != null && description.Contains(adapterName, StringComparison.OrdinalIgnoreCase))
                {
                    var dnsServers = obj["DNSServerSearchOrder"] as string[];
                    return dnsServers ?? Array.Empty<string>();
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to get DNS servers for adapter {Adapter}", adapterName);
        }

        return Array.Empty<string>();
    }

    /// <summary>
    /// Gets all network adapters and their DNS settings.
    /// </summary>
    public Dictionary<string, string[]> GetAllDnsSettings()
    {
        var result = new Dictionary<string, string[]>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = TRUE");

            foreach (ManagementObject obj in searcher.Get())
            {
                var description = obj["Description"]?.ToString();
                if (description != null)
                {
                    var dnsServers = obj["DNSServerSearchOrder"] as string[];
                    result[description] = dnsServers ?? Array.Empty<string>();
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to get DNS settings");
        }

        return result;
    }

    /// <summary>
    /// Backs up current DNS settings for all adapters.
    /// </summary>
    public void BackupDnsSettings()
    {
        lock (_lock)
        {
            _originalDnsSettings.Clear();
            var settings = GetAllDnsSettings();
            foreach (var kvp in settings)
            {
                _originalDnsSettings[kvp.Key] = kvp.Value;
            }
            Log.Information("Backed up DNS settings for {Count} adapters", _originalDnsSettings.Count);
        }
    }

    /// <summary>
    /// Sets DNS servers for an adapter.
    /// </summary>
    public void SetDnsServers(string adapterName, string[] dnsServers)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = TRUE");

            foreach (ManagementObject obj in searcher.Get())
            {
                var description = obj["Description"]?.ToString();
                if (description != null && description.Contains(adapterName, StringComparison.OrdinalIgnoreCase))
                {
                    var inParams = obj.GetMethodParameters("SetDNSServerSearchOrder");
                    inParams["DNSServerSearchOrder"] = dnsServers;
                    
                    var outParams = obj.InvokeMethod("SetDNSServerSearchOrder", inParams, null);
                    var returnValue = Convert.ToInt32(outParams["ReturnValue"]);
                    
                    if (returnValue == 0)
                    {
                        Log.Information("Set DNS servers for {Adapter}: {Servers}", 
                            adapterName, string.Join(", ", dnsServers));
                    }
                    else if (returnValue == 1)
                    {
                        Log.Warning("Set DNS servers for {Adapter}, reboot may be required", adapterName);
                    }
                    else
                    {
                        Log.Error("Failed to set DNS for {Adapter}: error code {Error}", adapterName, returnValue);
                        throw new InvalidOperationException($"SetDNSServerSearchOrder failed with code {returnValue}");
                    }
                    return;
                }
            }

            Log.Warning("Adapter not found: {Adapter}", adapterName);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to set DNS servers for adapter {Adapter}", adapterName);
            throw;
        }
    }

    /// <summary>
    /// Sets DNS servers for all active adapters.
    /// </summary>
    public void SetDnsForAllAdapters(string[] dnsServers)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = TRUE");

            foreach (ManagementObject obj in searcher.Get())
            {
                var description = obj["Description"]?.ToString();
                if (description == null) continue;

                // Skip virtual/loopback adapters
                if (description.Contains("Loopback", StringComparison.OrdinalIgnoreCase) ||
                    description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) && 
                    !description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    var inParams = obj.GetMethodParameters("SetDNSServerSearchOrder");
                    inParams["DNSServerSearchOrder"] = dnsServers;
                    
                    var outParams = obj.InvokeMethod("SetDNSServerSearchOrder", inParams, null);
                    var returnValue = Convert.ToInt32(outParams["ReturnValue"]);
                    
                    if (returnValue == 0 || returnValue == 1)
                    {
                        Log.Information("Set DNS servers for {Adapter}", description);
                    }
                    else
                    {
                        Log.Warning("Failed to set DNS for {Adapter}: error {Error}", description, returnValue);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Error setting DNS for adapter {Adapter}", description);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to set DNS for all adapters");
            throw;
        }
    }

    /// <summary>
    /// Restores an adapter to DHCP-assigned DNS.
    /// </summary>
    public void RestoreToDhcp(string adapterName)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = TRUE");

            foreach (ManagementObject obj in searcher.Get())
            {
                var description = obj["Description"]?.ToString();
                if (description != null && description.Contains(adapterName, StringComparison.OrdinalIgnoreCase))
                {
                    var inParams = obj.GetMethodParameters("SetDNSServerSearchOrder");
                    inParams["DNSServerSearchOrder"] = null;
                    
                    var outParams = obj.InvokeMethod("SetDNSServerSearchOrder", inParams, null);
                    var returnValue = Convert.ToInt32(outParams["ReturnValue"]);
                    
                    if (returnValue == 0 || returnValue == 1)
                    {
                        Log.Information("Restored {Adapter} to DHCP DNS", adapterName);
                    }
                    else
                    {
                        Log.Warning("Failed to restore DHCP DNS for {Adapter}: error {Error}", adapterName, returnValue);
                    }
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to restore DHCP DNS for adapter {Adapter}", adapterName);
            throw;
        }
    }

    /// <summary>
    /// Restores all adapters to their original DNS settings.
    /// </summary>
    public void RestoreAllDnsSettings()
    {
        lock (_lock)
        {
            foreach (var kvp in _originalDnsSettings)
            {
                try
                {
                    if (kvp.Value.Length == 0)
                    {
                        RestoreToDhcp(kvp.Key);
                    }
                    else
                    {
                        SetDnsServers(kvp.Key, kvp.Value);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to restore DNS for adapter {Adapter}", kvp.Key);
                }
            }
            
            _originalDnsSettings.Clear();
            Log.Information("Restored all DNS settings");
        }
    }

    /// <summary>
    /// Gets the stored original DNS settings.
    /// </summary>
    public Dictionary<string, string[]> GetOriginalDnsSettings()
    {
        lock (_lock)
        {
            return new Dictionary<string, string[]>(_originalDnsSettings);
        }
    }

    /// <summary>
    /// Flushes the DNS resolver cache.
    /// </summary>
    public static void FlushDnsCache()
    {
        try
        {
            using var process = new System.Diagnostics.Process();
            process.StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ipconfig.exe",
                Arguments = "/flushdns",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            process.Start();
            process.WaitForExit(5000);
            Log.Information("Flushed DNS cache");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to flush DNS cache");
        }
    }
}
