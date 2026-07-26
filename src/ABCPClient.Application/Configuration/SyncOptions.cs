namespace ABCPClient.Application.Configuration;

/// <summary>
/// Параметры фоновой синхронизации заказов.
/// </summary>
public sealed class SyncOptions
{
    /// <summary>Имя секции в конфигурации.</summary>
    public const string SectionName = "Sync";

    /// <summary>Включена ли фоновая синхронизация.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Интервал опроса API, секунды.</summary>
    public int PollingIntervalSeconds { get; set; } = 120;

    /// <summary>
    /// Перекрытие окна выборки, минуты: запрос идёт от <c>LastSyncAt - Overlap</c>.
    /// Защищает от потери заказов, обновлённых в ту же секунду, что и предыдущий срез.
    /// </summary>
    public int OverlapMinutes { get; set; } = 5;

    /// <summary>
    /// Глубина первой синхронизации, дни. Если фильтр по дате не передан,
    /// API само ограничивает выборку последними 30 днями.
    /// </summary>
    public int InitialSyncDays { get; set; } = 30;

    /// <summary>Показывать ли уведомления Windows.</summary>
    public bool NotificationsEnabled { get; set; } = true;

    /// <summary>
    /// Сколько карточек товаров разрешено запрашивать в минуту.
    /// </summary>
    /// <remarks>
    /// API ограничивает частоту обращений и при превышении отвечает ошибкой 303,
    /// блокируя доступ на время. Значение подобрано с запасом: карточки кэшируются,
    /// поэтому повторные открытия заказов запросов не тратят.
    /// </remarks>
    public int ArticleCardRequestsPerMinute { get; set; } = 20;

    /// <summary>
    /// Сколько карточек товаров разрешено запрашивать в час.
    /// </summary>
    /// <remarks>
    /// Лимиты API считаются в минуту, час и сутки одновременно, поэтому одного
    /// ограничения в минуту мало: несколько заказов подряд выбирают часовой лимит,
    /// не превысив минутный.
    /// </remarks>
    public int ArticleCardRequestsPerHour { get; set; } = 300;

    /// <summary>
    /// Сколько карточек товаров разрешено запрашивать в сутки.
    /// </summary>
    public int ArticleCardRequestsPerDay { get; set; } = 1500;

    /// <summary>
    /// На сколько минут прекращать запросы карточек после ошибки 303.
    /// </summary>
    public int ArticleCardCooldownMinutes { get; set; } = 15;
}
