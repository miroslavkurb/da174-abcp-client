namespace ABCPClient.Application.DTO;

/// <summary>
/// Итог импорта каталога магазина.
/// </summary>
/// <param name="Source">Откуда читалась выгрузка.</param>
/// <param name="FeedDate">Дата формирования выгрузки, если она указана в файле.</param>
/// <param name="Offers">Сколько предложений прочитано.</param>
/// <param name="Cards">Сколько карточек сохранено в кэше.</param>
/// <param name="WithImages">Сколько карточек получили изображение.</param>
/// <param name="WithBarcodes">Сколько карточек получили штрихкод.</param>
/// <param name="Skipped">Сколько предложений пропущено из-за отсутствия бренда или артикула.</param>
/// <param name="ImagesDownloaded">Сколько изображений скачано на диск при импорте.</param>
/// <param name="Elapsed">Длительность импорта.</param>
public sealed record CatalogImportResult(
    string Source,
    DateTimeOffset? FeedDate,
    int Offers,
    int Cards,
    int WithImages,
    int WithBarcodes,
    int Skipped,
    int ImagesDownloaded,
    TimeSpan Elapsed);

/// <summary>
/// Ход импорта каталога.
/// </summary>
/// <param name="Stage">Что выполняется сейчас.</param>
/// <param name="Processed">Сколько элементов обработано.</param>
/// <param name="Total">Сколько элементов всего, если известно.</param>
public sealed record CatalogImportProgress(string Stage, int Processed, int? Total = null);
