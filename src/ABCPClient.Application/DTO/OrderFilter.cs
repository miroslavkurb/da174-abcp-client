namespace ABCPClient.Application.DTO;

/// <summary>
/// Условия выборки заказов из локальной базы для показа в таблице.
/// </summary>
public sealed class OrderFilter
{
    /// <summary>Поиск по номеру заказа, номеру в учётной системе, клиенту, бренду или артикулу позиции.</summary>
    public string? SearchText { get; set; }

    /// <summary>Фильтр по преобладающему статусу заказа.</summary>
    public int? StatusCode { get; set; }

    /// <summary>Начало периода по дате заказа.</summary>
    public DateTime? DateFrom { get; set; }

    /// <summary>Конец периода по дате заказа.</summary>
    public DateTime? DateTo { get; set; }

    /// <summary>Показывать удалённые заказы.</summary>
    public bool IncludeDeleted { get; set; }

    /// <summary>Сколько записей пропустить.</summary>
    public int Skip { get; set; }

    /// <summary>Сколько записей вернуть.</summary>
    public int Take { get; set; } = 500;
}

/// <summary>
/// Строка таблицы заказов.
/// </summary>
/// <param name="Number">Онлайн-номер заказа.</param>
/// <param name="InternalNumber">Номер в учётной системе.</param>
/// <param name="Date">Дата заказа.</param>
/// <param name="Customer">Клиент.</param>
/// <param name="StatusText">Статус (сводка по позициям).</param>
/// <param name="StatusCode">Код преобладающего статуса.</param>
/// <param name="StatusColor">Цвет статуса из справочника.</param>
/// <param name="HasMixedStatuses">Позиции в разных статусах.</param>
/// <param name="Sum">Сумма заказа.</param>
/// <param name="PositionsCount">Количество позиций.</param>
/// <param name="DateUpdated">Дата последнего изменения.</param>
/// <param name="IsPaid">Заказ оплачен онлайн.</param>
/// <param name="IsDeleted">Заказ удалён в панели управления.</param>
public sealed record OrderListItem(
    string Number,
    string? InternalNumber,
    DateTime? Date,
    string? Customer,
    string StatusText,
    int? StatusCode,
    string? StatusColor,
    bool HasMixedStatuses,
    decimal Sum,
    int PositionsCount,
    DateTime? DateUpdated,
    bool IsPaid,
    bool IsDeleted);
