using Serilog;

namespace PingTunnelVPN.Platform;

/// <summary>
/// Manages Windows Firewall rules for blocking UDP traffic on the VPN interface.
/// Includes robust cleanup to ensure rules are removed on disconnect, app exit, or crash recovery.
/// </summary>
public class FirewallManager
{
    /// <summary>
    /// Unique prefix for all firewall rules created by this application.
    /// Used to identify and clean up orphaned rules.
    /// </summary>
    private const string RuleNamePrefix = "PingTunnelVPN_BlockUDP_";
    
    private readonly List<string> _addedRules = new();
    private readonly object _lock = new();
    private bool _exitHandlerRegistered;

    /// <summary>
    /// Blocks outbound UDP traffic on the specified interface subnet.
    /// UDP to localhost (127.0.0.1) is allowed for the DNS forwarder.
    /// </summary>
    /// <param name="interfaceSubnet">The subnet to block UDP on (e.g., "198.18.0.0/24")</param>
    public void BlockUdpOnSubnet(string interfaceSubnet)
    {
        lock (_lock)
        {
            // Ensure exit handler is registered
            RegisterExitHandler();

            var ruleName = $"{RuleNamePrefix}{interfaceSubnet.Replace("/", "_").Replace(".", "_")}";
            
            try
            {
                // Add rule to block outbound UDP on the VPN subnet
                // Using localip to target traffic from the VPN interface
                var args = $"advfirewall firewall add rule name=\"{ruleName}\" " +
                          $"dir=out action=block protocol=udp " +
                          $"localip={interfaceSubnet} " +
                          $"remoteip=any " +
                          $"enable=yes";
                
                ExecuteNetshCommand(args);
                _addedRules.Add(ruleName);
                Log.Information("Added firewall rule to block UDP on {Subnet}: {RuleName}", interfaceSubnet, ruleName);

                // Add exception for localhost UDP (DNS forwarder uses this)
                var localhostRuleName = $"{RuleNamePrefix}AllowLocalhost";
                if (!_addedRules.Contains(localhostRuleName))
                {
                    var localhostArgs = $"advfirewall firewall add rule name=\"{localhostRuleName}\" " +
                                       $"dir=out action=allow protocol=udp " +
                                       $"remoteip=127.0.0.1 " +
                                       $"enable=yes";
                    ExecuteNetshCommand(localhostArgs);
                    _addedRules.Add(localhostRuleName);
                    Log.Information("Added firewall rule to allow localhost UDP: {RuleName}", localhostRuleName);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to add firewall rule for {Subnet}", interfaceSubnet);
                throw;
            }
        }
    }

    /// <summary>
    /// Removes all firewall rules created by this instance.
    /// </summary>
    public void RemoveAllRules()
    {
        lock (_lock)
        {
            foreach (var ruleName in _addedRules.ToList())
            {
                try
                {
                    DeleteRule(ruleName);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to remove firewall rule: {RuleName}", ruleName);
                }
            }
            _addedRules.Clear();
        }
    }

    /// <summary>
    /// Cleans up any orphaned firewall rules from previous app runs or crashes.
    /// Should be called on application startup.
    /// </summary>
    public static void CleanupOrphanedRules()
    {
        try
        {
            // Query for existing rules with our prefix
            var existingRules = GetExistingPingTunnelRules();
            
            if (existingRules.Count > 0)
            {
                Log.Information("Found {Count} orphaned firewall rules, cleaning up...", existingRules.Count);
                
                foreach (var ruleName in existingRules)
                {
                    try
                    {
                        DeleteRuleStatic(ruleName);
                        Log.Debug("Cleaned up orphaned firewall rule: {RuleName}", ruleName);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Failed to clean up orphaned firewall rule: {RuleName}", ruleName);
                    }
                }
                
                Log.Information("Orphaned firewall rules cleanup completed");
            }
            else
            {
                Log.Debug("No orphaned firewall rules found");
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to check for orphaned firewall rules");
        }
    }

    /// <summary>
    /// Gets a list of existing firewall rules with our prefix.
    /// </summary>
    private static List<string> GetExistingPingTunnelRules()
    {
        var rules = new List<string>();
        
        try
        {
            using var process = new System.Diagnostics.Process();
            process.StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "netsh.exe",
                Arguments = $"advfirewall firewall show rule name=all",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            // Parse output to find our rules
            // Output format: "Rule Name:                            PingTunnelVPN_BlockUDP_..."
            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (line.StartsWith("Rule Name:", StringComparison.OrdinalIgnoreCase))
                {
                    var ruleName = line.Substring("Rule Name:".Length).Trim();
                    if (ruleName.StartsWith(RuleNamePrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        rules.Add(ruleName);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to enumerate existing firewall rules");
        }

        return rules;
    }

    /// <summary>
    /// Deletes a firewall rule by name.
    /// </summary>
    private void DeleteRule(string ruleName)
    {
        DeleteRuleStatic(ruleName);
        _addedRules.Remove(ruleName);
    }

    /// <summary>
    /// Deletes a firewall rule by name (static version for cleanup).
    /// </summary>
    private static void DeleteRuleStatic(string ruleName)
    {
        var args = $"advfirewall firewall delete rule name=\"{ruleName}\"";
        
        using var process = new System.Diagnostics.Process();
        process.StartInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "netsh.exe",
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        // Exit code 0 = success, but also check for "No rules match" which is fine
        if (process.ExitCode != 0 && !output.Contains("No rules match", StringComparison.OrdinalIgnoreCase))
        {
            Log.Warning("netsh delete rule returned non-zero exit code for {RuleName}: {Output} {Error}", 
                ruleName, output, error);
        }
        else
        {
            Log.Debug("Deleted firewall rule: {RuleName}", ruleName);
        }
    }

    /// <summary>
    /// Registers handlers for app exit and process exit to ensure cleanup.
    /// </summary>
    private void RegisterExitHandler()
    {
        if (_exitHandlerRegistered)
            return;

        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        
        _exitHandlerRegistered = true;
        Log.Debug("Registered firewall cleanup handlers for process exit");
    }

    /// <summary>
    /// Called when the process is exiting normally.
    /// </summary>
    private void OnProcessExit(object? sender, EventArgs e)
    {
        Log.Debug("Process exit detected, cleaning up firewall rules");
        try
        {
            RemoveAllRules();
        }
        catch (Exception ex)
        {
            // Can't do much here, just log
            Log.Warning(ex, "Error during process exit firewall cleanup");
        }
    }

    /// <summary>
    /// Called on unhandled exception (crash).
    /// </summary>
    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Log.Debug("Unhandled exception detected, cleaning up firewall rules");
        try
        {
            RemoveAllRules();
        }
        catch
        {
            // Can't do much here during crash
        }
    }

    /// <summary>
    /// Executes a netsh command.
    /// </summary>
    private static void ExecuteNetshCommand(string args)
    {
        using var process = new System.Diagnostics.Process();
        process.StartInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "netsh.exe",
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"netsh failed with exit code {process.ExitCode}: {output} {error}");
        }
    }
}
