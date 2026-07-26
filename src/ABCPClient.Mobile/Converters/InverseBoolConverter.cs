using System.Globalization;

namespace ABCPClient.Mobile.Converters;

/// <summary>
/// Обращает логическое значение.
/// </summary>
/// <remarks>
/// Нужен, чтобы кнопки выключались на время работы: в разметке есть признак
/// «занято», а требуется «доступно».
/// </remarks>
public sealed class InverseBoolConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not bool flag || !flag;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not bool flag || !flag;
}
