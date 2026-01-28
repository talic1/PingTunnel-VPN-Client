using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PingTunnelVPN.App.ViewModels;
using PingTunnelVPN.Core;
using PingTunnelVPN.Platform;
using Serilog;
using static PingTunnelVPN.App.FormattingHelper;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using RadioButton = System.Windows.Controls.RadioButton;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace PingTunnelVPN.App.Views;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private MainViewModel? _viewModel;
    private ConnectionStateMachine? _stateMachine;
    private ConfigManager? _configManager;
    private TrayIconManager? _trayIconManager;
    private System.Windows.Threading.DispatcherTimer? _uptimeTimer;
    private bool _exitInProgress;

    public MainWindow()
    {
        try
        {
            App.WriteCrashLog("MainWindow constructor starting...");
            
            InitializeComponent();
            App.WriteCrashLog("InitializeComponent completed");

            // Initialize services
            _configManager = new ConfigManager();
            App.WriteCrashLog("ConfigManager created");
            
            _configManager.Load();
            App.WriteCrashLog("Config loaded");
            
            _stateMachine = new ConnectionStateMachine(_configManager);
            _stateMachine.StateChanged += OnStateChanged;
            _stateMachine.StatsUpdated += OnStatsUpdated;
            App.WriteCrashLog("ConnectionStateMachine created");

            // Initialize view model
            _viewModel = new MainViewModel(_configManager);
            DataContext = _viewModel;
            App.WriteCrashLog("ViewModel created and bound");

            // Load initial values
            LoadConfigToUI();
            UpdateEmptyState();
            App.WriteCrashLog("Config loaded to UI");
            
            // Update connected config indicator
            if (_stateMachine != null)
            {
                _viewModel.UpdateConnectedConfig(_stateMachine.ConnectedConfigId);
            }

            // Setup tray icon (with error handling)
            try
            {
                SetupTrayIcon();
                App.WriteCrashLog("Tray icon set up");
            }
            catch (Exception ex)
            {
                App.WriteCrashLog($"Failed to setup tray icon: {ex.Message}");
                Log.Warning(ex, "Failed to setup tray icon");
            }

            // Setup uptime timer
            SetupUptimeTimer();
            App.WriteCrashLog("Uptime timer set up");

            // Set app version
            AppVersionText.Text = $"Version {AppInfo.Version}";

            // Set window and title bar icon (icon.ico)
            SetAppIcon();

            // Check elevation
            if (!ElevationHelper.IsElevated())
            {
                StatusText.Text = "Not Elevated";
                StatusSubText.Text = "Please restart as Administrator";
                StatusSubText.Visibility = Visibility.Visible;
                StatusIconContainer.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#f59e0b")!);
            }
            
            App.WriteCrashLog("MainWindow constructor completed successfully");
            Log.Information("MainWindow initialized successfully");
        }
        catch (Exception ex)
        {
            App.WriteCrashLog($"FATAL: MainWindow constructor failed: {ex}");
            Log.Fatal(ex, "MainWindow constructor failed");
            throw;
        }
    }

    internal Task EmergencyShutdownAsync(string reason)
    {
        if (!Dispatcher.CheckAccess())
        {
            return Dispatcher.InvokeAsync(() => EmergencyShutdownAsync(reason)).Task.Unwrap();
        }

        if (_exitInProgress)
        {
            return Task.CompletedTask;
        }

        _exitInProgress = true;

        try
        {
            SaveConfigFromUI();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to save config during emergency shutdown");
        }

        return EmergencyShutdownInternalAsync(reason);
    }

    private async Task EmergencyShutdownInternalAsync(string reason)
    {
        try
        {
            Log.Warning("Emergency shutdown requested: {Reason}", reason);

            if (_stateMachine != null && _stateMachine.CurrentState != ConnectionState.Disconnected)
            {
                var disconnectTask = _stateMachine.DisconnectAsync();
                await Task.WhenAny(disconnectTask, Task.Delay(5000));
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Emergency disconnect failed");
        }
        finally
        {
            _stateMachine?.Dispose();
            Application.Current.Shutdown();
        }
    }

    private void LoadConfigToUI()
    {
        if (_configManager == null || _viewModel == null) return;
        
        // Load global settings
        _viewModel.LoadGlobalSettings();
        
        // Update DNS mode combo box from global settings
        var globalSettings = _configManager.GetGlobalSettings();
        DnsModeComboBox.SelectedIndex = globalSettings.DnsMode == DnsMode.TunnelDns ? 0 : 1;
        
        // Sync encryption key password box from global settings
        EncryptionKeyPasswordBox.Password = globalSettings.EncryptionKey;
    }

    private void UpdateEmptyState()
    {
        if (_viewModel == null) return;
        
        var hasConfigs = _viewModel.Configs.Count > 0;
        EmptyStatePanel.Visibility = hasConfigs ? Visibility.Collapsed : Visibility.Visible;
        ConfigList.Visibility = hasConfigs ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetupTrayIcon()
    {
        if (_stateMachine == null) return;
        
        _trayIconManager = new TrayIconManager(this, _stateMachine);
        if (Application.Current is App app)
        {
            app.SetTrayIconManager(_trayIconManager);
        }
    }

    private void SetAppIcon()
    {
        try
        {
            if (!File.Exists(AppIcons.IconPath)) return;
            var uri = new Uri(AppIcons.IconPath, UriKind.Absolute);
            var frame = BitmapFrame.Create(uri);
            Icon = frame;
            if (TitleBarAppIcon != null)
                TitleBarAppIcon.Source = frame;
        }
        catch
        {
            // Ignore icon load errors
        }
    }

    private void SetupUptimeTimer()
    {
        _uptimeTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _uptimeTimer.Tick += (s, e) =>
        {
            if (_stateMachine?.CurrentState == ConnectionState.Connected)
            {
                var uptime = _stateMachine.Stats.Uptime;
                UptimeText.Text = $"Uptime: {uptime:hh\\:mm\\:ss}";
            }
        };
    }

    private void OnStateChanged(object? sender, ConnectionStateChangedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateUIForState(e.NewState, e.Message);
            
            // Update connected config indicator
            if (_stateMachine != null && _viewModel != null)
            {
                _viewModel.UpdateConnectedConfig(_stateMachine.ConnectedConfigId);
            }
        });
    }

    private void OnStatsUpdated(object? sender, ConnectionStats stats)
    {
        Dispatcher.Invoke(() =>
        {
            _trayIconManager?.UpdateConnectionStats(stats.Uptime, stats.BytesReceived, stats.BytesSent, stats.TunRxBps, stats.TunTxBps);
            
            if (_stateMachine?.CurrentState == ConnectionState.Connected && _viewModel != null)
            {
                // Update traffic display using byte metric format (KB/s, MB/s, GB/s)
                TunnelDownText.Text = FormattingHelper.FormatSpeedCompact(stats.TunRxBps);
                TunnelUpText.Text = FormattingHelper.FormatSpeedCompact(stats.TunTxBps);
                PhysicalDownText.Text = FormattingHelper.FormatSpeedCompact(stats.PhysicalRxBps);
                PhysicalUpText.Text = FormattingHelper.FormatSpeedCompact(stats.PhysicalTxBps);
                
                // Also update ViewModel
                _viewModel.UpdateTrafficStats(stats.TunRxBps, stats.TunTxBps, stats.PhysicalRxBps, stats.PhysicalTxBps);
            }
        });
    }

    private void UpdateUIForState(ConnectionState state, string? message)
    {
        var successBackground = (Color)ColorConverter.ConvertFromString("#0d3320")!;
        var successBorder = (Color)ColorConverter.ConvertFromString("#22c55e")!;
        var surfaceBackground = (Color)ColorConverter.ConvertFromString("#1e2235")!;
        var defaultBorder = (Color)ColorConverter.ConvertFromString("#2d3748")!;
        var warningColor = (Color)ColorConverter.ConvertFromString("#f59e0b")!;
        var errorColor = (Color)ColorConverter.ConvertFromString("#ef4444")!;
        var secondaryText = (Color)ColorConverter.ConvertFromString("#9ca3af")!;

        switch (state)
        {
            case ConnectionState.Disconnected:
                StatusText.Text = "Disconnected";
                StatusText.Foreground = new SolidColorBrush(Colors.White);
                StatusSubText.Visibility = Visibility.Collapsed;
                UptimeText.Text = "";
                
                // Update header card style (default)
                StatusHeaderCard.Background = new SolidColorBrush(surfaceBackground);
                StatusHeaderCard.BorderBrush = new SolidColorBrush(defaultBorder);
                
                // Update icon
                StatusIconContainer.Background = FindResource("Brush.Background.Elevated") as Brush;
                StatusIcon.Fill = new SolidColorBrush(secondaryText);
                StatusIcon.Data = (Geometry)FindResource("Icon.Shield");
                
                // Update button
                ConnectButton.Content = "Connect";
                ConnectButton.Style = (Style)FindResource("Button.Primary");
                ConnectButton.IsEnabled = true;
                
                // Hide traffic
                TrafficStatsSection.Visibility = Visibility.Collapsed;
                _uptimeTimer?.Stop();
                
                SetFieldsEnabled(true);
                break;

            case ConnectionState.Connecting:
                StatusText.Text = "Connecting...";
                StatusText.Foreground = new SolidColorBrush(Colors.White);
                StatusSubText.Text = message ?? "Please wait";
                StatusSubText.Visibility = Visibility.Visible;
                
                // Warning style header
                StatusIconContainer.Background = new SolidColorBrush(warningColor);
                StatusIcon.Fill = new SolidColorBrush(Colors.White);
                
                ConnectButton.Content = "Connecting...";
                ConnectButton.IsEnabled = false;
                
                SetFieldsEnabled(false);
                break;

            case ConnectionState.Connected:
                StatusText.Text = "Connected";
                StatusText.Foreground = new SolidColorBrush(successBorder);
                
                var connectedConfig = _configManager?.GetConfig(_stateMachine?.ConnectedConfigId ?? Guid.Empty);
                if (connectedConfig != null)
                {
                    StatusSubText.Text = $"to {connectedConfig.Name}";
                    StatusSubText.Visibility = Visibility.Visible;
                }
                else
                {
                    StatusSubText.Visibility = Visibility.Collapsed;
                }
                
                // Update header card style (success)
                StatusHeaderCard.Background = new SolidColorBrush(successBackground);
                StatusHeaderCard.BorderBrush = new SolidColorBrush(successBorder);
                
                // Update icon
                StatusIconContainer.Background = new SolidColorBrush(successBorder);
                StatusIcon.Fill = new SolidColorBrush(Colors.White);
                StatusIcon.Data = (Geometry)FindResource("Icon.ShieldCheck");
                
                // Update button
                ConnectButton.Content = "Disconnect";
                ConnectButton.Style = (Style)FindResource("Button.Danger");
                ConnectButton.IsEnabled = true;
                
                // Show traffic
                TrafficStatsSection.Visibility = Visibility.Visible;
                _uptimeTimer?.Start();
                
                SetFieldsEnabled(false);
                break;

            case ConnectionState.Disconnecting:
                StatusText.Text = "Disconnecting...";
                StatusText.Foreground = new SolidColorBrush(Colors.White);
                StatusSubText.Text = message ?? "Please wait";
                StatusSubText.Visibility = Visibility.Visible;
                
                StatusIconContainer.Background = new SolidColorBrush(warningColor);
                StatusIcon.Fill = new SolidColorBrush(Colors.White);
                
                ConnectButton.Content = "Disconnecting...";
                ConnectButton.IsEnabled = false;
                break;

            case ConnectionState.Error:
                StatusText.Text = "Error";
                StatusText.Foreground = new SolidColorBrush(errorColor);
                StatusSubText.Text = message ?? "An error occurred";
                StatusSubText.Visibility = Visibility.Visible;
                
                // Error style
                StatusIconContainer.Background = new SolidColorBrush(errorColor);
                StatusIcon.Fill = new SolidColorBrush(Colors.White);
                StatusIcon.Data = (Geometry)FindResource("Icon.Shield");
                
                // Reset header
                StatusHeaderCard.Background = new SolidColorBrush(surfaceBackground);
                StatusHeaderCard.BorderBrush = new SolidColorBrush(errorColor);
                
                ConnectButton.Content = "Connect";
                ConnectButton.Style = (Style)FindResource("Button.Primary");
                ConnectButton.IsEnabled = true;
                
                TrafficStatsSection.Visibility = Visibility.Collapsed;
                _uptimeTimer?.Stop();
                
                SetFieldsEnabled(true);
                break;
        }
    }

    private void SetFieldsEnabled(bool enabled)
    {
        MtuTextBox.IsEnabled = enabled;
        DnsModeComboBox.IsEnabled = enabled;
        DnsServersTextBox.IsEnabled = enabled;
        BypassSubnetsTextBox.IsEnabled = enabled;
        KillSwitchToggle.IsEnabled = enabled;
        EncryptionModeComboBox.IsEnabled = enabled;
        EncryptionKeyPasswordBox.IsEnabled = enabled;
        EncryptionKeyTextBox.IsEnabled = enabled;
        AutoRestartToggle.IsEnabled = enabled;
        AutoConnectToggle.IsEnabled = enabled;
        MinimizeToTrayToggle.IsEnabled = enabled;
        StartMinimizedToggle.IsEnabled = enabled;
    }

    #region Tab Navigation

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        // Guard against event firing during XAML initialization
        if (ConnectionsTabContent == null || LogsTabContent == null || AdvancedTabContent == null)
            return;
            
        if (sender is RadioButton tab)
        {
            ConnectionsTabContent.Visibility = tab == TabConnections ? Visibility.Visible : Visibility.Collapsed;
            LogsTabContent.Visibility = tab == TabLogs ? Visibility.Visible : Visibility.Collapsed;
            AdvancedTabContent.Visibility = tab == TabAdvanced ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    #endregion

    #region Connection Actions

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_stateMachine == null || _configManager == null || _viewModel == null) return;
        
        if (_stateMachine.CurrentState == ConnectionState.Disconnected ||
            _stateMachine.CurrentState == ConnectionState.Error)
        {
            // Get selected config
            var selectedConfig = _viewModel.SelectedConfig;
            if (selectedConfig == null && _viewModel.Configs.Count > 0)
            {
                selectedConfig = _viewModel.Configs[0];
                _viewModel.SelectedConfig = selectedConfig;
            }

            if (selectedConfig == null)
            {
                MessageBox.Show(
                    "Please add a configuration first.",
                    "No Configuration",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            // Select this config
            _configManager.SetSelectedConfig(selectedConfig.Id);
            
            // Save any pending UI changes
            SaveConfigFromUI();
            
            // Validate
            var errors = _configManager.CurrentConfig.Validate();
            if (errors.Count > 0)
            {
                MessageBox.Show(
                    string.Join("\n", errors),
                    "Configuration Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            try
            {
                await _stateMachine.ConnectAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Connection failed");
                MessageBox.Show(
                    $"Connection failed: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        else if (_stateMachine.CurrentState == ConnectionState.Connected)
        {
            try
            {
                await _stateMachine.DisconnectAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Disconnect failed");
            }
        }
    }

    #endregion

    #region Config Management

    private void ConfigItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is Guid configId)
        {
            _viewModel?.SelectConfig(configId);
            _viewModel?.LoadConfig();
            LoadConfigToUI();
        }
    }

    private void AddConfigButton_Click(object sender, RoutedEventArgs e)
    {
        if (_configManager == null || _viewModel == null) return;
        
        var dialog = new ConfigEditDialog(_configManager);
        dialog.Owner = this;
        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            var added = _configManager.AddConfig(dialog.Result);
            _configManager.SetSelectedConfig(added.Id);
            _viewModel.LoadConfigs();
            LoadConfigToUI();
            UpdateEmptyState();
            
            Log.Information("Configuration \"{ConfigName}\" added", dialog.Result.Name);
        }
    }

    private void EditConfigButton_Click(object sender, RoutedEventArgs e)
    {
        if (_configManager == null || _viewModel == null) return;
        
        Guid configId;
        if (sender is Button btn && btn.Tag is Guid id)
        {
            configId = id;
        }
        else if (_viewModel.SelectedConfig != null)
        {
            configId = _viewModel.SelectedConfig.Id;
        }
        else
        {
            return;
        }
        
        var dialog = new ConfigEditDialog(_configManager, configId);
        dialog.Owner = this;
        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            _configManager.UpdateConfig(configId, sc =>
            {
                sc.Name = dialog.Result.Name;
                sc.Configuration = dialog.Result.Configuration;
            });
            _viewModel.LoadConfigs();
            LoadConfigToUI();
        }
    }

    private void DeleteConfigButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null || _configManager == null || _stateMachine == null) return;
        
        Guid configId;
        string configName;
        
        if (sender is Button btn && btn.Tag is Guid id)
        {
            configId = id;
            var config = _configManager.GetConfig(id);
            configName = config?.Name ?? "Unknown";
        }
        else if (_viewModel.SelectedConfig != null)
        {
            configId = _viewModel.SelectedConfig.Id;
            configName = _viewModel.SelectedConfig.Name;
        }
        else
        {
            return;
        }
        
        var isConnected = _stateMachine.ConnectedConfigId == configId;
        
        var message = isConnected
            ? $"Configuration '{configName}' is currently connected. Disconnect first, then delete?"
            : $"Are you sure you want to delete configuration '{configName}'?";
        
        var result = MessageBox.Show(
            message,
            "Delete Configuration",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        
        if (result == MessageBoxResult.Yes)
        {
            if (isConnected)
            {
                _ = Task.Run(async () =>
                {
                    await _stateMachine.DisconnectAsync();
                    Dispatcher.Invoke(() =>
                    {
                        _viewModel.DeleteConfig(configId);
                        LoadConfigToUI();
                        UpdateEmptyState();
                    });
                });
            }
            else
            {
                _viewModel.DeleteConfig(configId);
                LoadConfigToUI();
                UpdateEmptyState();
            }
        }
    }

    private void SaveConfigFromUI()
    {
        if (_viewModel == null) return;
        
        // Update DNS mode if needed (settings are saved automatically via property setters)
        _viewModel.DnsMode = DnsModeComboBox.SelectedIndex == 0 ? DnsMode.TunnelDns : DnsMode.SystemDns;
        
        // Ensure server-specific config is saved (though it's also saved automatically)
        _viewModel.SaveConfig();
    }

    #endregion

    #region Settings Event Handlers

    private void DnsModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel == null) return;
        _viewModel.DnsMode = DnsModeComboBox.SelectedIndex == 0 ? DnsMode.TunnelDns : DnsMode.SystemDns;
        // Settings are saved automatically via property setter
    }

    private bool _encryptionKeyVisible;

    private void EncryptionKeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null && !_encryptionKeyVisible)
        {
            _viewModel.EncryptionKey = EncryptionKeyPasswordBox.Password;
            // Settings are saved automatically via property setter
        }
    }

    private void ToggleEncryptionKeyVisibility_Click(object sender, RoutedEventArgs e)
    {
        _encryptionKeyVisible = !_encryptionKeyVisible;
        
        if (_encryptionKeyVisible)
        {
            EncryptionKeyTextBox.Text = EncryptionKeyPasswordBox.Password;
            EncryptionKeyPasswordBox.Visibility = Visibility.Collapsed;
            EncryptionKeyTextBox.Visibility = Visibility.Visible;
            EncryptionKeyToggleIcon.Data = (Geometry)FindResource("Icon.EyeOff");
        }
        else
        {
            EncryptionKeyPasswordBox.Password = EncryptionKeyTextBox.Text;
            EncryptionKeyPasswordBox.Visibility = Visibility.Visible;
            EncryptionKeyTextBox.Visibility = Visibility.Collapsed;
            EncryptionKeyToggleIcon.Data = (Geometry)FindResource("Icon.Eye");
        }
    }

    #endregion

    #region Log Actions

    private void ClearLogs_Click(object sender, RoutedEventArgs e)
    {
        _viewModel?.ClearLogs();
    }

    private void ExportLogs_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        
        var dialog = new SaveFileDialog
        {
            Filter = "Log files (*.log)|*.log|Text files (*.txt)|*.txt",
            FileName = $"PingTunnelVPN_{DateTime.Now:yyyy-MM-dd_HHmmss}.log"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                _viewModel.ExportLogs(dialog.FileName);
                Log.Information("Logs exported to {LogPath}", dialog.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to export logs: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
    {
        var logDir = App.LogDirectory;
        try
        {
            if (!Directory.Exists(logDir))
            {
                Directory.CreateDirectory(logDir);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = logDir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to open log folder");
            MessageBox.Show($"Failed to open log folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ChangeLogFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;

        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select a folder for PingTunnelVPN logs",
            ShowNewFolderButton = true,
            SelectedPath = App.LogDirectory
        };

        var result = dialog.ShowDialog();
        if (result == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            if (!App.TryUpdateLogDirectory(dialog.SelectedPath, out var resolvedPath, out var error))
            {
                MessageBox.Show($"Failed to update log folder:\n\n{error}", "Log Folder", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _viewModel.AppLogDirectory = resolvedPath;
            _viewModel.RefreshLogPaths();
        }
    }

    #endregion

    #region Config Import/Export

    private void ImportConfig_Click(object sender, RoutedEventArgs e)
    {
        if (_configManager == null || _viewModel == null) return;
        
        var dialog = new OpenFileDialog
        {
            Filter = "JSON files (*.json)|*.json",
            Title = "Import Configuration"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var imported = _configManager.Import(dialog.FileName);
                _configManager.SetSelectedConfig(imported.Id);
                _viewModel.LoadConfigs();
                _viewModel.LoadGlobalSettings(); // Reload global settings (may have been imported)
                LoadConfigToUI();
                UpdateEmptyState();
                MessageBox.Show("Configuration and settings imported successfully.", "Import", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to import configuration: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void ExportConfig_Click(object sender, RoutedEventArgs e)
    {
        if (_configManager == null) return;
        
        var dialog = new SaveFileDialog
        {
            Filter = "JSON files (*.json)|*.json",
            FileName = "pingtunnelvpn-config.json",
            Title = "Export Configuration"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                SaveConfigFromUI();
                _configManager.Export(dialog.FileName);
                MessageBox.Show("Configuration and settings exported successfully.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to export configuration: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void ResetConfig_Click(object sender, RoutedEventArgs e)
    {
        if (_configManager == null || _viewModel == null) return;
        
        var result = MessageBox.Show(
            "Are you sure you want to reset all settings to defaults?",
            "Reset Configuration",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            // Reset global settings
            _configManager.ResetGlobalSettings();
            // Reset selected config (server-specific settings only)
            _configManager.Reset();
            _viewModel.LoadConfig();
            _viewModel.LoadGlobalSettings();
            LoadConfigToUI();
            Log.Information("Advanced settings reset to defaults");
        }
    }

    private void GitHubLink_Click(object sender, MouseButtonEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = AppInfo.GitHubUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to open GitHub URL");
            MessageBox.Show($"Could not open link: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    #endregion

    #region Titlebar Events

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            // Double-click does nothing for this window (no maximize)
            return;
        }
        
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private async void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Are you sure you want to exit the application?",
            "Exit Application",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            await ExitApplicationAsync();
        }
    }

    /// <summary>
    /// Cleanly exits the application - disconnects if connected and shuts down.
    /// Used by both UI close button and tray exit.
    /// </summary>
    private async Task ExitApplicationAsync()
    {
        if (_stateMachine == null) return;

        // Save config before exit
        SaveConfigFromUI();

        // Ensure clean disconnect before exit
        if (_stateMachine.CurrentState == ConnectionState.Connected || 
            _stateMachine.CurrentState == ConnectionState.Connecting)
        {
            try
            {
                await _stateMachine.DisconnectAsync();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error during disconnect on exit");
            }
        }

        _stateMachine.Dispose();
        Application.Current.Shutdown();
    }

    #endregion

    #region Window Events

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        if (_viewModel == null || _stateMachine == null) return;
        
        // If already exiting, let it proceed
        if (_exitInProgress) return;

        // Cancel the close and use our async exit method instead
        // This ensures consistent behavior regardless of how close was triggered
        if (_stateMachine.CurrentState != ConnectionState.Disconnected)
        {
            e.Cancel = true;
            _exitInProgress = true;
            _ = ExitApplicationAsync();
            return;
        }

        // If already disconnected, just clean up and let close proceed
        SaveConfigFromUI();
        _stateMachine.Dispose();
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (_viewModel == null) return;
        
        if (WindowState == WindowState.Minimized && _viewModel.MinimizeToTray)
        {
            Hide();
        }
    }

    #endregion
}
