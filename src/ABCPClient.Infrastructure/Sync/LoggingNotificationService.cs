using ABCPClient.Application.DTO;
using ABCPClient.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace ABCPClient.Infrastructure.Sync;

/// <summary>
/// Уведомления, которые только пишутся в журнал.
/// </summary>
/// <remarks>
/// Используется, когда слой представления не подключён: приложение без UI
/// (тесты, будущая служба обмена) не должно требовать Windows Toast.
/// </remarks>
public sealed class LoggingNotificationService : INotificationService
{
    private readonly ILogger<LoggingNotificationService> _logger;

    /// <summary>Создаёт службу.</summary>
    public LoggingNotificationService(ILogger<LoggingNotificationService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public Task NotifyAsync(OrderChangeSet changes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);

        foreach (string number in changes.CreatedOrders)
        {
            _logger.LogInformation("Новый заказ №{Number}", number);
        }

        foreach (OrderStatusChange change in changes.StatusChanges)
        {
            _logger.LogInformation(
                "Заказ №{Number}: позиция {Brand} {Article} перешла из статуса «{Previous}» в «{Current}»",
                change.OrderNumber,
                change.Brand,
                change.Number,
                change.PreviousStatus ?? "—",
                change.CurrentStatus ?? "—");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task NotifyMessageAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("{Title}: {Message}", title, message);
        return Task.CompletedTask;
    }
}
