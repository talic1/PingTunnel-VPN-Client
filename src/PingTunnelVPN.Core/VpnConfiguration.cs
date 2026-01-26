using System.Text.Json.Serialization;

namespace PingTunnelVPN.Core;

/// <summary>
/// VPN connection configuration.
/// </summary>
public class VpnConfiguration
{
    /// <summary>
    /// Hostname or IP address of the pingtunnel server.
    /// </summary>
    public string ServerAddress { get; set; } = string.Empty;

    /// <summary>
    /// Shared key/password for pingtunnel authentication.
    /// </summary>
    public string ServerKey { get; set; } = string.Empty;

    /// <summary>
    /// Local port for the SOCKS5 proxy (default: 1080).
    /// </summary>
    public int LocalSocksPort { get; set; } = 1080;

    /// <summary>
    /// Creates a deep clone of this configuration.
    /// </summary>
    public VpnConfiguration Clone()
    {
        return new VpnConfiguration
        {
            ServerAddress = ServerAddress,
            ServerKey = ServerKey,
            LocalSocksPort = LocalSocksPort
        };
    }

    /// <summary>
    /// Validates the configuration and returns a list of errors.
    /// </summary>
    public List<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(ServerAddress))
        {
            errors.Add("Server address is required.");
        }

        if (LocalSocksPort < 1 || LocalSocksPort > 65535)
        {
            errors.Add("Local SOCKS port must be between 1 and 65535.");
        }

        return errors;
    }
}

/// <summary>
/// DNS handling mode.
/// </summary>
public enum DnsMode
{
    /// <summary>
    /// Route DNS queries through the SOCKS5 tunnel.
    /// </summary>
    TunnelDns,

    /// <summary>
    /// Use system default DNS (may leak).
    /// </summary>
    SystemDns
}
