using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Serilog;

namespace PingTunnelVPN.Platform;

/// <summary>
/// Provides network adapter and interface helper methods.
/// </summary>
public static class NetworkHelper
{
    /// <summary>
    /// Gets all active network adapters.
    /// </summary>
    public static List<NetworkAdapterInfo> GetActiveAdapters()
    {
        var adapters = new List<NetworkAdapterInfo>();

        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up)
                    continue;

                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                var ipProps = nic.GetIPProperties();
                var ipv4Address = ipProps.UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?
                    .Address.ToString();

                var gateway = ipProps.GatewayAddresses
                    .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork)?
                    .Address.ToString();

                var dnsServers = ipProps.DnsAddresses
                    .Where(d => d.AddressFamily == AddressFamily.InterNetwork)
                    .Select(d => d.ToString())
                    .ToArray();

                adapters.Add(new NetworkAdapterInfo
                {
                    Name = nic.Name,
                    Description = nic.Description,
                    Id = nic.Id,
                    InterfaceIndex = ipProps.GetIPv4Properties()?.Index ?? 0,
                    IPv4Address = ipv4Address,
                    Gateway = gateway,
                    DnsServers = dnsServers,
                    InterfaceType = nic.NetworkInterfaceType.ToString(),
                    Speed = nic.Speed
                });
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to enumerate network adapters");
        }

        return adapters;
    }

    /// <summary>
    /// Gets the primary network adapter (with default gateway).
    /// </summary>
    public static NetworkAdapterInfo? GetPrimaryAdapter()
    {
        return GetActiveAdapters()
            .Where(a => !string.IsNullOrEmpty(a.Gateway))
            .OrderByDescending(a => a.Speed)
            .FirstOrDefault();
    }

    /// <summary>
    /// Resolves a hostname to IP addresses.
    /// </summary>
    public static async Task<IPAddress[]> ResolveHostnameAsync(string hostname)
    {
        try
        {
            // If it's already an IP, just parse it
            if (IPAddress.TryParse(hostname, out var ip))
            {
                return new[] { ip };
            }

            var entry = await Dns.GetHostEntryAsync(hostname);
            return entry.AddressList
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                .ToArray();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to resolve hostname: {Hostname}", hostname);
            throw;
        }
    }

    /// <summary>
    /// Checks if a TCP port is listening.
    /// </summary>
    public static async Task<bool> IsPortListeningAsync(string host, int port, int timeoutMs = 1000)
    {
        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(timeoutMs);
            await client.ConnectAsync(host, port, cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Waits for a port to start listening.
    /// </summary>
    public static async Task<bool> WaitForPortAsync(string host, int port, int timeoutMs = 10000, int pollIntervalMs = 200)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (await IsPortListeningAsync(host, port, 500))
            {
                return true;
            }
            await Task.Delay(pollIntervalMs);
        }
        return false;
    }

    /// <summary>
    /// Gets the index of a network interface by name pattern.
    /// </summary>
    public static int? GetInterfaceIndexByName(string namePattern)
    {
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.Name.Contains(namePattern, StringComparison.OrdinalIgnoreCase) ||
                    nic.Description.Contains(namePattern, StringComparison.OrdinalIgnoreCase))
                {
                    var ipProps = nic.GetIPProperties();
                    return ipProps.GetIPv4Properties()?.Index;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to find interface by name: {Pattern}", namePattern);
        }

        return null;
    }

    /// <summary>
    /// Gets the adapter name (for netsh) by interface index.
    /// </summary>
    public static string? GetInterfaceNameByIndex(int interfaceIndex)
    {
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                var ipProps = nic.GetIPProperties();
                var idx = ipProps.GetIPv4Properties()?.Index;
                if (idx == interfaceIndex)
                    return nic.Name;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to get interface name for index {Index}", interfaceIndex);
        }

        return null;
    }

    /// <summary>
    /// Gets the network interface by its IPv4 interface index.
    /// </summary>
    public static NetworkInterface? GetNetworkInterfaceByIndex(int index)
    {
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                var ipProps = nic.GetIPProperties();
                var idx = ipProps.GetIPv4Properties()?.Index;
                if (idx == index)
                    return nic;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to get interface by index {Index}", index);
        }

        return null;
    }

    /// <summary>
    /// Gets IPv4 bytes received/sent for an interface by index.
    /// Returns false if the interface is not found or stats cannot be read.
    /// </summary>
    public static bool TryGetInterfaceStats(int index, out long bytesReceived, out long bytesSent)
    {
        bytesReceived = 0;
        bytesSent = 0;
        var nic = GetNetworkInterfaceByIndex(index);
        if (nic == null)
            return false;
        try
        {
            var stats = nic.GetIPStatistics();
            bytesReceived = stats.BytesReceived;
            bytesSent = stats.BytesSent;
            return true;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to get stats for interface index {Index}", index);
            return false;
        }
    }

    /// <summary>
    /// Parses a CIDR notation string into network address and prefix length.
    /// </summary>
    public static (IPAddress Network, int PrefixLength) ParseCidr(string cidr)
    {
        var parts = cidr.Split('/');
        if (parts.Length != 2)
            throw new ArgumentException($"Invalid CIDR notation: {cidr}");

        var network = IPAddress.Parse(parts[0]);
        var prefixLength = int.Parse(parts[1]);

        if (prefixLength < 0 || prefixLength > 32)
            throw new ArgumentException($"Invalid prefix length: {prefixLength}");

        return (network, prefixLength);
    }

    /// <summary>
    /// Checks if an IP address is in a given subnet.
    /// </summary>
    public static bool IsInSubnet(IPAddress address, IPAddress network, int prefixLength)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork ||
            network.AddressFamily != AddressFamily.InterNetwork)
            return false;

        var addressBytes = address.GetAddressBytes();
        var networkBytes = network.GetAddressBytes();

        uint addressValue = (uint)(addressBytes[0] << 24 | addressBytes[1] << 16 | addressBytes[2] << 8 | addressBytes[3]);
        uint networkValue = (uint)(networkBytes[0] << 24 | networkBytes[1] << 16 | networkBytes[2] << 8 | networkBytes[3]);
        uint mask = prefixLength == 0 ? 0 : uint.MaxValue << (32 - prefixLength);

        return (addressValue & mask) == (networkValue & mask);
    }
}

/// <summary>
/// Information about a network adapter.
/// </summary>
public class NetworkAdapterInfo
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public int InterfaceIndex { get; set; }
    public string? IPv4Address { get; set; }
    public string? Gateway { get; set; }
    public string[] DnsServers { get; set; } = Array.Empty<string>();
    public string InterfaceType { get; set; } = string.Empty;
    public long Speed { get; set; }
}
