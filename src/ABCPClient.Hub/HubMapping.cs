using ABCPClient.Application.DTO;
using ABCPClient.Contracts;
using ABCPClient.Domain.Entities;
using ABCPClient.Domain.Models;

namespace ABCPClient.Hub;

/// <summary>
/// Перевод доменных объектов в контракты узла.
/// </summary>
/// <remarks>
/// Отдельный слой перевода нужен, чтобы изменение домена не ломало терминалы:
/// у них своё представление, и оно меняется только вместе с версией контракта.
/// </remarks>
internal static class HubMapping
{
    /// <summary>Переводит состояние задания в код контракта.</summary>
    public static string ToCode(PickingTaskStatus status) => status switch
    {
        PickingTaskStatus.InProgress => PickingStatusCodes.InProgress,
        PickingTaskStatus.Picked => PickingStatusCodes.Picked,
        PickingTaskStatus.Cancelled => PickingStatusCodes.Cancelled,
        _ => PickingStatusCodes.New,
    };

    /// <summary>Переводит наличие в код контракта.</summary>
    public static string ToCode(StockAvailability availability) => availability switch
    {
        StockAvailability.InStock => AvailabilityCodes.InStock,
        StockAvailability.Incoming => AvailabilityCodes.Incoming,
        _ => AvailabilityCodes.Unknown,
    };

    /// <summary>Переводит строку списка заданий.</summary>
    public static PickingTaskSummary ToSummary(PickingTaskListItem item) => new(
        item.Id,
        item.Number,
        item.OrderNumber,
        item.OneCOrderNumber,
        item.Customer,
        ToCode(item.Status),
        new DateTimeOffset(item.CreatedAt),
        item.LinesCount,
        item.InStockLines,
        item.IncomingLines,
        item.CompleteLines);

    /// <summary>Переводит задание со строками.</summary>
    public static PickingTaskDetails ToDetails(PickingTask task, IReadOnlyDictionary<string, ArticleCard> cards)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(cards);

        PickingTaskSummary summary = new(
            task.Id,
            task.Number,
            task.OrderNumber,
            task.OneCOrderNumber,
            task.Customer,
            ToCode(task.Status),
            new DateTimeOffset(task.CreatedAt),
            task.Lines.Count,
            task.InStockLines,
            task.IncomingLines,
            task.CompleteLines);

        PickingLine[] lines = task.Lines
            .OrderByDescending(line => line.Availability == StockAvailability.InStock)
            .ThenBy(line => line.Brand, StringComparer.OrdinalIgnoreCase)
            .ThenBy(line => line.Number, StringComparer.OrdinalIgnoreCase)
            .Select(line =>
            {
                cards.TryGetValue(new ArticleRef(line.Brand, line.Number).Key, out ArticleCard? card);

                return new PickingLine(
                    line.Id,
                    line.Brand,
                    line.Number,
                    line.Description ?? card?.Description,
                    line.OrderedQuantity,
                    line.AvailableQuantity,
                    line.PickedQuantity,
                    ToCode(line.Availability),
                    line.IncomingEta is { } eta ? new DateTimeOffset(eta) : null,
                    line.StockLocation,
                    SplitBarcodes(line.Barcodes ?? card?.Barcodes),
                    card?.ImageName);
            })
            .ToArray();

        return new PickingTaskDetails(summary, lines);
    }

    /// <summary>Переводит найденную деталь.</summary>
    public static ArticleMatch ToMatch(ArticleCard card)
    {
        ArgumentNullException.ThrowIfNull(card);

        return new ArticleMatch(
            card.Brand,
            card.Number,
            card.Description,
            SplitBarcodes(card.Barcodes),
            card.ImageName);
    }

    /// <summary>Переводит вид опознания детали в код контракта.</summary>
    public static string ToCode(ArticleLookupKind kind) => kind switch
    {
        ArticleLookupKind.Barcode => "barcode",
        ArticleLookupKind.Search => "search",
        ArticleLookupKind.Empty => "empty",
        _ => "not-found",
    };

    /// <summary>
    /// Разбирает штрихкоды, сохранённые строкой через точку с запятой.
    /// </summary>
    private static IReadOnlyList<string> SplitBarcodes(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
