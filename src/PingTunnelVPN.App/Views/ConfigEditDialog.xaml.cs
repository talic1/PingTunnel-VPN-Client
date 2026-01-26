using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PingTunnelVPN.App.ViewModels;
using PingTunnelVPN.Core;

namespace PingTunnelVPN.App.Views;

/// <summary>
/// Interaction logic for ConfigEditDialog.xaml
/// </summary>
public partial class ConfigEditDialog : Window
{
    private readonly ConfigEditViewModel _viewModel;
    private readonly ConfigManager _configManager;
    private readonly Guid? _configId;
    private bool _isPasswordVisible = false;

    public ConfigEditDialog(ConfigManager configManager, Guid? configId = null)
    {
        InitializeComponent();
        _configManager = configManager;
        _configId = configId;
        _viewModel = new ConfigEditViewModel();

        if (configId.HasValue)
        {
            // Editing existing config
            DialogTitle.Text = "Edit Configuration";
            var saveButton = FindName("SaveButton") as System.Windows.Controls.Button;
            
            var config = _configManager.GetConfig(configId.Value);
            if (config != null)
            {
                _viewModel.ConfigName = config.Name;
                LoadConfig(config.Configuration);
            }
        }
        else
        {
            // Creating new config
            DialogTitle.Text = "Add Configuration";
            _viewModel.ConfigName = "Server 1";
        }

        DataContext = _viewModel;
        LoadConfigToUI();

        // Set window icon (icon.ico)
        SetAppIcon();

        // Allow window dragging
        MouseLeftButtonDown += (s, e) => { if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove(); };
    }

    private void SetAppIcon()
    {
        try
        {
            if (!File.Exists(AppIcons.IconPath)) return;
            var uri = new Uri(AppIcons.IconPath, UriKind.Absolute);
            Icon = BitmapFrame.Create(uri);
        }
        catch
        {
            // Ignore icon load errors
        }
    }

    public ServerConfig? Result { get; private set; }

    private void LoadConfig(VpnConfiguration config)
    {
        // Only load server-specific settings
        _viewModel.ServerAddress = config.ServerAddress;
        _viewModel.ServerKey = config.ServerKey;
        _viewModel.LocalSocksPort = config.LocalSocksPort;
    }

    private void LoadConfigToUI()
    {
        ServerKeyPasswordBox.Password = _viewModel.ServerKey;
    }

    private void SaveConfigFromUI()
    {
        if (!_isPasswordVisible)
        {
            _viewModel.ServerKey = ServerKeyPasswordBox.Password;
        }
    }

    private void ServerKeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (!_isPasswordVisible)
        {
            _viewModel.ServerKey = ServerKeyPasswordBox.Password;
        }
    }

    private void TogglePasswordVisibility_Click(object sender, RoutedEventArgs e)
    {
        _isPasswordVisible = !_isPasswordVisible;

        if (_isPasswordVisible)
        {
            // Show password in TextBox
            ServerKeyTextBox.Text = ServerKeyPasswordBox.Password;
            ServerKeyTextBox.Visibility = Visibility.Visible;
            ServerKeyPasswordBox.Visibility = Visibility.Hidden;
            PasswordToggleIcon.Data = (Geometry)FindResource("Icon.EyeOff");
        }
        else
        {
            // Hide password in PasswordBox
            ServerKeyPasswordBox.Password = ServerKeyTextBox.Text;
            ServerKeyPasswordBox.Visibility = Visibility.Visible;
            ServerKeyTextBox.Visibility = Visibility.Collapsed;
            PasswordToggleIcon.Data = (Geometry)FindResource("Icon.Eye");
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        SaveConfigFromUI();

        // Validate required fields
        if (string.IsNullOrWhiteSpace(_viewModel.ConfigName))
        {
            MessageBox.Show(
                "Configuration name is required.",
                "Validation Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(_viewModel.ServerAddress))
        {
            MessageBox.Show(
                "Server address is required.",
                "Validation Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(_viewModel.ServerKey))
        {
            MessageBox.Show(
                "Server key is required.",
                "Validation Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // Create config
        var config = CreateConfig();
        var errors = config.Validate();
        if (errors.Count > 0)
        {
            MessageBox.Show(
                string.Join("\n", errors),
                "Configuration Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // Create ServerConfig
        var serverConfig = new ServerConfig
        {
            Id = _configId ?? Guid.NewGuid(),
            Name = _viewModel.ConfigName.Trim(),
            Configuration = config
        };

        Result = serverConfig;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private VpnConfiguration CreateConfig()
    {
        // Only set server-specific settings
        return new VpnConfiguration
        {
            ServerAddress = _viewModel.ServerAddress,
            ServerKey = _viewModel.ServerKey,
            LocalSocksPort = _viewModel.LocalSocksPort
        };
    }
}

/// <summary>
/// ViewModel for the config edit dialog.
/// </summary>
public class ConfigEditViewModel : System.ComponentModel.INotifyPropertyChanged
{
    // Only server-specific settings (global settings are managed in MainWindow Advanced tab)
    private string _configName = string.Empty;
    private string _serverAddress = string.Empty;
    private string _serverKey = string.Empty;
    private int _localSocksPort = 1080;

    public string ConfigName
    {
        get => _configName;
        set { _configName = value; OnPropertyChanged(); }
    }

    public string ServerAddress
    {
        get => _serverAddress;
        set { _serverAddress = value; OnPropertyChanged(); }
    }

    public string ServerKey
    {
        get => _serverKey;
        set { _serverKey = value; OnPropertyChanged(); }
    }

    public int LocalSocksPort
    {
        get => _localSocksPort;
        set { _localSocksPort = value; OnPropertyChanged(); }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
    }
}
