using System.Collections.ObjectModel;
using ABCPClient.Application.Configuration;
using ABCPClient.Application.DTO;
using ABCPClient.Application.Interfaces;
using ABCPClient.Mobile.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace ABCPClient.Mobile.ViewModels;

/// <summary>
/// Модель представления списка заказов.
/// </summary>
/// <remarks>
/// Список читается из локальной базы, поэтому работает и без сети — на складе
/// это обычное дело. Кнопка обновления обращается к API вручную: фоновой
/// синхронизации на телефоне нет.
/// </remarks>
public sealed partial class OrdersViewModel : ObservableObject
{
    private readonly IOrderRepository _orders;
    private readonly IOrderSyncService _sync;
    private readonly IAbcpSettingsProvider _settings;
    private readonly AppStartup _startup;
    private readonly ILogger<OrdersViewModel> _logger;

    /// <summary>Заказы.</summary>
    public ObservableCollection<OrderListItem> Orders { get; } = [];

    /// <summary>Строка поиска.</summary>
    [ObservableProperty]
    private string? _searchText;

    /// <summary>Сообщение о состоянии.</summary>
    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>Идёт загрузка или синхронизация.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;

    /// <summary>
    /// Работа не идёт — кнопки доступны.
    /// </summary>
    /// <remarks>
    /// Готовое свойство вместо преобразователя в разметке: тот жил в ресурсах
    /// приложения, а страницы создаются раньше этих ресурсов, и разбор разметки
    /// падал на поиске <c>StaticResource</c>.
    /// </remarks>
    public bool IsNotBusy => !IsBusy;

    /// <summary>Идёт обновление жестом «потянуть вниз».</summary>
    [ObservableProperty]
    private bool _isRefreshing;

    /// <summary>Реквизиты API заданы.</summary>
    [ObservableProperty]
    private bool _isConfigured;

    /// <summary>Список пуст.</summary>
    [ObservableProperty]
    private bool _isEmpty = true;

    /// <summary>Создаёт модель представления.</summary>
    public OrdersViewModel(
        IOrderRepository orders,
        IOrderSyncService sync,
        IAbcpSettingsProvider settings,
        AppStartup startup,
        ILogger<OrdersViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(orders);
        ArgumentNullException.ThrowIfNull(sync);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(startup);
        ArgumentNullException.ThrowIfNull(logger);

        _orders = orders;
        _sync = sync;
        _settings = settings;
        _startup = startup;
        _logger = logger;
    }

    /// <summary>
    /// Читает заказы из локальной базы.
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

        IsBusy = true;

        try
        {
            AbcpApiOptions api = await _settings.GetApiOptionsAsync(cancellationToken).ConfigureAwait(true);
            IsConfigured = api.IsConfigured;

            OrderFilter filter = new()
            {
                SearchText = SearchText,

                // На телефоне длинный список не нужен: смотрят свежие заказы,
                // остальное ищут поиском.
                Take = 200,
            };

            IReadOnlyList<OrderListItem> items = await _orders
                .GetListAsync(filter, cancellationToken)
                .ConfigureAwait(true);

            Orders.Clear();
            foreach (OrderListItem item in items)
            {
                Orders.Add(item);
            }

            IsEmpty = Orders.Count == 0;

            StatusMessage = Orders.Count == 0
                ? IsConfigured
                    ? "Заказов нет. Нажмите «Обновить», чтобы загрузить их из ABCP"
                    : "Укажите данные доступа к API на вкладке «Настройки»"
                : $"Заказов: {Orders.Count}";
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Не удалось прочитать заказы из базы");
            StatusMessage = $"Ошибка чтения: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Забирает новые и изменённые заказы из API и обновляет список.
    /// </summary>
    [RelayCommand]
    private async Task SyncAsync(CancellationToken cancellationToken)
    {
        await _startup.Ready.ConfigureAwait(true);

        AbcpApiOptions api = await _settings.GetApiOptionsAsync(cancellationToken).ConfigureAwait(true);
        if (!api.IsConfigured)
        {
            StatusMessage = "Сначала укажите данные доступа к API на вкладке «Настройки»";
            IsRefreshing = false;
            return;
        }

        IsBusy = true;
        StatusMessage = "Синхронизация…";

        try
        {
            SyncResult result = await _sync.SyncAsync(cancellationToken).ConfigureAwait(true);

            StatusMessage = result.IsSuccess
                ? $"Получено {result.OrdersFetched}, новых {result.Changes.CreatedOrders.Count}, "
                    + $"изменённых {result.Changes.UpdatedOrders.Count}"
                : $"Синхронизация не удалась: {result.Message}";

            await LoadAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Синхронизация с мобильного клиента не удалась");
            StatusMessage = $"Ошибка синхронизации: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
            IsRefreshing = false;
        }
    }

    /// <summary>
    /// Применяет поиск.
    /// </summary>
    [RelayCommand]
    private async Task SearchAsync(CancellationToken cancellationToken) =>
        await LoadAsync(cancellationToken).ConfigureAwait(true);

    /// <summary>
    /// Открывает карточку заказа.
    /// </summary>
    [RelayCommand]
    private static async Task OpenOrderAsync(OrderListItem? order)
    {
        if (order is null)
        {
            return;
        }

        await Shell.Current
            .GoToAsync($"{nameof(Views.OrderDetailsPage)}?number={Uri.EscapeDataString(order.Number)}")
            .ConfigureAwait(true);
    }
}
