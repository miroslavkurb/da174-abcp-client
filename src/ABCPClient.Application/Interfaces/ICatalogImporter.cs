using ABCPClient.Application.DTO;

namespace ABCPClient.Application.Interfaces;

/// <summary>
/// Импорт каталога магазина в локальный кэш карточек товаров.
/// </summary>
/// <remarks>
/// Выгрузка каталога заменяет тысячи обращений к API одним файлом: в ней есть
/// описания, свойства, изображения и штрихкоды по всему ассортименту магазина.
/// К API импорт не обращается вообще и лимит вызовов не расходует.
/// </remarks>
public interface ICatalogImporter
{
    /// <summary>
    /// Читает выгрузку и обновляет кэш карточек товаров.
    /// </summary>
    /// <param name="source">
    /// Путь к файлу или адрес выгрузки. Пусто — берётся путь из настроек.
    /// </param>
    /// <param name="progress">Приёмник сведений о ходе импорта.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<CatalogImportResult> ImportAsync(
        string? source = null,
        IProgress<CatalogImportProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
