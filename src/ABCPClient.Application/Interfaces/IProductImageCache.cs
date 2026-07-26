namespace ABCPClient.Application.Interfaces;

/// <summary>
/// Локальный кэш изображений товаров.
/// </summary>
/// <remarks>
/// API отдаёт только имена файлов; сами изображения лежат на CDN платформы.
/// Кэш нужен, чтобы одна и та же картинка не скачивалась при каждом открытии заказа
/// и чтобы карточка открывалась без сети, если изображение уже загружалось.
/// </remarks>
public interface IProductImageCache
{
    /// <summary>
    /// Возвращает путь к локальной копии изображения, при необходимости скачивая его.
    /// </summary>
    /// <param name="imageName">
    /// Имя файла на CDN платформы (так его отдаёт API) либо полный адрес изображения
    /// (так он записан в выгрузке каталога магазина).
    /// </param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Полный путь к файлу или <c>null</c>, если изображение получить не удалось.</returns>
    Task<string?> GetOrDownloadAsync(string imageName, CancellationToken cancellationToken = default);
}
