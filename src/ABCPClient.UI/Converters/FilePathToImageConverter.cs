using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace ABCPClient.UI.Converters;

/// <summary>
/// Превращает путь к файлу изображения в источник для <c>Image</c>.
/// </summary>
/// <remarks>
/// Прямая привязка пути к <c>Image.Source</c> не годится по двум причинам:
/// стандартный преобразователь не принимает <c>null</c> (а у товара может не быть фото)
/// и загружает файл в потоковом режиме, оставляя его открытым — после этого кэш
/// изображений нельзя обновить или очистить.
/// Здесь используется <see cref="BitmapCacheOption.OnLoad"/>: файл читается целиком
/// и сразу закрывается.
/// </remarks>
public sealed class FilePathToImageConverter : IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            BitmapImage image = new();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();

            return image;
        }
        catch (Exception exception) when (exception is NotSupportedException or IOException or UriFormatException)
        {
            // Битый или недокачанный файл не должен ломать отрисовку списка позиций.
            return null;
        }
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
