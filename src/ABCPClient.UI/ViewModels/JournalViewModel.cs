using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ABCPClient.Application.DTO;
using ABCPClient.Application.Interfaces;
using ABCPClient.Domain.Entities;
using ABCPClient.Domain.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace ABCPClient.UI.ViewModels;

/// <summary>
/// Модель представления журнала синхронизации.
/// </summary>
public sealed partial class JournalViewModel : ObservableObject, IDisposable
{
    private readonly ISyncLogRepository _syncLog;
    private readonly ISyncEventBus _eventBus;
    private readonly ILogger<JournalViewModel> _logger;

    /// <summary>Записи журнала, свежие сверху.</summary>
    public ObservableCollection<JournalRow> Entries { get; } = [];

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Создаёт модель представления.</summary>
    public JournalViewModel(
        ISyncLogRepository syncLog,
        ISyncEventBus eventBus,
        ILogger<JournalViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(syncLog);
        ArgumentNullException.ThrowIfNull(eventBus);
        ArgumentNullException.ThrowIfNull(logger);

        _syncLog = syncLog;
        _eventBus = eventBus;
        _logger = logger;

        _eventBus.SyncCompleted += OnSyncCompleted;
    }

    /// <summary>
    /// Перечитывает журнал из локальной базы.
    /// </summary>
    [RelayCommand]
    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            IReadOnlyList<SyncLogEntry> entries = await _syncLog
                .GetRecentAsync(200, cancellationToken)
                .ConfigureAwait(true);

            Entries.Clear();
            foreach (SyncLogEntry entry in entries)
            {
                Entries.Add(JournalRow.FromEntry(entry));
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Не удалось прочитать журнал синхронизации");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnSyncCompleted(object? sender, SyncResult result)
    {
        if (System.Windows.Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.InvokeAsync(() => _ = ReloadAsync(CancellationToken.None));
            return;
        }

        _ = ReloadAsync(CancellationToken.None);
    }

    /// <inheritdoc />
    public void Dispose() => _eventBus.SyncCompleted -= OnSyncCompleted;
}

/// <summary>
/// Строка журнала синхронизации.
/// </summary>
/// <param name="StartedAt">Начало операции.</param>
/// <param name="Operation">Операция.</param>
/// <param name="Outcome">Результат.</param>
/// <param name="Fetched">Получено заказов.</param>
/// <param name="Created">Создано заказов.</param>
/// <param name="Updated">Обновлено заказов.</param>
/// <param name="StatusChanges">Смен статусов.</param>
/// <param name="DurationText">Длительность.</param>
/// <param name="Message">Пояснение или текст ошибки.</param>
public sealed record JournalRow(
    DateTime StartedAt,
    string Operation,
    string Outcome,
    int Fetched,
    int Created,
    int Updated,
    int StatusChanges,
    string DurationText,
    string? Message)
{
    /// <summary>Создаёт строку журнала по записи базы.</summary>
    /// <param name="entry">Запись журнала.</param>
    public static JournalRow FromEntry(SyncLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new JournalRow(
            entry.StartedAt,
            entry.Operation switch
            {
                SyncOperation.Orders => "Заказы",
                SyncOperation.Statuses => "Справочник статусов",
                SyncOperation.StatusHistory => "История статусов",
                SyncOperation.ConnectionCheck => "Проверка подключения",
                _ => entry.Operation.ToString(),
            },
            entry.Outcome switch
            {
                SyncOutcome.Success => "Успешно",
                SyncOutcome.Failed => "Ошибка",
                SyncOutcome.Skipped => "Пропущено",
                _ => entry.Outcome.ToString(),
            },
            entry.OrdersFetched,
            entry.OrdersCreated,
            entry.OrdersUpdated,
            entry.StatusChanges,
            entry.Duration is { } duration ? $"{duration.TotalSeconds:0.0} с" : "—",
            entry.ErrorCode is { } code ? $"[{code}] {entry.Message}" : entry.Message);
    }
}
