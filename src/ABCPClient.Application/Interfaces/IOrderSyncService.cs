using ABCPClient.Application.DTO;

namespace ABCPClient.Application.Interfaces;

/// <summary>
/// Синхронизация заказов с API ABCP.
/// </summary>
public interface IOrderSyncService
{
    /// <summary>
    /// Выполняет инкрементальную синхронизацию: забирает новые и изменённые заказы
    /// и применяет их к локальной базе.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<SyncResult> SyncAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Обновляет справочник статусов из API.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Количество статусов в справочнике.</returns>
    Task<int> RefreshStatusCatalogAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Сверяет сохранённые заказы с порталом и помечает удалённые.
    /// </summary>
    /// <remarks>
    /// Инкрементальная синхронизация узнаёт об удалении только пока заказ попадает
    /// в окно по <c>dateUpdated</c>. Заказ, удалённый раньше, останется в базе,
    /// поэтому нужна отдельная сверка по номерам с параметром <c>withDeleted</c>.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Сколько заказов помечено удалёнными.</returns>
    Task<int> ReconcileDeletedOrdersAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Оповещение подписчиков о завершении синхронизации.
/// </summary>
/// <remarks>
/// Развязывает фоновую службу и слой представления: служба публикует результат,
/// модели представления и служба уведомлений его слушают, не зная друг о друге.
/// </remarks>
public interface ISyncEventBus
{
    /// <summary>Синхронизация завершилась (успешно или нет).</summary>
    event EventHandler<SyncResult>? SyncCompleted;

    /// <summary>Публикует результат синхронизации.</summary>
    /// <param name="result">Результат.</param>
    void Publish(SyncResult result);
}

/// <summary>
/// Показ уведомлений пользователю.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Уведомляет о результатах синхронизации: новых заказах и сменах статусов.
    /// </summary>
    /// <param name="changes">Обнаруженные изменения.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task NotifyAsync(OrderChangeSet changes, CancellationToken cancellationToken = default);
}

/// <summary>
/// Преобразование пароля в вид, который принимает API.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>
    /// Возвращает md5-хэш пароля в нижнем регистре — значение параметра <c>userpsw</c>.
    /// </summary>
    /// <param name="password">Пароль API-администратора.</param>
    string ToApiHash(string password);

    /// <summary>
    /// Проверяет, что строка уже является 32-символьным md5-хэшем.
    /// Позволяет вставить в настройки готовый хэш вместо пароля.
    /// </summary>
    /// <param name="value">Проверяемая строка.</param>
    bool LooksLikeHash(string? value);
}
