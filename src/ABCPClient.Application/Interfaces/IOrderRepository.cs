using ABCPClient.Application.DTO;
using ABCPClient.Domain.Entities;

namespace ABCPClient.Application.Interfaces;

/// <summary>
/// Доступ к заказам в локальной базе.
/// </summary>
public interface IOrderRepository
{
    /// <summary>
    /// Возвращает строки таблицы заказов по фильтру.
    /// </summary>
    /// <param name="filter">Условия выборки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<IReadOnlyList<OrderListItem>> GetListAsync(
        OrderFilter filter,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает количество заказов, удовлетворяющих фильтру.
    /// </summary>
    /// <param name="filter">Условия выборки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<int> CountAsync(OrderFilter filter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает заказ с позициями по онлайн-номеру.
    /// </summary>
    /// <param name="number">Онлайн-номер заказа.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Order?> GetByNumberAsync(string number, CancellationToken cancellationToken = default);

    /// <summary>
    /// Добавляет новые и обновляет существующие заказы, возвращая обнаруженные изменения.
    /// </summary>
    /// <param name="orders">Заказы, полученные от API.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<OrderChangeSet> UpsertAsync(
        IReadOnlyCollection<OrderDto> orders,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает максимальную дату обновления среди сохранённых заказов.
    /// Используется как точка продолжения синхронизации, если она не сохранена в настройках.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<DateTime?> GetMaxDateUpdatedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает номера и даты заказов, не помеченных удалёнными.
    /// Нужно для сверки с порталом: заказ мог быть удалён после того,
    /// как мы его сохранили.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<IReadOnlyList<ActiveOrderRef>> GetActiveOrderRefsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Помечает заказы удалёнными вместе с их позициями.
    /// </summary>
    /// <param name="numbers">Номера заказов.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Сколько заказов было помечено (уже удалённые не считаются).</returns>
    Task<int> MarkDeletedAsync(
        IReadOnlyCollection<string> numbers,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Ссылка на активный (неудалённый) заказ локальной базы.
/// </summary>
/// <param name="Number">Онлайн-номер заказа.</param>
/// <param name="Date">
/// Дата размещения заказа. Нужна, чтобы при сверке задать окно по дате создания:
/// без фильтра по дате API само ограничивает выборку последними 30 днями,
/// а слишком широкий диапазон отклоняет с ошибкой
/// «Диапазон выбора даты создания заказа не должен превышать 1 год».
/// </param>
public sealed record ActiveOrderRef(string Number, DateTime? Date);

/// <summary>
/// Доступ к справочнику статусов в локальной базе.
/// </summary>
public interface IStatusCatalogRepository
{
    /// <summary>Возвращает справочник статусов.</summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<IReadOnlyList<OrderStatus>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Обновляет справочник статусов данными из API.
    /// </summary>
    /// <param name="statuses">Статусы из API.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Сколько записей добавлено или обновлено.</returns>
    Task<int> UpsertAsync(
        IReadOnlyCollection<OrderStatusDto> statuses,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Доступ к журналу синхронизации.
/// </summary>
public interface ISyncLogRepository
{
    /// <summary>Записывает результат операции синхронизации.</summary>
    /// <param name="entry">Запись журнала.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task AddAsync(SyncLogEntry entry, CancellationToken cancellationToken = default);

    /// <summary>Возвращает последние записи журнала.</summary>
    /// <param name="take">Сколько записей вернуть.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<IReadOnlyList<SyncLogEntry>> GetRecentAsync(
        int take = 200,
        CancellationToken cancellationToken = default);
}
