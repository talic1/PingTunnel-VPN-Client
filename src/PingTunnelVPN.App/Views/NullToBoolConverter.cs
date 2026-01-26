using System.Globalization;
using System.Windows.Data;

namespace PingTunnelVPN.App.Views;

/// <summary>
/// Converts null to boolean. If ConverterParameter is "Invert", inverts the result.
/// </summary>
public class NullToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isNull = value == null;
        var invert = parameter?.ToString() == "Invert";
        return invert ? !isNull : isNull;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
