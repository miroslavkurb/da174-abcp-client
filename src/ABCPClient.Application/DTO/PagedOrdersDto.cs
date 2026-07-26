using System.Text.Json.Serialization;

namespace ABCPClient.Application.DTO;

/// <summary>
/// Ответ операции <c>cp/orders</c> с параметром <c>format=p</c>.
/// </summary>
/// <remarks>
/// Единственный способ узнать общее количество заказов по фильтру:
/// без <c>format=p</c> API отдаёт просто массив и молча обрезает выдачу на 1000 записях.
/// </remarks>
public sealed class PagedOrdersDto
{
    /// <summary>Заказы текущей страницы.</summary>
    [JsonPropertyName("items")]
    public List<OrderDto> Items { get; set; } = [];

    /// <summary>Общее количество заказов, удовлетворяющих фильтру.</summary>
    [JsonPropertyName("count")]
    public int Count { get; set; }
}
