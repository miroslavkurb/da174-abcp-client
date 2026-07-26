using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ABCPClient.Application.Configuration;
using ABCPClient.Application.DTO;
using ABCPClient.Application.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace ABCPClient.UI.ViewModels;

/// <summary>
/// Модель представления окна настроек.
/// </summary>
/// <remarks>
/// Пароль в приложении не хранится: он сразу превращается в md5-хэш (параметр
/// <c>userpsw</c> API) и сохраняется зашифрованным через DPAPI. В поле пароля можно
/// вставить и готовый хэш — тогда повторное хэширование не выполняется.
/// </remarks>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IAppSettingsStore _store;
    private readonly IAbcpSettingsProvider _settings;
    private readonly IPasswordHasher _hasher;
    private readonly IAbcpApiClient _api;
    private readonly IOrderSyncService _sync;
    private readonly ICatalogImporter _catalog;
    private readonly ILogger<SettingsViewModel> _logger;

    [ObservableProperty]
    private string? _baseUrl;

    [ObservableProperty]
    private string? _login;

    /// <summary>
    /// Введённый пароль или готовый md5-хэш. Пустое значение означает
    /// «оставить сохранённый пароль без изменений».
    /// </summary>
    [ObservableProperty]
    private string? _password;

    [ObservableProperty]
    private bool _hasStoredPassword;

    [ObservableProperty]
    private int _timeoutSeconds = 30;

    [ObservableProperty]
    private int _pollingIntervalSeconds = 120;

    [ObservableProperty]
    private bool _notificationsEnabled = true;

    /// <summary>Путь к выгрузке каталога магазина или её адрес.</summary>
    [ObservableProperty]
    private string? _catalogFeedPath;

    /// <summary>Скачивать изображения каталога сразу при импорте.</summary>
    [ObservableProperty]
    private bool _catalogPrefetchImages;

    /// <summary>Адрес витрины магазина — источник карточек для деталей под заказ.</summary>
    [ObservableProperty]
    private string? _storefrontUrl;

    /// <summary>Когда каталог импортировали в прошлый раз.</summary>
    [ObservableProperty]
    private string? _catalogLastImport;

    /// <summary>Репозиторий с релизами в виде «владелец/имя».</summary>
    [ObservableProperty]
    private string? _updatesRepository;

    /// <summary>
    /// Введённый токен доступа к GitHub. Пусто — оставить сохранённый.
    /// </summary>
    [ObservableProperty]
    private string? _updatesToken;

    /// <summary>Токен уже сохранён.</summary>
    [ObservableProperty]
    private bool _hasStoredUpdatesToken;

    /// <summary>Проверять обновления при запуске.</summary>
    [ObservableProperty]
    private bool _updatesCheckOnStartup = true;

    /// <summary>Учитывать предварительные выпуски.</summary>
    [ObservableProperty]
    private bool _updatesIncludePrerelease;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Настройки были сохранены.</summary>
    public bool IsSaved { get; private set; }

    /// <summary>
    /// Создаёт модель представления.
    /// </summary>
    public SettingsViewModel(
        IAppSettingsStore store,
        IAbcpSettingsProvider settings,
        IPasswordHasher hasher,
        IAbcpApiClient api,
        IOrderSyncService sync,
        ICatalogImporter catalog,
        ILogger<SettingsViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(hasher);
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(sync);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(logger);

        _store = store;
        _settings = settings;
        _hasher = hasher;
        _api = api;
        _sync = sync;
        _catalog = catalog;
        _logger = logger;
    }

    /// <summary>
    /// Загружает действующие настройки в поля окна.
    /// </summary>
    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        AbcpApiOptions api = await _settings.GetApiOptionsAsync(cancellationToken).ConfigureAwait(true);
        SyncOptions sync = await _settings.GetSyncOptionsAsync(cancellationToken).ConfigureAwait(true);
        CatalogOptions catalog = await _settings.GetCatalogOptionsAsync(cancellationToken).ConfigureAwait(true);

        BaseUrl = api.BaseUrl;
        Login = api.Login;
        TimeoutSeconds = api.TimeoutSeconds;
        PollingIntervalSeconds = sync.PollingIntervalSeconds;
        NotificationsEnabled = sync.NotificationsEnabled;

        CatalogFeedPath = catalog.FeedPath;
        CatalogPrefetchImages = catalog.PrefetchImages;
        StorefrontUrl = catalog.StorefrontUrl;

        UpdateOptions updates = await _settings.GetUpdateOptionsAsync(cancellationToken).ConfigureAwait(true);

        UpdatesRepository = updates.Repository;
        UpdatesCheckOnStartup = updates.CheckOnStartup;
        UpdatesIncludePrerelease = updates.IncludePrerelease;

        // Сам токен в интерфейс не выводится — только признак его наличия.
        HasStoredUpdatesToken = !string.IsNullOrWhiteSpace(updates.Token);
        UpdatesToken = null;

        string? lastImport = await _store
            .GetAsync(AppSettingKeys.CatalogLastImportAt, cancellationToken)
            .ConfigureAwait(true);

        CatalogLastImport = DateTimeOffset.TryParse(
            lastImport,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out DateTimeOffset moment)
            ? $"Последний импорт: {moment.LocalDateTime:dd.MM.yyyy HH:mm}"
            : "Каталог ещё не импортировался";

        // Сам хэш в интерфейс не выводится — только признак его наличия.
        HasStoredPassword = !string.IsNullOrWhiteSpace(api.PasswordMd5);
        Password = null;
    }

    /// <summary>
    /// Сохраняет настройки в локальную базу.
    /// </summary>
    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        // Настройки каталога сохраняются до проверки реквизитов API:
        // импорт каталога от подключения к API не зависит.
        await SaveCatalogSettingsAsync(cancellationToken).ConfigureAwait(true);

        if (!Validate())
        {
            return;
        }

        IsBusy = true;

        try
        {
            await _store.SetAsync(
                AppSettingKeys.ApiBaseUrl,
                BaseUrl?.Trim(),
                protect: false,
                cancellationToken).ConfigureAwait(true);

            await _store.SetAsync(
                AppSettingKeys.ApiLogin,
                Login?.Trim(),
                protect: false,
                cancellationToken).ConfigureAwait(true);

            if (!string.IsNullOrWhiteSpace(Password))
            {
                string hash = _hasher.LooksLikeHash(Password.Trim())
                    ? Password.Trim().ToLowerInvariant()
                    : _hasher.ToApiHash(Password);

                await _store.SetAsync(
                    AppSettingKeys.ApiPasswordMd5,
                    hash,
                    protect: true,
                    cancellationToken).ConfigureAwait(true);

                Password = null;
                HasStoredPassword = true;
            }

            await _store.SetAsync(
                AppSettingKeys.ApiTimeoutSeconds,
                TimeoutSeconds.ToString(CultureInfo.InvariantCulture),
                protect: false,
                cancellationToken).ConfigureAwait(true);

            await _store.SetAsync(
                AppSettingKeys.SyncPollingIntervalSeconds,
                PollingIntervalSeconds.ToString(CultureInfo.InvariantCulture),
                protect: false,
                cancellationToken).ConfigureAwait(true);

            await _store.SetAsync(
                AppSettingKeys.SyncNotificationsEnabled,
                NotificationsEnabled ? "true" : "false",
                protect: false,
                cancellationToken).ConfigureAwait(true);

            IsSaved = true;
            StatusMessage = "Настройки сохранены";
            _logger.LogInformation("Настройки подключения к API обновлены пользователем");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Не удалось сохранить настройки");
            StatusMessage = $"Ошибка сохранения: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Проверяет подключение к API текущими значениями полей.
    /// </summary>
    /// <remarks>
    /// Проверка идёт по сохранённым настройкам, поэтому перед ней настройки
    /// записываются в базу: у API нет способа проверить реквизиты «вслепую».
    /// </remarks>
    [RelayCommand]
    private async Task TestConnectionAsync(CancellationToken cancellationToken)
    {
        if (!Validate())
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Проверка подключения…";

        try
        {
            await SaveAsync(cancellationToken).ConfigureAwait(true);

            ConnectionCheckResult result = await _api
                .CheckConnectionAsync(cancellationToken)
                .ConfigureAwait(true);

            StatusMessage = result.IsSuccess
                ? $"✔ {result.Message}"
                : $"✖ {result.Message}";

            if (result.IsSuccess)
            {
                // Справочник статусов нужен интерфейсу для фильтра и цветов.
                await _sync.RefreshStatusCatalogAsync(cancellationToken).ConfigureAwait(true);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Проверка подключения завершилась ошибкой");
            StatusMessage = $"✖ {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Выбирает файл выгрузки каталога на диске.
    /// </summary>
    [RelayCommand]
    private void BrowseCatalog()
    {
        OpenFileDialog dialog = new()
        {
            Title = "Выгрузка каталога магазина",
            Filter = "Выгрузка YML (*.xml;*.yml)|*.xml;*.yml|Все файлы|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() == true)
        {
            CatalogFeedPath = dialog.FileName;
        }
    }

    /// <summary>
    /// Импортирует каталог магазина в кэш карточек товаров.
    /// </summary>
    /// <remarks>
    /// Импорт не обращается к API и не расходует лимит его вызовов: выгрузка содержит
    /// описания, свойства, изображения и штрихкоды по всему ассортименту магазина.
    /// После него карточки заказов открываются из кэша, а к API приложение обращается
    /// только за чужими артикулами.
    /// </remarks>
    [RelayCommand]
    private async Task ImportCatalogAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(CatalogFeedPath))
        {
            StatusMessage = "Укажите файл выгрузки каталога или её адрес";
            return;
        }

        IsBusy = true;
        StatusMessage = "Импорт каталога…";

        try
        {
            await SaveCatalogSettingsAsync(cancellationToken).ConfigureAwait(true);

            // Progress создаётся в потоке интерфейса и сам возвращает отчёты в него.
            Progress<CatalogImportProgress> progress = new(report =>
                StatusMessage = report.Total is { } total
                    ? $"{report.Stage}: {report.Processed} из {total}…"
                    : $"{report.Stage}: {report.Processed}…");

            CatalogImportResult result = await _catalog
                .ImportAsync(CatalogFeedPath.Trim(), progress, cancellationToken)
                .ConfigureAwait(true);

            StatusMessage =
                $"Каталог загружен: карточек {result.Cards}, с изображениями {result.WithImages}, "
                + $"со штрихкодами {result.WithBarcodes}"
                + (result.ImagesDownloaded > 0 ? $", скачано изображений {result.ImagesDownloaded}" : string.Empty)
                + $". Заняло {result.Elapsed.TotalSeconds:N0} с, запросов к API — ни одного";

            CatalogLastImport = $"Последний импорт: {DateTime.Now:dd.MM.yyyy HH:mm}";

            _logger.LogInformation(
                "Каталог импортирован пользователем: карточек {Cards}, изображений {Images}",
                result.Cards,
                result.WithImages);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Импорт каталога завершился ошибкой");
            StatusMessage = $"✖ Импорт каталога: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SaveCatalogSettingsAsync(CancellationToken cancellationToken)
    {
        await _store.SetAsync(
            AppSettingKeys.CatalogFeedPath,
            CatalogFeedPath?.Trim(),
            protect: false,
            cancellationToken).ConfigureAwait(true);

        await _store.SetAsync(
            AppSettingKeys.CatalogPrefetchImages,
            CatalogPrefetchImages ? "true" : "false",
            protect: false,
            cancellationToken).ConfigureAwait(true);

        await _store.SetAsync(
            AppSettingKeys.CatalogStorefrontUrl,
            StorefrontUrl?.Trim().TrimEnd('/'),
            protect: false,
            cancellationToken).ConfigureAwait(true);

        await _store.SetAsync(
            AppSettingKeys.UpdatesRepository,
            UpdatesRepository?.Trim(),
            protect: false,
            cancellationToken).ConfigureAwait(true);

        await _store.SetAsync(
            AppSettingKeys.UpdatesCheckOnStartup,
            UpdatesCheckOnStartup ? "true" : "false",
            protect: false,
            cancellationToken).ConfigureAwait(true);

        await _store.SetAsync(
            AppSettingKeys.UpdatesIncludePrerelease,
            UpdatesIncludePrerelease ? "true" : "false",
            protect: false,
            cancellationToken).ConfigureAwait(true);

        // Пустое поле означает «оставить сохранённый токен», а не «удалить».
        if (!string.IsNullOrWhiteSpace(UpdatesToken))
        {
            // Токен даёт доступ к репозиторию, поэтому шифруется DPAPI,
            // как и хэш пароля API.
            await _store.SetAsync(
                AppSettingKeys.UpdatesToken,
                UpdatesToken.Trim(),
                protect: true,
                cancellationToken).ConfigureAwait(true);

            UpdatesToken = null;
            HasStoredUpdatesToken = true;
        }
    }

    private bool Validate()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl)
            || !Uri.TryCreate(BaseUrl.Trim(), UriKind.Absolute, out Uri? uri)
            || uri.Scheme is not ("http" or "https"))
        {
            StatusMessage = "Укажите адрес API вида https://…  (адрес выдаёт менеджер ABCP)";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Login))
        {
            StatusMessage = "Укажите логин API-администратора";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Password) && !HasStoredPassword)
        {
            StatusMessage = "Укажите пароль API-администратора";
            return false;
        }

        if (TimeoutSeconds is < 5 or > 300)
        {
            StatusMessage = "Таймаут должен быть в пределах 5–300 секунд";
            return false;
        }

        if (PollingIntervalSeconds is < 15 or > 3600)
        {
            StatusMessage = "Интервал опроса должен быть в пределах 15–3600 секунд";
            return false;
        }

        return true;
    }
}
