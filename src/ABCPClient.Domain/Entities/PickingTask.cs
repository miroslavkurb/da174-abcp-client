using ABCPClient.Domain.Models;

namespace ABCPClient.Domain.Entities;

/// <summary>
/// Задание на сборку заказа.
/// </summary>
/// <remarks>
/// Заказ живёт в 1С и в ABCP, а задание — только здесь: это рабочий документ склада.
/// Итог сборки уходит в 1С файлом, документ по нему создаёт менеджер, поэтому задание
/// не претендует на роль учётного документа и своей нумерации в 1С не занимает.
/// </remarks>
public class PickingTask
{
    /// <summary>Локальный первичный ключ.</summary>
    public int Id { get; set; }

    /// <summary>Номер задания, сквозной по приложению.</summary>
    public string Number { get; set; } = string.Empty;

    /// <summary>Онлайн-номер заказа в ABCP, если заказ пришёл из интернет-магазина.</summary>
    public string? OrderNumber { get; set; }

    /// <summary>
    /// Номер «Заказа клиента» в 1С, если он известен.
    /// </summary>
    /// <remarks>
    /// У заказов физического магазина это единственный номер: в ABCP их нет.
    /// Для интернет-заказов совпадает с <c>internalNumber</c> из API ABCP.
    /// </remarks>
    public string? OneCOrderNumber { get; set; }

    /// <summary>Клиент — чтобы сборщик понимал, что комплектует.</summary>
    public string? Customer { get; set; }

    /// <summary>Склад, с которого собирают.</summary>
    public string? Warehouse { get; set; }

    /// <summary>Состояние задания.</summary>
    public PickingTaskStatus Status { get; set; }

    /// <summary>Когда задание создано.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Когда сборку начали.</summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>Когда сборку закрыли.</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>Кто закрыл задание.</summary>
    public string? CompletedBy { get; set; }

    /// <summary>Когда итог сборки выгружен для 1С.</summary>
    public DateTime? ExportedAt { get; set; }

    /// <summary>Комментарий.</summary>
    public string? Comment { get; set; }

    /// <summary>Строки задания.</summary>
    public List<PickingTaskLine> Lines { get; set; } = [];

    /// <summary>Сколько строк доступно к сборке прямо сейчас.</summary>
    public int InStockLines => Lines.Count(line => line.Availability == StockAvailability.InStock);

    /// <summary>Сколько строк ждут поступления.</summary>
    public int IncomingLines => Lines.Count(line => line.Availability == StockAvailability.Incoming);

    /// <summary>Сколько строк собрано полностью.</summary>
    public int CompleteLines => Lines.Count(line => line.IsComplete);

    /// <summary>
    /// Пересчитывает состояние задания по строкам.
    /// </summary>
    /// <remarks>
    /// Состояние производное, а не выставляемое вручную: иначе оно расходится
    /// с фактом сборки. Отмена — исключение: это решение человека, и пересчёт
    /// её не отменяет.
    /// Строки, которых нет в наличии, собрать нельзя, поэтому задание считается
    /// собранным, когда закрыто всё, что было доступно.
    /// </remarks>
    public void RefreshStatus()
    {
        if (Status == PickingTaskStatus.Cancelled)
        {
            return;
        }

        if (Lines.Count == 0)
        {
            Status = PickingTaskStatus.New;
            return;
        }

        PickingTaskLine[] pickable = Lines
            .Where(line => line.Availability == StockAvailability.InStock)
            .ToArray();

        bool anyPicked = Lines.Exists(line => line.IsStarted);

        // Если в наличии нет ничего, задание закрывать нечем — оно остаётся ждать
        // поступления, даже когда сборщик уже что-то отметил.
        bool allPicked = pickable.Length > 0 && pickable.All(line => line.IsComplete);

        Status = allPicked
            ? PickingTaskStatus.Picked
            : anyPicked
                ? PickingTaskStatus.InProgress
                : PickingTaskStatus.New;
    }
}
