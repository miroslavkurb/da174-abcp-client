using ABCPClient.Application.DTO;

namespace ABCPClient.Application.Interfaces;

/// <summary>
/// Клиент административного интерфейса API ABCP (операции <c>/cp/...</c>).
/// </summary>
/// <remarks>
/// Все операции требуют пользователя со статусом «API-администратор».
/// Реквизиты берутся из действующих настроек при каждом вызове,
/// поэтому смена настроек в окне настроек применяется без перезапуска приложения.
/// </remarks>
public interface IAbcpApiClient
{
    /// <summary>
    /// Возвращает страницу заказов по условиям фильтрации (<c>cp/orders</c>).
    /// </summary>
    /// <param name="query">Условия выборки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<OrderPage> GetOrdersAsync(OrderQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает количество заказов по условиям фильтрации, без самих заказов.
    /// </summary>
    /// <param name="query">Условия выборки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<int> GetOrdersCountAsync(OrderQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает один заказ по онлайн-номеру (<c>cp/order</c>).
    /// </summary>
    /// <param name="number">Онлайн-номер заказа.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Заказ или <c>null</c>, если заказ не найден.</returns>
    Task<OrderDto?> GetOrderAsync(string number, CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает справочник статусов позиций заказов (<c>cp/statuses</c>).
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<IReadOnlyList<OrderStatusDto>> GetStatusesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает историю изменения статусов для указанных позиций
    /// (пакетная операция <c>cp/orders/statusHistory</c>).
    /// </summary>
    /// <param name="positionIds">Идентификаторы позиций заказов.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<IReadOnlyDictionary<long, IReadOnlyList<PositionStatusHistoryDto>>> GetStatusHistoryAsync(
        IReadOnlyCollection<long> positionIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает карточки товаров с изображениями и свойствами
    /// (пакетная операция <c>cp/articles/info/batch</c>).
    /// </summary>
    /// <param name="articles">Детали в виде пары «бренд + номер», не более 100 в одном запросе.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<IReadOnlyList<ArticleInfoDto>> GetArticlesInfoAsync(
        IReadOnlyCollection<ArticleRef> articles,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Проверяет доступность API и корректность реквизитов.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<ConnectionCheckResult> CheckConnectionAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Страница заказов.
/// </summary>
/// <param name="Orders">Заказы страницы.</param>
/// <param name="TotalCount">
/// Общее количество заказов по фильтру. Известно только при <c>format=p</c>,
/// иначе равно количеству полученных записей.
/// </param>
public sealed record OrderPage(IReadOnlyList<OrderDto> Orders, int TotalCount);

/// <summary>
/// Результат проверки подключения.
/// </summary>
/// <param name="IsSuccess">Подключение работает.</param>
/// <param name="Message">Пояснение для пользователя.</param>
/// <param name="ErrorCode">Код ошибки API, если он был получен.</param>
public sealed record ConnectionCheckResult(bool IsSuccess, string Message, int? ErrorCode = null);
