namespace ABCPClient.Domain.Entities;

/// <summary>
/// Запись истории изменения статуса позиции заказа
/// (операции <c>cp/order/statusHistory</c> и <c>cp/orders/statusHistory</c>).
/// </summary>
public class OrderItemStatusHistoryEntry
{
    /// <summary>Локальный первичный ключ.</summary>
    public int Id { get; set; }

    /// <summary>Ссылка на локальную позицию заказа.</summary>
    public int OrderItemId { get; set; }

    /// <summary>Позиция заказа.</summary>
    public OrderItem? OrderItem { get; set; }

    /// <summary>Код статуса.</summary>
    public int StatusCode { get; set; }

    /// <summary>Название статуса.</summary>
    public string? Status { get; set; }

    /// <summary>Дата и время изменения статуса (время портала).</summary>
    public DateTime ChangedAt { get; set; }

    /// <summary>Идентификатор автора изменения.</summary>
    public int? ManagerId { get; set; }

    /// <summary>Имя автора изменения.</summary>
    public string? ManagerName { get; set; }
}
