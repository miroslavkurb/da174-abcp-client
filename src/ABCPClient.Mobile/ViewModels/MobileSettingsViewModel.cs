using System.Globalization;
using ABCPClient.Application.Configuration;
using ABCPClient.Application.DTO;
using ABCPClient.Application.Interfaces;
using ABCPClient.Mobile.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace ABCPClient.Mobile.ViewModels;

/// <summary>
/// Модель представления настроек мобильного приложения.
/// </summary>
/// <remarks>
/// Пароль не хранится: он сразу превращается в md5-хэш (параметр <c>userpsw</c>
/// API) и сохраняется зашифрованным. Можно вставить и готовый хэш.
/// </remarks>
public sealed partial class MobileSettingsViewModel : ObservableObject
{
    private readonly IAppSettingsStore _store;
    private readonly IAbcpSettingsProvider _settings;
    private readonly IPasswordHasher _hasher;
    private readonly IAbcpApiClient _api;
    private readonly IOrderSyncService _sync;
    private readonly AppStartup _startup;
    private readonly HubClient _hub;
    private readonly ILogger<MobileSettingsViewModel> _logger;

    /// <summary>Адрес API.</summary>
    [ObservableProperty]
    private string? _baseUrl;

    /// <summary>Логин API-администратора.</summary>
    [ObservableProperty]
    private string? _login;

    /// <summary>Пароль или готовый md5-хэш. Пусто — оставить сохранённый.</summary>
    [ObservableProperty]
    private string? _password;

    /// <summary>Пароль уже сохранён.</summary>
    [ObservableProperty]
    private bool _hasStoredPassword;

    /// <summary>Адрес витрины — источник карточек без обращений к API.</summary>
    [ObservableProperty]
    private string? _storefrontUrl;

    /// <summary>Сообщение о состоянии.</summary>
    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>Идёт сохранение или проверка.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;

    /// <summary>Работа не идёт — кнопки доступны.</summary>
    public bool IsNotBusy => !IsBusy;

    /// <summary>Адрес узла склада — программы на компьютере.</summary>
    [ObservableProperty]
    private string? _hubAddress;

    /// <summary>Код сопряжения, показанный в программе на компьютере.</summary>
    [ObservableProperty]
    private string? _pairingCode;

    /// <summary>Имя этого терминала — попадёт в отметку о сборке.</summary>
    [ObservableProperty]
    private string? _deviceName;

    /// <summary>Состояние подключения к узлу.</summary>
    [ObservableProperty]
    private string _hubState = "Не подключено";

    /// <summary>Терминал подключён к узлу.</summary>
    [ObservableProperty]
    private bool _isPaired;

    /// <summary>Каталог данных приложения — для понимания, где лежит база.</summary>
    public string DataDirectory => Infrastructure.AppPaths.DataDirectory;

    /// <summary>Создаёт модель представления.</summary>
    public MobileSettingsViewModel(
        IAppSettingsStore store,
        IAbcpSettingsProvider settings,
        IPasswordHasher hasher,
        IAbcpApiClient api,
        IOrderSyncService sync,
        AppStartup startup,
        HubClient hub,
        ILogger<MobileSettingsViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(hasher);
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(sync);
        ArgumentNullException.ThrowIfNull(startup);
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(logger);

        _store = store;
        _settings = settings;
        _hasher = hasher;
        _api = api;
        _sync = sync;
        _startup = startup;
        _hub = hub;
        _logger = logger;
    }

    /// <summary>
    /// Читает действующие настройки.
    /// </summary>
    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        await _startup.Ready.ConfigureAwait(true);

        if (_startup.FailureMessage is { } failure)
        {
            StatusMessage = $"Ошибка запуска: {failure}";
            return;
        }

        AbcpApiOptions api = await _settings.GetApiOptionsAsync(cancellationToken).ConfigureAwait(true);
        CatalogOptions catalog = await _settings.GetCatalogOptionsAsync(cancellationToken).ConfigureAwait(true);

        BaseUrl = api.BaseUrl;
        Login = api.Login;
        StorefrontUrl = catalog.StorefrontUrl;

        HasStoredPassword = !string.IsNullOrWhiteSpace(api.PasswordMd5);
        Password = null;

        await _hub.LoadAsync().ConfigureAwait(true);

        HubAddress = _hub.Address;
        DeviceName = _hub.DeviceName ?? DeviceInfo.Current.Name;
        IsPaired = _hub.IsPaired;

        HubState = IsPaired
            ? $"Подключено к {_hub.Address} как «{_hub.DeviceName}»"
            : "Не подключено";
    }

    /// <summary>
    /// Проверяет, отвечает ли узел по указанному адресу.
    /// </summary>
    /// <remarks>
    /// Отдельным шагом до подключения: неверный адрес нужно обнаружить раньше,
    /// чем сборщик начнёт вводить код, который живёт десять минут.
    /// </remarks>
    [RelayCommand]
    private async Task CheckHubAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            HubResult<bool> result = await _hub
                .CheckAsync(HubAddress ?? string.Empty, cancellationToken)
                .ConfigureAwait(true);

            StatusMessage = result.IsSuccess ? "✔ Узел отвечает" : $"✖ {result.Error}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Подключает терминал к узлу по коду сопряжения.
    /// </summary>
    [RelayCommand]
    private async Task PairAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            HubResult<string> result = await _hub
                .PairAsync(
                    HubAddress ?? string.Empty,
                    PairingCode ?? string.Empty,
                    DeviceName ?? string.Empty,
                    cancellationToken)
                .ConfigureAwait(true);

            if (!result.IsSuccess)
            {
                StatusMessage = $"✖ {result.Error}";
                return;
            }

            PairingCode = null;
            IsPaired = true;
            HubState = $"Подключено к {_hub.Address} как «{result.Value}»";
            StatusMessage = "✔ Терминал подключён. Задания появятся на вкладке «Сборка»";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Забывает подключение к узлу.
    /// </summary>
    [RelayCommand]
    private async Task ForgetHubAsync()
    {
        await _hub.ForgetAsync().ConfigureAwait(true);

        IsPaired = false;
        HubState = "Не подключено";
        StatusMessage = "Подключение забыто. Понадобится новый код";
    }

    /// <summary>
    /// Сохраняет настройки.
    /// </summary>
    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (!Validate())
        {
            return;
        }

        IsBusy = true;

        try
        {
            await _store.SetAsync(AppSettingKeys.ApiBaseUrl, BaseUrl?.Trim(), false, cancellationToken)
                .ConfigureAwait(true);

            await _store.SetAsync(AppSettingKeys.ApiLogin, Login?.Trim(), false, cancellationToken)
                .ConfigureAwait(true);

            await _store.SetAsync(
                    AppSettingKeys.CatalogStorefrontUrl,
                    StorefrontUrl?.Trim().TrimEnd('/'),
                    false,
                    cancellationToken)
                .ConfigureAwait(true);

            if (!string.IsNullOrWhiteSpace(Password))
            {
                string hash = _hasher.LooksLikeHash(Password.Trim())
                    ? Password.Trim().ToLowerInvariant()
                    : _hasher.ToApiHash(Password);

                await _store.SetAsync(AppSettingKeys.ApiPasswordMd5, hash, true, cancellationToken)
                    .ConfigureAwait(true);

                Password = null;
                HasStoredPassword = true;
            }

            StatusMessage = "Настройки сохранены";
            _logger.LogInformation("Настройки подключения обновлены");
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
    /// Проверяет подключение сохранёнными реквизитами.
    /// </summary>
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
            // Проверка идёт по сохранённым настройкам: «вслепую» реквизиты
            // проверить нечем, поэтому сначала запись.
            await SaveAsync(cancellationToken).ConfigureAwait(true);

            ConnectionCheckResult result = await _api
                .CheckConnectionAsync(cancellationToken)
                .ConfigureAwait(true);

            StatusMessage = result.IsSuccess ? $"✔ {result.Message}" : $"✖ {result.Message}";

            if (result.IsSuccess)
            {
                await _sync.RefreshStatusCatalogAsync(cancellationToken).ConfigureAwait(true);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Проверка подключения не удалась");
            StatusMessage = $"✖ {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool Validate()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl)
            || !Uri.TryCreate(BaseUrl.Trim(), UriKind.Absolute, out Uri? uri)
            || uri.Scheme is not ("http" or "https"))
        {
            StatusMessage = "Укажите адрес API вида https://… (адрес выдаёт менеджер ABCP)";
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

        if (!string.IsNullOrWhiteSpace(StorefrontUrl)
            && (!Uri.TryCreate(StorefrontUrl.Trim(), UriKind.Absolute, out Uri? shop)
                || shop.Scheme is not ("http" or "https")))
        {
            StatusMessage = "Адрес витрины должен быть вида https://da174.ru";
            return false;
        }

        return true;
    }

    /// <summary>Версия приложения для строки состояния.</summary>
    public string VersionText => string.Create(
        CultureInfo.InvariantCulture,
        $"Версия {AppInfo.Current.VersionString}");
}
