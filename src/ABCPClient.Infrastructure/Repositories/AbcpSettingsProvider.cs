using System.Globalization;
using ABCPClient.Application.Configuration;
using ABCPClient.Application.Interfaces;
using Microsoft.Extensions.Options;

namespace ABCPClient.Infrastructure.Repositories;

/// <summary>
/// Собирает действующие настройки: база данных перекрывает <c>appsettings.json</c>.
/// </summary>
/// <remarks>
/// Файл конфигурации задаёт значения по умолчанию при первом запуске,
/// окно настроек пишет в базу. Реквизиты доступа к API живут только в базе,
/// в зашифрованном виде, поэтому читать их через <c>IOptions</c> нельзя.
/// </remarks>
public sealed class AbcpSettingsProvider : IAbcpSettingsProvider
{
    private readonly IAppSettingsStore _store;
    private readonly IOptionsMonitor<AbcpApiOptions> _apiDefaults;
    private readonly IOptionsMonitor<SyncOptions> _syncDefaults;
    private readonly IOptionsMonitor<CatalogOptions> _catalogDefaults;

    /// <summary>
    /// Создаёт поставщик настроек.
    /// </summary>
    /// <param name="store">Хранилище пользовательских настроек.</param>
    /// <param name="apiDefaults">Значения по умолчанию для параметров API.</param>
    /// <param name="syncDefaults">Значения по умолчанию для параметров синхронизации.</param>
    /// <param name="catalogDefaults">Значения по умолчанию для импорта каталога.</param>
    public AbcpSettingsProvider(
        IAppSettingsStore store,
        IOptionsMonitor<AbcpApiOptions> apiDefaults,
        IOptionsMonitor<SyncOptions> syncDefaults,
        IOptionsMonitor<CatalogOptions> catalogDefaults)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(apiDefaults);
        ArgumentNullException.ThrowIfNull(syncDefaults);
        ArgumentNullException.ThrowIfNull(catalogDefaults);

        _store = store;
        _apiDefaults = apiDefaults;
        _syncDefaults = syncDefaults;
        _catalogDefaults = catalogDefaults;
    }

    /// <inheritdoc />
    public async Task<AbcpApiOptions> GetApiOptionsAsync(CancellationToken cancellationToken = default)
    {
        AbcpApiOptions defaults = _apiDefaults.CurrentValue;
        IReadOnlyDictionary<string, string?> stored = await _store
            .GetAllAsync(cancellationToken)
            .ConfigureAwait(false);

        return new AbcpApiOptions
        {
            BaseUrl = NormalizeBaseUrl(Text(stored, AppSettingKeys.ApiBaseUrl) ?? defaults.BaseUrl),
            Login = Text(stored, AppSettingKeys.ApiLogin) ?? defaults.Login,
            PasswordMd5 = Text(stored, AppSettingKeys.ApiPasswordMd5) ?? defaults.PasswordMd5,
            TimeoutSeconds = Number(stored, AppSettingKeys.ApiTimeoutSeconds) ?? defaults.TimeoutSeconds,
            RetryCount = defaults.RetryCount,
            PageSize = Math.Clamp(defaults.PageSize, 1, AbcpApiOptions.MaxPageSize),
        };
    }

    /// <inheritdoc />
    public async Task<SyncOptions> GetSyncOptionsAsync(CancellationToken cancellationToken = default)
    {
        SyncOptions defaults = _syncDefaults.CurrentValue;
        IReadOnlyDictionary<string, string?> stored = await _store
            .GetAllAsync(cancellationToken)
            .ConfigureAwait(false);

        return new SyncOptions
        {
            Enabled = defaults.Enabled,
            PollingIntervalSeconds =
                Number(stored, AppSettingKeys.SyncPollingIntervalSeconds) ?? defaults.PollingIntervalSeconds,
            OverlapMinutes = defaults.OverlapMinutes,
            InitialSyncDays = defaults.InitialSyncDays,
            NotificationsEnabled =
                Flag(stored, AppSettingKeys.SyncNotificationsEnabled) ?? defaults.NotificationsEnabled,
            ArticleCardRequestsPerMinute = defaults.ArticleCardRequestsPerMinute,
            ArticleCardRequestsPerHour = defaults.ArticleCardRequestsPerHour,
            ArticleCardRequestsPerDay = defaults.ArticleCardRequestsPerDay,
            ArticleCardCooldownMinutes = defaults.ArticleCardCooldownMinutes,
        };
    }

    /// <inheritdoc />
    public async Task<CatalogOptions> GetCatalogOptionsAsync(CancellationToken cancellationToken = default)
    {
        CatalogOptions defaults = _catalogDefaults.CurrentValue;
        IReadOnlyDictionary<string, string?> stored = await _store
            .GetAllAsync(cancellationToken)
            .ConfigureAwait(false);

        return new CatalogOptions
        {
            FeedPath = Text(stored, AppSettingKeys.CatalogFeedPath) ?? defaults.FeedPath,
            AutoImportHours = defaults.AutoImportHours,
            PrefetchImages = Flag(stored, AppSettingKeys.CatalogPrefetchImages) ?? defaults.PrefetchImages,
            StorefrontUrl = Text(stored, AppSettingKeys.CatalogStorefrontUrl) ?? defaults.StorefrontUrl,
            StorefrontRequestsPerMinute = defaults.StorefrontRequestsPerMinute,
        };
    }

    private static string? Text(IReadOnlyDictionary<string, string?> stored, string key) =>
        stored.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;

    private static int? Number(IReadOnlyDictionary<string, string?> stored, string key) =>
        Text(stored, key) is { } raw
        && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : null;

    private static bool? Flag(IReadOnlyDictionary<string, string?> stored, string key)
    {
        string? raw = Text(stored, key);
        if (raw is null)
        {
            return null;
        }

        if (bool.TryParse(raw, out bool parsed))
        {
            return parsed;
        }

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)
            ? number != 0
            : null;
    }

    /// <summary>
    /// Убирает завершающий слеш, чтобы пути операций склеивались предсказуемо.
    /// </summary>
    private static string NormalizeBaseUrl(string baseUrl) =>
        string.IsNullOrWhiteSpace(baseUrl) ? string.Empty : baseUrl.Trim().TrimEnd('/');
}
