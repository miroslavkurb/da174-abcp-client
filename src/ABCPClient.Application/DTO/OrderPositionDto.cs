using System.Text.Json.Serialization;

namespace ABCPClient.Application.DTO;

/// <summary>
/// Позиция заказа в ответе API.
/// </summary>
public sealed class OrderPositionDto
{
    /// <summary>Уникальный идентификатор позиции в портале.</summary>
    [JsonPropertyName("id")]
    public long Id { get; set; }

    /// <summary>Производитель.</summary>
    [JsonPropertyName("brand")]
    public string Brand { get; set; } = string.Empty;

    /// <summary>Очищенное имя производителя.</summary>
    [JsonPropertyName("brandFix")]
    public string? BrandFix { get; set; }

    /// <summary>Номер детали.</summary>
    [JsonPropertyName("number")]
    public string Number { get; set; } = string.Empty;

    /// <summary>Очищенный номер детали.</summary>
    [JsonPropertyName("numberFix")]
    public string? NumberFix { get; set; }

    /// <summary>Описание детали.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Заказанное количество.</summary>
    [JsonPropertyName("quantity")]
    public decimal Quantity { get; set; }

    /// <summary>Итоговое количество.</summary>
    [JsonPropertyName("quantityFinal")]
    public decimal QuantityFinal { get; set; }

    /// <summary>Цена поставщика за единицу.</summary>
    [JsonPropertyName("priceIn")]
    public decimal? PriceIn { get; set; }

    /// <summary>Цена продажи за единицу.</summary>
    [JsonPropertyName("priceOut")]
    public decimal PriceOut { get; set; }

    /// <summary>Цена продажи в валюте сайта.</summary>
    [JsonPropertyName("priceInSiteCurrency")]
    public decimal? PriceInSiteCurrency { get; set; }

    /// <summary>Идентификатор валюты покупки.</summary>
    [JsonPropertyName("currencyInId")]
    public int? CurrencyInId { get; set; }

    /// <summary>Идентификатор валюты продажи.</summary>
    [JsonPropertyName("currencyOutId")]
    public int? CurrencyOutId { get; set; }

    /// <summary>Срок поставки в часах.</summary>
    [JsonPropertyName("deadline")]
    public int? Deadline { get; set; }

    /// <summary>Гарантированный срок поставки в часах.</summary>
    [JsonPropertyName("deadlineMax")]
    public int? DeadlineMax { get; set; }

    /// <summary>Название статуса позиции.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>Код статуса позиции.</summary>
    [JsonPropertyName("statusCode")]
    public int? StatusCode { get; set; }

    /// <summary>Дата выставления статуса.</summary>
    [JsonPropertyName("statusChangeDate")]
    public DateTime? StatusChangeDate { get; set; }

    /// <summary>Дата обновления позиции.</summary>
    [JsonPropertyName("dateUpdated")]
    public DateTime? DateUpdated { get; set; }

    /// <summary>Флаг запроса на удаление позиции: 0, 1 или 2.</summary>
    [JsonPropertyName("isCanceled")]
    public int? IsCanceled { get; set; }

    /// <summary>Признак удалённой позиции.</summary>
    [JsonPropertyName("isDelete")]
    public bool IsDeleted { get; set; }

    /// <summary>Идентификатор поставщика.</summary>
    [JsonPropertyName("distributorId")]
    public int? DistributorId { get; set; }

    /// <summary>Имя поставщика.</summary>
    [JsonPropertyName("distributorName")]
    public string? DistributorName { get; set; }

    /// <summary>Тип поставщика: 20 прайсовый, 21 дилерский прайс, 22 online.</summary>
    [JsonPropertyName("distributorType")]
    public int? DistributorType { get; set; }

    /// <summary>Номер заказа у поставщика.</summary>
    [JsonPropertyName("distributorOrderId")]
    public string? DistributorOrderId { get; set; }

    /// <summary>Идентификатор маршрута склада.</summary>
    [JsonPropertyName("routeId")]
    public int? RouteId { get; set; }

    /// <summary>Код поставки от поставщика.</summary>
    [JsonPropertyName("supplierCode")]
    public string? SupplierCode { get; set; }

    /// <summary>Код позиции из результатов поиска. Не уникален.</summary>
    [JsonPropertyName("itemKey")]
    public string? ItemKey { get; set; }

    /// <summary>Комментарий к позиции.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; set; }

    /// <summary>Ответ на комментарий к позиции.</summary>
    [JsonPropertyName("commentAnswer")]
    public string? CommentAnswer { get; set; }

    /// <summary>Вес товара.</summary>
    [JsonPropertyName("weight")]
    public decimal? Weight { get; set; }
}
