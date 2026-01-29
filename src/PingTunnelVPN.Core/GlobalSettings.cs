using System.IO;
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
    public List<string> BypassSubnets { get; set; } = new() { "127.0.0.0/8", "192.168.0.0/16", "10.0.0.0/8", "172.16.0.0/12" };

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

    /// <summary>
    /// Automatically check for updates on startup and periodically.
    /// </summary>
    public bool AutoCheckUpdates { get; set; } = true;

    /// <summary>
    /// Application log level (DEBUG, INFO, WARN, ERROR, FATAL).
    /// This controls both the UI log filter and PingTunnel's log level.
    /// </summary>
    public string AppLogLevel { get; set; } = "INFO";

    /// <summary>
    /// Custom log directory for application logs. Leave empty to use the default location.
    /// </summary>
    public string AppLogDirectory { get; set; } = string.Empty;

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

    // PingTunnel Settings - Basic
    /// <summary>
    /// Local address to listen for ICMP traffic (default: 0.0.0.0).
    /// Maps to pingtunnel -icmp_l flag.
    /// </summary>
    public string IcmpListenAddress { get; set; } = "0.0.0.0";

    /// <summary>
    /// Connection timeout in seconds (default: 60).
    /// Maps to pingtunnel -timeout flag.
    /// </summary>
    public int ConnectionTimeout { get; set; } = 60;

    // PingTunnel Settings - TCP Tunneling
    /// <summary>
    /// Enable TCP forwarding over ICMP tunnel (default: false).
    /// Maps to pingtunnel -tcp flag.
    /// </summary>
    public bool EnableTcp { get; set; } = false;

    /// <summary>
    /// TCP send/receive buffer size (default: "1MB").
    /// Maps to pingtunnel -tcp_bs flag.
    /// </summary>
    public string TcpBufferSize { get; set; } = "1MB";

    /// <summary>
    /// Maximum TCP window size (default: 20000).
    /// Maps to pingtunnel -tcp_mw flag.
    /// </summary>
    public int TcpMaxWindow { get; set; } = 20000;

    /// <summary>
    /// TCP timeout resend time in milliseconds (default: 400).
    /// Maps to pingtunnel -tcp_rst flag.
    /// </summary>
    public int TcpResendTimeout { get; set; } = 400;

    /// <summary>
    /// TCP compression threshold - compress packets larger than this size (0 = no compression).
    /// Maps to pingtunnel -tcp_gz flag.
    /// </summary>
    public int TcpCompressionThreshold { get; set; } = 0;

    /// <summary>
    /// Print TCP connection statistics to log (default: false).
    /// Maps to pingtunnel -tcp_stat flag.
    /// </summary>
    public bool TcpShowStatistics { get; set; } = false;

    // PingTunnel Settings - SOCKS5 Proxy
    /// <summary>
    /// Enable SOCKS5 proxy mode (default: true).
    /// Maps to pingtunnel -sock5 flag.
    /// </summary>
    public bool EnableSocks5 { get; set; } = true;

    /// <summary>
    /// Country codes to bypass in SOCKS5 mode (e.g., "CN" for China direct connect).
    /// Maps to pingtunnel -s5filter flag.
    /// </summary>
    public string Socks5GeoFilter { get; set; } = string.Empty;

    /// <summary>
    /// Path to GeoLite2-Country.mmdb database file for SOCKS5 geo filtering.
    /// Maps to pingtunnel -s5ftfile flag.
    /// </summary>
    public string Socks5FilterDbPath { get; set; } = "GeoLite2-Country.mmdb";

    // PingTunnel Settings - Logging
    /// <summary>
    /// Disable PingTunnel log files, only print to stdout (default: false).
    /// Maps to pingtunnel -nolog flag.
    /// </summary>
    public bool PtDisableLogFiles { get; set; } = false;

    /// <summary>
    /// PingTunnel log level (default: "info").
    /// Auto-synced from AppLogLevel.
    /// Maps to pingtunnel -loglevel flag.
    /// </summary>
    public string PtLogLevel { get; set; } = "info";

    // PingTunnel Settings - Debug
    /// <summary>
    /// Enable performance profiling on specified port (0 = disabled).
    /// Maps to pingtunnel -profile flag.
    /// </summary>
    public int ProfilePort { get; set; } = 0;

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
            AutoConnect = AutoConnect,
            MinimizeToTray = MinimizeToTray,
            StartMinimized = StartMinimized,
            AutoCheckUpdates = AutoCheckUpdates,
            AppLogLevel = AppLogLevel,
            AppLogDirectory = AppLogDirectory,
            AutoRestartOnHighLatency = AutoRestartOnHighLatency,
            LatencyThresholdMs = LatencyThresholdMs,
            HighLatencyCountThreshold = HighLatencyCountThreshold,
            RestartCooldownSeconds = RestartCooldownSeconds,
            MaxAutoRestarts = MaxAutoRestarts,
            // PingTunnel Settings
            IcmpListenAddress = IcmpListenAddress,
            ConnectionTimeout = ConnectionTimeout,
            EnableTcp = EnableTcp,
            TcpBufferSize = TcpBufferSize,
            TcpMaxWindow = TcpMaxWindow,
            TcpResendTimeout = TcpResendTimeout,
            TcpCompressionThreshold = TcpCompressionThreshold,
            TcpShowStatistics = TcpShowStatistics,
            EnableSocks5 = EnableSocks5,
            Socks5GeoFilter = Socks5GeoFilter,
            Socks5FilterDbPath = Socks5FilterDbPath,
            PtDisableLogFiles = PtDisableLogFiles,
            PtLogLevel = PtLogLevel,
            ProfilePort = ProfilePort
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

        // Validate SOCKS5 geo filter configuration
        if (!string.IsNullOrWhiteSpace(Socks5GeoFilter))
        {
            if (string.IsNullOrWhiteSpace(Socks5FilterDbPath))
            {
                errors.Add("SOCKS5 Geo Filter requires a database file path.");
            }
            else if (!File.Exists(Socks5FilterDbPath))
            {
                // Also check in common locations
                var resourcesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", Socks5FilterDbPath);
                var basePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Socks5FilterDbPath);
                
                if (!File.Exists(resourcesPath) && !File.Exists(basePath))
                {
                    errors.Add($"SOCKS5 Geo Filter database file not found: {Socks5FilterDbPath}. The geo filter feature requires a GeoLite2-Country.mmdb file.");
                }
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
