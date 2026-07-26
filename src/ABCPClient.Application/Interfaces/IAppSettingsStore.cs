namespace ABCPClient.Application.Interfaces;

/// <summary>
/// Локальное хранилище пользовательских настроек («ключ — значение»).
/// </summary>
public interface IAppSettingsStore
{
    /// <summary>
    /// Возвращает значение настройки или <c>null</c>, если она не задана.
    /// Защищённые значения расшифровываются автоматически.
    /// </summary>
    /// <param name="key">Ключ настройки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Сохраняет значение настройки.
    /// </summary>
    /// <param name="key">Ключ настройки.</param>
    /// <param name="value">Значение; <c>null</c> сохраняется как отсутствующее значение.</param>
    /// <param name="protect">Шифровать значение перед записью на диск.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task SetAsync(string key, string? value, bool protect = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает все настройки в открытом виде.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<IReadOnlyDictionary<string, string?>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Удаляет настройку.
    /// </summary>
    /// <param name="key">Ключ настройки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><c>true</c>, если настройка существовала и была удалена.</returns>
    Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default);
}

/// <summary>
/// Ключи настроек, сохраняемых в локальной базе.
/// </summary>
/// <remarks>
/// Значения из базы имеют приоритет над <c>appsettings.json</c>: файл задаёт значения
/// по умолчанию при первом запуске, а окно настроек пишет в базу.
/// </remarks>
public static class AppSettingKeys
{
    /// <summary>Базовый адрес API (выдаётся менеджером ABCP).</summary>
    public const string ApiBaseUrl = "Abcp:BaseUrl";

    /// <summary>Логин API-администратора.</summary>
    public const string ApiLogin = "Abcp:Login";

    /// <summary>md5-хэш пароля API-администратора. Хранится зашифрованным.</summary>
    public const string ApiPasswordMd5 = "Abcp:PasswordMd5";

    /// <summary>Таймаут запроса в секундах.</summary>
    public const string ApiTimeoutSeconds = "Abcp:TimeoutSeconds";

    /// <summary>Интервал опроса в секундах.</summary>
    public const string SyncPollingIntervalSeconds = "Sync:PollingIntervalSeconds";

    /// <summary>Признак включённых уведомлений.</summary>
    public const string SyncNotificationsEnabled = "Sync:NotificationsEnabled";

    /// <summary>Момент последней успешной синхронизации (время портала).</summary>
    public const string SyncLastSyncAt = "Sync:LastSyncAt";

    /// <summary>
    /// Расход лимита запросов карточек товаров: счётчики за минуту, час и сутки в JSON.
    /// </summary>
    /// <remarks>
    /// Хранится в базе, а не в памяти: лимиты API считаются на его стороне и перезапуск
    /// приложения их не обнуляет. Иначе после рестарта расход начинался бы с нуля
    /// и первое же открытие заказа упиралось бы в ошибку 303.
    /// </remarks>
    public const string ArticleCardUsage = "Sync:ArticleCardUsage";

    /// <summary>До какого момента запросы карточек приостановлены после ошибки 303.</summary>
    public const string ArticleCardBlockedUntil = "Sync:ArticleCardBlockedUntil";

    /// <summary>Путь или адрес выгрузки каталога магазина (YML).</summary>
    public const string CatalogFeedPath = "Catalog:FeedPath";

    /// <summary>Скачивать изображения каталога сразу при импорте.</summary>
    public const string CatalogPrefetchImages = "Catalog:PrefetchImages";

    /// <summary>Адрес витрины магазина — источник карточек для деталей под заказ.</summary>
    public const string CatalogStorefrontUrl = "Catalog:StorefrontUrl";

    /// <summary>Момент последнего импорта каталога.</summary>
    public const string CatalogLastImportAt = "Catalog:LastImportAt";
}
