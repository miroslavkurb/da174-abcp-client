using ABCPClient.Domain.Entities;

namespace ABCPClient.Domain.Models;

/// <summary>
/// Сводка статусов заказа, вычисленная по статусам его позиций.
/// </summary>
/// <remarks>
/// В API ABCP у заказа нет поля статуса — статус есть только у позиции.
/// Порядок статусов в воронке API тоже не сообщает (в справочнике <c>cp/statuses</c>
/// есть флаги и цвет, но нет индекса сортировки), поэтому «минимальный по воронке»
/// статус вычислить нельзя. Используется честное правило:
/// преобладающий статус — самый частый среди позиций, при равенстве частот
/// берётся меньший код; если статусов больше одного, заказ помечается смешанным.
/// Удалённые позиции в расчёт не входят.
/// </remarks>
public sealed record OrderStatusAggregate
{
    /// <summary>Пустая сводка: у заказа нет позиций со статусом.</summary>
    public static readonly OrderStatusAggregate Empty = new();

    /// <summary>Код преобладающего статуса.</summary>
    public int? DominantStatusCode { get; init; }

    /// <summary>Название преобладающего статуса.</summary>
    public string? DominantStatusName { get; init; }

    /// <summary>Количество различных статусов среди позиций.</summary>
    public int DistinctStatusCount { get; init; }

    /// <summary>Количество позиций, учтённых в расчёте.</summary>
    public int CountedItems { get; init; }

    /// <summary>Позиции находятся в разных статусах.</summary>
    public bool IsMixed => DistinctStatusCount > 1;

    /// <summary>
    /// Текст для показа в таблице заказов.
    /// </summary>
    public string DisplayText => DominantStatusCode is null
        ? "Без статуса"
        : IsMixed
            ? $"{DominantStatusName} (+{DistinctStatusCount - 1})"
            : DominantStatusName ?? DominantStatusCode.Value.ToString();

    /// <summary>
    /// Вычисляет сводку по позициям заказа.
    /// </summary>
    /// <param name="items">Позиции заказа.</param>
    public static OrderStatusAggregate FromItems(IEnumerable<OrderItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        List<OrderItem> counted = items
            .Where(item => !item.IsDeleted && item.StatusCode.HasValue)
            .ToList();

        if (counted.Count == 0)
        {
            return Empty;
        }

        var groups = counted
            .GroupBy(item => item.StatusCode!.Value)
            .Select(group => new
            {
                StatusCode = group.Key,
                Count = group.Count(),
                Name = group.Select(item => item.Status).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)),
            })
            .OrderByDescending(group => group.Count)
            .ThenBy(group => group.StatusCode)
            .ToList();

        return new OrderStatusAggregate
        {
            DominantStatusCode = groups[0].StatusCode,
            DominantStatusName = groups[0].Name,
            DistinctStatusCount = groups.Count,
            CountedItems = counted.Count,
        };
    }
}
