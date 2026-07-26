namespace ABCPClient.Domain.Models;

/// <summary>
/// Вид операции синхронизации с API.
/// </summary>
public enum SyncOperation
{
    /// <summary>Загрузка заказов и их позиций.</summary>
    Orders = 1,

    /// <summary>Обновление справочника статусов.</summary>
    Statuses = 2,

    /// <summary>Загрузка истории изменения статусов позиций.</summary>
    StatusHistory = 3,

    /// <summary>Проверка подключения к API.</summary>
    ConnectionCheck = 4,
}

/// <summary>
/// Результат операции синхронизации.
/// </summary>
public enum SyncOutcome
{
    /// <summary>Операция выполнена.</summary>
    Success = 1,

    /// <summary>Операция завершилась ошибкой.</summary>
    Failed = 2,

    /// <summary>Операция пропущена (например, не настроено подключение).</summary>
    Skipped = 3,
}
