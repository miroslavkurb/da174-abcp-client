using System.Text.Json.Serialization;

namespace ABCPClient.Application.DTO;

/// <summary>
/// Элемент справочника статусов в ответе операции <c>cp/statuses</c>.
/// </summary>
public sealed class OrderStatusDto
{
    /// <summary>Идентификатор статуса, он же <c>statusCode</c> у позиций заказа.</summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>Имя статуса.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Описание для клиента.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; set; }

    /// <summary>Флаг «Уведомить клиента».</summary>
    [JsonPropertyName("notify")]
    public bool Notify { get; set; }

    /// <summary>Флаг «Статус после оплаты заказа».</summary>
    [JsonPropertyName("paid")]
    public bool Paid { get; set; }

    /// <summary>Флаг «Начальный статус доставки/списания».</summary>
    [JsonPropertyName("startDelivery")]
    public bool StartDelivery { get; set; }

    /// <summary>Флаг «Статус доставки/списания».</summary>
    [JsonPropertyName("delivery")]
    public bool Delivery { get; set; }

    /// <summary>Флаг «Статус после размещения заказа у поставщика».</summary>
    [JsonPropertyName("placingOrder")]
    public bool PlacingOrder { get; set; }

    /// <summary>Цвет статуса.</summary>
    [JsonPropertyName("color")]
    public string? Color { get; set; }
}
