namespace ABCPClient.Application.DTO;

/// <summary>
/// Условия выборки заказов для операции <c>cp/orders</c>.
/// </summary>
public sealed class OrderQuery
{
    /// <summary>Начало окна по дате размещения заказа.</summary>
    public DateTime? DateCreatedStart { get; set; }

    /// <summary>Конец окна по дате размещения заказа.</summary>
    public DateTime? DateCreatedEnd { get; set; }

    /// <summary>
    /// Начало окна по дате обновления заказа. Основной фильтр инкрементальной
    /// синхронизации: возвращает и новые, и изменённые заказы.
    /// </summary>
    public DateTime? DateUpdatedStart { get; set; }

    /// <summary>Конец окна по дате обновления заказа.</summary>
    public DateTime? DateUpdatedEnd { get; set; }

    /// <summary>Номера заказов в портале.</summary>
    public IReadOnlyList<string>? Numbers { get; set; }

    /// <summary>
    /// Номера заказов в учётной системе. Учитываются только если не задан <see cref="Numbers"/>.
    /// </summary>
    public IReadOnlyList<string>? InternalNumbers { get; set; }

    /// <summary>
    /// Коды статусов позиций. Отбираются заказы, содержащие хотя бы одну позицию
    /// в одном из указанных статусов.
    /// </summary>
    public IReadOnlyList<int>? StatusCodes { get; set; }

    /// <summary>Идентификатор клиента.</summary>
    public int? UserId { get; set; }

    /// <summary>Идентификатор офиса.</summary>
    public int? OfficeId { get; set; }

    /// <summary>Возвращать удалённые заказы и позиции.</summary>
    public bool? WithDeleted { get; set; }

    /// <summary>Выбирать архивные заказы вместо активных.</summary>
    public bool? IsArchive { get; set; }

    /// <summary>Сортировка по убыванию идентификатора — новые заказы сверху.</summary>
    public bool? Descending { get; set; }

    /// <summary>Сколько записей пропустить.</summary>
    public int? Skip { get; set; }

    /// <summary>Сколько записей вернуть. Верхний предел API — 1000.</summary>
    public int? Limit { get; set; }

    /// <summary>Формат ответа.</summary>
    public OrderQueryFormat Format { get; set; } = OrderQueryFormat.Paged;
}

/// <summary>
/// Значения параметра <c>format</c> операции <c>cp/orders</c>.
/// </summary>
public enum OrderQueryFormat
{
    /// <summary>Полный ответ: массив заказов с составом позиций.</summary>
    Full = 0,

    /// <summary>
    /// <c>p</c> — заказы в поле <c>items</c> и общее количество в <c>count</c>.
    /// Единственный способ узнать размер выборки для пагинации.
    /// </summary>
    Paged = 1,

    /// <summary><c>short</c> — без содержимого позиций.</summary>
    Short = 2,

    /// <summary><c>status_only</c> — только статусы позиций. Дешёвый опрос.</summary>
    StatusOnly = 3,

    /// <summary><c>count</c> — только количество заказов.</summary>
    CountOnly = 4,

    /// <summary><c>additional</c> — плюс данные клиента при гостевом заказе.</summary>
    Additional = 5,
}
