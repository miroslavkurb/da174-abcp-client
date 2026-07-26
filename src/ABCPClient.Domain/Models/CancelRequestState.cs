namespace ABCPClient.Domain.Models;

/// <summary>
/// Состояние запроса на удаление позиции заказа (поле <c>isCanceled</c> в API).
/// </summary>
public enum CancelRequestState
{
    /// <summary>Запрос не отправлялся.</summary>
    NotRequested = 0,

    /// <summary>Запрос отправлен клиентом.</summary>
    Requested = 1,

    /// <summary>Запрос отклонён менеджером.</summary>
    RejectedByManager = 2,
}
