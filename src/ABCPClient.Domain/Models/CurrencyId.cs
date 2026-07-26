namespace ABCPClient.Domain.Models;

/// <summary>
/// Идентификаторы валют платформы ABCP (поля <c>currencyInId</c> и <c>currencyOutId</c>).
/// </summary>
public enum CurrencyId
{
    /// <summary>Значение не передано.</summary>
    Unknown = 0,

    /// <summary>Российский рубль.</summary>
    RussianRuble = 1,

    /// <summary>Евро.</summary>
    Euro = 2,

    /// <summary>Доллар США.</summary>
    UsDollar = 3,

    /// <summary>Гривна Украины.</summary>
    UkrainianHryvnia = 4,

    /// <summary>Литовский лит.</summary>
    LithuanianLitas = 5,

    /// <summary>Белорусский рубль (до 1 июля 2016).</summary>
    BelarusianRubleOld = 6,

    /// <summary>Казахстанский тенге.</summary>
    KazakhstaniTenge = 7,

    /// <summary>Латвийский лат.</summary>
    LatvianLats = 8,

    /// <summary>Японская иена.</summary>
    JapaneseYen = 9,

    /// <summary>Китайский юань.</summary>
    ChineseYuan = 10,

    /// <summary>Армянский драм.</summary>
    ArmenianDram = 11,

    /// <summary>Киргизский сом.</summary>
    KyrgyzstaniSom = 12,

    /// <summary>Азербайджанский манат.</summary>
    AzerbaijaniManat = 13,

    /// <summary>Белорусский рубль.</summary>
    BelarusianRuble = 14,
}
