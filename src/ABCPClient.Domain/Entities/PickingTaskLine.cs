using ABCPClient.Domain.Models;

namespace ABCPClient.Domain.Entities;

/// <summary>
/// Строка задания на сборку.
/// </summary>
public class PickingTaskLine
{
    /// <summary>Локальный первичный ключ.</summary>
    public int Id { get; set; }

    /// <summary>Задание, к которому относится строка.</summary>
    public int PickingTaskId { get; set; }

    /// <summary>Задание.</summary>
    public PickingTask? Task { get; set; }

    /// <summary>Имя производителя.</summary>
    public string Brand { get; set; } = string.Empty;

    /// <summary>Номер детали в том виде, в котором он записан в заказе.</summary>
    public string Number { get; set; } = string.Empty;

    /// <summary>
    /// Сопоставительный ключ «бренд + номер» без разделителей и регистра.
    /// </summary>
    /// <remarks>
    /// Тот же ключ, что у карточек товаров (<see cref="ArticleKey.Match"/>): по нему
    /// строка находится по сканеру и связывается с остатками из 1С, где артикул
    /// записан иначе.
    /// </remarks>
    public string MatchKey { get; set; } = string.Empty;

    /// <summary>Наименование детали.</summary>
    public string? Description { get; set; }

    /// <summary>Сколько заказал клиент.</summary>
    public decimal OrderedQuantity { get; set; }

    /// <summary>
    /// Сколько доступно к сборке по данным о наличии.
    /// </summary>
    /// <remarks>
    /// Может быть меньше заказанного: часть позиции лежит на складе,
    /// часть ещё едет. Тогда собирается доступная часть.
    /// </remarks>
    public decimal AvailableQuantity { get; set; }

    /// <summary>Сколько фактически собрано.</summary>
    public decimal PickedQuantity { get; set; }

    /// <summary>Наличие детали.</summary>
    public StockAvailability Availability { get; set; }

    /// <summary>Ожидаемая дата поступления, если деталь в пути.</summary>
    public DateTime? IncomingEta { get; set; }

    /// <summary>Место хранения на складе, если известно.</summary>
    public string? StockLocation { get; set; }

    /// <summary>
    /// Штрихкоды детали через точку с запятой — снимок на момент создания задания.
    /// </summary>
    /// <remarks>
    /// Копия, а не ссылка на карточку товара: терминал должен искать по сканеру
    /// и когда карточку из кэша уже удалили, и когда сети нет.
    /// </remarks>
    public string? Barcodes { get; set; }

    /// <summary>
    /// Идентификатор позиции в портале ABCP, если строка пришла из интернет-заказа.
    /// </summary>
    /// <remarks>
    /// Нужен, чтобы вернуть статус позиции в ABCP после сборки:
    /// <c>itemKey</c> для этого не годится, он не уникален.
    /// </remarks>
    public long? PositionId { get; set; }

    /// <summary>Когда строку собрали.</summary>
    public DateTime? PickedAt { get; set; }

    /// <summary>Кто собрал: имя устройства или сборщика.</summary>
    public string? PickedBy { get; set; }

    /// <summary>Строка собрана полностью.</summary>
    public bool IsComplete => PickedQuantity >= Effective;

    /// <summary>Строку начали собирать.</summary>
    public bool IsStarted => PickedQuantity > 0;

    /// <summary>
    /// Сколько по этой строке реально можно собрать.
    /// </summary>
    /// <remarks>
    /// Заказано может быть больше, чем есть на складе. Считать строку несобранной
    /// из-за того, что остального товара просто нет, неправильно: задание тогда
    /// никогда не закроется.
    /// </remarks>
    public decimal Effective => AvailableQuantity <= 0 || AvailableQuantity > OrderedQuantity
        ? OrderedQuantity
        : AvailableQuantity;

    /// <summary>
    /// Записывает факт сборки.
    /// </summary>
    /// <remarks>
    /// Значение задаётся, а не прибавляется: терминал повторяет отправку при обрыве
    /// связи, и прибавление удваивало бы факт. Итог ограничен заказанным количеством —
    /// собрать больше, чем в заказе, нельзя.
    /// </remarks>
    /// <param name="quantity">Собранное количество.</param>
    /// <param name="pickedBy">Кто собрал.</param>
    /// <param name="moment">Момент сборки.</param>
    /// <exception cref="ArgumentOutOfRangeException">Количество отрицательное.</exception>
    public void RegisterPick(decimal quantity, string? pickedBy, DateTime moment)
    {
        if (quantity < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                quantity,
                "Собранное количество не может быть отрицательным");
        }

        PickedQuantity = Math.Min(quantity, OrderedQuantity);
        PickedBy = pickedBy;
        PickedAt = PickedQuantity > 0 ? moment : null;
    }
}
