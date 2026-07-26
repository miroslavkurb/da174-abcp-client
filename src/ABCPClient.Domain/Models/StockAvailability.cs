namespace ABCPClient.Domain.Models;

/// <summary>
/// Наличие детали для сборки.
/// </summary>
/// <remarks>
/// Сборщику важно различать «лежит на складе» и «ещё в пути»: во втором случае
/// строку сейчас не собрать, и искать её на полке бессмысленно.
/// <see cref="Unknown"/> — честный ответ «данных нет», а не «нет в наличии»:
/// остатки приходят выгрузкой из 1С, и деталь может просто не попасть в файл.
/// </remarks>
public enum StockAvailability
{
    /// <summary>Данных о наличии нет.</summary>
    Unknown = 0,

    /// <summary>Есть на складе.</summary>
    InStock = 1,

    /// <summary>В пути на склад.</summary>
    Incoming = 2,
}
