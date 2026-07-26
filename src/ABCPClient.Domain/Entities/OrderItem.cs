using ABCPClient.Domain.Models;

namespace ABCPClient.Domain.Entities;

/// <summary>
/// Позиция заказа.
/// </summary>
/// <remarks>
/// Статус в API ABCP относится именно к позиции, а не к заказу целиком,
/// поэтому <see cref="StatusCode"/> хранится здесь, а у заказа он производный.
/// </remarks>
public class OrderItem
{
    /// <summary>Локальный первичный ключ.</summary>
    public int Id { get; set; }

    /// <summary>
    /// Идентификатор позиции в портале (поле <c>id</c>). Стабильный ключ позиции,
    /// по нему выполняется сопоставление при синхронизации.
    /// </summary>
    public long PositionId { get; set; }

    /// <summary>Ссылка на локальный заказ.</summary>
    public int OrderId { get; set; }

    /// <summary>Заказ, которому принадлежит позиция.</summary>
    public Order? Order { get; set; }

    /// <summary>Производитель.</summary>
    public string Brand { get; set; } = string.Empty;

    /// <summary>Очищенное имя производителя (<c>brandFix</c>).</summary>
    public string? BrandFix { get; set; }

    /// <summary>Номер детали (код производителя).</summary>
    public string Number { get; set; } = string.Empty;

    /// <summary>Очищенный номер детали (<c>numberFix</c>).</summary>
    public string? NumberFix { get; set; }

    /// <summary>Описание детали.</summary>
    public string? Description { get; set; }

    /// <summary>Заказанное количество.</summary>
    public decimal Quantity { get; set; }

    /// <summary>Итоговое количество (<c>quantityFinal</c>).</summary>
    public decimal QuantityFinal { get; set; }

    /// <summary>Цена поставщика за единицу (<c>priceIn</c>).</summary>
    public decimal? PriceIn { get; set; }

    /// <summary>Цена продажи за единицу (<c>priceOut</c>).</summary>
    public decimal PriceOut { get; set; }

    /// <summary>Цена продажи в валюте сайта (<c>priceInSiteCurrency</c>).</summary>
    public decimal? PriceInSiteCurrency { get; set; }

    /// <summary>Валюта покупки.</summary>
    public CurrencyId CurrencyInId { get; set; }

    /// <summary>Валюта продажи.</summary>
    public CurrencyId CurrencyOutId { get; set; }

    /// <summary>Срок поставки в часах (<c>deadline</c>).</summary>
    public int? DeadlineHours { get; set; }

    /// <summary>Гарантированный срок поставки в часах (<c>deadlineMax</c>).</summary>
    public int? DeadlineMaxHours { get; set; }

    /// <summary>Название статуса позиции.</summary>
    public string? Status { get; set; }

    /// <summary>Код статуса позиции (<c>statusCode</c>), он же идентификатор в справочнике статусов.</summary>
    public int? StatusCode { get; set; }

    /// <summary>Дата выставления текущего статуса.</summary>
    public DateTime? StatusChangeDate { get; set; }

    /// <summary>Дата обновления позиции.</summary>
    public DateTime? DateUpdated { get; set; }

    /// <summary>Состояние запроса на удаление позиции.</summary>
    public CancelRequestState CancelRequest { get; set; }

    /// <summary>Признак удалённой позиции.</summary>
    public bool IsDeleted { get; set; }

    /// <summary>Идентификатор поставщика.</summary>
    public int? DistributorId { get; set; }

    /// <summary>Имя поставщика.</summary>
    public string? DistributorName { get; set; }

    /// <summary>Тип поставщика.</summary>
    public DistributorType DistributorType { get; set; }

    /// <summary>Номер заказа у поставщика (<c>distributorOrderId</c>).</summary>
    public string? DistributorOrderId { get; set; }

    /// <summary>Идентификатор маршрута склада (<c>routeId</c>).</summary>
    public int? RouteId { get; set; }

    /// <summary>Код поставки от поставщика (<c>supplierCode</c>).</summary>
    public string? SupplierCode { get; set; }

    /// <summary>
    /// Код позиции из результатов поиска (<c>itemKey</c>).
    /// Не является уникальным идентификатором и ключом не используется.
    /// </summary>
    public string? ItemKey { get; set; }

    /// <summary>Комментарий к позиции.</summary>
    public string? Comment { get; set; }

    /// <summary>Ответ на комментарий к позиции.</summary>
    public string? CommentAnswer { get; set; }

    /// <summary>Вес товара.</summary>
    public decimal? Weight { get; set; }

    /// <summary>История изменения статуса позиции.</summary>
    public ICollection<OrderItemStatusHistoryEntry> StatusHistory { get; set; } =
        new List<OrderItemStatusHistoryEntry>();

    /// <summary>Сумма позиции по цене продажи.</summary>
    public decimal Total => PriceOut * QuantityFinal;
}
