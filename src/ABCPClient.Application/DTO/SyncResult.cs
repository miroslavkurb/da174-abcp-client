using ABCPClient.Domain.Models;

namespace ABCPClient.Application.DTO;

/// <summary>
/// Смена статуса позиции, обнаруженная при синхронизации.
/// </summary>
/// <param name="OrderNumber">Номер заказа.</param>
/// <param name="PositionId">Идентификатор позиции.</param>
/// <param name="Brand">Бренд.</param>
/// <param name="Number">Артикул.</param>
/// <param name="PreviousStatus">Предыдущий статус.</param>
/// <param name="PreviousStatusCode">Код предыдущего статуса.</param>
/// <param name="CurrentStatus">Новый статус.</param>
/// <param name="CurrentStatusCode">Код нового статуса.</param>
public sealed record OrderStatusChange(
    string OrderNumber,
    long PositionId,
    string Brand,
    string Number,
    string? PreviousStatus,
    int? PreviousStatusCode,
    string? CurrentStatus,
    int? CurrentStatusCode);

/// <summary>
/// Результат применения полученных заказов к локальной базе.
/// </summary>
/// <param name="CreatedOrders">Номера появившихся заказов.</param>
/// <param name="UpdatedOrders">Номера изменившихся заказов.</param>
/// <param name="StatusChanges">Смены статусов позиций.</param>
public sealed record OrderChangeSet(
    IReadOnlyList<string> CreatedOrders,
    IReadOnlyList<string> UpdatedOrders,
    IReadOnlyList<OrderStatusChange> StatusChanges)
{
    /// <summary>Пустой набор изменений.</summary>
    public static readonly OrderChangeSet Empty = new([], [], []);

    /// <summary>Есть ли что показывать пользователю.</summary>
    public bool HasChanges =>
        CreatedOrders.Count > 0 || UpdatedOrders.Count > 0 || StatusChanges.Count > 0;
}

/// <summary>
/// Итог одной операции синхронизации.
/// </summary>
/// <param name="Outcome">Результат.</param>
/// <param name="OrdersFetched">Сколько заказов получено от API.</param>
/// <param name="Changes">Обнаруженные изменения.</param>
/// <param name="WindowFrom">Нижняя граница окна выборки.</param>
/// <param name="Duration">Длительность операции.</param>
/// <param name="Message">Пояснение (для пропуска или ошибки).</param>
/// <param name="ErrorCode">Код ошибки API.</param>
public sealed record SyncResult(
    SyncOutcome Outcome,
    int OrdersFetched,
    OrderChangeSet Changes,
    DateTime? WindowFrom,
    TimeSpan Duration,
    string? Message = null,
    int? ErrorCode = null)
{
    /// <summary>Операция выполнена успешно.</summary>
    public bool IsSuccess => Outcome == SyncOutcome.Success;
}
