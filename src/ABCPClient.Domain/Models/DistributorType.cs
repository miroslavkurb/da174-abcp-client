namespace ABCPClient.Domain.Models;

/// <summary>
/// Тип поставщика позиции заказа (поле <c>distributorType</c> в API).
/// </summary>
public enum DistributorType
{
    /// <summary>Значение не передано.</summary>
    Unknown = 0,

    /// <summary>Прайсовый поставщик.</summary>
    PriceList = 20,

    /// <summary>Дилерский прайс-лист.</summary>
    DealerPriceList = 21,

    /// <summary>Online-поставщик.</summary>
    Online = 22,
}
