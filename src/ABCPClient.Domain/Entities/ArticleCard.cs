using ABCPClient.Domain.Models;

namespace ABCPClient.Domain.Entities;

/// <summary>
/// Карточка товара, сохранённая локально.
/// </summary>
/// <remarks>
/// Карточки кэшируются в базе, потому что API ограничивает число запросов
/// в минуту/час/сутки (ошибка 303). Один и тот же артикул встречается во многих
/// заказах, поэтому повторно он не запрашивается: данные карточки практически
/// не меняются, а лимит вызовов расходуется только на новые артикулы.
/// </remarks>
public class ArticleCard
{
    /// <summary>Локальный первичный ключ.</summary>
    public int Id { get; set; }

    /// <summary>Имя производителя.</summary>
    public string Brand { get; set; } = string.Empty;

    /// <summary>
    /// Номер детали в том виде, в котором он был запрошен (как в позиции заказа).
    /// </summary>
    public string Number { get; set; } = string.Empty;

    /// <summary>
    /// «Очищенный» номер из ответа API: <c>ADW0855</c> вместо <c>ADW-0855</c>.
    /// </summary>
    public string? NumberFix { get; set; }

    /// <summary>
    /// Сопоставительный ключ «бренд + номер» без разделителей и регистра.
    /// </summary>
    /// <remarks>
    /// Заполняется репозиторием при сохранении. Нужен потому, что один и тот же
    /// артикул записан по-разному в заказе и в каталоге (<c>ADW-0855</c> против
    /// <c>ADW0855</c>): без него карточка из каталога не находится и приложение
    /// зря идёт в API. Подробности — в <see cref="Models.ArticleKey"/>.
    /// </remarks>
    public string MatchKey { get; set; } = string.Empty;

    /// <summary>Локализованное описание детали.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// Основное изображение: либо имя файла на CDN платформы (так его отдаёт API),
    /// либо полный адрес — так изображения записаны в выгрузке каталога магазина.
    /// </summary>
    public string? ImageName { get; set; }

    /// <summary>Количество изображений в карточке.</summary>
    public int ImagesCount { get; set; }

    /// <summary>Свойства детали в виде JSON, как их отдал API.</summary>
    public string? PropertiesJson { get; set; }

    /// <summary>
    /// Карточки для этой детали в портале нет. Признак нужен, чтобы не запрашивать
    /// её снова при каждом открытии заказа.
    /// </summary>
    public bool NotFound { get; set; }

    /// <summary>
    /// Штрихкоды детали через точку с запятой. Заполняются из выгрузки каталога:
    /// API их не отдаёт. Нужны для поиска по сканеру на терминале сборки.
    /// </summary>
    public string? Barcodes { get; set; }

    /// <summary>Откуда получена карточка.</summary>
    public ArticleCardSource Source { get; set; }

    /// <summary>Момент получения карточки.</summary>
    public DateTime SyncedAt { get; set; }
}
