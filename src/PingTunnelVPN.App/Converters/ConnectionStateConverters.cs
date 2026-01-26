using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using PingTunnelVPN.Core;

namespace PingTunnelVPN.App.Converters;

/// <summary>
/// Converts ConnectionState to the appropriate status text.
/// </summary>
public class ConnectionStateToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not ConnectionState state)
            return "Unknown";

        return state switch
        {
            ConnectionState.Disconnected => "Disconnected",
            ConnectionState.Connecting => "Connecting...",
            ConnectionState.Connected => "Connected",
            ConnectionState.Disconnecting => "Disconnecting...",
            ConnectionState.Error => "Error",
            _ => "Unknown"
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts ConnectionState to visibility for connected-only elements.
/// </summary>
public class ConnectionStateToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not ConnectionState state)
            return Visibility.Collapsed;

        var targetState = parameter?.ToString() ?? "Connected";
        
        return targetState switch
        {
            "Connected" => state == ConnectionState.Connected ? Visibility.Visible : Visibility.Collapsed,
            "Disconnected" => state == ConnectionState.Disconnected || state == ConnectionState.Error 
                ? Visibility.Visible : Visibility.Collapsed,
            "NotConnected" => state != ConnectionState.Connected ? Visibility.Visible : Visibility.Collapsed,
            _ => Visibility.Collapsed
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts ConnectionState to boolean (true if connected).
/// </summary>
public class ConnectionStateToConnectedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is ConnectionState state && state == ConnectionState.Connected;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts ConnectionState to button text (Connect/Disconnect).
/// </summary>
public class ConnectionStateToButtonTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not ConnectionState state)
            return "Connect";

        return state switch
        {
            ConnectionState.Connected => "Disconnect",
            ConnectionState.Connecting => "Connecting...",
            ConnectionState.Disconnecting => "Disconnecting...",
            _ => "Connect"
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Returns true if the connection can be toggled (not in transitional state).
/// </summary>
public class ConnectionStateToCanToggleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not ConnectionState state)
            return true;

        return state == ConnectionState.Connected || 
               state == ConnectionState.Disconnected || 
               state == ConnectionState.Error;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
