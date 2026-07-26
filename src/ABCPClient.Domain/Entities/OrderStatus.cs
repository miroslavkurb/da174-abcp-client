namespace ABCPClient.Domain.Entities;

/// <summary>
/// Элемент справочника статусов позиций заказов (операция <c>cp/statuses</c>).
/// </summary>
public class OrderStatus
{
    /// <summary>
    /// Код статуса. В API это поле <c>id</c>, оно же <c>statusCode</c> у позиций заказа.
    /// </summary>
    public int StatusCode { get; set; }

    /// <summary>Имя статуса.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Описание для клиента.</summary>
    public string? Comment { get; set; }

    /// <summary>Флаг «Уведомить клиента».</summary>
    public bool Notify { get; set; }

    /// <summary>Флаг «Статус после оплаты заказа».</summary>
    public bool Paid { get; set; }

    /// <summary>Флаг «Начальный статус доставки/списания».</summary>
    public bool StartDelivery { get; set; }

    /// <summary>Флаг «Статус доставки/списания».</summary>
    public bool Delivery { get; set; }

    /// <summary>Флаг «Статус после размещения заказа у поставщика».</summary>
    public bool PlacingOrder { get; set; }

    /// <summary>Цвет статуса, как он задан в панели управления.</summary>
    public string? Color { get; set; }

    /// <summary>Момент последнего обновления справочника.</summary>
    public DateTime SyncedAt { get; set; }
}
