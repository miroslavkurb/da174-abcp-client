using System.Text.Json.Serialization;

namespace ABCPClient.Application.DTO;

/// <summary>
/// Запись истории изменения статуса позиции
/// (<c>cp/order/statusHistory</c>, <c>cp/orders/statusHistory</c>).
/// </summary>
public sealed class PositionStatusHistoryDto
{
    /// <summary>Идентификатор позиции. Приходит только в пакетном варианте операции.</summary>
    [JsonPropertyName("id")]
    public long? Id { get; set; }

    /// <summary>Код статуса.</summary>
    [JsonPropertyName("statusCode")]
    public int StatusCode { get; set; }

    /// <summary>Название статуса.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>Дата и время изменения статуса.</summary>
    [JsonPropertyName("datetime")]
    public DateTime? DateTime { get; set; }

    /// <summary>Идентификатор автора изменения.</summary>
    [JsonPropertyName("managerId")]
    public int? ManagerId { get; set; }

    /// <summary>Имя автора изменения.</summary>
    [JsonPropertyName("managerName")]
    public string? ManagerName { get; set; }
}

/// <summary>
/// Ответ пакетной операции <c>cp/orders/statusHistory</c>.
/// </summary>
/// <remarks>
/// Ответ содержит узел <c>positions</c>, сгруппированный по принятым идентификаторам позиций.
/// Точная форма узла в документации не приведена, поэтому клиент API при несовпадении
/// должен логировать сырой ответ, а не падать.
/// </remarks>
public sealed class BatchStatusHistoryDto
{
    /// <summary>История статусов по идентификатору позиции.</summary>
    [JsonPropertyName("positions")]
    public Dictionary<string, List<PositionStatusHistoryDto>> Positions { get; set; } = [];
}
