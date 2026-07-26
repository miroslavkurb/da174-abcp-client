using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ABCPClient.Application.Configuration;
using ABCPClient.Application.DTO;
using ABCPClient.Application.Interfaces;
using ABCPClient.Domain.Entities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace ABCPClient.UI.ViewModels;

/// <summary>
/// Модель представления главного окна: таблица заказов, фильтры и строка статуса.
/// </summary>
/// <remarks>
/// Данные читаются из локальной базы, а не из API: обращения к API — дело фоновой службы.
/// О завершении синхронизации модель узнаёт из <see cref="ISyncEventBus"/>,
/// поэтому она не знает ни о фоновой службе, ни о клиенте API.
/// </remarks>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IOrderRepository _orders;
    private readonly IStatusCatalogRepository _statusCatalog;
    private readonly IOrderSyncService _sync;
    private readonly IAbcpSettingsProvider _settings;
    private readonly ISyncEventBus _eventBus;
    private readonly ILogger<MainViewModel> _logger;

    /// <summary>Строки таблицы заказов.</summary>
    public ObservableCollection<OrderListItem> Orders { get; } = [];

    /// <summary>Статусы для фильтра. Первый элемент — «Все статусы».</summary>
    public ObservableCollection<StatusFilterItem> Statuses { get; } = [];

    /// <summary>Журнал синхронизации, показывается на отдельной вкладке.</summary>
    public JournalViewModel Journal { get; }

    /// <summary>Сборка заказов и узел для терминалов, отдельная вкладка.</summary>
    public PickingViewModel Picking { get; }

    [ObservableProperty]
    private string _connectionStatus = "Подключение не настроено";

    [ObservableProperty]
    private string _lastSyncText = "Синхронизация не выполнялась";

    [ObservableProperty]
    private int _ordersCount;

    [ObservableProperty]
    private bool _isApiConfigured;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _searchText;

    [ObservableProperty]
    private StatusFilterItem? _selectedStatus;

    [ObservableProperty]
    private DateTime? _dateFrom;

    [ObservableProperty]
    private DateTime? _dateTo;

    /// <summary>
    /// Показывать заказы, удалённые в панели управления.
    /// </summary>
    /// <remarks>
    /// По умолчанию выключено: удалённые заказы не выбрасываются из локальной базы,
    /// а помечаются, чтобы история и журнал оставались целыми.
    /// </remarks>
    [ObservableProperty]
    private bool _showDeleted;

    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>
    /// Создаёт модель представления.
    /// </summary>
    public MainViewModel(
        IOrderRepository orders,
        IStatusCatalogRepository statusCatalog,
        IOrderSyncService sync,
        IAbcpSettingsProvider settings,
        ISyncEventBus eventBus,
        JournalViewModel journal,
        PickingViewModel picking,
        ILogger<MainViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(orders);
        ArgumentNullException.ThrowIfNull(statusCatalog);
        ArgumentNullException.ThrowIfNull(sync);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(eventBus);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(picking);
        ArgumentNullException.ThrowIfNull(logger);

        Journal = journal;
        Picking = picking;

        _orders = orders;
        _statusCatalog = statusCatalog;
        _sync = sync;
        _settings = settings;
        _eventBus = eventBus;
        _logger = logger;

        _eventBus.SyncCompleted += OnSyncCompleted;
    }

    /// <summary>
    /// Загружает состояние окна: настройки подключения, справочник статусов и заказы.
    /// </summary>
    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        await RefreshConnectionStateAsync(cancellationToken).ConfigureAwait(true);
        await LoadStatusesAsync(cancellationToken).ConfigureAwait(true);
        await ReloadOrdersAsync(cancellationToken).ConfigureAwait(true);
        await Journal.ReloadCommand.ExecuteAsync(null).ConfigureAwait(true);
    }

    /// <summary>
    /// Перечитывает заказы из локальной базы по текущим фильтрам.
    /// </summary>
    [RelayCommand]
    private async Task ReloadOrdersAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            OrderFilter filter = new()
            {
                SearchText = SearchText,
                StatusCode = SelectedStatus?.StatusCode,
                DateFrom = DateFrom,
                DateTo = DateTo,
                IncludeDeleted = ShowDeleted,
                Take = 1000,
            };

            IReadOnlyList<OrderListItem> items = await _orders
                .GetListAsync(filter, cancellationToken)
                .ConfigureAwait(true);

            int total = await _orders.CountAsync(filter, cancellationToken).ConfigureAwait(true);

            Orders.Clear();
            foreach (OrderListItem item in items)
            {
                Orders.Add(item);
            }

            OrdersCount = total;
            StatusMessage = items.Count < total
                ? $"Показаны первые {items.Count} из {total} заказов"
                : null;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Не удалось прочитать заказы из локальной базы");
            StatusMessage = $"Ошибка чтения базы: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Запускает синхронизацию с API по требованию пользователя.
    /// </summary>
    [RelayCommand]
    private async Task SyncNowAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        StatusMessage = "Синхронизация…";

        try
        {
            SyncResult result = await _sync.SyncAsync(cancellationToken).ConfigureAwait(true);

            // Ручной запуск дополнительно сверяет удалённые заказы: пользователь жмёт
            // «Синхронизировать» именно тогда, когда данные расходятся с панелью управления.
            int markedDeleted = await _sync
                .ReconcileDeletedOrdersAsync(cancellationToken)
                .ConfigureAwait(true);

            if (markedDeleted > 0)
            {
                StatusMessage = $"Удалённых в портале заказов помечено: {markedDeleted}";
                await ReloadOrdersAsync(cancellationToken).ConfigureAwait(true);
            }

            // Публикуем результат сами: ручной запуск идёт мимо фоновой службы,
            // но остальные подписчики (журнал, уведомления) должны узнать о нём так же.
            _eventBus.Publish(result);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Ручная синхронизация завершилась ошибкой");
            StatusMessage = $"Ошибка синхронизации: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Сбрасывает фильтры и перечитывает список.
    /// </summary>
    [RelayCommand]
    private async Task ClearFiltersAsync(CancellationToken cancellationToken)
    {
        SearchText = null;
        DateFrom = null;
        DateTo = null;
        ShowDeleted = false;
        SelectedStatus = Statuses.Count > 0 ? Statuses[0] : null;

        await ReloadOrdersAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Переключение галки «удалённые» сразу перечитывает список:
    /// отдельная кнопка «Применить» здесь была бы лишним шагом.
    /// </summary>
    partial void OnShowDeletedChanged(bool value) => _ = ReloadOrdersAsync(CancellationToken.None);

    /// <summary>
    /// Обновляет строку состояния подключения.
    /// </summary>
    public async Task RefreshConnectionStateAsync(CancellationToken cancellationToken = default)
    {
        AbcpApiOptions options = await _settings.GetApiOptionsAsync(cancellationToken).ConfigureAwait(true);

        IsApiConfigured = options.IsConfigured;
        ConnectionStatus = options.IsConfigured
            ? $"API: {options.BaseUrl} (логин {options.Login})"
            : "Подключение не настроено — откройте настройки";
    }

    private async Task LoadStatusesAsync(CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<OrderStatus> statuses = await _statusCatalog
                .GetAllAsync(cancellationToken)
                .ConfigureAwait(true);

            int? selectedCode = SelectedStatus?.StatusCode;

            Statuses.Clear();
            Statuses.Add(new StatusFilterItem(null, "Все статусы"));

            foreach (OrderStatus status in statuses)
            {
                Statuses.Add(new StatusFilterItem(status.StatusCode, status.Name));
            }

            SelectedStatus = Statuses.FirstOrDefault(item => item.StatusCode == selectedCode) ?? Statuses[0];
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Не удалось прочитать справочник статусов");
        }
    }

    /// <summary>
    /// Реакция на завершение синхронизации. Событие приходит из фонового потока,
    /// поэтому обновление привязанных коллекций переносится в поток интерфейса.
    /// </summary>
    private void OnSyncCompleted(object? sender, SyncResult result)
    {
        void Apply()
        {
            LastSyncText = result.Outcome switch
            {
                Domain.Models.SyncOutcome.Success =>
                    $"Синхронизация {DateTime.Now:HH:mm:ss}: получено {result.OrdersFetched}, "
                    + $"новых {result.Changes.CreatedOrders.Count}, обновлено {result.Changes.UpdatedOrders.Count}",
                Domain.Models.SyncOutcome.Skipped => $"Синхронизация пропущена: {result.Message}",
                _ => $"Ошибка синхронизации {DateTime.Now:HH:mm:ss}: {result.Message}",
            };

            if (result.IsSuccess && result.Changes.HasChanges)
            {
                _ = ReloadOrdersAsync(CancellationToken.None);
            }
        }

        // Полное имя типа обязательно: ABCPClient.Application — это пространство имён слоя.
        if (System.Windows.Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.InvokeAsync(Apply);
            return;
        }

        Apply();
    }

    /// <inheritdoc />
    public void Dispose() => _eventBus.SyncCompleted -= OnSyncCompleted;
}

/// <summary>
/// Элемент фильтра по статусу.
/// </summary>
/// <param name="StatusCode">Код статуса; <c>null</c> — все статусы.</param>
/// <param name="Name">Название для списка.</param>
public sealed record StatusFilterItem(int? StatusCode, string Name);
