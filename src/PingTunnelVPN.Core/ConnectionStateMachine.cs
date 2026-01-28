using System.Net;
using PingTunnelVPN.Platform;
using Serilog;

namespace PingTunnelVPN.Core;

/// <summary>
/// Manages the VPN connection lifecycle with robust state transitions.
/// </summary>
public class ConnectionStateMachine : IDisposable
{
    private readonly ConfigManager _configManager;
    private readonly GlobalSettings _globalSettings;
    private readonly ProcessManager _processManager;
    private readonly RouteManager _routeManager;
    private readonly DnsManager _dnsManager;
    private readonly CrashRecoveryManager _recoveryManager;
    private readonly FirewallManager _firewallManager;
    private DnsForwarder? _dnsForwarder;
    
    private ConnectionState _currentState = ConnectionState.Disconnected;
    private readonly object _stateLock = new();
    private CancellationTokenSource? _healthCheckCts;
    private Task? _healthCheckTask;
    private CancellationTokenSource? _trafficCts;
    private Task? _trafficTask;
    private bool _disposed;

    // Traffic polling: last snapshot and baseline for session totals
    private long _trafficTunRx;
    private long _trafficTunTx;
    private long _trafficPhysRx;
    private long _trafficPhysTx;
    private DateTime _trafficSnapshotAt;
    private bool _trafficHasBaseline;
    private long _trafficBaselineTunRx;
    private long _trafficBaselineTunTx;
    private long _trafficBaselinePhysRx;
    private long _trafficBaselinePhysTx;

    // Stored state for cleanup
    private IPAddress? _serverIp;
    private uint _originalDefaultInterfaceIndex;
    private string? _originalDefaultGateway;
    private uint _tunInterfaceIndex;
    private ConnectionStats _stats = new();

    // Auto-restart state tracking
    private DateTime _lastRestartTime = DateTime.MinValue;
    private int _autoRestartCount = 0;
    private bool _isRestarting = false;

    // Currently connected config ID
    private Guid? _connectedConfigId;

    // Latency monitoring defaults (can be overridden by config)
    private const double DefaultLatencyWarningThresholdMs = 500;
    private const double DefaultLatencyCriticalThresholdMs = 2000;

    /// <summary>
    /// TUN adapter IP configuration.
    /// </summary>
    private const string TUN_ADDRESS = "198.18.0.2";
    private const string TUN_GATEWAY = "198.18.0.1";
    private const int TUN_PREFIX = 24;

    /// <summary>
    /// Event raised when connection state changes.
    /// </summary>
    public event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Event raised when connection stats are updated.
    /// </summary>
    public event EventHandler<ConnectionStats>? StatsUpdated;

    /// <summary>
    /// Gets the current connection state.
    /// </summary>
    public ConnectionState CurrentState
    {
        get
        {
            lock (_stateLock)
            {
                return _currentState;
            }
        }
    }

    /// <summary>
    /// Gets the current connection stats.
    /// </summary>
    public ConnectionStats Stats => _stats;

    /// <summary>
    /// Gets the ID of the currently connected configuration, or null if not connected.
    /// </summary>
    public Guid? ConnectedConfigId => _connectedConfigId;

    public ConnectionStateMachine(ConfigManager configManager)
    {
        _configManager = configManager;
        _globalSettings = configManager.GetGlobalSettings();
        _processManager = new ProcessManager();
        _routeManager = new RouteManager();
        _dnsManager = new DnsManager();
        _recoveryManager = new CrashRecoveryManager();
        _firewallManager = new FirewallManager();

        _processManager.ProcessExited += OnProcessExited;
        _processManager.ProcessOutput += OnProcessOutput;
    }

    /// <summary>
    /// Initiates a VPN connection.
    /// </summary>
    public async Task ConnectAsync()
    {
        lock (_stateLock)
        {
            if (_currentState != ConnectionState.Disconnected && _currentState != ConnectionState.Error)
            {
                throw new InvalidOperationException($"Cannot connect from state: {_currentState}");
            }
            SetState(ConnectionState.Connecting, "Initializing connection...");
        }

        // Reset auto-restart counters on new connection
        _autoRestartCount = 0;
        _lastRestartTime = DateTime.MinValue;
        _isRestarting = false;

        try
        {
            var selectedConfig = _configManager.GetSelectedConfig();
            if (selectedConfig == null)
            {
                throw new InvalidOperationException("No configuration selected");
            }

            var config = selectedConfig.Configuration;
            _connectedConfigId = selectedConfig.Id;

            // Validate configuration
            var errors = config.Validate();
            errors.AddRange(_globalSettings.Validate());
            if (errors.Count > 0)
            {
                throw new InvalidOperationException($"Configuration errors: {string.Join(", ", errors)}");
            }

            // Step 1: Check if elevated
            if (!ElevationHelper.IsElevated())
            {
                throw new UnauthorizedAccessException("Administrator privileges are required");
            }

            // Step 2: Ensure binaries are available
            ProcessManager.EnsureBinaries();
            if (!ProcessManager.AreBinariesAvailable())
            {
                var missing = ProcessManager.GetMissingBinaries();
                throw new FileNotFoundException($"Missing binaries: {string.Join(", ", missing)}");
            }

            // Step 3: Resolve server hostname
            Log.Information("Resolving server address: {Server}", config.ServerAddress);
            var serverIps = await NetworkHelper.ResolveHostnameAsync(config.ServerAddress);
            if (serverIps.Length == 0)
            {
                throw new InvalidOperationException($"Could not resolve server address: {config.ServerAddress}");
            }
            _serverIp = serverIps[0];
            Log.Information("Server resolved to: {IP}", _serverIp);

            // Step 4: Store original network state
            var defaultGateway = _routeManager.GetDefaultGateway();
            if (defaultGateway == null)
            {
                throw new InvalidOperationException("Could not determine default gateway");
            }
            _originalDefaultGateway = defaultGateway.Value.Gateway.ToString();
            _originalDefaultInterfaceIndex = defaultGateway.Value.InterfaceIndex;
            Log.Information("Original gateway: {Gateway} on interface {Index}", 
                _originalDefaultGateway, _originalDefaultInterfaceIndex);

            // Step 5: Backup DNS settings
            _dnsManager.BackupDnsSettings();

            // Step 6: Save recovery state
            SaveRecoveryState();

            // Step 7: Start pingtunnel
            SetState(ConnectionState.Connecting, "Starting pingtunnel...");
            _processManager.StartPingTunnel(
                config.ServerAddress,
                config.LocalSocksPort,
                config.ServerKey,
                _globalSettings);

            // Step 8: Wait for SOCKS proxy to be ready
            Log.Information("Waiting for SOCKS proxy on port {Port}...", config.LocalSocksPort);
            if (!await NetworkHelper.WaitForPortAsync("127.0.0.1", config.LocalSocksPort, 15000))
            {
                throw new TimeoutException("SOCKS proxy did not start in time");
            }
            Log.Information("SOCKS proxy is ready");

            // Step 8b: Allow SOCKS proxy to stabilize before accepting connections
            // This prevents early connection failures (socks handshake: EOF errors)
            await Task.Delay(1000);
            Log.Debug("SOCKS proxy stabilization delay completed");

            // Step 9: Start tun2socks
            SetState(ConnectionState.Connecting, "Starting tun2socks...");
            _processManager.StartTun2Socks(
                config.LocalSocksPort,
                _globalSettings.Mtu);

            // Step 10: Wait for TUN interface to appear
            await Task.Delay(2000); // Give tun2socks time to create the adapter
            var tunIndex = NetworkHelper.GetInterfaceIndexByName("wintun");
            if (tunIndex == null)
            {
                // Try a few more times
                for (int i = 0; i < 5; i++)
                {
                    await Task.Delay(1000);
                    tunIndex = NetworkHelper.GetInterfaceIndexByName("wintun");
                    if (tunIndex != null) break;
                }
            }

            if (tunIndex == null)
            {
                throw new InvalidOperationException("TUN interface did not appear");
            }
            _tunInterfaceIndex = (uint)tunIndex.Value;
            Log.Information("TUN interface index: {Index}", _tunInterfaceIndex);

            // Step 10b: Set TUN adapter IP (tun2socks does not set it; required for routing)
            var tunAdapterName = NetworkHelper.GetInterfaceNameByIndex((int)_tunInterfaceIndex) ?? "wintun";
            try
            {
                _routeManager.SetInterfaceAddress(tunAdapterName, TUN_ADDRESS, TUN_PREFIX);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to set TUN address; routing may not work");
            }

            // Step 11: Configure routes
            SetState(ConnectionState.Connecting, "Configuring routes...");
            ConfigureRoutes(config);

            // Step 12: Configure DNS
            if (_globalSettings.DnsMode == DnsMode.TunnelDns)
            {
                SetState(ConnectionState.Connecting, "Starting DNS forwarder...");
                StartDnsForwarder(config);
            }

            // Step 13: Start health monitoring
            StartHealthCheck();

            // Step 14: Update stats
            _stats = new ConnectionStats
            {
                ConnectedAt = DateTime.UtcNow,
                ServerAddress = config.ServerAddress
            };

            SetState(ConnectionState.Connected, "Connected successfully");
            StartTrafficPolling();
            Log.Information("VPN connection established to {Server} (Config: {ConfigName})", 
                config.ServerAddress, selectedConfig.Name);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Connection failed");
            _connectedConfigId = null;
            await CleanupAsync();
            SetState(ConnectionState.Error, ex.Message, ex);
            throw;
        }
    }

    /// <summary>
    /// Disconnects the VPN.
    /// </summary>
    public async Task DisconnectAsync()
    {
        lock (_stateLock)
        {
            if (_currentState == ConnectionState.Disconnected || _currentState == ConnectionState.Disconnecting)
            {
                return;
            }
            SetState(ConnectionState.Disconnecting, "Disconnecting...");
        }

        try
        {
            await CleanupAsync();
            _connectedConfigId = null;
            SetState(ConnectionState.Disconnected, "Disconnected");
            Log.Information("VPN disconnected");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during disconnect");
            _connectedConfigId = null;
            SetState(ConnectionState.Error, ex.Message, ex);
        }
    }

    /// <summary>
    /// Switches to a different configuration, disconnecting from the current one if connected.
    /// </summary>
    public async Task SwitchConfigAsync(Guid newConfigId)
    {
        var wasConnected = false;
        Guid? previousConfigId = null;

        lock (_stateLock)
        {
            wasConnected = _currentState == ConnectionState.Connected;
            previousConfigId = _connectedConfigId;
        }

        try
        {
            // Check if config exists
            var newConfig = _configManager.GetConfig(newConfigId);
            if (newConfig == null)
            {
                throw new ArgumentException($"Configuration with ID {newConfigId} not found", nameof(newConfigId));
            }

            // If connected, disconnect first
            if (wasConnected)
            {
                Log.Information("Switching config: Disconnecting from current config...");
                await DisconnectAsync();
                
                // Wait a bit for cleanup to complete
                await Task.Delay(500);
            }

            // Set new config as selected
            _configManager.SetSelectedConfig(newConfigId);

            // Connect to new config
            Log.Information("Switching config: Connecting to {ConfigName}...", newConfig.Name);
            await ConnectAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to switch configuration");
            
            // Restore previous config if switch failed
            if (previousConfigId.HasValue)
            {
                try
                {
                    _configManager.SetSelectedConfig(previousConfigId.Value);
                }
                catch
                {
                    // Ignore restore errors
                }
            }
            
            throw;
        }
    }

    private void ConfigureRoutes(VpnConfiguration config)
    {
        // Add route to VPN server via original gateway (to prevent tunnel loop)
        if (_serverIp != null && _originalDefaultGateway != null)
        {
            _routeManager.AddRoute(
                _serverIp.ToString(), 
                32, 
                _originalDefaultGateway, 
                _originalDefaultInterfaceIndex,
                1);
        }

        // Add bypass routes for configured subnets
        foreach (var cidr in _globalSettings.BypassSubnets)
        {
            try
            {
                var (network, prefix) = NetworkHelper.ParseCidr(cidr);
                if (_originalDefaultGateway != null)
                {
                    _routeManager.AddRoute(
                        network.ToString(),
                        prefix,
                        _originalDefaultGateway,
                        _originalDefaultInterfaceIndex,
                        1);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to add bypass route for {Cidr}", cidr);
            }
        }

        // Always bypass the proxy IP (127.0.0.1) to ensure the app works even if 127.0.0.0/8 is removed from bypass subnets
        if (_originalDefaultGateway != null)
        {
            try
            {
                _routeManager.AddRoute(
                    "127.0.0.1",
                    32,
                    _originalDefaultGateway,
                    _originalDefaultInterfaceIndex,
                    1);
                Log.Information("Added bypass route for proxy IP (127.0.0.1/32)");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to add bypass route for proxy IP (127.0.0.1/32)");
            }
        }

        // Set low metric on TUN interface to make it preferred
        _routeManager.SetInterfaceMetric(_tunInterfaceIndex, 1);

        // Add blackhole routes for broadcast/multicast to prevent tunnel flooding
        if (_originalDefaultGateway != null)
        {
            _routeManager.AddBroadcastBlackholeRoutes(
                _originalDefaultGateway, 
                _originalDefaultInterfaceIndex,
                "198.18.0.255"); // TUN subnet broadcast
        }

        // Add default route via TUN gateway so all traffic goes through the tunnel
        _routeManager.AddRoute("0.0.0.0", 0, TUN_GATEWAY, _tunInterfaceIndex, 1);

        // Block UDP on the TUN interface to prevent QUIC and other UDP traffic
        // that would fail through pingtunnel (no UDP ASSOCIATE support)
        try
        {
            _firewallManager.BlockUdpOnSubnet("198.18.0.0/24");
            Log.Information("UDP blocked on TUN interface via firewall");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to block UDP on TUN interface; QUIC traffic may cause issues");
        }

        Log.Information("Routes configured");
    }

    private void StartDnsForwarder(VpnConfiguration config)
    {
        _dnsForwarder = new DnsForwarder(
            listenPort: 53,
            socksHost: "127.0.0.1",
            socksPort: config.LocalSocksPort,
            upstreamServers: _globalSettings.DnsServers);

        try
        {
            _dnsForwarder.Start();

            // Set system DNS to use our forwarder
            _dnsManager.SetDnsForAllAdapters(new[] { "127.0.0.1" });
            DnsManager.FlushDnsCache();

            Log.Information("DNS forwarder started");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to start DNS forwarder on port 53, DNS may not be tunneled");
        }
    }

    private void StartHealthCheck()
    {
        _healthCheckCts = new CancellationTokenSource();
        _healthCheckTask = Task.Run(() => HealthCheckLoopAsync(_healthCheckCts.Token));
    }

    private async Task HealthCheckLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(5000, ct);

                // Skip checks during restart
                if (_isRestarting)
                    continue;

                var config = _configManager.CurrentConfig;

                // Check if processes are still running
                if (!_processManager.IsPingTunnelRunning || !_processManager.IsTun2SocksRunning)
                {
                    Log.Warning("Health check failed: process not running");
                    
                    // Try fast restart if enabled
                    if (_globalSettings.AutoRestartOnHighLatency && CanPerformAutoRestart())
                    {
                        Log.Information("Attempting fast restart due to process failure...");
                        await PerformFastRestartAsync(ct);
                        continue;
                    }
                    
                    await DisconnectAsync();
                    break;
                }

                // Check if SOCKS port is still listening
                if (!await NetworkHelper.IsPortListeningAsync("127.0.0.1", config.LocalSocksPort, 2000))
                {
                    Log.Warning("Health check failed: SOCKS port not responding");
                    
                    // Try fast restart if enabled
                    if (_globalSettings.AutoRestartOnHighLatency && CanPerformAutoRestart())
                    {
                        Log.Information("Attempting fast restart due to SOCKS port failure...");
                        await PerformFastRestartAsync(ct);
                        continue;
                    }
                    
                    await DisconnectAsync();
                    break;
                }

                // Check for sustained high latency - use global settings threshold
                var latencyThreshold = _globalSettings.LatencyThresholdMs > 0 ? _globalSettings.LatencyThresholdMs : DefaultLatencyCriticalThresholdMs;
                var countThreshold = _globalSettings.HighLatencyCountThreshold > 0 ? _globalSettings.HighLatencyCountThreshold : 5;

                if (_stats.HighLatencyCount >= countThreshold)
                {
                    Log.Warning("Tunnel latency degraded ({Count} readings > {Threshold}ms)", 
                        _stats.HighLatencyCount, latencyThreshold);

                    // Try fast restart if enabled
                    if (_globalSettings.AutoRestartOnHighLatency && CanPerformAutoRestart())
                    {
                        Log.Information("Attempting fast restart due to high latency (latency: {Latency:F0}ms)...", 
                            _stats.LatencyMs);
                        await PerformFastRestartAsync(ct);
                        continue;
                    }
                    
                    // No more restarts available - disconnect
                    Log.Error("Max auto-restarts reached ({Count}). Disconnecting.", _autoRestartCount);
                    SetState(ConnectionState.Error, $"Connection quality too poor (latency: {_stats.LatencyMs:F0}ms)");
                    await DisconnectAsync();
                    break;
                }

                // Log periodic health status when connected
                if (_stats.IsLatencyDegraded)
                {
                    Log.Debug("Health check: latency degraded ({Latency:F0}ms, count: {Count}/{Max})", 
                        _stats.LatencyMs, _stats.HighLatencyCount, countThreshold);
                }

                // Update stats
                StatsUpdated?.Invoke(this, _stats);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Health check iteration failed");
            }
        }
    }

    private void StartTrafficPolling()
    {
        _trafficHasBaseline = false;
        _trafficTunRx = _trafficTunTx = _trafficPhysRx = _trafficPhysTx = 0;
        _trafficSnapshotAt = default;
        _trafficCts = new CancellationTokenSource();
        _trafficTask = Task.Run(() => TrafficPollingLoopAsync(_trafficCts.Token));
    }

    private async Task StopTrafficPollingAsync()
    {
        _trafficCts?.Cancel();
        if (_trafficTask != null)
        {
            try
            {
                await Task.WhenAny(_trafficTask, Task.Delay(2000));
            }
            catch
            {
                // Ignore
            }

            _trafficTask = null;
            _trafficCts?.Dispose();
            _trafficCts = null;
        }
    }

    private async Task TrafficPollingLoopAsync(CancellationToken ct)
    {
        const int intervalMs = 1000;
        var tunIdx = (int)_tunInterfaceIndex;
        var physIdx = (int)_originalDefaultInterfaceIndex;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(intervalMs, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (_isRestarting)
                continue;

            var now = DateTime.UtcNow;
            long tunRx = 0, tunTx = 0, physRx = 0, physTx = 0;
            var gotTun = NetworkHelper.TryGetInterfaceStats(tunIdx, out tunRx, out tunTx);
            var gotPhys = NetworkHelper.TryGetInterfaceStats(physIdx, out physRx, out physTx);

            if (!gotTun && !gotPhys)
                continue;

            double tunRxBps = 0, tunTxBps = 0, physRxBps = 0, physTxBps = 0;
            var elapsed = (_trafficSnapshotAt != default)
                ? (now - _trafficSnapshotAt).TotalSeconds
                : 0;

            if (elapsed > 0)
            {
                if (gotTun)
                {
                    tunRxBps = Math.Max(0, (double)(tunRx - _trafficTunRx) / elapsed);
                    tunTxBps = Math.Max(0, (double)(tunTx - _trafficTunTx) / elapsed);
                }
                if (gotPhys)
                {
                    physRxBps = Math.Max(0, (double)(physRx - _trafficPhysRx) / elapsed);
                    physTxBps = Math.Max(0, (double)(physTx - _trafficPhysTx) / elapsed);
                }
            }

            if (!_trafficHasBaseline)
            {
                _trafficBaselineTunRx = tunRx;
                _trafficBaselineTunTx = tunTx;
                _trafficBaselinePhysRx = physRx;
                _trafficBaselinePhysTx = physTx;
                _trafficHasBaseline = true;
            }

            _trafficTunRx = tunRx;
            _trafficTunTx = tunTx;
            _trafficPhysRx = physRx;
            _trafficPhysTx = physTx;
            _trafficSnapshotAt = now;

            var tunSessionRx = Math.Max(0, tunRx - _trafficBaselineTunRx);
            var tunSessionTx = Math.Max(0, tunTx - _trafficBaselineTunTx);
            var physSessionRx = Math.Max(0, physRx - _trafficBaselinePhysRx);
            var physSessionTx = Math.Max(0, physTx - _trafficBaselinePhysTx);

            _stats.TunRxBps = tunRxBps;
            _stats.TunTxBps = tunTxBps;
            _stats.PhysicalRxBps = physRxBps;
            _stats.PhysicalTxBps = physTxBps;
            _stats.TunBytesReceived = tunSessionRx;
            _stats.TunBytesSent = tunSessionTx;
            _stats.PhysicalBytesReceived = physSessionRx;
            _stats.PhysicalBytesSent = physSessionTx;
            _stats.BytesReceived = tunSessionRx;
            _stats.BytesSent = tunSessionTx;

            StatsUpdated?.Invoke(this, _stats);
        }
    }

    /// <summary>
    /// Checks if auto-restart can be performed based on cooldown and max restarts.
    /// </summary>
    private bool CanPerformAutoRestart()
    {
        // Check max restarts (0 = unlimited)
        if (_globalSettings.MaxAutoRestarts > 0 && _autoRestartCount >= _globalSettings.MaxAutoRestarts)
        {
            Log.Debug("Auto-restart limit reached ({Count}/{Max})", _autoRestartCount, _globalSettings.MaxAutoRestarts);
            return false;
        }

        // Check cooldown
        var timeSinceLastRestart = DateTime.UtcNow - _lastRestartTime;
        if (timeSinceLastRestart.TotalSeconds < _globalSettings.RestartCooldownSeconds)
        {
            Log.Debug("Auto-restart on cooldown ({Elapsed:F0}s / {Cooldown}s)", 
                timeSinceLastRestart.TotalSeconds, _globalSettings.RestartCooldownSeconds);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Performs a fast restart of the tunnel processes without tearing down the NIC, routes, or firewall.
    /// This is much faster than a full disconnect/reconnect cycle.
    /// </summary>
    private async Task PerformFastRestartAsync(CancellationToken ct)
    {
        if (_isRestarting)
        {
            Log.Debug("Fast restart already in progress, skipping");
            return;
        }

        _isRestarting = true;
        _autoRestartCount++;
        _lastRestartTime = DateTime.UtcNow;

        try
        {
            var config = _configManager.CurrentConfig;
            
            Log.Information("Fast restart #{Count}: Stopping tunnel processes...", _autoRestartCount);
            SetState(ConnectionState.Connecting, $"Restarting tunnel ({_autoRestartCount})...");

            // Stop processes only (keep NIC, routes, firewall, DNS forwarder)
            _processManager.StopAll();
            
            // Brief pause to allow ports to be released
            await Task.Delay(1000, ct);

            // Restart pingtunnel
            Log.Information("Fast restart: Starting pingtunnel...");
            _processManager.StartPingTunnel(
                config.ServerAddress,
                config.LocalSocksPort,
                config.ServerKey,
                _globalSettings);

            // Wait for SOCKS proxy
            Log.Information("Fast restart: Waiting for SOCKS proxy...");
            if (!await NetworkHelper.WaitForPortAsync("127.0.0.1", config.LocalSocksPort, 10000))
            {
                throw new TimeoutException("SOCKS proxy did not restart in time");
            }

            // Brief stabilization delay
            await Task.Delay(500, ct);

            // Restart tun2socks
            Log.Information("Fast restart: Starting tun2socks...");
            _processManager.StartTun2Socks(
                config.LocalSocksPort,
                _globalSettings.Mtu);

            // Wait for tun2socks to reconnect to the existing NIC
            await Task.Delay(1000, ct);

            // Reset latency counters
            _stats.HighLatencyCount = 0;
            _stats.IsLatencyDegraded = false;
            _stats.LatencyMs = 0;

            SetState(ConnectionState.Connected, "Tunnel restarted successfully");
            Log.Information("Fast restart #{Count} completed successfully", _autoRestartCount);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Fast restart failed");
            SetState(ConnectionState.Error, $"Fast restart failed: {ex.Message}");
            
            // On failure, perform full disconnect
            await DisconnectAsync();
        }
        finally
        {
            _isRestarting = false;
        }
    }

    private async Task CleanupAsync()
    {
        Log.Information("Cleaning up connection...");

        // Stop traffic polling
        await StopTrafficPollingAsync();

        // Stop health check
        _healthCheckCts?.Cancel();
        if (_healthCheckTask != null)
        {
            try
            {
                await Task.WhenAny(_healthCheckTask, Task.Delay(2000));
            }
            catch { }
        }
        _healthCheckCts?.Dispose();
        _healthCheckCts = null;

        // Stop DNS forwarder
        try
        {
            _dnsForwarder?.Stop();
            _dnsForwarder?.Dispose();
            _dnsForwarder = null;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error stopping DNS forwarder");
        }

        // Restore DNS settings
        try
        {
            _dnsManager.RestoreAllDnsSettings();
            DnsManager.FlushDnsCache();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error restoring DNS settings");
        }

        // Remove routes
        try
        {
            _routeManager.RemoveAllAddedRoutes();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error removing routes");
        }

        // Remove firewall rules
        try
        {
            _firewallManager.RemoveAllRules();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error removing firewall rules");
        }

        // Stop processes
        _processManager.StopAll();

        // Clear recovery state
        _recoveryManager.ClearState();

        // Reset state
        _serverIp = null;
        _originalDefaultGateway = null;
        _stats = new ConnectionStats();
        _connectedConfigId = null;

        Log.Information("Cleanup completed");
    }

    private void SaveRecoveryState()
    {
        var state = new CrashRecoveryManager.RecoveryState
        {
            IsConnected = true,
            OriginalDefaultGateway = _originalDefaultGateway,
            OriginalDefaultInterfaceIndex = (int)_originalDefaultInterfaceIndex,
            OriginalDnsSettings = _dnsManager.GetOriginalDnsSettings(),
            AddedRoutes = _routeManager.GetAddedRoutes()
                .Select(r => new CrashRecoveryManager.RouteEntry
                {
                    Destination = r.Destination,
                    PrefixLength = r.PrefixLength,
                    Gateway = r.Gateway,
                    InterfaceIndex = (int)r.InterfaceIndex,
                    Metric = (int)r.Metric
                })
                .ToList()
        };

        _recoveryManager.SaveState(state);
    }

    private void SetState(ConnectionState newState, string? message = null, Exception? error = null)
    {
        ConnectionState oldState;
        lock (_stateLock)
        {
            oldState = _currentState;
            _currentState = newState;
        }

        Log.Information("State changed: {OldState} -> {NewState}: {Message}", oldState, newState, message);
        StateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(oldState, newState, message, error));
    }

    private void OnProcessExited(object? sender, ProcessExitEventArgs e)
    {
        if (_currentState == ConnectionState.Connected)
        {
            Log.Warning("Process {Name} exited unexpectedly with code {Code}", e.ProcessName, e.ExitCode);
            _ = DisconnectAsync();
        }
    }

    private void OnProcessOutput(object? sender, ProcessOutputEventArgs e)
    {
        // Parse pingtunnel output for latency information
        // Format: "pong from X.X.X.X 123.456ms" or "pong from X.X.X.X 1.234s"
        if (e.ProcessName == "pingtunnel" && e.Output != null)
        {
            ParseLatencyFromPingTunnel(e.Output);
        }
    }

    /// <summary>
    /// Parses latency from pingtunnel pong messages.
    /// </summary>
    private void ParseLatencyFromPingTunnel(string output)
    {
        try
        {
            // Look for "pong from X.X.X.X XXXms" or "pong from X.X.X.X X.Xs"
            if (!output.Contains("pong from", StringComparison.OrdinalIgnoreCase))
                return;

            // Extract the latency value using regex
            var match = System.Text.RegularExpressions.Regex.Match(
                output, 
                @"pong from [\d\.]+ ([\d\.]+)(ms|s)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (!match.Success)
                return;

            double latencyMs;
            if (double.TryParse(match.Groups[1].Value, out var value))
            {
                // Convert to milliseconds if in seconds
                latencyMs = match.Groups[2].Value.ToLowerInvariant() == "s" 
                    ? value * 1000 
                    : value;
            }
            else
            {
                return;
            }

            // Update stats
            _stats.LatencyMs = latencyMs;

            // Get thresholds from global settings (with defaults)
            var warningThreshold = _globalSettings.LatencyThresholdMs > 0 
                ? _globalSettings.LatencyThresholdMs * 0.5  // Warning at 50% of threshold
                : DefaultLatencyWarningThresholdMs;
            var criticalThreshold = _globalSettings.LatencyThresholdMs > 0 
                ? _globalSettings.LatencyThresholdMs 
                : DefaultLatencyCriticalThresholdMs;

            // Check for degradation
            if (latencyMs > warningThreshold)
            {
                _stats.HighLatencyCount++;
                _stats.IsLatencyDegraded = true;

                if (latencyMs > criticalThreshold)
                {
                    Log.Warning("Critical tunnel latency: {Latency:F0}ms (threshold: {Threshold}ms)", 
                        latencyMs, criticalThreshold);
                }
                else if (_stats.HighLatencyCount % 5 == 1) // Log every 5th high reading
                {
                    Log.Warning("Elevated tunnel latency: {Latency:F0}ms (count: {Count})", 
                        latencyMs, _stats.HighLatencyCount);
                }
            }
            else
            {
                // Reset counter when latency is good
                if (_stats.HighLatencyCount > 0)
                {
                    Log.Information("Tunnel latency recovered: {Latency:F0}ms", latencyMs);
                }
                _stats.HighLatencyCount = 0;
                _stats.IsLatencyDegraded = false;
            }
        }
        catch
        {
            // Ignore parsing errors
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _trafficCts?.Cancel();
        _trafficCts?.Dispose();
        _trafficCts = null;
        _healthCheckCts?.Cancel();
        _dnsForwarder?.Dispose();
        _processManager.Dispose();
    }
}
