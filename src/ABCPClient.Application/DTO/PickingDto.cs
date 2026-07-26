using ABCPClient.Domain.Models;

namespace ABCPClient.Application.DTO;

/// <summary>
/// Строка списка заданий на сборку.
/// </summary>
/// <param name="Id">Локальный идентификатор задания.</param>
/// <param name="Number">Номер задания.</param>
/// <param name="OrderNumber">Онлайн-номер заказа ABCP.</param>
/// <param name="OneCOrderNumber">Номер заказа клиента в 1С.</param>
/// <param name="Customer">Клиент.</param>
/// <param name="Status">Состояние задания.</param>
/// <param name="CreatedAt">Когда создано.</param>
/// <param name="CompletedAt">Когда закрыто.</param>
/// <param name="LinesCount">Всего строк.</param>
/// <param name="InStockLines">Строк в наличии.</param>
/// <param name="IncomingLines">Строк в пути.</param>
/// <param name="CompleteLines">Строк собрано полностью.</param>
public sealed record PickingTaskListItem(
    int Id,
    string Number,
    string? OrderNumber,
    string? OneCOrderNumber,
    string? Customer,
    PickingTaskStatus Status,
    DateTime CreatedAt,
    DateTime? CompletedAt,
    int LinesCount,
    int InStockLines,
    int IncomingLines,
    int CompleteLines);

/// <summary>
/// Условия выборки заданий на сборку.
/// </summary>
public sealed class PickingTaskFilter
{
    /// <summary>Показывать только незакрытые задания.</summary>
    public bool OnlyOpen { get; set; }

    /// <summary>Поиск по номеру задания, номеру заказа или клиенту.</summary>
    public string? SearchText { get; set; }

    /// <summary>Сколько записей вернуть.</summary>
    public int Take { get; set; } = 200;
}

/// <summary>
/// Итог создания заданий на сборку.
/// </summary>
/// <param name="Created">Созданные задания.</param>
/// <param name="SkippedExisting">
/// Заказы, для которых незакрытое задание уже было.
/// </param>
/// <param name="SkippedEmpty">Заказы, в которых нечего собирать.</param>
/// <param name="NotFound">Заказы, которых нет в локальной базе.</param>
public sealed record PickingTaskCreationResult(
    IReadOnlyList<PickingTaskListItem> Created,
    IReadOnlyList<string> SkippedExisting,
    IReadOnlyList<string> SkippedEmpty,
    IReadOnlyList<string> NotFound)
{
    /// <summary>Ничего не создано.</summary>
    public bool IsEmpty => Created.Count == 0;
}

/// <summary>
/// Запрос на фиксацию собранного количества.
/// </summary>
/// <param name="TaskId">Задание.</param>
/// <param name="LineId">Строка задания.</param>
/// <param name="Quantity">Собранное количество.</param>
/// <param name="PickedBy">Кто собрал: имя устройства или сборщика.</param>
public sealed record PickRequest(int TaskId, int LineId, decimal Quantity, string? PickedBy);
