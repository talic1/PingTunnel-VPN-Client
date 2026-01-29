namespace PingTunnelVPN.Core.Models;

/// <summary>
/// Represents information about an available application update from GitHub releases.
/// </summary>
public class UpdateInfo
{
    /// <summary>
    /// The version string of the update (e.g., "1.2.0").
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// The download URL for the installer executable.
    /// </summary>
    public string DownloadUrl { get; set; } = string.Empty;

    /// <summary>
    /// The release notes/changelog for this version.
    /// </summary>
    public string ReleaseNotes { get; set; } = string.Empty;

    /// <summary>
    /// The date and time when this release was published.
    /// </summary>
    public DateTime PublishedAt { get; set; }

    /// <summary>
    /// The size of the installer file in bytes.
    /// </summary>
    public long FileSizeBytes { get; set; }

    /// <summary>
    /// The name of the release (e.g., "v1.2.0 - Feature Update").
    /// </summary>
    public string ReleaseName { get; set; } = string.Empty;
}
