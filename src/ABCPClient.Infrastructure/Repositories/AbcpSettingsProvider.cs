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
    private readonly IOptionsMonitor<UpdateOptions> _updateDefaults;
    private readonly IOptionsMonitor<PickingOptions> _pickingDefaults;

    /// <summary>
    /// Создаёт поставщик настроек.
    /// </summary>
    /// <param name="store">Хранилище пользовательских настроек.</param>
    /// <param name="apiDefaults">Значения по умолчанию для параметров API.</param>
    /// <param name="syncDefaults">Значения по умолчанию для параметров синхронизации.</param>
    /// <param name="catalogDefaults">Значения по умолчанию для импорта каталога.</param>
    /// <param name="updateDefaults">Значения по умолчанию для проверки обновлений.</param>
    /// <param name="pickingDefaults">Значения по умолчанию для сборки заказов.</param>
    public AbcpSettingsProvider(
        IAppSettingsStore store,
        IOptionsMonitor<AbcpApiOptions> apiDefaults,
        IOptionsMonitor<SyncOptions> syncDefaults,
        IOptionsMonitor<CatalogOptions> catalogDefaults,
        IOptionsMonitor<UpdateOptions> updateDefaults,
        IOptionsMonitor<PickingOptions> pickingDefaults)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(apiDefaults);
        ArgumentNullException.ThrowIfNull(syncDefaults);
        ArgumentNullException.ThrowIfNull(catalogDefaults);
        ArgumentNullException.ThrowIfNull(updateDefaults);
        ArgumentNullException.ThrowIfNull(pickingDefaults);

        _store = store;
        _apiDefaults = apiDefaults;
        _syncDefaults = syncDefaults;
        _catalogDefaults = catalogDefaults;
        _updateDefaults = updateDefaults;
        _pickingDefaults = pickingDefaults;
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

    /// <inheritdoc />
    public async Task<UpdateOptions> GetUpdateOptionsAsync(CancellationToken cancellationToken = default)
    {
        UpdateOptions defaults = _updateDefaults.CurrentValue;
        IReadOnlyDictionary<string, string?> stored = await _store
            .GetAllAsync(cancellationToken)
            .ConfigureAwait(false);

        return new UpdateOptions
        {
            Repository = Text(stored, AppSettingKeys.UpdatesRepository) ?? defaults.Repository,
            Token = Text(stored, AppSettingKeys.UpdatesToken) ?? defaults.Token,
            CheckOnStartup = Flag(stored, AppSettingKeys.UpdatesCheckOnStartup) ?? defaults.CheckOnStartup,
            IncludePrerelease =
                Flag(stored, AppSettingKeys.UpdatesIncludePrerelease) ?? defaults.IncludePrerelease,
            CheckIntervalHours = defaults.CheckIntervalHours,
            AssetPattern = defaults.AssetPattern,
            ChecksumAssetName = defaults.ChecksumAssetName,
        };
    }

    /// <inheritdoc />
    public async Task<PickingOptions> GetPickingOptionsAsync(CancellationToken cancellationToken = default)
    {
        PickingOptions defaults = _pickingDefaults.CurrentValue;
        IReadOnlyDictionary<string, string?> stored = await _store
            .GetAllAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PickingOptions
        {
            NumberPrefix = defaults.NumberPrefix,
            InStockStatusCodes =
                Codes(stored, AppSettingKeys.PickingInStockStatusCodes) ?? defaults.InStockStatusCodes,
            IncomingStatusCodes =
                Codes(stored, AppSettingKeys.PickingIncomingStatusCodes) ?? defaults.IncomingStatusCodes,
            SkipCancelledPositions = defaults.SkipCancelledPositions,
            TreatDeadlineAsIncoming = defaults.TreatDeadlineAsIncoming,
        };
    }

    /// <summary>
    /// Разбирает список кодов статусов, записанный через запятую.
    /// </summary>
    /// <remarks>
    /// Список хранится строкой, потому что таблица настроек — «ключ и значение».
    /// Нечисловые части молча отбрасываются: пользователь мог оставить пробел
    /// или лишнюю запятую, и ронять из-за этого настройки незачем.
    /// </remarks>
    private static IReadOnlyList<int>? Codes(IReadOnlyDictionary<string, string?> stored, string key)
    {
        string? raw = Text(stored, key);
        if (raw is null)
        {
            return null;
        }

        int[] codes = raw
            .Split((char[])[',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part =>
                int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out int code) ? code : (int?)null)
            .Where(code => code.HasValue)
            .Select(code => code!.Value)
            .Distinct()
            .ToArray();

        return codes;
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
