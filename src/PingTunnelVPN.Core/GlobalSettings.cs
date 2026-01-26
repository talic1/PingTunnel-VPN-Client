using System.Text.Json.Serialization;

namespace PingTunnelVPN.Core;

/// <summary>
/// Global application settings shared across all server configurations.
/// </summary>
public class GlobalSettings
{
    // Network settings
    /// <summary>
    /// MTU size for the TUN adapter (default: 1420).
    /// </summary>
    public int Mtu { get; set; } = 1420;

    /// <summary>
    /// DNS mode for handling DNS queries.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DnsMode DnsMode { get; set; } = DnsMode.TunnelDns;

    /// <summary>
    /// Upstream DNS servers when using tunneled DNS.
    /// </summary>
    public List<string> DnsServers { get; set; } = new() { "1.1.1.1", "8.8.8.8" };

    /// <summary>
    /// CIDRs to bypass the tunnel (split tunneling).
    /// </summary>
    public List<string> BypassSubnets { get; set; } = new() { "192.168.0.0/16", "10.0.0.0/8", "172.16.0.0/12" };

    /// <summary>
    /// If enabled, block all non-tunnel traffic when connected.
    /// </summary>
    public bool KillSwitch { get; set; } = false;

    // Encryption
    /// <summary>
    /// Encryption mode for pingtunnel (optional).
    /// Options: none, aes128, aes256, chacha20
    /// </summary>
    public string EncryptionMode { get; set; } = "none";

    /// <summary>
    /// Encryption key for pingtunnel (base64 or passphrase).
    /// </summary>
    public string EncryptionKey { get; set; } = string.Empty;

    // UDP
    /// <summary>
    /// Enable UDP forwarding in tun2socks.
    /// Note: UDP forwarding only works if the SOCKS5 server supports UDP ASSOCIATE.
    /// pingtunnel does NOT support UDP ASSOCIATE, so this should typically be false.
    /// </summary>
    public bool EnableUdp { get; set; } = false;

    /// <summary>
    /// UDP timeout in seconds for tun2socks.
    /// </summary>
    public int UdpTimeout { get; set; } = 60;

    // Application behavior
    /// <summary>
    /// Auto-connect on application startup.
    /// </summary>
    public bool AutoConnect { get; set; } = false;

    /// <summary>
    /// Minimize to tray on close.
    /// </summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>
    /// Start minimized.
    /// </summary>
    public bool StartMinimized { get; set; } = false;

    // Auto-restart
    /// <summary>
    /// Automatically restart the tunnel when latency exceeds threshold.
    /// This performs a "fast restart" - only restarts processes, keeps NIC/routes intact.
    /// </summary>
    public bool AutoRestartOnHighLatency { get; set; } = true;

    /// <summary>
    /// Latency threshold in milliseconds. Tunnel restarts when latency exceeds this.
    /// Default: 1000ms (1 second)
    /// </summary>
    public int LatencyThresholdMs { get; set; } = 1000;

    /// <summary>
    /// Number of consecutive high-latency readings before triggering restart.
    /// Default: 5 readings (with 1-second ping interval = ~5 seconds of high latency)
    /// </summary>
    public int HighLatencyCountThreshold { get; set; } = 5;

    /// <summary>
    /// Minimum seconds between auto-restarts to prevent restart loops.
    /// Default: 30 seconds
    /// </summary>
    public int RestartCooldownSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum number of auto-restarts before giving up and disconnecting.
    /// Default: 3 restarts (set to 0 for unlimited)
    /// </summary>
    public int MaxAutoRestarts { get; set; } = 3;

    /// <summary>
    /// Creates a deep clone of this global settings instance.
    /// </summary>
    public GlobalSettings Clone()
    {
        return new GlobalSettings
        {
            Mtu = Mtu,
            DnsMode = DnsMode,
            DnsServers = new List<string>(DnsServers),
            BypassSubnets = new List<string>(BypassSubnets),
            KillSwitch = KillSwitch,
            EncryptionMode = EncryptionMode,
            EncryptionKey = EncryptionKey,
            EnableUdp = EnableUdp,
            UdpTimeout = UdpTimeout,
            AutoConnect = AutoConnect,
            MinimizeToTray = MinimizeToTray,
            StartMinimized = StartMinimized,
            AutoRestartOnHighLatency = AutoRestartOnHighLatency,
            LatencyThresholdMs = LatencyThresholdMs,
            HighLatencyCountThreshold = HighLatencyCountThreshold,
            RestartCooldownSeconds = RestartCooldownSeconds,
            MaxAutoRestarts = MaxAutoRestarts
        };
    }

    /// <summary>
    /// Validates the global settings and returns a list of errors.
    /// </summary>
    public List<string> Validate()
    {
        var errors = new List<string>();

        if (Mtu < 576 || Mtu > 9000)
        {
            errors.Add("MTU must be between 576 and 9000.");
        }

        if (DnsMode == DnsMode.TunnelDns && DnsServers.Count == 0)
        {
            errors.Add("At least one DNS server is required for tunneled DNS mode.");
        }

        foreach (var dns in DnsServers)
        {
            if (!System.Net.IPAddress.TryParse(dns, out _))
            {
                errors.Add($"Invalid DNS server address: {dns}");
            }
        }

        foreach (var subnet in BypassSubnets)
        {
            if (!IsValidCidr(subnet))
            {
                errors.Add($"Invalid bypass subnet: {subnet}");
            }
        }

        return errors;
    }

    private static bool IsValidCidr(string cidr)
    {
        if (string.IsNullOrWhiteSpace(cidr))
            return false;

        var parts = cidr.Split('/');
        if (parts.Length != 2)
            return false;

        if (!System.Net.IPAddress.TryParse(parts[0], out _))
            return false;

        if (!int.TryParse(parts[1], out int prefix) || prefix < 0 || prefix > 32)
            return false;

        return true;
    }
}
