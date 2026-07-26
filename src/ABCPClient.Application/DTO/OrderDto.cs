using System.Text.Json.Serialization;

namespace ABCPClient.Application.DTO;

/// <summary>
/// Заказ в ответе операций <c>cp/orders</c> и <c>cp/order</c>.
/// </summary>
public sealed class OrderDto
{
    /// <summary>Онлайн-номер заказа в портале.</summary>
    [JsonPropertyName("number")]
    public string Number { get; set; } = string.Empty;

    /// <summary>Номер заказа в учётной системе (1С).</summary>
    [JsonPropertyName("internalNumber")]
    public string? InternalNumber { get; set; }

    /// <summary>Номер заказа во внутренней системе учёта клиента.</summary>
    [JsonPropertyName("clientOrderNumber")]
    public string? ClientOrderNumber { get; set; }

    /// <summary>Идентификатор клиента на сайте.</summary>
    [JsonPropertyName("userId")]
    public int? UserId { get; set; }

    /// <summary>Имя покупателя.</summary>
    [JsonPropertyName("userName")]
    public string? UserName { get; set; }

    /// <summary>Название организации покупателя.</summary>
    [JsonPropertyName("userFullName")]
    public string? UserFullName { get; set; }

    /// <summary>Электронная почта покупателя.</summary>
    [JsonPropertyName("userEmail")]
    public string? UserEmail { get; set; }

    /// <summary>Контактный телефон покупателя.</summary>
    [JsonPropertyName("userMobile")]
    public string? UserMobile { get; set; }

    /// <summary>Внутренний код пользователя.</summary>
    [JsonPropertyName("userCode")]
    public string? UserCode { get; set; }

    /// <summary>Идентификатор профиля клиента.</summary>
    [JsonPropertyName("profileId")]
    public int? ProfileId { get; set; }

    /// <summary>Идентификатор менеджера, создавшего заказ.</summary>
    [JsonPropertyName("managerId")]
    public int? ManagerId { get; set; }

    /// <summary>Количество позиций.</summary>
    [JsonPropertyName("positionsQuantity")]
    public int PositionsQuantity { get; set; }

    /// <summary>Сумма заказа.</summary>
    [JsonPropertyName("sum")]
    public decimal Sum { get; set; }

    /// <summary>Долг по оплате заказа.</summary>
    [JsonPropertyName("debt")]
    public decimal? Debt { get; set; }

    /// <summary>Признак успешной онлайн-оплаты (<c>1</c> или <c>true</c>).</summary>
    [JsonPropertyName("paid")]
    public bool Paid { get; set; }

    /// <summary>Дата размещения заказа.</summary>
    [JsonPropertyName("date")]
    public DateTime? Date { get; set; }

    /// <summary>Дата последнего обновления заказа.</summary>
    [JsonPropertyName("dateUpdated")]
    public DateTime? DateUpdated { get; set; }

    /// <summary>Дата отгрузки.</summary>
    [JsonPropertyName("shipmentDate")]
    public DateTime? ShipmentDate { get; set; }

    /// <summary>Комментарий к заказу.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; set; }

    /// <summary>Адрес доставки.</summary>
    [JsonPropertyName("deliveryAddress")]
    public string? DeliveryAddress { get; set; }

    /// <summary>Офис самовывоза.</summary>
    [JsonPropertyName("deliveryOffice")]
    public string? DeliveryOffice { get; set; }

    /// <summary>Тип доставки.</summary>
    [JsonPropertyName("deliveryType")]
    public string? DeliveryType { get; set; }

    /// <summary>Стоимость доставки.</summary>
    [JsonPropertyName("deliveryCost")]
    public decimal? DeliveryCost { get; set; }

    /// <summary>Тип оплаты.</summary>
    [JsonPropertyName("paymentType")]
    public string? PaymentType { get; set; }

    /// <summary>Признак удалённого заказа.</summary>
    [JsonPropertyName("isDelete")]
    public bool IsDeleted { get; set; }

    /// <summary>Позиции заказа. Отсутствуют при <c>format=short</c>.</summary>
    [JsonPropertyName("positions")]
    public List<OrderPositionDto> Positions { get; set; } = [];
}
