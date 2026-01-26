namespace PingTunnelVPN.Core;

/// <summary>
/// Represents a server configuration with metadata.
/// </summary>
public class ServerConfig
{
    /// <summary>
    /// Unique identifier for this configuration.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// User-friendly name for this configuration.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The VPN configuration settings.
    /// </summary>
    public VpnConfiguration Configuration { get; set; } = new();

    /// <summary>
    /// Timestamp when this configuration was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when this configuration was last modified.
    /// </summary>
    public DateTime LastModified { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Creates a deep clone of this server configuration.
    /// </summary>
    public ServerConfig Clone()
    {
        return new ServerConfig
        {
            Id = Id,
            Name = Name,
            Configuration = Configuration.Clone(),
            CreatedAt = CreatedAt,
            LastModified = LastModified
        };
    }
}
