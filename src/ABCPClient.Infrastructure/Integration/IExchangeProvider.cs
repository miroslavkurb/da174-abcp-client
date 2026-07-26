using ABCPClient.Application.DTO;
using ABCPClient.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace ABCPClient.Infrastructure.Integration;

/// <summary>
/// Двусторонний обмен данными с внешней учётной системой.
/// </summary>
/// <remarks>
/// Слой интеграции сознательно отделён от кода ABCP: код платформы ничего не знает
/// об 1С, а реализация обмена — о деталях API ABCP, она работает с доменными сущностями.
/// Связка заказов строится на паре «онлайн-номер (<c>number</c>) ↔ номер в учётной
/// системе (<c>internalNumber</c>)», которую API отдаёт сам, поэтому собственный
/// идентификатор обмена не нужен.
/// </remarks>
public interface IExchangeProvider
{
    /// <summary>Имя провайдера для журнала и интерфейса.</summary>
    string Name { get; }

    /// <summary>Готов ли провайдер к обмену (настроен и доступен).</summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Выгружает во внешнюю систему изменения, полученные из ABCP:
    /// новые заказы, смены статусов, оплаты, комментарии, трек-номера.
    /// </summary>
    /// <param name="changes">Изменения последней синхронизации.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task PushAsync(OrderChangeSet changes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Забирает из внешней системы данные, которые нужно применить в ABCP:
    /// созданные и изменённые заказы, статусы, цены, остатки.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Заказы внешней системы, ожидающие применения.</returns>
    Task<IReadOnlyList<Order>> PullAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Заглушка обмена с 1С:УТ 11.4.
/// </summary>
/// <remarks>
/// Реализация появится отдельным этапом; сейчас класс фиксирует контракт и точку
/// расширения, чтобы добавление обмена не потребовало переделки приложения.
/// </remarks>
public sealed class OneCExchangeProvider : IExchangeProvider
{
    private readonly ILogger<OneCExchangeProvider> _logger;

    /// <summary>Создаёт провайдер.</summary>
    public OneCExchangeProvider(ILogger<OneCExchangeProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "1С:Управление торговлей 11.4";

    /// <inheritdoc />
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    /// <inheritdoc />
    public Task PushAsync(OrderChangeSet changes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);

        _logger.LogDebug(
            "Обмен с 1С не настроен: к выгрузке готовы {Created} новых заказов и {StatusChanges} смен статусов",
            changes.CreatedOrders.Count,
            changes.StatusChanges.Count);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Order>> PullAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Order>>([]);
}
