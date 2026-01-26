using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using PingTunnelVPN.App.Models;
using PingTunnelVPN.App.Logging;
using PingTunnelVPN.Core;

namespace PingTunnelVPN.App.ViewModels;

/// <summary>
/// Main view model for the application.
/// </summary>
public class MainViewModel : INotifyPropertyChanged
{
    private readonly ConfigManager _configManager;
    private readonly ObservableCollection<string> _logs = new();
    private readonly ObservableCollection<LogEntry> _logEntries = new();
    private readonly object _logLock = new();
    private readonly ConcurrentQueue<LogEntry> _pendingEntries = new();
    private int _flushScheduled;
    private const int MaxLogEntries = 1000;
    private bool _isLoadingSettings = false; // Flag to prevent saving during initial load

    private readonly ObservableCollection<ServerConfigViewModel> _configs = new();
    private ServerConfigViewModel? _selectedConfig;
    private Guid? _connectedConfigId;

    // Server-specific settings (per config)
    private string _serverAddress = string.Empty;
    private string _serverKey = string.Empty;
    private int _localSocksPort = 1080;
    
    // Global settings (loaded from and saved to _configManager.GlobalSettings)
    private int _mtu = 1420;
    private DnsMode _dnsMode = DnsMode.TunnelDns;
    private string _dnsServersText = "1.1.1.1\n8.8.8.8";
    private string _bypassSubnetsText = "192.168.0.0/16\n10.0.0.0/8\n172.16.0.0/12";
    private bool _enableUdp = true;
    private bool _killSwitch;
    private bool _autoConnect;
    private bool _minimizeToTray = true;
    private bool _startMinimized;
    private string _encryptionMode = "none";
    private string _encryptionKey = string.Empty;
    private int _udpTimeout = 60;
    private bool _autoRestartOnHighLatency = true;
    private int _latencyThresholdMs = 1000;
    private int _highLatencyCountThreshold = 5;
    private int _restartCooldownSeconds = 30;
    private int _maxAutoRestarts = 3;
    private string _logSearchText = string.Empty;
    private string _logLevel = "WARN";

    // Traffic stats properties for UI binding (stored as bytes per second)
    private double _tunnelDownBps;
    private double _tunnelUpBps;
    private double _physicalDownBps;
    private double _physicalUpBps;
    private int _selectedTabIndex;

    public MainViewModel(ConfigManager configManager)
    {
        _configManager = configManager;
        
        // Subscribe to config changes
        _configManager.SelectedConfigChanged += OnSelectedConfigChanged;
        
        LoadConfigs();
        LoadConfig();
        LoadGlobalSettings();

        // Subscribe to Serilog output for logging
        SetupLogging();
    }

    private void OnSelectedConfigChanged(object? sender, ServerConfig? config)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            UpdateSelectedConfig(config?.Id);
            if (config != null)
            {
                LoadConfig();
            }
        });
    }

    private void SetupLogging()
    {
        // Seed UI with the most recent log events and then subscribe for live updates.
        foreach (var entry in LogEventHub.SnapshotBacklog())
        {
            EnqueueLog(entry);
        }

        LogEventHub.LogEntryReceived += OnLogEntryReceived;
    }

    private void OnLogEntryReceived(LogEntry entry)
    {
        EnqueueLog(entry);
    }

    private void EnqueueLog(LogEntry entry)
    {
        _pendingEntries.Enqueue(entry);
        ScheduleLogFlush();
    }

    private void ScheduleLogFlush()
    {
        if (Interlocked.Exchange(ref _flushScheduled, 1) == 1)
            return;

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            FlushPendingLogs();
            return;
        }

        dispatcher.BeginInvoke(new Action(FlushPendingLogs), System.Windows.Threading.DispatcherPriority.Background);
    }

    public void AddLog(string message)
    {
        EnqueueLog(LogEntry.Parse(message));
    }

    private void FlushPendingLogs()
    {
        try
        {
            var processed = 0;

            lock (_logLock)
            {
                while (_pendingEntries.TryDequeue(out var entry))
                {
                    _logs.Add(entry.RawLine);
                    _logEntries.Add(entry);
                    processed++;

                    if (_logs.Count > MaxLogEntries)
                    {
                        var removeCount = _logs.Count - MaxLogEntries;
                        for (var i = 0; i < removeCount; i++)
                        {
                            _logs.RemoveAt(0);
                            _logEntries.RemoveAt(0);
                        }
                    }

                    if (processed >= 200)
                        break;
                }
            }

            if (processed > 0)
            {
                OnPropertyChanged(nameof(FilteredLogs));
                OnPropertyChanged(nameof(FilteredLogEntries));
            }
        }
        finally
        {
            Interlocked.Exchange(ref _flushScheduled, 0);

            if (!_pendingEntries.IsEmpty)
            {
                ScheduleLogFlush();
            }
        }
    }

    public void ClearLogs()
    {
        while (_pendingEntries.TryDequeue(out _))
        {
        }

        lock (_logLock)
        {
            _logs.Clear();
            _logEntries.Clear();
            OnPropertyChanged(nameof(FilteredLogs));
            OnPropertyChanged(nameof(FilteredLogEntries));
        }
    }

    public void ExportLogs(string filePath)
    {
        lock (_logLock)
        {
            File.WriteAllLines(filePath, _logs);
        }
    }

    public ObservableCollection<string> FilteredLogs
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_logSearchText))
                return _logs;

            var filtered = new ObservableCollection<string>(
                _logs.Where(l => l.Contains(_logSearchText, StringComparison.OrdinalIgnoreCase)));
            return filtered;
        }
    }

    /// <summary>
    /// Gets the structured log entries for the new UI.
    /// </summary>
    public ObservableCollection<LogEntry> FilteredLogEntries
    {
        get
        {
            var entries = _logEntries.Where(ShouldShowLogEntry);

            if (!string.IsNullOrWhiteSpace(_logSearchText))
            {
                entries = entries.Where(l => 
                    l.Message.Contains(_logSearchText, StringComparison.OrdinalIgnoreCase) ||
                    l.Level.Contains(_logSearchText, StringComparison.OrdinalIgnoreCase));
            }

            return new ObservableCollection<LogEntry>(entries);
        }
    }

    /// <summary>
    /// Updates traffic stats from connection stats (bytes per second).
    /// </summary>
    public void UpdateTrafficStats(double tunnelRxBps, double tunnelTxBps, double physicalRxBps, double physicalTxBps)
    {
        TunnelDownBps = tunnelRxBps;
        TunnelUpBps = tunnelTxBps;
        PhysicalDownBps = physicalRxBps;
        PhysicalUpBps = physicalTxBps;
    }

    /// <summary>
    /// Loads all configurations from ConfigManager.
    /// </summary>
    public void LoadConfigs()
    {
        var allConfigs = _configManager.GetAllConfigs();
        var selectedId = _configManager.GetSelectedConfigId();
        var connectedId = _connectedConfigId;

        _configs.Clear();
        foreach (var config in allConfigs)
        {
            var vm = new ServerConfigViewModel(config);
            vm.IsSelected = config.Id == selectedId;
            vm.IsConnected = config.Id == connectedId;
            _configs.Add(vm);
        }

        UpdateSelectedConfig(selectedId);
        OnPropertyChanged(nameof(Configs));
    }

    /// <summary>
    /// Updates which config is marked as selected.
    /// </summary>
    private void UpdateSelectedConfig(Guid? configId)
    {
        foreach (var config in _configs)
        {
            config.IsSelected = config.Id == configId;
        }

        SelectedConfig = _configs.FirstOrDefault(c => c.Id == configId);
    }

    /// <summary>
    /// Updates which config is marked as connected.
    /// </summary>
    public void UpdateConnectedConfig(Guid? configId)
    {
        _connectedConfigId = configId;
        foreach (var config in _configs)
        {
            config.IsConnected = config.Id == configId;
        }
    }

    /// <summary>
    /// Loads the selected configuration into the form fields (server-specific settings only).
    /// </summary>
    public void LoadConfig()
    {
        _isLoadingSettings = true;
        try
        {
            var config = _configManager.CurrentConfig;
            
            ServerAddress = config.ServerAddress;
            ServerKey = config.ServerKey;
            LocalSocksPort = config.LocalSocksPort;
        }
        finally
        {
            _isLoadingSettings = false;
        }
    }

    /// <summary>
    /// Loads global settings into the form fields.
    /// </summary>
    public void LoadGlobalSettings()
    {
        _isLoadingSettings = true;
        try
        {
            var settings = _configManager.GetGlobalSettings();
            
            Mtu = settings.Mtu;
            DnsMode = settings.DnsMode;
            DnsServersText = string.Join("\n", settings.DnsServers);
            BypassSubnetsText = string.Join("\n", settings.BypassSubnets);
            EnableUdp = settings.EnableUdp;
            UdpTimeout = settings.UdpTimeout;
            KillSwitch = settings.KillSwitch;
            EncryptionMode = settings.EncryptionMode;
            EncryptionKey = settings.EncryptionKey;
            AutoConnect = settings.AutoConnect;
            MinimizeToTray = settings.MinimizeToTray;
            StartMinimized = settings.StartMinimized;
            AutoRestartOnHighLatency = settings.AutoRestartOnHighLatency;
            LatencyThresholdMs = settings.LatencyThresholdMs;
            HighLatencyCountThreshold = settings.HighLatencyCountThreshold;
            RestartCooldownSeconds = settings.RestartCooldownSeconds;
            MaxAutoRestarts = settings.MaxAutoRestarts;
        }
        finally
        {
            _isLoadingSettings = false;
        }
    }

    /// <summary>
    /// Saves the current form values to the selected configuration (server-specific settings only).
    /// </summary>
    public void SaveConfig()
    {
        var selectedId = _configManager.GetSelectedConfigId();
        if (!selectedId.HasValue)
        {
            // No config selected, skip saving (user should create config via dialog)
            return;
        }

        _configManager.UpdateConfig(selectedId.Value, serverConfig =>
        {
            serverConfig.Configuration.ServerAddress = ServerAddress;
            serverConfig.Configuration.ServerKey = ServerKey;
            serverConfig.Configuration.LocalSocksPort = LocalSocksPort;
        });

        LoadConfigs();
    }

    /// <summary>
    /// Saves the current form values to global settings.
    /// </summary>
    public void SaveGlobalSettings()
    {
        _configManager.UpdateGlobalSettings(settings =>
        {
            settings.Mtu = Mtu;
            settings.DnsMode = DnsMode;
            settings.DnsServers = DnsServersText
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();
            settings.BypassSubnets = BypassSubnetsText
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();
            settings.EnableUdp = EnableUdp;
            settings.UdpTimeout = UdpTimeout;
            settings.KillSwitch = KillSwitch;
            settings.EncryptionMode = EncryptionMode;
            settings.EncryptionKey = EncryptionKey;
            settings.AutoConnect = AutoConnect;
            settings.MinimizeToTray = MinimizeToTray;
            settings.StartMinimized = StartMinimized;
            settings.AutoRestartOnHighLatency = AutoRestartOnHighLatency;
            settings.LatencyThresholdMs = LatencyThresholdMs;
            settings.HighLatencyCountThreshold = HighLatencyCountThreshold;
            settings.RestartCooldownSeconds = RestartCooldownSeconds;
            settings.MaxAutoRestarts = MaxAutoRestarts;
        });
    }

    /// <summary>
    /// Saves the current form values as a new configuration (server-specific settings only).
    /// </summary>
    public ServerConfigViewModel SaveCurrentConfigAsNew()
    {
        var config = new VpnConfiguration
        {
            ServerAddress = ServerAddress,
            ServerKey = ServerKey,
            LocalSocksPort = LocalSocksPort
        };

        var serverConfig = new ServerConfig
        {
            Name = string.IsNullOrWhiteSpace(ServerAddress) ? "New Config" : $"{ServerAddress}",
            Configuration = config
        };

        var added = _configManager.AddConfig(serverConfig);
        _configManager.SetSelectedConfig(added.Id);
        
        LoadConfigs();
        return _configs.First(c => c.Id == added.Id);
    }

    /// <summary>
    /// Adds a new empty configuration.
    /// </summary>
    public ServerConfigViewModel AddNewConfig()
    {
        var config = new ServerConfig
        {
            Name = "New Config",
            Configuration = new VpnConfiguration()
        };

        var added = _configManager.AddConfig(config);
        _configManager.SetSelectedConfig(added.Id);
        
        LoadConfigs();
        LoadConfig();
        return _configs.First(c => c.Id == added.Id);
    }

    /// <summary>
    /// Deletes a configuration.
    /// </summary>
    public void DeleteConfig(Guid id)
    {
        _configManager.DeleteConfig(id);
        LoadConfigs();
        LoadConfig();
    }

    /// <summary>
    /// Selects a configuration.
    /// </summary>
    public void SelectConfig(Guid id)
    {
        _configManager.SetSelectedConfig(id);
        LoadConfigs();
        LoadConfig();
    }

    #region Config Management Properties

    /// <summary>
    /// Gets all server configurations.
    /// </summary>
    public ObservableCollection<ServerConfigViewModel> Configs => _configs;

    /// <summary>
    /// Gets or sets the currently selected configuration.
    /// </summary>
    public ServerConfigViewModel? SelectedConfig
    {
        get => _selectedConfig;
        set
        {
            if (_selectedConfig != value)
            {
                _selectedConfig = value;
                OnPropertyChanged();
            }
        }
    }

    #endregion

    #region Properties

    public string ServerAddress
    {
        get => _serverAddress;
        set
        {
            if (_serverAddress != value)
            {
                _serverAddress = value;
                OnPropertyChanged();
                // Save server-specific config immediately (unless loading)
                if (!_isLoadingSettings)
                {
                    SaveConfig();
                }
            }
        }
    }

    public string ServerKey
    {
        get => _serverKey;
        set
        {
            if (_serverKey != value)
            {
                _serverKey = value;
                OnPropertyChanged();
                // Save server-specific config immediately (unless loading)
                if (!_isLoadingSettings)
                {
                    SaveConfig();
                }
            }
        }
    }

    public int LocalSocksPort
    {
        get => _localSocksPort;
        set
        {
            if (_localSocksPort != value)
            {
                _localSocksPort = value;
                OnPropertyChanged();
                // Save server-specific config immediately (unless loading)
                if (!_isLoadingSettings)
                {
                    SaveConfig();
                }
            }
        }
    }

    public int Mtu
    {
        get => _mtu;
        set
        {
            if (_mtu != value)
            {
                _mtu = value;
                OnPropertyChanged();
                if (!_isLoadingSettings)
                {
                    SaveGlobalSettings();
                }
            }
        }
    }

    public DnsMode DnsMode
    {
        get => _dnsMode;
        set
        {
            if (_dnsMode != value)
            {
                _dnsMode = value;
                OnPropertyChanged();
                if (!_isLoadingSettings)
                {
                    SaveGlobalSettings();
                }
            }
        }
    }

    public string DnsServersText
    {
        get => _dnsServersText;
        set
        {
            if (_dnsServersText != value)
            {
                _dnsServersText = value;
                OnPropertyChanged();
                if (!_isLoadingSettings)
                {
                    SaveGlobalSettings();
                }
            }
        }
    }

    public string BypassSubnetsText
    {
        get => _bypassSubnetsText;
        set
        {
            if (_bypassSubnetsText != value)
            {
                _bypassSubnetsText = value;
                OnPropertyChanged();
                if (!_isLoadingSettings)
                {
                    SaveGlobalSettings();
                }
            }
        }
    }

    public bool EnableUdp
    {
        get => _enableUdp;
        set
        {
            if (_enableUdp != value)
            {
                _enableUdp = value;
                OnPropertyChanged();
                if (!_isLoadingSettings)
                {
                    SaveGlobalSettings();
                }
            }
        }
    }

    public bool KillSwitch
    {
        get => _killSwitch;
        set
        {
            if (_killSwitch != value)
            {
                _killSwitch = value;
                OnPropertyChanged();
                if (!_isLoadingSettings)
                {
                    SaveGlobalSettings();
                }
            }
        }
    }

    public bool AutoConnect
    {
        get => _autoConnect;
        set
        {
            if (_autoConnect != value)
            {
                _autoConnect = value;
                OnPropertyChanged();
                if (!_isLoadingSettings)
                {
                    SaveGlobalSettings();
                }
            }
        }
    }

    public bool MinimizeToTray
    {
        get => _minimizeToTray;
        set
        {
            if (_minimizeToTray != value)
            {
                _minimizeToTray = value;
                OnPropertyChanged();
                if (!_isLoadingSettings)
                {
                    SaveGlobalSettings();
                }
            }
        }
    }

    public bool StartMinimized
    {
        get => _startMinimized;
        set
        {
            if (_startMinimized != value)
            {
                _startMinimized = value;
                OnPropertyChanged();
                if (!_isLoadingSettings)
                {
                    SaveGlobalSettings();
                }
            }
        }
    }

    public string EncryptionMode
    {
        get => _encryptionMode;
        set
        {
            if (_encryptionMode != value)
            {
                _encryptionMode = value;
                OnPropertyChanged();
                if (!_isLoadingSettings)
                {
                    SaveGlobalSettings();
                }
            }
        }
    }

    public string EncryptionKey
    {
        get => _encryptionKey;
        set
        {
            if (_encryptionKey != value)
            {
                _encryptionKey = value;
                OnPropertyChanged();
                if (!_isLoadingSettings)
                {
                    SaveGlobalSettings();
                }
            }
        }
    }

    public int UdpTimeout
    {
        get => _udpTimeout;
        set
        {
            if (_udpTimeout != value)
            {
                _udpTimeout = value;
                OnPropertyChanged();
                if (!_isLoadingSettings)
                {
                    SaveGlobalSettings();
                }
            }
        }
    }

    public bool AutoRestartOnHighLatency
    {
        get => _autoRestartOnHighLatency;
        set
        {
            if (_autoRestartOnHighLatency != value)
            {
                _autoRestartOnHighLatency = value;
                OnPropertyChanged();
                if (!_isLoadingSettings)
                {
                    SaveGlobalSettings();
                }
            }
        }
    }

    public int LatencyThresholdMs
    {
        get => _latencyThresholdMs;
        set
        {
            if (_latencyThresholdMs != value)
            {
                _latencyThresholdMs = value;
                OnPropertyChanged();
                if (!_isLoadingSettings)
                {
                    SaveGlobalSettings();
                }
            }
        }
    }

    public int HighLatencyCountThreshold
    {
        get => _highLatencyCountThreshold;
        set
        {
            if (_highLatencyCountThreshold != value)
            {
                _highLatencyCountThreshold = value;
                OnPropertyChanged();
                if (!_isLoadingSettings)
                {
                    SaveGlobalSettings();
                }
            }
        }
    }

    public int RestartCooldownSeconds
    {
        get => _restartCooldownSeconds;
        set
        {
            if (_restartCooldownSeconds != value)
            {
                _restartCooldownSeconds = value;
                OnPropertyChanged();
                if (!_isLoadingSettings)
                {
                    SaveGlobalSettings();
                }
            }
        }
    }

    public int MaxAutoRestarts
    {
        get => _maxAutoRestarts;
        set
        {
            if (_maxAutoRestarts != value)
            {
                _maxAutoRestarts = value;
                OnPropertyChanged();
                if (!_isLoadingSettings)
                {
                    SaveGlobalSettings();
                }
            }
        }
    }

    public string LogSearchText
    {
        get => _logSearchText;
        set
        {
            if (_logSearchText != value)
            {
                _logSearchText = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FilteredLogs));
                OnPropertyChanged(nameof(FilteredLogEntries));
            }
        }
    }

    public string LogLevel
    {
        get => _logLevel;
        set
        {
            if (_logLevel != value)
            {
                _logLevel = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FilteredLogs));
                OnPropertyChanged(nameof(FilteredLogEntries));
                UpdateSerilogLevel();
            }
        }
    }

    private static readonly Dictionary<string, int> LogLevelPriority = new()
    {
        { "DEBUG", 0 },
        { "INFO", 1 },
        { "WARN", 2 },
        { "WARNING", 2 },
        { "ERROR", 3 },
        { "FATAL", 4 }
    };

    private void UpdateSerilogLevel()
    {
        // Update Serilog minimum level based on selection
        var level = _logLevel switch
        {
            "DEBUG" => Serilog.Events.LogEventLevel.Debug,
            "INFO" => Serilog.Events.LogEventLevel.Information,
            "WARN" => Serilog.Events.LogEventLevel.Warning,
            "ERROR" => Serilog.Events.LogEventLevel.Error,
            "FATAL" => Serilog.Events.LogEventLevel.Fatal,
            _ => Serilog.Events.LogEventLevel.Warning
        };
        
        // Note: Serilog doesn't support runtime level changes easily without LoggingLevelSwitch
        // This would require additional setup in the logging configuration
    }

    private bool ShouldShowLogEntry(LogEntry entry)
    {
        if (!LogLevelPriority.TryGetValue(entry.Level.ToUpperInvariant(), out var entryPriority))
            entryPriority = 1; // Default to INFO if unknown

        if (!LogLevelPriority.TryGetValue(_logLevel.ToUpperInvariant(), out var filterPriority))
            filterPriority = 2; // Default to WARN

        return entryPriority >= filterPriority;
    }

    #endregion

    #region Traffic Stats Properties

    public double TunnelDownBps
    {
        get => _tunnelDownBps;
        set
        {
            if (Math.Abs(_tunnelDownBps - value) > 0.01)
            {
                _tunnelDownBps = value;
                OnPropertyChanged();
            }
        }
    }

    public double TunnelUpBps
    {
        get => _tunnelUpBps;
        set
        {
            if (Math.Abs(_tunnelUpBps - value) > 0.01)
            {
                _tunnelUpBps = value;
                OnPropertyChanged();
            }
        }
    }

    public double PhysicalDownBps
    {
        get => _physicalDownBps;
        set
        {
            if (Math.Abs(_physicalDownBps - value) > 0.01)
            {
                _physicalDownBps = value;
                OnPropertyChanged();
            }
        }
    }

    public double PhysicalUpBps
    {
        get => _physicalUpBps;
        set
        {
            if (Math.Abs(_physicalUpBps - value) > 0.01)
            {
                _physicalUpBps = value;
                OnPropertyChanged();
            }
        }
    }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            if (_selectedTabIndex != value)
            {
                _selectedTabIndex = value;
                OnPropertyChanged();
            }
        }
    }

    #endregion

    #region INotifyPropertyChanged

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    #endregion
}
