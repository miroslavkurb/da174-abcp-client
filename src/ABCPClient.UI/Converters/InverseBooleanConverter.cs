using System.Globalization;
using System.Windows.Data;

namespace ABCPClient.UI.Converters;

/// <summary>
/// Инвертирует логическое значение: используется, чтобы гасить кнопки во время работы.
/// </summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool flag ? !flag : true;

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool flag ? !flag : true;
}
