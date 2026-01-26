using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;
using PingTunnelVPN.Core;

namespace PingTunnelVPN.App;

/// <summary>
/// Manages the system tray icon and context menu.
/// </summary>
public class TrayIconManager : IDisposable
{
    private readonly TaskbarIcon _taskbarIcon;
    private readonly ConnectionStateMachine _stateMachine;
    private readonly Window _mainWindow;
    private readonly string _iconPath;
    private readonly string _iconOffPath;
    private bool _disposed;

    public TrayIconManager(Window mainWindow, ConnectionStateMachine stateMachine)
    {
        _mainWindow = mainWindow;
        _stateMachine = stateMachine;

        // Set up icon paths (icon.ico = connected, icon-off.ico = disconnected)
        _iconPath = AppIcons.IconPath;
        _iconOffPath = AppIcons.IconOffPath;

        _taskbarIcon = new TaskbarIcon
        {
            ToolTipText = "PingTunnelVPN - Disconnected",
            ContextMenu = CreateContextMenu()
        };

        // Set initial icon (disconnected state - use icon-off.ico)
        UpdateIcon(ConnectionState.Disconnected);

        _taskbarIcon.TrayMouseDoubleClick += OnTrayDoubleClick;

        // Subscribe to state changes
        _stateMachine.StateChanged += OnStateChanged;
    }

    /// <summary>
    /// Updates the tray icon based on connection state.
    /// Uses icon-off.ico when disconnected, icon.ico when connected.
    /// </summary>
    private void UpdateIcon(ConnectionState state)
    {
        try
        {
            string iconPathToUse;
            
            // Use icon-off.ico when disconnected, icon.ico when connected
            if (state == ConnectionState.Disconnected || state == ConnectionState.Error)
            {
                iconPathToUse = File.Exists(_iconOffPath) ? _iconOffPath : _iconPath;
            }
            else
            {
                iconPathToUse = _iconPath;
            }

            if (File.Exists(iconPathToUse))
            {
                _taskbarIcon.Icon = new Icon(iconPathToUse);
            }
            else
            {
                // Fallback to system icon if custom icons don't exist
                _taskbarIcon.Icon = SystemIcons.Shield;
            }
        }
        catch
        {
            // Fallback to system icon on any error
            _taskbarIcon.Icon = SystemIcons.Shield;
        }
    }

    private ContextMenu CreateContextMenu()
    {
        var menu = new ContextMenu();

        var connectItem = new MenuItem { Header = "Connect", Name = "ConnectMenuItem" };
        connectItem.Click += async (s, e) => await OnConnectClick();
        menu.Items.Add(connectItem);

        var disconnectItem = new MenuItem { Header = "Disconnect", Name = "DisconnectMenuItem", IsEnabled = false };
        disconnectItem.Click += async (s, e) => await OnDisconnectClick();
        menu.Items.Add(disconnectItem);

        menu.Items.Add(new Separator());

        var openItem = new MenuItem { Header = "Open PingTunnelVPN" };
        openItem.Click += (s, e) => ShowMainWindow();
        menu.Items.Add(openItem);

        menu.Items.Add(new Separator());

        var exitItem = new MenuItem { Header = "Exit" };
        exitItem.Click += async (s, e) => await OnExitClick();
        menu.Items.Add(exitItem);

        return menu;
    }

    private void OnTrayDoubleClick(object? sender, RoutedEventArgs e)
    {
        ShowMainWindow();
    }

    private void ShowMainWindow()
    {
        try
        {
            if (_mainWindow.IsLoaded)
            {
                _mainWindow.Show();
                _mainWindow.WindowState = WindowState.Normal;
                _mainWindow.Activate();
            }
        }
        catch (InvalidOperationException)
        {
            // Window has been closed - ignore
        }
    }

    private async Task OnConnectClick()
    {
        if (_stateMachine.CurrentState == ConnectionState.Disconnected)
        {
            await _stateMachine.ConnectAsync();
        }
    }

    private async Task OnDisconnectClick()
    {
        if (_stateMachine.CurrentState == ConnectionState.Connected)
        {
            await _stateMachine.DisconnectAsync();
        }
    }

    private async Task OnExitClick()
    {
        // Ensure clean disconnect before exit
        if (_stateMachine.CurrentState == ConnectionState.Connected)
        {
            await _stateMachine.DisconnectAsync();
        }

        System.Windows.Application.Current.Shutdown();
    }

    private void OnStateChanged(object? sender, ConnectionStateChangedEventArgs e)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            UpdateIcon(e.NewState);
            UpdateTooltip(e.NewState);
            UpdateMenuItems(e.NewState);
        });
    }

    private void UpdateTooltip(ConnectionState state)
    {
        _taskbarIcon.ToolTipText = state switch
        {
            ConnectionState.Disconnected => "PingTunnelVPN - Disconnected",
            ConnectionState.Connecting => "PingTunnelVPN - Connecting...",
            ConnectionState.Connected => $"PingTunnelVPN - Connected",
            ConnectionState.Disconnecting => "PingTunnelVPN - Disconnecting...",
            ConnectionState.Error => "PingTunnelVPN - Error",
            _ => "PingTunnelVPN"
        };
    }

    private void UpdateMenuItems(ConnectionState state)
    {
        if (_taskbarIcon.ContextMenu == null) return;

        foreach (var item in _taskbarIcon.ContextMenu.Items.OfType<MenuItem>())
        {
            switch (item.Name)
            {
                case "ConnectMenuItem":
                    item.IsEnabled = state == ConnectionState.Disconnected;
                    break;
                case "DisconnectMenuItem":
                    item.IsEnabled = state == ConnectionState.Connected;
                    break;
            }
        }
    }

    public void UpdateConnectionStats(TimeSpan uptime, long bytesReceived, long bytesSent, double tunRxBps = 0, double tunTxBps = 0)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (_stateMachine.CurrentState == ConnectionState.Connected)
            {
                var usage = $"↓ {FormattingHelper.FormatBytes(bytesReceived)} / ↑ {FormattingHelper.FormatBytes(bytesSent)}";
                var speed = $"↓ {FormattingHelper.FormatSpeed(tunRxBps)} / ↑ {FormattingHelper.FormatSpeed(tunTxBps)}";
                _taskbarIcon.ToolTipText = $"PingTunnelVPN - Connected\nUptime: {uptime:hh\\:mm\\:ss}\n{usage}\n{speed}";
            }
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _stateMachine.StateChanged -= OnStateChanged;
        _taskbarIcon.Dispose();
    }
}
