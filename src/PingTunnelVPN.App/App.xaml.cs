using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using PingTunnelVPN.App.Logging;
using PingTunnelVPN.Core;
using PingTunnelVPN.Platform;
using Serilog;

namespace PingTunnelVPN.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private TrayIconManager? _trayIconManager;
    private static string _crashLogPath = string.Empty;
    private static Mutex? _singleInstanceMutex;

    // Windows API declarations for window restoration
    [DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private const int SW_RESTORE = 9;
    private const int SW_SHOW = 5;

    public App()
    {
        // Set up crash log path immediately - use app directory
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        _crashLogPath = Path.Combine(appDir, "crash.log");
        
        // Set up global exception handlers FIRST
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            WriteCrashLog("Application starting...");
            
            // Check for existing instance
            if (!EnsureSingleInstance())
            {
                WriteCrashLog("Another instance detected, exiting...");
                Shutdown(0);
                return;
            }
            
            // Set working directory to app directory (important when running as admin)
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            Environment.CurrentDirectory = appDir;
            
            WriteCrashLog($"App directory: {appDir}");
            WriteCrashLog($"Working directory set to: {Environment.CurrentDirectory}");

            // Configure logging
            ConfigureLogging();
            
            WriteCrashLog("Logging configured");

            base.OnStartup(e);

            // Check binaries availability
            EnsureBinaries();
            
            WriteCrashLog("Binaries check completed");

            // Check for crash recovery on startup
            PerformCrashRecovery();
            
            WriteCrashLog("Crash recovery check completed");

            // Cleanup any orphaned firewall rules from previous crashes
            CleanupOrphanedFirewallRules();
            
            WriteCrashLog("Firewall orphan cleanup completed");

            Log.Information("PingTunnelVPN application starting");
            WriteCrashLog("Startup completed successfully");
        }
        catch (Exception ex)
        {
            WriteCrashLog($"FATAL ERROR during startup: {ex}");
            MessageBox.Show($"Failed to start application:\n\n{ex.Message}\n\nSee crash.log in app directory for details.",
                "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void ConfigureLogging()
    {
        try
        {
            // Primary log path in LocalAppData
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PingTunnelVPN",
                "logs",
                "pingtunnelvpn-.log");

            // Ensure directory exists
            var logDir = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
            {
                Directory.CreateDirectory(logDir);
            }

            // Also log to app directory for easy access
            var appLogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.log");

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(logPath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7,
                    shared: true,
                    flushToDiskInterval: TimeSpan.FromSeconds(1),
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.File(appLogPath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 3,
                    shared: true,
                    flushToDiskInterval: TimeSpan.FromSeconds(1),
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.Sink(new UiLogSink())
                .CreateLogger();

            WriteCrashLog($"Logging initialized. Logs at: {logPath} and {appLogPath}");
            Log.Information("Logging initialized. Logs at: {LogPath} and {AppLogPath}", logPath, appLogPath);
        }
        catch (Exception ex)
        {
            WriteCrashLog($"Failed to configure logging: {ex}");
            throw;
        }
    }

    private void EnsureBinaries()
    {
        try
        {
            Log.Information("Checking binaries...");

            EmbeddedResourceHelper.EnsureResourcesPresent();
            
            ProcessManager.EnsureBinaries();
            
            // Check if binaries are available
            if (ProcessManager.AreBinariesAvailable())
            {
                Log.Information("All binaries are available");
            }
            else
            {
                var missing = ProcessManager.GetMissingBinaries();
                Log.Warning("Missing binaries: {Missing}", string.Join(", ", missing));
                Log.Warning("Expected location: {Path}", ProcessManager.ResourcesDirectory);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error checking binaries");
            WriteCrashLog($"Error checking binaries: {ex}");
        }
    }

    private void PerformCrashRecovery()
    {
        try
        {
            var recoveryManager = new CrashRecoveryManager();
            if (recoveryManager.NeedsRecovery())
            {
                Log.Warning("Detected unclean shutdown, performing recovery...");
                WriteCrashLog("Performing crash recovery...");
                recoveryManager.PerformRecovery();
                Log.Information("Crash recovery completed");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during crash recovery");
            WriteCrashLog($"Error during crash recovery: {ex}");
        }
    }

    private void CleanupOrphanedFirewallRules()
    {
        try
        {
            FirewallManager.CleanupOrphanedRules();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error cleaning up orphaned firewall rules");
            WriteCrashLog($"Error cleaning up orphaned firewall rules: {ex}");
        }
    }

    public void SetTrayIconManager(TrayIconManager manager)
    {
        _trayIconManager = manager;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("PingTunnelVPN application exiting");
        _trayIconManager?.Dispose();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        var message = ex?.ToString() ?? "Unknown exception";
        
        WriteCrashLog($"UNHANDLED EXCEPTION (IsTerminating={e.IsTerminating}):\n{message}");
        Log.Fatal(ex, "Unhandled exception - application terminating");
        Log.CloseAndFlush();
        
        if (e.IsTerminating)
        {
            MessageBox.Show($"A fatal error occurred:\n\n{ex?.Message}\n\nSee crash.log for details.",
                "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrashLog($"DISPATCHER EXCEPTION:\n{e.Exception}");
        Log.Error(e.Exception, "Dispatcher unhandled exception");
        
        MessageBox.Show($"An error occurred:\n\n{e.Exception.Message}\n\nThe application will attempt to continue.\nSee crash.log for details.",
            "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        
        e.Handled = true; // Prevent app from crashing for non-fatal errors
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteCrashLog($"UNOBSERVED TASK EXCEPTION:\n{e.Exception}");
        Log.Error(e.Exception, "Unobserved task exception");
        e.SetObserved(); // Prevent app from crashing
    }

    private static void WriteCrashLog(string message)
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var logEntry = $"[{timestamp}] {message}\n";
            File.AppendAllText(_crashLogPath, logEntry);
        }
        catch
        {
            // Can't write to crash log - nothing we can do
        }
    }

    /// <summary>
    /// Ensures only one instance of the application is running.
    /// If another instance exists, restores its window and returns false.
    /// </summary>
    private static bool EnsureSingleInstance()
    {
        const string mutexName = "PingTunnelVPN_SingleInstance_Mutex";
        _singleInstanceMutex = new Mutex(true, mutexName, out bool isNewInstance);

        if (!isNewInstance)
        {
            // Another instance is running, try to restore its window
            RestoreExistingInstance();
            return false;
        }

        return true;
    }

    /// <summary>
    /// Finds and restores the window of the existing application instance.
    /// </summary>
    private static void RestoreExistingInstance()
    {
        try
        {
            IntPtr hWnd = IntPtr.Zero;
            var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
            var processes = System.Diagnostics.Process.GetProcessesByName(currentProcess.ProcessName);
            
            // Find the other instance's process
            System.Diagnostics.Process? otherProcess = null;
            foreach (var process in processes)
            {
                if (process.Id != currentProcess.Id)
                {
                    otherProcess = process;
                    break;
                }
            }

            if (otherProcess == null)
            {
                WriteCrashLog("Could not find other process instance");
                return;
            }

            // Try to get the main window handle first
            hWnd = otherProcess.MainWindowHandle;

            // If main window handle is zero (window might be hidden), enumerate windows
            if (hWnd == IntPtr.Zero)
            {
                IntPtr foundWindow = IntPtr.Zero;

                EnumWindows((windowHandle, lParam) =>
                {
                    GetWindowThreadProcessId(windowHandle, out uint processId);
                    if (processId == otherProcess.Id)
                    {
                        // Check if this window has the title we're looking for
                        var sb = new System.Text.StringBuilder(256);
                        GetWindowText(windowHandle, sb, sb.Capacity);
                        if (sb.ToString().Contains("PingTunnelVPN"))
                        {
                            foundWindow = windowHandle;
                            return false; // Stop enumeration
                        }
                    }
                    return true; // Continue enumeration
                }, IntPtr.Zero);

                hWnd = foundWindow;
            }

            // If still not found, try FindWindow as fallback
            if (hWnd == IntPtr.Zero)
            {
                hWnd = FindWindow(null, "PingTunnelVPN");
            }

            if (hWnd != IntPtr.Zero)
            {
                // Restore window if minimized
                if (IsIconic(hWnd))
                {
                    ShowWindow(hWnd, SW_RESTORE);
                }
                else
                {
                    // Show window even if it's hidden
                    ShowWindow(hWnd, SW_SHOW);
                }

                // Bring window to foreground
                SetForegroundWindow(hWnd);
            }
            else
            {
                WriteCrashLog("Could not find window handle for existing instance");
            }
        }
        catch (Exception ex)
        {
            // Log but don't fail - the new instance will still exit
            WriteCrashLog($"Failed to restore existing instance window: {ex.Message}");
        }
    }
}
