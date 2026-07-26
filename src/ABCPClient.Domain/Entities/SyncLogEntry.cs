using ABCPClient.Domain.Models;

namespace ABCPClient.Domain.Entities;

/// <summary>
/// Запись журнала синхронизации: что делали, сколько получили и чем закончилось.
/// </summary>
public class SyncLogEntry
{
    /// <summary>Локальный первичный ключ.</summary>
    public int Id { get; set; }

    /// <summary>Вид операции.</summary>
    public SyncOperation Operation { get; set; }

    /// <summary>Результат операции.</summary>
    public SyncOutcome Outcome { get; set; }

    /// <summary>Начало операции (локальное время машины).</summary>
    public DateTime StartedAt { get; set; }

    /// <summary>Окончание операции.</summary>
    public DateTime? FinishedAt { get; set; }

    /// <summary>
    /// Нижняя граница окна выборки, переданная в API (<c>dateUpdatedStart</c>).
    /// Хранится для разбора пропусков в синхронизации.
    /// </summary>
    public DateTime? WindowFrom { get; set; }

    /// <summary>Сколько заказов вернуло API.</summary>
    public int OrdersFetched { get; set; }

    /// <summary>Сколько заказов добавлено локально.</summary>
    public int OrdersCreated { get; set; }

    /// <summary>Сколько заказов обновлено локально.</summary>
    public int OrdersUpdated { get; set; }

    /// <summary>Сколько позиций сменило статус.</summary>
    public int StatusChanges { get; set; }

    /// <summary>Код ошибки API (<c>errorCode</c>), если операция завершилась ошибкой.</summary>
    public int? ErrorCode { get; set; }

    /// <summary>Сообщение об ошибке или пояснение к результату.</summary>
    public string? Message { get; set; }

    /// <summary>Длительность операции.</summary>
    public TimeSpan? Duration => FinishedAt is null ? null : FinishedAt.Value - StartedAt;
}
