using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using PingTunnelVPN.Core.Models;
using Serilog;

namespace PingTunnelVPN.Core.Services;

/// <summary>
/// Service for checking and downloading application updates from GitHub releases.
/// </summary>
public class UpdateService : IDisposable
{
    private const string GitHubApiBaseUrl = "https://api.github.com";
    private const string RepoOwner = "DrSaeedHub";
    private const string RepoName = "PingTunnel-VPN-Client";
    private const string InstallerFilePattern = "PingTunnelVPN-Setup";

    private readonly HttpClient _httpClient;
    private readonly string _currentVersion;
    private readonly string _tempDownloadDirectory;
    private bool _disposed;

    /// <summary>
    /// Event raised when an update is available.
    /// </summary>
    public event EventHandler<UpdateInfo>? UpdateAvailable;

    /// <summary>
    /// Event raised when download progress changes.
    /// </summary>
    public event EventHandler<double>? DownloadProgressChanged;

    /// <summary>
    /// Event raised when download completes.
    /// </summary>
    public event EventHandler<string>? DownloadCompleted;

    /// <summary>
    /// Creates a new instance of the UpdateService.
    /// </summary>
    /// <param name="currentVersion">The current application version string.</param>
    public UpdateService(string currentVersion)
    {
        _currentVersion = currentVersion;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", $"PingTunnelVPN/{currentVersion}");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");

        _tempDownloadDirectory = Path.Combine(Path.GetTempPath(), "PingTunnelVPN");
        Directory.CreateDirectory(_tempDownloadDirectory);
    }

    /// <summary>
    /// Checks for available updates from GitHub releases.
    /// </summary>
    /// <returns>UpdateInfo if a newer version is available, null otherwise.</returns>
    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"{GitHubApiBaseUrl}/repos/{RepoOwner}/{RepoName}/releases/latest";
            Log.Debug("Checking for updates at: {Url}", url);

            var response = await _httpClient.GetAsync(url, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("GitHub API returned status code: {StatusCode}", response.StatusCode);
                return null;
            }

            var release = await response.Content.ReadFromJsonAsync<GitHubRelease>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                cancellationToken);

            if (release == null)
            {
                Log.Warning("Failed to parse GitHub release response");
                return null;
            }

            // Parse version from tag (remove 'v' prefix if present)
            var latestVersion = release.TagName.TrimStart('v');
            
            if (!IsNewerVersion(latestVersion, _currentVersion))
            {
                Log.Debug("Current version {Current} is up to date (latest: {Latest})", 
                    _currentVersion, latestVersion);
                return null;
            }

            // Find the installer asset
            var installerAsset = release.Assets?.FirstOrDefault(a => 
                a.Name.Contains(InstallerFilePattern, StringComparison.OrdinalIgnoreCase) &&
                a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

            if (installerAsset == null)
            {
                Log.Warning("No installer asset found in release {Version}", latestVersion);
                return null;
            }

            var updateInfo = new UpdateInfo
            {
                Version = latestVersion,
                DownloadUrl = installerAsset.BrowserDownloadUrl,
                ReleaseNotes = release.Body ?? string.Empty,
                PublishedAt = release.PublishedAt,
                FileSizeBytes = installerAsset.Size,
                ReleaseName = release.Name ?? $"v{latestVersion}"
            };

            Log.Information("Update available: {Version} (current: {Current})", 
                latestVersion, _currentVersion);

            UpdateAvailable?.Invoke(this, updateInfo);
            return updateInfo;
        }
        catch (HttpRequestException ex)
        {
            Log.Warning(ex, "Network error while checking for updates");
            return null;
        }
        catch (TaskCanceledException)
        {
            Log.Debug("Update check was cancelled");
            return null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unexpected error while checking for updates");
            return null;
        }
    }

    /// <summary>
    /// Downloads the update installer to a temporary directory.
    /// </summary>
    /// <param name="updateInfo">The update information containing the download URL.</param>
    /// <param name="progress">Optional progress reporter (0.0 to 1.0).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The full path to the downloaded installer file.</returns>
    public async Task<string> DownloadUpdateAsync(
        UpdateInfo updateInfo, 
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var fileName = $"PingTunnelVPN-Setup-{updateInfo.Version}.exe";
        var filePath = Path.Combine(_tempDownloadDirectory, fileName);

        // Check if already downloaded
        if (File.Exists(filePath))
        {
            var existingFile = new FileInfo(filePath);
            if (existingFile.Length == updateInfo.FileSizeBytes)
            {
                Log.Debug("Update installer already downloaded: {Path}", filePath);
                progress?.Report(1.0);
                DownloadCompleted?.Invoke(this, filePath);
                return filePath;
            }
            // Delete incomplete file
            File.Delete(filePath);
        }

        Log.Information("Downloading update from: {Url}", updateInfo.DownloadUrl);

        using var response = await _httpClient.GetAsync(
            updateInfo.DownloadUrl, 
            HttpCompletionOption.ResponseHeadersRead, 
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? updateInfo.FileSizeBytes;
        var downloadedBytes = 0L;

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = new FileStream(
            filePath, 
            FileMode.Create, 
            FileAccess.Write, 
            FileShare.None, 
            bufferSize: 81920, 
            useAsync: true);

        var buffer = new byte[81920];
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            downloadedBytes += bytesRead;

            var progressValue = totalBytes > 0 ? (double)downloadedBytes / totalBytes : 0;
            progress?.Report(progressValue);
            DownloadProgressChanged?.Invoke(this, progressValue);
        }

        Log.Information("Update downloaded successfully: {Path}", filePath);
        DownloadCompleted?.Invoke(this, filePath);
        return filePath;
    }

    /// <summary>
    /// Launches the installer and exits the current application.
    /// </summary>
    /// <param name="installerPath">The path to the downloaded installer.</param>
    public void LaunchInstallerAndExit(string installerPath)
    {
        if (!File.Exists(installerPath))
        {
            Log.Error("Installer file not found: {Path}", installerPath);
            throw new FileNotFoundException("Installer file not found", installerPath);
        }

        Log.Information("Launching installer: {Path}", installerPath);

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = installerPath,
                UseShellExecute = true,
                Verb = "runas" // Request admin elevation
            };

            Process.Start(startInfo);

            // Exit the application to allow the installer to run
            Log.Information("Exiting application for update installation");
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to launch installer");
            throw;
        }
    }

    /// <summary>
    /// Cleans up old downloaded installers.
    /// </summary>
    public void CleanupOldDownloads()
    {
        try
        {
            if (!Directory.Exists(_tempDownloadDirectory))
                return;

            var files = Directory.GetFiles(_tempDownloadDirectory, "*.exe");
            foreach (var file in files)
            {
                try
                {
                    var fileInfo = new FileInfo(file);
                    // Delete files older than 7 days
                    if (fileInfo.LastWriteTime < DateTime.Now.AddDays(-7))
                    {
                        File.Delete(file);
                        Log.Debug("Deleted old installer: {Path}", file);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to delete old installer: {Path}", file);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to cleanup old downloads");
        }
    }

    /// <summary>
    /// Compares two version strings to determine if the new version is newer.
    /// </summary>
    private static bool IsNewerVersion(string newVersion, string currentVersion)
    {
        try
        {
            // Handle versions like "1.0.0" or "1.0.0.0"
            var newParts = newVersion.Split('.').Select(int.Parse).ToArray();
            var currentParts = currentVersion.Split('.').Select(int.Parse).ToArray();

            // Normalize lengths
            var maxLength = Math.Max(newParts.Length, currentParts.Length);
            Array.Resize(ref newParts, maxLength);
            Array.Resize(ref currentParts, maxLength);

            for (int i = 0; i < maxLength; i++)
            {
                if (newParts[i] > currentParts[i])
                    return true;
                if (newParts[i] < currentParts[i])
                    return false;
            }

            return false; // Versions are equal
        }
        catch
        {
            // Fallback to string comparison if parsing fails
            return string.Compare(newVersion, currentVersion, StringComparison.OrdinalIgnoreCase) > 0;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _httpClient.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }

    #region GitHub API Models

    private class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("published_at")]
        public DateTime PublishedAt { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubAsset>? Assets { get; set; }
    }

    private class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = string.Empty;

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }

    #endregion
}
