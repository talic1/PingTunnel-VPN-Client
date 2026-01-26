using System.Diagnostics;
using System.Reflection;
using Serilog;

namespace PingTunnelVPN.Core;

/// <summary>
/// Manages external processes (pingtunnel and tun2socks).
/// </summary>
public class ProcessManager : IDisposable
{
    private Process? _pingTunnelProcess;
    private Process? _tun2socksProcess;
    private readonly object _lock = new();
    private bool _disposed;

    /// <summary>
    /// Event raised when a process outputs a line.
    /// </summary>
    public event EventHandler<ProcessOutputEventArgs>? ProcessOutput;

    /// <summary>
    /// Event raised when a managed process exits unexpectedly.
    /// </summary>
    public event EventHandler<ProcessExitEventArgs>? ProcessExited;

    /// <summary>
    /// Gets the directory where the executable is located.
    /// </summary>
    public static string AppDirectory
    {
        get
        {
            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(processPath))
            {
                var directory = Path.GetDirectoryName(processPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    return directory;
                }
            }

            return AppContext.BaseDirectory;
        }
    }

    /// <summary>
    /// Gets the Resources directory (where binaries are stored).
    /// </summary>
    public static string ResourcesDirectory => Path.Combine(AppDirectory, "Resources");

    /// <summary>
    /// Gets the path to a binary file.
    /// </summary>
    public static string GetBinaryPath(string binaryName)
    {
        // Only load third-party tools from the Resources folder.
        return Path.Combine(ResourcesDirectory, binaryName);
    }

    /// <summary>
    /// Ensures binaries are available (logs status).
    /// </summary>
    public static void EnsureBinaries()
    {
        Log.Information("App directory: {AppDir}", AppDirectory);
        Log.Information("Resources directory: {ResDir}", ResourcesDirectory);
        
        var binaries = new[] { "pingtunnel.exe", "tun2socks.exe", "wintun.dll" };
        
        foreach (var binary in binaries)
        {
            var path = GetBinaryPath(binary);
            if (File.Exists(path))
            {
                Log.Information("Binary found: {Binary} at {Path}", binary, path);
            }
            else
            {
                Log.Warning("Binary NOT found: {Binary} (expected at {Path})", binary, path);
            }
        }
    }

    /// <summary>
    /// Checks if all required binaries are available.
    /// </summary>
    public static bool AreBinariesAvailable()
    {
        var required = new[] { "pingtunnel.exe", "tun2socks.exe", "wintun.dll" };
        
        foreach (var binary in required)
        {
            var path = GetBinaryPath(binary);
            if (!File.Exists(path))
            {
                return false;
            }
        }
        
        return true;
    }

    /// <summary>
    /// Gets the missing binaries.
    /// </summary>
    public static List<string> GetMissingBinaries()
    {
        var required = new[] { "pingtunnel.exe", "tun2socks.exe", "wintun.dll" };
        var missing = new List<string>();
        
        foreach (var binary in required)
        {
            var path = GetBinaryPath(binary);
            if (!File.Exists(path))
            {
                missing.Add(binary);
            }
        }
        
        return missing;
    }

    /// <summary>
    /// Starts the pingtunnel client process.
    /// </summary>
    public void StartPingTunnel(string serverAddress, int localPort, string key, string? encryptMode = null, string? encryptKey = null)
    {
        lock (_lock)
        {
            if (_pingTunnelProcess != null && !_pingTunnelProcess.HasExited)
            {
                throw new InvalidOperationException("pingtunnel is already running");
            }

            var exePath = GetBinaryPath("pingtunnel.exe");
            if (!File.Exists(exePath))
            {
                throw new FileNotFoundException("pingtunnel.exe not found", exePath);
            }

            // Build arguments
            var args = $"-type client -l :{localPort} -s {serverAddress} -sock5 1";
            
            if (!string.IsNullOrWhiteSpace(key))
            {
                args += $" -key {key}";
            }

            if (!string.IsNullOrWhiteSpace(encryptMode) && encryptMode != "none")
            {
                args += $" -encrypt {encryptMode}";
                if (!string.IsNullOrWhiteSpace(encryptKey))
                {
                    args += $" -encrypt-key {encryptKey}";
                }
            }

            Log.Information("Starting pingtunnel: {Args}", args.Replace(key ?? "", "****"));

            _pingTunnelProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = ResourcesDirectory
                },
                EnableRaisingEvents = true
            };

            _pingTunnelProcess.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    Log.Debug("[pingtunnel] {Output}", e.Data);
                    ProcessOutput?.Invoke(this, new ProcessOutputEventArgs("pingtunnel", e.Data));
                }
            };

            _pingTunnelProcess.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    Log.Debug("[pingtunnel] {Output}", e.Data);
                    ProcessOutput?.Invoke(this, new ProcessOutputEventArgs("pingtunnel", e.Data));
                }
            };

            _pingTunnelProcess.Exited += (s, e) =>
            {
                var exitCode = _pingTunnelProcess?.ExitCode ?? -1;
                Log.Warning("pingtunnel exited with code {ExitCode}", exitCode);
                ProcessExited?.Invoke(this, new ProcessExitEventArgs("pingtunnel", exitCode));
            };

            _pingTunnelProcess.Start();
            _pingTunnelProcess.BeginOutputReadLine();
            _pingTunnelProcess.BeginErrorReadLine();

            Log.Information("pingtunnel started with PID {Pid}", _pingTunnelProcess.Id);
        }
    }

    /// <summary>
    /// Starts the tun2socks process.
    /// </summary>
    public void StartTun2Socks(int socksPort, int mtu = 1420, bool enableUdp = false, int udpTimeout = 60)
    {
        lock (_lock)
        {
            if (_tun2socksProcess != null && !_tun2socksProcess.HasExited)
            {
                throw new InvalidOperationException("tun2socks is already running");
            }

            var exePath = GetBinaryPath("tun2socks.exe");
            if (!File.Exists(exePath))
            {
                throw new FileNotFoundException("tun2socks.exe not found", exePath);
            }

            // Build arguments
            var args = $"-device wintun -proxy socks5://127.0.0.1:{socksPort} -mtu {mtu} -loglevel info";
            
            if (enableUdp)
            {
                args += $" -udp-timeout {udpTimeout}s";
            }

            Log.Information("Starting tun2socks: {Args}", args);

            _tun2socksProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = ResourcesDirectory
                },
                EnableRaisingEvents = true
            };

            _tun2socksProcess.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    Log.Debug("[tun2socks] {Output}", e.Data);
                    ProcessOutput?.Invoke(this, new ProcessOutputEventArgs("tun2socks", e.Data));
                }
            };

            _tun2socksProcess.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    Log.Debug("[tun2socks] {Output}", e.Data);
                    ProcessOutput?.Invoke(this, new ProcessOutputEventArgs("tun2socks", e.Data));
                }
            };

            _tun2socksProcess.Exited += (s, e) =>
            {
                var exitCode = _tun2socksProcess?.ExitCode ?? -1;
                Log.Warning("tun2socks exited with code {ExitCode}", exitCode);
                ProcessExited?.Invoke(this, new ProcessExitEventArgs("tun2socks", exitCode));
            };

            _tun2socksProcess.Start();
            _tun2socksProcess.BeginOutputReadLine();
            _tun2socksProcess.BeginErrorReadLine();

            Log.Information("tun2socks started with PID {Pid}", _tun2socksProcess.Id);
        }
    }

    /// <summary>
    /// Stops the pingtunnel process.
    /// </summary>
    public void StopPingTunnel()
    {
        lock (_lock)
        {
            if (_pingTunnelProcess != null)
            {
                try
                {
                    if (!_pingTunnelProcess.HasExited)
                    {
                        _pingTunnelProcess.Kill(true);
                        _pingTunnelProcess.WaitForExit(5000);
                        Log.Information("pingtunnel stopped");
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Error stopping pingtunnel");
                }
                finally
                {
                    _pingTunnelProcess.Dispose();
                    _pingTunnelProcess = null;
                }
            }
        }
    }

    /// <summary>
    /// Stops the tun2socks process.
    /// </summary>
    public void StopTun2Socks()
    {
        lock (_lock)
        {
            if (_tun2socksProcess != null)
            {
                try
                {
                    if (!_tun2socksProcess.HasExited)
                    {
                        _tun2socksProcess.Kill(true);
                        _tun2socksProcess.WaitForExit(5000);
                        Log.Information("tun2socks stopped");
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Error stopping tun2socks");
                }
                finally
                {
                    _tun2socksProcess.Dispose();
                    _tun2socksProcess = null;
                }
            }
        }
    }

    /// <summary>
    /// Stops all managed processes.
    /// </summary>
    public void StopAll()
    {
        StopTun2Socks();
        StopPingTunnel();
    }

    /// <summary>
    /// Checks if pingtunnel is running.
    /// </summary>
    public bool IsPingTunnelRunning => _pingTunnelProcess != null && !_pingTunnelProcess.HasExited;

    /// <summary>
    /// Checks if tun2socks is running.
    /// </summary>
    public bool IsTun2SocksRunning => _tun2socksProcess != null && !_tun2socksProcess.HasExited;

    /// <summary>
    /// Kills any orphaned pingtunnel/tun2socks processes from previous runs.
    /// </summary>
    public static void KillOrphanedProcesses()
    {
        var processNames = new[] { "pingtunnel", "tun2socks" };

        foreach (var name in processNames)
        {
            try
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    try
                    {
                        // Check if this is our process (in our Resources directory or app directory)
                        var fileName = process.MainModule?.FileName;
                        if (fileName != null &&
                            fileName.StartsWith(ResourcesDirectory, StringComparison.OrdinalIgnoreCase))
                        {
                            process.Kill(true);
                            Log.Information("Killed orphaned {Name} process (PID {Pid})", name, process.Id);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "Error checking/killing process {Name}", name);
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error enumerating {Name} processes", name);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        StopAll();
    }
}

/// <summary>
/// Event arguments for process output.
/// </summary>
public class ProcessOutputEventArgs : EventArgs
{
    public string ProcessName { get; }
    public string Output { get; }

    public ProcessOutputEventArgs(string processName, string output)
    {
        ProcessName = processName;
        Output = output;
    }
}

/// <summary>
/// Event arguments for process exit.
/// </summary>
public class ProcessExitEventArgs : EventArgs
{
    public string ProcessName { get; }
    public int ExitCode { get; }

    public ProcessExitEventArgs(string processName, int exitCode)
    {
        ProcessName = processName;
        ExitCode = exitCode;
    }
}
