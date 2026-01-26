using System.Net;
using System.Runtime.InteropServices;
using PingTunnelVPN.Platform.Native;
using Serilog;

namespace PingTunnelVPN.Platform;

/// <summary>
/// Manages Windows IP routing table entries.
/// </summary>
public class RouteManager
{
    private readonly List<RouteEntry> _addedRoutes = new();
    private readonly object _lock = new();

    /// <summary>
    /// Represents a route entry.
    /// </summary>
    public record RouteEntry(
        string Destination,
        int PrefixLength,
        string Gateway,
        uint InterfaceIndex,
        uint Metric);

    /// <summary>
    /// Gets the default gateway information.
    /// </summary>
    public (IPAddress Gateway, uint InterfaceIndex)? GetDefaultGateway()
    {
        // Use shell-based approach for reliability
        try
        {
            return GetDefaultGatewayViaShell();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Shell-based gateway detection failed, trying P/Invoke");
        }

        // Fallback to P/Invoke (may have issues on some systems)
        return GetDefaultGatewayViaPInvoke();
    }

    /// <summary>
    /// Gets the default gateway using route print command.
    /// </summary>
    private (IPAddress Gateway, uint InterfaceIndex)? GetDefaultGatewayViaShell()
    {
        using var process = new global::System.Diagnostics.Process();
        process.StartInfo = new global::System.Diagnostics.ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -Command \"Get-NetRoute -DestinationPrefix '0.0.0.0/0' | Sort-Object -Property RouteMetric | Select-Object -First 1 | ForEach-Object { $_.NextHop + '|' + $_.InterfaceIndex }\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        process.Start();
        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();

        if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
        {
            var parts = output.Split('|');
            if (parts.Length == 2 && 
                IPAddress.TryParse(parts[0], out var gateway) && 
                uint.TryParse(parts[1], out var ifIndex))
            {
                Log.Debug("Found default gateway via shell: {Gateway} on interface {Index}", gateway, ifIndex);
                return (gateway, ifIndex);
            }
        }

        Log.Warning("Failed to get default gateway via shell, output: {Output}", output);
        return null;
    }

    /// <summary>
    /// Gets the default gateway using P/Invoke (fallback).
    /// </summary>
    private (IPAddress Gateway, uint InterfaceIndex)? GetDefaultGatewayViaPInvoke()
    {
        IntPtr tablePtr = IntPtr.Zero;
        try
        {
            int result = IpHelperApi.GetIpForwardTable2(IpHelperApi.AF_INET, out tablePtr);
            if (result != IpHelperApi.ERROR_SUCCESS)
            {
                Log.Warning("GetIpForwardTable2 failed with error {Error}", result);
                return null;
            }

            var table = Marshal.PtrToStructure<IpHelperApi.MIB_IPFORWARD_TABLE2>(tablePtr);
            int rowSize = Marshal.SizeOf<IpHelperApi.MIB_IPFORWARD_ROW2>();
            IntPtr rowPtr = tablePtr + Marshal.SizeOf<IpHelperApi.MIB_IPFORWARD_TABLE2>();

            IpHelperApi.MIB_IPFORWARD_ROW2? bestRoute = null;
            uint lowestMetric = uint.MaxValue;

            for (int i = 0; i < table.NumEntries; i++)
            {
                var row = Marshal.PtrToStructure<IpHelperApi.MIB_IPFORWARD_ROW2>(rowPtr + (i * rowSize));
                
                // Check for default route (0.0.0.0/0)
                if (row.DestinationPrefix.PrefixLength == 0)
                {
                    var destAddr = IpHelperApi.GetAddressFromSockAddr(row.DestinationPrefix.Prefix);
                    if (destAddr.Equals(IPAddress.Any))
                    {
                        if (row.Metric < lowestMetric)
                        {
                            bestRoute = row;
                            lowestMetric = row.Metric;
                        }
                    }
                }
            }

            if (bestRoute.HasValue)
            {
                var gateway = IpHelperApi.GetAddressFromSockAddr(bestRoute.Value.NextHop);
                return (gateway, bestRoute.Value.InterfaceIndex);
            }

            return null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "P/Invoke gateway detection failed");
            return null;
        }
        finally
        {
            if (tablePtr != IntPtr.Zero)
            {
                IpHelperApi.FreeMibTable(tablePtr);
            }
        }
    }

    /// <summary>
    /// Gets the best interface for reaching a destination.
    /// </summary>
    public uint? GetBestInterface(IPAddress destination)
    {
        var destAddr = IpHelperApi.CreateSockAddrInet(destination);
        int result = IpHelperApi.GetBestInterfaceEx(ref destAddr, out uint ifIndex);
        
        if (result == IpHelperApi.ERROR_SUCCESS)
        {
            return ifIndex;
        }

        Log.Warning("GetBestInterfaceEx failed with error {Error}", result);
        return null;
    }

    /// <summary>
    /// Adds a route to the routing table using shell (route.exe) to avoid P/Invoke TypeLoadException.
    /// </summary>
    public void AddRoute(string destination, int prefixLength, string gateway, uint interfaceIndex, uint metric = 0)
    {
        lock (_lock)
        {
            if (!IPAddress.TryParse(destination, out _))
                throw new ArgumentException($"Invalid destination address: {destination}");
            if (!IPAddress.TryParse(gateway, out _))
                throw new ArgumentException($"Invalid gateway address: {gateway}");

            AddRouteViaShell(destination, prefixLength, gateway, interfaceIndex, metric);
            var entry = new RouteEntry(destination, prefixLength, gateway, interfaceIndex, metric);
            _addedRoutes.Add(entry);
        }
    }

    /// <summary>
    /// Deletes a route from the routing table.
    /// </summary>
    public void DeleteRoute(string destination, int prefixLength, uint interfaceIndex)
    {
        lock (_lock)
        {
            if (!IPAddress.TryParse(destination, out var destAddr))
                throw new ArgumentException($"Invalid destination address: {destination}");

            var row = new IpHelperApi.MIB_IPFORWARD_ROW2();
            IpHelperApi.InitializeIpForwardEntry(ref row);

            row.InterfaceIndex = interfaceIndex;
            row.DestinationPrefix = new IpHelperApi.IP_ADDRESS_PREFIX
            {
                Prefix = IpHelperApi.CreateSockAddrInet(destAddr),
                PrefixLength = (byte)prefixLength
            };

            int result = IpHelperApi.DeleteIpForwardEntry2(ref row);
            
            if (result == IpHelperApi.ERROR_SUCCESS || result == IpHelperApi.ERROR_NOT_FOUND)
            {
                _addedRoutes.RemoveAll(r => 
                    r.Destination == destination && 
                    r.PrefixLength == prefixLength && 
                    r.InterfaceIndex == interfaceIndex);
                Log.Information("Deleted route: {Dest}/{Prefix} if{Index}", destination, prefixLength, interfaceIndex);
            }
            else
            {
                Log.Warning("Failed to delete route {Dest}/{Prefix}: error {Error}", destination, prefixLength, result);
            }
        }
    }

    /// <summary>
    /// Adds a route using the shell (fallback method).
    /// </summary>
    public void AddRouteViaShell(string destination, int prefixLength, string gateway, uint interfaceIndex = 0, uint metric = 0)
    {
        var mask = PrefixLengthToSubnetMask(prefixLength);
        var args = $"add {destination} mask {mask} {gateway}";
        
        // Always specify interface index for explicit binding
        if (interfaceIndex > 0)
        {
            args += $" if {interfaceIndex}";
        }
        
        if (metric > 0)
        {
            args += $" metric {metric}";
        }

        ExecuteRouteCommand(args);
        Log.Information("Added route via shell: {Dest}/{Prefix} via {Gateway} if {Interface} metric {Metric}", 
            destination, prefixLength, gateway, interfaceIndex, metric);
    }

    /// <summary>
    /// Deletes a route using the shell (fallback method).
    /// </summary>
    public void DeleteRouteViaShell(string destination, int prefixLength, string gateway)
    {
        var mask = PrefixLengthToSubnetMask(prefixLength);
        var args = $"delete {destination} mask {mask} {gateway}";
        
        try
        {
            ExecuteRouteCommand(args);
            Log.Information("Deleted route via shell: {Dest}/{Prefix}", destination, prefixLength);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to delete route via shell: {Dest}/{Prefix}", destination, prefixLength);
        }
    }

    /// <summary>
    /// Removes all routes that were added by this manager (uses shell to avoid P/Invoke).
    /// </summary>
    public void RemoveAllAddedRoutes()
    {
        lock (_lock)
        {
            foreach (var route in _addedRoutes.ToList())
            {
                try
                {
                    DeleteRouteViaShell(route.Destination, route.PrefixLength, route.Gateway);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to remove route {Dest}/{Prefix}", route.Destination, route.PrefixLength);
                }
            }
            _addedRoutes.Clear();
        }
    }

    /// <summary>
    /// Gets the list of routes added by this manager.
    /// </summary>
    public List<RouteEntry> GetAddedRoutes()
    {
        lock (_lock)
        {
            return new List<RouteEntry>(_addedRoutes);
        }
    }

    /// <summary>
    /// Gets the interface index for an adapter by name.
    /// </summary>
    public uint? GetInterfaceIndexByName(string adapterName)
    {
        int result = IpHelperApi.ConvertInterfaceNameToLuidW(adapterName, out var luid);
        if (result != IpHelperApi.ERROR_SUCCESS)
        {
            Log.Warning("ConvertInterfaceNameToLuidW failed for {Name}: error {Error}", adapterName, result);
            return null;
        }

        result = IpHelperApi.ConvertInterfaceLuidToIndex(ref luid, out uint index);
        if (result != IpHelperApi.ERROR_SUCCESS)
        {
            Log.Warning("ConvertInterfaceLuidToIndex failed: error {Error}", result);
            return null;
        }

        return index;
    }

    /// <summary>
    /// Sets the interface metric for better route prioritization (best-effort; may throw TypeLoadException).
    /// </summary>
    public void SetInterfaceMetric(uint interfaceIndex, uint metric)
    {
        bool success = false;
        
        // Try P/Invoke first
        try
        {
            var row = new IpHelperApi.MIB_IPINTERFACE_ROW
            {
                Family = IpHelperApi.AF_INET,
                InterfaceIndex = interfaceIndex
            };

            int result = IpHelperApi.GetIpInterfaceEntry(ref row);
            if (result == IpHelperApi.ERROR_SUCCESS)
            {
                row.UseAutomaticMetric = false;
                row.Metric = metric;

                result = IpHelperApi.SetIpInterfaceEntry(ref row);
                if (result == IpHelperApi.ERROR_SUCCESS)
                {
                    Log.Information("Set interface {Index} metric to {Metric} via P/Invoke", interfaceIndex, metric);
                    success = true;
                }
                else
                {
                    Log.Debug("SetIpInterfaceEntry failed with error {Error}, trying netsh fallback", result);
                }
            }
            else
            {
                Log.Debug("GetIpInterfaceEntry failed with error {Error}, trying netsh fallback", result);
            }
        }
        catch (TypeLoadException)
        {
            Log.Debug("P/Invoke type load failed for SetInterfaceMetric, trying netsh fallback");
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "P/Invoke SetInterfaceMetric failed, trying netsh fallback");
        }

        // Fallback to netsh if P/Invoke failed
        if (!success)
        {
            try
            {
                SetInterfaceMetricViaNetsh(interfaceIndex, metric);
                success = true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to set interface {Index} metric via netsh", interfaceIndex);
            }
        }
    }

    /// <summary>
    /// Sets the interface metric using netsh (fallback method).
    /// </summary>
    private void SetInterfaceMetricViaNetsh(uint interfaceIndex, uint metric)
    {
        // Get interface name by index first
        var name = NetworkHelper.GetInterfaceNameByIndex((int)interfaceIndex);
        if (string.IsNullOrEmpty(name))
        {
            Log.Warning("Could not get interface name for index {Index}", interfaceIndex);
            return;
        }

        var args = $"interface ipv4 set interface interface=\"{name}\" metric={metric}";
        ExecuteNetshCommand(args);
        Log.Information("Set interface {Name} (index {Index}) metric to {Metric} via netsh", name, interfaceIndex, metric);
    }

    /// <summary>
    /// Sets static IPv4 address on an adapter by name (e.g. for the TUN interface).
    /// Uses netsh. gateway=none to avoid adding a default gateway on that interface.
    /// </summary>
    public void SetInterfaceAddress(string adapterName, string address, int prefixLength)
    {
        var mask = PrefixLengthToSubnetMask(prefixLength);
        var args = $"interface ip set address name=\"{adapterName}\" source=static addr={address} mask={mask} gateway=none";
        ExecuteNetshCommand(args);
        Log.Information("Set interface {Name} address to {Address}/{Prefix}", adapterName, address, prefixLength);
    }

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
            throw new InvalidOperationException($"netsh failed: {output} {error}");
        }
    }

    private static void ExecuteRouteCommand(string args)
    {
        using var process = new System.Diagnostics.Process();
        process.StartInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "route.exe",
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
            throw new InvalidOperationException($"route.exe failed: {output} {error}");
        }
    }

    private static string PrefixLengthToSubnetMask(int prefixLength)
    {
        if (prefixLength < 0 || prefixLength > 32)
            throw new ArgumentOutOfRangeException(nameof(prefixLength));

        uint mask = prefixLength == 0 ? 0 : uint.MaxValue << (32 - prefixLength);
        byte[] bytes = BitConverter.GetBytes(mask);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        
        return new IPAddress(bytes).ToString();
    }

    /// <summary>
    /// Adds blackhole routes for broadcast and multicast addresses to prevent
    /// them from entering the tunnel. These routes send traffic to a null gateway
    /// on the original interface instead of the VPN tunnel.
    /// </summary>
    /// <param name="originalGateway">The original network gateway</param>
    /// <param name="originalInterfaceIndex">The original network interface index</param>
    /// <param name="tunSubnetBroadcast">The TUN subnet broadcast address (e.g., 198.18.0.255)</param>
    public void AddBroadcastBlackholeRoutes(string originalGateway, uint originalInterfaceIndex, string? tunSubnetBroadcast = null)
    {
        // List of broadcast/multicast addresses to blackhole
        var blackholeRoutes = new List<(string destination, int prefix, string description)>
        {
            ("255.255.255.255", 32, "General broadcast"),
            ("224.0.0.0", 4, "Multicast range"),       // 224.0.0.0 - 239.255.255.255
            ("169.254.0.0", 16, "Link-local range"),   // APIPA
        };

        // Add TUN subnet broadcast if specified
        if (!string.IsNullOrEmpty(tunSubnetBroadcast))
        {
            blackholeRoutes.Add((tunSubnetBroadcast, 32, "TUN subnet broadcast"));
        }

        foreach (var (destination, prefix, description) in blackholeRoutes)
        {
            try
            {
                // Route broadcast/multicast traffic via the original gateway
                // This effectively blackholes it from the perspective of the VPN tunnel
                AddRoute(destination, prefix, originalGateway, originalInterfaceIndex, 1);
                Log.Information("Added blackhole route for {Description}: {Destination}/{Prefix}", 
                    description, destination, prefix);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to add blackhole route for {Description}: {Destination}/{Prefix}", 
                    description, destination, prefix);
            }
        }
    }
}
