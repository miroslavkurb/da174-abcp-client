using ABCPClient.Domain.Models;

namespace ABCPClient.Domain.Entities;

/// <summary>
/// Заказ клиента, полученный из API ABCP.
/// </summary>
/// <remarks>
/// Даты приходят из API строками во времени портала и хранятся как есть,
/// без перевода в UTC: часовой пояс сервера в документации не определён,
/// а любые пересчёты исказят сравнение с полем <c>dateUpdated</c> при следующей синхронизации.
/// </remarks>
public class Order
{
    /// <summary>Локальный первичный ключ.</summary>
    public int Id { get; set; }

    /// <summary>
    /// Онлайн-номер заказа в портале (поле <c>number</c>). Стабильный ключ заказа.
    /// </summary>
    public string Number { get; set; } = string.Empty;

    /// <summary>
    /// Номер заказа в учётной системе (<c>internalNumber</c>).
    /// Точка связи с 1С:УТ — обмен работает в терминах пары номеров.
    /// </summary>
    public string? InternalNumber { get; set; }

    /// <summary>Номер заказа во внутренней системе учёта клиента (<c>clientOrderNumber</c>).</summary>
    public string? ClientOrderNumber { get; set; }

    /// <summary>Идентификатор клиента на сайте.</summary>
    public int? UserId { get; set; }

    /// <summary>Имя покупателя.</summary>
    public string? UserName { get; set; }

    /// <summary>Название организации покупателя.</summary>
    public string? UserFullName { get; set; }

    /// <summary>Электронная почта покупателя.</summary>
    public string? UserEmail { get; set; }

    /// <summary>Контактный телефон покупателя.</summary>
    public string? UserMobile { get; set; }

    /// <summary>Внутренний код пользователя.</summary>
    public string? UserCode { get; set; }

    /// <summary>Идентификатор профиля клиента.</summary>
    public int? ProfileId { get; set; }

    /// <summary>Идентификатор менеджера, если заказ создан менеджером.</summary>
    public int? ManagerId { get; set; }

    /// <summary>Количество позиций по данным API (<c>positionsQuantity</c>).</summary>
    public int PositionsQuantity { get; set; }

    /// <summary>Сумма заказа.</summary>
    public decimal Sum { get; set; }

    /// <summary>Долг по оплате заказа.</summary>
    public decimal? Debt { get; set; }

    /// <summary>Признак успешной онлайн-оплаты.</summary>
    public bool IsPaid { get; set; }

    /// <summary>Дата размещения заказа.</summary>
    public DateTime? Date { get; set; }

    /// <summary>Дата последнего обновления заказа. Основа инкрементальной синхронизации.</summary>
    public DateTime? DateUpdated { get; set; }

    /// <summary>Дата отгрузки.</summary>
    public DateTime? ShipmentDate { get; set; }

    /// <summary>Комментарий к заказу.</summary>
    public string? Comment { get; set; }

    /// <summary>Адрес доставки.</summary>
    public string? DeliveryAddress { get; set; }

    /// <summary>Офис самовывоза.</summary>
    public string? DeliveryOffice { get; set; }

    /// <summary>Тип доставки.</summary>
    public string? DeliveryType { get; set; }

    /// <summary>Стоимость доставки.</summary>
    public decimal? DeliveryCost { get; set; }

    /// <summary>Тип оплаты.</summary>
    public string? PaymentType { get; set; }

    /// <summary>Признак удалённого заказа.</summary>
    public bool IsDeleted { get; set; }

    /// <summary>Признак архивного заказа.</summary>
    public bool IsArchive { get; set; }

    /// <summary>
    /// Код статуса, преобладающий среди позиций заказа. Значение производное:
    /// в API у заказа собственного статуса нет.
    /// </summary>
    public int? DominantStatusCode { get; set; }

    /// <summary>Название преобладающего статуса.</summary>
    public string? DominantStatusName { get; set; }

    /// <summary>Признак того, что позиции заказа находятся в разных статусах.</summary>
    public bool HasMixedStatuses { get; set; }

    /// <summary>Момент последней успешной синхронизации заказа (локальное время машины).</summary>
    public DateTime SyncedAt { get; set; }

    /// <summary>Позиции заказа.</summary>
    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();

    /// <summary>
    /// Пересчитывает производные поля статуса по текущему составу позиций.
    /// </summary>
    public void RefreshStatusAggregate()
    {
        OrderStatusAggregate aggregate = OrderStatusAggregate.FromItems(Items);

        DominantStatusCode = aggregate.DominantStatusCode;
        DominantStatusName = aggregate.DominantStatusName;
        HasMixedStatuses = aggregate.IsMixed;
    }
}
