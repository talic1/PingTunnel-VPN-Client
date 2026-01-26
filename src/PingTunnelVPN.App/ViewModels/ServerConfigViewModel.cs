using System.ComponentModel;
using System.Runtime.CompilerServices;
using PingTunnelVPN.Core;

namespace PingTunnelVPN.App.ViewModels;

/// <summary>
/// ViewModel wrapper for ServerConfig for UI binding.
/// </summary>
public class ServerConfigViewModel : INotifyPropertyChanged
{
    private readonly ServerConfig _config;
    private bool _isSelected;
    private bool _isConnected;

    public ServerConfigViewModel(ServerConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>
    /// Gets the underlying ServerConfig.
    /// </summary>
    public ServerConfig Config => _config;

    /// <summary>
    /// Gets the unique identifier.
    /// </summary>
    public Guid Id => _config.Id;

    /// <summary>
    /// Gets or sets the configuration name.
    /// </summary>
    public string Name
    {
        get => _config.Name;
        set
        {
            if (_config.Name != value)
            {
                _config.Name = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Gets the server address for display.
    /// </summary>
    public string ServerAddress => _config.Configuration.ServerAddress;

    /// <summary>
    /// Gets or sets whether this config is currently selected.
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Gets or sets whether this config is currently connected.
    /// </summary>
    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            if (_isConnected != value)
            {
                _isConnected = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Gets the display text for the config.
    /// </summary>
    public string DisplayText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ServerAddress))
                return Name;
            return $"{Name} ({ServerAddress})";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
