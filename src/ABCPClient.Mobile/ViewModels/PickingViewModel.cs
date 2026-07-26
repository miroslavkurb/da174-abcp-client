using System.Collections.ObjectModel;
using ABCPClient.Contracts;
using ABCPClient.Mobile.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace ABCPClient.Mobile.ViewModels;

/// <summary>
/// Модель представления списка заданий на сборку.
/// </summary>
/// <remarks>
/// Задания живут на узле — в программе на компьютере. Своей копии у терминала
/// нет намеренно: два источника заданий разошлись бы, и собранное перестало бы
/// соответствовать заказу. Поэтому без связи список не показывается, и причина
/// написана на экране.
/// </remarks>
public sealed partial class PickingViewModel : ObservableObject
{
    private readonly HubClient _hub;
    private readonly ILogger<PickingViewModel> _logger;

    /// <summary>Задания на сборку.</summary>
    public ObservableCollection<PickingTaskItemViewModel> Tasks { get; } = [];

    /// <summary>Сообщение о состоянии.</summary>
    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>Идёт обращение к узлу.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;

    /// <summary>Идёт обновление жестом.</summary>
    [ObservableProperty]
    private bool _isRefreshing;

    /// <summary>Устройство подключено к узлу.</summary>
    [ObservableProperty]
    private bool _isPaired;

    /// <summary>Показывать только незакрытые задания.</summary>
    [ObservableProperty]
    private bool _onlyOpen = true;

    /// <summary>Обращение не идёт — кнопки доступны.</summary>
    public bool IsNotBusy => !IsBusy;

    /// <summary>Создаёт модель представления.</summary>
    public PickingViewModel(HubClient hub, ILogger<PickingViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(logger);

        _hub = hub;
        _logger = logger;
    }

    /// <summary>
    /// Читает задания с узла.
    /// </summary>
    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        await _hub.LoadAsync().ConfigureAwait(true);

        IsPaired = _hub.IsPaired;

        if (!IsPaired)
        {
            Tasks.Clear();
            StatusMessage = "Терминал не подключён к компьютеру. Откройте «Настройки» и подключитесь по коду";
            IsRefreshing = false;
            return;
        }

        IsBusy = true;

        try
        {
            HubResult<PickingTaskSummary[]> result = await _hub
                .GetTasksAsync(OnlyOpen, cancellationToken)
                .ConfigureAwait(true);

            if (!result.IsSuccess)
            {
                StatusMessage = result.Error;

                if (result.RequiresPairing)
                {
                    IsPaired = false;
                    StatusMessage += ". Подключитесь заново в «Настройках»";
                }

                return;
            }

            Tasks.Clear();
            foreach (PickingTaskSummary task in result.Value!)
            {
                Tasks.Add(new PickingTaskItemViewModel(task));
            }

            StatusMessage = Tasks.Count == 0
                ? OnlyOpen
                    ? "Незакрытых заданий нет"
                    : "Заданий нет. Создайте их в программе на компьютере кнопкой «На сборку»"
                : $"Заданий: {Tasks.Count}  ·  {_hub.DeviceName}";
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Не удалось прочитать задания с узла");
            StatusMessage = $"Ошибка: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
            IsRefreshing = false;
        }
    }

    /// <summary>
    /// Открывает задание.
    /// </summary>
    [RelayCommand]
    private static async Task OpenTaskAsync(PickingTaskItemViewModel? task)
    {
        if (task is null)
        {
            return;
        }

        await Shell.Current
            .GoToAsync($"{nameof(Views.PickingTaskPage)}?id={task.Id}")
            .ConfigureAwait(true);
    }
}

/// <summary>
/// Задание на сборку в списке.
/// </summary>
public sealed class PickingTaskItemViewModel
{
    /// <summary>Создаёт представление задания.</summary>
    /// <param name="task">Сведения о задании с узла.</param>
    public PickingTaskItemViewModel(PickingTaskSummary task)
    {
        ArgumentNullException.ThrowIfNull(task);

        Id = task.Id;
        Number = task.Number;

        Order = task.OrderNumber is { Length: > 0 } order
            ? task.OneCOrderNumber is { Length: > 0 } internalNumber ? $"{order} / {internalNumber}" : order
            : task.OneCOrderNumber ?? "—";

        Customer = task.Customer ?? "Клиент не указан";
        CreatedAt = task.CreatedAt.LocalDateTime.ToString("dd.MM HH:mm", null);

        StatusText = task.Status switch
        {
            PickingStatusCodes.InProgress => "собирается",
            PickingStatusCodes.Picked => "собрано",
            PickingStatusCodes.Cancelled => "отменено",
            _ => "новое",
        };

        // Сборщику важно, сколько строк он может взять прямо сейчас,
        // а не сколько их всего.
        Progress = $"{task.CompleteLines} из {task.InStockLines} в наличии";
        IncomingText = task.IncomingLines > 0 ? $"в пути: {task.IncomingLines}" : string.Empty;

        HasWork = task.InStockLines > task.CompleteLines;

        // Цвета выбраны читаемыми и на светлом, и на тёмном фоне: полоса узкая,
        // и подбирать её оттенок под тему было бы возней без выгоды.
        AccentColor = HasWork
            ? Color.FromArgb("#2E9E52")
            : task.Status == PickingStatusCodes.Picked
                ? Color.FromArgb("#8A97A2")
                : Color.FromArgb("#D19A28");
    }

    /// <summary>Цвет полосы состояния слева.</summary>
    public Color AccentColor { get; }

    /// <summary>Идентификатор задания.</summary>
    public int Id { get; }

    /// <summary>Номер задания.</summary>
    public string Number { get; }

    /// <summary>Номера заказа.</summary>
    public string Order { get; }

    /// <summary>Клиент.</summary>
    public string Customer { get; }

    /// <summary>Когда создано.</summary>
    public string CreatedAt { get; }

    /// <summary>Состояние словами.</summary>
    public string StatusText { get; }

    /// <summary>Сколько строк собрано из доступных.</summary>
    public string Progress { get; }

    /// <summary>Сколько строк ждут поступления.</summary>
    public string IncomingText { get; }

    /// <summary>Есть что собирать прямо сейчас.</summary>
    public bool HasWork { get; }
}
