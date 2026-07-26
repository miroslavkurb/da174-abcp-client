using System.Threading;
using System.Threading.Tasks;
using ABCPClient.Application.DTO;
using ABCPClient.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Toolkit.Uwp.Notifications;

namespace ABCPClient.UI.Services;

/// <summary>
/// Уведомления Windows (Toast) о новых заказах и сменах статусов.
/// </summary>
/// <remarks>
/// Показ уведомлений не должен ломать синхронизацию: любые сбои подсистемы
/// уведомлений (нет прав, отключены в системе, приложение без идентичности)
/// логируются и не выбрасываются наружу.
/// При большом числе изменений отправляется сводка, а не десятки уведомлений подряд.
/// </remarks>
public sealed class ToastNotificationService : INotificationService
{
    /// <summary>Сколько уведомлений показывать поштучно, прежде чем перейти к сводке.</summary>
    private const int DetailedLimit = 3;

    private readonly ILogger<ToastNotificationService> _logger;

    /// <summary>Создаёт службу уведомлений.</summary>
    public ToastNotificationService(ILogger<ToastNotificationService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public Task NotifyAsync(OrderChangeSet changes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);

        try
        {
            NotifyNewOrders(changes.CreatedOrders);
            NotifyStatusChanges(changes.StatusChanges);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Не удалось показать уведомление Windows");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task NotifyMessageAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        try
        {
            Show(title, message);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Не удалось показать уведомление Windows");
        }

        return Task.CompletedTask;
    }

    private void NotifyNewOrders(IReadOnlyList<string> createdOrders)
    {
        if (createdOrders.Count == 0)
        {
            return;
        }

        if (createdOrders.Count > DetailedLimit)
        {
            Show("Новые заказы", $"Получено новых заказов: {createdOrders.Count}");
            return;
        }

        foreach (string number in createdOrders)
        {
            Show("Новый заказ", $"Новый заказ №{number}");
        }
    }

    private void NotifyStatusChanges(IReadOnlyList<OrderStatusChange> statusChanges)
    {
        if (statusChanges.Count == 0)
        {
            return;
        }

        if (statusChanges.Count > DetailedLimit)
        {
            int orders = statusChanges.Select(change => change.OrderNumber).Distinct(StringComparer.Ordinal).Count();
            Show("Изменение статусов", $"Позиций сменило статус: {statusChanges.Count} в {orders} заказах");
            return;
        }

        foreach (OrderStatusChange change in statusChanges)
        {
            Show(
                $"Заказ №{change.OrderNumber}",
                $"{change.Brand} {change.Number}: новый статус «{change.CurrentStatus ?? "—"}»");
        }
    }

    private void Show(string title, string message)
    {
        new ToastContentBuilder()
            .AddText(title)
            .AddText(message)
            .Show();

        _logger.LogDebug("Показано уведомление: {Title} — {Message}", title, message);
    }
}
