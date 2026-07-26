using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ABCPClient.Application.DTO;
using ABCPClient.Application.Interfaces;
using ABCPClient.Hub;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace ABCPClient.UI.ViewModels;

/// <summary>
/// Модель представления вкладки «Сборка».
/// </summary>
/// <remarks>
/// Настольная программа здесь управляющая: она создаёт задания и держит узел,
/// к которому подключаются терминалы. Терминал своей базы заданий не имеет,
/// поэтому без запущенной программы он ничего не увидит — это видно на экране,
/// а не выясняется на складе.
/// </remarks>
public sealed partial class PickingViewModel : ObservableObject
{
    private readonly IPickingService _picking;
    private readonly WarehouseHub _hub;
    private readonly DeviceRegistry _devices;
    private readonly ILogger<PickingViewModel> _logger;

    /// <summary>Задания на сборку.</summary>
    public ObservableCollection<PickingTaskListItem> Tasks { get; } = [];

    /// <summary>Подключённые терминалы.</summary>
    public ObservableCollection<string> Devices { get; } = [];

    /// <summary>Показывать только незакрытые задания.</summary>
    [ObservableProperty]
    private bool _onlyOpen = true;

    /// <summary>Поиск по номеру задания, заказу или клиенту.</summary>
    [ObservableProperty]
    private string? _searchText;

    /// <summary>Выбранное задание.</summary>
    [ObservableProperty]
    private PickingTaskListItem? _selectedTask;

    /// <summary>Сообщение о состоянии.</summary>
    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>Идёт работа.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;

    /// <summary>Состояние узла для строки на экране.</summary>
    [ObservableProperty]
    private string _hubState = "Узел не запущен";

    /// <summary>Адреса, которые набирают на терминале.</summary>
    [ObservableProperty]
    private string _hubAddresses = "—";

    /// <summary>Действующий код сопряжения.</summary>
    [ObservableProperty]
    private string? _pairingCode;

    /// <summary>Пояснение к коду сопряжения.</summary>
    [ObservableProperty]
    private string? _pairingHint;

    /// <summary>Работа не идёт — кнопки доступны.</summary>
    public bool IsNotBusy => !IsBusy;

    /// <summary>Создаёт модель представления.</summary>
    public PickingViewModel(
        IPickingService picking,
        WarehouseHub hub,
        DeviceRegistry devices,
        ILogger<PickingViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(picking);
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(devices);
        ArgumentNullException.ThrowIfNull(logger);

        _picking = picking;
        _hub = hub;
        _devices = devices;
        _logger = logger;
    }

    /// <summary>
    /// Обновляет список заданий, состояние узла и список устройств.
    /// </summary>
    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            RefreshHubState();

            IReadOnlyList<PickingTaskListItem> tasks = await _picking
                .GetTasksAsync(
                    new PickingTaskFilter { OnlyOpen = OnlyOpen, SearchText = SearchText },
                    cancellationToken)
                .ConfigureAwait(true);

            Tasks.Clear();
            foreach (PickingTaskListItem task in tasks)
            {
                Tasks.Add(task);
            }

            Devices.Clear();
            foreach ((string name, DateTimeOffset pairedAt) in await _devices
                .GetDevicesAsync(cancellationToken)
                .ConfigureAwait(true))
            {
                Devices.Add($"{name} — подключён {pairedAt.LocalDateTime:dd.MM.yyyy HH:mm}");
            }

            StatusMessage = Tasks.Count == 0
                ? "Заданий нет. Выберите заказы на вкладке «Заказы» и нажмите «На сборку»"
                : $"Заданий: {Tasks.Count}";
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Не удалось прочитать задания на сборку");
            StatusMessage = $"Ошибка чтения: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Создаёт задания на сборку по указанным заказам.
    /// </summary>
    /// <param name="orderNumbers">Онлайн-номера заказов.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async Task CreateTasksAsync(
        IReadOnlyCollection<string> orderNumbers,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderNumbers);

        if (orderNumbers.Count == 0)
        {
            StatusMessage = "Не выбран ни один заказ";
            return;
        }

        IsBusy = true;

        try
        {
            PickingTaskCreationResult result = await _picking
                .CreateTasksAsync(orderNumbers, cancellationToken)
                .ConfigureAwait(true);

            StatusMessage = Describe(result);

            await LoadAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Не удалось создать задания на сборку");
            StatusMessage = $"Ошибка создания: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Составляет понятное сообщение об итоге создания заданий.
    /// </summary>
    /// <remarks>
    /// Пропущенные заказы называются по причинам: «ничего не создано» без
    /// объяснения выглядит как поломка, хотя обычно задание просто уже есть.
    /// </remarks>
    internal static string Describe(PickingTaskCreationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        List<string> parts = [];

        if (result.Created.Count > 0)
        {
            parts.Add($"создано заданий: {result.Created.Count} "
                + $"({string.Join(", ", result.Created.Select(task => task.Number))})");
        }

        if (result.SkippedExisting.Count > 0)
        {
            parts.Add($"уже собираются: {string.Join(", ", result.SkippedExisting)}");
        }

        if (result.SkippedEmpty.Count > 0)
        {
            parts.Add($"нечего собирать: {string.Join(", ", result.SkippedEmpty)}");
        }

        if (result.NotFound.Count > 0)
        {
            parts.Add($"нет в локальной базе: {string.Join(", ", result.NotFound)}");
        }

        return parts.Count == 0 ? "Ничего не создано" : string.Join("; ", parts);
    }

    /// <summary>
    /// Выдаёт код сопряжения для подключения терминала.
    /// </summary>
    [RelayCommand]
    private void IssuePairingCode()
    {
        if (!_hub.IsRunning)
        {
            StatusMessage = "Узел не запущен: подключить терминал нечем. "
                + "Проверьте, не занят ли порт и не блокирует ли его брандмауэр";
            return;
        }

        PairingCode = _devices.IssuePairingCode();
        PairingHint = $"Введите код на терминале до {_devices.PairingCodeExpiresAt.LocalDateTime:HH:mm}. "
            + "Код одноразовый.";

        _logger.LogInformation("Код сопряжения выдан пользователем");
    }

    /// <summary>
    /// Отключает выбранный терминал.
    /// </summary>
    [RelayCommand]
    private async Task RevokeDeviceAsync(string? device)
    {
        if (string.IsNullOrWhiteSpace(device))
        {
            return;
        }

        // В списке рядом с именем показано время подключения — берём только имя.
        string name = device.Split('—')[0].Trim();

        if (await _devices.RevokeDeviceAsync(name).ConfigureAwait(true))
        {
            StatusMessage = $"Терминал «{name}» отключён. Ему потребуется новый код";
            await LoadAsync(CancellationToken.None).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Отменяет выбранное задание.
    /// </summary>
    [RelayCommand]
    private async Task CancelTaskAsync(CancellationToken cancellationToken)
    {
        if (SelectedTask is not { } task)
        {
            StatusMessage = "Не выбрано задание";
            return;
        }

        try
        {
            await _picking.CancelTaskAsync(task.Id, "отменено в программе", cancellationToken)
                .ConfigureAwait(true);

            StatusMessage = $"Задание {task.Number} отменено";

            await LoadAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (InvalidOperationException exception)
        {
            StatusMessage = exception.Message;
        }
    }

    private void RefreshHubState()
    {
        if (_hub.IsRunning)
        {
            IReadOnlyList<string> addresses = _hub.GetAddresses();

            HubState = $"Узел работает, порт {_hub.Port}";
            HubAddresses = addresses.Count > 0
                ? string.Join("   ", addresses)
                : $"http://localhost:{_hub.Port} (сетевых адресов не найдено)";
        }
        else
        {
            HubState = "Узел не запущен — терминалы подключиться не смогут";
            HubAddresses = "—";
        }

        PairingCode = _devices.CurrentPairingCode;
        PairingHint = PairingCode is null ? null : PairingHint;
    }
}
