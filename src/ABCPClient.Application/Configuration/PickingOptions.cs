namespace ABCPClient.Application.Configuration;

/// <summary>
/// Параметры сборки заказов.
/// </summary>
public sealed class PickingOptions
{
    /// <summary>Имя секции в конфигурации.</summary>
    public const string SectionName = "Picking";

    /// <summary>Префикс номера задания.</summary>
    public string NumberPrefix { get; set; } = "СБ-";

    /// <summary>
    /// Коды статусов позиций ABCP, означающие «товар на складе».
    /// </summary>
    /// <remarks>
    /// Пока нет выгрузки остатков из 1С, наличие определять больше нечем: в ответе
    /// API про физическое наличие ничего нет. Коды берутся из справочника статусов
    /// (панель управления → статусы заказов) и задаются в настройках приложения,
    /// потому что у каждого сайта они свои.
    /// Пустой список означает «наличие неизвестно», и это честный ответ:
    /// сборщик увидит признак «нет данных», а не ложное «нет в наличии».
    /// </remarks>
    public IReadOnlyList<int> InStockStatusCodes { get; set; } = [];

    /// <summary>
    /// Коды статусов позиций, означающие «товар в пути».
    /// </summary>
    public IReadOnlyList<int> IncomingStatusCodes { get; set; } = [];

    /// <summary>
    /// Не включать в задание позиции, отменённые или удалённые в панели управления.
    /// </summary>
    public bool SkipCancelledPositions { get; set; } = true;

    /// <summary>
    /// Считать срок поставки признаком «в пути», если статус ничего не говорит.
    /// </summary>
    /// <remarks>
    /// У позиции под заказ есть срок поставки в часах. Он не доказывает, что товар
    /// уже едет, но отличает «заказано у поставщика» от «неизвестно», и сборщику
    /// это полезнее, чем прочерк.
    /// </remarks>
    public bool TreatDeadlineAsIncoming { get; set; } = true;
}
