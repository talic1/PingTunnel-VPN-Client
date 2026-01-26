namespace PingTunnelVPN.Core;

/// <summary>
/// Represents the connection states of the VPN tunnel.
/// </summary>
public enum ConnectionState
{
    /// <summary>
    /// VPN is not connected. This is the initial state.
    /// </summary>
    Disconnected,

    /// <summary>
    /// VPN is in the process of connecting.
    /// </summary>
    Connecting,

    /// <summary>
    /// VPN is fully connected and routing traffic.
    /// </summary>
    Connected,

    /// <summary>
    /// VPN is in the process of disconnecting.
    /// </summary>
    Disconnecting,

    /// <summary>
    /// An error occurred during connection or while connected.
    /// </summary>
    Error
}

/// <summary>
/// Event arguments for connection state changes.
/// </summary>
public class ConnectionStateChangedEventArgs : EventArgs
{
    public ConnectionState PreviousState { get; }
    public ConnectionState NewState { get; }
    public string? Message { get; }
    public Exception? Error { get; }

    public ConnectionStateChangedEventArgs(
        ConnectionState previousState,
        ConnectionState newState,
        string? message = null,
        Exception? error = null)
    {
        PreviousState = previousState;
        NewState = newState;
        Message = message;
        Error = error;
    }
}

/// <summary>
/// Connection statistics.
/// </summary>
public class ConnectionStats
{
    public DateTime ConnectedAt { get; set; }
    public TimeSpan Uptime => DateTime.UtcNow - ConnectedAt;
    public long BytesReceived { get; set; }
    public long BytesSent { get; set; }
    public int PacketsReceived { get; set; }
    public int PacketsSent { get; set; }
    public string ServerAddress { get; set; } = string.Empty;
    
    /// <summary>
    /// Current latency in milliseconds (from pingtunnel pong responses).
    /// </summary>
    public double LatencyMs { get; set; }
    
    /// <summary>
    /// Indicates if the connection is experiencing latency issues.
    /// </summary>
    public bool IsLatencyDegraded { get; set; }
    
    /// <summary>
    /// Number of consecutive high-latency measurements.
    /// </summary>
    public int HighLatencyCount { get; set; }

    /// <summary>Downstream speed on TUN adapter (bytes/sec).</summary>
    public double TunRxBps { get; set; }
    /// <summary>Upstream speed on TUN adapter (bytes/sec).</summary>
    public double TunTxBps { get; set; }
    /// <summary>Downstream speed on physical adapter (bytes/sec).</summary>
    public double PhysicalRxBps { get; set; }
    /// <summary>Upstream speed on physical adapter (bytes/sec).</summary>
    public double PhysicalTxBps { get; set; }

    /// <summary>Session total bytes received on TUN.</summary>
    public long TunBytesReceived { get; set; }
    /// <summary>Session total bytes sent on TUN.</summary>
    public long TunBytesSent { get; set; }
    /// <summary>Session total bytes received on physical adapter.</summary>
    public long PhysicalBytesReceived { get; set; }
    /// <summary>Session total bytes sent on physical adapter.</summary>
    public long PhysicalBytesSent { get; set; }
}
